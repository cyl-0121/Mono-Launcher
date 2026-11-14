using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MonoLauncher.Agent;

public abstract record AgentEvent;
public sealed record AgentTextDelta(string Text) : AgentEvent;
public sealed record AgentToolCall(string CallId, string Name, string ArgumentsJson, string Risk, bool RequiresConfirmation) : AgentEvent;
public sealed record AgentToolResult(string CallId, string Name, bool Success, string Result) : AgentEvent;
public sealed record AgentCompleted(string FullText, int ToolRounds) : AgentEvent;
public sealed record AgentError(string Message, string? Detail = null) : AgentEvent;

public enum ToolRisk { Low, Medium, High }

public sealed class ToolCallRequest
{
    public required string CallId { get; init; }
    public required string Name { get; init; }
    public required string ArgumentsJson { get; init; }
    public ToolRisk Risk { get; init; }
}

public interface IToolApproval
{
    /// <summary>Returns true when the user explicitly approved running the tool.</summary>
    Task<bool> ApproveAsync(ToolCallRequest request, CancellationToken ct);
}

/// <summary>Auto-approves safe tools only; high-risk tools are always denied without a real UI.</summary>
public sealed class DenyHighRiskApproval : IToolApproval
{
    public Task<bool> ApproveAsync(ToolCallRequest request, CancellationToken ct) =>
        Task.FromResult(request.Risk == ToolRisk.Low);
}

public sealed class AgentSessionOptions
{
    public string SystemPrompt { get; init; } = DefaultSystemPrompt;
    public int MaxToolRounds { get; init; } = 6;
    public bool EnableTools { get; init; } = true;

    public const string DefaultSystemPrompt = """
        你是 MonoLauncher 启动器内置的 AI 助手，帮助用户管理 Minecraft 实例、安装版本与 Mod、排查启动问题。
        你能调用的工具会返回真实的启动器状态，请优先使用工具获取事实，不要凭空猜测版本号或文件路径。
        高风险操作（安装、启动、删除）必须等待用户确认；用户拒绝时不要重试。
        回答使用简体中文，简洁、具体；涉及命令或路径时使用 Markdown 代码格式。
        """;
}

/// <summary>
/// One conversation: provider + system prompt + tool registry + history.
/// Streams text deltas while executing tool calls one round at a time, pausing for user approval.
/// </summary>
public sealed class AgentSession
{
    private readonly IChatCompletionService _chat;
    private readonly Kernel _kernel;
    private readonly IToolApproval _approval;
    private readonly AgentSessionOptions _options;
    private readonly ChatHistory _history = new();
    private readonly List<string> _toolNames = new();
    private int _toolRounds;

    public AgentSession(
        IChatCompletionService chat,
        Kernel kernel,
        IToolApproval approval,
        AgentSessionOptions? options = null)
    {
        _chat = chat;
        _kernel = kernel;
        _approval = approval;
        _options = options ?? new AgentSessionOptions();
        _history.AddSystemMessage(_options.SystemPrompt);
        foreach (var plugin in kernel.Plugins)
        foreach (var fn in plugin)
            _toolNames.Add(fn.Name);
    }

    public string ProviderName { get; init; } = "";
    public string ModelName { get; init; } = "";
    public IReadOnlyList<string> ToolNames => _toolNames;
    public IReadOnlyList<ChatMessageContent> History => _history.ToList();
    public int ToolRounds => _toolRounds;

    public void Clear()
    {
        _history.Clear();
        _history.AddSystemMessage(_options.SystemPrompt);
        _toolRounds = 0;
    }

    public async IAsyncEnumerable<AgentEvent> SendAsync(
        string userText,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        _history.AddUserMessage(userText);
        var finalText = new StringBuilder();
        _toolRounds = 0;

        for (int round = 0; round <= _options.MaxToolRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            var text = new StringBuilder();
            var calls = new List<FunctionCallContent>();
            var builder = new FunctionCallContentBuilder();
            Exception? failure = null;

            var stream = _chat.GetStreamingChatMessageContentsAsync(_history, null, _kernel, ct);
            await using (var enumerator = stream.GetAsyncEnumerator(ct))
            {
                while (true)
                {
                    string? delta = null;
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                        var chunk = enumerator.Current;
                        builder.Append(chunk);
                        delta = chunk.Content;
                        if (chunk.InnerContent is IEnumerable<FunctionCallContent> innerCalls)
                            calls.AddRange(innerCalls);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }

                    if (failure is not null) break;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        text.Append(delta);
                        finalText.Append(delta);
                        yield return new AgentTextDelta(delta);
                    }
                }
            }

