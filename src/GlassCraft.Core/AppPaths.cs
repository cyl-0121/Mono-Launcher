using System.Diagnostics;

namespace GlassCraft.Core;

/// <summary>
/// Centralised resolution of every directory GlassCraft writes to.
/// Layout (root = %APPDATA%\GlassCraft by default, override with GLASSCRAFT_HOME):
///   root/
///     config/            settings.json, providers.json, ui.json
///     data/
///       versions/        &lt;id&gt;/&lt;id&gt;.json  (merged metadata, client jar)
///       libraries/       maven-style shared library tree
///       assets/          indexes/ + objects/ (shared, content addressed)
///       java/            managed java runtimes
///       instances/       &lt;instanceId&gt;/instance.json + minecraft/
///       downloads/       partial download spool (same volume => atomic rename)
///     logs/
///     cache/
/// </summary>
public sealed class AppPaths
{
    public const string EnvHome = "GLASSCRAFT_HOME";

    public static AppPaths Default { get; } = new(ResolveRoot());

    public string Root { get; }
    public string ConfigDir { get; }
    public string DataDir { get; }
    public string VersionsDir { get; }
    public string LibrariesDir { get; }
    public string AssetsDir { get; }
    public string AssetIndexesDir { get; }
    public string AssetObjectsDir { get; }
    public string JavaDir { get; }
    public string InstancesDir { get; }
    public string DownloadsDir { get; }
    public string LogsDir { get; }
    public string CacheDir { get; }
    public string SettingsFile { get; }

    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
        ConfigDir = Path.Combine(Root, "config");
        DataDir = Path.Combine(Root, "data");
        VersionsDir = Path.Combine(DataDir, "versions");
        LibrariesDir = Path.Combine(DataDir, "libraries");
        AssetsDir = Path.Combine(DataDir, "assets");
        AssetIndexesDir = Path.Combine(AssetsDir, "indexes");
        AssetObjectsDir = Path.Combine(AssetsDir, "objects");
        JavaDir = Path.Combine(DataDir, "java");
        InstancesDir = Path.Combine(DataDir, "instances");
        DownloadsDir = Path.Combine(DataDir, "downloads");
        LogsDir = Path.Combine(Root, "logs");
        CacheDir = Path.Combine(Root, "cache");
        SettingsFile = Path.Combine(ConfigDir, "settings.json");
    }

    public static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable(EnvHome);
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = AppContext.BaseDirectory;
        return Path.Combine(appData, "GlassCraft");
    }

    /// <summary>Creates every directory. Safe to call repeatedly and concurrently.</summary>
    public void EnsureDirectories()
    {
        foreach (var d in new[]
                 {
                     Root, ConfigDir, DataDir, VersionsDir, LibrariesDir, AssetsDir,
                     AssetIndexesDir, AssetObjectsDir, JavaDir, InstancesDir, DownloadsDir, LogsDir, CacheDir
                 })
        {
            Directory.CreateDirectory(d);
        }
    }

    public string VersionDir(string id) => Path.Combine(VersionsDir, id);
    public string VersionJson(string id) => Path.Combine(VersionsDir, id, id + ".json");
    public string VersionJar(string id) => Path.Combine(VersionsDir, id, id + ".jar");

    public string AssetIndexFile(string id) => Path.Combine(AssetIndexesDir, id + ".json");
    public string AssetObject(string hash) =>
        Path.Combine(AssetObjectsDir, hash[..2], hash);

    /// <summary>Converts a maven coordinate (group:artifact:version[:classifier][@ext]) to a path under libraries.</summary>
    public string LibraryPath(string mavenName)
    {
        return Path.Combine(LibrariesDir, MavenCoordinate.ToPath(mavenName));
    }

    public void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            /* best effort */
        }
    }
}

/// <summary>Parsing/formatting of Maven coordinates: group:artifact:version[:classifier][@extension].</summary>
public readonly record struct MavenCoordinate(string Group, string Artifact, string Version, string? Classifier, string Extension)
{
    public static MavenCoordinate Parse(string name)
    {
        var ext = "jar";
        var at = name.IndexOf('@');
        var core = name;
        if (at >= 0)
        {
            ext = name[(at + 1)..];
            core = name[..at];
        }

        var parts = core.Split(':');
        if (parts.Length < 3)
            throw new FormatException($"Invalid maven coordinate '{name}'");
        string? classifier = parts.Length >= 4 ? parts[3] : null;
        return new MavenCoordinate(parts[0], parts[1], parts[2], classifier, ext);
    }

    public static string ToPath(string name)
    {
        var c = Parse(name);
        var file = $"{c.Artifact}-{c.Version}{(c.Classifier is null ? "" : "-" + c.Classifier)}.{c.Extension}";
        return System.IO.Path.Combine(c.Group.Split('.').Append(c.Artifact).Append(c.Version).Append(file).ToArray());
    }

    public string Path => ToPath(ToString());

    public override string ToString() =>
        $"{Group}:{Artifact}:{Version}" +
        (Classifier is null ? "" : ":" + Classifier) +
        (Extension == "jar" ? "" : "@" + Extension);
}
