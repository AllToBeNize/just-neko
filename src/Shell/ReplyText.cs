using JustNeko.Core.Interaction;

namespace JustNeko.Shell;

internal static class ReplyText
{
    public const string Waiting = "…";

    /// <summary>交互结束后聊天记录和气泡里显示的文字。</summary>
    public static string ForEnded(InteractionEnded ended) => ended.Outcome switch
    {
        InteractionOutcome.Completed => ended.Text,
        InteractionOutcome.Failed => ended.Error ?? "回复失败。",
        _ => WithStatus(ended.Text, ended.Outcome == InteractionOutcome.Cancelled ? "已停止" : ended.Error ?? "连接模型超时。"),
    };

    /// <summary>保存的聊天记录里的一条，显示成和当时一样的文字。</summary>
    public static string ForEntry(ChatEntry entry) => entry.Outcome is { } outcome
        ? ForEnded(new InteractionEnded(0, outcome, entry.Text, entry.Error))
        : entry.Text;

    private static string WithStatus(string text, string status) => text.Length == 0 ? status : $"{text}\n{status}";
}
