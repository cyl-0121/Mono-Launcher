using System.Text;
using System.Text.Json;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Models;

namespace MonoLauncher.Core.Launch;

public sealed class LaunchOptions
{
    public required VersionInfo Version { get; init; }
    public required MinecraftAccount Account { get; init; }
    public required string GameDirectory { get; init; }
    public required string AssetsDirectory { get; init; }
    public required string LibrariesDirectory { get; init; }
    public required string NativesDirectory { get; init; }
    public required string Classpath { get; init; }
    public required string JavaPath { get; init; }
    public int MinMemoryMb { get; init; } = 512;
    public int MaxMemoryMb { get; init; } = 4096;
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool Fullscreen { get; init; }
    public bool DemoMode { get; init; }
    public string? ServerAddress { get; init; }
    public int? ServerPort { get; init; }
    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExtraGameArguments { get; init; } = Array.Empty<string>();
    public string LauncherName { get; init; } = "MonoLauncher";
    public string LauncherVersion { get; init; } = "0.9.0";
    public string? Log4jConfigPath { get; init; }
    public string? VersionType { get; init; }
    public string? UserType { get; init; }
}

public sealed class LaunchPlan
{
    public required string JavaPath { get; init; }
    public required string WorkingDirectory { get; init; }
    public required List<string> Arguments { get; init; }
    public required string CommandLinePreview { get; init; }
    public required string MainClass { get; init; }
}

/// <summary>
/// Builds the JVM + game argument vector exactly like the official launcher:
/// rules are evaluated for the current OS, ${...} placeholders are substituted, and the
/// legacy (pre-1.13) minecraftArguments string format is supported.
/// </summary>
public static class LaunchArguments
{
    public static LaunchPlan Build(LaunchOptions o)
    {
        var version = o.Version;
        var classpath = o.Classpath;
        var natives = o.NativesDirectory;

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = o.Account.Name,
            ["version_name"] = version.Id,
            ["game_directory"] = o.GameDirectory,
            ["assets_root"] = o.AssetsDirectory,
            ["game_assets"] = o.AssetsDirectory,
            ["assets_index_name"] = version.AssetIndex?.Id ?? version.Assets ?? "legacy",
            ["auth_uuid"] = o.Account.UuidWithoutDashes,
            ["auth_access_token"] = o.Account.EffectiveAccessToken,
            ["auth_session"] = "token:" + o.Account.EffectiveAccessToken + ":" + o.Account.UuidWithoutDashes,
            ["user_type"] = o.UserType ?? (o.Account.Kind == AccountKind.Microsoft ? "msa" : "legacy"),
            ["version_type"] = o.VersionType ?? version.Type,
            ["natives_directory"] = natives,
            ["launcher_name"] = o.LauncherName,
            ["launcher_version"] = o.LauncherVersion,
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = o.LibrariesDirectory,
            ["resolution_width"] = o.Width.ToString(),
            ["resolution_height"] = o.Height.ToString(),
            ["user_properties"] = "{}",
            ["quickPlayPath"] = "",
            ["quickPlaySingleplayer"] = "",
            ["quickPlayMultiplayer"] = o.ServerAddress is null ? "" : $"{o.ServerAddress}:{o.ServerPort ?? 25565}",
            ["quickPlayRealms"] = ""
        };

        var ctx = new RuleContext
        {
            IsDemoUser = o.DemoMode,
            HasCustomResolution = true,
            // Modern versions expose --quickPlayMultiplayer through a feature-gated argument.
            HasQuickPlaysSupport = o.ServerAddress is not null,
            QuickPlayMode = o.ServerAddress is null ? QuickPlayMode.None : QuickPlayMode.Multiplayer
        };

        var jvm = new List<string>
        {
            $"-Xms{o.MinMemoryMb}M",
            $"-Xmx{o.MaxMemoryMb}M",
            "-Djava.library.path=" + natives,
            $"-Dminecraft.launcher.brand={o.LauncherName}",
            $"-Dminecraft.launcher.version={o.LauncherVersion}"
        };

        if (o.Log4jConfigPath is not null && File.Exists(o.Log4jConfigPath))
            jvm.Add("-Dlog4j.configurationFile=" + o.Log4jConfigPath);

        // Version-provided JVM args (new format).
        if (version.Arguments?.Jvm is { Count: > 0 } jvmArgs)
            jvm.AddRange(Expand(jvmArgs, ctx, variables));
        else if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
            jvm.Add("-Djava.library.path=" + natives); // legacy already covered

        if (!jvm.Any(a => a.StartsWith("-cp", StringComparison.Ordinal) || a.StartsWith("-classpath", StringComparison.Ordinal)))
        {
            jvm.Add("-cp");
            jvm.Add(classpath);
        }

