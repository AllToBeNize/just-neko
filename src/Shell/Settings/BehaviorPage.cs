using Godot;

namespace JustNeko.Shell.Settings;

/// <summary>行为：全屏时自动隐藏、勿扰，以及自主活动的说明。改了立即生效。</summary>
public sealed partial class BehaviorPage : SettingsPage
{
    private readonly PetServices _services;
    private CheckButton _quiet = null!;

    internal BehaviorPage(PetServices services)
    {
        _services = services;
        Name = "BehaviorPage";
    }

    public override void _Ready()
    {
        var settings = _services.Settings;
        AddHeading("行为");
        AddToggle("HideWhenFullscreen", "全屏时自动隐藏", "打游戏、看视频或演示时，角色自动藏起来，退出全屏后回来。",
            settings.Current.HideWhenFullscreen, value => settings.Update(settings.Current with { HideWhenFullscreen = value }));
        _quiet = AddToggle("QuietMode", "勿扰模式", "角色留在桌面上，但不会自己走动、不会主动搭话。托盘菜单里也能开关。",
            settings.Current.QuietMode, value => settings.Update(settings.Current with { QuietMode = value }));

        AddSection("自主活动");
        var activities = _services.Activities.Activities;
        AddChild(PetTheme.Note(activities.Count == 0
            ? "角色空闲时会自己找点事做（比如沿着任务栏散步）。这部分还在做，之后会出现在这里，可以逐个开关。"
            : string.Join("、", activities.Select(activity => activity.Label))));
    }

    internal override void OnShown() => _quiet.SetPressedNoSignal(_services.Settings.Current.QuietMode);
}
