using Microsoft.Extensions.AI;

namespace JustNeko.Core.Interaction;

/// <summary>近期对话，按轮保留最近的若干条，发给模型做上下文。</summary>
public sealed class ConversationHistory(int messageLimit = 16)
{
    private readonly List<ChatMessage> _messages = [];

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public void AddTurn(string user, string assistant)
    {
        _messages.Add(new ChatMessage(ChatRole.User, user));
        _messages.Add(new ChatMessage(ChatRole.Assistant, assistant));
        while (_messages.Count > messageLimit)
            _messages.RemoveRange(0, 2);
    }

    public void Clear() => _messages.Clear();
}
