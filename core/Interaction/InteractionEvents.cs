namespace JustNeko.Core.Interaction;

/// <summary>谁发起了这次交互。用户输入、习惯到期、看屏幕、Agent 都走同一个入口。</summary>
public enum InteractionSource
{
    User,
    Habit,
    Perception,
    Agent,
}

public enum InteractionOutcome
{
    Completed,
    Cancelled,
    TimedOut,
    Failed,
}

/// <param name="Priority">正在进行的交互优先级更高时，新的请求会被拒绝；否则打断它。用户输入最高。</param>
public sealed record InteractionRequest(InteractionSource Source, string Text, int Priority)
{
    public const int UserPriority = 100;

    public static InteractionRequest FromUser(string text) => new(InteractionSource.User, text, UserPriority);
}

public abstract record InteractionEvent(long InteractionId);

public sealed record InteractionStarted(long InteractionId, InteractionRequest Request) : InteractionEvent(InteractionId);

/// <summary>回复正文有新内容。Text 是到目前为止的完整正文，指令标签已经去掉。</summary>
public sealed record ReplyUpdated(long InteractionId, string Text) : InteractionEvent(InteractionId);

/// <summary>模型在回复里给出的指令，例如 [emotion:happy]。值是否有效由接收方判断。</summary>
public sealed record DirectiveReceived(long InteractionId, string Name, string Value) : InteractionEvent(InteractionId);

/// <summary>交互结束。每次交互恰好一条，之后这次交互不会再有任何事件。</summary>
public sealed record InteractionEnded(long InteractionId, InteractionOutcome Outcome, string Text, string? Error) : InteractionEvent(InteractionId);
