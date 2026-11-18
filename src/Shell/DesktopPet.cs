using Godot;
using JustNeko.Core.Ai;
using JustNeko.Core.Interaction;
using JustNeko.Core.LocalModel;
using JustNeko.Core.Platform;
using JustNeko.Core.Settings;
using JustNeko.Pet;
using JustNeko.Pet.Activities;
using JustNeko.Pet.Rig;
using JustNeko.Shell.Settings;
using Timer = Godot.Timer;

namespace JustNeko.Shell;

/// <summary>
/// 主场景：组装各个模块，处理角色上的鼠标操作，并让聊天、气泡跟着角色走。
/// 角色只管互动（单击摸头、双击聊天、右键互动菜单）；设置类的东西都在托盘菜单和设置中心里。
/// AI 流程在 <see cref="InteractionOrchestrator"/>，动作选择在 <see cref="PetMotionDirector"/>，
/// 自主活动在 <see cref="ActivityDirector"/>。
/// </summary>
public partial class DesktopPet : Control, IPetBody
{
    private const int Chat = 0;
    private const int Pat = 1;
    private const int Clear = 2;
    private const int HideToTray = 3;
    private const string DragLayer = "drag";
    private const string InteractionLayer = "interaction";
    private const double MoodSeconds = 2.5;

    private PetServices _services = null!;
    private SettingsService _settings = null!;
    private InteractionOrchestrator _interactions = null!;
    private PetMotionDirector _motions = null!;
    private Window _window = null!;
    private PetAvatar _pet = null!;
    private PetBadge _badge = null!;
    private ChatWindow _chat = null!;
    private PetSpeechBubble _speech = null!;
    private SettingsCenter _settingsCenter = null!;
    private OnboardingWindow _onboarding = null!;
    private TrayIcon _tray = null!;
    private PopupMenu _menu = null!;
    private bool _headless;
    private bool _pressed;
    private bool _dragging;
    private bool _doubleClick;
    private bool _hiddenByUser;
    private bool _hiddenForFullscreen;
    private bool _shown = true;
    private Vector2I _shownPosition;
    private Vector2I _baseSize;
    private Vector2I _pressPosition;
    private Vector2I _dragOrigin;
    private long? _speaking;
    private bool _replying;
    private long _moodVersion;
    private readonly LookAtCursor _look = new();
    private readonly DragSway _sway = new();
    private readonly TextMouth _mouth = new();
    private int _spokenLength;

