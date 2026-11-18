using Godot;
using JustNeko.Core.Ai;
using JustNeko.Core.Interaction;
using JustNeko.Core.Platform;
using JustNeko.Core.Settings;
using JustNeko.Pet;
using JustNeko.Pet.Activities;
using Timer = Godot.Timer;

namespace JustNeko.Shell;

/// <summary>
/// 主场景：组装各个模块，处理角色上的鼠标操作，并让聊天、气泡、属性面板跟着角色走。
/// AI 流程在 <see cref="InteractionOrchestrator"/>，动作选择在 <see cref="PetMotionDirector"/>，
/// 自主活动在 <see cref="ActivityDirector"/>。
/// </summary>
public partial class DesktopPet : Control, IPetBody
{
    private const int Chat = 0;
    private const int Settings = 1;
    private const int Clear = 2;
    private const int Quit = 3;
    private const string DragLayer = "drag";
    private const string InteractionLayer = "interaction";

    private IPlatformServices _platform = null!;
    private SettingsService _settings = null!;
    private ModelRouter _models = null!;
    private InteractionOrchestrator _interactions = null!;
    private BehaviorCatalog _behaviors = null!;
    private PetMotionDirector _motions = null!;
    private ActivityDirector _activities = null!;
    private Window _window = null!;
    private PetAvatar _pet = null!;
    private ChatWindow _chat = null!;
    private PetSpeechBubble _speech = null!;
    private SettingsWindow _settingsWindow = null!;
    private PetPropertiesWindow _properties = null!;
    private PopupMenu _menu = null!;
    private Timer _singleClick = null!;
    private bool _headless;
    private bool _pressed;
    private bool _dragging;
    private bool _doubleClick;
    private Vector2I _baseSize;
    private Vector2I _pressPosition;
    private Vector2I _dragOrigin;
    private long? _speaking;
    private bool _replying;

    public override void _Ready()
    {
        Engine.MaxFps = 30;
        _headless = DisplayServer.GetName() == "headless";
        _window = GetWindow();
        _baseSize = _window.Size;
        _window.Unresizable = true;
        _window.CloseRequested += () => GetTree().Quit();

        var dataDirectory = ProjectSettings.GlobalizePath("user://");
        _platform = PlatformServices.Create(dataDirectory);
        _settings = new SettingsService(dataDirectory, _platform.Secrets);
        _settings.Load();
        _models = new ModelRouter(_settings);

        _pet = new PetAvatar { Name = "Pet", MouseFilter = MouseFilterEnum.Stop };
        AddChild(_pet);
        _pet.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _pet.GuiInput += OnPetInput;

        _interactions = new InteractionOrchestrator(_models, () => _settings.Current.RolePrompt, () => _pet.EmotionNames.ToArray());
        _interactions.Changed += OnInteraction;
        _ = new InteractionLog(Path.Combine(dataDirectory, "logs"), _interactions, DescribeChatRoute);

        // 行为槽要在角色属性窗口创建前登记完，活动登记的槽才会出现在属性面板里。
        _behaviors = new BehaviorCatalog();
        _activities = new ActivityDirector { Name = "PetActivities" };
        _properties = new PetPropertiesWindow(_pet.Motions, _behaviors) { Name = "PetProperties" };
        _properties.AppearanceChanged += ResizePet;
        _motions = new PetMotionDirector(_pet, _behaviors, () => _properties.Appearance);
        _pet.MotionFinished += _motions.OnMotionFinished;
        _activities.Initialize(new PetContext(this, _motions), IsUserBusy);

        _settingsWindow = new SettingsWindow(_settings) { Name = "PetSettings" };
        AddChild(_settingsWindow);
        AddChild(_properties);
        _speech = new PetSpeechBubble { Name = "PetReply" };
        AddChild(_speech);
        _chat = new ChatWindow(_interactions) { Name = "PetChat" };
        _chat.Opened += () => OnUserAction(BehaviorCatalog.ChatOpened);
        _chat.Closed += () => _motions.PlayOnce(BehaviorCatalog.ChatClosed);
        AddChild(_chat);
        AddChild(_activities);

        _menu = new PopupMenu { Name = "PetMenu", ForceNative = true, Theme = PetTheme.Create() };
        _menu.AddItem("聊天", Chat);
        _menu.AddItem("设置…", Settings);
        _menu.AddItem("清空对话", Clear);
        _menu.AddSeparator();
        _menu.AddItem("退出", Quit);
        _menu.IdPressed += OnMenuItem;
        AddChild(_menu);

        _singleClick = new Timer { OneShot = true, WaitTime = _platform.DoubleClickTime.TotalSeconds };
        _singleClick.Timeout += OpenChat;
        AddChild(_singleClick);

        ResizePet();
        if (!_headless)
        {
            var area = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
            _window.Position = area.End - _window.Size - new Vector2I(24, 12);
        }
    }

