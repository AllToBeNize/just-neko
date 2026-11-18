using System.IO.Compression;
using System.Security.Cryptography;

namespace JustNeko.Core.LocalModel;

public sealed record InstallProgress(string Label, long Done, long Total, string Source);

/// <summary>
/// 下载并安装内置模型。中途取消或断网，已下载的部分留着，下次接着下；下完逐个校验 SHA256，
/// 不对就删掉重下。下载前检查磁盘空间，不够就直接报出还差多少。
/// </summary>
public sealed class LocalModelInstaller(LocalModelManifest manifest, string root)
{
    private const int BufferSize = 1 << 16;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public LocalModelManifest Manifest => manifest;

    public string RuntimeDirectory => Path.Combine(root, "runtime", manifest.RuntimeVersion);

    public string ServerPath => Path.Combine(RuntimeDirectory, OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");

    public string ModelPath => Path.Combine(root, "models", manifest.Model.FileName);

    private string RuntimeMarker => Path.Combine(RuntimeDirectory, ".installed");

    private string ModelMarker => ModelPath + ".verified";

    public bool IsInstalled =>
        File.Exists(ServerPath) && ReadMarker(RuntimeMarker) == manifest.Runtime.Sha256
        && File.Exists(ModelPath) && ReadMarker(ModelMarker) == manifest.Model.Sha256
        && new FileInfo(ModelPath).Length == manifest.Model.Size;

    public async Task InstallAsync(IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        EnsureDiskSpace();

        if (!(File.Exists(ServerPath) && ReadMarker(RuntimeMarker) == manifest.Runtime.Sha256))
        {
            var archive = await DownloadAsync(manifest.Runtime, Path.Combine(root, "downloads"), progress, cancellationToken).ConfigureAwait(false);
            progress.Report(new InstallProgress("正在解压运行时", manifest.Runtime.Size, manifest.Runtime.Size, ""));
            var staging = RuntimeDirectory + ".tmp";
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(archive, staging);
            if (Directory.Exists(RuntimeDirectory))
                Directory.Delete(RuntimeDirectory, true);
            Directory.Move(staging, RuntimeDirectory);
            File.WriteAllText(RuntimeMarker, manifest.Runtime.Sha256);
            File.Delete(archive);
        }

        if (!(File.Exists(ModelPath) && ReadMarker(ModelMarker) == manifest.Model.Sha256))
        {
            var model = await DownloadAsync(manifest.Model, Path.Combine(root, "models"), progress, cancellationToken).ConfigureAwait(false);
            if (model != ModelPath)
                File.Move(model, ModelPath, overwrite: true);
            File.WriteAllText(ModelMarker, manifest.Model.Sha256);
        }
    }

    /// <summary>还需要下载的字节数（已经下了一部分的按剩下的算）。</summary>
    public long RemainingBytes()
    {
        long remaining = 0;
        if (!(File.Exists(ServerPath) && ReadMarker(RuntimeMarker) == manifest.Runtime.Sha256))
            remaining += manifest.Runtime.Size - PartLength(Path.Combine(root, "downloads"), manifest.Runtime);
        if (!(File.Exists(ModelPath) && ReadMarker(ModelMarker) == manifest.Model.Sha256))
            remaining += manifest.Model.Size - PartLength(Path.Combine(root, "models"), manifest.Model);
        return remaining;
    }

    private void EnsureDiskSpace()
    {
        // 运行时压缩包解压时要同时放下压缩包和解压后的文件，多留一份运行时的空间。
        var needed = RemainingBytes() + manifest.Runtime.Size * 3;
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
        if (drive.AvailableFreeSpace < needed)
        {
            throw new IOException(
                $"磁盘空间不够：{drive.Name} 还剩 {Gigabytes(drive.AvailableFreeSpace)}，下载内置模型需要 {Gigabytes(needed)}，" +
                $"还差 {Gigabytes(needed - drive.AvailableFreeSpace)}。请清理后再试。");
        }
    }

    private static async Task<string> DownloadAsync(DownloadItem item, string directory, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, item.FileName);
        var part = target + ".part";
        Exception? lastError = null;
        foreach (var source in item.Sources)
        {
            try
            {
                await DownloadFromAsync(item, source, part, progress, cancellationToken).ConfigureAwait(false);
                File.Move(part, target, overwrite: true);
                return target;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or TimeoutException && !cancellationToken.IsCancellationRequested)
            {
                lastError = error;
            }
        }
        throw new IOException($"{item.Label}下载失败：{lastError?.Message}", lastError);
    }

    private static async Task DownloadFromAsync(DownloadItem item, Uri source, string part, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var done = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (done > item.Size)
        {
            File.Delete(part);
            done = 0;
        }
        if (done > 0)
            await HashExistingAsync(part, hash, cancellationToken).ConfigureAwait(false);

        var host = source.Host;
        if (done < item.Size)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            if (done > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(done, null);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (done > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            {
                // 服务器不支持续传，从头下。
                done = 0;
                hash.GetHashAndReset();
            }

            await using var input = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var output = new FileStream(part, done > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];
            var lastReport = 0L;
            try
            {
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    var read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    if (done - lastReport >= 1 << 20 || done == item.Size)
                    {
                        lastReport = done;
                        progress.Report(new InstallProgress(item.Label, done, item.Size, host));
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{host} 超过 {StallTimeout.TotalSeconds:0} 秒没有数据。");
            }
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (done != item.Size || actual != item.Sha256)
        {
            File.Delete(part);
            throw new IOException($"{item.Label}文件校验不通过（来自 {host}），已删除，请重试。");
        }
    }

    private static async Task HashExistingAsync(string path, IncrementalHash hash, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            hash.AppendData(buffer, 0, read);
    }

    private static long PartLength(string directory, DownloadItem item)
    {
        var part = Path.Combine(directory, item.FileName + ".part");
        return File.Exists(part) ? Math.Min(new FileInfo(part).Length, item.Size) : 0;
    }

    private static string? ReadMarker(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : null;

    private static string Gigabytes(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.0} GB";
}
