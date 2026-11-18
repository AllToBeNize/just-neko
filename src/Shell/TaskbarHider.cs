using Godot;
using JustNeko.Core.Platform;

namespace JustNeko.Shell;

/// <summary>让常驻的小窗（角色、聊天、气泡）不在任务栏上占按钮。原生窗口每次显示时都可能重建，所以每次显示后都设一次。</summary>
internal static class TaskbarHider
{
    public static void Attach(Window window, IPlatformServices platform)
    {
        window.VisibilityChanged += () =>
        {
            if (window.Visible)
                Callable.From(() => Apply(window, platform)).CallDeferred();
        };
        if (window.Visible)
            Callable.From(() => Apply(window, platform)).CallDeferred();
    }

    private static void Apply(Window window, IPlatformServices platform)
    {
        if (!GodotObject.IsInstanceValid(window) || !window.Visible || DisplayServer.GetName() == "headless")
            return;
        platform.HideFromTaskbar(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId()));
    }
}
