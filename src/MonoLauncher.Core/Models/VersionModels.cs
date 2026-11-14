using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoLauncher.Core.Models;

public sealed class VersionManifest
{
    [JsonPropertyName("latest")] public LatestVersions Latest { get; set; } = new();
    [JsonPropertyName("versions")] public List<VersionSummary> Versions { get; set; } = new();
}

public sealed class LatestVersions
{
    [JsonPropertyName("release")] public string? Release { get; set; }
    [JsonPropertyName("snapshot")] public string? Snapshot { get; set; }
}

public sealed class VersionSummary
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "release";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("time")] public DateTimeOffset Time { get; set; }
    [JsonPropertyName("releaseTime")] public DateTimeOffset ReleaseTime { get; set; }
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("complianceLevel")] public int ComplianceLevel { get; set; }
    [JsonPropertyName("javaVersion")] public JavaVersionInfo? JavaVersion { get; set; }

    [JsonIgnore]
    public bool IsRelease => string.Equals(Type, "release", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore]
    public bool IsSnapshot => string.Equals(Type, "snapshot", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore]
    public bool IsOld => string.Equals(Type, "old_beta", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Type, "old_alpha", StringComparison.OrdinalIgnoreCase);
}

public sealed class JavaVersionInfo
{
    [JsonPropertyName("component")] public string? Component { get; set; }
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; }
}

public sealed class VersionInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "release";
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; set; }
    [JsonPropertyName("mainClass")] public string? MainClass { get; set; }
    [JsonPropertyName("jar")] public string? Jar { get; set; }
    [JsonPropertyName("assets")] public string? Assets { get; set; }
    [JsonPropertyName("assetIndex")] public AssetIndexRef? AssetIndex { get; set; }
    [JsonPropertyName("downloads")] public VersionDownloads? Downloads { get; set; }
    [JsonPropertyName("javaVersion")] public JavaVersionInfo? JavaVersion { get; set; }
    [JsonPropertyName("libraries")] public List<Library> Libraries { get; set; } = new();
    [JsonPropertyName("arguments")] public Arguments? Arguments { get; set; }
    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; set; }
    [JsonPropertyName("logging")] public LoggingConfig? Logging { get; set; }
    [JsonPropertyName("complianceLevel")] public int ComplianceLevel { get; set; }
    [JsonPropertyName("releaseTime")] public DateTimeOffset ReleaseTime { get; set; }
    [JsonPropertyName("time")] public DateTimeOffset Time { get; set; }
    [JsonPropertyName("minimumLauncherVersion")] public int MinimumLauncherVersion { get; set; }

    /// <summary>Set by the loader installer / version resolver, not part of the vanilla JSON.</summary>
    [JsonIgnore] public string? LoaderKind { get; set; }
    [JsonIgnore] public string? LoaderVersion { get; set; }

    /// <summary>True when this JSON was produced by merging a child with its parent.</summary>
    [JsonIgnore] public bool IsMerged { get; set; }

    [JsonIgnore] public string DisplayType => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "旧 Beta",
        "old_alpha" => "旧 Alpha",
        _ => Type
    };
}

public sealed class VersionDownloads
{
    [JsonPropertyName("client")] public DownloadInfo? Client { get; set; }
    [JsonPropertyName("server")] public DownloadInfo? Server { get; set; }
    [JsonPropertyName("client_mappings")] public DownloadInfo? ClientMappings { get; set; }
    [JsonPropertyName("server_mappings")] public DownloadInfo? ServerMappings { get; set; }
}

public sealed class DownloadInfo
{
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed class AssetIndexRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("totalSize")] public long TotalSize { get; set; }
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed class Arguments
{
    [JsonPropertyName("game")] public List<JsonElement> Game { get; set; } = new();
    [JsonPropertyName("jvm")] public List<JsonElement> Jvm { get; set; } = new();
}

public sealed class Library
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; set; }
    [JsonPropertyName("extract")] public ExtractRules? Extract { get; set; }
    [JsonPropertyName("rules")] public List<Rule>? Rules { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }

    /// <summary>True when the library declares platform natives via the legacy "natives" map.</summary>
    [JsonIgnore] public bool HasNatives => Natives is { Count: > 0 };
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")] public DownloadInfo? Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

public sealed class ExtractRules
{
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }
}

public sealed class Rule
{
    [JsonPropertyName("action")] public string Action { get; set; } = "allow";
    [JsonPropertyName("os")] public OsRule? Os { get; set; }
    [JsonPropertyName("features")] public Dictionary<string, bool>? Features { get; set; }

    /// <summary>Evaluates this single rule. Unknown features are treated as false.</summary>
    public bool Matches(RuleContext ctx)
    {
        if (Os is not null && !Os.Matches())
            return false;
        if (Features is not null)
        {
            foreach (var (key, expected) in Features)
            {
                var actual = key switch
                {
                    "is_demo_user" => ctx.IsDemoUser,
                    "has_custom_resolution" => ctx.HasCustomResolution,
                    "has_quick_plays_support" => ctx.HasQuickPlaysSupport,
                    "is_quick_play_singleplayer" => ctx.QuickPlayMode == QuickPlayMode.Singleplayer,
                    "is_quick_play_multiplayer" => ctx.QuickPlayMode == QuickPlayMode.Multiplayer,
                    "is_quick_play_realms" => ctx.QuickPlayMode == QuickPlayMode.Realms,
                    _ => false
                };
                if (actual != expected)
                    return false;
            }
        }
        return true;
    }
}

public enum QuickPlayMode { None, Singleplayer, Multiplayer, Realms }

public sealed class RuleContext
{
    public bool IsDemoUser { get; init; }
    public bool HasCustomResolution { get; init; }
    public bool HasQuickPlaysSupport { get; init; }
    public QuickPlayMode QuickPlayMode { get; init; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("arch")] public string? Arch { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }

    public bool Matches()
    {
        if (Name is not null)
        {
            var current = OsNames.Current;
            if (!string.Equals(Name, current, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (Arch is not null)
        {
            var current = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            // Mojang uses "x86" for 32-bit windows and "x86_64" (or "arm64") for 64-bit.
            if (!string.Equals(Arch, current, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}

public static class OsNames
{
    public static string Current =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "osx" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";
}

public sealed class LoggingConfig
{
    [JsonPropertyName("client")] public LoggingClient? Client { get; set; }
}

public sealed class LoggingClient
{
    [JsonPropertyName("argument")] public string? Argument { get; set; }
    [JsonPropertyName("file")] public DownloadInfo? File { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class AssetIndexDocument
{
    [JsonPropertyName("objects")] public Dictionary<string, AssetObject> Objects { get; set; } = new();
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
