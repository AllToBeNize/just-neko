using Godot;

namespace JustNeko;

internal static class PetTheme
{
    public static readonly Color Background = new("171c24");
    public static readonly Color Surface = new("222b39");
    public static readonly Color Ink = new("fcf3e5");
    public static readonly Color Muted = new("b7c2d2");
    public static readonly Color Accent = new("fac383");

    public static Theme Create() => new()
    {
        DefaultFont = new SystemFont { FontNames = ["Microsoft YaHei UI", "Segoe UI"] },
        DefaultFontSize = 15,
    };

    public static StyleBoxFlat Box(Color color, int radius, int horizontal = 0, int vertical = 0)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = horizontal;
        box.ContentMarginTop = box.ContentMarginBottom = vertical;
        return box;
    }

    public static Label Label(string text, Color color, int fontSize = 15)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
