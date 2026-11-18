using System.Text.Json;
using System.Text.Json.Serialization;
using JustNeko.Core.Ai;
using JustNeko.Core.Platform;

namespace JustNeko.Core.Settings;

/// <summary>读写 settings.json（UTF-8，先写临时文件再替换），API Key 交给平台的凭据存储。</summary>
public sealed class SettingsService(string directory, ISecretStore secrets)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path = Path.Combine(directory, "settings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>上次读取失败的原因；为 null 表示正常。读坏了就用默认值启动，不覆盖原文件，直到用户保存。</summary>
    public string? LoadError { get; private set; }

    public bool KeysPersist => secrets.IsPersistent;

    public void Load()
    {
        LoadError = null;
        if (!File.Exists(_path))
            return;
        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path, System.Text.Encoding.UTF8), Json) ?? new AppSettings();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadError = $"读取设置失败，已使用默认设置：{error.Message}";
        }
    }

    /// <summary>更新并保存设置。保存失败时内存里的设置照样生效，返回失败原因。</summary>
    public string? Update(AppSettings next)
    {
        Current = next with { Version = AppSettings.CurrentVersion };
        try
        {
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Current, Json), new System.Text.UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"无法保存设置：{error.Message}";
        }
    }

    public string GetApiKey(ProviderKind kind) => secrets.Get(SecretName(kind)) ?? "";

    public string? SetApiKey(ProviderKind kind, string key) => secrets.Set(SecretName(kind), key.Length > 0 ? key : null);

    private static string SecretName(ProviderKind kind) => $"api-key/{kind}";
}
