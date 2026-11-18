using Godot;
using JustNeko.Pet.Rig;

namespace JustNeko.Pet.Vrm;

/// <summary>
/// VRM 3D 角色：用 godot-vrm 导入的场景，在透明的子视口里用正交相机渲染，按模型大小自动取景。
/// 表情用 VRM 标准表情，口型用 aa，眨眼和待机姿态由程序生成；没有动作文件，行为槽留空。
/// </summary>
public sealed partial class VrmAvatar : PetAvatar
{
    private const int RenderScale = 2;
    private const float MoodFade = 6f;
    // Live2D 角色常用的情绪名映射到 VRM 标准表情。
    private static readonly Dictionary<string, (string Expression, float Weight)> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["curious"] = ("surprised", 0.55f),
        ["shy"] = ("happy", 0.6f),
        ["sleepy"] = ("relaxed", 0.9f),
    };

    private readonly string _scenePath;
    private readonly Dictionary<string, float> _weights = new(StringComparer.OrdinalIgnoreCase);
    private readonly RandomNumberGenerator _random = new();
    private SubViewport? _viewport;
    private Camera3D? _camera;
    private Node3D? _model;
    private VrmExpressions? _expressions;
    private VrmPoser? _poser;
    private Aabb _bounds;
    private (string Expression, float Weight)? _mood;
    private float _mouth;
    private double _nextBlink;
    private double _blinkTime = -1;

    public VrmAvatar(string scenePath) => _scenePath = scenePath;

    /// <summary>模型没加载成功（文件不在、没导入）时为 false，调用方可以换回 Live2D。</summary>
    internal bool Loaded => _model is not null;

    internal override IEnumerable<string> EmotionNames =>
        _expressions is null ? ["neutral"]
        : new[] { "neutral" }.Concat(new[] { "happy", "angry", "sad", "relaxed", "surprised" }.Where(_expressions.Has))
            .Concat(Aliases.Where(alias => _expressions.Has(alias.Value.Expression)).Select(alias => alias.Key));

    public override void _Ready()
    {
        if (!ResourceLoader.Exists(_scenePath) || ResourceLoader.Load<PackedScene>(_scenePath)?.Instantiate() is not Node3D model)
        {
            GD.PushWarning($"VRM 角色加载失败：{_scenePath}");
            return;
        }
        _model = model;
        _viewport = new SubViewport
        {
            Name = "PetRenderViewport",
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        AddChild(_viewport);
        _viewport.AddChild(model);
        _bounds = MeshBounds(model);

        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = _bounds.Size.Y * 1.04f };
        _camera.Position = new Vector3(_bounds.GetCenter().X, _bounds.GetCenter().Y, _bounds.End.Z + 3);
        _viewport.AddChild(_camera);
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, 20, 0), LightEnergy = 1.0f });
        _viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.75f, 0.75f, 0.8f),
            },
        });

        var surface = new TextureRect
        {
            Texture = _viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            TextureFilter = TextureFilterEnum.Linear,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _expressions = new VrmExpressions(model);
        if (model.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault() is { } skeleton)
        {
            _poser = new VrmPoser(skeleton);
            RemoveConstraints(model);
        }
        _nextBlink = _random.RandfRange(1.5f, 4f);
        Resized += Fit;
        Fit();
    }

    public override void _Process(double delta)
    {
        // 先于模型里的约束和弹簧骨骼执行（父节点先处理），它们会跟着这里的姿态动。
        _poser?.Update(delta);
        if (_expressions is null)
            return;
        var fade = 1 - Mathf.Exp(-MoodFade * (float)delta);
        foreach (var name in new[] { "happy", "angry", "sad", "relaxed", "surprised" })
        {
            var target = _mood is { } mood && mood.Expression.Equals(name, StringComparison.OrdinalIgnoreCase) ? mood.Weight : 0;
            _weights[name] = Mathf.Lerp(_weights.GetValueOrDefault(name), target, fade);
        }
        _weights["aa"] = _mouth;
        _weights["blink"] = Blink(delta);
        _expressions.Apply(_weights);
    }

    public override bool ContainsPointer(Vector2 position)
    {
        if (_model is null)
            return base.ContainsPointer(position);
        // 手臂放下后身体大约是身高的 0.42 倍宽。
        var relative = position / Size;
        var width = 0.42f * _bounds.Size.Y / (_camera!.Size * Size.X / Size.Y);
        return new Rect2(0.5f - width / 2, 0.01f, width, 0.98f).HasPoint(relative);
    }

    public override void SetMood(string mood)
    {
        Mood = mood;
        if (_expressions is null || mood.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            _mood = null;
        else if (_expressions.Has(mood))
            _mood = (mood, 1f);
        else if (Aliases.TryGetValue(mood, out var alias) && _expressions.Has(alias.Expression))
            _mood = alias;
        else
            _mood = null;
    }

    internal override void SetPose(AvatarPose pose)
    {
        _mouth = pose.MouthOpen;
        if (_poser is not null)
            _poser.Pose = pose;
    }

    // godot-vrm 的节点约束（BoneNodeConstraintApplier）通过骨架修改器的信号每帧重算袖子、扭转骨骼，
    // 但对带约束的 VRoid 模型会算错（袖子停在水平）。约束只管细微的扭转，这里连同它的内部修改器一起移除，
    // 由 VrmPoser 带着这些骨骼走。只停用 _process 不够，它不走 _process。
    private static void RemoveConstraints(Node model)
    {
        foreach (var node in model.FindChildren("*", "Node", true, false))
        {
            if (node.GetScript().AsGodotObject() is not Script script || !script.ResourcePath.EndsWith("bone_node_constraint_applier.gd"))
                continue;
            if (node.Get("internal_modifier_node").AsGodotObject() is Node modifier)
                modifier.QueueFree();
            node.QueueFree();
        }
    }

    private float Blink(double delta)
    {
        // 开心时眼睛本来就眯着，不再眨。
        if (_weights.GetValueOrDefault("happy") > 0.5f)
            return 0;
        if (_blinkTime < 0)
        {
            _nextBlink -= delta;
            if (_nextBlink > 0)
                return 0;
            _blinkTime = 0;
        }
        _blinkTime += delta;
        const double duration = 0.16;
        if (_blinkTime >= duration)
        {
            _blinkTime = -1;
            _nextBlink = _random.RandfRange(2.5f, 5.5f);
            return 0;
        }
        return Mathf.Sin((float)(_blinkTime / duration) * Mathf.Pi);
    }

    private void Fit()
    {
        if (_viewport is null)
            return;
        _viewport.Size = ((Vector2I)Size * RenderScale).Max(new Vector2I(2, 2));
    }

    private static Aabb MeshBounds(Node3D model)
    {
        var bounds = new Aabb();
        var first = true;
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            var box = mesh.Transform * mesh.GetAabb();
            for (var parent = mesh.GetParentNode3D(); parent is not null && parent != model; parent = parent.GetParentNode3D())
                box = parent.Transform * box;
            bounds = first ? box : bounds.Merge(box);
            first = false;
        }
        return bounds;
    }
}
