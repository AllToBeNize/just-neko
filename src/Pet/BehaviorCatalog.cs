namespace JustNeko.Pet;

/// <summary>一个可以绑定动作的行为槽，会出现在「角色属性」的动画列表里。</summary>
internal sealed record PetBehaviorSlot(string Id, string Label, bool Loop);

/// <summary>
/// 所有行为槽。内置的 7 个之外，自主活动（比如沿任务栏走）可以登记自己需要的槽，
/// 玩家就能在角色属性里给它选动作。登记要在角色属性窗口创建之前完成。
/// </summary>
internal sealed class BehaviorCatalog
{
    public const string Idle = "Idle";
    public const string ChatOpened = "ChatOpened";
    public const string ChatClosed = "ChatClosed";
    public const string Dragging = "Dragging";
    public const string WaitingForReply = "WaitingForReply";
    public const string Replying = "Replying";
    public const string ReplyFinished = "ReplyFinished";
    public const string Petted = "Petted";

    private readonly List<PetBehaviorSlot> _slots =
    [
        new(Idle, "待机", true),
        new(ChatOpened, "打开聊天", false),
        new(ChatClosed, "收起聊天", false),
        new(Dragging, "拖动角色", true),
        new(WaitingForReply, "等待回复", true),
        new(Replying, "回复中", true),
        new(ReplyFinished, "回复完成", false),
        new(Petted, "被摸头", false),
    ];

    public IReadOnlyList<PetBehaviorSlot> Slots => _slots;

    public PetBehaviorSlot? Find(string id) => _slots.Find(slot => slot.Id == id);

    public void Register(PetBehaviorSlot slot)
    {
        if (Find(slot.Id) is null)
            _slots.Add(slot);
    }
}
