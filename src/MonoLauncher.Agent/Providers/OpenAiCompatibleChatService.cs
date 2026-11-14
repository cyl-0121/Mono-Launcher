using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MonoLauncher.Agent.Providers;

/// <summary>
/// Chat completion connector for every OpenAI /chat/completions compatible endpoint.
/// Implements SK's IChatCompletionService so the rest of the app stays provider agnostic,
/// while keeping full control over streaming and tool calls (needed for the confirmation flow).
/// </summary>
public sealed class OpenAiCompatibleChatService : IChatCompletionService
{
    private readonly HttpClient _http;
    private readonly AgentProviderConfig _config;
    private readonly string _model;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public OpenAiCompatibleChatService(HttpClient http, AgentProviderConfig config)
    {
        _http = http;
        _config = config;
        _model = config.EffectiveModel;
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public ChatHistory CreateNewChatHistory() => new();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(chatHistory, stream: false, kernel);
        using var request = BuildRequest(payload);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new AgentHttpException((int)response.StatusCode, body);

        var node = JsonNode.Parse(body) ?? throw new InvalidDataException("empty response");
        var message = node["choices"]?[0]?["message"];
        if (message is null) throw new InvalidDataException("response has no choices");

        var content = message["content"]?.GetValue<string>() ?? "";
        var items = new ChatMessageContentItemCollection();
        if (!string.IsNullOrEmpty(content)) items.Add(new TextContent(content));

        if (message["tool_calls"] is JsonArray calls)
        {
            foreach (var call in calls)
            {
                var id = call?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                var name = call?["function"]?["name"]?.GetValue<string>() ?? "";
                var argsText = call?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                items.Add(new FunctionCallContent(name, "MonoLauncher", id, ToKernelArguments(ParseArguments(argsText))));
            }
        }

        return new[]
        {
            new ChatMessageContent(AuthorRole.Assistant, items, modelId: _model)
        };
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(chatHistory, stream: true, kernel);
        using var request = BuildRequest(payload);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new AgentHttpException((int)response.StatusCode, error);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Accumulators for tool call deltas, which are chunked across SSE events.
        var toolCalls = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            if (data.Length == 0) continue;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch { continue; }
            if (node is null) continue;

            var choice = node["choices"]?[0];
            if (choice is null) continue;
            var delta = choice["delta"];
            if (delta is null) continue;

            var text = delta["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(text))
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, text, modelId: _model);

            if (delta["tool_calls"] is JsonArray callDeltas)
            {
                foreach (var cd in callDeltas)
                {
                    var index = cd?["index"]?.GetValue<int>() ?? 0;
                    var id = cd?["id"]?.GetValue<string>();
                    var name = cd?["function"]?["name"]?.GetValue<string>();
                    var args = cd?["function"]?["arguments"]?.GetValue<string>();
                    if (!toolCalls.TryGetValue(index, out var acc))
                    {
                        acc = (id ?? Guid.NewGuid().ToString("N"), name ?? "", new StringBuilder());
                        toolCalls[index] = acc;
                    }
                    if (!string.IsNullOrEmpty(id)) acc.Id = id;
                    if (!string.IsNullOrEmpty(name)) acc.Name = name;
                    if (!string.IsNullOrEmpty(args)) acc.Args.Append(args);
                }
            }
        }

