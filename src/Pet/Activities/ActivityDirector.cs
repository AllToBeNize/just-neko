using Godot;

namespace JustNeko.Pet.Activities;

/// <summary>
/// 安排自主活动：角色空闲一段时间后按权重抽一个活动来做，用户一有操作就停下。
/// 同一时间只做一个活动。
/// </summary>
public sealed partial class ActivityDirector : Node
{
    private const double MinIdleSeconds = 20;
    private const double MaxIdleSeconds = 60;
    private readonly List<IPetActivity> _activities = [];
    private readonly RandomNumberGenerator _random = new();
    private PetContext _context = null!;
    private Func<bool> _isUserBusy = null!;
    private IPetActivity? _current;
    private double _idleLeft;

    internal IReadOnlyList<IPetActivity> Activities => _activities;

    public string? CurrentId => _current?.Id;

    internal void Initialize(PetContext context, Func<bool> isUserBusy)
    {
        _context = context;
        _isUserBusy = isUserBusy;
        ResetIdle();
    }

    internal void Register(IPetActivity activity, BehaviorCatalog catalog)
    {
        _activities.Add(activity);
        foreach (var slot in activity.Slots)
            catalog.Register(slot);
    }

    /// <summary>按 Id 立即开始一个活动（给 AI 指令、Agent 工具、调试用）。用户正在操作时不开始。</summary>
    public bool TryStart(string id)
    {
        var activity = _activities.Find(candidate => candidate.Id == id);
        if (activity is null || _isUserBusy())
            return false;
        Begin(activity);
        return true;
    }

    /// <summary>用户有操作时调用：停掉正在做的活动，重新开始计空闲时间。</summary>
    public void Interrupt()
    {
        StopCurrent();
        ResetIdle();
    }

    public override void _Process(double delta)
    {
        if (_current is not null)
        {
            if (!_current.Update(_context, delta))
            {
                StopCurrent();
                ResetIdle();
            }
            return;
        }

        if (_activities.Count == 0 || _isUserBusy())
        {
            ResetIdle();
            return;
        }
        _idleLeft -= delta;
        if (_idleLeft > 0)
            return;
        ResetIdle();
        if (Pick() is { } next)
            Begin(next);
    }

    public override void _ExitTree() => StopCurrent();

    private IPetActivity? Pick()
    {
        var weighted = _activities.Select(activity => (activity, weight: Math.Max(0, activity.Weight(_context))))
            .Where(item => item.weight > 0).ToList();
        var roll = _random.Randf() * weighted.Sum(item => item.weight);
        foreach (var (activity, weight) in weighted)
        {
            roll -= weight;
            if (roll <= 0)
                return activity;
        }
        return weighted.LastOrDefault().activity;
    }

    private void Begin(IPetActivity activity)
    {
        StopCurrent();
        _current = activity;
        activity.Start(_context);
    }

    private void StopCurrent()
    {
        if (_current is null)
            return;
        var stopping = _current;
        _current = null;
        stopping.Stop(_context);
        _context.ReleaseMotion();
    }

    private void ResetIdle() => _idleLeft = _random.RandfRange((float)MinIdleSeconds, (float)MaxIdleSeconds);
}
