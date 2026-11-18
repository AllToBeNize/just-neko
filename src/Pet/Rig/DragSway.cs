using Godot;

namespace JustNeko.Pet.Rig;

/// <summary>
/// 拖动晃动：按窗口移动的速度给身体一个反方向的倾斜（被拖着走时身子往后甩），用带阻尼的弹簧算，
/// 松手后会晃几下再停住。头发、衣服由角色自己的物理跟着摆。
/// </summary>
internal sealed class DragSway
{
    private const float Stiffness = 60f;
    private const float Damping = 7f;
    private const float SpeedForFullLean = 1400f;  // 像素/秒
    private Vector2 _velocity;
    private Vector2? _lastPosition;

    public Vector2 Value { get; private set; }

    public Vector2 Update(Vector2I windowPosition, double delta)
    {
        var dt = (float)Mathf.Clamp(delta, 1e-3, 0.1);
        var move = _lastPosition is { } last ? (windowPosition - last) / dt : Vector2.Zero;
        _lastPosition = windowPosition;

        // 横向移动 → 往反方向侧倾；竖向移动 → 前后仰。
        var target = new Vector2(-move.X, move.Y) / SpeedForFullLean;
        target = target.LimitLength(1);
        var acceleration = Stiffness * (target - Value) - Damping * _velocity;
        _velocity += acceleration * dt;
        Value = (Value + _velocity * dt).Clamp(new Vector2(-1, -1), new Vector2(1, 1));
        return Value;
    }
}
