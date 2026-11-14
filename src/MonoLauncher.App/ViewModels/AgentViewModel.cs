using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoLauncher.Agent;
using MonoLauncher.Agent.Providers;
using MonoLauncher.Agent.Tools;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Security;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MonoLauncher.App.ViewModels;

public sealed partial class AgentMessage : ObservableObject
{
    public required string Role { get; init; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string? _meta;

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsTool => Role == "tool";
    public bool IsSystem => Role == "system";
}

public sealed record ProviderOption(ProviderDefinition Definition)
{
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public bool IsLocal => Definition.IsLocal;
    public override string ToString() => Name;
}

public sealed partial class AgentViewModel : ObservableObject
{
    private readonly AgentConfigStore _store;
    private CancellationTokenSource? _cts;
    private AgentSession? _session;

    public ObservableCollection<ProviderOption> Providers { get; } = new();
    public ObservableCollection<AgentMessage> Messages { get; } = new();
    public ObservableCollection<string> Models { get; } = new();

    [ObservableProperty] private ProviderOption? _selectedProvider;
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _showSettings = true;
    [ObservableProperty] private string _providerHint = "";

    public string ToolSummary =>
        "已注册工具：list_instances · list_installed_versions · get_instance_status · install_version · launch_instance · read_log · get_instance_crash_report · search_modrinth（安装/启动前会弹窗确认）";
    [ObservableProperty] private string? _errorText;

    public AgentViewModel()
    {
        _store = new AgentConfigStore(AppServices.Paths);
        foreach (var def in ProviderCatalog.All)
            Providers.Add(new ProviderOption(def));
        Providers.Add(new ProviderOption(new ProviderDefinition
        {
            Id = "mock", Name = "演示模式（无需 Key）", BaseUrl = "", DefaultModel = "mock-demo",
            Models = new[] { "mock-demo" }, RequiresApiKey = false
        }));

        var config = _store.Load();
        SelectedProvider = Providers.FirstOrDefault(p => p.Id == config.ProviderId) ?? Providers[0];
        Model = string.IsNullOrWhiteSpace(config.Model)
            ? SelectedProvider.Definition.DefaultModel ?? ""
            : config.Model;
        BaseUrl = config.BaseUrl ?? "";
        ApiKey = AppServices.Providers.LoadApiKey(SelectedProvider.Id) ?? "";
        RefreshModels();
        AddSystemMessage("选择提供商与模型，然后开始对话。密钥只保存在本机 DPAPI 加密存储中。");
    }

    partial void OnSelectedProviderChanged(ProviderOption? value)
    {
        if (value is null) return;
        var def = value.Definition;
        BaseUrl = def.BaseUrl ?? "";
        Model = def.DefaultModel ?? "";
        ApiKey = AppServices.Providers.LoadApiKey(value.Id) ?? "";
        ProviderHint = def.Notes ?? "";
        RefreshModels();
        StatusText = value.Id == "mock" ? "演示模式：本地生成流式文本" : $"提供商：{def.Name}";
    }

    private void RefreshModels()
    {
        Models.Clear();
        var def = SelectedProvider?.Definition;
        if (def is null) return;
        foreach (var m in def.Models) Models.Add(m);
        if (Models.Count > 0 && string.IsNullOrWhiteSpace(Model)) Model = Models[0];
    }

    [RelayCommand]
    private void SaveProvider()
    {
        if (SelectedProvider is null) return;
        _store.Save(new StoredAgentConfig
        {
            ProviderId = SelectedProvider.Id,
            Model = Model,
            BaseUrl = BaseUrl
        });
        if (!string.IsNullOrWhiteSpace(ApiKey))
            AppServices.Secrets.Set(AppServices.Providers.SecretKey(SelectedProvider.Id), ApiKey);
        StatusText = "已保存提供商配置";
    }

    [RelayCommand]
    private void ClearChat()
    {
        _session = null;
        Messages.Clear();
        AddSystemMessage("对话已清空。");
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "已取消";
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0 || IsStreaming) return;
        if (SelectedProvider is null) return;

        InputText = "";
        ErrorText = null;
        Messages.Add(new AgentMessage { Role = "user", Text = text });
        var assistant = new AgentMessage { Role = "assistant", Text = "", IsStreaming = true };
        Messages.Add(assistant);

        IsStreaming = true;
        StatusText = "正在请求模型…";
        _cts = new CancellationTokenSource();

