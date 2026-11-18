using System.ClientModel;
using System.Text.Json;

namespace JustNeko.Core.Ai;

public static class ModelErrors
{
    /// <summary>把模型调用的异常翻译成用户能照着做的一句话。</summary>
    public static string Describe(Exception error, ProviderKind kind) => Unwrap(error) switch
    {
        ClientResultException { Status: 401 or 403 } => "模型服务拒绝了请求。请检查 API Key。",
        ClientResultException { Status: 404 } => "服务上找不到这个模型或接口。请检查服务地址和模型名。",
        ClientResultException { Status: > 0 } result => $"模型服务返回 HTTP {result.Status}。请检查地址、模型和 Key。",
        ClientResultException or HttpRequestException => kind == ProviderKind.Ollama
            ? "连不上本地 Ollama。请确认 Ollama 已经启动，或在设置里换一个服务。"
            : "连不上模型服务。请检查网络和服务地址。",
        JsonException => "模型返回了无法解析的流式数据。",
        var other => other.Message,
    };

    // SDK 重试失败后把每次的错误包成 AggregateException，取第一次的真实原因。
    private static Exception Unwrap(Exception error) =>
        error is AggregateException { InnerExceptions: [var first, ..] } ? Unwrap(first) : error;
}
