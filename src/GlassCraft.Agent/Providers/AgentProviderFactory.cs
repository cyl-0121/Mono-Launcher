using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using GlassCraft.Core.Security;

namespace GlassCraft.Agent.Providers;

public sealed class AgentProviderFactory
{
    private readonly HttpClient _http;
    private readonly SecretStore _secrets;

    public AgentProviderFactory(HttpClient http, SecretStore secrets)
    {
        _http = http;
        _secrets = secrets;
    }

    public string SecretKey(string providerId) => "provider." + providerId + ".apikey";

    public string? LoadApiKey(string providerId) => _secrets.Get(SecretKey(providerId));

    /// <summary>Builds a SK chat service for the provider. API keys are resolved from DPAPI storage.</summary>
    public IChatCompletionService Create(AgentProviderConfig config)
    {
        var cfg = config.Clone();
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            cfg.ApiKey = LoadApiKey(cfg.ProviderId);
        var def = ProviderCatalog.Find(cfg.ProviderId);
        if (def?.Protocol == ProviderProtocol.Anthropic)
            return new AnthropicChatService(_http, cfg);
        return new OpenAiCompatibleChatService(_http, cfg);
    }

    public Kernel BuildKernel(IEnumerable<KernelPlugin> plugins)
    {
        var builder = Kernel.CreateBuilder();
        foreach (var plugin in plugins)
            builder.Plugins.Add(plugin);
        return builder.Build();
    }
}
