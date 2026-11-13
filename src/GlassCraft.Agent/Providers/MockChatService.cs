using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace GlassCraft.Agent.Providers;

/// <summary>
/// Offline demo connector. It is a real IChatCompletionService that streams locally generated text
/// with realistic pacing, so the UI / streaming pipeline can be exercised without an API key.
/// It is clearly labelled as a demo provider in the UI and is never used for real answers silently.
/// </summary>
public sealed class MockChatService : IChatCompletionService
{
    private readonly string _prefix;

    public MockChatService(string prefix = "演示模式") => _prefix = prefix;

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public ChatHistory CreateNewChatHistory() => new();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var text = BuildReply(chatHistory);
        return Task.FromResult<IReadOnlyList<ChatMessageContent>>(new[]
        {
            new ChatMessageContent(AuthorRole.Assistant, text)
        });
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reply = BuildReply(chatHistory);
        // Stream in small word-ish chunks to mimic token streaming.
        var index = 0;
        while (index < reply.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var len = Math.Min(Random.Shared.Next(1, 4), reply.Length - index);
            var chunk = reply.Substring(index, len);
            index += len;
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, chunk, modelId: "mock-demo");
            await Task.Delay(28, cancellationToken).ConfigureAwait(false);
        }
    }

    private string BuildReply(ChatHistory history)
    {
        var lastUser = history.LastOrDefault(m => m.Role == AuthorRole.User)?.Content ?? "";
        return $"""
            [{_prefix}] 我收到了你的消息：「{Truncate(lastUser, 60)}」

            这是 GlassCraft 的离线演示回复，用于验证流式 UI 与原生渲染管线：Avalonia 12 控件树、Skia 玻璃卡片、增量文本渲染都已经在真实运行，没有 WebView 参与。

            要接入真实模型，请在右侧选择 DeepSeek、OpenAI、Anthropic、Gemini、Ollama 或其他 OpenAI 兼容端点，填入 API Key 与模型名后发送。密钥保存在 Windows DPAPI 加密的保险箱中，不会写入日志。

            在后续阶段，我还会具备这些工具能力：
            - 列出与检查本机实例
            - 安装原版 / NeoForge / Fabric / Forge / Quilt 版本
            - 启动实例并读取崩溃日志定位原因
            - 搜索 Modrinth 并在确认后安装 Mod
            """;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