        jvm.AddRange(o.ExtraJvmArguments);

        var game = new List<string>();
        if (version.Arguments?.Game is { Count: > 0 } gameArgs)
        {
            game.AddRange(Expand(gameArgs, ctx, variables));
        }
        else if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
        {
            game.AddRange(ExpandLegacy(version.MinecraftArguments!, variables));
        }

        // Resolution is only applied when the version does not already add it.
        if (!game.Contains("--width"))
        {
            game.Add("--width");
            game.Add(o.Width.ToString());
            game.Add("--height");
            game.Add(o.Height.ToString());
        }
        if (o.Fullscreen) game.Add("--fullscreen");

        // Direct connect. 1.20+ uses quickPlayMultiplayer via the feature rule above; older
        // versions still accept --server/--port, so add them only when the version has no quick-play.
        if (o.ServerAddress is not null)
        {
            var hasQuickPlay = game.Any(a => a.Contains("quickPlayMultiplayer", StringComparison.OrdinalIgnoreCase));
            if (!hasQuickPlay)
            {
                game.Add("--server");
                game.Add(o.ServerAddress);
                game.Add("--port");
                game.Add((o.ServerPort ?? 25565).ToString());
            }
        }

        game.AddRange(o.ExtraGameArguments);

        var args = new List<string>(jvm) { version.MainClass ?? "net.minecraft.client.main.Main" };
        args.AddRange(game);

        return new LaunchPlan
        {
            JavaPath = o.JavaPath,
            WorkingDirectory = o.GameDirectory,
            Arguments = args,
            MainClass = version.MainClass ?? "",
            CommandLinePreview = BuildPreview(o.JavaPath, args)
        };
    }

    private static IEnumerable<string> Expand(List<JsonElement> raw, RuleContext ctx, Dictionary<string, string> vars)
    {
        foreach (var el in raw)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    yield return Substitute(el.GetString() ?? "", vars);
                    break;
                case JsonValueKind.Object:
                {
                    var rules = el.TryGetProperty("rules", out var r)
                        ? JsonSerializer.Deserialize<List<Rule>>(r.GetRawText(), JsonDefaults.Options)
                        : null;
                    if (rules is { Count: > 0 } && !EvaluateRules(rules, ctx)) break;

                    if (el.TryGetProperty("value", out var value))
                    {
                        if (value.ValueKind == JsonValueKind.String)
                            yield return Substitute(value.GetString() ?? "", vars);
                        else if (value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var v in value.EnumerateArray())
                                if (v.ValueKind == JsonValueKind.String)
                                    yield return Substitute(v.GetString() ?? "", vars);
                        }
                    }
                    break;
                }
            }
        }
    }

    public static bool EvaluateRules(List<Rule> rules, RuleContext ctx)
    {
        bool allowed = false;
        foreach (var rule in rules)
        {
            if (rule.Matches(ctx))
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    /// <summary>Legacy minecraftArguments: whitespace separated, supports double quotes.</summary>
    public static IEnumerable<string> ExpandLegacy(string args, Dictionary<string, string> vars)
    {
        var tokens = Tokenize(args);
        foreach (var token in tokens)
            yield return Substitute(token, vars);
    }

    public static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static string Substitute(string input, Dictionary<string, string> vars)
    {
        if (!input.Contains("${")) return input;
        var sb = new StringBuilder(input.Length + 32);
        int i = 0;
        while (i < input.Length)
        {
            var start = input.IndexOf("${", i, StringComparison.Ordinal);
            if (start < 0) { sb.Append(input, i, input.Length - i); break; }
            sb.Append(input, i, start - i);
            var end = input.IndexOf('}', start + 2);
            if (end < 0) { sb.Append(input, start, input.Length - start); break; }
            var key = input[(start + 2)..end];
            sb.Append(vars.TryGetValue(key, out var v) ? v : input[start..(end + 1)]);
            i = end + 1;
        }
        return sb.ToString();
    }

    private static string BuildPreview(string java, List<string> args)
    {
        static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
        return Quote(java) + " " + string.Join(" ", args.Select(Quote));
    }

    /// <summary>Redacts access tokens and other secrets from a command line for logs/UI.</summary>
    public static string RedactCommandLine(string cmd)
    {
        var result = System.Text.RegularExpressions.Regex.Replace(cmd,
            @"(--accessToken\s+)([^\s""]+)", "$1<redacted>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Legacy session format: token:<accessToken>:<uuid>
        result = System.Text.RegularExpressions.Regex.Replace(result,
            @"token:[^:\s]+:[0-9a-fA-F]{32}", "token:<redacted>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        result = System.Text.RegularExpressions.Regex.Replace(result,
            @"(Bearer\s+)[A-Za-z0-9\-._~+/]+=*", "$1<redacted>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return result;
    }
}
