using JustNeko.Core.Ai;
using JustNeko.Core.Interaction;
using JustNeko.Core.LocalModel;
using JustNeko.Core.Platform;
using JustNeko.Core.Settings;
using JustNeko.Pet;
using JustNeko.Pet.Activities;

namespace JustNeko.Shell;

/// <summary>各个窗口共用的服务，由 <see cref="DesktopPet"/> 在启动时创建。</summary>
internal sealed record PetServices(
    IPlatformServices Platform,
    SettingsService Settings,
    LocalModelManager LocalModel,
    ModelRouter Models,
    InteractionOrchestrator Interactions,
    ChatHistoryStore ChatHistory,
    BehaviorCatalog Behaviors,
    AppearanceStore Appearance,
    ActivityDirector Activities,
    string DataDirectory)
{
    /// <summary>当前角色。热切换角色时会换成新的对象。</summary>
    public PetAvatar Avatar { get; private set; } = null!;

    /// <summary>换了角色（设置页借此刷新动作绑定列表）。</summary>
    public event Action? AvatarChanged;

    public void SetAvatar(PetAvatar avatar)
    {
        Avatar = avatar;
        AvatarChanged?.Invoke();
    }
}
