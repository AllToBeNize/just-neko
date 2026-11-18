using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace JustNeko;

internal sealed record ChatMessage(string Role, string Content);

internal static class ChatClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static Uri ResolveEndpoint(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https")
            || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
        {
            throw new ArgumentException("请输入 HTTP 或 HTTPS 服务地址，例如 http://localhost:11434/v1。");
        }

        var path = url.AbsolutePath.TrimEnd('/');
        return path.EndsWith("/chat/completions", StringComparison.Ordinal)
            ? new UriBuilder(url) { Path = path }.Uri
            : new UriBuilder(url) { Path = path + "/chat/completions" }.Uri;
    }

    public static async IAsyncEnumerable<string> StreamAsync(
        Uri endpoint, string model, string apiKey, IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                model,
                stream = true,
                messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            }),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (apiKey.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"模型服务返回 HTTP {(int)response.StatusCode}。请检查地址、模型和 Key。");

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        var eventData = new List<string>();
        var receivedText = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is { Length: > 0 })
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                    eventData.Add(line[5..].TrimStart(' '));
                continue;
            }

            if (eventData.Count > 0)
            {
                var data = string.Join('\n', eventData);
                eventData.Clear();
                if (data == "[DONE]")
                    break;

                using var frame = JsonDocument.Parse(data);
                var root = frame.RootElement;
                if (root.TryGetProperty("error", out _))
                    throw new HttpRequestException("模型服务在流式回复中报告了错误。");

                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array
                    && choices.GetArrayLength() > 0 && choices[0].TryGetProperty("delta", out var delta)
                    && delta.ValueKind == JsonValueKind.Object && delta.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } piece)
                {
                    receivedText = true;
                    yield return piece;
                }
            }

            if (line is null)
                break;
        }

        if (!receivedText)
            throw new InvalidDataException("模型没有返回可显示的流式文本。请检查该接口是否支持流式 Chat Completions。");
    }

    public static async IAsyncEnumerable<string> DemoAsync(
        string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var preview = prompt.Length > 36 ? prompt[..36] + "…" : prompt;
        var parts = new[] { "[emo", "tion:curious]", "喵，", "我收到了「", preview, "」。", "这是一条本地演示回复。", "换成你的模型后，", "文字和表情会用同一条流程。" };
        foreach (var part in parts)
        {
            await Task.Delay(170, cancellationToken);
            yield return part;
        }
    }
}

internal sealed class EmotionStream(IEnumerable<string> emotionNames)
{
    private const string Prefix = "[emotion:";
    private const int TagLimit = 128;
    private readonly HashSet<string> _allowed = new(emotionNames, StringComparer.OrdinalIgnoreCase);
    private string _pending = "";
    private bool _foundBody;

    public string Emotion { get; private set; } = "neutral";

    public string Read(string piece)
    {
        if (_foundBody)
            return piece;

        _pending += piece;
        var prefixText = _pending.TrimStart();
        if (_pending.Length <= TagLimit && prefixText.Length < Prefix.Length && Prefix.StartsWith(prefixText, StringComparison.OrdinalIgnoreCase))
            return "";

        if (prefixText.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var end = prefixText.IndexOf(']');
            if (end < 0 && prefixText.Length <= TagLimit)
                return "";
            if (end >= 0)
            {
                var emotion = prefixText[Prefix.Length..end].Trim().ToLowerInvariant();
                if (_allowed.Contains(emotion))
                    Emotion = emotion;
                _pending = prefixText[(end + 1)..].TrimStart();
            }
        }

        _foundBody = true;
        return Complete();
    }

    public string Complete()
    {
        var text = _pending;
        _pending = "";
        return text;
    }
}
