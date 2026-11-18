using Godot;
using JustNeko.Pet.Rig;

namespace JustNeko.Pet;

/// <summary>
/// 角色后端的基类：Live2D、VRM 等各实现一份。上层（动作选择、表情、通用层、命中判断）只认这里的接口。
/// </summary>
public partial class PetAvatar : Control, IAvatarRig
{
    public string Mood { get; protected set; } = "neutral";

    /// <summary>角色能做的表情名，会告诉模型让它用 [emotion:名称] 选。</summary>
    internal virtual IEnumerable<string> EmotionNames => ["neutral"];

    /// <summary>角色自带的动作，给行为槽绑定。没有动作文件的角色返回空。</summary>
    internal virtual IReadOnlyCollection<PetMotion> Motions => [];

    internal event Action? MotionFinished;

    /// <summary>这个位置（相对本控件）是不是点在角色身上，用来决定鼠标穿透。</summary>
    public virtual bool ContainsPointer(Vector2 position) => new Rect2(Vector2.Zero, Size).HasPoint(position);

    internal virtual bool HasMotion(string key) => false;

    internal virtual void PlayMotion(string? key, bool loop)
    {
    }

    public virtual void SetMood(string mood) => Mood = mood;

    /// <summary>通用层（看鼠标、拖动晃动、对口型）算出的姿态，叠加在动作上。</summary>
    internal virtual void SetPose(AvatarPose pose)
    {
    }

    void IAvatarRig.SetPose(AvatarPose pose) => SetPose(pose);

    protected void RaiseMotionFinished() => MotionFinished?.Invoke();
}
