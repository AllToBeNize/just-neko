using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JustNeko.Core.Platform;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform(string dataDirectory) : IPlatformServices
{
    public string Name => "windows";

    public TimeSpan DoubleClickTime => TimeSpan.FromMilliseconds(Math.Max(100, GetDoubleClickTime()));

    public ISecretStore Secrets { get; } = new DpapiSecretStore(Path.Combine(dataDirectory, "secrets.json"));

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
}

/// <summary>用当前 Windows 用户的 DPAPI 加密后存成 JSON，换用户或换电脑都解不开。</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string path) : ISecretStore
{
    private static readonly byte[] Entropy = "JustNeko.Secrets.v1"u8.ToArray();
    private Dictionary<string, string>? _protected;

    public bool IsPersistent => true;

    public string? Get(string name)
    {
        if (!Read().TryGetValue(name, out var value))
            return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public string? Set(string name, string? value)
    {
        var values = Read();
        if (value is null)
            values.Remove(name);
        else
            values[name] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(values), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"无法保存 Key：{error.Message}";
        }
    }

    private Dictionary<string, string> Read()
    {
        if (_protected is not null)
            return _protected;
        try
        {
            _protected = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8)) ?? []
                : [];
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            _protected = [];
        }
        return _protected;
    }
}
