using System.Text.Json;
using GlassCraft.Core.Launch;
using GlassCraft.Core.Models;

namespace GlassCraft.Core.Instances;

public sealed class InstanceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新实例";
    public string MinecraftVersion { get; set; } = "";
    /// <summary>null / "vanilla" / "fabric" / "quilt" / "forge" / "neoforge".</summary>
    public string? Loader { get; set; }
    public string? LoaderVersion { get; set; }
    public InstanceLaunchSettings Launch { get; set; } = new();
    public string? JavaPathOverride { get; set; }
    /// <summary>When false the instance shares the global .minecraft directory.</summary>
    public bool Isolated { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset LastPlayedAt { get; set; }
    public string? IconPath { get; set; }
    public string? Notes { get; set; }
    public string? ImportedFrom { get; set; }
    public string? Group { get; set; }

    public string VersionId => string.IsNullOrWhiteSpace(Loader)
        ? MinecraftVersion
        : (LoaderVersion is null ? MinecraftVersion : $"{MinecraftVersion}-{Loader}-{LoaderVersion}");

    public string LoaderDisplay => Loader switch
    {
        null or "" or "vanilla" => "原版",
        "neoforge" => "NeoForge",
        "forge" => "Forge",
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        _ => Loader
    };
}

public sealed class InstanceStore
{
    private readonly AppPaths _paths;
    private readonly object _lock = new();

    public InstanceStore(AppPaths paths) => _paths = paths;

    public IReadOnlyList<InstanceRecord> List()
    {
        var result = new List<InstanceRecord>();
        if (!Directory.Exists(_paths.InstancesDir)) return result;
        foreach (var dir in Directory.EnumerateDirectories(_paths.InstancesDir))
        {
            var file = Path.Combine(dir, "instance.json");
            if (!File.Exists(file)) continue;
            try
            {
                var rec = JsonSerializer.Deserialize<InstanceRecord>(File.ReadAllText(file), JsonDefaults.Options);
                if (rec is not null) result.Add(rec);
            }
            catch { /* skip broken instance, keep the folder intact */ }
        }
        return result.OrderByDescending(r => r.LastPlayedAt).ThenBy(r => r.Name).ToList();
    }

    public string InstanceDirectory(string id) => Path.Combine(_paths.InstancesDir, id);

    /// <summary>Game directory for an instance. Isolated instances never touch the user's shared .minecraft.</summary>
    public string GameDirectory(InstanceRecord record) =>
        record.Isolated
            ? Path.Combine(InstanceDirectory(record.Id), "minecraft")
            : Path.Combine(_paths.Root, "data", "minecraft");

    public InstanceRecord Create(string name, string minecraftVersion, string? loader = null, string? loaderVersion = null)
    {
        var record = new InstanceRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? $"{minecraftVersion} 实例" : name.Trim(),
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            CreatedAt = DateTimeOffset.Now
        };
        Save(record);
        Directory.CreateDirectory(GameDirectory(record));
        Directory.CreateDirectory(Path.Combine(GameDirectory(record), "mods"));
        return record;
    }

    public void Save(InstanceRecord record)
    {
        lock (_lock)
        {
            var dir = InstanceDirectory(record.Id);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "instance.json");
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(record, JsonDefaults.Options));
            File.Move(tmp, file, overwrite: true);
        }
    }

    public InstanceRecord? Get(string id)
    {
        var file = Path.Combine(InstanceDirectory(id), "instance.json");
        if (!File.Exists(file)) return null;
        try { return JsonSerializer.Deserialize<InstanceRecord>(File.ReadAllText(file), JsonDefaults.Options); }
        catch { return null; }
    }

    /// <summary>
    /// Moves an instance folder into data/trash instead of deleting it, so a mistaken delete can be
    /// undone manually. Never touches shared .minecraft contents.
    /// </summary>
    public string? MoveToTrash(string id)
    {
        var dir = InstanceDirectory(id);
        if (!Directory.Exists(dir)) return null;
        var trashRoot = Path.Combine(_paths.DataDir, "trash");
        Directory.CreateDirectory(trashRoot);
        var dest = Path.Combine(trashRoot, $"{id}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.Move(dir, dest);
        return dest;
    }

    public void MarkPlayed(string id)
    {
        var rec = Get(id);
        if (rec is null) return;
        rec.LastPlayedAt = DateTimeOffset.Now;
        Save(rec);
    }
}
