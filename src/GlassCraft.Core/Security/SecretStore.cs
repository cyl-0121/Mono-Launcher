using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GlassCraft.Core.Security;

/// <summary>
/// DPAPI-protected key/value store for API keys and tokens.
/// Values never touch disk in cleartext and are never written to logs.
/// </summary>
public sealed class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GlassCraft.Secrets.v1");
    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public SecretStore(AppPaths paths)
    {
        _path = Path.Combine(paths.ConfigDir, "secrets.bin");
        Load();
    }

    public string StoragePath => _path;

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path)) return;
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
                _values = JsonSerializer.Deserialize<Dictionary<string, string>>(bytes) ?? new(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _values = new(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(_values);
            var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            var tmp = _path + ".tmp";
            File.WriteAllBytes(tmp, enc);
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public string? Get(string key)
    {
        lock (_lock) return _values.TryGetValue(key, out var v) ? v : null;
    }

    public void Set(string key, string? value)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(value)) _values.Remove(key);
            else _values[key] = value;
        }
        Save();
    }

    public void Remove(string key)
    {
        lock (_lock) _values.Remove(key);
        Save();
    }

    public bool Has(string key)
    {
        lock (_lock) return _values.ContainsKey(key);
    }

    public IReadOnlyCollection<string> Keys
    {
        get { lock (_lock) return _values.Keys.ToList(); }
    }
}

/// <summary>Masks secrets that might accidentally appear in log lines or crash dumps.</summary>
public static class SecretRedactor
{
    private static readonly System.Text.RegularExpressions.Regex[] Patterns =
    {
        new(@"(?i)(sk-[A-Za-z0-9_\-]{12,})", System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"(?i)(Bearer\s+)[A-Za-z0-9\-._~+/]{12,}=*", System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"(?i)(""?(?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|authorization)""?\s*[:=]\s*""?)([^""\s,}]{6,})", System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"(?i)(x-api-key\s*[:=]\s*)(\S{6,})", System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"token:[^:\s]+:[0-9a-fA-F]{32}", System.Text.RegularExpressions.RegexOptions.Compiled)
    };

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = text;
        result = Patterns[0].Replace(result, "sk-***");
        result = Patterns[1].Replace(result, "$1***");
        result = Patterns[2].Replace(result, "$1***");
        result = Patterns[3].Replace(result, "$1***");
        result = Patterns[4].Replace(result, "token:***");
        return result;
    }

    /// <summary>Registers known secret values so they are scrubbed even without a recognisable prefix.</summary>
    public static string RedactKnown(string text, IEnumerable<string?> secrets)
    {
        var result = Redact(text);
        foreach (var s in secrets)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length < 6) continue;
            result = result.Replace(s, "***", StringComparison.Ordinal);
        }
        return result;
    }
}
