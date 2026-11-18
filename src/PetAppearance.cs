using Godot;

namespace JustNeko;

internal enum PetBehavior
{
    Idle,
    ChatOpened,
    ChatClosed,
    Dragging,
    WaitingForReply,
    Replying,
    ReplyFinished,
}

internal sealed record PetMotion(string Group, int Index, string File)
{
    public string Key => $"{Group}/{File}";
    public string Name => File.GetFile().TrimSuffix(".motion3.json");
    public string Label => $"{Group} · {Name}";
}

internal sealed record PetBehaviorSlot(PetBehavior Behavior, string Label, bool Loop);

internal sealed record PetAppearance(int SizePercent, IReadOnlyDictionary<PetBehavior, string> Motions)
{
    public const int MinSize = 60;
    public const int MaxSize = 180;
    private const string Path = "user://pet-appearance.cfg";
    private const string DefaultsPath = "res://config/pet-motion-defaults.cfg";

    public static IReadOnlyList<PetBehaviorSlot> Slots { get; } =
    [
        new(PetBehavior.Idle, "待机", true),
        new(PetBehavior.ChatOpened, "打开聊天", false),
        new(PetBehavior.ChatClosed, "收起聊天", false),
        new(PetBehavior.Dragging, "拖动角色", true),
        new(PetBehavior.WaitingForReply, "等待回复", true),
        new(PetBehavior.Replying, "回复中", true),
        new(PetBehavior.ReplyFinished, "回复完成", false),
    ];

    public static PetAppearance Load()
    {
        using var config = new ConfigFile();
        using var defaults = new ConfigFile();
        config.Load(Path);
        defaults.Load(DefaultsPath);
        var size = Math.Clamp(config.GetValue("desktop", "size_percent", 100).AsInt32(), MinSize, MaxSize);
        var motions = new Dictionary<PetBehavior, string>();
        foreach (var slot in Slots)
        {
            var behavior = slot.Behavior.ToString();
            var key = config.GetValue("motions", behavior, defaults.GetValue("motions", behavior, "")).AsString();
            if (key.Length > 0)
                motions[slot.Behavior] = key;
        }
        return new PetAppearance(size, motions);
    }

    public Error Save()
    {
        using var config = new ConfigFile();
        config.SetValue("desktop", "size_percent", SizePercent);
        foreach (var slot in Slots)
            config.SetValue("motions", slot.Behavior.ToString(), Motions.GetValueOrDefault(slot.Behavior, ""));
        var temporary = Path + ".tmp";
        var error = config.Save(temporary);
        return error == Error.Ok ? DirAccess.RenameAbsolute(temporary, Path) : error;
    }
}
