using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace JustNeko.Pet.Rig;

/// <summary>
/// 把语义姿态叠加到 Live2D 参数上。挂一个 GDCubismEffectCustom 到模型下，在「动作之后、物理之前」的那一步写参数，
/// 这样头发衣服的物理会跟着动。
/// </summary>
/// <remarks>
/// 默认映射用 Live2D 标准参数；嘴型读 model3.json 里的 LipSync 分组（Mao 是 ParamA），没有时用 ParamMouthOpenY。
/// 不符合标准的角色可以在模型目录放 rig.json 覆盖：[{"id": "ParamAngleX", "source": "LookX", "scale": 30}, …]。
/// gd_cubism 给出的参数值是上一帧写回后的结果，所以先减掉上一帧自己加的偏移，得到动作本身的值，再叠加这一帧的偏移。
/// </remarks>
internal sealed class Live2DRig
{
    private readonly List<Binding> _bindings = [];
    private AvatarPose _pose = AvatarPose.Rest;

    private Live2DRig()
    {
    }

    public static Live2DRig? Attach(Node2D cubism, string modelPath)
    {
        if (!ClassDB.ClassExists("GDCubismEffectCustom") || ClassDB.Instantiate("GDCubismEffectCustom").AsGodotObject() is not Node effect)
            return null;
        var parameters = new Dictionary<string, GodotObject>(StringComparer.Ordinal);
        foreach (var item in cubism.Call("get_parameters").AsGodotArray())
        {
            if (item.AsGodotObject() is { } parameter)
                parameters[parameter.Call("get_id").AsString()] = parameter;
        }

        var rig = new Live2DRig();
        foreach (var (id, source, scale) in ReadMapping(modelPath))
        {
            if (!parameters.TryGetValue(id, out var parameter))
                continue;
            rig._bindings.Add(new Binding(parameter, source, scale,
                parameter.Call("get_minimum_value").AsSingle(), parameter.Call("get_maximum_value").AsSingle()));
        }
        effect.Name = "NekoRig";
        cubism.AddChild(effect);
        effect.Connect("cubism_process", Callable.From<GodotObject, double>((_, _) => rig.Apply()));
        return rig;
    }

    /// <summary>实际生效的参数，调试和测试用。</summary>
    public IEnumerable<string> BoundParameters => _bindings.Select(binding => $"{binding.Parameter.Call("get_id")}←{binding.Source}×{binding.Scale}");

    public void SetPose(AvatarPose pose) => _pose = pose;

    private void Apply()
    {
        foreach (var binding in _bindings)
        {
            var current = binding.Parameter.Get("value").AsSingle();
            var motion = current - binding.LastOffset;
            var value = Mathf.Clamp(motion + Read(binding.Source) * binding.Scale, binding.Minimum, binding.Maximum);
            binding.LastOffset = value - motion;
            binding.Parameter.Set("value", value);
        }
    }

    private float Read(RigSource source) => source switch
    {
        RigSource.LookX => _pose.Look.X,
        RigSource.LookY => _pose.Look.Y,
        RigSource.LeanX => _pose.Lean.X,
        RigSource.LeanY => _pose.Lean.Y,
        RigSource.Mouth => _pose.MouthOpen,
        _ => 0,
    };

    private static IEnumerable<(string Id, RigSource Source, float Scale)> ReadMapping(string modelPath)
    {
        var custom = modelPath.GetBaseDir().PathJoin("rig.json");
        if (FileAccess.FileExists(custom))
        {
            using var document = JsonDocument.Parse(FileAccess.GetFileAsString(custom));
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (Enum.TryParse<RigSource>(entry.GetProperty("source").GetString(), out var source))
                    yield return (entry.GetProperty("id").GetString() ?? "", source, entry.GetProperty("scale").GetSingle());
            }
            yield break;
        }

        // Live2D 标准参数：头 ±30°，身体 ±10°，眼球 ±1。
        yield return ("ParamAngleX", RigSource.LookX, 30);
        yield return ("ParamAngleY", RigSource.LookY, 30);
        yield return ("ParamBodyAngleX", RigSource.LookX, 10);
        yield return ("ParamEyeBallX", RigSource.LookX, 1);
        yield return ("ParamEyeBallY", RigSource.LookY, 1);
        yield return ("ParamAngleZ", RigSource.LeanX, 22);
        yield return ("ParamBodyAngleZ", RigSource.LeanX, 10);
        yield return ("ParamBodyAngleY", RigSource.LeanY, 8);
        var lipSync = LipSyncParameters(modelPath).ToArray();
        foreach (var id in lipSync.Length > 0 ? lipSync : ["ParamMouthOpenY"])
            yield return (id, RigSource.Mouth, 1);
    }

    private static IEnumerable<string> LipSyncParameters(string modelPath)
    {
        using var document = JsonDocument.Parse(FileAccess.GetFileAsString(modelPath));
        if (!document.RootElement.TryGetProperty("Groups", out var groups))
            yield break;
        foreach (var group in groups.EnumerateArray())
        {
            if (group.GetProperty("Name").GetString() == "LipSync")
            {
                foreach (var id in group.GetProperty("Ids").EnumerateArray())
                    yield return id.GetString() ?? "";
            }
        }
    }

    private enum RigSource
    {
        LookX,
        LookY,
        LeanX,
        LeanY,
        Mouth,
    }

    private sealed class Binding(GodotObject parameter, RigSource source, float scale, float minimum, float maximum)
    {
        public GodotObject Parameter { get; } = parameter;
        public RigSource Source { get; } = source;
        public float Scale { get; } = scale;
        public float Minimum { get; } = minimum;
        public float Maximum { get; } = maximum;
        public float LastOffset { get; set; }
    }
}
