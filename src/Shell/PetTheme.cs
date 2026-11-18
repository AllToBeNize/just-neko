using Godot;

namespace JustNeko.Shell;

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

    public static readonly Color Good = new("8fd19e");

    /// <summary>说明文字：浅色、自动换行。</summary>
    public static Label Note(string text, int fontSize = 13)
    {
        var label = Label(text, Muted, fontSize);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    public static Button PrimaryButton(string name, string text)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new Vector2(84, 34) };
        button.AddThemeStyleboxOverride("normal", Box(Accent, 10, 12, 4));
        button.AddThemeStyleboxOverride("hover", Box(new Color("ffdda9"), 10, 12, 4));
        button.AddThemeStyleboxOverride("pressed", Box(new Color("e9ae73"), 10, 12, 4));
        button.AddThemeStyleboxOverride("disabled", Box(Surface, 10, 12, 4));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, Background);
        return button;
    }

    public static Label Label(string text, Color color, int fontSize = 15)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
