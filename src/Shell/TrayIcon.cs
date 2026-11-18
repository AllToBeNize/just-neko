using Godot;
using JustNeko.Core.LocalModel;

namespace JustNeko.Shell;

/// <summary>
/// 托盘图标：桌宠的「后台」。左键显示 / 隐藏角色，右键打开菜单（勿扰、设置中心、本地模型状态、退出）。
/// </summary>
public sealed partial class TrayIcon : Node
{
    private const int ShowPet = 0;
    private const int Quiet = 1;
    private const int Settings = 2;
    private const int LocalModel = 3;
    private const int Quit = 4;
    private const string IconPath = "res://assets/icon.svg";

    private StatusIndicator _indicator = null!;
    private PopupMenu _menu = null!;

    internal event Action? TogglePetRequested;
    internal event Action? QuietToggled;
    internal event Action<string>? SettingsRequested;
    internal event Action? QuitRequested;

    public override void _Ready()
    {
        Name = "Tray";
        _menu = new PopupMenu { Name = "TrayMenu", ForceNative = true, Theme = PetTheme.Create() };
        _menu.AddCheckItem("显示角色", ShowPet);
        _menu.AddCheckItem("勿扰模式", Quiet);
        _menu.AddSeparator();
        _menu.AddItem("设置中心…", Settings);
        _menu.AddItem("本地模型", LocalModel);
        _menu.AddSeparator();
        _menu.AddItem("退出 Just Neko", Quit);
        _menu.IdPressed += OnMenuItem;
        AddChild(_menu);

        _indicator = new StatusIndicator { Name = "TrayIndicator", Icon = GD.Load<Texture2D>(IconPath), Tooltip = "Just Neko" };
        _indicator.Pressed += OnPressed;
        AddChild(_indicator);
    }

    internal PopupMenu Menu => _menu;

    internal void Refresh(bool petShown, bool quiet, LocalModelState localModel)
    {
        _menu.SetItemChecked(_menu.GetItemIndex(ShowPet), petShown);
        _menu.SetItemChecked(_menu.GetItemIndex(Quiet), quiet);
        _menu.SetItemText(_menu.GetItemIndex(LocalModel), "本地模型：" + localModel.Status switch
        {
            LocalModelStatus.Unsupported => "不可用",
            LocalModelStatus.NotInstalled => "未下载",
            LocalModelStatus.Downloading => $"下载中 {localModel.Progress:P0}",
            LocalModelStatus.Installed => "已就绪",
            LocalModelStatus.Starting => "启动中",
            LocalModelStatus.Running => "运行中",
            _ => "出错了",
        });
        _indicator.Tooltip = quiet ? "Just Neko（勿扰中）" : "Just Neko";
    }

    /// <summary>在托盘位置弹出菜单。菜单整体放在光标上方，不压住任务栏。</summary>
    internal void PopupAt(Vector2I position)
    {
        _menu.ResetSize();
        var size = (Vector2I)_menu.GetContentsMinimumSize();
        var screen = DisplayServer.GetScreenFromRect(new Rect2(position, Vector2.One));
        var area = new Rect2I(DisplayServer.ScreenGetPosition(screen), DisplayServer.ScreenGetSize(screen));
        var origin = ScreenMath.ClampToScreen(position - new Vector2I(size.X / 2, size.Y), size, area);
        _menu.Popup(new Rect2I(origin, size));
    }

    private void OnPressed(long button, Vector2I position)
    {
        if (button == (long)MouseButton.Left)
            TogglePetRequested?.Invoke();
        else if (button == (long)MouseButton.Right)
            PopupAt(position);
    }

    private void OnMenuItem(long id)
    {
        switch (id)
        {
            case ShowPet:
                TogglePetRequested?.Invoke();
                break;
            case Quiet:
                QuietToggled?.Invoke();
                break;
            case Settings:
                SettingsRequested?.Invoke(Shell.Settings.SettingsCenter.Chat);
                break;
            case LocalModel:
                SettingsRequested?.Invoke(Shell.Settings.SettingsCenter.LocalModel);
                break;
            case Quit:
                QuitRequested?.Invoke();
                break;
        }
    }
}
