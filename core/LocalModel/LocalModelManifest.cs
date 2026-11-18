using System.Runtime.InteropServices;

namespace JustNeko.Core.LocalModel;

/// <summary>一个要下载的文件。按顺序尝试各个来源，下载完按 SHA256 校验。</summary>
public sealed record DownloadItem(string Label, string FileName, long Size, string Sha256, IReadOnlyList<Uri> Sources);

/// <summary>内置本地模型用到的运行时和模型文件，版本固定，升级时改这里。</summary>
public sealed record LocalModelManifest(string Name, string RuntimeVersion, DownloadItem Runtime, DownloadItem Model)
{
    public long TotalSize => Runtime.Size + Model.Size;

    /// <summary>当前平台能用的清单；还没有适配的平台返回 null。</summary>
    public static LocalModelManifest? ForCurrentPlatform() =>
        OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64 ? WindowsX64 : null;

    // llama.cpp 的 Vulkan 版：几十 MB，AMD / NVIDIA / Intel 显卡都能加速，没有可用显卡时用 CPU。MIT 协议。
    // 模型：Qwen3 1.7B 的 Q4_K_M 量化（Apache-2.0），HuggingFace 连不上时换国内镜像。
    private static readonly LocalModelManifest WindowsX64 = new(
        "Qwen3 1.7B",
        "b12503",
        new DownloadItem(
            "运行时",
            "llama-b12503-bin-win-vulkan-x64.zip",
            38_603_975,
            "6c83027548f989a4d5c9502b31b580dc5e1254998bd49c1d78e85f316900d118",
            [new Uri("https://github.com/ggml-org/llama.cpp/releases/download/b12503/llama-b12503-bin-win-vulkan-x64.zip")]),
        new DownloadItem(
            "模型",
            "Qwen3-1.7B-Q4_K_M.gguf",
            1_107_409_472,
            "b139949c5bd74937ad8ed8c8cf3d9ffb1e99c866c823204dc42c0d91fa181897",
            [
                new Uri("https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf"),
                new Uri("https://hf-mirror.com/unsloth/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf"),
            ]));
}