    public override void _Ready()
    {
        Engine.MaxFps = 30;
        _headless = DisplayServer.GetName() == "headless";
        _window = GetWindow();
        _baseSize = _window.Size;
        _window.Unresizable = true;
        _window.CloseRequested += Quit;

        var dataDirectory = ProjectSettings.GlobalizePath("user://");
        var platform = PlatformServices.Create(dataDirectory);
        _settings = new SettingsService(dataDirectory, platform.Secrets);
        _settings.Load();
        var localModel = new LocalModelManager(platform, Path.Combine(dataDirectory, "logs"));
        var models = new ModelRouter(_settings, localModel);

        _pet = new PetAvatar { Name = "Pet", MouseFilter = MouseFilterEnum.Stop };
        AddChild(_pet);
        _pet.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _pet.GuiInput += OnPetInput;
        _badge = new PetBadge();
        AddChild(_badge);
        _badge.Pressed += OpenProblemSettings;

        _interactions = new InteractionOrchestrator(models, () => _settings.Current.RolePrompt, () => _pet.EmotionNames.ToArray())
        {
            QuietMode = _settings.Current.QuietMode,
        };
        _interactions.Changed += OnInteraction;
        _ = new InteractionLog(Path.Combine(dataDirectory, "logs"), _interactions, DescribeChatRoute);
        var history = new ChatHistoryStore(dataDirectory, _interactions, _settings.Current.SaveChatHistory);
        _interactions.RestoreHistory(history.CompletedTurns(8));

        // 行为槽要在设置中心创建前登记完，活动登记的槽才会出现在「角色 → 动作绑定」里。
        var behaviors = new BehaviorCatalog();
        var activities = new ActivityDirector { Name = "PetActivities" };
        var appearance = new AppearanceStore { Name = "Appearance" };
        appearance.Initialize(behaviors);
        AddChild(appearance);
        appearance.Changed += ResizePet;
        _motions = new PetMotionDirector(_pet, behaviors, () => appearance.Current);
        _pet.MotionFinished += _motions.OnMotionFinished;
        activities.Initialize(new PetContext(this, _motions), IsUserBusy);
        _services = new PetServices(platform, _settings, localModel, models, _interactions, history, behaviors, appearance, activities, _pet, dataDirectory);

        _speech = new PetSpeechBubble { Name = "PetReply" };
        AddChild(_speech);
        _chat = new ChatWindow(_interactions) { Name = "PetChat" };
        _chat.Opened += OnChatOpened;
        _chat.Closed += OnChatClosed;
        _chat.LayoutChanged += () => _speech.Avoid(_chat.Visible ? _chat.Bounds : default);
        AddChild(_chat);
        _chat.LoadHistory(history.Entries);
        AddChild(activities);

        _settingsCenter = new SettingsCenter(_services);
        _settingsCenter.ChatPage.ConnectionVerified += OnConnectionVerified;
        _settingsCenter.PrivacyPage.HistoryCleared += ClearConversation;
        AddChild(_settingsCenter);
        _onboarding = new OnboardingWindow(_services);
        _onboarding.CloudRequested += () => _settingsCenter.Open(SettingsCenter.Chat);
        AddChild(_onboarding);

        _menu = new PopupMenu { Name = "PetMenu", ForceNative = true, Theme = PetTheme.Create() };
        _menu.AddItem("聊天", Chat);
        _menu.AddItem("摸摸头", Pat);
        _menu.AddItem("清空对话", Clear);
        _menu.AddSeparator();
        _menu.AddItem("藏到托盘", HideToTray);
        _menu.IdPressed += OnMenuItem;
        AddChild(_menu);

        _tray = new TrayIcon();
        AddChild(_tray);
        _tray.TogglePetRequested += () => SetHiddenByUser(!_hiddenByUser);
        _tray.QuietToggled += () => _settings.Update(_settings.Current with { QuietMode = !_settings.Current.QuietMode });
        _tray.SettingsRequested += page => _settingsCenter.Open(page);
        _tray.QuitRequested += Quit;
        _settings.Changed += OnSettingsChanged;
        localModel.StateChanged += OnLocalModelState;

        var fullscreenCheck = new Timer { Name = "FullscreenCheck", WaitTime = 1.5, Autostart = true };
        fullscreenCheck.Timeout += CheckFullscreen;
        AddChild(fullscreenCheck);

        foreach (var window in new[] { _window, _chat, _speech })
            TaskbarHider.Attach(window, platform);

        ResizePet();
        if (!_headless)
        {
            var area = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
            _window.Position = area.End - _window.Size - new Vector2I(24, 12);
        }
        RefreshTray();
        if (!_settings.Current.OnboardingDone)
            Callable.From(_onboarding.Start).CallDeferred();
    }

    public override void _ExitTree()
    {
        _interactions.Cancel();
        _services.Models.Dispose();
        _services.LocalModel.Dispose();
    }

    public override void _Process(double delta)
    {
        if (_headless || !_shown)
            return;
        UpdateRig(delta);

        var cursor = DisplayServer.MouseGetPosition();
        if (_pressed)
        {
            var offset = cursor - _pressPosition;
            if (!_dragging && offset.LengthSquared() >= 36)
            {
                _dragging = true;
                _services.Activities.Interrupt();
                _motions.Hold(DragLayer, PetMotionDirector.DragPriority, BehaviorCatalog.Dragging);
            }
            if (_dragging)
            {
                var area = DisplayServer.ScreenGetUsableRect((int)DisplayServer.ScreenWithMouseFocus);
                var position = ScreenMath.ClampToScreen(_dragOrigin + offset, _window.Size, area);
                if (position != _window.Position)
                {
                    _window.Position = position;
                    FollowPetWindows();
                }
            }
        }

        var local = cursor - _window.Position;
        var passThrough = !_pressed && !_pet.ContainsPointer(local) && !(_badge.Visible && _badge.GetRect().HasPoint(local));
        if (_window.MousePassthrough != passThrough)
            _window.MousePassthrough = passThrough;
    }

    // ---- 通用层：看鼠标、拖动晃动、对口型，和角色格式无关 ----

    private void UpdateRig(double delta)
    {
        var bounds = new Rect2I(_window.Position, _window.Size);
        var head = new Vector2(bounds.GetCenter().X, bounds.Position.Y + bounds.Size.Y * 0.28f);
        var look = _look.Update(DisplayServer.MouseGetPosition(), head, enabled: !_dragging, delta);
        var lean = _sway.Update(_window.Position, delta);
        ((IAvatarRig)_pet).SetPose(new AvatarPose(look, lean, _mouth.Sample(delta)));
    }

