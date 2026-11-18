using Godot;

namespace JustNeko.Shell;

internal static class ScreenMath
{
    public static Vector2I ClampToScreen(Vector2I position, Vector2I size, Rect2I area)
    {
        if (!area.HasArea())
            return position;
        var limit = (area.End - size).Max(area.Position);
        return position.Clamp(area.Position, limit);
    }
}