            if (failure is not null)
            {
                yield return new AgentError(failure.Message, failure.ToString());
                yield break;
            }

            if (calls.Count == 0)
                calls.AddRange(builder.Build());

            if (calls.Count == 0)
            {
                _history.AddAssistantMessage(text.ToString());
                yield return new AgentCompleted(finalText.ToString(), _toolRounds);
                yield break;
            }

            var assistantItems = new ChatMessageContentItemCollection();
            if (text.Length > 0) assistantItems.Add(new TextContent(text.ToString()));
            foreach (var call in calls) assistantItems.Add(call);
            _history.Add(new ChatMessageContent(AuthorRole.Assistant, assistantItems));
            _toolRounds++;

            foreach (var call in calls)
            {
                ct.ThrowIfCancellationRequested();
                var json = JsonSerializer.Serialize(OpenAiCompat_ToDictionary(call.Arguments));
                var (risk, requiresConfirm) = DescribeRisk(call.FunctionName);
                yield return new AgentToolCall(call.Id, call.FunctionName, json, risk.ToString(), requiresConfirm);

                if (requiresConfirm && _options.EnableTools)
                {
                    var approved = await _approval.ApproveAsync(new ToolCallRequest
                    {
                        CallId = call.Id,
                        Name = call.FunctionName,
                        ArgumentsJson = json,
                        Risk = risk
                    }, ct).ConfigureAwait(false);

                    if (!approved)
                    {
                        _history.Add(new ChatMessageContent(AuthorRole.Tool,
                            new ChatMessageContentItemCollection
                            {
                                new FunctionResultContent(call.FunctionName, call.PluginName, call.Id,
                                    "用户拒绝了此操作。请告知用户操作已取消，不要重试。")
                            }));
                        yield return new AgentToolResult(call.Id, call.FunctionName, false, "用户拒绝执行");
                        continue;
                    }
                }

                string result;
                bool ok;
                try
                {
                    var function = FindFunction(call.FunctionName)
                                   ?? throw new InvalidOperationException($"工具 {call.FunctionName} 未注册");
                    var args = new KernelArguments();
                    foreach (var (k, v) in call.Arguments) args[k] = v;
                    var outcome = await _kernel.InvokeAsync(function, args, ct).ConfigureAwait(false);
                    result = outcome.GetValue<object>()?.ToString() ?? "";
                    ok = true;
                }
                catch (Exception ex)
                {
                    result = "工具执行失败：" + ex.Message;
                    ok = false;
                }

                _history.Add(new ChatMessageContent(AuthorRole.Tool,
                    new ChatMessageContentItemCollection
                    {
                        new FunctionResultContent(call.FunctionName, call.PluginName, call.Id, result)
                    }));
                yield return new AgentToolResult(call.Id, call.FunctionName, ok, result);
            }

            if (round == _options.MaxToolRounds)
            {
                yield return new AgentError("工具调用轮次过多，已停止。");
                yield break;
            }
        }
    }

    private KernelFunction? FindFunction(string name)
    {
        foreach (var plugin in _kernel.Plugins)
        {
            var fn = plugin.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (fn is not null) return fn;
        }
        return null;
    }

    private static Dictionary<string, object?> OpenAiCompat_ToDictionary(KernelArguments? args)
    {
        var result = new Dictionary<string, object?>();
        if (args is null) return result;
        foreach (var (k, v) in args) result[k] = v;
        return result;
    }

    private static (ToolRisk Risk, bool RequiresConfirmation) DescribeRisk(string name) => name switch
    {
        "launch_instance" or "install_version" or "delete_instance" or "install_mod" => (ToolRisk.High, true),
        "search_modrinth" or "list_instances" or "get_instance_status" or "read_log" => (ToolRisk.Low, false),
        _ => (ToolRisk.Medium, true)
    };
}
