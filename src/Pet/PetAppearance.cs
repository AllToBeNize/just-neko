using Godot;

namespace JustNeko.Pet;

internal sealed record PetMotion(string Group, int Index, string File)
{
    public string Key => $"{Group}/{File}";
    public string Name => File.GetFile().TrimSuffix(".motion3.json");
    public string Label => $"{Group} · {Name}";
}

/// <summary>人物大小和每个行为槽绑定的动作。模型的默认绑定在 config/，玩家的选择存在 user:// 并优先。</summary>
internal sealed record PetAppearance(int SizePercent, IReadOnlyDictionary<string, string> Motions)
{
    public const int MinSize = 60;
    public const int MaxSize = 180;
    private const string Path = "user://pet-appearance.cfg";
    private const string DefaultsPath = "res://config/pet-motion-defaults.cfg";

    public static PetAppearance Load(BehaviorCatalog catalog)
    {
        using var config = new ConfigFile();
        using var defaults = new ConfigFile();
        config.Load(Path);
        defaults.Load(DefaultsPath);
        var size = Math.Clamp(config.GetValue("desktop", "size_percent", 100).AsInt32(), MinSize, MaxSize);
        var motions = new Dictionary<string, string>();
        foreach (var slot in catalog.Slots)
        {
            var key = config.GetValue("motions", slot.Id, defaults.GetValue("motions", slot.Id, "")).AsString();
            if (key.Length > 0)
                motions[slot.Id] = key;
        }
        return new PetAppearance(size, motions);
    }

    public Error Save(BehaviorCatalog catalog)
    {
        using var config = new ConfigFile();
        config.SetValue("desktop", "size_percent", SizePercent);
        foreach (var slot in catalog.Slots)
            config.SetValue("motions", slot.Id, Motions.GetValueOrDefault(slot.Id, ""));
        var temporary = Path + ".tmp";
        var error = config.Save(temporary);
        return error == Error.Ok ? DirAccess.RenameAbsolute(temporary, Path) : error;
    }
}
