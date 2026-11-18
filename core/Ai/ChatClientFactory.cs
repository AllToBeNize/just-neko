using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace JustNeko.Core.Ai;

public static class ChatClientFactory
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(30);

    public static IChatClient Create(ModelEndpoint endpoint, string apiKey) => endpoint.Kind switch
    {
        ProviderKind.Demo => new DemoChatClient(),
        _ => CreateOpenAIClient(endpoint.BaseUrl, apiKey).GetChatClient(endpoint.Model).AsIChatClient(),
    };

    // Ollama 和大部分本地服务都走 OpenAI 兼容接口。没有 Key 时随便给一个占位值，这些服务会忽略它。
    internal static OpenAIClient CreateOpenAIClient(string baseUrl, string apiKey) => new(
        new ApiKeyCredential(apiKey.Length > 0 ? apiKey : "no-key"),
        new OpenAIClientOptions
        {
            Endpoint = EndpointUrl.Normalize(baseUrl),
            NetworkTimeout = NetworkTimeout,
            // 桌宠要尽快告诉用户连不上，只重试一次。
            RetryPolicy = new ClientRetryPolicy(maxRetries: 1),
        });
}
