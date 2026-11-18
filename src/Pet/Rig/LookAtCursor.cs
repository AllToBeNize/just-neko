using Godot;

namespace JustNeko.Pet.Rig;

/// <summary>
/// 视线跟着鼠标：光标相对头部的方向换算成 -1..1，平滑过去。光标停下一阵子后慢慢回正，避免一直盯着不动的鼠标。
/// </summary>
internal sealed class LookAtCursor
{
    private const float Reach = 520f;          // 光标离头部这么远（像素）时转到最大角度
    private const float FollowRate = 5f;       // 跟随速度（越大越快）
    private const float RestRate = 1.2f;       // 回正速度
    private const double IdleSeconds = 6;
    private Vector2I _lastCursor;
    private double _idle;

    public Vector2 Value { get; private set; }

    /// <param name="head">头部在屏幕上的位置（像素）。</param>
    /// <param name="enabled">拖动时关掉，让位给拖动晃动。</param>
    public Vector2 Update(Vector2I cursor, Vector2 head, bool enabled, double delta)
    {
        _idle = cursor == _lastCursor ? _idle + delta : 0;
        _lastCursor = cursor;
        var target = Vector2.Zero;
        if (enabled && _idle < IdleSeconds)
        {
            var offset = (cursor - head) / Reach;
            // 屏幕 Y 向下为正，视线 Y 向上为正；用平滑的饱和曲线，远处不会突然顶到头。
            target = new Vector2(Saturate(offset.X), Saturate(-offset.Y));
        }
        var rate = target == Vector2.Zero ? RestRate : FollowRate;
        Value = Value.Lerp(target, 1 - Mathf.Exp(-rate * (float)delta));
        return Value;
    }

    private static float Saturate(float value) => value / Mathf.Sqrt(1 + value * value);
}
