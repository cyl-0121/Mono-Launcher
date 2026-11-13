using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace GlassCraft.Agent.Providers;

/// <summary>Anthropic Messages API connector (native protocol, not an OpenAI shim).</summary>
public sealed class AnthropicChatService : IChatCompletionService
{
    private readonly HttpClient _http;
    private readonly AgentProviderConfig _config;
    private readonly string _model;

    public AnthropicChatService(HttpClient http, AgentProviderConfig config)
    {
        _http = http;
        _config = config;
        _model = config.EffectiveModel;
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public ChatHistory CreateNewChatHistory() => new();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(chatHistory, stream: false, kernel);
        using var request = BuildRequest(payload);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new AgentHttpException((int)response.StatusCode, body);

        var node = JsonNode.Parse(body) ?? throw new InvalidDataException("empty anthropic response");
        var items = new ChatMessageContentItemCollection();
        if (node["content"] is JsonArray arr)
        {
            foreach (var block in arr)
            {
                var type = block?["type"]?.GetValue<string>();
                if (type == "text")
                {
                    var text = block?["text"]?.GetValue<string>() ?? "";
                    if (text.Length > 0) items.Add(new TextContent(text));
                }
                else if (type == "tool_use")
                {
                    var id = block?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                    var name = block?["name"]?.GetValue<string>() ?? "";
                    var input = block?["input"];
                    var args = input is JsonObject o
                        ? o.ToDictionary(kv => kv.Key, kv => (object?)(kv.Value?.DeepClone()))
                        : new Dictionary<string, object?>();
                    items.Add(new FunctionCallContent(name, "GlassCraft", id,
                        OpenAiCompatibleChatService.ToKernelArguments(args)));
                }
            }
        }
        return new[] { new ChatMessageContent(AuthorRole.Assistant, items, modelId: _model) };
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

        // index -> (id, name, json accumulator)
        var toolUses = new Dictionary<int, (string Id, string Name, StringBuilder Json)>();
        string? currentEvent = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line[6..].Trim();
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;

            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch { continue; }
            if (node is null) continue;

            switch (currentEvent)
            {
                case "content_block_start":
                {
                    var index = node["index"]?.GetValue<int>() ?? 0;
                    var block = node["content_block"];
                    if (block?["type"]?.GetValue<string>() == "tool_use")
                    {
                        toolUses[index] = (
                            block["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                            block["name"]?.GetValue<string>() ?? "",
                            new StringBuilder());
                    }
                    break;
                }
                case "content_block_delta":
                {
                    var index = node["index"]?.GetValue<int>() ?? 0;
                    var delta = node["delta"];
                    var type = delta?["type"]?.GetValue<string>();
                    if (type == "text_delta")
                    {
                        var text = delta?["text"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(text))
                            yield return new StreamingChatMessageContent(AuthorRole.Assistant, text, modelId: _model);
                    }
                    else if (type == "input_json_delta" && toolUses.TryGetValue(index, out var acc))
                    {
                        acc.Json.Append(delta?["partial_json"]?.GetValue<string>() ?? "");
                    }
                    break;
                }
                case "message_stop":
                {
                    if (toolUses.Count > 0)
                    {
                        var calls = new List<FunctionCallContent>();
                        foreach (var (_, t) in toolUses.OrderBy(k => k.Key))
                        {
                            calls.Add(new FunctionCallContent(t.Name, "GlassCraft", t.Id,
                                OpenAiCompatibleChatService.ToKernelArguments(
                                    OpenAiCompatibleChatService.ParseArguments(t.Json.ToString()))));
                        }
                        // Same internal contract as the OpenAI connector: tool calls arrive via InnerContent.
                        yield return new StreamingChatMessageContent(AuthorRole.Assistant, null, innerContent: calls, modelId: _model);
                    }
                    yield break;
                }
            }
        }
    }

    private JsonObject BuildPayload(ChatHistory history, bool stream, Kernel? kernel)
    {
        var systemParts = new List<string>();
        var messages = new JsonArray();

        foreach (var msg in history)
        {
            if (msg.Role == AuthorRole.System)
            {
                systemParts.Add(msg.Content ?? "");
                continue;
            }

            // Tool results travel as user messages in the Anthropic protocol.
            var results = msg.Items.OfType<FunctionResultContent>().ToList();
            if (results.Count > 0)
            {
                var blocks = new JsonArray();
                foreach (var r in results)
                {
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = r.CallId,
                        ["content"] = r.Result?.ToString() ?? ""
                    });
                }
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = blocks });
                continue;
            }

            var calls = msg.Items.OfType<FunctionCallContent>().ToList();
            if (calls.Count > 0)
            {
                var blocks = new JsonArray();
                if (!string.IsNullOrEmpty(msg.Content))
                    blocks.Add(new JsonObject { ["type"] = "text", ["text"] = msg.Content });
                foreach (var c in calls)
                {
                    blocks.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = c.Id,
                        ["name"] = c.FunctionName,
                        ["input"] = JsonSerializer.SerializeToNode(OpenAiCompatibleChatService.ToDictionary(c.Arguments))
                    });
                }
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = blocks });
                continue;
            }

            messages.Add(new JsonObject
            {
                ["role"] = msg.Role == AuthorRole.Assistant ? "assistant" : "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = msg.Content ?? "" }
                }
            });
        }

        var payload = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = _config.MaxTokens,
            ["temperature"] = _config.Temperature,
            ["stream"] = stream,
            ["messages"] = messages
        };
        if (systemParts.Count > 0)
            payload["system"] = string.Join("\n\n", systemParts);

        if (kernel is not null && kernel.Plugins.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var plugin in kernel.Plugins)
            foreach (var fn in plugin)
            {
                var props = new JsonObject();
                var required = new JsonArray();
                foreach (var p in fn.Metadata.Parameters)
                {
                    props[p.Name] = new JsonObject
                    {
                        ["type"] = p.ParameterType == typeof(int) || p.ParameterType == typeof(long) ? "integer" : "string",
                        ["description"] = p.Description ?? ""
                    };
                    if (p.IsRequired) required.Add(p.Name);
                }
                tools.Add(new JsonObject
                {
                    ["name"] = fn.Name,
                    ["description"] = fn.Description ?? "",
                    ["input_schema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = props,
                        ["required"] = required
                    }
                });
            }
            if (tools.Count > 0) payload["tools"] = tools;
        }
        return payload;
    }

    private HttpRequestMessage BuildRequest(JsonObject payload)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_config.EffectiveBaseUrl)
            ? "https://api.anthropic.com/v1"
            : _config.EffectiveBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/messages");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
            request.Headers.TryAddWithoutValidation("x-api-key", _config.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Headers.TryAddWithoutValidation("anthropic-beta", "tools-2024-04-04");
        foreach (var (k, v) in _config.ExtraHeaders)
            if (!string.IsNullOrWhiteSpace(k))
                request.Headers.TryAddWithoutValidation(k, v);
        return request;
    }
}
