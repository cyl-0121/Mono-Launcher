using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
///
/// The unauthenticated REST API is limited to 60 requests/hour per IP, which is easy to exhaust
/// (and does not reset on app restart), so the Atom feed is tried first: it needs no token and
/// has no such quota. The REST API is kept as a fallback.
/// </summary>
public sealed class UpdateService
{
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly AppPaths _paths;

    public UpdateService(HttpClient http, AppPaths paths)
    {
        _http = http;
        _paths = paths;
    }

    public string? Repository { get; set; }
    public string CurrentVersion { get; set; } = "0.9.0";
    public string? LastError { get; private set; }

    private string CacheFile => Path.Combine(_paths.CacheDir, "update-cache.json");
    private string Repo => (Repository ?? "").Trim().Trim('/');

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Repo)) return LoadCache();
        LastError = null;

        var atom = await TryAtomAsync(ct).ConfigureAwait(false);
        if (atom is not null) return atom;

        return await TryApiAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads https://github.com/{repo}/releases.atom — no token, no quota.</summary>
    private async Task<UpdateInfo?> TryAtomAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://github.com/{Repo}/releases.atom");
            request.Headers.TryAddWithoutValidation("User-Agent", "MonoLauncher/0.9");
            request.Headers.TryAddWithoutValidation("Accept", "application/atom+xml");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"Atom feed HTTP {(int)response.StatusCode}";
                return null;
            }

            var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://www.w3.org/2005/Atom";
            var entry = doc.Root?.Element(ns + "entry");
            if (entry is null)
            {
                LastError = "Atom feed 中没有 release";
                return null;
            }

            var link = entry.Element(ns + "link")?.Attribute("href")?.Value ?? "";
            var tag = ExtractTag(link);
            var title = entry.Element(ns + "title")?.Value ?? tag;
            var updatedText = entry.Element(ns + "updated")?.Value;
            var html = entry.Element(ns + "content")?.Value ?? "";
            var body = ToPlainText(html);

            return await SaveAndBuildAsync(new UpdateInfo
            {
                Tag = tag,
                Name = string.IsNullOrWhiteSpace(title) ? tag : title,
                Body = body,
                HtmlUrl = link,
                PublishedAt = DateTimeOffset.TryParse(updatedText, out var dt) ? dt : DateTimeOffset.Now,
                IsNewer = IsNewerVersion(tag, CurrentVersion)
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = "Atom feed 失败：" + ex.Message;
            return null;
        }
    }

    private async Task<UpdateInfo?> TryApiAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repo}/releases/latest");
            request.Headers.TryAddWithoutValidation("User-Agent", "MonoLauncher/0.9");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if ((int)response.StatusCode == 403 && errBody.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                    LastError = "GitHub API 限额已用完（未登录 60 次/小时），稍后会自动恢复";
                else
                    LastError = $"HTTP {(int)response.StatusCode}";
                return LoadCache();
            }

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(text);
            if (node is null) return LoadCache();

            var tag = node["tag_name"]?.GetValue<string>() ?? "";
            return await SaveAndBuildAsync(new UpdateInfo
            {
                Tag = tag,
                Name = node["name"]?.GetValue<string>() ?? tag,
                Body = node["body"]?.GetValue<string>() ?? "",
                HtmlUrl = node["html_url"]?.GetValue<string>() ?? "",
                PublishedAt = DateTimeOffset.TryParse(node["published_at"]?.GetValue<string>(), out var dt)
                    ? dt
                    : DateTimeOffset.Now,
                IsNewer = IsNewerVersion(tag, CurrentVersion)
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return LoadCache();
        }
    }

    private async Task<UpdateInfo> SaveAndBuildAsync(UpdateInfo info, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.CacheDir);
        await File.WriteAllTextAsync(CacheFile, JsonSerializer.Serialize(info), ct).ConfigureAwait(false);
        return info;
    }

    private static string ExtractTag(string link)
    {
        var marker = "/releases/tag/";
        var index = link.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? "" : Uri.UnescapeDataString(link[(index + marker.Length)..].Trim('/'));
    }

    private static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = TagRegex.Replace(html, "\n");
        text = WebUtility.HtmlDecode(text);
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        text = BlankLines.Replace(text, "\n\n");
        text = string.Join('\n', text.Split('\n').Select(l => l.Trim()).Where((l, i) => l.Length > 0 || i > 0));
        text = text.Trim();
        return text.Length <= 4000 ? text : text[..4000] + "\n…";
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
