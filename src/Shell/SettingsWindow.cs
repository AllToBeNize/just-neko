using Godot;
using JustNeko.Core.Ai;
using JustNeko.Core.Settings;

namespace JustNeko.Shell;

public partial class SettingsWindow : Window
{
    private static readonly (ProviderKind Kind, string Label)[] Providers =
    [
        (ProviderKind.Ollama, "本地 Ollama"),
        (ProviderKind.OpenAICompatible, "OpenAI 兼容服务"),
        (ProviderKind.Demo, "离线演示"),
    ];

    private readonly SettingsService _settings;
    private readonly Dictionary<ProviderKind, Draft> _drafts = [];
    private OptionButton _provider = null!;
    private LineEdit _endpoint = null!;
    private LineEdit _model = null!;
    private Button _detect = null!;
    private PopupMenu _models = null!;
    private LineEdit _key = null!;
    private Label _keyNote = null!;
    private TextEdit _prompt = null!;
    private Label _error = null!;
    private ProviderKind _editing;
    private CancellationTokenSource? _detecting;

    internal SettingsWindow(SettingsService settings)
    {
        _settings = settings;
        Visible = false;
        ForceNative = true;
        Unresizable = true;
        Title = "Just Neko 系统设置";
        Size = new Vector2I(424, 500);
        Theme = PetTheme.Create();
    }

