namespace JustNeko.Core.Ai;

public static class EndpointUrl
{
    private const string ChatPath = "/chat/completions";

    /// <summary>把用户填的地址整理成 OpenAI 兼容接口的基础地址。填完整的 …/chat/completions 也接受。</summary>
    public static Uri Normalize(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https")
            || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
        {
            throw new ArgumentException("请输入 HTTP 或 HTTPS 服务地址，例如 http://localhost:11434/v1。");
        }

        var path = url.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(ChatPath, StringComparison.Ordinal))
            path = path[..^ChatPath.Length];
        return new UriBuilder(url) { Path = path }.Uri;
    }
}
