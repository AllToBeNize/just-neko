using System.Text;

namespace JustNeko.Core.Interaction;

public abstract record StreamToken;

public sealed record TextToken(string Text) : StreamToken;

public sealed record DirectiveToken(string Name, string Value) : StreamToken;

/// <summary>
/// 从流式回复里拆出 [名称:值] 形式的指令，其余原样作为正文。标签可以出现在任何位置、可以被拆在几段里；
/// 只认登记过的名称，其他方括号内容照常显示。
/// </summary>
public sealed class DirectiveParser(IEnumerable<string> names)
{
    private const int TagLimit = 64;
    private readonly HashSet<string> _names = new(names, StringComparer.OrdinalIgnoreCase);
    private readonly StringBuilder _text = new();
    private readonly StringBuilder _tag = new();

    public IReadOnlyList<StreamToken> Read(string piece)
    {
        var tokens = new List<StreamToken>();
        foreach (var character in piece)
            Accept(character, tokens);
        FlushText(tokens);
        return tokens;
    }

    /// <summary>流结束时调用，把还没闭合的半截标签当作正文交出去。</summary>
    public IReadOnlyList<StreamToken> Complete()
    {
        var tokens = new List<StreamToken>();
        _text.Append(_tag);
        _tag.Clear();
        FlushText(tokens);
        return tokens;
    }

    private void Accept(char character, List<StreamToken> tokens)
    {
        if (_tag.Length == 0)
        {
            if (character == '[')
                _tag.Append(character);
            else
                _text.Append(character);
            return;
        }

        if (character == '[')
        {
            _text.Append(_tag);
            _tag.Clear().Append(character);
            return;
        }

        _tag.Append(character);
        if (character == ']')
        {
            var body = _tag.ToString(1, _tag.Length - 2);
            var colon = body.IndexOf(':');
            var name = colon > 0 ? body[..colon].Trim() : "";
            var value = colon > 0 ? body[(colon + 1)..].Trim() : "";
            if (value.Length > 0 && _names.Contains(name))
            {
                FlushText(tokens);
                tokens.Add(new DirectiveToken(name.ToLowerInvariant(), value));
            }
            else
            {
                _text.Append(_tag);
            }
            _tag.Clear();
        }
        else if (_tag.Length > TagLimit || !CouldBeDirective())
        {
            _text.Append(_tag);
            _tag.Clear();
        }
    }

    // 还没看到冒号时，已有的部分必须是某个指令名的开头，否则立即当作正文，不拖慢显示。
    private bool CouldBeDirective()
    {
        var body = _tag.ToString(1, _tag.Length - 1);
        var colon = body.IndexOf(':');
        var name = (colon >= 0 ? body[..colon] : body).Trim();
        return colon >= 0
            ? _names.Contains(name)
            : _names.Any(candidate => candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase));
    }

    private void FlushText(List<StreamToken> tokens)
    {
        if (_text.Length == 0)
            return;
        tokens.Add(new TextToken(_text.ToString()));
        _text.Clear();
    }
}
