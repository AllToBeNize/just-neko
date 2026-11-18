using Godot;
using JustNeko.Pet.Vrm;
using FileAccess = Godot.FileAccess;

namespace JustNeko.Pet;

internal sealed record CharacterOption(string Id, string Label);

/// <summary>能选的角色：Models 下的 Live2D 模型，以及 Models/vrm 下导入过的 VRM。</summary>
internal static class Characters
{
    public const string Live2D = "live2d";
    private const string VrmPrefix = "vrm:";
    private const string VrmDirectory = "res://Models/vrm";

    public static IReadOnlyList<CharacterOption> Available()
    {
        var options = new List<CharacterOption>
        {
            new(Live2D, FileAccess.FileExists("res://Models/neko.model3.json") ? "Mao（Live2D）" : "预览猫（没有 Live2D 模型）"),
        };
        if (DirAccess.DirExistsAbsolute(VrmDirectory))
        {
            foreach (var file in DirAccess.GetFilesAt(VrmDirectory).Select(name => name.TrimSuffix(".import")).Distinct().Order())
            {
                if (file.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
                    options.Add(new(VrmPrefix + VrmDirectory.PathJoin(file), $"{file.GetBaseName()}（VRM 3D）"));
            }
        }
        return options;
    }

    /// <summary>按设置创建角色；VRM 文件不在或没导入时退回 Live2D。</summary>
    public static PetAvatar Create(string id)
    {
        if (id.StartsWith(VrmPrefix, StringComparison.Ordinal))
        {
            var path = id[VrmPrefix.Length..];
            if (ResourceLoader.Exists(path))
                return new VrmAvatar(path);
            GD.PushWarning($"找不到 VRM 角色 {path}，改用 Live2D。");
        }
        return new Live2DAvatar();
    }
}