    public override void _Ready()
    {
        CloseRequested += Dismiss;
        var surface = new PanelContainer();
        surface.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Background, 0, 22, 19));
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var fields = new VBoxContainer();
        fields.AddThemeConstantOverride("separation", 9);
        surface.AddChild(fields);
        fields.AddChild(PetTheme.Label("模型服务", PetTheme.Ink, 19));
        _provider = new OptionButton { Name = "ModelProvider" };
        foreach (var (_, label) in Providers)
            _provider.AddItem(label);
        _provider.ItemSelected += SelectProvider;
        fields.AddChild(_provider);

        var connected = new VBoxContainer();
        connected.AddThemeConstantOverride("separation", 6);
        fields.AddChild(connected);
        _endpoint = AddField(connected, "Endpoint", "服务地址", "例如 http://localhost:11434/v1");
        _model = AddField(connected, "ModelName", "模型", "服务中的模型名称", out var modelRow);
        _detect = new Button { Name = "DetectModels", Text = "检测", TooltipText = "列出服务上可用的模型", CustomMinimumSize = new Vector2(56, 34) };
        _detect.Pressed += DetectModels;
        modelRow.AddChild(_detect);
        _models = new PopupMenu { Name = "DetectedModels" };
        _models.IndexPressed += index => _model.Text = _models.GetItemText((int)index);
        AddChild(_models);
        _key = AddField(connected, "ApiKey", "API Key", "按服务要求填写");
        _key.Secret = true;
        _keyNote = PetTheme.Label("", PetTheme.Muted, 12);
        _keyNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        connected.AddChild(_keyNote);

        fields.AddChild(PetTheme.Label("系统提示词", PetTheme.Muted));
        _prompt = new TextEdit
        {
            Name = "RolePrompt",
            CustomMinimumSize = new Vector2(0, 75),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        _prompt.AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Surface, 8, 10, 7));
        fields.AddChild(_prompt);

        _error = PetTheme.Label("", PetTheme.Accent, 13);
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _error.Hide();
        fields.AddChild(_error);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        fields.AddChild(actions);
        var cancel = new Button { Text = "取消", CustomMinimumSize = new Vector2(64, 34) };
        cancel.Pressed += Dismiss;
        actions.AddChild(cancel);
        var apply = new Button { Name = "ApplySettings", Text = "应用", CustomMinimumSize = new Vector2(76, 34) };
        apply.Pressed += Apply;
        actions.AddChild(apply);
    }

    public override void _ExitTree() => _detecting?.Cancel();

    internal void Open(Rect2I screenArea)
    {
        var current = _settings.Current;
        _drafts.Clear();
        foreach (var (kind, _) in Providers)
        {
            var endpoint = current.EndpointFor(kind);
            _drafts[kind] = new Draft(endpoint.BaseUrl, endpoint.Model, _settings.GetApiKey(kind));
        }
        _editing = current.ChatProvider;
        _provider.Select(IndexOf(_editing));
        ShowDraft(_editing);
        _prompt.Text = current.RolePrompt;
        if (_settings.LoadError is { } loadError)
            ShowError(loadError);
        else
            _error.Hide();
        if (screenArea.HasArea())
            Position = ScreenMath.ClampToScreen(screenArea.Position + (screenArea.Size - Size) / 2, Size, screenArea);
        Show();
        GrabFocus();
    }

    private void Dismiss()
    {
        _detecting?.Cancel();
        Hide();
    }

    private void SelectProvider(long index)
    {
        KeepDraft();
        _editing = Providers[index].Kind;
        ShowDraft(_editing);
        _error.Hide();
    }

    private void ShowDraft(ProviderKind kind)
    {
        var draft = _drafts[kind];
        _endpoint.Text = draft.BaseUrl;
        _model.Text = draft.Model;
        _key.Text = draft.Key;
        var connected = kind != ProviderKind.Demo;
        _endpoint.Editable = connected;
        _model.Editable = connected;
        _key.Editable = connected;
        _detect.Disabled = !connected;
        _model.PlaceholderText = kind == ProviderKind.Ollama ? "留空则自动使用已安装的第一个模型" : "服务中的模型名称";
        _keyNote.Text = !connected ? "" : kind == ProviderKind.Ollama ? "本地 Ollama 一般不需要 Key。"
            : _settings.KeysPersist ? "Key 加密保存在本机，只有当前系统用户能解开。" : "当前系统暂不支持安全保存 Key，重启后需要重新填写。";
    }

    private void KeepDraft() =>
        _drafts[_editing] = new Draft(_endpoint.Text.Trim(), _model.Text.Trim(), _key.Text.Trim());

    private async void DetectModels()
    {
        _detecting?.Cancel();
        using var detecting = new CancellationTokenSource();
        _detecting = detecting;
        _detect.Disabled = true;
        _detect.Text = "检测中";
        try
        {
            var models = await ModelCatalog.ListAsync(_endpoint.Text.Trim(), _key.Text.Trim(), detecting.Token);
            if (detecting.IsCancellationRequested || !IsInsideTree())
                return;
            if (models.Count == 0)
            {
                ShowError(_editing == ProviderKind.Ollama ? "本地 Ollama 里还没有模型。请先用 ollama pull 下载一个聊天模型。" : "服务没有返回可用的模型。");
                return;
            }
            _error.Hide();
            _models.Clear();
            foreach (var model in models)
                _models.AddItem(model);
            _models.Popup(new Rect2I(Position + (Vector2I)_model.GlobalPosition + new Vector2I(0, (int)_model.Size.Y), new Vector2I((int)_model.Size.X, 0)));
        }
        catch (OperationCanceledException) when (detecting.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (IsInsideTree())
                ShowError(error is ArgumentException ? error.Message : ModelErrors.Describe(error, _editing));
        }
        finally
        {
            if (_detecting == detecting)
                _detecting = null;
            if (IsInsideTree())
            {
                _detect.Text = "检测";
                _detect.Disabled = _editing == ProviderKind.Demo;
            }
        }
    }

    private void Apply()
    {
        KeepDraft();
        var draft = _drafts[_editing];
        if (_editing != ProviderKind.Demo)
        {
            if (_editing != ProviderKind.Ollama && draft.Model.Length == 0)
            {
                ShowError("请填写模型名称。");
                return;
            }
            try
            {
                EndpointUrl.Normalize(draft.BaseUrl);
            }
            catch (ArgumentException error)
            {
                ShowError(error.Message);
                return;
            }
        }

        var next = _settings.Current with { ChatProvider = _editing, RolePrompt = _prompt.Text.Trim() };
        foreach (var (kind, _) in Providers)
        {
            if (kind == ProviderKind.Demo)
                continue;
            var edited = _drafts[kind];
            next = next.WithEndpoint(new ModelEndpoint(kind, edited.BaseUrl, edited.Model));
        }

        var failure = _settings.Update(next);
        foreach (var (kind, _) in Providers)
        {
            if (kind != ProviderKind.Demo && _drafts[kind].Key != _settings.GetApiKey(kind))
                failure ??= _settings.SetApiKey(kind, _drafts[kind].Key);
        }
        if (failure is not null)
        {
            ShowError(failure + " 本次运行仍按新设置使用。");
            return;
        }
        Dismiss();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Show();
    }

    private static int IndexOf(ProviderKind kind) => Array.FindIndex(Providers, provider => provider.Kind == kind);

    private static LineEdit AddField(Control parent, string name, string caption, string placeholder) =>
        AddField(parent, name, caption, placeholder, out _);

    private static LineEdit AddField(Control parent, string name, string caption, string placeholder, out HBoxContainer row)
    {
        parent.AddChild(PetTheme.Label(caption, PetTheme.Muted, 13));
        row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);
        var control = new LineEdit
        {
            Name = name,
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        control.AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Surface, 8, 9, 4));
        row.AddChild(control);
        return control;
    }

    private sealed record Draft(string BaseUrl, string Model, string Key);
}
