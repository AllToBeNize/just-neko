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
    PetAvatar Avatar,
    string DataDirectory);