    public override void _ExitTree()
    {
        _interactions.Cancel();
        _models.Dispose();
    }

    public override void _Process(double delta)
    {
        if (_headless)
            return;

        var cursor = DisplayServer.MouseGetPosition();
        if (_pressed)
        {
            var offset = cursor - _pressPosition;
            if (!_dragging && offset.LengthSquared() >= 36)
            {
                _singleClick.Stop();
                _dragging = true;
                _activities.Interrupt();
                _motions.Hold(DragLayer, PetMotionDirector.DragPriority, BehaviorCatalog.Dragging);
            }
            if (_dragging)
            {
                var area = DisplayServer.ScreenGetUsableRect((int)DisplayServer.ScreenWithMouseFocus);
                var position = ClampPet(_dragOrigin + offset, _window.Size, area, _properties.Visible);
                if (position != _window.Position)
                {
                    _window.Position = position;
                    FollowPetWindows();
                }
            }
        }

        var passThrough = !_pressed && !_pet.ContainsPointer(cursor - _window.Position);
        if (_window.MousePassthrough != passThrough)
            _window.MousePassthrough = passThrough;
    }

    // ---- IPetBody：给自主活动用 ----

    Rect2I IPetBody.Bounds => new(_window.Position, _window.Size);

    Rect2I IPetBody.WorkArea => ScreenArea();

    Rect2I IPetBody.ScreenRect => new(DisplayServer.ScreenGetPosition(_window.CurrentScreen), DisplayServer.ScreenGetSize(_window.CurrentScreen));

    void IPetBody.MoveTo(Vector2I position)
    {
        _window.Position = ClampPet(position, _window.Size, ScreenArea(), _properties.Visible);
        FollowPetWindows();
    }

    void IPetBody.SetMood(string mood) => _pet.SetMood(mood);

    private bool IsUserBusy() => _pressed || _chat.Visible || _properties.Visible || _settingsWindow.Visible || _interactions.IsBusy;

    // ---- 交互事件 → 气泡、表情、动作 ----

    private void OnInteraction(InteractionEvent change)
    {
        switch (change)
        {
            case InteractionStarted started:
                _speaking = started.InteractionId;
                _replying = false;
                _activities.Interrupt();
                _speech.Begin();
                _pet.SetMood("neutral");
                _motions.Hold(InteractionLayer, PetMotionDirector.InteractionPriority, BehaviorCatalog.WaitingForReply);
                break;
            case ReplyUpdated updated when updated.InteractionId == _speaking:
                if (!_replying)
                {
                    _replying = true;
                    _motions.Hold(InteractionLayer, PetMotionDirector.InteractionPriority, BehaviorCatalog.Replying);
                }
                _speech.SetText(updated.Text);
                break;
            case DirectiveReceived { Name: "emotion" } directive when directive.InteractionId == _speaking:
                var emotion = directive.Value.ToLowerInvariant();
                if (_pet.EmotionNames.Contains(emotion, StringComparer.OrdinalIgnoreCase) && _pet.Mood != emotion)
                    _pet.SetMood(emotion);
                break;
            case InteractionEnded ended when ended.InteractionId == _speaking:
                _speaking = null;
                _speech.SetText(ReplyText.ForEnded(ended));
                _speech.FadeAfterReply();
                _motions.Release(InteractionLayer);
                if (ended.Outcome == InteractionOutcome.Completed)
                    _motions.PlayOnce(BehaviorCatalog.ReplyFinished);
                else
                    _motions.ClearOneShot();
                break;
        }
    }

