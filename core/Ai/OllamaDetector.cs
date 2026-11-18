using System.Text.Json;

namespace JustNeko.Core.Ai;

public static class OllamaDetector
{
    /// <summary>快速看一眼本机有没有在跑的 Ollama，返回已安装的模型；没有在跑返回 null。只等很短的时间，用在首次启动引导里。</summary>
    public static async Task<IReadOnlyList<string>?> FindModelsAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            using var response = await http.GetAsync("http://127.0.0.1:11434/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array
                ? models.EnumerateArray().Select(model => model.GetProperty("name").GetString() ?? "").Where(name => name.Length > 0).ToArray()
                : [];
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                      && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
