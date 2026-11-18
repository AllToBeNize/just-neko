using Godot;
using JustNeko.Pet.Rig;
using FileAccess = Godot.FileAccess;

namespace JustNeko.Pet;

/// <summary>Live2D 角色：优先用 Live2D 模型渲染，扩展或模型不可用时画内置的预览猫。</summary>
public partial class Live2DAvatar : PetAvatar
{
    private const int RenderScale = 2;
    private const int MotionPriority = 3;
    private static readonly string[] FallbackEmotions = ["neutral", "curious", "happy", "sleepy"];
    private static readonly Color Fur = new("fac383");
    private static readonly Color Ear = new("ce866e");
    private static readonly Color Face = new("342529");
    private static readonly Color Blush = new("ed9e86");
    private readonly Dictionary<string, string> _expressions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PetMotion> _motions = new(StringComparer.Ordinal);
    private SubViewport? _renderViewport;
    private Node2D? _cubism;
    private int _motionVersion;
    private Live2DRig? _rig;
    private AvatarPose _pose = AvatarPose.Rest;

    internal override IEnumerable<string> EmotionNames => _cubism is null ? FallbackEmotions : _expressions.Keys.Prepend("neutral");
    internal override IReadOnlyCollection<PetMotion> Motions => _motions.Values;

    public override bool ContainsPointer(Vector2 position)
    {
        var relative = position / Size;
        if (_cubism is not null)
            return new Rect2(0.12f, 0.06f, 0.76f, 0.88f).HasPoint(relative);

        var point = (position - Size / 2) / PreviewScale;
        return new Rect2(-48, -57, 96, 95).HasPoint(point);
    }

