using Godot;
using JustNeko.Pet;

namespace JustNeko.Shell.Settings;

/// <summary>角色：人物大小（拖动时角色实时变化），以及给创作者用的动作绑定（默认收起）。</summary>
public sealed partial class CharacterPage : SettingsPage
{
    private readonly PetServices _services;
    private readonly PetMotion[] _motions;

    /// <summary>换了角色，用户点了「立即重启」。</summary>
    internal event Action? RestartRequested;

    internal CharacterPage(PetServices services)
    {
        _services = services;
        _motions = services.Avatar.Motions.ToArray();
        Name = "CharacterPage";
    }

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

        var advanced = new VBoxContainer { Name = "MotionBindings", Visible = false };
        advanced.AddThemeConstantOverride("separation", 4);
        var expand = new Button { Name = "ShowMotionBindings", Text = "▸ 高级：动作绑定", Flat = true, Alignment = HorizontalAlignment.Left };
        expand.Pressed += () =>
        {
            advanced.Visible = !advanced.Visible;
            expand.Text = (advanced.Visible ? "▾" : "▸") + " 高级：动作绑定";
        };
        AddChild(expand);
        AddChild(advanced);
        advanced.AddChild(PetTheme.Note(_motions.Length == 0
            ? "这个角色没有动作文件，待机、呼吸、眨眼和反应都由程序生成。"
            : "给每个状态选一段模型自带的动作。一般不用改；做新角色时在这里调。", 12));
        foreach (var slot in _services.Behaviors.Slots)
            AddMotionField(advanced, slot);
    }

    private void AddCharacterSelector()
    {
        var settings = _services.Settings;
        var options = Characters.Available();
        var selector = new OptionButton { Name = "CharacterSelect", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
        foreach (var option in options)
            selector.AddItem(option.Label);
        var current = settings.Current.Character;
        selector.Select(Math.Max(0, options.ToList().FindIndex(option => option.Id == current)));
        AddChild(selector);
        var note = PetTheme.Note("换角色要重启 Just Neko 才生效。VRM 角色放在 Models/vrm 下，用编辑器打开一次项目就会自动导入。", 12);
        AddChild(note);
        var restart = PetTheme.PrimaryButton("RestartNow", "立即重启");
        restart.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        restart.Visible = false;
        restart.Pressed += () => RestartRequested?.Invoke();
        AddChild(restart);
        selector.ItemSelected += index =>
        {
            settings.Update(settings.Current with { Character = options[(int)index].Id });
            restart.Visible = options[(int)index].Id != current;
        };
    }

    private void AddMotionField(VBoxContainer fields, PetBehaviorSlot slot)
    {
        var appearance = _services.Appearance;
        var row = new HBoxContainer();
        fields.AddChild(row);
        var caption = PetTheme.Label(slot.Label, PetTheme.Muted, 13);
        caption.CustomMinimumSize = new Vector2(84, 0);
        row.AddChild(caption);
        var selector = new OptionButton
        {
            Name = slot.Id,
            Disabled = _motions.Length == 0,
            FitToLongestItem = false,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 27),
        };
        selector.AddThemeFontSizeOverride("font_size", 13);
        selector.AddItem("无");
        foreach (var motion in _motions)
        {
            selector.AddItem(motion.Name);
            selector.GetPopup().SetItemTooltip(selector.ItemCount - 1, motion.Label);
        }
        var key = appearance.Current.Motions.GetValueOrDefault(slot.Id);
        selector.Select(Array.FindIndex(_motions, motion => motion.Key == key) + 1);
        selector.TooltipText = selector.Selected > 0 ? _motions[selector.Selected - 1].Label : "无";
        selector.ItemSelected += index =>
        {
            var bindings = new Dictionary<string, string>(appearance.Current.Motions);
            if (index == 0)
                bindings.Remove(slot.Id);
            else
                bindings[slot.Id] = _motions[index - 1].Key;
            selector.TooltipText = index == 0 ? "无" : _motions[index - 1].Label;
            appearance.Update(appearance.Current with { Motions = bindings });
        };
        row.AddChild(selector);
    }
}
