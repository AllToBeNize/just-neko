using Godot;

namespace JustNeko.Pet.Rig;

/// <summary>
/// 通用层算出来的「语义姿态」，和角色格式无关。各个角色后端（Live2D、以后的 VRM）把它换算成自己的参数或骨骼，
/// 叠加在正在播放的动作上。
/// </summary>
/// <param name="Look">看向哪里，-1..1。X 向右为正，Y 向上为正。</param>
/// <param name="Lean">身体倾斜，-1..1。X 向右倾为正，Y 前倾为正。</param>
/// <param name="MouthOpen">嘴张开的程度，0..1。</param>
internal readonly record struct AvatarPose(Vector2 Look, Vector2 Lean, float MouthOpen)
{
    public static AvatarPose Rest => new(Vector2.Zero, Vector2.Zero, 0);
}

/// <summary>能接受语义姿态的角色。</summary>
internal interface IAvatarRig
{
    void SetPose(AvatarPose pose);
}
