using JustNeko.Core.LocalModel;
using JustNeko.Core.Settings;
using Microsoft.Extensions.AI;

namespace JustNeko.Core.Ai;

/// <summary>按任务从设置里解析出要用的服务和模型，并复用已经建好的客户端。</summary>
public sealed class ModelRouter(SettingsService settings, LocalModelManager localModel) : IDisposable
{
    private (string Key, IChatClient Client)? _cached;

    /// <summary>当前设置下这个任务会用哪个服务（不建连接）。</summary>
    public ModelEndpoint Peek(ModelTask task) => settings.Current.EndpointFor(task);

    public async Task<ModelRoute> ResolveAsync(ModelTask task, CancellationToken cancellationToken)
    {
        var endpoint = await PrepareAsync(settings.Current.EndpointFor(task), cancellationToken);
        var apiKey = settings.GetApiKey(endpoint.Kind);
        var cacheKey = $"{endpoint.Kind}|{endpoint.BaseUrl}|{endpoint.Model}|{apiKey}";
        if (_cached?.Key != cacheKey)
        {
            _cached?.Client.Dispose();
            _cached = (cacheKey, ChatClientFactory.Create(endpoint, apiKey));
        }
        return new ModelRoute(endpoint, _cached.Value.Client);
    }

    /// <summary>
    /// 测试一个（还没保存的）服务配置能不能用：能连上、模型存在。成功返回给用户看的一句话，失败抛异常。
    /// </summary>
    public async Task<string> TestAsync(ModelEndpoint endpoint, string apiKey, CancellationToken cancellationToken)
    {
        switch (endpoint.Kind)
        {
            case ProviderKind.Demo:
                return "离线演示不需要连接。";
            case ProviderKind.BuiltIn:
                await localModel.EnsureServerAsync(cancellationToken);
                return "内置本地模型已经启动，可以聊天了。";
        }

        var models = await ModelCatalog.ListAsync(endpoint.BaseUrl, apiKey, cancellationToken);
        if (endpoint.Model.Length == 0)
        {
            return models.Count == 0
                ? throw new InvalidOperationException("连上了，但服务上还没有模型。")
                : $"连上了，会自动使用 {models[0]}。";
        }
        // 有些 OpenAI 兼容服务不在 /models 里列出全部模型，列表为空时不当作失败。
        return models.Count == 0 || models.Contains(endpoint.Model, StringComparer.OrdinalIgnoreCase)
            ? $"连上了，模型 {endpoint.Model} 可用。"
            : throw new InvalidOperationException($"连上了，但服务上没有模型 {endpoint.Model}。可以点「检测」从可用的模型里选。");
    }

    private async Task<ModelEndpoint> PrepareAsync(ModelEndpoint endpoint, CancellationToken cancellationToken)
    {
        switch (endpoint.Kind)
        {
            case ProviderKind.BuiltIn:
                var url = await localModel.EnsureServerAsync(cancellationToken);
                return endpoint with { BaseUrl = url.ToString(), Model = ModelEndpoint.BuiltInModel };
            case ProviderKind.OpenAICompatible when endpoint.Model.Length == 0:
                throw new InvalidOperationException("还没有填写模型名称。请在设置中心的「对话」里填写。");
            case ProviderKind.Ollama when endpoint.Model.Length == 0:
                return await PickInstalledModelAsync(endpoint, cancellationToken);
            default:
                return endpoint;
        }
    }

    // 没指定 Ollama 模型时用已安装的第一个，并记进设置，下次不用再查。
    private async Task<ModelEndpoint> PickInstalledModelAsync(ModelEndpoint endpoint, CancellationToken cancellationToken)
    {
        var models = await ModelCatalog.ListAsync(endpoint.BaseUrl, settings.GetApiKey(endpoint.Kind), cancellationToken);
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
