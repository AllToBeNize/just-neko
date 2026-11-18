namespace JustNeko.Pet;

/// <summary>
/// 决定角色现在播哪个行为的动作。各个来源按优先级「占用」一层：拖动、交互阶段、自主活动……
/// 优先级最高、且绑定了动作的那一层生效；都没有时回到待机。一次性动作（打开聊天、回复完成）
/// 播完自动退出，任何一层有新的占用时也会被打断。
/// </summary>
internal sealed class PetMotionDirector(PetAvatar avatar, BehaviorCatalog catalog, Func<PetAppearance> appearance)
{
    private PetAvatar _avatar = avatar;

    public const int DragPriority = 100;
    public const int OneShotPriority = 50;
    public const int InteractionPriority = 20;
    public const int ActivityPriority = 10;

    private readonly Dictionary<string, (int Priority, string Behavior)> _layers = [];
    private string? _oneShot;
    private (string Behavior, string? Key)? _playing;

    public string? Current => _playing?.Behavior;

    public void Hold(string layer, int priority, string behavior)
    {
        _layers[layer] = (priority, behavior);
        _oneShot = null;
        Refresh();
    }

    public void Release(string layer)
    {
        if (_layers.Remove(layer))
            Refresh();
    }

    public void PlayOnce(string behavior)
    {
        _oneShot = GetMotion(behavior) is not null ? behavior : null;
        Refresh();
    }

    public void ClearOneShot()
    {
        _oneShot = null;
        Refresh();
    }

    /// <summary>角色的动作播完时调用。</summary>
    public void OnMotionFinished()
    {
        if (_oneShot is null || _playing?.Behavior != _oneShot)
            return;
        _oneShot = null;
        Refresh();
    }

    /// <summary>换了角色：之前播的动作作废，按新角色重新选一次。</summary>
    public void SetAvatar(PetAvatar next)
    {
        _avatar = next;
        _playing = null;
        Refresh();
    }

    /// <summary>动作绑定改了以后重新选一次。</summary>
    public void Refresh()
    {
        var candidates = _layers.Values.ToList();
        if (_oneShot is not null)
            candidates.Add((OneShotPriority, _oneShot));
        var behavior = candidates
            .OrderByDescending(candidate => candidate.Priority)
            .Select(candidate => candidate.Behavior)
            .FirstOrDefault(candidate => GetMotion(candidate) is not null) ?? BehaviorCatalog.Idle;

        var next = (behavior, GetMotion(behavior));
        if (_playing == next)
            return;
        _playing = next;
        _avatar.PlayMotion(next.Item2, catalog.Find(behavior)?.Loop ?? true);
    }

    private string? GetMotion(string behavior)
    {
        var key = appearance().Motions.GetValueOrDefault(behavior);
        return key is not null && _avatar.HasMotion(key) ? key : null;
    }
}
