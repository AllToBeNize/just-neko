using Godot;

namespace JustNeko.Shell.Settings;

/// <summary>设置中心：从托盘菜单打开的普通窗口，左边分页，右边内容。</summary>
public sealed partial class SettingsCenter : Window
{
    public const string Chat = "chat";
    public const string LocalModel = "local-model";
    public const string Character = "character";
    public const string Behavior = "behavior";
    public const string Privacy = "privacy";
    public const string About = "about";

    private readonly List<(string Id, string Label, SettingsPage Page)> _pages;
    private readonly Dictionary<string, Button> _tabs = [];
    private ScrollContainer _scroll = null!;

    internal SettingsCenter(PetServices services)
    {
        Name = "SettingsCenter";
        Title = "Just Neko 设置中心";
        Visible = false;
        ForceNative = true;
        Size = new Vector2I(680, 560);
        MinSize = new Vector2I(560, 420);
        Theme = PetTheme.Create();
        ChatPage = new ChatPage(services);
        ChatPage.LocalModelRequested += () => Open(LocalModel);
        PrivacyPage = new PrivacyPage(services);
        _pages =
        [
            (Chat, "对话", ChatPage),
            (LocalModel, "本地模型", new LocalModelPage(services)),
            (Character, "角色", new CharacterPage(services)),
            (Behavior, "行为", new BehaviorPage(services)),
            (Privacy, "隐私", PrivacyPage),
            (About, "关于", new AboutPage()),
        ];
    }

    internal ChatPage ChatPage { get; }

    internal PrivacyPage PrivacyPage { get; }

    public override void _Ready()
    {
        CloseRequested += Hide;
        var background = new PanelContainer();
        background.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Background, 0));
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var layout = new HBoxContainer();
        layout.AddThemeConstantOverride("separation", 0);
        background.AddChild(layout);

        var sidebar = new PanelContainer { CustomMinimumSize = new Vector2(148, 0) };
        sidebar.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Surface, 0, 10, 16));
        layout.AddChild(sidebar);
        var tabs = new VBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        sidebar.AddChild(tabs);
        var group = new ButtonGroup();
        foreach (var (id, label, _) in _pages)
        {
            var tab = new Button
            {
                Name = "Tab_" + id,
                Text = label,
                ToggleMode = true,
                ButtonGroup = group,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 36),
            };
            tab.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            tab.AddThemeStyleboxOverride("hover", PetTheme.Box(PetTheme.Background, 8, 12, 0));
            tab.AddThemeStyleboxOverride("pressed", PetTheme.Box(PetTheme.Background, 8, 12, 0));
            tab.AddThemeColorOverride("font_pressed_color", PetTheme.Accent);
            tab.Pressed += () => ShowPage(id);
            tabs.AddChild(tab);
            _tabs[id] = tab;
        }

        _scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        layout.AddChild(_scroll);
        var content = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            content.AddThemeConstantOverride("margin_" + side, side is "left" or "right" ? 26 : 20);
        _scroll.AddChild(content);
        foreach (var (_, _, page) in _pages)
        {
            page.Hide();
            content.AddChild(page);
        }
    }

    internal void Open(string page = Chat)
    {
        if (!Visible)
        {
            var area = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
            Position = ScreenMath.ClampToScreen(area.Position + (area.Size - Size) / 2, Size, area);
            Show();
        }
        ShowPage(page);
        GrabFocus();
    }

    internal string? CurrentPage { get; private set; }

    private void ShowPage(string id)
    {
        CurrentPage = id;
        foreach (var (pageId, _, page) in _pages)
        {
            var selected = pageId == id;
            page.Visible = selected;
            _tabs[pageId].SetPressedNoSignal(selected);
            if (selected)
                page.OnShown();
        }
        _scroll.ScrollVertical = 0;
    }
}