    // ---- IPetBody：给自主活动用 ----

    Rect2I IPetBody.Bounds => new(_window.Position, _window.Size);

    Rect2I IPetBody.WorkArea => ScreenArea();

    Rect2I IPetBody.ScreenRect => new(DisplayServer.ScreenGetPosition(_window.CurrentScreen), DisplayServer.ScreenGetSize(_window.CurrentScreen));

    void IPetBody.MoveTo(Vector2I position)
    {
        _window.Position = ScreenMath.ClampToScreen(position, _window.Size, ScreenArea());
        FollowPetWindows();
    }

    void IPetBody.SetMood(string mood) => _pet.SetMood(mood);

    private bool IsUserBusy() => _pressed || _chat.Visible || _settingsCenter.Visible || _onboarding.Visible
                                 || _interactions.IsBusy || _settings.Current.QuietMode || !_shown;

    // ---- 交互事件 → 气泡、表情、动作、小标记 ----

    private void OnInteraction(InteractionEvent change)
    {
        switch (change)
        {
            case InteractionStarted started:
                _speaking = started.InteractionId;
                _replying = false;
                _mouth.Stop();
                _spokenLength = 0;
                _moodVersion++;
                _services.Activities.Interrupt();
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
                if (updated.Text.Length > _spokenLength)
                    _mouth.Feed(updated.Text[_spokenLength..]);
                _spokenLength = updated.Text.Length;
                break;
            case DirectiveReceived { Name: "emotion" } directive when directive.InteractionId == _speaking:
                var emotion = directive.Value.ToLowerInvariant();
                if (_pet.EmotionNames.Contains(emotion, StringComparer.OrdinalIgnoreCase) && _pet.Mood != emotion)
                    _pet.SetMood(emotion);
                break;
            case InteractionEnded ended when ended.InteractionId == _speaking:
                _speaking = null;
                if (ended.Outcome == InteractionOutcome.Completed)
                    _mouth.Finish();
                else
                    _mouth.Stop();
                _speech.SetText(ReplyText.ForEnded(ended));
                _speech.FadeAfterReply();
                _motions.Release(InteractionLayer);
                if (ended.Outcome == InteractionOutcome.Completed)
                {
                    _badge.Clear();
                    _motions.PlayOnce(BehaviorCatalog.ReplyFinished);
                }
                else
                {
                    if (ended.Outcome is InteractionOutcome.Failed or InteractionOutcome.TimedOut)
                        _badge.ShowProblem(ended.Error ?? "模型没有回复。");
                    _motions.ClearOneShot();
                }
                break;
        }
    }

    // 回复始终在头顶气泡里说，聊天窗口只是输入框（和可选的记录）。
    private void OnChatOpened() => OnUserAction(BehaviorCatalog.ChatOpened);

    private void OnChatClosed()
    {
        _speech.Avoid(default);
        _motions.PlayOnce(BehaviorCatalog.ChatClosed);
    }

    private void OnConnectionVerified()
    {
        _badge.Clear();
        ShowMood("happy");
    }

    private void OpenProblemSettings()
    {
        var builtInMissing = _settings.Current.ChatProvider == ProviderKind.BuiltIn && !_services.LocalModel.IsInstalled;
        _settingsCenter.Open(builtInMissing ? SettingsCenter.LocalModel : SettingsCenter.Chat);
    }

    private void OnSettingsChanged()
    {
        _interactions.QuietMode = _settings.Current.QuietMode;
        if (_settings.Current.QuietMode)
            _services.Activities.Interrupt();
        CheckFullscreen();
        RefreshTray();
    }

    private void OnLocalModelState(LocalModelState state)
    {
        RefreshTray();
        if (_settings.Current.ChatProvider == ProviderKind.BuiltIn && state.Status is LocalModelStatus.Running or LocalModelStatus.Installed)
            _badge.Clear();
    }

    private string DescribeChatRoute()
    {
        var endpoint = _settings.Current.EndpointFor(ModelTask.Chat);
        return endpoint.IsExternal ? $"{endpoint.Kind}:{endpoint.Model}" : endpoint.Kind.ToString();
    }

    // ---- 鼠标和菜单 ----

    private void OnPetInput(InputEvent input)
    {
        if (input is not InputEventMouseButton button)
            return;

        if (button.ButtonIndex == MouseButton.Right && !button.Pressed)
        {
            var position = _headless ? Vector2I.Zero : DisplayServer.MouseGetPosition();
            _menu.Popup(new Rect2I(position, new Vector2I(132, 0)));
        }
        else if (button.ButtonIndex == MouseButton.Left && button.Pressed)
        {
            _pressed = true;
            _dragging = false;
            _doubleClick = button.DoubleClick;
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
                OpenChat();
            else if (!button.Canceled)
                PatHead();
        }
    }

