using System.Text;
using System.Text.Json;
using Godot;
using HttpRequestException = System.Net.Http.HttpRequestException;

namespace JustNeko;

public partial class ChatWindow : Window
{
    private const int ComposerWidth = 238;
    private const int ComposerHeight = 45;
    private const int TranscriptHeight = 142;
    private const int ChatSpacing = 6;
    private const int HistoryLimit = 16;
    private readonly List<ChatMessage> _history = [];
    private readonly PetPreview _pet;
    private readonly SettingsWindow _settings;
    private readonly PetSpeechBubble _speech;
    private PanelContainer _transcript = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _messages = null!;
    private LineEdit _input = null!;
    private Button _send = null!;
    private CancellationTokenSource? _request;
    private (int Provider, Uri? Endpoint, string Model)? _conversationRoute;
    private Rect2I _petBounds;
    private Rect2I _screenArea;

    internal event Action<PetBehavior>? BehaviorChanged;

    internal static int LeftExtent(int petWidth) => Math.Max(0, ComposerWidth + petWidth / 5 - petWidth / 2);

    internal ChatWindow(PetPreview pet, SettingsWindow settings, PetSpeechBubble speech)
    {
        _pet = pet;
        _settings = settings;
        _speech = speech;
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
    }

    public override void _ExitTree()
    {
        _request?.Cancel();
    }

    internal void Open()
    {
        var wasVisible = Visible;
        Show();
        GrabFocus();
        _input.GrabFocus();
        ScrollToLatest();
        if (!wasVisible)
            BehaviorChanged?.Invoke(PetBehavior.ChatOpened);
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
            BehaviorChanged?.Invoke(PetBehavior.ChatClosed);
    }

    internal void ClearConversation()
    {
        _request?.Cancel();
        _history.Clear();
        foreach (var row in _messages.GetChildren())
        {
            _messages.RemoveChild(row);
            row.QueueFree();
        }
        _transcript.Hide();
        ResizeChat();
        _speech.Dismiss();
        _pet.SetMood("neutral");
        BehaviorChanged?.Invoke(PetBehavior.Idle);
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
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _transcript.AddChild(_scroll);
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
        if (_messages.GetChildCount() > HistoryLimit)
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
            PlaceholderText = "和 Neko 聊一句…",
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
        return composer;
    }

    private void SendOrCancel()
    {
        if (_request is not null)
        {
            _request.Cancel();
            return;
        }
        _ = SendAsync();
    }

    private async Task SendAsync()
    {
        var input = _input.Text.Trim();
        if (input.Length == 0)
            return;

        var settings = _settings.Current;
        var endpoint = settings.Provider == 0 ? null : ChatClient.ResolveEndpoint(settings.Endpoint);
        var route = (settings.Provider, endpoint, settings.Model);
        if (_conversationRoute != route)
        {
            _history.Clear();
            _conversationRoute = route;
        }

        var user = new ChatMessage("user", input);
        var expressionNames = _pet.EmotionNames.ToArray();
        var expressionPrompt = "在回复正文最开头输出一个表情标签，例如 [emotion:neutral]。可选名称包括 "
            + string.Join("、", expressionNames) + "。表情用名称表示，只输出一个标签，然后直接输出正文。";
        ChatMessage[] context = [new("system", settings.Prompt + "\n" + expressionPrompt), .. _history, user];
        _input.Clear();
        AddMessage("你", input);
        var entry = AddMessage("Neko", "…");
        _transcript.Show();
        ResizeChat();
        ScrollToLatest();
        _speech.Begin();
        _pet.SetMood("neutral");
        BehaviorChanged?.Invoke(PetBehavior.WaitingForReply);

        using var cancellation = new CancellationTokenSource();
        _request = cancellation;
        _send.Text = "停止";
        var emotion = new EmotionStream(expressionNames);
        var body = new StringBuilder();
        var completed = false;
        try
        {
            var stream = endpoint is null
                ? ChatClient.DemoAsync(input, cancellation.Token)
                : ChatClient.StreamAsync(endpoint, settings.Model, settings.Key, context, cancellation.Token);
            await foreach (var piece in stream)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var text = emotion.Read(piece);
                if (_pet.Mood != emotion.Emotion)
                    _pet.SetMood(emotion.Emotion);
                if (text.Length > 0)
                {
                    if (body.Length == 0)
                        BehaviorChanged?.Invoke(PetBehavior.Replying);
                    SetReply(entry, body.Append(text).ToString());
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
            var reply = body.Append(emotion.Complete()).ToString();
            if (string.IsNullOrWhiteSpace(reply))
                throw new InvalidDataException("模型没有返回正文。");
            SetReply(entry, reply);
            _history.Add(user);
            _history.Add(new ChatMessage("assistant", reply));
            if (_history.Count > HistoryLimit)
                _history.RemoveRange(0, 2);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            var status = cancellation.IsCancellationRequested ? "已停止" : "连接模型超时";
            SetReply(entry, body.Length == 0 ? status : $"{body}\n{status}");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            SetReply(entry, error is JsonException ? "模型返回了无法解析的流式数据。" : error.Message);
        }
        finally
        {
            _request = null;
            if (IsInsideTree())
            {
                _send.Text = "发送";
                if (IsCurrentReply(entry))
                {
                    _speech.FadeAfterReply();
                    BehaviorChanged?.Invoke(completed ? PetBehavior.ReplyFinished : PetBehavior.Idle);
                }
                if (Visible)
                    _input.GrabFocus();
            }
        }
    }

    private bool IsCurrentReply(Label entry) => IsInsideTree() && IsInstanceValid(entry)
        && !entry.IsQueuedForDeletion() && entry.IsInsideTree();

    private void SetReply(Label entry, string text)
    {
        if (!IsCurrentReply(entry))
            return;
        entry.Text = text;
        _speech.SetText(text);
        ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        if (Visible)
            CallDeferred(MethodName.ScrollToBottom);
    }

    private void ScrollToBottom() => _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;

    private void ResizeChat()
    {
        var historyRoom = _petBounds.Size.Y * 3 / 4 - _petBounds.Size.Y / 16 - ComposerHeight / 2 - ChatSpacing - 8;
        var historyHeight = Math.Clamp(historyRoom, 48, TranscriptHeight);
        _transcript.CustomMinimumSize = new Vector2(0, historyHeight);
        var height = ComposerHeight + (_transcript.Visible ? historyHeight + ChatSpacing : 0);
        var size = new Vector2I(ComposerWidth, height);
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
        var next = DesktopPet.ClampToScreen(new Vector2I(x, y), Size, _screenArea);
        if (next != Position)
            Position = next;
    }
}
