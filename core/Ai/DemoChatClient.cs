using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace JustNeko.Core.Ai;

/// <summary>不联网的演示模型，用来验证聊天、表情和停止流程。</summary>
public sealed class DemoChatClient : IChatClient
{
    private static readonly TimeSpan TokenDelay = TimeSpan.FromMilliseconds(170);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prompt = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? "";
        var preview = prompt.Length > 36 ? prompt[..36] + "…" : prompt;
        string[] parts = ["[emo", "tion:curious]", "喵，", "我收到了「", preview, "」。", "这是一条本地演示回复。", "换成你的模型后，", "文字和表情会用同一条流程。"];
        foreach (var part in parts)
        {
            await Task.Delay(TokenDelay, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, part);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