    private void OnMenuItem(long id)
    {
        switch (id)
        {
            case Chat:
                OpenChat();
                break;
            case Pat:
                PatHead();
                break;
            case Clear:
                ClearConversation();
                break;
            case HideToTray:
                SetHiddenByUser(true);
                break;
        }
    }

    private void OnUserAction(string reaction)
    {
        _services.Activities.Interrupt();
        _motions.PlayOnce(reaction);
    }

    // 单击立即摸头、不等双击判定；双击时第一下的摸头动作会被「打开聊天」接上。
    private void OpenChat()
    {
        FollowPetWindows();
        _chat.Open();
    }

    private void PatHead()
    {
        OnUserAction(BehaviorCatalog.Petted);
        if (_speaking is null)
            ShowMood("happy");
    }

    private void ShowMood(string mood)
    {
        var version = ++_moodVersion;
        _pet.SetMood(mood);
        GetTree().CreateTimer(MoodSeconds).Timeout += () =>
        {
            if (version == _moodVersion && _speaking is null && IsInsideTree())
                _pet.SetMood("neutral");
        };
    }

    private void ClearConversation()
    {
        _mouth.Stop();
        _interactions.ClearConversation();
        _services.ChatHistory.Clear();
        _chat.ClearTranscript();
        _speech.Dismiss();
        _pet.SetMood("neutral");
        _motions.Release(InteractionLayer);
        _motions.ClearOneShot();
    }

    private void Quit()
    {
        _services.LocalModel.Dispose();
        GetTree().Quit();
    }

    // ---- 显示与隐藏 ----

    private void SetHiddenByUser(bool hidden)
    {
        _hiddenByUser = hidden;
        ApplyVisibility();
    }

    private void CheckFullscreen()
    {
        var hide = _settings.Current.HideWhenFullscreen && _services.Platform.IsFullscreenAppActive();
        if (hide == _hiddenForFullscreen)
            return;
        _hiddenForFullscreen = hide;
        ApplyVisibility();
    }

    // Godot 不允许隐藏主窗口，交给系统隐藏；不支持的平台就挪到屏幕外。藏起来时降到 5 帧省资源。
    private void ApplyVisibility()
    {
        var shown = !_hiddenByUser && !_hiddenForFullscreen;
        if (_shown != shown)
        {
            _shown = shown;
            if (!shown)
            {
                _pressed = false;
                _dragging = false;
                _motions.Release(DragLayer);
                _chat.Dismiss();
                _speech.Dismiss();
                _services.Activities.Interrupt();
            }
            var handle = _headless ? 0 : DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, _window.GetWindowId());
            if (!_services.Platform.SetWindowShown(handle, shown))
            {
                if (!shown)
                    _shownPosition = _window.Position;
                _window.Position = shown ? _shownPosition : new Vector2I(-32000, -32000);
            }
            _pet.Visible = shown;
            _window.MousePassthrough = !shown;
            Engine.MaxFps = shown ? 30 : 5;
            if (shown)
                FollowPetWindows();
        }
        RefreshTray();
    }

    private void RefreshTray() => _tray.Refresh(!_hiddenByUser, _settings.Current.QuietMode, _services.LocalModel.State);

    // ---- 窗口布局 ----

    private void ResizePet()
    {
        var next = (Vector2I)((Vector2)_baseSize * (_services.Appearance.Current.SizePercent / 100f)).Round();
        if (_window.Size != next)
        {
            // 以脚底中点为锚点缩放，角色不会「跳」。
            var delta = _window.Size - next;
            var anchor = _window.Position + new Vector2I(delta.X / 2, delta.Y);
            _window.Size = next;
            _window.Position = ScreenMath.ClampToScreen(anchor, next, ScreenArea());
            FollowPetWindows();
        }
        _badge.Position = new Vector2(next.X * 0.70f, next.Y * 0.10f);
        _motions.Refresh();
    }

    private void FollowPetWindows()
    {
        var petBounds = new Rect2I(_window.Position, _window.Size);
        var area = ScreenArea();
        _chat.Follow(petBounds, area);
        _speech.Follow(petBounds, area);
    }

    private Rect2I ScreenArea() => DisplayServer.ScreenGetUsableRect(_window.CurrentScreen);
}
