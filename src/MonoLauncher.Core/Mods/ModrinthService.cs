using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonoLauncher.Core.Mods;

public sealed class ModrinthProject
{
    public string Id { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? IconUrl { get; init; }
    public long Downloads { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Versions { get; init; } = Array.Empty<string>();
    public string? Author { get; init; }
}

public sealed class ModrinthFile
{
    public required string Url { get; init; }
    public required string Filename { get; init; }
    public bool Primary { get; init; }
    public long Size { get; init; }
    public string? Sha1 { get; init; }
    public string? Sha512 { get; init; }
}

public sealed class ModrinthVersion
{
    public string Id { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string Name { get; init; } = "";
    public string VersionNumber { get; init; } = "";
    public string VersionType { get; init; } = "release";
    public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ModrinthFile> Files { get; init; } = Array.Empty<ModrinthFile>();
    public IReadOnlyList<ModrinthDependency> Dependencies { get; init; } = Array.Empty<ModrinthDependency>();

    public ModrinthFile? PrimaryFile => Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

public sealed record ModrinthDependency(string? ProjectId, string? VersionId, string DependencyType);

/// <summary>Full project payload (includes the long README-style body).</summary>
public sealed class ModrinthProjectDetail
{
    public string Id { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Body { get; init; } = "";
    public string? License { get; init; }
    public long Downloads { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public string? IconUrl { get; init; }
}

/// <summary>
/// Modrinth API v2 client. Read-only public endpoints, no API key required.
/// Rate limiting: the API asks for an identifying User-Agent and <=300 requests/minute.
/// </summary>
public sealed class ModrinthService
{
    private const string BaseUrl = "https://api.modrinth.com/v2";
    private readonly HttpClient _http;

    public ModrinthService(HttpClient http)
    {
        _http = http;
        // Modrinth requires a descriptive UA; the shared client already sets one.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MonoLauncher/0.9 (launcher; contact: local-user)");
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchAsync(
        string query,
        string? gameVersion = null,
        string? loader = null,
        int limit = 20,
        string projectType = "mod",
        CancellationToken ct = default)
    {
        var facets = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add(JsonSerializer.Serialize(new[] { "versions:" + gameVersion }));
        if (!string.IsNullOrWhiteSpace(loader) && loader is not "vanilla")
            facets.Add(JsonSerializer.Serialize(new[] { "categories:" + loader }));
        if (!string.IsNullOrWhiteSpace(projectType))
            facets.Add(JsonSerializer.Serialize(new[] { "project_type:" + projectType }));

        var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query)}&limit={Math.Clamp(limit, 1, 50)}";
        if (facets.Count > 0)
            url += "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]");

        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        var result = new List<ModrinthProject>();
        if (json?["hits"] is not JsonArray hits) return result;
        foreach (var hit in hits)
        {
            result.Add(new ModrinthProject
            {
                Id = hit?["project_id"]?.GetValue<string>() ?? "",
                Slug = hit?["slug"]?.GetValue<string>() ?? "",
                Title = hit?["title"]?.GetValue<string>() ?? "",
                Description = hit?["description"]?.GetValue<string>() ?? "",
                IconUrl = hit?["icon_url"]?.GetValue<string>(),
                Downloads = hit?["downloads"]?.GetValue<long>() ?? 0,
                Categories = hit?["categories"]?.AsArray().Select(c => c?.GetValue<string>() ?? "").ToList() ?? new(),
                Versions = hit?["versions"]?.AsArray().Select(v => v?.GetValue<string>() ?? "").ToList() ?? new(),
                Author = hit?["author"]?.GetValue<string>()
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectIdOrSlug,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectIdOrSlug)}/version";
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion })));
        if (!string.IsNullOrWhiteSpace(loader) && loader is not "vanilla")
            query.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader })));
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        var result = new List<ModrinthVersion>();
        if (json is not JsonArray arr) return result;
        foreach (var item in arr)
        {
            result.Add(new ModrinthVersion
            {
                Id = item?["id"]?.GetValue<string>() ?? "",
                ProjectId = item?["project_id"]?.GetValue<string>() ?? "",
                Name = item?["name"]?.GetValue<string>() ?? "",
                VersionNumber = item?["version_number"]?.GetValue<string>() ?? "",
                VersionType = item?["version_type"]?.GetValue<string>() ?? "release",
                GameVersions = item?["game_versions"]?.AsArray().Select(v => v?.GetValue<string>() ?? "").ToList() ?? new(),
                Loaders = item?["loaders"]?.AsArray().Select(v => v?.GetValue<string>() ?? "").ToList() ?? new(),
                Files = ParseFiles(item?["files"]),
                Dependencies = ParseDependencies(item?["dependencies"])
            });
        }
        return result;
    }

    private static List<ModrinthFile> ParseFiles(JsonNode? node)
    {
        var files = new List<ModrinthFile>();
        if (node is not JsonArray arr) return files;
        foreach (var f in arr)
        {
            var url = f?["url"]?.GetValue<string>();
            var name = f?["filename"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(name)) continue;
            files.Add(new ModrinthFile
            {
                Url = url!,
                Filename = name!,
                Primary = f?["primary"]?.GetValue<bool>() ?? false,
                Size = f?["size"]?.GetValue<long>() ?? 0,
                Sha1 = f?["hashes"]?["sha1"]?.GetValue<string>(),
                Sha512 = f?["hashes"]?["sha512"]?.GetValue<string>()
            });
        }
        return files;
    }

    private static List<ModrinthDependency> ParseDependencies(JsonNode? node)
    {
        var result = new List<ModrinthDependency>();
        if (node is not JsonArray arr) return result;
        foreach (var d in arr)
        {
            var projectId = d?["project_id"]?.GetValue<string>();
            var versionId = d?["version_id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(versionId)) continue;
            result.Add(new ModrinthDependency(
                string.IsNullOrWhiteSpace(projectId) ? null : projectId,
                string.IsNullOrWhiteSpace(versionId) ? null : versionId,
                d?["dependency_type"]?.GetValue<string>() ?? "required"));
        }
        return result;
    }

    /// <summary>Fetches the full project payload (title, summary and the long body).</summary>
    public async Task<ModrinthProjectDetail?> GetProjectAsync(string projectIdOrSlug, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectIdOrSlug)}";
        var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (json is null) return null;
        return new ModrinthProjectDetail
        {
            Id = json["id"]?.GetValue<string>() ?? "",
            Slug = json["slug"]?.GetValue<string>() ?? "",
            Title = json["title"]?.GetValue<string>() ?? "",
            Description = json["description"]?.GetValue<string>() ?? "",
            Body = json["body"]?.GetValue<string>() ?? "",
            License = json["license"]?["id"]?.GetValue<string>(),
            Downloads = json["downloads"]?.GetValue<long>() ?? 0,
            Categories = json["categories"]?.AsArray().Select(c => c?.GetValue<string>() ?? "").ToList() ?? new(),
            IconUrl = json["icon_url"]?.GetValue<string>()
        };
    }

    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonNode.Parse(text);
    }
}
