using System.Text.Json;

namespace GlassCraft.Core.Logging;

public enum LogLevel { Debug, Info, Warn, Error }

public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Category, string Message, string? Exception = null)
{
    public string ToJsonLine() => JsonSerializer.Serialize(new
    {
        ts = Time.ToString("O"),
        level = Level.ToString().ToLowerInvariant(),
        cat = Category,
        msg = Message,
        ex = Exception
    });
}

/// <summary>
/// Structured, thread-safe file logger. Every line is scrubbed of secrets before it is written.
/// Also keeps the most recent N entries in memory for the UI log page.
/// </summary>
public sealed class LogService
{
    private readonly string _dir;
    private readonly object _lock = new();
    private readonly Queue<LogEntry> _recent = new();
    private readonly string _sessionFile;
    private IntPtr _hint = IntPtr.Zero;

    public const int RecentCapacity = 2000;

    public LogService(AppPaths paths, string? sessionFile = null)
    {
        _dir = paths.LogsDir;
        Directory.CreateDirectory(_dir);
        _sessionFile = Path.Combine(_dir, sessionFile ?? $"app-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public string SessionFile => _sessionFile;
    public IReadOnlyList<LogEntry> Recent
    {
        get { lock (_lock) return _recent.ToArray(); }
    }

    public event Action<LogEntry>? EntryWritten;

    public void Write(LogLevel level, string category, string message, Exception? ex = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, category,
            Core.Security.SecretRedactor.Redact(message),
            ex is null ? null : Core.Security.SecretRedactor.Redact(ex.ToString()));
        lock (_lock)
        {
            _recent.Enqueue(entry);
            while (_recent.Count > RecentCapacity) _recent.Dequeue();
            try
            {
                File.AppendAllText(_sessionFile, entry.ToJsonLine() + Environment.NewLine);
            }
            catch { /* logging must never crash the app */ }
        }
        EntryWritten?.Invoke(entry);
    }

    public void Debug(string cat, string msg) => Write(LogLevel.Debug, cat, msg);
    public void Info(string cat, string msg) => Write(LogLevel.Info, cat, msg);
    public void Warn(string cat, string msg) => Write(LogLevel.Warn, cat, msg);
    public void Error(string cat, string msg, Exception? ex = null) => Write(LogLevel.Error, cat, msg, ex);
}
