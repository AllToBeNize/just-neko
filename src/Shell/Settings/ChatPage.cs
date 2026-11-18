using Godot;
using JustNeko.Core.Ai;
using JustNeko.Core.LocalModel;

namespace JustNeko.Shell.Settings;

/// <summary>对话：用哪个模型服务、角色提示词。改完点「应用」才生效；「测试连接」用还没保存的填写内容试。</summary>
public sealed partial class ChatPage : SettingsPage
{
    private static readonly (ProviderKind Kind, string Label)[] Providers =
    [
        (ProviderKind.BuiltIn, "内置本地模型"),
        (ProviderKind.Ollama, "本地 Ollama"),
        (ProviderKind.OpenAICompatible, "OpenAI 兼容服务"),
        (ProviderKind.Demo, "离线演示"),
    ];

    private readonly PetServices _services;
    private readonly Dictionary<ProviderKind, Draft> _drafts = [];
    private OptionButton _provider = null!;
    private VBoxContainer _external = null!;
    private Label _builtInStatus = null!;
    private Button _manageLocalModel = null!;
    private LineEdit _endpoint = null!;
    private LineEdit _model = null!;
    private Button _detect = null!;
    private PopupMenu _models = null!;
    private LineEdit _key = null!;
    private Label _keyNote = null!;
    private TextEdit _prompt = null!;
    private Button _test = null!;
    private Label _result = null!;
    private ProviderKind _editing;
    private CancellationTokenSource? _busy;

    internal ChatPage(PetServices services)
    {
        _services = services;
        Name = "ChatPage";
    }

    /// <summary>测试连接成功时触发，桌宠借此给个开心的反应、清掉头顶的小标记。</summary>
    internal event Action? ConnectionVerified;

    /// <summary>想去「本地模型」页时触发。</summary>
    internal event Action? LocalModelRequested;

    public override void _Ready()
    {
        AddHeading("对话", "桌宠用哪个模型聊天。默认用内置本地模型，不联网也能聊；本机装了 Ollama 或有云端服务的话也可以换过去。");
        _provider = new OptionButton { Name = "ModelProvider" };
        foreach (var (_, label) in Providers)
            _provider.AddItem(label);
        _provider.ItemSelected += SelectProvider;
        AddChild(_provider);

        var builtIn = new HBoxContainer();
        builtIn.AddThemeConstantOverride("separation", 8);
        _builtInStatus = PetTheme.Note("");
        _builtInStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        builtIn.AddChild(_builtInStatus);
        _manageLocalModel = new Button { Name = "ManageLocalModel", Text = "管理本地模型" };
        _manageLocalModel.Pressed += () => LocalModelRequested?.Invoke();
        builtIn.AddChild(_manageLocalModel);
        AddChild(builtIn);

        _external = new VBoxContainer();
        _external.AddThemeConstantOverride("separation", 6);
        AddChild(_external);
        _endpoint = Field(_external, "Endpoint", "服务地址", "例如 http://localhost:11434/v1", out _);
        _model = Field(_external, "ModelName", "模型", "服务中的模型名称", out var modelRow);
        _detect = new Button { Name = "DetectModels", Text = "检测", TooltipText = "列出服务上可用的模型", CustomMinimumSize = new Vector2(56, 34) };
        _detect.Pressed += DetectModels;
        modelRow.AddChild(_detect);
        _models = new PopupMenu { Name = "DetectedModels" };
        _models.IndexPressed += index => _model.Text = _models.GetItemText((int)index);
        AddChild(_models);
        _key = Field(_external, "ApiKey", "API Key", "按服务要求填写", out _);
        _key.Secret = true;
        _keyNote = PetTheme.Note("", 12);
        _external.AddChild(_keyNote);

        AddChild(PetTheme.Label("角色提示词", PetTheme.Muted, 13));
        _prompt = new TextEdit
        {
            Name = "RolePrompt",
            CustomMinimumSize = new Vector2(0, 90),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        _prompt.AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Surface, 8, 10, 7));
        AddChild(_prompt);

