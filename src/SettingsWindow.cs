using Godot;

namespace JustNeko;

internal sealed record ChatSettings(int Provider, string Endpoint, string Model, string Key, string Prompt);

public partial class SettingsWindow : Window
{
    private const string DefaultPrompt = "你是桌宠 Neko。用简短、自然的中文陪用户聊天。";
    private const string OllamaEndpoint = "http://localhost:11434/v1";
    private OptionButton _provider = null!;
    private LineEdit _endpoint = null!;
    private LineEdit _model = null!;
    private LineEdit _key = null!;
    private TextEdit _prompt = null!;
    private Label _error = null!;

    internal ChatSettings Current { get; private set; } = new(0, "", "", "", DefaultPrompt);

    public SettingsWindow()
    {
        Visible = false;
        ForceNative = true;
        Unresizable = true;
        Title = "Just Neko 系统设置";
        Size = new Vector2I(424, 470);
        Theme = PetTheme.Create();
    }

    public override void _Ready()
    {
        CloseRequested += Hide;
        var surface = new PanelContainer();
        surface.AddThemeStyleboxOverride("panel", PetTheme.Box(PetTheme.Background, 0, 22, 19));
        AddChild(surface);
        surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var fields = new VBoxContainer();
        fields.AddThemeConstantOverride("separation", 9);
        surface.AddChild(fields);
        fields.AddChild(PetTheme.Label("模型服务", PetTheme.Ink, 19));
        _provider = new OptionButton { Name = "ModelProvider" };
        _provider.AddItem("离线演示");
        _provider.AddItem("本地 Ollama");
        _provider.AddItem("OpenAI 兼容服务");
        _provider.ItemSelected += SelectProvider;
        fields.AddChild(_provider);

        var connected = new VBoxContainer();
        connected.AddThemeConstantOverride("separation", 6);
        fields.AddChild(connected);
        _endpoint = AddField(connected, "Endpoint", "服务地址", "例如 http://localhost:11434/v1");
        _model = AddField(connected, "ModelName", "模型", "服务中的模型名称");
        _key = AddField(connected, "ApiKey", "API Key", "按服务要求填写");
        _key.Secret = true;

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
        cancel.Pressed += Hide;
        actions.AddChild(cancel);
        var apply = new Button { Name = "ApplySettings", Text = "应用", CustomMinimumSize = new Vector2(76, 34) };
        apply.Pressed += Apply;
        actions.AddChild(apply);
    }

    internal void Open(Rect2I screenArea)
    {
        _provider.Select(Current.Provider);
        _endpoint.Text = Current.Endpoint;
        _model.Text = Current.Model;
        _key.Text = Current.Key;
        _prompt.Text = Current.Prompt;
        SetConnectionEditable(Current.Provider != 0);
        _error.Hide();
        if (screenArea.HasArea())
            Position = DesktopPet.ClampToScreen(screenArea.Position + (screenArea.Size - Size) / 2, Size, screenArea);
        Show();
        GrabFocus();
    }

    private void SelectProvider(long index)
    {
        var sameProvider = index == Current.Provider;
        _endpoint.Text = sameProvider ? Current.Endpoint : index == 1 ? OllamaEndpoint : "";
        _model.Text = sameProvider ? Current.Model : "";
        _key.Text = sameProvider ? Current.Key : "";
        SetConnectionEditable(index != 0);
        _error.Hide();
    }

    private void SetConnectionEditable(bool enabled)
    {
        _endpoint.Editable = enabled;
        _model.Editable = enabled;
        _key.Editable = enabled;
    }

    private void Apply()
    {
        var next = new ChatSettings(_provider.Selected, _endpoint.Text.Trim(), _model.Text.Trim(),
            _key.Text.Trim(), _prompt.Text.Trim());
        if (next.Provider != 0)
        {
            if (next.Model.Length == 0)
            {
                ShowError("请填写模型名称。");
                return;
            }
            try
            {
                ChatClient.ResolveEndpoint(next.Endpoint);
            }
            catch (ArgumentException error)
            {
                ShowError(error.Message);
                return;
            }
        }
        Current = next;
        Hide();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Show();
    }

    private static LineEdit AddField(Control parent, string name, string caption, string placeholder)
    {
        parent.AddChild(PetTheme.Label(caption, PetTheme.Muted, 13));
        var control = new LineEdit { Name = name, PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 34) };
        control.AddThemeStyleboxOverride("normal", PetTheme.Box(PetTheme.Surface, 8, 9, 4));
        parent.AddChild(control);
        return control;
    }
}
