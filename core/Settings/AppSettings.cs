using JustNeko.Core.Ai;

namespace JustNeko.Core.Settings;

public sealed record AppSettings
{
    public const int CurrentVersion = 1;
    public const string DefaultRolePrompt = "你是桌宠 Neko。用简短、自然的中文陪用户聊天。";

    public int Version { get; init; } = CurrentVersion;

    /// <summary>聊天用哪个服务。默认用本地 Ollama。</summary>
    public ProviderKind ChatProvider { get; init; } = ProviderKind.Ollama;

    /// <summary>每种服务各记一份地址和模型，切换服务再切回来时不用重填。Key 不在这里，交给平台的凭据存储。</summary>
    public IReadOnlyDictionary<ProviderKind, ModelEndpoint> Endpoints { get; init; } = new Dictionary<ProviderKind, ModelEndpoint>();

    public string RolePrompt { get; init; } = DefaultRolePrompt;

    public ModelEndpoint EndpointFor(ProviderKind kind) =>
        Endpoints.TryGetValue(kind, out var endpoint) ? endpoint with { Kind = kind } : ModelEndpoint.DefaultFor(kind);

    // 目前只有聊天任务；加视觉、Embedding 时在这里按任务分别取。
    public ModelEndpoint EndpointFor(ModelTask task) => EndpointFor(ChatProvider);

    public AppSettings WithEndpoint(ModelEndpoint endpoint) => this with
    {
        Endpoints = new Dictionary<ProviderKind, ModelEndpoint>(Endpoints) { [endpoint.Kind] = endpoint },
    };
}