        _result = PetTheme.Note("");
        _result.Name = "ChatResult";
        _result.Hide();
        AddChild(_result);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 8);
        AddChild(actions);
        _test = new Button { Name = "TestConnection", Text = "测试连接", CustomMinimumSize = new Vector2(84, 34) };
        _test.Pressed += TestConnection;
        actions.AddChild(_test);
        var apply = PetTheme.PrimaryButton("ApplySettings", "应用");
        apply.Pressed += Apply;
        actions.AddChild(apply);

        _services.LocalModel.StateChanged += RefreshBuiltIn;
    }

    public override void _ExitTree()
    {
        _busy?.Cancel();
        _services.LocalModel.StateChanged -= RefreshBuiltIn;
    }

    internal override void OnShown()
    {
        var current = _services.Settings.Current;
        _drafts.Clear();
        foreach (var (kind, _) in Providers)
        {
            var endpoint = current.EndpointFor(kind);
            _drafts[kind] = new Draft(endpoint.BaseUrl, endpoint.Model, _services.Settings.GetApiKey(kind));
        }
        _editing = current.ChatProvider;
        _provider.Select(IndexOf(_editing));
        ShowDraft(_editing);
        _prompt.Text = current.RolePrompt;
        if (_services.Settings.LoadError is { } loadError)
            Flash(_result, loadError, PetTheme.Accent, 8);
        else
            _result.Hide();
    }

    private void SelectProvider(long index)
    {
        KeepDraft();
        _editing = Providers[index].Kind;
        ShowDraft(_editing);
        _result.Hide();
    }

    private void ShowDraft(ProviderKind kind)
    {
        var draft = _drafts[kind];
        _endpoint.Text = draft.BaseUrl;
        _model.Text = draft.Model;
        _key.Text = draft.Key;
        var external = kind is ProviderKind.Ollama or ProviderKind.OpenAICompatible;
        _external.Visible = external;
        _builtInStatus.GetParent<Control>().Visible = kind == ProviderKind.BuiltIn;
        _test.Disabled = kind == ProviderKind.Demo;
        _model.PlaceholderText = kind == ProviderKind.Ollama ? "留空则自动使用已安装的第一个模型" : "服务中的模型名称";
        _keyNote.Text = kind == ProviderKind.Ollama ? "本地 Ollama 一般不需要 Key。"
            : _services.Settings.KeysPersist ? "Key 加密保存在本机，只有当前系统用户能解开。" : "当前系统暂不支持安全保存 Key，重启后需要重新填写。";
        RefreshBuiltIn(_services.LocalModel.State);
    }

    private void RefreshBuiltIn(LocalModelState state)
    {
        if (!IsInsideTree())
            return;
        _builtInStatus.Text = state.Status switch
        {
            LocalModelStatus.NotInstalled => "还没有下载内置模型（约 1.1 GB）。",
            LocalModelStatus.Downloading => $"下载中 {state.Progress:P0}…",
            _ => state.Message,
        };
    }

    private void KeepDraft() =>
        _drafts[_editing] = new Draft(_endpoint.Text.Trim(), _model.Text.Trim(), _key.Text.Trim());

    private async void DetectModels()
    {
        using var busy = Begin(_detect, "检测中");
        try
        {
            var models = await ModelCatalog.ListAsync(_endpoint.Text.Trim(), _key.Text.Trim(), busy.Token);
            if (busy.Token.IsCancellationRequested || !IsInsideTree())
                return;
            if (models.Count == 0)
            {
                Flash(_result, _editing == ProviderKind.Ollama ? "本地 Ollama 里还没有模型。请先用 ollama pull 下载一个聊天模型。" : "服务没有返回可用的模型。", PetTheme.Accent, 8);
                return;
            }
            _result.Hide();
            _models.Clear();
            foreach (var model in models)
                _models.AddItem(model);
            var window = GetWindow();
            _models.Popup(new Rect2I(window.Position + (Vector2I)_model.GlobalPosition + new Vector2I(0, (int)_model.Size.Y), new Vector2I((int)_model.Size.X, 0)));
        }
        catch (OperationCanceledException) when (busy.Token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsInsideTree())
                Flash(_result, error is ArgumentException ? error.Message : ModelErrors.Describe(error, _editing), PetTheme.Accent, 8);
        }
        finally
        {
            End(busy, _detect, "检测");
        }
    }

    private async void TestConnection()
    {
        KeepDraft();
        var draft = _drafts[_editing];
        var endpoint = new ModelEndpoint(_editing, draft.BaseUrl, draft.Model);
        using var busy = Begin(_test, "测试中");
        _result.Show();
        _result.Modulate = Colors.White;
        _result.Text = _editing == ProviderKind.BuiltIn ? "正在启动内置模型，第一次可能要十几秒…" : "正在连接…";
        _result.AddThemeColorOverride("font_color", PetTheme.Muted);
        try
        {
            var message = await _services.Models.TestAsync(endpoint, draft.Key, busy.Token);
            if (!IsInsideTree())
                return;
            Flash(_result, "✓ " + message, PetTheme.Good, 6);
            ConnectionVerified?.Invoke();
        }
        catch (OperationCanceledException) when (busy.Token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsInsideTree())
                Flash(_result, error is ArgumentException or InvalidOperationException ? error.Message : ModelErrors.Describe(error, _editing), PetTheme.Accent, 10);
        }
        finally
        {
            End(busy, _test, "测试连接");
        }
    }

    private void Apply()
    {
        KeepDraft();
        var draft = _drafts[_editing];
        if (_editing is ProviderKind.Ollama or ProviderKind.OpenAICompatible)
        {
            if (_editing == ProviderKind.OpenAICompatible && draft.Model.Length == 0)
            {
                Flash(_result, "请填写模型名称。", PetTheme.Accent, 6);
                return;
            }
            try
            {
                EndpointUrl.Normalize(draft.BaseUrl);
            }
            catch (ArgumentException error)
            {
                Flash(_result, error.Message, PetTheme.Accent, 6);
                return;
            }
        }

        var settings = _services.Settings;
        var next = settings.Current with { ChatProvider = _editing, RolePrompt = _prompt.Text.Trim() };
        foreach (var kind in new[] { ProviderKind.Ollama, ProviderKind.OpenAICompatible })
            next = next.WithEndpoint(new ModelEndpoint(kind, _drafts[kind].BaseUrl, _drafts[kind].Model));

        var failure = settings.Update(next);
        foreach (var kind in new[] { ProviderKind.Ollama, ProviderKind.OpenAICompatible })
        {
            if (_drafts[kind].Key != settings.GetApiKey(kind))
                failure ??= settings.SetApiKey(kind, _drafts[kind].Key);
        }
        if (failure is not null)
            Flash(_result, failure + " 本次运行仍按新设置使用。", PetTheme.Accent, 8);
        else
            Flash(_result, "✓ 已保存，下一句话开始生效。", PetTheme.Good);
    }

    private CancellationTokenSource Begin(Button button, string text)
    {
        _busy?.Cancel();
        _busy = new CancellationTokenSource();
        button.Disabled = true;
        button.Text = text;
        return _busy;
    }

    private void End(CancellationTokenSource busy, Button button, string text)
    {
        if (_busy == busy)
            _busy = null;
        if (!IsInsideTree())
            return;
        button.Text = text;
        button.Disabled = button == _test && _editing == ProviderKind.Demo;
    }

    private static int IndexOf(ProviderKind kind) => Array.FindIndex(Providers, provider => provider.Kind == kind);

    private sealed record Draft(string BaseUrl, string Model, string Key);
}
