namespace JustNeko.Core.Platform;

/// <summary>
/// 和操作系统打交道的能力都从这里取。上层只认接口；新平台（macOS、Linux）各自实现一份，
/// 在 <see cref="PlatformServices.Create"/> 里按系统选。
/// </summary>
public interface IPlatformServices
{
    string Name { get; }

    /// <summary>系统设置的双击间隔，用来区分单击和双击。</summary>
    TimeSpan DoubleClickTime { get; }

    ISecretStore Secrets { get; }
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

/// <summary>还没有专门实现的平台：功能都能用，Key 只留在内存里。</summary>
public sealed class GenericPlatform : IPlatformServices
{
    public string Name => "generic";

    public TimeSpan DoubleClickTime => TimeSpan.FromMilliseconds(500);

    public ISecretStore Secrets { get; } = new MemorySecretStore();
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
