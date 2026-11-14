using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonoLauncher.Core.Update;

public sealed class UpdateInfo
{
    public string Tag { get; init; } = "";
    public string Name { get; init; } = "";
    public string Body { get; init; } = "";
    public string HtmlUrl { get; init; } = "";
    public DateTimeOffset PublishedAt { get; init; }
    public bool IsNewer { get; init; }
}

/// <summary>
/// Reads the latest GitHub release for the configured repository and compares it with the
/// running version. Results are cached so the home page can render instantly on next start.
/// </summary>
public sealed class UpdateService
{
    private readonly HttpClient _http;
    private readonly AppPaths _paths;

    public UpdateService(HttpClient http, AppPaths paths)
    {
        _http = http;
        _paths = paths;
    }

    public string? Repository { get; set; }
    public string CurrentVersion { get; set; } = "0.9.0";

    private string CacheFile => Path.Combine(_paths.CacheDir, "update-cache.json");

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Repository)) return LoadCache();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repository.Trim('/')}/releases/latest");
            request.Headers.TryAddWithoutValidation("User-Agent", "MonoLauncher/0.9");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return LoadCache();

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(text);
            if (node is null) return LoadCache();

            var tag = node["tag_name"]?.GetValue<string>() ?? "";
            var info = new UpdateInfo
            {
                Tag = tag,
                Name = node["name"]?.GetValue<string>() ?? tag,
                Body = node["body"]?.GetValue<string>() ?? "",
                HtmlUrl = node["html_url"]?.GetValue<string>() ?? "",
                PublishedAt = DateTimeOffset.TryParse(node["published_at"]?.GetValue<string>(), out var dt)
                    ? dt
                    : DateTimeOffset.Now,
                IsNewer = IsNewerVersion(tag, CurrentVersion)
            };

            Directory.CreateDirectory(_paths.CacheDir);
            await File.WriteAllTextAsync(CacheFile,
                JsonSerializer.Serialize(info), ct).ConfigureAwait(false);
            return info;
        }
        catch
        {
            return LoadCache();
        }
    }

    public UpdateInfo? LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            return JsonSerializer.Deserialize<UpdateInfo>(File.ReadAllText(CacheFile));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Compares "v1.2.3" / "1.2.3" style tags with the running version.</summary>
    public static bool IsNewerVersion(string tag, string current)
    {
        static int[] Parse(string s) =>
            s.TrimStart('v', 'V').Split('.', '-', '+')
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .Take(3).ToArray();

        var a = Parse(tag);
        var b = Parse(current);
        for (int i = 0; i < 3; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}
