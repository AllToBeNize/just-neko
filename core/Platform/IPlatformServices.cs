using System.Diagnostics;

namespace JustNeko.Core.Platform;

/// <summary>
/// 和操作系统打交道的能力都从这里取。上层只认接口；新平台（macOS、Linux）各自实现一份，
/// 在 <see cref="PlatformServices.Create"/> 里按系统选。
/// </summary>
public interface IPlatformServices
{
    string Name { get; }

    /// <summary>放大文件（内置模型、运行时）的目录。Windows 上是 %LOCALAPPDATA%\JustNeko，不随漫游配置同步。</summary>
    string LocalDataDirectory { get; }

    ISecretStore Secrets { get; }

    /// <summary>不在任务栏显示这个窗口的按钮（角色、聊天、气泡这类常驻小窗）。</summary>
    void HideFromTaskbar(long windowHandle);

    /// <summary>
    /// 直接让系统隐藏 / 显示一个窗口（Godot 不允许隐藏主窗口）。不支持时返回 false，调用方自己想办法（比如移到屏幕外）。
    /// </summary>
    bool SetWindowShown(long windowHandle, bool shown);

    /// <summary>前台是不是有全屏程序（游戏、演示）。</summary>
    bool IsFullscreenAppActive();

    /// <summary>让子进程跟着桌宠一起退出，桌宠崩溃时也不留下孤儿进程。</summary>
    void TieToProcessLifetime(Process process);
}

/// <summary>凭据存储。Windows 用 DPAPI；以后 macOS 用钥匙串、Linux 用 libsecret。</summary>
public interface ISecretStore
{
    /// <summary>为 false 时只在本次运行里记住，重启后需要重新填写。</summary>
    bool IsPersistent { get; }

    string? Get(string name);

    /// <summary>value 为 null 时删除。返回失败原因，成功时为 null。</summary>
    string? Set(string name, string? value);
}

public static class PlatformServices
{
    public static IPlatformServices Create(string dataDirectory) =>
        OperatingSystem.IsWindows() ? new WindowsPlatform(dataDirectory) : new GenericPlatform();
}

/// <summary>还没有专门实现的平台：功能都能用，Key 只留在内存里，任务栏和全屏相关的能力先不生效。</summary>
public sealed class GenericPlatform : IPlatformServices
{
    public string Name => "generic";

    public string LocalDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JustNeko");

    public ISecretStore Secrets { get; } = new MemorySecretStore();

    public void HideFromTaskbar(long windowHandle)
    {
    }

    public bool SetWindowShown(long windowHandle, bool shown) => false;

    public bool IsFullscreenAppActive() => false;

    public void TieToProcessLifetime(Process process)
    {
    }
}

public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public bool IsPersistent => false;

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public string? Set(string name, string? value)
    {
        if (value is null)
            _values.Remove(name);
        else
            _values[name] = value;
        return null;
    }
}
