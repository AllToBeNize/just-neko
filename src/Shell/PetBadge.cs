using Godot;

namespace JustNeko.Shell;

/// <summary>角色头顶的小标记：模型连不上时亮起，点一下打开设置中心对应的页面。</summary>
public sealed partial class PetBadge : Button
{
    public PetBadge()
    {
        Name = "PetBadge";
        Text = "!";
        Visible = false;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(22, 22);
        AddThemeFontSizeOverride("font_size", 14);
        AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Accent, 11));
        AddThemeStyleboxOverride("hover", PetTheme.Box(new Color("ffdda9"), 11));
        AddThemeStyleboxOverride("pressed", PetTheme.Box(new Color("e9ae73"), 11));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
            AddThemeColorOverride(state, PetTheme.Background);
    }

    internal void ShowProblem(string message)
    {
        TooltipText = message + "\n点一下打开设置。";
        if (!Visible)
        {
            Show();
            Scale = Vector2.One * 0.4f;
            PivotOffset = Size / 2;
            CreateTween().TweenProperty(this, "scale", Vector2.One, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }

    internal void Clear() => Hide();
}
