using Godot;
using JustNeko.Core.Ai;
using JustNeko.Core.LocalModel;

namespace JustNeko.Shell;

/// <summary>
/// 第一次启动时的引导：先看本机有没有 Ollama，有就直接用；没有就让用户选「下载内置模型 / 用云端服务 / 先用离线演示」。
/// 不管怎么关掉都算走完引导，之后在设置中心里随时能改。
/// </summary>
public sealed partial class OnboardingWindow : Window
{
    private readonly PetServices _services;
    private VBoxContainer _body = null!;
    private CancellationTokenSource? _detecting;

    internal OnboardingWindow(PetServices services)
    {
        _services = services;
        Name = "Onboarding";
        Title = "欢迎使用 Just Neko";
        Visible = false;
        ForceNative = true;
        Unresizable = true;
        Size = new Vector2I(460, 400);
        Theme = PetTheme.Create();
    }

    /// <summary>用户选了云端服务，需要打开设置中心填写。</summary>
    internal event Action? CloudRequested;

    /// <summary>引导结束（选好了或者关掉了）。</summary>
    internal event Action? Finished;

    public override void _Ready()
    {
        CloseRequested += Finish;
        var surface = new PanelContainer();
        surface.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Background, 0, 28, 24));
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _body = new VBoxContainer { Name = "OnboardingBody" };
        _body.AddThemeConstantOverride("separation", 12);
        surface.AddChild(_body);
    }

    public override void _ExitTree()
    {
        _detecting?.Cancel();
        _services.LocalModel.StateChanged -= ShowDownload;
    }

    internal async void Start()
    {
        var area = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
        Position = ScreenMath.ClampToScreen(area.Position + (area.Size - Size) / 2, Size, area);
        Show();
        Page("你好，我是 Neko", "正在看看这台电脑上有没有能用的本地模型…");

        if (_services.LocalModel.IsInstalled)
        {
            Choose(ProviderKind.BuiltIn);
            Done("内置本地模型已经准备好了", "直接点我就能聊天。设置都在右下角托盘图标的右键菜单里。");
            return;
        }

        using var detecting = new CancellationTokenSource();
        _detecting = detecting;
        var models = await OllamaDetector.FindModelsAsync(detecting.Token);
        _detecting = null;
        if (!IsInsideTree() || detecting.IsCancellationRequested)
            return;
        if (models is { Count: > 0 })
        {
            Choose(ProviderKind.Ollama);
            Done("找到了本机的 Ollama", $"已有 {models.Count} 个模型，我会直接用 {models[0]}。\n设置都在右下角托盘图标的右键菜单里。");
            return;
        }
        ShowChoices();
    }

    private void ShowChoices()
    {
        var manifest = _services.LocalModel.Manifest;
        Page("选一个让我开口说话的方式", "之后随时能在托盘菜单 → 设置中心 → 对话里换。");
        if (manifest is not null)
        {
            Option("DownloadBuiltIn", "下载内置本地模型（推荐）",
                $"约 {manifest.TotalSize / 1024d / 1024 / 1024:0.0} GB，下载一次，之后不联网也能聊，对话不离开这台电脑。", primary: true, () =>
                {
                    Choose(ProviderKind.BuiltIn);
                    _services.LocalModel.StateChanged += ShowDownload;
                    ShowDownload(_services.LocalModel.State);
                    _ = _services.LocalModel.InstallAsync();
                });
        }
        Option("UseCloud", "使用云端服务", "填写 OpenAI 兼容服务的地址、模型和 Key。", primary: false, () =>
        {
            Choose(ProviderKind.OpenAICompatible);
            CloudRequested?.Invoke();
            Finish();
        });
        Option("UseDemo", "先用离线演示", "不连模型，先看看聊天和表情是怎么动的。", primary: false, () =>
        {
            Choose(ProviderKind.Demo);
            Finish();
        });
    }

    private void ShowDownload(LocalModelState state)
    {
        if (!IsInsideTree())
            return;
        switch (state.Status)
        {
            case LocalModelStatus.Installed or LocalModelStatus.Running:
                _services.LocalModel.StateChanged -= ShowDownload;
                Done("下载好了", "点我就能聊天。第一次开口要加载模型，会多等几秒。\n设置都在右下角托盘图标的右键菜单里。");
                return;
            case LocalModelStatus.Failed or LocalModelStatus.NotInstalled when _body.GetNodeOrNull("DownloadProgress") is not null:
                _services.LocalModel.StateChanged -= ShowDownload;
                Page("下载没有完成", state.Message);
                Option("RetryDownload", "重试", "已下载的部分会保留，接着下。", primary: true, () =>
                {
                    _services.LocalModel.StateChanged += ShowDownload;
                    ShowDownload(_services.LocalModel.State);
                    _ = _services.LocalModel.InstallAsync();
                });
                Option("BackToChoices", "换一种方式", "用云端服务或离线演示。", primary: false, ShowChoices);
                return;
        }

        if (_body.GetNodeOrNull<ProgressBar>("DownloadProgress") is not { } bar)
        {
            Page("正在下载内置模型", "可以先关掉这个窗口，下载会在后台继续，进度在托盘菜单里能看到。");
            bar = new ProgressBar { Name = "DownloadProgress", MaxValue = 1, Step = 0.001, CustomMinimumSize = new Vector2(0, 14), ShowPercentage = false };
            bar.AddThemeStyleboxOverride("background", PetTheme.Box(PetTheme.Surface, 7));
            bar.AddThemeStyleboxOverride("fill", PetTheme.Box(PetTheme.Accent, 7));
            _body.AddChild(bar);
            var status = PetTheme.Note("");
            status.Name = "DownloadStatus";
            _body.AddChild(status);
            var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            actions.AddThemeConstantOverride("separation", 8);
            _body.AddChild(actions);
            var cancel = new Button { Name = "CancelDownload", Text = "取消下载", CustomMinimumSize = new Vector2(84, 34) };
            cancel.Pressed += _services.LocalModel.CancelInstall;
            actions.AddChild(cancel);
            var background = PetTheme.PrimaryButton("DownloadInBackground", "后台下载");
            background.Pressed += Finish;
            actions.AddChild(background);
        }
        bar.Value = state.Progress;
        _body.GetNode<Label>("DownloadStatus").Text = state.Message;
    }

    private void Choose(ProviderKind kind) =>
        _services.Settings.Update(_services.Settings.Current with { ChatProvider = kind });

    private void Done(string title, string note)
    {
        Page(title, note);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        _body.AddChild(actions);
        var start = PetTheme.PrimaryButton("StartUsing", "开始使用");
        start.Pressed += Finish;
        actions.AddChild(start);
    }

    private void Page(string title, string note)
    {
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        _body.AddChild(PetTheme.Label(title, PetTheme.Ink, 21));
        _body.AddChild(PetTheme.Note(note, 14));
    }

    private void Option(string name, string title, string note, bool primary, Action chosen)
    {
        var card = new Button
        {
            Name = name,
            CustomMinimumSize = new Vector2(0, 66),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        var color = primary ? new Color("2c3a4f") : PetTheme.Surface;
        card.AddThemeStyleboxOverride("normal", PetTheme.Box(color, 12, 14, 10));
        card.AddThemeStyleboxOverride("hover", PetTheme.Box(color.Lightened(0.08f), 12, 14, 10));
        card.AddThemeStyleboxOverride("pressed", PetTheme.Box(color.Darkened(0.1f), 12, 14, 10));
        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 2);
        var heading = PetTheme.Label(title, primary ? PetTheme.Accent : PetTheme.Ink, 15);
        heading.MouseFilter = Control.MouseFilterEnum.Ignore;
        text.AddChild(heading);
        var description = PetTheme.Note(note, 12);
        description.MouseFilter = Control.MouseFilterEnum.Ignore;
        text.AddChild(description);
        card.AddChild(text);
        text.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepSize, 12);
        card.Pressed += chosen;
        _body.AddChild(card);
    }

    private void Finish()
    {
        _detecting?.Cancel();
        _services.LocalModel.StateChanged -= ShowDownload;
        _services.Settings.Update(_services.Settings.Current with { OnboardingDone = true });
        Hide();
        Finished?.Invoke();
    }
}
