using Godot;
using Timer = Godot.Timer;

namespace JustNeko.Pet;

/// <summary>当前的人物大小和动作绑定。改动立即生效，停手 0.4 秒后保存。</summary>
public sealed partial class AppearanceStore : Node
{
    private BehaviorCatalog _catalog = null!;
    private Timer _saveTimer = null!;
    private bool _dirty;

    internal PetAppearance Current { get; private set; } = null!;

    /// <summary>上次保存失败的原因，正常时为 null。</summary>
    internal string? SaveError { get; private set; }

    internal event Action? Changed;

    internal void Initialize(BehaviorCatalog catalog)
    {
        _catalog = catalog;
        Current = PetAppearance.Load(catalog);
    }

    public override void _Ready()
    {
        _saveTimer = new Timer { OneShot = true, WaitTime = 0.4 };
        _saveTimer.Timeout += Save;
        AddChild(_saveTimer);
    }

    public override void _ExitTree() => Save();

    internal void Update(PetAppearance next)
    {
        Current = next;
        _dirty = true;
        _saveTimer.Start();
        Changed?.Invoke();
    }

    internal void Save()
    {
        if (!_dirty)
            return;
        var error = Current.Save(_catalog);
        _dirty = error != Error.Ok;
        SaveError = _dirty ? $"无法保存角色设置（{error}）。" : null;
    }
}