    private string DescribeChatRoute()
    {
        var endpoint = _settings.Current.EndpointFor(ModelTask.Chat);
        return endpoint.NeedsConnection ? $"{endpoint.Kind}:{endpoint.Model}" : endpoint.Kind.ToString();
    }

    // ---- 鼠标和菜单 ----

    private void OnPetInput(InputEvent input)
    {
        if (input is not InputEventMouseButton button)
            return;

        if (button.ButtonIndex == MouseButton.Right && !button.Pressed)
        {
            _singleClick.Stop();
            var position = _headless ? Vector2I.Zero : DisplayServer.MouseGetPosition();
            _menu.Popup(new Rect2I(position, new Vector2I(152, 0)));
        }
        else if (button.ButtonIndex == MouseButton.Left && button.Pressed)
        {
            _pressed = true;
            _dragging = false;
            _doubleClick = button.DoubleClick;
            if (_doubleClick)
                _singleClick.Stop();
            _pressPosition = DisplayServer.MouseGetPosition();
            _dragOrigin = _window.Position;
        }
        else if (button.ButtonIndex == MouseButton.Left && _pressed)
        {
            _pressed = false;
            if (_dragging)
            {
                _dragging = false;
                _motions.Release(DragLayer);
            }
            else if (_doubleClick || button.DoubleClick)
            {
                _singleClick.Stop();
                ToggleProperties();
            }
            else if (!button.Canceled)
                _singleClick.Start();
        }
    }

    private void OnMenuItem(long id)
    {
        switch (id)
        {
            case Chat:
                OpenChat();
                break;
            case Settings:
                _chat.Dismiss();
                _activities.Interrupt();
                _settingsWindow.Open(ScreenArea());
                break;
            case Clear:
                ClearConversation();
                break;
            case Quit:
                GetTree().Quit();
                break;
        }
    }

    private void OnUserAction(string reaction)
    {
        _activities.Interrupt();
        _motions.PlayOnce(reaction);
    }

    private void OpenChat()
    {
        FollowPetWindows();
        _chat.Open();
    }

    private void ClearConversation()
    {
        _interactions.ClearConversation();
        _chat.ClearTranscript();
        _speech.Dismiss();
        _pet.SetMood("neutral");
        _motions.Release(InteractionLayer);
        _motions.ClearOneShot();
    }

    private void ToggleProperties()
    {
        if (_properties.Visible)
        {
            _properties.Dismiss();
            return;
        }

        _activities.Interrupt();
        _window.Position = ClampPet(_window.Position, _window.Size, ScreenArea(), withProperties: true);
        FollowPetWindows();
        _properties.Open();
    }

    // ---- 窗口布局 ----

    private void ResizePet()
    {
        var next = (Vector2I)((Vector2)_baseSize * (_properties.Appearance.SizePercent / 100f)).Round();
        if (_window.Size != next)
        {
            var delta = _window.Size - next;
            var area = ScreenArea();
            var anchor = _window.Position + new Vector2I(delta.X / 2, delta.Y);
            if (_properties.Visible)
                anchor.X = _properties.Position.X - PetPropertiesWindow.RightOffset(next.X);
            _window.Size = next;
            _window.Position = ClampPet(anchor, next, area, _properties.Visible);
            FollowPetWindows(followProperties: false);
        }
        _motions.Refresh();
    }

    private void FollowPetWindows(bool followProperties = true)
    {
        var petBounds = new Rect2I(_window.Position, _window.Size);
        var area = ScreenArea();
        _chat.Follow(petBounds, area);
        _speech.Follow(petBounds, area);
        if (followProperties)
            _properties.Follow(petBounds, area);
    }

    private Rect2I ScreenArea() => DisplayServer.ScreenGetUsableRect(_window.CurrentScreen);

    private static Vector2I ClampPet(Vector2I position, Vector2I size, Rect2I area, bool withProperties)
    {
        var next = ScreenMath.ClampToScreen(position, size, area);
        if (!withProperties || !area.HasArea())
            return next;

        var right = PetPropertiesWindow.RightExtent(size.X);
        if (right > area.Size.X)
            return next;
        var left = Math.Min(ChatWindow.LeftExtent(size.X), area.Size.X - right);
        return new Vector2I(Math.Clamp(next.X, area.Position.X + left, area.End.X - right), next.Y);
    }
}