    public override void _Ready()
    {
        const string path = "res://Models/neko.model3.json";
        if (!ClassDB.ClassExists("GDCubismUserModel") || !FileAccess.FileExists(path))
            return;
        if (ClassDB.Instantiate("GDCubismUserModel").AsGodotObject() is not Node2D cubism)
            return;

        _renderViewport = new SubViewport
        {
            Name = "PetRenderViewport",
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_renderViewport);
        _renderViewport.AddChild(cubism);
        cubism.Set("assets", path);
        if (cubism.Call("get_canvas_info").AsGodotDictionary().Count == 0)
        {
            _renderViewport.QueueFree();
            _renderViewport = null;
            return;
        }

        _cubism = cubism;
        var surface = new TextureRect
        {
            Texture = _renderViewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TextureFilter = TextureFilterEnum.Linear,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha },
        };
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ReadExpressions(cubism);
        ReadMotions(path);
        _rig = Live2DRig.Attach(cubism, path);
        if (cubism.HasSignal("motion_finished"))
            cubism.Connect("motion_finished", Callable.From(OnNativeMotionFinished));
        Resized += FitCubism;
        FitCubism();
        QueueRedraw();
    }

    private void ReadMotions(string path)
    {
        var model = Json.ParseString(FileAccess.GetFileAsString(path)).AsGodotDictionary();
        var references = model["FileReferences"].AsGodotDictionary();
        if (!references.TryGetValue("Motions", out var entries))
            return;
        foreach (var group in entries.AsGodotDictionary())
        {
            var motions = group.Value.AsGodotArray();
            for (var index = 0; index < motions.Count; index++)
            {
                var file = motions[index].AsGodotDictionary()["File"].AsString();
                if (file.Length == 0 || !FileAccess.FileExists(path.GetBaseDir().PathJoin(file)))
                    continue;
                var motion = new PetMotion(group.Key.AsString(), index, file);
                _motions.TryAdd(motion.Key, motion);
            }
        }
    }

    internal override bool HasMotion(string key) => _motions.ContainsKey(key);

    /// <summary>通用层（看鼠标、拖动晃动、对口型）算出的姿态，叠加在动作上。</summary>
    internal override void SetPose(AvatarPose pose)
    {
        if (_rig is not null)
        {
            _rig.SetPose(pose);
            return;
        }
        // 没有 Live2D 时，预览猫的眼睛跟着看。
        if (pose.Look.DistanceSquaredTo(_pose.Look) > 1e-4f)
        {
            _pose = pose;
            QueueRedraw();
        }
    }

    internal IEnumerable<string> RigBindings => _rig?.BoundParameters ?? [];

    internal override void PlayMotion(string? key, bool loop)
    {
        if (_cubism is null)
            return;
        _motionVersion++;
        _cubism.Call("stop_motion");
        if (key is not null && _motions.TryGetValue(key, out var motion))
            _cubism.Call("start_motion_loop", motion.Group, motion.Index, MotionPriority, loop, true);
    }

    private void OnNativeMotionFinished()
    {
        var version = _motionVersion;
        Callable.From(() =>
        {
            if (IsInsideTree() && version == _motionVersion)
                RaiseMotionFinished();
        }).CallDeferred();
    }

    private void ReadExpressions(Node2D cubism)
    {
        foreach (var item in cubism.Call("get_expressions").AsGodotArray())
        {
            var name = item.AsString();
            if (name.Length > 0 && !name.Equals("neutral", StringComparison.OrdinalIgnoreCase))
                _expressions.TryAdd(name, name);
        }

        if (FileAccess.FileExists("res://Models/expressions.json"))
        {
            var data = Json.ParseString(FileAccess.GetFileAsString("res://Models/expressions.json"));
            if (data.VariantType == Variant.Type.Dictionary)
            {
                foreach (var pair in data.AsGodotDictionary())
                {
                    var alias = pair.Key.AsString();
                    if (alias.Length > 0 && !alias.Equals("neutral", StringComparison.OrdinalIgnoreCase)
                        && _expressions.TryGetValue(pair.Value.AsString(), out var expression))
                        _expressions.TryAdd(alias, expression);
                }
            }
        }
    }

    public override void SetMood(string mood)
    {
        Mood = mood;
        if (_cubism is not null)
        {
            if (_expressions.TryGetValue(mood, out var expression))
                _cubism.Call("start_expression", expression);
            else
                _cubism.Call("stop_expression");
        }
        QueueRedraw();
    }

    private void FitCubism()
    {
        if (_cubism is null || _renderViewport is null)
            return;
        var renderSize = ((Vector2I)Size * RenderScale).Max(new Vector2I(2, 2));
        _renderViewport.Size = renderSize;
        var info = _cubism.Call("get_canvas_info").AsGodotDictionary();
        var canvas = info["size_in_pixels"].AsVector2();
        var scale = MathF.Min(renderSize.X / MathF.Max(1, canvas.X), renderSize.Y / MathF.Max(1, canvas.Y));
        _cubism.Position = (Vector2)renderSize / 2;
        _cubism.Scale = Vector2.One * scale;
    }

    public override void _Draw()
    {
        if (_cubism is not null)
            return;

        DrawSetTransform(Size / 2, 0, Vector2.One * PreviewScale);
        DrawColoredPolygon([new(-39, -14), new(-46, -56), new(-13, -36)], Fur);
        DrawColoredPolygon([new(39, -14), new(46, -56), new(13, -36)], Fur);
        DrawColoredPolygon([new(-32, -23), new(-39, -44), new(-22, -34)], Ear);
        DrawColoredPolygon([new(32, -23), new(39, -44), new(22, -34)], Ear);
        DrawCircle(new Vector2(-13, 4), 33, Fur, antialiased: true);
        DrawCircle(new Vector2(13, 4), 33, Fur, antialiased: true);
        DrawCircle(new Vector2(-27, 12), 5, Blush, antialiased: true);
        DrawCircle(new Vector2(27, 12), 5, Blush, antialiased: true);
        DrawEye(-17);
        DrawEye(17);
        DrawColoredPolygon([new(-3, 10), new(3, 10), new(0, 13)], Face);
        if (Mood == "curious")
            DrawCircle(new Vector2(0, 20), 3.7f, Face, antialiased: true);
        else
            DrawArc(new Vector2(0, 15), 5, 0, Mathf.Pi, 12, Face, 1.7f, true);
    }

    private float PreviewScale => MathF.Min(Size.X / 120, Size.Y / 115);

    private void DrawEye(float x)
    {
        if (Mood == "sleepy")
            DrawLine(new Vector2(x - 5, 0), new Vector2(x + 5, 1), Face, 2.6f, true);
        else if (Mood == "happy")
            DrawArc(new Vector2(x, 1), 5, Mathf.Pi, Mathf.Tau, 14, Face, 2.5f, true);
        else
            DrawCircle(new Vector2(x + _pose.Look.X * 2f, -_pose.Look.Y * 2f), Mood == "curious" ? 4.6f : 3.5f, Face, antialiased: true);
    }
}
