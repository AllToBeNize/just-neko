namespace JustNeko.Core.Ai;

public static class ModelCatalog
{
    /// <summary>列出服务上可用的模型（OpenAI 兼容的 GET /models，Ollama 返回本机已安装的模型）。</summary>
    public static async Task<IReadOnlyList<string>> ListAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        var client = ChatClientFactory.CreateOpenAIClient(baseUrl, apiKey).GetOpenAIModelClient();
        var models = await client.GetModelsAsync(cancellationToken);
        return models.Value.Select(model => model.Id).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
