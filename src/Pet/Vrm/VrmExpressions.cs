using Godot;

namespace JustNeko.Pet.Vrm;

/// <summary>
/// VRM 表情：导入后每个表情是一段改脸部形变的动画。这里把它们读成「表情 → (网格, 形变, 值)」，
/// 再按各自的权重叠加写到网格上，所以开心、张嘴、眨眼可以同时生效。
/// </summary>
internal sealed class VrmExpressions
{
    private readonly Dictionary<string, List<(MeshInstance3D Mesh, int Shape, float Value)>> _expressions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(MeshInstance3D Mesh, int Shape)> _touched = [];
    private readonly Dictionary<(MeshInstance3D Mesh, int Shape), float> _frame = [];

    public VrmExpressions(Node avatar)
    {
        var player = avatar.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
        if (player is null)
            return;
        var root = player.GetNode(player.RootNode);
        foreach (var name in player.GetAnimationList())
        {
            if (name == "RESET")
                continue;
            var animation = player.GetAnimation(name);
            var shapes = new List<(MeshInstance3D, int, float)>();
            for (var track = 0; track < animation.GetTrackCount(); track++)
            {
                if (animation.TrackGetType(track) != Animation.TrackType.BlendShape || animation.TrackGetKeyCount(track) == 0)
                    continue;
                var path = animation.TrackGetPath(track);
                if (root.GetNodeOrNull<MeshInstance3D>(new NodePath(path.GetConcatenatedNames())) is not { } mesh)
                    continue;
                var shape = mesh.FindBlendShapeByName(path.GetConcatenatedSubNames());
                if (shape < 0)
                    continue;
                var value = animation.TrackGetKeyValue(track, animation.TrackGetKeyCount(track) - 1).AsSingle();
                shapes.Add((mesh, shape, value));
                _touched.Add((mesh, shape));
            }
            if (shapes.Count > 0)
                _expressions[name] = shapes;
        }
    }

    public bool Has(string name) => _expressions.ContainsKey(name);

    public IEnumerable<string> Names => _expressions.Keys;

    /// <summary>按权重写一帧表情。没出现在 weights 里的表情归零。</summary>
    public void Apply(IReadOnlyDictionary<string, float> weights)
    {
        _frame.Clear();
        foreach (var (name, weight) in weights)
        {
            if (weight <= 0 || !_expressions.TryGetValue(name, out var shapes))
                continue;
            foreach (var (mesh, shape, value) in shapes)
                _frame[(mesh, shape)] = _frame.GetValueOrDefault((mesh, shape)) + value * weight;
        }
        foreach (var key in _touched)
            key.Mesh.SetBlendShapeValue(key.Shape, Mathf.Clamp(_frame.GetValueOrDefault(key), 0, 1));
    }
}
