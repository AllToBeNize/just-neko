using Godot;
using JustNeko.Core.LocalModel;

namespace JustNeko.Shell.Settings;

/// <summary>本地模型：下载、取消、查看状态。下载在后台进行，关掉设置中心也不影响。</summary>
public sealed partial class LocalModelPage : SettingsPage
{
    private readonly PetServices _services;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private Button _download = null!;
    private Button _cancel = null!;

    internal LocalModelPage(PetServices services)
    {
        _services = services;
        Name = "LocalModelPage";
    }

    public override void _Ready()
    {
        var manifest = _services.LocalModel.Manifest;
        AddHeading("本地模型", "桌宠自带一个小模型，下载一次以后不联网也能聊，对话内容不会离开这台电脑。");
        if (manifest is not null)
        {
            AddChild(PetTheme.Note(
                $"模型：{manifest.Name}（{manifest.Model.FileName}，{manifest.Model.Size / 1024d / 1024 / 1024:0.0} GB）\n" +
                $"运行时：llama.cpp {manifest.RuntimeVersion}（Vulkan，可用 AMD / NVIDIA / Intel 显卡加速，没有显卡时用 CPU）\n" +
                $"存放位置：{_services.LocalModel.Directory}"));
        }

        _status = PetTheme.Label("", PetTheme.Ink, 14);
        _status.Name = "LocalModelStatus";
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_status);
        _progress = new ProgressBar { Name = "LocalModelProgress", MaxValue = 1, Step = 0.001, CustomMinimumSize = new Vector2(0, 14), ShowPercentage = false };
        _progress.AddThemeStyleboxOverride("background", PetTheme.Box(PetTheme.Surface, 7));
        _progress.AddThemeStyleboxOverride("fill", PetTheme.Box(PetTheme.Accent, 7));
        AddChild(_progress);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        AddChild(actions);
        _download = PetTheme.PrimaryButton("DownloadLocalModel", "下载");
        _download.Pressed += () => _ = _services.LocalModel.InstallAsync();
        actions.AddChild(_download);
        _cancel = new Button { Name = "CancelLocalModel", Text = "取消下载", CustomMinimumSize = new Vector2(84, 34) };
        _cancel.Pressed += _services.LocalModel.CancelInstall;
        actions.AddChild(_cancel);
        var folder = new Button { Name = "OpenLocalModelFolder", Text = "打开所在文件夹", CustomMinimumSize = new Vector2(0, 34) };
        folder.Pressed += () =>
        {
            System.IO.Directory.CreateDirectory(_services.LocalModel.Directory);
            OS.ShellOpen(_services.LocalModel.Directory);
        };
        actions.AddChild(folder);
        AddChild(PetTheme.Note("下载中途取消或断网时，已下载的部分会保留，下次接着下。HuggingFace 连不上时会自动换国内镜像。", 12));

        _services.LocalModel.StateChanged += Render;
        Render(_services.LocalModel.State);
    }

    public override void _ExitTree() => _services.LocalModel.StateChanged -= Render;

    private void Render(LocalModelState state)
    {
        _status.Text = state.Status switch
        {
            LocalModelStatus.NotInstalled => "还没有下载。" + (state.Message.StartsWith("已取消") ? state.Message : ""),
            LocalModelStatus.Downloading => state.Message,
            _ => state.Message,
        };
        _status.AddThemeColorOverride("font_color", state.Status switch
        {
            LocalModelStatus.Failed => PetTheme.Accent,
            LocalModelStatus.Running or LocalModelStatus.Installed => PetTheme.Good,
            _ => PetTheme.Ink,
        });
        _progress.Visible = state.Status == LocalModelStatus.Downloading;
        _progress.Value = state.Progress;
        var canDownload = state.Status is LocalModelStatus.NotInstalled or LocalModelStatus.Failed && !_services.LocalModel.IsInstalled;
        _download.Visible = state.Status != LocalModelStatus.Unsupported;
        _download.Disabled = !canDownload;
        _download.Text = state.Status == LocalModelStatus.Failed && !_services.LocalModel.IsInstalled ? "重试" : _services.LocalModel.IsInstalled ? "已下载" : "下载";
        _cancel.Visible = state.Status == LocalModelStatus.Downloading;
    }
}
