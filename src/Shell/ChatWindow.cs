using Godot;
using JustNeko.Core.Interaction;

namespace JustNeko.Shell;

/// <summary>聊天记录和输入框。只负责显示和收集输入，回复由交互编排器产生。</summary>
public partial class ChatWindow : Window
{
    private const int ComposerWidth = 262;
    private const string Placeholder = "和 Neko 聊一句…";
    private const int ExpandedWidth = 330;
    private const int ExpandedHeight = 460;
    private const int ComposerHeight = 45;
    private const int TranscriptHeight = 142;
    private const int ChatSpacing = 6;
    private const int RowLimit = ChatHistoryStore.EntryLimit;
    private readonly InteractionOrchestrator _interactions;
    private readonly Dictionary<long, Label> _replies = [];
    private PanelContainer _transcript = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _messages = null!;
    private LineEdit _input = null!;
    private Button _send = null!;
    private Button _expand = null!;
    private Button _historyToggle = null!;
    private bool _expanded;
    private Rect2I _petBounds;
    private Rect2I _screenArea;

    internal event Action? Opened;
    internal event Action? Closed;

    /// <summary>窗口位置或大小变了（展开记录、跟着角色移动），气泡借此避开它。</summary>
    internal event Action? LayoutChanged;

    internal Rect2I Bounds => new(Position, Size);

    internal ChatWindow(InteractionOrchestrator interactions)
    {
        _interactions = interactions;
        Visible = false;
        ForceNative = true;
        AlwaysOnTop = true;
        Transparent = true;
        TransparentBg = true;
        Borderless = true;
        Unresizable = true;
        Size = new Vector2I(ComposerWidth, ComposerHeight);
        Title = "Neko 聊天";
        Theme = PetTheme.Create();
    }

    public override void _Ready()
    {
        CloseRequested += Dismiss;
        WindowInput += HandleKey;
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", ChatSpacing);
        AddChild(stack);
        stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        stack.AddChild(BuildTranscript());
        stack.AddChild(BuildComposer());
        _interactions.Changed += OnInteraction;
    }

    public override void _ExitTree() => _interactions.Changed -= OnInteraction;

    internal void Open()
    {
        var wasVisible = Visible;
        Show();
        GrabFocus();
        _input.GrabFocus();
        ScrollToLatest();
        if (!wasVisible)
            Opened?.Invoke();
    }

    internal void Follow(Rect2I petBounds, Rect2I screenArea)
    {
        _petBounds = petBounds;
        _screenArea = screenArea;
        ResizeChat();
    }

    internal void Dismiss()
    {
        var wasVisible = Visible;
        Hide();
        if (wasVisible)
            Closed?.Invoke();
    }

    /// <summary>显示上次保存的聊天记录。</summary>
    internal void LoadHistory(IEnumerable<ChatEntry> entries)
    {
        foreach (var entry in entries)
            AddMessage(entry.Speaker == ChatEntry.User ? "你" : "Neko", ReplyText.ForEntry(entry));
        ResizeChat();
    }

    /// <summary>清掉显示的聊天记录。对话上下文由编排器清。</summary>
    internal void ClearTranscript()
    {
        _replies.Clear();
        _expanded = false;
        _expand.Text = "展开";
        foreach (var row in _messages.GetChildren())
        {
            _messages.RemoveChild(row);
            row.QueueFree();
        }
        _transcript.Hide();
        _historyToggle.SetPressedNoSignal(false);
        ResizeChat();
    }

    private void HandleKey(InputEvent input)
    {
        if (input is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            Dismiss();
            SetInputAsHandled();
        }
    }

