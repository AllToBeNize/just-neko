using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustNeko.Core.Interaction;

/// <summary>
/// 交互留痕：每次交互结束写一行 JSON 到 logs/interactions-日期.jsonl，记录来源、首字延迟、耗时和结果，
/// 用来事后还原「卡在哪、为什么失败」。只记长度，不记对话内容。
/// </summary>
public sealed class InteractionLog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly Func<string> _route;
    private readonly Dictionary<long, Entry> _open = [];

    public InteractionLog(string directory, InteractionOrchestrator orchestrator, Func<string> route)
    {
        _directory = directory;
        _route = route;
        orchestrator.Changed += Record;
    }

    private void Record(InteractionEvent change)
    {
        switch (change)
        {
            case InteractionStarted started:
                _open[started.InteractionId] = new Entry(started.Request.Source, DateTimeOffset.Now, Stopwatch.StartNew());
                break;
            case ReplyUpdated updated when _open.TryGetValue(updated.InteractionId, out var entry):
                entry.FirstTextMs ??= entry.Clock.ElapsedMilliseconds;
                break;
            case InteractionEnded ended when _open.Remove(ended.InteractionId, out var entry):
                Write(new
                {
                    id = ended.InteractionId,
                    at = entry.StartedAt,
                    source = entry.Source,
                    route = _route(),
                    outcome = ended.Outcome,
                    firstTextMs = entry.FirstTextMs,
                    totalMs = entry.Clock.ElapsedMilliseconds,
                    replyChars = ended.Text.Length,
                    error = ended.Error,
                });
                break;
        }
    }

    private void Write(object record)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, $"interactions-{DateTime.Now:yyyyMMdd}.jsonl");
            File.AppendAllText(path, JsonSerializer.Serialize(record, Json) + "\n", new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // 留痕写不进去不影响桌宠使用，只在调试输出里提示。
            Debug.WriteLine($"交互留痕写入失败：{error.Message}");
        }
    }

    private sealed class Entry(InteractionSource source, DateTimeOffset startedAt, Stopwatch clock)
    {
        public InteractionSource Source { get; } = source;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public Stopwatch Clock { get; } = clock;
        public long? FirstTextMs { get; set; }
    }
}
