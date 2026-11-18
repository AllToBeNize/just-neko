using Godot;

namespace JustNeko.Pet.Activities;

/// <summary>
/// 桌宠空闲时自己做的事，比如沿任务栏走、打个盹、看鼠标。实现这个接口并在
/// <see cref="ActivityDirector.Register"/> 里登记即可；用户一拖动、开聊天或者开始一次 AI 交互，
/// 正在做的活动就会被 <see cref="Stop"/>。AI 指令和 Agent 工具也可以按 Id 直接触发。
/// </summary>
internal interface IPetActivity
{
    string Id { get; }

    string Label { get; }

    /// <summary>需要的行为槽，会出现在角色属性里让玩家选动作。</summary>
    IEnumerable<PetBehaviorSlot> Slots => [];

    /// <summary>现在想不想做，返回抽中的权重；0 表示现在不做。</summary>
    double Weight(PetContext context);

    void Start(PetContext context);

    /// <summary>每帧调用；返回 false 表示做完了。</summary>
    bool Update(PetContext context, double delta);

    /// <summary>做完或被打断时调用，在这里收拾干净（释放占用的动作层等）。</summary>
    void Stop(PetContext context);
}

/// <summary>活动能用到的角色能力。坐标都是屏幕像素。</summary>
internal interface IPetBody
{
    /// <summary>角色窗口在屏幕上的位置和大小。</summary>
    Rect2I Bounds { get; }

    /// <summary>当前屏幕去掉任务栏后的可用区域。</summary>
    Rect2I WorkArea { get; }

    /// <summary>当前屏幕的完整区域。和 WorkArea 的差就是任务栏所在的边。</summary>
    Rect2I ScreenRect { get; }

    void MoveTo(Vector2I position);

    void SetMood(string mood);
}

internal sealed class PetContext(IPetBody body, PetMotionDirector motions)
{
    private const string Layer = "activity";

    public IPetBody Body { get; } = body;

    public void HoldMotion(string behavior) => motions.Hold(Layer, PetMotionDirector.ActivityPriority, behavior);

    public void ReleaseMotion() => motions.Release(Layer);
}
