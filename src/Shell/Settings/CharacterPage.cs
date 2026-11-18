using Godot;
using JustNeko.Pet;

namespace JustNeko.Shell.Settings;

/// <summary>角色：选角色（立即切换，不用重启）、人物大小（拖动时角色实时变化），以及给创作者用的动作绑定（默认收起）。</summary>
public sealed partial class CharacterPage : SettingsPage
{
    private readonly PetServices _services;
    private VBoxContainer _bindings = null!;
    private Label _result = null!;
    private PetMotion[] _motions = [];

    internal CharacterPage(PetServices services)
    {
        _services = services;
        Name = "CharacterPage";
    }

    /// <summary>用户选了另一个角色。返回 null 表示换好了，否则是失败原因（此时仍是原来的角色）。</summary>
    internal Func<string, string?>? SwitchCharacter { get; set; }

    public override void _Ready()
    {
        AddHeading("角色");
        AddCharacterSelector();
        var appearance = _services.Appearance;
        var row = new HBoxContainer();
        var label = PetTheme.Label("人物大小", PetTheme.Muted, 13);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(label);
        var percent = PetTheme.Label($"{appearance.Current.SizePercent}%", PetTheme.Accent, 13);
        row.AddChild(percent);
        AddChild(row);
        var slider = new HSlider
        {
            Name = "PetSizePercent",
            MinValue = PetAppearance.MinSize,
            MaxValue = PetAppearance.MaxSize,
            Value = appearance.Current.SizePercent,
            Step = 1,
            CustomMinimumSize = new Vector2(0, 24),
        };
        slider.ValueChanged += value =>
        {
            percent.Text = $"{(int)value}%";
            appearance.Update(appearance.Current with { SizePercent = (int)value });
        };
        AddChild(slider);
        AddChild(PetTheme.Note("拖动滑块时角色会实时变大变小，脚的位置保持不动。", 12));

        _bindings = new VBoxContainer { Name = "MotionBindings", Visible = false };
        _bindings.AddThemeConstantOverride("separation", 4);
        var expand = new Button { Name = "ShowMotionBindings", Text = "▸ 高级：动作绑定", Flat = true, Alignment = HorizontalAlignment.Left };
        expand.Pressed += () =>
        {
            _bindings.Visible = !_bindings.Visible;
            expand.Text = (_bindings.Visible ? "▾" : "▸") + " 高级：动作绑定";
        };
        AddChild(expand);
        AddChild(_bindings);
        BuildBindings();
        _services.AvatarChanged += BuildBindings;
    }

    public override void _ExitTree() => _services.AvatarChanged -= BuildBindings;

    private void AddCharacterSelector()
    {
        var settings = _services.Settings;
        var options = Characters.Available();
        var selector = new OptionButton { Name = "CharacterSelect", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
        foreach (var option in options)
            selector.AddItem(option.Label);
        var current = Math.Max(0, options.ToList().FindIndex(option => option.Id == settings.Current.Character));
        selector.Select(current);
        AddChild(selector);
        AddChild(PetTheme.Note("选好立即生效。VRM 角色放在 Models/vrm 下，用编辑器打开一次项目就会自动导入。", 12));
        _result = PetTheme.Note("");
        _result.Name = "CharacterResult";
        _result.Hide();
        AddChild(_result);
        selector.ItemSelected += index =>
        {
            var option = options[(int)index];
            var failure = SwitchCharacter?.Invoke(option.Id);
            if (failure is null)
            {
                settings.Update(settings.Current with { Character = option.Id });
                current = (int)index;
                Flash(_result, "✓ 换好了：" + option.Label, PetTheme.Good);
            }
            else
            {
                selector.Select(current);
                Flash(_result, failure, PetTheme.Accent, 8);
            }
        };
    }

    // 动作列表跟着当前角色走，换角色后重建。
    private void BuildBindings()
    {
        foreach (var child in _bindings.GetChildren())
        {
            _bindings.RemoveChild(child);
            child.QueueFree();
        }
        _motions = _services.Avatar.Motions.ToArray();
        _bindings.AddChild(PetTheme.Note(_motions.Length == 0
            ? "这个角色没有动作文件，待机、呼吸、眨眼和反应都由程序生成。"
            : "给每个状态选一段模型自带的动作。一般不用改；做新角色时在这里调。", 12));
        foreach (var slot in _services.Behaviors.Slots)
            AddMotionField(_bindings, slot);
    }

    private void AddMotionField(VBoxContainer fields, PetBehaviorSlot slot)
    {
        var appearance = _services.Appearance;
        var motions = _motions;
        var row = new HBoxContainer();
        fields.AddChild(row);
        var caption = PetTheme.Label(slot.Label, PetTheme.Muted, 13);
        caption.CustomMinimumSize = new Vector2(84, 0);
        row.AddChild(caption);
        var selector = new OptionButton
        {
            Name = slot.Id,
            Disabled = motions.Length == 0,
            FitToLongestItem = false,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 27),
        };
        selector.AddThemeFontSizeOverride("font_size", 13);
        selector.AddItem("无");
        foreach (var motion in motions)
        {
            selector.AddItem(motion.Name);
            selector.GetPopup().SetItemTooltip(selector.ItemCount - 1, motion.Label);
        }
        var key = appearance.Current.Motions.GetValueOrDefault(slot.Id);
        selector.Select(Array.FindIndex(motions, motion => motion.Key == key) + 1);
        selector.TooltipText = selector.Selected > 0 ? motions[selector.Selected - 1].Label : "无";
        selector.ItemSelected += index =>
        {
            var bindings = new Dictionary<string, string>(appearance.Current.Motions);
            if (index == 0)
                bindings.Remove(slot.Id);
            else
                bindings[slot.Id] = motions[index - 1].Key;
            selector.TooltipText = index == 0 ? "无" : motions[index - 1].Label;
            appearance.Update(appearance.Current with { Motions = bindings });
        };
        row.AddChild(selector);
    }
}
