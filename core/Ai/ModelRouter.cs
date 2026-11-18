using JustNeko.Core.Settings;
using Microsoft.Extensions.AI;

namespace JustNeko.Core.Ai;

/// <summary>按任务从设置里解析出要用的服务和模型，并复用已经建好的客户端。</summary>
public sealed class ModelRouter(SettingsService settings) : IDisposable
{
    private (string Key, IChatClient Client)? _cached;

    /// <summary>当前设置下这个任务会用哪个服务（不建连接）。</summary>
    public ModelEndpoint Peek(ModelTask task) => settings.Current.EndpointFor(task);

    public async Task<ModelRoute> ResolveAsync(ModelTask task, CancellationToken cancellationToken)
    {
        var endpoint = settings.Current.EndpointFor(task);
        var apiKey = settings.GetApiKey(endpoint.Kind);
        if (endpoint.NeedsConnection && endpoint.Model.Length == 0)
            endpoint = await PickInstalledModelAsync(endpoint, apiKey, cancellationToken);

        var cacheKey = $"{endpoint.Kind}|{endpoint.BaseUrl}|{endpoint.Model}|{apiKey}";
        if (_cached?.Key != cacheKey)
        {
            _cached?.Client.Dispose();
            _cached = (cacheKey, ChatClientFactory.Create(endpoint, apiKey));
        }
        return new ModelRoute(endpoint, _cached.Value.Client);
    }

    // 默认用本地 Ollama：没指定模型时用已安装的第一个，并记进设置，下次不用再查。
    private async Task<ModelEndpoint> PickInstalledModelAsync(ModelEndpoint endpoint, string apiKey, CancellationToken cancellationToken)
    {
        if (endpoint.Kind != ProviderKind.Ollama)
            throw new InvalidOperationException("还没有填写模型名称。请在设置里填写。");

        var models = await ModelCatalog.ListAsync(endpoint.BaseUrl, apiKey, cancellationToken);
        if (models.Count == 0)
            throw new InvalidOperationException("本地 Ollama 里还没有模型。请先用 ollama pull 下载一个聊天模型。");

        var picked = endpoint with { Model = models[0] };
        settings.Update(settings.Current.WithEndpoint(picked));
        return picked;
    }

    public void Dispose()
    {
        _cached?.Client.Dispose();
        _cached = null;
    }
}
