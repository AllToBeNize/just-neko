using Godot;

namespace JustNeko.Shell.Settings;

/// <summary>设置中心的一页。</summary>
public partial class SettingsPage : VBoxContainer
{
    protected SettingsPage()
    {
        AddThemeConstantOverride("separation", 9);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    /// <summary>切到这一页时调用，用来刷新显示。</summary>
    internal virtual void OnShown()
    {
    }

    protected void AddHeading(string title, string? note = null)
    {
        AddChild(PetTheme.Label(title, PetTheme.Ink, 19));
        if (note is not null)
            AddChild(PetTheme.Note(note));
    }

    protected void AddSection(string title) => AddChild(PetTheme.Label(title, PetTheme.Ink, 15));

    protected CheckButton AddToggle(string name, string caption, string note, bool value, Action<bool> changed)
    {
        var toggle = new CheckButton { Name = name, Text = caption, ButtonPressed = value, FocusMode = FocusModeEnum.None };
        toggle.Toggled += pressed => changed(pressed);
        AddChild(toggle);
        AddChild(PetTheme.Note(note, 12));
        return toggle;
    }

    protected static LineEdit Field(Control parent, string name, string caption, string placeholder, out HBoxContainer row)
    {
        parent.AddChild(PetTheme.Label(caption, PetTheme.Muted, 13));
        row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);
        var control = new LineEdit
        {
            Name = name,
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        control.AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Surface, 8, 9, 4));
        row.AddChild(control);
        return control;
    }

    /// <summary>短暂显示一句结果，过几秒自动淡出。</summary>
    protected void Flash(Label label, string text, Color color, double seconds = 4)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", color);
        label.Modulate = Colors.White;
        label.Show();
        var tween = label.CreateTween();
        tween.TweenInterval(seconds);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.6);
    }
}