    private PanelContainer BuildTranscript()
    {
        _transcript = new PanelContainer
        {
            Name = "ChatTranscript",
            Visible = false,
            CustomMinimumSize = new Vector2(0, TranscriptHeight),
        };
        _transcript.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Background, 15, 11, 9));
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 4);
        _transcript.AddChild(layout);
        var header = new HBoxContainer();
        var title = PetTheme.Label("聊天记录", PetTheme.Muted, 11);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        _expand = new Button { Name = "ExpandHistory", Text = "展开", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "展开看完整的聊天记录" };
        _expand.AddThemeFontSizeOverride("font_size", 11);
        _expand.AddThemeColorOverride("font_color", PetTheme.Accent);
        _expand.Pressed += ToggleExpanded;
        header.AddChild(_expand);
        layout.AddChild(header);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        layout.AddChild(_scroll);
        _messages = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _messages.AddThemeConstantOverride("separation", 9);
        _scroll.AddChild(_messages);
        return _transcript;
    }

    private Label AddMessage(string name, string text)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        row.AddChild(PetTheme.Label(name, name == "Neko" ? PetTheme.Accent : PetTheme.Muted, 12));
        var body = new Label
        {
            Name = name == "Neko" ? "ChatReply" : "ChatUser",
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        row.AddChild(body);
        _messages.AddChild(row);
        if (_messages.GetChildCount() > RowLimit)
        {
            var oldest = _messages.GetChild(0);
            _messages.RemoveChild(oldest);
            oldest.QueueFree();
        }
        return body;
    }

    private PanelContainer BuildComposer()
    {
        var composer = new PanelContainer();
        composer.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Background, 17, 8, 6));
        var fields = new HBoxContainer();
        fields.AddThemeConstantOverride("separation", 5);
        composer.AddChild(fields);
        _input = new LineEdit
        {
            Name = "MessageInput",
            PlaceholderText = Placeholder,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 33),
        };
        _input.AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Surface, 12, 9, 4));
        _input.TextSubmitted += _ => SendOrCancel();
        fields.AddChild(_input);

        _send = new Button { Name = "SendMessage", Text = "发送", CustomMinimumSize = new Vector2(51, 32) };
        _send.AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Accent, 11));
        _send.AddThemeStyleboxOverride("hover", PetTheme.Box(new Color("ffdda9"), 11));
        _send.AddThemeStyleboxOverride("pressed", PetTheme.Box(new Color("e9ae73"), 11));
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            _send.AddThemeColorOverride(state, PetTheme.Background);
        _send.Pressed += SendOrCancel;
        fields.AddChild(_send);

        var close = new Button { Name = "CloseChat", Text = "×", TooltipText = "收起聊天", CustomMinimumSize = new Vector2(28, 32) };
        close.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        close.Pressed += Dismiss;
        fields.AddChild(close);
        fields.MoveChild(BuildHistoryToggle(), 0);
        return composer;
    }

    // 默认只有一个输入框，回复在角色头顶的气泡里说；想翻之前的对话再点「≡」展开记录。
    private Button BuildHistoryToggle()
    {
        _historyToggle = new Button
        {
            Name = "ToggleHistory",
            Text = "≡",
            TooltipText = "聊天记录",
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(28, 32),
        };
        _historyToggle.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _historyToggle.AddThemeStyleboxOverride("pressed", PetTheme.Box(PetTheme.Surface, 10));
        _historyToggle.AddThemeColorOverride("font_pressed_color", PetTheme.Accent);
        _historyToggle.Toggled += ShowHistory;
        _input.GetParent().AddChild(_historyToggle);
        return _historyToggle;
    }

    private void ShowHistory(bool shown)
    {
        _transcript.Visible = shown;
        if (!shown && _expanded)
        {
            _expanded = false;
            _expand.Text = "展开";
        }
        ResizeChat();
        ScrollToLatest();
        _input.GrabFocus();
    }

    private void SendOrCancel()
    {
        if (_interactions.IsBusy)
        {
            _interactions.Cancel();
            return;
        }

        var input = _input.Text.Trim();
        if (input.Length == 0)
            return;
        _input.Clear();
        AddMessage("你", input);
        _interactions.Submit(InteractionRequest.FromUser(input));
    }

    private void OnInteraction(InteractionEvent change)
    {
        switch (change)
        {
            case InteractionStarted started:
                _replies[started.InteractionId] = AddMessage("Neko", ReplyText.Waiting);
                ScrollToLatest();
                _input.PlaceholderText = "Neko 在说话…";
                _send.Text = "停止";
                break;
            case ReplyUpdated updated:
                SetReply(updated.InteractionId, updated.Text);
                break;
            case InteractionEnded ended:
                SetReply(ended.InteractionId, ReplyText.ForEnded(ended));
                _replies.Remove(ended.InteractionId);
                if (!_interactions.IsBusy)
                {
                    _send.Text = "发送";
                    _input.PlaceholderText = Placeholder;
                }
                if (Visible)
                    _input.GrabFocus();
                break;
        }
    }

    private void SetReply(long interactionId, string text)
    {
        if (!_replies.TryGetValue(interactionId, out var entry) || !IsInstanceValid(entry) || !entry.IsInsideTree())
            return;
        entry.Text = text;
        ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        if (Visible)
            CallDeferred(MethodName.ScrollToBottom);
    }

    private void ScrollToBottom() => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        _expand.Text = _expanded ? "收起" : "展开";
        ResizeChat();
        ScrollToLatest();
    }

    private void ResizeChat()
    {
        var composerBottom = _petBounds.Position.Y + _petBounds.Size.Y * 3 / 4 + ComposerHeight / 2;
        var historyRoom = _expanded
            ? Math.Min(ExpandedHeight, composerBottom - _screenArea.Position.Y - ComposerHeight - ChatSpacing - 8)
            : _petBounds.Size.Y * 3 / 4 - _petBounds.Size.Y / 16 - ComposerHeight / 2 - ChatSpacing - 8;
        var historyHeight = Math.Clamp(historyRoom, 72, _expanded ? ExpandedHeight : TranscriptHeight);
        _transcript.CustomMinimumSize = new Vector2(0, historyHeight);
        var height = ComposerHeight + (_transcript.Visible ? historyHeight + ChatSpacing : 0);
        var size = new Vector2I(_expanded && _transcript.Visible ? ExpandedWidth : ComposerWidth, height);
        if (size != Size)
            Size = size;
        PlaceChat();
    }

    private void PlaceChat()
    {
        if (!_screenArea.HasArea())
            return;
        var center = _petBounds.GetCenter();
        var sideGap = _petBounds.Size.X / 5;
        var left = center.X - sideGap - Size.X;
        var x = left >= _screenArea.Position.X ? left : center.X + sideGap;
        var composerBottom = _petBounds.Position.Y + _petBounds.Size.Y * 3 / 4 + ComposerHeight / 2;
        var y = composerBottom - Size.Y;
        var next = ScreenMath.ClampToScreen(new Vector2I(x, y), Size, _screenArea);
        if (next != Position)
            Position = next;
        LayoutChanged?.Invoke();
    }
}
