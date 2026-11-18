using Godot;

namespace JustNeko.Shell.Settings;

/// <summary>隐私：聊天记录存不存、清不清；交互留痕和 Key 存在哪。</summary>
public sealed partial class PrivacyPage : SettingsPage
{
    private readonly PetServices _services;
    private Label _result = null!;

    internal PrivacyPage(PetServices services)
    {
        _services = services;
        Name = "PrivacyPage";
    }

    /// <summary>用户清除了聊天记录。</summary>
    internal event Action? HistoryCleared;

    public override void _Ready()
    {
        var settings = _services.Settings;
        AddHeading("隐私", "所有数据都只存在这台电脑上。用云端模型服务时，聊天内容会发给你填写的那个服务。");
        AddToggle("SaveChatHistory", "保存聊天记录", "重启后还能看到之前的聊天，桌宠也记得最近几轮。关掉会立即删除已保存的记录。",
            settings.Current.SaveChatHistory, value =>
            {
                settings.Update(settings.Current with { SaveChatHistory = value });
                _services.ChatHistory.SetPersist(value);
            });
        var clear = new Button { Name = "ClearChatHistory", Text = "清除聊天记录", CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        clear.Pressed += () =>
        {
            HistoryCleared?.Invoke();
            Flash(_result, "✓ 聊天记录已清除。", PetTheme.Good);
        };
        AddChild(clear);
        _result = PetTheme.Note("");
        _result.Hide();
        AddChild(_result);

        AddSection("交互留痕");
        AddChild(PetTheme.Note("每次对话记一行耗时和结果（不含聊天内容），用来排查「为什么慢、为什么失败」。"));
        var logs = new Button { Name = "OpenLogs", Text = "打开日志文件夹", CustomMinimumSize = new Vector2(0, 34), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        logs.Pressed += () =>
        {
            var path = System.IO.Path.Combine(_services.DataDirectory, "logs");
            System.IO.Directory.CreateDirectory(path);
            OS.ShellOpen(path);
        };
        AddChild(logs);

        AddSection("API Key");
        AddChild(PetTheme.Note(settings.KeysPersist
            ? "Key 用系统加密保存，只有当前系统用户能解开，设置文件里没有明文。"
            : "当前系统暂不支持安全保存 Key，只在本次运行里记住。"));
    }
}
