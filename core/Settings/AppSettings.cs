using JustNeko.Core.Ai;

namespace JustNeko.Core.Settings;

public sealed record AppSettings
{
    public const int CurrentVersion = 1;
    public const string DefaultRolePrompt = "你是桌宠 Neko。用简短、自然的中文陪用户聊天。";

    public int Version { get; init; } = CurrentVersion;

    /// <summary>聊天用哪个服务。默认用内置本地模型；首次启动引导检测到本机 Ollama 时改用 Ollama。</summary>
    public ProviderKind ChatProvider { get; init; } = ProviderKind.BuiltIn;

    /// <summary>首次启动引导是否已经走完。</summary>
    public bool OnboardingDone { get; init; }

    /// <summary>每种服务各记一份地址和模型，切换服务再切回来时不用重填。Key 不在这里，交给平台的凭据存储。</summary>
    public IReadOnlyDictionary<ProviderKind, ModelEndpoint> Endpoints { get; init; } = new Dictionary<ProviderKind, ModelEndpoint>();

    public string RolePrompt { get; init; } = DefaultRolePrompt;

    /// <summary>前台有全屏程序（游戏、演示）时自动藏起角色。</summary>
    public bool HideWhenFullscreen { get; init; } = true;

    /// <summary>勿扰：角色留在桌面，但不主动做事（自主活动、习惯、主动搭话都暂停）。</summary>
    public bool QuietMode { get; init; }

    /// <summary>把聊天记录存在本机，重启后还能看到、模型也记得最近几轮。</summary>
    public bool SaveChatHistory { get; init; } = true;

    public ModelEndpoint EndpointFor(ProviderKind kind) =>
        Endpoints.TryGetValue(kind, out var endpoint) ? endpoint with { Kind = kind } : ModelEndpoint.DefaultFor(kind);

    // 目前只有聊天任务；加视觉、Embedding 时在这里按任务分别取。
    public ModelEndpoint EndpointFor(ModelTask task) => EndpointFor(ChatProvider);

    public AppSettings WithEndpoint(ModelEndpoint endpoint) => this with
    {
        Endpoints = new Dictionary<ProviderKind, ModelEndpoint>(Endpoints) { [endpoint.Kind] = endpoint },
    };
}
