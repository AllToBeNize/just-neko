using Godot;

namespace JustNeko.Shell.Settings;

public sealed partial class AboutPage : SettingsPage
{
    internal AboutPage() => Name = "AboutPage";

    public override void _Ready()
    {
        AddHeading("关于 Just Neko", "一只住在桌面上的 AI 桌宠。");
        AddSection("用到的开源组件和素材");
        AddChild(PetTheme.Note(
            "Godot Engine — MIT\n" +
            "gd_cubism — MIT\n" +
            "godot-vrm、Godot-MToon-Shader — MIT\n" +
            "Live2D Cubism SDK — Live2D 专有软件许可\n" +
            "llama.cpp — MIT\n" +
            "Qwen3 1.7B — Apache-2.0\n" +
            "OpenAI .NET SDK、Microsoft.Extensions.AI — MIT\n" +
            "示例角色 Mao — Live2D 示例素材（对外发布前替换为自有角色）\n" +
            "示例 VRM 角色 — pixiv Inc.（VRM 1.0 许可，允许商用和再分发）"));
    }
}
