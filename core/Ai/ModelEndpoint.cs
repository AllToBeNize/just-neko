using Microsoft.Extensions.AI;

namespace JustNeko.Core.Ai;

public enum ProviderKind
{
    Ollama,
    OpenAICompatible,
    Demo,
}

/// <summary>需要模型的任务。以后加视觉、Embedding、语音时各自绑定服务，几项任务可以共用同一个连接。</summary>
public enum ModelTask
{
    Chat,
}

public sealed record ModelEndpoint(ProviderKind Kind, string BaseUrl, string Model)
{
    public const string OllamaBaseUrl = "http://localhost:11434/v1";

    public bool NeedsConnection => Kind != ProviderKind.Demo;

    public static ModelEndpoint DefaultFor(ProviderKind kind) =>
        new(kind, kind == ProviderKind.Ollama ? OllamaBaseUrl : "", "");
}

/// <summary>一次解析出来的模型路由。Key 相同说明还是同一个服务和模型，可以继续用之前的对话。</summary>
public sealed record ModelRoute(ModelEndpoint Endpoint, IChatClient Client)
{
    public string Key => $"{Endpoint.Kind}|{Endpoint.BaseUrl}|{Endpoint.Model}";
}
