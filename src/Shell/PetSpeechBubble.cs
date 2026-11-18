using Godot;

namespace JustNeko.Shell;

public partial class PetSpeechBubble : Window
{
    private const int BubbleWidth = 274;
    private const int TailHeight = 9;
    private const double StaySeconds = 5.0;
    private const double FadeSeconds = 0.65;
    private Control _surface = null!;
    private Polygon2D _tail = null!;
    private RichTextLabel _text = null!;
    private Tween? _fade;
    private Rect2I _petBounds;
    private Rect2I _screenArea;
    private Rect2I _avoid;
    private bool _pendingLayout;

    public PetSpeechBubble()
    {
        Title = "Neko 回复";
        Visible = false;
        ForceNative = true;
        Transparent = true;
        TransparentBg = true;
        Borderless = true;
        Unfocusable = true;
        MousePassthrough = true;
        Unresizable = true;
        AlwaysOnTop = true;
        Size = new Vector2I(BubbleWidth, 50);
        Theme = PetTheme.Create();
    }

    public override void _Ready()
    {
        CloseRequested += Dismiss;
        _surface = new Control { Name = "BubbleSurface", MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_surface);
        _surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Surface, 15, 13, 9));
        _surface.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.OffsetBottom = -TailHeight;
        _text = new RichTextLabel
        {
            Name = "ReplyBubble",
            ScrollFollowing = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 23),
        };
        _text.AddThemeColorOverride("default_color", PetTheme.Ink);
        panel.AddChild(_text);

        _tail = new Polygon2D
        {
            Color = PetTheme.Surface,
            Polygon = [new(-8, -1), new(8, -1), new(0, TailHeight)],
            Antialiased = true,
        };
        _surface.AddChild(_tail);
    }

    internal void Follow(Rect2I petBounds, Rect2I screenArea)
    {
        _petBounds = petBounds;
        _screenArea = screenArea;
        Place();
    }

    /// <summary>要避开的区域（展开的聊天记录）。头顶放不下时挪到角色右侧。</summary>
    internal void Avoid(Rect2I area)
    {
        if (_avoid == area)
            return;
        _avoid = area;
        if (Visible)
            Place();
    }

    internal void Begin()
    {
        _fade?.Kill();
        _fade = null;
        _surface.Modulate = Colors.White;
        Size = new Vector2I(BubbleWidth, 50);
        Place();
        Show();
        SetText("…");
    }

    internal void SetText(string text)
    {
        _text.Text = text;
        if (!Visible || _pendingLayout)
            return;
        _pendingLayout = true;
        CallDeferred(MethodName.RefreshLayout);
    }

    internal void FadeAfterReply()
    {
        if (!Visible)
            return;
        _fade = CreateTween();
        _fade.TweenInterval(StaySeconds);
        _fade.TweenProperty(_surface, "modulate:a", 0.0f, FadeSeconds).SetTrans(Tween.TransitionType.Sine);
        _fade.TweenCallback(Callable.From(() =>
        {
            _fade = null;
            Hide();
        }));
    }

    internal void Dismiss()
    {
        _fade?.Kill();
        _fade = null;
        Hide();
    }

    private void RefreshLayout()
    {
        _pendingLayout = false;
        if (!Visible)
            return;
        var height = Math.Clamp(_text.GetContentHeight(), 23, 172);
        var size = new Vector2I(BubbleWidth, height + 18 + TailHeight);
        if (size != Size)
            Size = size;
        Place();
    }

    // 依次试：头顶 → 角色右侧 → 聊天记录左侧 → 聊天记录上方 → 角色下方，用第一个完整在屏幕内、又不压住聊天记录的位置。
    private void Place()
    {
        var head = new Vector2I(_petBounds.GetCenter().X, _petBounds.Position.Y + _petBounds.Size.Y / 16);
        var above = new Vector2I(head.X - Size.X / 2, head.Y - Size.Y - 5);
        Vector2I[] candidates =
        [
            above,
            new(_petBounds.End.X + 5, head.Y),
            new(_avoid.Position.X - Size.X - 5, head.Y),
            new(_avoid.Position.X + (_avoid.Size.X - Size.X) / 2, _avoid.Position.Y - Size.Y - 5),
            new(head.X - Size.X / 2, _petBounds.End.Y + 5),
        ];
        var next = candidates.FirstOrDefault(Fits, candidates[_avoid.HasArea() ? 1 : 0]);
        next = ScreenMath.ClampToScreen(next, Size, _screenArea);
        if (next != Position)
            Position = next;
        _tail.Visible = next == above;
        _tail.Position = new Vector2(Math.Clamp(head.X - next.X, 16, BubbleWidth - 16), Size.Y - TailHeight);
    }

    private bool Fits(Vector2I position)
    {
        var bounds = new Rect2I(position, Size);
        var onScreen = !_screenArea.HasArea() || _screenArea.Encloses(bounds);
        return onScreen && !(_avoid.HasArea() && bounds.Intersects(_avoid));
    }
}
