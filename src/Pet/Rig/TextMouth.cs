using Godot;

namespace JustNeko.Pet.Rig;

/// <summary>嘴型的来源。现在按文字节奏模拟；有了语音以后换成按播放音量驱动。</summary>
internal interface IMouthSource
{
    /// <summary>每帧调用，返回 0..1 的张嘴程度。</summary>
    float Sample(double delta);
}

/// <summary>
/// 按回复文字流出的节奏开合嘴巴：每个字一开一合，标点处停顿，积压太多时加快，回复结束后把剩下的说完再闭嘴。
/// </summary>
internal sealed class TextMouth : IMouthSource
{
    private const double SyllableSeconds = 0.13;
    private const double PauseSeconds = 0.22;
    private const int Backlog = 12;
    private const int FinishSyllables = 6;
    private readonly Queue<char> _pending = new();
    private readonly RandomNumberGenerator _random = new();
    private double _left;
    private double _length;
    private float _peak;
    private float _value;

    /// <summary>回复里新增的文字。</summary>
    public void Feed(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character))
                _pending.Enqueue(character);
        }
    }

    /// <summary>
    /// 回复说完了：没有语音时文字已经全部显示出来，只把最后几个字念完（不到 1 秒）就闭嘴，免得嘴比文字慢太多。
    /// </summary>
    public void Finish()
    {
        while (_pending.Count > FinishSyllables)
            _pending.Dequeue();
    }

    /// <summary>停止或清空时立刻闭嘴。</summary>
    public void Stop()
    {
        _pending.Clear();
        _left = 0;
        _peak = 0;
    }

    public float Sample(double delta)
    {
        _left -= delta;
        if (_left <= 0 && _pending.Count > 0)
        {
            var character = _pending.Dequeue();
            var pause = char.IsPunctuation(character) || "～…—".Contains(character);
            var speed = _pending.Count > Backlog ? 1.6 : 1.0;
            _length = (pause ? PauseSeconds : SyllableSeconds) / speed;
            _left = _length;
            _peak = pause ? 0 : _random.RandfRange(0.55f, 1f);
        }
        // 每个字前 60% 的时间张开、后面合上，再平滑一下避免抖动。
        var phase = _left > 0 && _length > 0 ? 1 - _left / _length : 1;
        var target = _left > 0 && phase < 0.6 ? _peak : 0;
        _value = Mathf.Lerp(_value, target, 1 - Mathf.Exp(-28f * (float)delta));
        return _value;
    }
}
