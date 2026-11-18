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

    private static string WithStatus(string text, string status) => text.Length == 0 ? status : $"{text}\n{status}";
}
