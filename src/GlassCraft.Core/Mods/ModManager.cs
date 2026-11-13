using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GlassCraft.Core.Mods;

public sealed class ModEntry
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public string ModId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string? Description { get; init; }
    public string? Authors { get; init; }
    public string Loader { get; init; } = "";
    public bool Enabled { get; init; }
    public long Size { get; init; }

    public string SizeText => Size switch
    {
        > 1024 * 1024 => $"{Size / 1024.0 / 1024:0.0} MB",
        > 1024 => $"{Size / 1024.0:0} KB",
        _ => $"{Size} B"
    };
}

/// <summary>
/// Scans and manages a mods directory. Disabling renames the file to *.disabled (reversible),
/// deleting moves it to the trash folder instead of removing it.
/// </summary>
public sealed class ModManager
{
    private readonly AppPaths _paths;

    public ModManager(AppPaths paths) => _paths = paths;

    public IReadOnlyList<ModEntry> Scan(string modsDir)
    {
        var result = new List<ModEntry>();
        if (!Directory.Exists(modsDir)) return result;
        foreach (var file in Directory.EnumerateFiles(modsDir))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(".zip.disabled", StringComparison.OrdinalIgnoreCase))
                continue;
            var entry = Read(file);
            if (entry is not null) result.Add(entry);
        }
        return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ModEntry? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var enabled = !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            string? id = null, name = null, version = null, description = null, authors = null, loader = null;

            using var archive = ZipFile.OpenRead(path);
            var fabric = archive.GetEntry("fabric.mod.json");
            var quilt = archive.GetEntry("quilt.mod.json");
            var neoforge = archive.GetEntry("META-INF/neoforge.mods.toml");
            var forge = archive.GetEntry("META-INF/mods.toml");

            if (quilt is not null)
            {
                var json = ReadJson(quilt);
                id = json?["quilt_loader"]?["id"]?.GetValue<string>();
                version = json?["quilt_loader"]?["version"]?.GetValue<string>();
                name = json?["quilt_loader"]?["metadata"]?["name"]?.GetValue<string>() ?? id;
                description = json?["quilt_loader"]?["metadata"]?["description"]?.GetValue<string>();
                loader = "Quilt";
            }
            else if (fabric is not null)
            {
                var json = ReadJson(fabric);
                id = json?["id"]?.GetValue<string>();
                name = json?["name"]?.GetValue<string>() ?? id;
                version = json?["version"]?.GetValue<string>();
                description = json?["description"]?.GetValue<string>();
                loader = "Fabric";
                if (json?["authors"] is JsonArray arr)
                    authors = string.Join(", ", arr.Select(a => a is JsonObject o ? o["name"]?.GetValue<string>() : a?.GetValue<string>()));
            }
            else if (neoforge is not null || forge is not null)
            {
                using var reader = new StreamReader((neoforge ?? forge)!.Open());
                var toml = reader.ReadToEnd();
                id = MatchToml(toml, "modId");
                version = MatchToml(toml, "version");
                name = MatchToml(toml, "displayName") ?? id;
                description = MatchToml(toml, "description");
                authors = MatchToml(toml, "authors");
                loader = neoforge is not null ? "NeoForge" : "Forge";
            }
            else
            {
                name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                loader = "未知";
            }

            return new ModEntry
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                ModId = id ?? "",
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name!,
                Version = version ?? "",
                Description = description,
                Authors = authors,
                Loader = loader ?? "",
                Enabled = enabled,
                Size = info.Length
            };
        }
        catch
        {
            return new ModEntry
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Name = Path.GetFileNameWithoutExtension(path),
                Enabled = !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
                Size = new FileInfo(path).Length
            };
        }
    }

    private static JsonNode? ReadJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd());
    }

    private static string? MatchToml(string toml, string key)
    {
        var m = Regex.Match(toml, $@"^\s*{Regex.Escape(key)}\s*=\s*""([^""]*)""", RegexOptions.Multiline);
        if (m.Success) return m.Groups[1].Value;
        var m2 = Regex.Match(toml, $@"^\s*{Regex.Escape(key)}\s*=\s*'([^']*)'", RegexOptions.Multiline);
        return m2.Success ? m2.Groups[1].Value : null;
    }

    public bool SetEnabled(ModEntry mod, bool enable)
    {
        try
        {
            if (enable && mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                var target = mod.FilePath[..^".disabled".Length];
                File.Move(mod.FilePath, target, overwrite: false);
                return true;
            }
            if (!enable && !mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                File.Move(mod.FilePath, mod.FilePath + ".disabled", overwrite: false);
                return true;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Moves a mod file into data/trash/mods instead of deleting it.</summary>
    public string? MoveToTrash(ModEntry mod)
    {
        try
        {
            var trash = Path.Combine(_paths.DataDir, "trash", "mods");
            Directory.CreateDirectory(trash);
            var dest = Path.Combine(trash, $"{DateTime.Now:yyyyMMdd-HHmmss}-{mod.FileName}");
            File.Move(mod.FilePath, dest);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Copies a downloaded file into the mods directory, refusing to overwrite an existing file.</summary>
    public string InstallFile(string modsDir, string sourceFile)
    {
        Directory.CreateDirectory(modsDir);
        var name = Path.GetFileName(sourceFile);
        var dest = Path.Combine(modsDir, name);
        if (File.Exists(dest))
            throw new IOException($"Mod 已存在：{name}（未覆盖）");
        File.Copy(sourceFile, dest);
        return dest;
    }
}