        try
        {
            _session ??= CreateSession();
            var sb = new System.Text.StringBuilder();
            await foreach (var ev in _session.SendAsync(text, _cts.Token))
            {
                switch (ev)
                {
                    case AgentTextDelta delta:
                        sb.Append(delta.Text);
                        assistant.Text = sb.ToString();
                        break;
                    case AgentToolCall call:
                        Messages.Add(new AgentMessage
                        {
                            Role = "tool",
                            Text = $"调用工具 {call.Name}\n{call.ArgumentsJson}",
                            Meta = call.RequiresConfirmation ? "等待确认" : "自动"
                        });
                        break;
                    case AgentToolResult result:
                        Messages.Add(new AgentMessage
                        {
                            Role = "tool",
                            Text = (result.Success ? "✓ " : "✗ ") + result.Result,
                            Meta = result.Name
                        });
                        break;
                    case AgentError error:
                        ErrorText = error.Message;
                        assistant.Text = sb.ToString() + (sb.Length > 0 ? "\n\n" : "") + "⚠ " + error.Message;
                        break;
                    case AgentCompleted:
                        StatusText = "完成";
                        break;
                }
                if (sb.Length > 0) StatusText = $"正在生成… {sb.Length} 字符";
            }
        }
        catch (OperationCanceledException)
        {
            assistant.Text += "\n\n（已取消）";
            StatusText = "已取消";
        }
        catch (AgentHttpException ex)
        {
            ErrorText = ex.FriendlyMessage;
            assistant.Text = sbSafe(assistant.Text, "⚠ " + ex.FriendlyMessage + "\n\n" + ex.Body);
            StatusText = "请求失败";
            AppServices.Log.Error("agent", ex.Message);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            assistant.Text = sbSafe(assistant.Text, "⚠ " + ex.Message);
            StatusText = "发生错误";
            AppServices.Log.Error("agent", "Agent 请求失败", ex);
        }
        finally
        {
            assistant.IsStreaming = false;
            IsStreaming = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static string sbSafe(string existing, string append) =>
        string.IsNullOrWhiteSpace(existing) ? append : existing + "\n\n" + append;

    private bool CanSend() => !IsStreaming;

    private AgentSession CreateSession()
    {
        if (SelectedProvider is null) throw new InvalidOperationException("未选择提供商");
        // Persist key so a restart keeps it (DPAPI only).
        if (!string.IsNullOrWhiteSpace(ApiKey))
            AppServices.Secrets.Set(AppServices.Providers.SecretKey(SelectedProvider.Id), ApiKey);

        IChatCompletionService chat;
        if (SelectedProvider.Id == "mock")
        {
            chat = new MockChatService();
        }
        else
        {
            var cfg = new AgentProviderConfig
            {
                ProviderId = SelectedProvider.Id,
                BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl,
                ApiKey = ApiKey,
                Model = Model
            };
            chat = AppServices.Providers.Create(cfg);
        }

        var kernel = Kernel.CreateBuilder().Build();
        kernel.Plugins.Add(LauncherTools.CreatePlugin(AppServices.Tools));
        var approval = new UiToolApproval { Handler = request => Dialogs.ConfirmToolAsync(request) };
        return new AgentSession(chat, kernel, approval, new AgentSessionOptions())
        {
            ProviderName = SelectedProvider.Name,
            ModelName = Model
        };
    }

    private void AddSystemMessage(string text) => Messages.Add(new AgentMessage { Role = "system", Text = text });

    partial void OnIsStreamingChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Asks the user through the UI before a high-risk tool runs.</summary>
public sealed class UiToolApproval : IToolApproval
{
    public Func<ToolCallRequest, Task<bool>>? Handler { get; set; }

    public Task<bool> ApproveAsync(ToolCallRequest request, CancellationToken ct)
    {
        if (Handler is null) return Task.FromResult(false);
        return Handler(request);
    }
}

public sealed class StoredAgentConfig
{
    public string ProviderId { get; set; } = "mock";
    public string Model { get; set; } = "";
    public string? BaseUrl { get; set; }
}

public sealed class AgentConfigStore
{
    private readonly string _path;

    public AgentConfigStore(Core.AppPaths paths)
    {
        _path = Path.Combine(paths.ConfigDir, "agent.json");
    }

    public StoredAgentConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new StoredAgentConfig();
            return JsonSerializer.Deserialize<StoredAgentConfig>(File.ReadAllText(_path)) ?? new StoredAgentConfig();
        }
        catch { return new StoredAgentConfig(); }
    }

    public void Save(StoredAgentConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
