using System.Runtime.InteropServices;
using Godot;
using Timer = Godot.Timer;

namespace JustNeko;

public partial class DesktopPet : Control
{
    private const int Chat = 0;
    private const int Settings = 1;
    private const int Clear = 2;
    private const int Quit = 3;

    private Window _window = null!;
    private PetPreview _pet = null!;
    private ChatWindow _chat = null!;
    private PetSpeechBubble _speech = null!;
    private SettingsWindow _settings = null!;
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
    private PetBehavior _chatPhase;
    private PetBehavior? _oneShot;
    private (PetBehavior Behavior, string? Key)? _playing;

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    public override void _Ready()
    {
        Engine.MaxFps = 30;
        _headless = DisplayServer.GetName() == "headless";
        _window = GetWindow();
        _baseSize = _window.Size;
        _window.Unresizable = true;
        _window.CloseRequested += () => GetTree().Quit();

        _pet = new PetPreview { Name = "Pet", MouseFilter = MouseFilterEnum.Stop };
        AddChild(_pet);
        _pet.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _pet.GuiInput += OnPetInput;
        _pet.MotionFinished += OnMotionFinished;

        _settings = new SettingsWindow { Name = "PetSettings" };
        AddChild(_settings);
        _properties = new PetPropertiesWindow(_pet.Motions) { Name = "PetProperties" };
        _properties.AppearanceChanged += ResizePet;
        AddChild(_properties);
        _speech = new PetSpeechBubble { Name = "PetReply" };
        AddChild(_speech);
        _chat = new ChatWindow(_pet, _settings, _speech) { Name = "PetChat" };
        _chat.BehaviorChanged += OnChatBehavior;
        AddChild(_chat);

        _menu = new PopupMenu { Name = "PetMenu", ForceNative = true, Theme = PetTheme.Create() };
        _menu.AddItem("聊天", Chat);
        _menu.AddItem("设置…", Settings);
        _menu.AddItem("清空对话", Clear);
        _menu.AddSeparator();
        _menu.AddItem("退出", Quit);
        _menu.IdPressed += OnMenuItem;
        AddChild(_menu);

        _singleClick = new Timer
        {
            OneShot = true,
            WaitTime = OperatingSystem.IsWindows() ? Math.Max(0.1, GetDoubleClickTime() / 1000.0) : 0.5,
        };
        _singleClick.Timeout += OpenChat;
        AddChild(_singleClick);

        ResizePet();
        if (!_headless)
        {
            var area = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
            _window.Position = area.End - _window.Size - new Vector2I(24, 12);
        }
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
                _oneShot = null;
                ApplyMotion();
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
                ApplyMotion();
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
        ApplyMotion();
    }

    private void OnChatBehavior(PetBehavior behavior)
    {
        if (behavior is PetBehavior.WaitingForReply or PetBehavior.Replying)
        {
            _chatPhase = behavior;
            _oneShot = null;
        }
        else
        {
            if (behavior is PetBehavior.Idle or PetBehavior.ReplyFinished)
                _chatPhase = PetBehavior.Idle;
            _oneShot = behavior != PetBehavior.Idle && GetMotion(behavior) is not null ? behavior : null;
        }
        ApplyMotion();
    }

    private void OnMotionFinished()
    {
        if (_oneShot is null || _playing?.Behavior != _oneShot)
            return;
        _oneShot = null;
        ApplyMotion();
    }

    private void ApplyMotion()
    {
        var behavior = _dragging && GetMotion(PetBehavior.Dragging) is not null
            ? PetBehavior.Dragging : _oneShot ?? _chatPhase;
        var motion = GetMotion(behavior);
        if (motion is null)
        {
            behavior = PetBehavior.Idle;
            motion = GetMotion(behavior);
        }
        var next = (behavior, motion);
        if (_playing == next)
            return;
        _playing = next;
        var slot = PetAppearance.Slots.First(slot => slot.Behavior == behavior);
        _pet.PlayMotion(motion, slot.Loop);
    }

    private string? GetMotion(PetBehavior behavior)
    {
        var key = _properties.Appearance.Motions.GetValueOrDefault(behavior);
        return key is not null && _pet.HasMotion(key) ? key : null;
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
                _settings.Open(ScreenArea());
                break;
            case Clear:
                _chat.ClearConversation();
                break;
            case Quit:
                GetTree().Quit();
                break;
        }
    }

    private void OpenChat()
    {
        FollowPetWindows();
        _chat.Open();
    }

    private void ToggleProperties()
    {
        if (_properties.Visible)
        {
            _properties.Dismiss();
            return;
        }

        _window.Position = ClampPet(_window.Position, _window.Size, ScreenArea(), withProperties: true);
        FollowPetWindows();
        _properties.Open();
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
        var next = ClampToScreen(position, size, area);
        if (!withProperties || !area.HasArea())
            return next;

        var right = PetPropertiesWindow.RightExtent(size.X);
        if (right > area.Size.X)
            return next;
        var left = Math.Min(ChatWindow.LeftExtent(size.X), area.Size.X - right);
        return new Vector2I(Math.Clamp(next.X, area.Position.X + left, area.End.X - right), next.Y);
    }

    internal static Vector2I ClampToScreen(Vector2I position, Vector2I size, Rect2I area)
    {
        if (!area.HasArea())
            return position;
        var limit = (area.End - size).Max(area.Position);
        return position.Clamp(area.Position, limit);
    }
}