        if (toolCalls.Count > 0)
        {
            var calls = new List<FunctionCallContent>();
            foreach (var (_, call) in toolCalls.OrderBy(k => k.Key))
            {
                calls.Add(new FunctionCallContent(call.Name, "MonoLauncher", call.Id,
                    ToKernelArguments(ParseArguments(call.Args.ToString()))));
            }
            // SK 1.x has no public ctor for streaming content items; the list is handed over through
            // InnerContent and picked up by AgentSession. This is an internal contract of this connector.
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, null, innerContent: calls, modelId: _model);
        }
    }

    private JsonObject BuildPayload(ChatHistory history, bool stream, Kernel? kernel)
    {
        var messages = new JsonArray();
        foreach (var msg in history)
        {
            if (msg.Role == AuthorRole.Tool || msg.Items.OfType<FunctionResultContent>().Any())
            {
                foreach (var result in msg.Items.OfType<FunctionResultContent>())
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = result.CallId,
                        ["content"] = result.Result?.ToString() ?? ""
                    });
                }
                continue;
            }

            var calls = msg.Items.OfType<FunctionCallContent>().ToList();
            if (calls.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var c in calls)
                {
                    arr.Add(new JsonObject
                    {
                        ["id"] = c.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = c.FunctionName,
                            ["arguments"] = JsonSerializer.Serialize(ToDictionary(c.Arguments))
                        }
                    });
                }
                messages.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = msg.Content ?? "",
                    ["tool_calls"] = arr
                });
                continue;
            }

            messages.Add(new JsonObject
            {
                ["role"] = RoleName(msg.Role),
                ["content"] = msg.Content ?? ""
            });
        }

        var payload = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["stream"] = stream,
            ["temperature"] = _config.Temperature,
            ["max_tokens"] = _config.MaxTokens
        };
        if (stream)
            payload["stream_options"] = new JsonObject { ["include_usage"] = false };

        if (kernel is not null && kernel.Plugins.Count > 0)
        {
            var tools = BuildTools(kernel);
            if (tools.Count > 0) payload["tools"] = tools;
        }
        return payload;
    }

    private static JsonArray BuildTools(Kernel kernel)
    {
        var tools = new JsonArray();
        foreach (var plugin in kernel.Plugins)
        {
            foreach (var fn in plugin)
            {
                var props = new JsonObject();
                var required = new JsonArray();
                foreach (var p in fn.Metadata.Parameters)
                {
                    props[p.Name] = new JsonObject
                    {
                        ["type"] = MapType(p.ParameterType),
                        ["description"] = p.Description ?? ""
                    };
                    if (p.IsRequired) required.Add(p.Name);
                }
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = fn.Name,
                        ["description"] = fn.Description ?? "",
                        ["parameters"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = props,
                            ["required"] = required
                        }
                    }
                });
            }
        }
        return tools;
    }

    private static string MapType(Type? type)
    {
        type = Nullable.GetUnderlyingType(type ?? typeof(string)) ?? type ?? typeof(string);
        if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return "integer";
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))) return "array";
        return "string";
    }

    private HttpRequestMessage BuildRequest(JsonObject payload)
    {
        var baseUrl = _config.EffectiveBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        foreach (var (k, v) in _config.ExtraHeaders)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            request.Headers.TryAddWithoutValidation(k, v);
        }
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream, application/json");
        return request;
    }

    private static string RoleName(AuthorRole role)
    {
        if (role == AuthorRole.System) return "system";
        if (role == AuthorRole.Assistant) return "assistant";
        if (role == AuthorRole.Tool) return "tool";
        return "user";
    }

    internal static Dictionary<string, object?> ParseArguments(string? json)
    {
        var result = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj) return result;
            foreach (var (k, v) in obj)
                result[k] = v is null ? null : v.DeepClone();
            return result;
        }
        catch
        {
            return result;
        }
    }

    internal static KernelArguments ToKernelArguments(Dictionary<string, object?> dict)
    {
        var args = new KernelArguments();
        foreach (var (k, v) in dict) args[k] = v;
        return args;
    }

    internal static Dictionary<string, object?> ToDictionary(KernelArguments? args)
    {
        var result = new Dictionary<string, object?>();
        if (args is null) return result;
        foreach (var (k, v) in args) result[k] = v;
        return result;
    }
}

public sealed class AgentHttpException(int statusCode, string body) : Exception($"HTTP {statusCode}: {Trim(body)}")
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;

    public string FriendlyMessage => StatusCode switch
    {
        401 => "API Key 无效或已过期 (401)",
        403 => "无权访问该模型 (403)",
        404 => "端点或模型不存在 (404)，请检查 Base URL 与模型名",
        429 => "请求过于频繁或额度不足 (429)",
        500 or 502 or 503 => "服务端错误，请稍后重试",
        _ => $"请求失败 (HTTP {StatusCode})"
    };

    private static string Trim(string s) => s.Length > 600 ? s[..600] + "…" : s;
}
