using System.Text;
using JustNeko.Core.Ai;

namespace JustNeko.Core.Interaction;

/// <summary>
/// 一次交互的完整流程：准备输入 → 选模型 → 边收边拆出正文和指令 → 写回近期对话。
/// 同一时间只跑一次交互；每次有自己的 ID 和取消信号，结束（包括被停止）之后的迟到事件一律丢弃。
/// </summary>
/// <remarks>
/// 事件在发起 <see cref="Submit"/> 的线程上触发（Godot 里就是主线程），这里不用 ConfigureAwait(false)，
/// 让 await 之后回到原来的同步上下文，界面可以直接处理事件。
/// </remarks>
public sealed class InteractionOrchestrator(
    ModelRouter models, Func<string> rolePrompt, Func<IReadOnlyCollection<string>> emotionNames)
{
    private readonly ConversationHistory _history = new();
    private string? _historyRoute;
    private Run? _current;
    private long _nextId;

    public event Action<InteractionEvent>? Changed;

    /// <summary>回复里会被当作指令拆出来的标签名。表现层、工具层可以登记自己的名称，例如 motion、action。</summary>
    public ISet<string> Directives { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "emotion" };

    public bool IsBusy => _current is not null;

    public long? CurrentId => _current?.Id;

    /// <summary>发起一次交互。正在进行的交互优先级更高时返回 false；否则先停掉它再开始。</summary>
    public bool Submit(InteractionRequest request)
    {
        if (_current is { } running)
        {
            if (running.Request.Priority > request.Priority)
                return false;
            Cancel();
        }

        var run = new Run(++_nextId, request);
        _current = run;
        _ = RunAsync(run);
        return true;
    }

    public void Cancel()
    {
        if (_current is not { } run)
            return;
        run.Cancellation.Cancel();
        End(run, InteractionOutcome.Cancelled, null);
    }

    public void ClearConversation()
    {
        Cancel();
        _history.Clear();
    }

    private async Task RunAsync(Run run)
    {
        Emit(run, new InteractionStarted(run.Id, run.Request));
        var kind = models.Peek(ModelTask.Chat).Kind;
        try
        {
            var route = await models.ResolveAsync(ModelTask.Chat, run.Cancellation.Token);
            kind = route.Endpoint.Kind;
            if (run.Ended)
                return;
            if (route.Key != _historyRoute)
            {
                _history.Clear();
                _historyRoute = route.Key;
            }

            var messages = PromptBuilder.Build(rolePrompt(), emotionNames(), _history.Messages, run.Request.Text);
            var parser = new DirectiveParser(Directives);
            await foreach (var update in route.Client.GetStreamingResponseAsync(messages, null, run.Cancellation.Token))
            {
                if (run.Ended)
                    return;
                Apply(run, parser.Read(update.Text));
            }
            if (run.Ended)
                return;
            Apply(run, parser.Complete());

            var reply = run.Reply.ToString();
            if (string.IsNullOrWhiteSpace(reply))
                throw new InvalidDataException("模型没有返回正文。请检查该接口是否支持流式 Chat Completions。");
            _history.AddTurn(run.Request.Text, reply);
            End(run, InteractionOutcome.Completed, null);
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
            // Cancel() 已经发出了结束事件。
        }
        catch (OperationCanceledException)
        {
            End(run, InteractionOutcome.TimedOut, "连接模型超时。");
        }
        catch (Exception error)
        {
            End(run, InteractionOutcome.Failed, ModelErrors.Describe(error, kind));
        }
        finally
        {
            run.Cancellation.Dispose();
        }
    }

    private void Apply(Run run, IReadOnlyList<StreamToken> tokens)
    {
        foreach (var token in tokens)
        {
            if (run.Ended)
                return;
            switch (token)
            {
                case DirectiveToken directive:
                    Emit(run, new DirectiveReceived(run.Id, directive.Name, directive.Value));
                    break;
                case TextToken { Text: var text }:
                    if (run.Reply.Length == 0)
                        text = text.TrimStart();
                    if (text.Length == 0)
                        break;
                    run.Reply.Append(text);
                    Emit(run, new ReplyUpdated(run.Id, run.Reply.ToString()));
                    break;
            }
        }
    }

    private void Emit(Run run, InteractionEvent change)
    {
        if (!run.Ended)
            Changed?.Invoke(change);
    }

    private void End(Run run, InteractionOutcome outcome, string? error)
    {
        if (run.Ended)
            return;
        run.Ended = true;
        if (_current == run)
            _current = null;
        Changed?.Invoke(new InteractionEnded(run.Id, outcome, run.Reply.ToString(), error));
    }

    private sealed class Run(long id, InteractionRequest request)
    {
        public long Id { get; } = id;
        public InteractionRequest Request { get; } = request;
        public CancellationTokenSource Cancellation { get; } = new();
        public StringBuilder Reply { get; } = new();
        public bool Ended { get; set; }
    }
}
