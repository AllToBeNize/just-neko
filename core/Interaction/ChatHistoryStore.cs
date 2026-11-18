using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustNeko.Core.Interaction;

/// <param name="Speaker">"user" 或 "neko"。</param>
/// <param name="Outcome">Neko 的回复怎么结束的；用户消息为 null。</param>
public sealed record ChatEntry(string Speaker, string Text, InteractionOutcome? Outcome, string? Error, DateTimeOffset At)
{
    public const string User = "user";
    public const string Neko = "neko";
}

/// <summary>
/// 聊天记录：存在本机的 chat-history.json，只保留最近若干条。关掉「保存聊天记录」时删除文件，
/// 之后只在内存里记本次运行的记录。
/// </summary>
public sealed class ChatHistoryStore
{
    public const int EntryLimit = 200;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly List<ChatEntry> _entries = [];

    public ChatHistoryStore(string directory, InteractionOrchestrator orchestrator, bool persist)
    {
        _path = Path.Combine(directory, "chat-history.json");
        Persist = persist;
        if (persist)
            Load();
        orchestrator.Changed += Record;
    }

    public bool Persist { get; private set; }

    public IReadOnlyList<ChatEntry> Entries => _entries;

    /// <summary>最近几轮完整的问答，用来恢复模型的上下文。</summary>
    public IEnumerable<(string User, string Assistant)> CompletedTurns(int limit)
    {
        var turns = new List<(string, string)>();
        for (var index = 1; index < _entries.Count; index++)
        {
            var reply = _entries[index];
            var question = _entries[index - 1];
            if (reply.Speaker == ChatEntry.Neko && reply.Outcome == InteractionOutcome.Completed && question.Speaker == ChatEntry.User)
                turns.Add((question.Text, reply.Text));
        }
        return turns.TakeLast(limit);
    }

    public void SetPersist(bool persist)
    {
        Persist = persist;
        if (persist)
            Save();
        else
            DeleteFile();
    }

    public void Clear()
    {
        _entries.Clear();
        if (Persist)
            Save();
    }

    private void Record(InteractionEvent change)
    {
        switch (change)
        {
            case InteractionStarted { Request.Source: InteractionSource.User } started:
                Add(new ChatEntry(ChatEntry.User, started.Request.Text, null, null, DateTimeOffset.Now));
                break;
            case InteractionEnded ended:
                Add(new ChatEntry(ChatEntry.Neko, ended.Text, ended.Outcome, ended.Error, DateTimeOffset.Now));
                break;
        }
    }

    private void Add(ChatEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count > EntryLimit)
            _entries.RemoveRange(0, _entries.Count - EntryLimit);
        if (Persist)
            Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _entries.AddRange(JsonSerializer.Deserialize<List<ChatEntry>>(File.ReadAllText(_path, Encoding.UTF8), Json) ?? []);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            // 记录读坏了就从空白开始；原文件留着，下一次保存时才覆盖。
            System.Diagnostics.Debug.WriteLine($"读取聊天记录失败：{error.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, Json), new UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"保存聊天记录失败：{error.Message}");
        }
    }

    private void DeleteFile()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
