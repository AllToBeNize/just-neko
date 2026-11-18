using Microsoft.Extensions.AI;

namespace JustNeko.Core.Ai;

public enum ProviderKind
{
    /// <summary>桌宠自带的 llama-server + 小模型，由 <see cref="LocalModel.LocalModelManager"/> 下载和启动。</summary>
    BuiltIn,
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

    public const string BuiltInModel = "neko-local";

    /// <summary>需要用户自己填地址和模型的服务。</summary>
    public bool IsExternal => Kind is ProviderKind.Ollama or ProviderKind.OpenAICompatible;

    public static ModelEndpoint DefaultFor(ProviderKind kind) =>
        new(kind, kind == ProviderKind.Ollama ? OllamaBaseUrl : "", "");
}

/// <summary>一次解析出来的模型路由。Key 相同说明还是同一个服务和模型，可以继续用之前的对话。</summary>
public sealed record ModelRoute(ModelEndpoint Endpoint, IChatClient Client)
{
    public string Key => $"{Endpoint.Kind}|{Endpoint.BaseUrl}|{Endpoint.Model}";
}
