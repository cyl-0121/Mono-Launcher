namespace MonoLauncher.Agent.Providers;

public enum ProviderProtocol
{
    /// <summary>OpenAI /chat/completions compatible (the majority of providers).</summary>
    OpenAiCompatible,
    /// <summary>Anthropic Messages API.</summary>
    Anthropic
}

public sealed record ProviderDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ProviderProtocol Protocol { get; init; } = ProviderProtocol.OpenAiCompatible;
    public string? BaseUrl { get; init; }
    public string? DefaultModel { get; init; }
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
    public bool RequiresApiKey { get; init; } = true;
    public bool IsLocal { get; init; }
    public bool IsCustom { get; init; }
    public string? DocsUrl { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyDictionary<string, string> ExtraHeaders { get; init; } = new Dictionary<string, string>();
}

public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderDefinition> All { get; } = new List<ProviderDefinition>
    {
        new()
        {
            Id = "openai", Name = "OpenAI", BaseUrl = "https://api.openai.com/v1",
            DefaultModel = "gpt-4o-mini",
            Models = new[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1", "gpt-4.1-mini", "o4-mini" },
            DocsUrl = "https://platform.openai.com/docs"
        },
        new()
        {
            Id = "azure-openai", Name = "Azure OpenAI", BaseUrl = null,
            DefaultModel = "gpt-4o-mini",
            Models = new[] { "gpt-4o-mini", "gpt-4o", "gpt-4.1" },
            Notes = "需要填写资源端点，例如 https://<resource>.openai.azure.com/openai/deployments/<deployment>",
            DocsUrl = "https://learn.microsoft.com/azure/ai-services/openai/"
        },
        new()
        {
            Id = "anthropic", Name = "Anthropic Claude", Protocol = ProviderProtocol.Anthropic,
            BaseUrl = "https://api.anthropic.com/v1",
            DefaultModel = "claude-sonnet-4-5",
            Models = new[] { "claude-sonnet-4-5", "claude-opus-4-1", "claude-haiku-4-5", "claude-3-5-haiku-latest" },
            DocsUrl = "https://docs.anthropic.com"
        },
        new()
        {
            Id = "gemini", Name = "Google Gemini", BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai",
            DefaultModel = "gemini-2.5-flash",
            Models = new[] { "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash" },
            DocsUrl = "https://ai.google.dev/gemini-api/docs/openai"
        },
        new()
        {
            Id = "deepseek", Name = "DeepSeek 深度求索", BaseUrl = "https://api.deepseek.com/v1",
            DefaultModel = "deepseek-chat",
            Models = new[] { "deepseek-chat", "deepseek-reasoner" },
            DocsUrl = "https://api-docs.deepseek.com"
        },
        new()
        {
            Id = "openrouter", Name = "OpenRouter", BaseUrl = "https://openrouter.ai/api/v1",
            DefaultModel = "openai/gpt-4o-mini",
            Models = new[] { "openai/gpt-4o-mini", "anthropic/claude-sonnet-4.5", "deepseek/deepseek-chat", "google/gemini-2.5-flash" },
            ExtraHeaders = new Dictionary<string, string> { ["X-Title"] = "MonoLauncher Launcher" },
            DocsUrl = "https://openrouter.ai/docs"
        },
        new()
        {
            Id = "zhipu", Name = "智谱 GLM", BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
            DefaultModel = "glm-4-flash",
            Models = new[] { "glm-4-flash", "glm-4-plus", "glm-4.5", "glm-4.6" },
            DocsUrl = "https://open.bigmodel.cn/dev/api"
        },
        new()
        {
            Id = "dashscope", Name = "通义千问 (DashScope)", BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            DefaultModel = "qwen-plus",
            Models = new[] { "qwen-plus", "qwen-max", "qwen-turbo", "qwen3-coder-plus" },
            DocsUrl = "https://help.aliyun.com/zh/model-studio/"
        },
        new()
        {
            Id = "moonshot", Name = "月之暗面 Kimi", BaseUrl = "https://api.moonshot.cn/v1",
            DefaultModel = "kimi-k2-0905-preview",
            Models = new[] { "moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k", "kimi-k2-0905-preview" },
            DocsUrl = "https://platform.moonshot.cn/docs"
        },
        new()
        {
            Id = "siliconflow", Name = "硅基流动 SiliconFlow", BaseUrl = "https://api.siliconflow.cn/v1",
            DefaultModel = "Qwen/Qwen2.5-7B-Instruct",
            Models = new[] { "Qwen/Qwen2.5-7B-Instruct", "deepseek-ai/DeepSeek-V3", "THUDM/glm-4-9b-chat" },
            DocsUrl = "https://docs.siliconflow.cn"
        },
        new()
        {
            Id = "local", Name = "本地 AI（本机一键部署）", BaseUrl = "",
            DefaultModel = "qwen2.5-1.5b", RequiresApiKey = false, IsLocal = true,
            Models = new[] { "qwen2.5-1.5b", "qwen2.5-3b", "qwen2.5-7b" },
            Notes = "无需联网、无需注册：先检测本机性能，再一键下载 GGUF 模型在本机推理（CPU）"
        },
        new()
        {
            Id = "ollama", Name = "Ollama (本地)", BaseUrl = "http://localhost:11434/v1",
            DefaultModel = "qwen2.5:7b", RequiresApiKey = false, IsLocal = true,
            Models = new[] { "qwen2.5:7b", "llama3.2", "deepseek-r1:7b", "gemma3:4b" },
            DocsUrl = "https://github.com/ollama/ollama/blob/main/docs/openai.md"
        },
        new()
        {
            Id = "lmstudio", Name = "LM Studio (本地)", BaseUrl = "http://localhost:1234/v1",
            DefaultModel = "local-model", RequiresApiKey = false, IsLocal = true,
            Models = new[] { "local-model" },
            DocsUrl = "https://lmstudio.ai/docs/api/openai-api"
        },
        new()
        {
            Id = "custom", Name = "自定义 OpenAI 兼容端点", BaseUrl = "",
            DefaultModel = "", RequiresApiKey = false, IsCustom = true,
            Models = Array.Empty<string>(),
            Notes = "自行填写 Base URL、模型名、API Key 与额外请求头"
        }
    };

    public static ProviderDefinition? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Recommended model list for a provider, extended by user-entered models.</summary>
    public static IReadOnlyList<string> ModelsFor(string providerId) =>
        Find(providerId)?.Models ?? Array.Empty<string>();
}

public sealed class AgentProviderConfig
{
    public string ProviderId { get; set; } = "deepseek";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "";
    public Dictionary<string, string> ExtraHeaders { get; set; } = new();
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 2048;
    public bool Stream { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;

    public string EffectiveBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl)
            ? ProviderCatalog.Find(ProviderId)?.BaseUrl ?? ""
            : BaseUrl!.TrimEnd('/');

    public string EffectiveModel =>
        string.IsNullOrWhiteSpace(Model) ? ProviderCatalog.Find(ProviderId)?.DefaultModel ?? "" : Model;

    public AgentProviderConfig Clone() => new()
    {
        ProviderId = ProviderId,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Model = Model,
        ExtraHeaders = new Dictionary<string, string>(ExtraHeaders),
        Temperature = Temperature,
        MaxTokens = MaxTokens,
        Stream = Stream,
        TimeoutSeconds = TimeoutSeconds
    };
}
