using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;

using MonoLauncher.Core;

namespace MonoLauncher.Agent.Local;

public enum LocalModelState { Idle, Loading, Ready, Failed }

public sealed class LocalModelStatus
{
    public LocalModelState State { get; init; }
    public string? Message { get; init; }
    public string? ModelPath { get; init; }
    public int GpuLayers { get; init; }
    public int ContextSize { get; init; }
    public double Progress { get; init; }
    public string? Error { get; init; }
    public string? Backend { get; init; }
}

/// <summary>
/// Minimal GGUF local inference wrapper over LLamaSharp (llama.cpp).
/// Loading happens off the UI thread; the UI observes <see cref="Status"/>.
/// </summary>
public sealed class LocalLlamaService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private StatelessExecutor? _executor;
    private readonly object _stateLock = new();

    public LocalModelStatus Status { get; private set; } = new() { State = LocalModelState.Idle };

    public event Action<LocalModelStatus>? StatusChanged;

    public bool IsReady => Status.State == LocalModelState.Ready;

    private void SetStatus(LocalModelStatus status)
    {
        lock (_stateLock) Status = status;
        StatusChanged?.Invoke(status);
    }

    public async Task<bool> LoadAsync(
        string modelPath,
        int gpuLayers,
        int contextSize,
        int threads,
        int batchSize,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(modelPath))
        {
            SetStatus(new LocalModelStatus
            {
                State = LocalModelState.Failed,
                ModelPath = modelPath,
                Error = "模型文件不存在"
            });
            return false;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            UnloadInternal();
            SetStatus(new LocalModelStatus
            {
                State = LocalModelState.Loading,
                ModelPath = modelPath,
                GpuLayers = gpuLayers,
                ContextSize = contextSize,
                Message = "正在加载 GGUF 模型…",
                Progress = 0.05
            });

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = (uint)Math.Max(512, contextSize),
                GpuLayerCount = Math.Max(0, gpuLayers),
                BatchSize = (uint)Math.Max(32, batchSize),
                Threads = threads > 0 ? threads : Math.Max(1, Environment.ProcessorCount / 2)
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var weights = await Task.Run(() => LLamaWeights.LoadFromFile(parameters), ct).ConfigureAwait(false);
            var context = weights.CreateContext(parameters);
            var executor = new StatelessExecutor(weights, parameters);

            _weights = weights;
            _context = context;
            _executor = executor;

            SetStatus(new LocalModelStatus
            {
                State = LocalModelState.Ready,
                ModelPath = modelPath,
                GpuLayers = gpuLayers,
                ContextSize = contextSize,
                Message = $"模型已加载 ({sw.Elapsed.TotalSeconds:0.0}s)",
                Progress = 1.0,
                Backend = DescribeBackend()
            });
            progress?.Report(1.0);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(new LocalModelStatus
            {
                State = LocalModelState.Failed,
                ModelPath = modelPath,
                Error = ex.Message
            });
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string DescribeBackend()
    {
        try
        {
            return NativeLibraryConfig.Instance != null ? "llama.cpp (CPU/Auto)" : "llama.cpp";
        }
        catch
        {
            return "llama.cpp";
        }
    }

    /// <summary>
    /// Layer count meaning "offload everything". llama.cpp ignores it on CPU-only machines, so a
    /// single value works for both the GPU and CPU paths.
    /// </summary>
    public const int GpuLayersAuto = 99;

    /// <summary>
    /// Loads a model preferring the GPU (Vulkan) backend and silently falls back to pure CPU.
    /// A missing or outdated Vulkan driver is the usual reason a GPU load fails, so the retry
    /// with 0 layers keeps the feature usable on every machine.
    /// </summary>
    public async Task<bool> LoadAutoAsync(
        string modelPath, int contextSize, int threads, int batchSize = 512,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (await LoadAsync(modelPath, GpuLayersAuto, contextSize, threads, batchSize, progress, ct).ConfigureAwait(false))
            return true;

        SetStatus(new LocalModelStatus
        {
            State = LocalModelState.Loading,
            ModelPath = modelPath,
            GpuLayers = 0,
            ContextSize = contextSize,
            Message = "显卡加载失败，正在回退 CPU…",
            Progress = 0.5
        });
        return await LoadAsync(modelPath, 0, contextSize, threads, batchSize, progress, ct).ConfigureAwait(false);
    }

    public void Unload()
    {
        _gate.Wait();
        try { UnloadInternal(); }
        finally { _gate.Release(); }
        SetStatus(new LocalModelStatus { State = LocalModelState.Idle });
    }

    private void UnloadInternal()
    {
        _executor = null;
        _context?.Dispose();
        _context = null;
        _weights?.Dispose();
        _weights = null;
    }

    public async IAsyncEnumerable<string> InferAsync(
        string prompt,
        int maxTokens = 512,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_executor is null)
            throw new InvalidOperationException("本地模型尚未加载");

        var inference = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = new[] { "<|im_end|>", "</s>", "\nUser:" }
        };

        await foreach (var token in _executor.InferAsync(prompt, inference, ct).ConfigureAwait(false))
            yield return token;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, int maxTokens = 512, CancellationToken ct = default)
    {
        var prompt = BuildChatPrompt(systemPrompt, userPrompt);
        var sb = new StringBuilder();
        await foreach (var token in InferAsync(prompt, maxTokens, ct).ConfigureAwait(false))
            sb.Append(token);
        return sb.ToString().Trim();
    }

    public static string BuildChatPrompt(string system, string user) => $"""
        <|im_start|>system
        {system}<|im_end|>
        <|im_start|>user
        {user}<|im_end|>
        <|im_start|>assistant
        """;

    public static string? FindBundledModel(AppPaths paths)
    {
        var dir = Path.Combine(paths.DataDir, "models");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories).FirstOrDefault();
    }

    public void Dispose()
    {
        Unload();
        _gate.Dispose();
    }
}
