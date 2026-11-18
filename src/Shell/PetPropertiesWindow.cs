using Godot;
using JustNeko.Pet;
using Timer = Godot.Timer;

namespace JustNeko.Shell;

public partial class PetPropertiesWindow : Window
{
    private const int PanelWidth = 218;
    private const int PanelHeight = 344;
    private readonly PetMotion[] _motions;
    private readonly BehaviorCatalog _catalog;
    private Timer _saveTimer = null!;
    private Label _saveError = null!;
    private bool _dirty;

    internal PetAppearance Appearance { get; private set; }
    internal event Action? AppearanceChanged;

    internal PetPropertiesWindow(IEnumerable<PetMotion> motions, BehaviorCatalog catalog)
    {
        _motions = motions.ToArray();
        _catalog = catalog;
        Appearance = PetAppearance.Load(catalog);
        Title = "角色属性";
        Visible = false;
        ForceNative = true;
        Transparent = true;
        TransparentBg = true;
        Borderless = true;
        AlwaysOnTop = true;
        Unresizable = true;
        Size = new Vector2I(PanelWidth, PanelHeight);
        Theme = PetTheme.Create();
    }

    public override void _Ready()
    {
        CloseRequested += Dismiss;
        WindowInput += input =>
        {
            if (input is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                SetInputAsHandled();
                Dismiss();
            }
        };

        _saveTimer = new Timer { OneShot = true, WaitTime = 0.4 };
        _saveTimer.Timeout += Save;
        AddChild(_saveTimer);

        var surface = new PanelContainer { Name = "PropertySurface" };
        surface.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Background, 16, 12, 10));
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 7);
        surface.AddChild(layout);

        var header = new HBoxContainer();
        var title = PetTheme.Label("角色属性", PetTheme.Ink, 17);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = new Button { Name = "CloseProperties", Text = "×", TooltipText = "收起角色属性", CustomMinimumSize = new Vector2(25, 25) };
        close.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        close.Pressed += Dismiss;
        header.AddChild(close);
        layout.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        layout.AddChild(scroll);
        var fields = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        fields.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(fields);
        BuildSizeControl(fields);

        fields.AddChild(PetTheme.Label("行为动画", PetTheme.Ink, 14));
        foreach (var slot in _catalog.Slots)
            AddMotionField(fields, slot);

        _saveError = PetTheme.Label("", PetTheme.Accent, 12);
        _saveError.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _saveError.Hide();
        layout.AddChild(_saveError);
    }

    public override void _ExitTree() => Save();

    internal void Open()
    {
        Show();
        GrabFocus();
    }

    internal void Dismiss()
    {
        _saveTimer.Stop();
        Save();
        Hide();
    }

    internal static int RightExtent(int petWidth) => Math.Max(petWidth, RightOffset(petWidth) + PanelWidth);

    internal void Follow(Rect2I petBounds, Rect2I screenArea)
    {
        var offset = new Vector2I(RightOffset(petBounds.Size.X), 12);
        var next = ScreenMath.ClampToScreen(petBounds.Position + offset, Size, screenArea);
        if (Position != next)
            Position = next;
    }

    internal static int RightOffset(int petWidth) => petWidth * 3 / 4 + 9;

    private void BuildSizeControl(VBoxContainer fields)
    {
        var row = new HBoxContainer();
        var label = PetTheme.Label("人物大小", PetTheme.Muted, 13);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        var percent = PetTheme.Label($"{Appearance.SizePercent}%", PetTheme.Accent, 13);
        row.AddChild(percent);
        fields.AddChild(row);
        var slider = new HSlider
        {
            Name = "PetSizePercent",
            MinValue = PetAppearance.MinSize,
            MaxValue = PetAppearance.MaxSize,
            Value = Appearance.SizePercent,
            Step = 1,
            CustomMinimumSize = new Vector2(0, 24),
        };
        slider.ValueChanged += value =>
        {
            var size = (int)value;
            percent.Text = $"{size}%";
            UpdateAppearance(Appearance with { SizePercent = size });
        };
        fields.AddChild(slider);
    }

    private void AddMotionField(VBoxContainer fields, PetBehaviorSlot slot)
    {
        var row = new HBoxContainer();
        fields.AddChild(row);
        var caption = PetTheme.Label(slot.Label, PetTheme.Muted, 13);
        caption.CustomMinimumSize = new Vector2(58, 0);
        row.AddChild(caption);
        var selector = new OptionButton
        {
            Name = slot.Id,
            Disabled = _motions.Length == 0,
            FitToLongestItem = false,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 27),
        };
        selector.AddThemeFontSizeOverride("font_size", 13);
        selector.AddItem("无");
        foreach (var motion in _motions)
        {
            selector.AddItem(motion.Name);
            selector.GetPopup().SetItemTooltip(selector.ItemCount - 1, motion.Label);
        }
        var key = Appearance.Motions.GetValueOrDefault(slot.Id);
        selector.Select(Array.FindIndex(_motions, motion => motion.Key == key) + 1);
        selector.TooltipText = selector.Selected > 0 ? _motions[selector.Selected - 1].Label : "无";
        selector.ItemSelected += index =>
        {
            var bindings = new Dictionary<string, string>(Appearance.Motions);
            if (index == 0)
            {
                selector.TooltipText = "无";
                bindings.Remove(slot.Id);
            }
            else
            {
                var motion = _motions[index - 1];
                selector.TooltipText = motion.Label;
                bindings[slot.Id] = motion.Key;
            }
            UpdateAppearance(Appearance with { Motions = bindings });
        };
        row.AddChild(selector);
    }

    private void UpdateAppearance(PetAppearance next)
    {
        Appearance = next;
        _dirty = true;
        _saveTimer.Start();
        AppearanceChanged?.Invoke();
    }

    private void Save()
    {
        if (!_dirty)
            return;
        var error = Appearance.Save(_catalog);
        _dirty = error != Error.Ok;
        if (_dirty)
            _saveError.Text = $"无法保存角色属性（{error}）。";
        _saveError.Visible = _dirty;
    }
}
