using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonoLauncher.Agent;
using MonoLauncher.Agent.Local;
using MonoLauncher.Agent.Providers;
using MonoLauncher.Agent.Tools;
using MonoLauncher.App.Services;
using MonoLauncher.Core.Hardware;
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

    // ---- built-in local AI ----
    public ObservableCollection<LocalModelSpec> LocalModels { get; } = new();
    [ObservableProperty] private LocalModelSpec? _selectedLocalModel;
    [ObservableProperty] private string _localCpu = "—";
    [ObservableProperty] private string _localRam = "—";
    [ObservableProperty] private string _localGpu = "—";
    [ObservableProperty] private string _localVerdict = "";
    [ObservableProperty] private bool _hardwareChecked;
    [ObservableProperty] private bool _canDeployLocal;
    [ObservableProperty] private bool _isDeployingLocal;
    [ObservableProperty] private double _localProgress;
    [ObservableProperty] private string _localDeployStatus = "";
    [ObservableProperty] private bool _localReady;
    [ObservableProperty] private string _localModelHint = "";
    /// <summary>true = 写代码（推荐 Coder 模型），false = 聊天 / 翻译。</summary>
    [ObservableProperty] private bool _codingMode;
    private HardwareReport? _hardware;

    partial void OnCodingModeChanged(bool value) => RefreshModelChoices();

    /// <summary>True when the "本地 AI（本机一键部署）" provider is selected.</summary>
    public bool IsLocalProvider => SelectedProvider?.Id == "local";

    public string ToolSummary =>
        "已注册工具：deploy_environment（一键部署环境）· optimize_memory / set_memory（内存优化）· install_mod_full（装 Mod 连前置）· get_project_detail / translate_text（简介翻译）· recommend_mods（推荐 Mod）· diagnose_crash（崩溃排查）· list_instances · search_modrinth · install_version · launch_instance · read_log（高风险操作弹窗确认）";
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
        if (SelectedProvider.Id == "local") RefreshHardware();
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
        OnPropertyChanged(nameof(IsLocalProvider));
        if (value.Id == "local")
        {
            RefreshHardware();
            StatusText = CanDeployLocal ? "本机已满足部署条件" : "本机性能过低";
        }
        else
        {
            StatusText = value.Id == "mock" ? "演示模式：本地生成流式文本" : $"提供商：{def.Name}";
        }
    }

    // ================= built-in local AI =================

    /// <summary>Re-runs the hardware probe (CPU / memory / GPU) and picks a suitable model.</summary>
    [RelayCommand]
    private void CheckHardware() => RefreshHardware();

    private void RefreshHardware()
    {
        var hw = HardwareProbe.Probe();
        _hardware = hw;
        LocalCpu = hw.CpuText;
        LocalRam = hw.RamText;
        LocalGpu = hw.VramMb >= 512
            ? $"{string.Join(" / ", hw.Gpus)}（显存 {hw.VramText}）"
            : hw.GpuText;
        LocalVerdict = hw.VerdictText;
        CanDeployLocal = hw.CanRunLocalAi;
        HardwareChecked = true;
        RefreshModelChoices();

        var s = AppServices.Settings.Current;
        LocalReady = !string.IsNullOrWhiteSpace(s.LocalModelPath) && File.Exists(s.LocalModelPath);
        LocalDeployStatus = LocalReady
            ? $"已部署：{LocalModelCatalog.ById(s.LocalModelId)?.DisplayName ?? Path.GetFileName(s.LocalModelPath)}"
            : CanDeployLocal ? "点「一键部署 AI」自动下载并配置" : "性能过低，无法部署本地 AI";
    }

    /// <summary>Repopulates the model picker for the current use (chat vs coding) and hardware.</summary>
    private void RefreshModelChoices()
    {
        if (_hardware is null) return;
        var use = CodingMode ? LocalModelUse.Coding : LocalModelUse.Chat;
        LocalModels.Clear();
        foreach (var m in LocalModelCatalog.Usable(_hardware, use)) LocalModels.Add(m);
        SelectedLocalModel = LocalModelCatalog.Recommend(_hardware, use) ?? LocalModels.FirstOrDefault();
        LocalModelHint = LocalModels.Count > 0
            ? $"可用 {LocalModels.Count} 个{(CodingMode ? "写代码" : "聊天")}模型（按本机显存/内存推荐）"
            : CodingMode
                ? $"显存/内存不足以跑写代码模型，至少需要 {8} GB（Coder-7B）"
                : "显存/内存不足以跑任何模型";
    }

    /// <summary>Downloads the chosen GGUF, writes the inference settings and loads it.</summary>
    [RelayCommand]
    private async Task DeployLocalAiAsync()
    {
        if (SelectedLocalModel is null)
        {
            ErrorText = "请先选择一个模型（若列表为空，说明本机内存不足）";
            return;
        }
        if (IsDeployingLocal) return;

        IsDeployingLocal = true;
        ErrorText = null;
        LocalProgress = 0;
        var spec = SelectedLocalModel;
        try
        {
            LocalDeployStatus = $"准备下载 {spec.Name}（{spec.SizeText}）…";
            var progress = new Progress<LocalDeployProgress>(p =>
            {
                LocalProgress = p.Fraction;
                LocalDeployStatus = p.Stage + " " + p.DoneText;
            });
            var path = await AppServices.LocalDeployer.EnsureModelAsync(spec, progress, CancellationToken.None).ConfigureAwait(true);

            var threads = Math.Max(1, Environment.ProcessorCount - 1);
            using (var scope = AppServices.Settings.BeginUpdate())
            {
                var s2 = scope.Settings;
                s2.LocalModelPath = path;
                s2.LocalModelId = spec.Id;
                s2.LocalContextSize = spec.ContextSize;
                // Auto: offload to Vulkan GPU when one is usable, otherwise llama.cpp keeps running on CPU.
                s2.LocalGpuLayers = LocalLlamaService.GpuLayersAuto;
                s2.LocalThreads = threads;
                s2.LocalBatchSize = 512;
                s2.DefaultProviderId = "local";
            }
            AppServices.LocalAi.Unload();

            LocalDeployStatus = "正在加载模型（优先使用显卡，首次加载需要几十秒）…";
            LocalProgress = 0.99;
            var ok = await AppServices.LocalAi.LoadAutoAsync(path, spec.ContextSize, threads, 512, null).ConfigureAwait(true);
            LocalReady = ok;
            LocalProgress = 1;
            if (ok)
            {
                // Remember the choice so the next launch starts on the local model.
                _store.Save(new StoredAgentConfig { ProviderId = "local", Model = spec.Id, BaseUrl = "" });
                LocalDeployStatus = "部署完成，可以直接对话了";
                StatusText = "本地 AI 就绪";
                AddSystemMessage($"本地模型已就绪：{spec.Name}（{spec.SizeText}）。推理在本机 CPU 完成，不联网、不产生费用。");
            }
            else
            {
                LocalDeployStatus = "模型加载失败：" + (AppServices.LocalAi.Status.Error ?? "未知错误");
                StatusText = "本地模型加载失败";
            }
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            LocalDeployStatus = "部署失败";
            StatusText = "部署失败";
            AppServices.Log.Error("agent", "本地 AI 部署失败", ex);
        }
        finally
        {
            IsDeployingLocal = false;
        }
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
            if (SelectedProvider.Id == "local")
            {
                await StreamLocalAsync(assistant, text, new System.Text.StringBuilder(), _cts.Token);
                return;
            }
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

    /// <summary>Streams tokens from the built-in local GGUF model. Local mode has no tool calling.</summary>
    private async Task StreamLocalAsync(AgentMessage assistant, string text, System.Text.StringBuilder sb, CancellationToken ct)
    {
        var s = AppServices.Settings.Current;
        if (!AppServices.LocalAi.IsReady)
        {
            if (string.IsNullOrWhiteSpace(s.LocalModelPath) || !File.Exists(s.LocalModelPath))
                throw new InvalidOperationException("本地 AI 尚未部署：请在左侧点「一键部署 AI」");
            StatusText = "正在加载本地模型…";
            var ok = await AppServices.LocalAi.LoadAutoAsync(s.LocalModelPath!, s.LocalContextSize,
                s.LocalThreads, s.LocalBatchSize, null, ct);
            if (!ok)
                throw new InvalidOperationException("本地模型加载失败：" + (AppServices.LocalAi.Status.Error ?? "未知错误"));
        }

        StatusText = "本地模型生成中…";
        var spec = LocalModelCatalog.ById(AppServices.Settings.Current.LocalModelId);
        var coding = spec?.IsCoding == true;
        var system = coding
            ? "你是编程助手。回答给出完整可运行代码，用 ```语言名 标注代码块；先给代码再简短说明；不确定的地方直接说不确定，不要编造 API。"
            : "你是 MonoLauncher 内置助手，回答使用简体中文，简洁具体；可以介绍启动器功能、解释游戏报错、翻译文本。";
        var prompt = LocalLlamaService.BuildChatPrompt(system, text);
        var maxTokens = spec?.MaxTokens ?? 512;
        var temperature = coding ? 0.25f : 0.7f;
        await foreach (var token in AppServices.LocalAi.InferAsync(prompt, maxTokens, temperature, ct))
        {
            sb.Append(token);
            assistant.Text = sb.ToString();
            StatusText = $"正在生成… {sb.Length} 字符";
        }
        StatusText = "完成";
    }

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
        // Give the translate/recommend tools a one-shot chat channel on the same provider.
        AppServices.Tools.QuickChat = async (prompt, ct) =>
        {
            var reply = await chat.GetChatMessageContentsAsync(
                new ChatHistory { new ChatMessageContent(AuthorRole.User, prompt) }, null, kernel, ct).ConfigureAwait(false);
            return string.Join("\n", reply.Select(r => r.Content ?? ""));
        };
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
