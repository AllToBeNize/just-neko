using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using JustNeko.Core.Platform;

namespace JustNeko.Core.LocalModel;

public enum LocalModelStatus
{
    /// <summary>当前平台还没有内置模型。</summary>
    Unsupported,
    NotInstalled,
    Downloading,
    Installed,
    Starting,
    Running,
    Failed,
}

/// <param name="Progress">下载进度 0–1，其他状态为 0。</param>
public sealed record LocalModelState(LocalModelStatus Status, double Progress, string Message);

/// <summary>
/// 内置本地模型的总入口：下载安装、启动和关闭 llama-server，并把状态通知给界面（引导页、设置、托盘）。
/// 状态事件都回到创建它的线程（Godot 主线程）上触发；下载和等服务就绪都在后台线程跑，不卡界面。
/// </summary>
public sealed class LocalModelManager : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);
    private readonly LocalModelInstaller? _installer;
    private readonly IPlatformServices _platform;
    private readonly string _logPath;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private CancellationTokenSource? _installing;
    private Task<Uri>? _starting;
    private Process? _server;

    public LocalModelManager(IPlatformServices platform, string logDirectory)
    {
        _platform = platform;
        _logPath = Path.Combine(logDirectory, "llama-server.log");
        var manifest = LocalModelManifest.ForCurrentPlatform();
        _installer = manifest is null ? null : new LocalModelInstaller(manifest, Path.Combine(platform.LocalDataDirectory, "local-model"));
        State = _installer is null ? new(LocalModelStatus.Unsupported, 0, "这个系统暂时还没有内置本地模型。")
            : _installer.IsInstalled ? new(LocalModelStatus.Installed, 0, "已下载，聊天时自动启动。")
            : new(LocalModelStatus.NotInstalled, 0, "还没有下载。");
    }

    public LocalModelState State { get; private set; }

    public event Action<LocalModelState>? StateChanged;

    public LocalModelManifest? Manifest => _installer?.Manifest;

    public string Directory => Path.Combine(_platform.LocalDataDirectory, "local-model");

    public bool IsInstalled => _installer?.IsInstalled == true;

    public Uri? BaseUrl { get; private set; }

    /// <summary>开始下载安装；已经在下载或已经装好时直接返回。完成或失败都体现在 <see cref="State"/> 上。</summary>
    public async Task InstallAsync()
    {
        if (_installer is null || _installing is not null || _installer.IsInstalled)
            return;
        using var installing = new CancellationTokenSource();
        _installing = installing;
        var installer = _installer;
        var total = installer.Manifest.TotalSize;
        var progress = new Progress<InstallProgress>(step =>
        {
            if (_installing != installing)
                return;
            var done = step.Label == installer.Manifest.Model.Label ? installer.Manifest.Runtime.Size + step.Done : step.Done;
            var source = step.Source.Length > 0 ? $"（{step.Source}）" : "";
            Publish(new(LocalModelStatus.Downloading, (double)done / total,
                $"正在下载{step.Label}{source}：{Megabytes(done)} / {Megabytes(total)}"));
        });
        Publish(new(LocalModelStatus.Downloading, 0, "正在准备下载…"));
        try
        {
            await Task.Run(() => installer.InstallAsync(progress, installing.Token), installing.Token);
            Publish(new(LocalModelStatus.Installed, 1, "下载完成，聊天时自动启动。"));
        }
        catch (OperationCanceledException) when (installing.IsCancellationRequested)
        {
            Publish(new(LocalModelStatus.NotInstalled, 0, "已取消。已下载的部分会保留，下次接着下。"));
        }
        catch (Exception error)
        {
            Publish(new(LocalModelStatus.Failed, 0, error.Message));
        }
        finally
        {
            _installing = null;
        }
    }

    public void CancelInstall() => _installing?.Cancel();

    /// <summary>确保 llama-server 在跑，返回它的地址。并发调用共用同一次启动。</summary>
    public Task<Uri> EnsureServerAsync(CancellationToken cancellationToken)
    {
        if (_server is { HasExited: false } && BaseUrl is not null)
            return Task.FromResult(BaseUrl);
        if (_installer is null)
            throw new InvalidOperationException(State.Message);
        if (!_installer.IsInstalled)
            throw new InvalidOperationException("内置本地模型还没有下载。请在托盘菜单的「设置中心 → 本地模型」里下载。");
        if (_starting is { IsCompleted: true } && _server is not { HasExited: false })
            _starting = null;  // 上次启动的进程已经退出（崩溃或被关掉），重新启动。
        _starting ??= StartAsync(_installer);
        return _starting.WaitAsync(cancellationToken);
    }

    public void StopServer()
    {
        var server = _server;
        _server = null;
        _starting = null;
        BaseUrl = null;
        if (server is { HasExited: false })
            server.Kill(entireProcessTree: true);
        server?.Dispose();
        if (State.Status is LocalModelStatus.Running or LocalModelStatus.Starting)
            Publish(new(LocalModelStatus.Installed, 0, "已下载，聊天时自动启动。"));
    }

    public void Dispose()
    {
        _installing?.Cancel();
        StopServer();
    }

    private async Task<Uri> StartAsync(LocalModelInstaller installer)
    {
        Publish(new(LocalModelStatus.Starting, 0, "正在启动内置模型…"));
        try
        {
            var port = FreePort();
            var start = new ProcessStartInfo(installer.ServerPath)
            {
                WorkingDirectory = installer.RuntimeDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // --reasoning off：关掉 Qwen3 的思考过程，桌宠要的是马上开口。-ngl 99：能放进显卡的层都放进去。
            foreach (var argument in new[]
            {
                "-m", installer.ModelPath, "--host", "127.0.0.1", "--port", port.ToString(),
                "-ngl", "99", "-c", "8192", "--reasoning", "off", "--no-webui", "-a", Ai.ModelEndpoint.BuiltInModel,
            })
                start.ArgumentList.Add(argument);

            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            // 允许别的程序同时读日志（比如用户从设置里打开日志文件夹查看）。
            var log = new StreamWriter(new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
            await log.WriteLineAsync($"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动 llama-server，端口 {port}");
            var server = Process.Start(start) ?? throw new InvalidOperationException("无法启动内置模型。");
            _platform.TieToProcessLifetime(server);
            server.OutputDataReceived += (_, line) => WriteLog(log, line.Data);
            server.ErrorDataReceived += (_, line) => WriteLog(log, line.Data);
            server.Exited += (_, _) => log.Dispose();
            server.EnableRaisingEvents = true;
            server.BeginOutputReadLine();
            server.BeginErrorReadLine();
            _server = server;

            var url = new Uri($"http://127.0.0.1:{port}/v1");
            await Task.Run(() => WaitUntilReadyAsync(server, port)).ConfigureAwait(true);
            BaseUrl = url;
            Publish(new(LocalModelStatus.Running, 0, $"运行中（{installer.Manifest.Name}，端口 {port}）。"));
            return url;
        }
        catch (Exception error)
        {
            _starting = null;
            if (_server is { HasExited: false } failed)
                failed.Kill(entireProcessTree: true);
            _server = null;
            var message = $"内置模型启动失败：{error.Message} 详细信息在 {_logPath}。";
            Publish(new(LocalModelStatus.Failed, 0, message));
            throw new InvalidOperationException(message, error);
        }
    }

    private static async Task WaitUntilReadyAsync(Process server, int port)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < StartTimeout)
        {
            if (server.HasExited)
                throw new InvalidOperationException($"llama-server 意外退出（代码 {server.ExitCode}）。");
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/health").ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                // 模型还在加载，接着等。
            }
            await Task.Delay(300).ConfigureAwait(false);
        }
        throw new TimeoutException($"等了 {StartTimeout.TotalMinutes:0} 分钟还没准备好。");
    }

    private static void WriteLog(StreamWriter log, string? line)
    {
        if (line is null)
            return;
        lock (log)
        {
            try
            {
                log.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void Publish(LocalModelState state)
    {
        if (_context is null || SynchronizationContext.Current == _context)
            Apply(state);
        else
            _context.Post(_ => Apply(state), null);
    }

    private void Apply(LocalModelState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static string Megabytes(long bytes) => $"{bytes / 1024d / 1024:0} MB";
}
