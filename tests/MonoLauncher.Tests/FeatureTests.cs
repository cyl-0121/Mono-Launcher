using System.Reflection;
using Microsoft.SemanticKernel;
using MonoLauncher.Agent.Tools;
using MonoLauncher.Core;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Launch;
using MonoLauncher.Core.Models;
using MonoLauncher.Core.Versions;
using Xunit;

namespace MonoLauncher.Tests;

public class MemoryAdvisorTests
{
    [Fact]
    public void RecommendGivesSaneRange()
    {
        var plan = MemoryAdvisor.Recommend();
        Assert.True(plan.MinMb >= 256, "min heap should be at least 256 MB");
        Assert.True(plan.MaxMb > plan.MinMb, "max heap must exceed min");
        Assert.InRange(plan.MaxMb, 1024, 32768);
        Assert.True(plan.TotalMb > 0, "machine memory must be detected");
    }

    [Fact]
    public void AggressivePlanIsNotSmaller()
    {
        var normal = MemoryAdvisor.Recommend();
        var aggressive = MemoryAdvisor.Recommend(aggressive: true);
        Assert.True(aggressive.MaxMb >= normal.MaxMb);
    }

    [Fact]
    public void ApplyToEnablesOptimizationAndSetsMemory()
    {
        var settings = new InstanceLaunchSettings();
        MemoryAdvisor.ApplyTo(settings, new MemoryPlan(1024, 4096, 16384, 8000, "test"));
        Assert.Equal(1024, settings.MinMemoryMb);
        Assert.Equal(4096, settings.MaxMemoryMb);
        Assert.True(settings.OptimizeJvmArguments);
    }

    [Fact]
    public void OptimizationFlagsAreG1Based()
    {
        Assert.Contains("-XX:+UseG1GC", MemoryAdvisor.JvmOptimizationFlags);
    }
}

public class JvmArgumentMergeTests
{
    private static VersionInfo MinimalVersion() => new()
    {
        Id = "1.21.1",
        Type = "release",
        MainClass = "net.minecraft.client.main.Main"
    };

    private static (AppPaths Paths, JavaRuntime Java) Fixture()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "ml-test-" + Guid.NewGuid().ToString("N")));
        paths.EnsureDirectories();
        var java = new JavaRuntime { ExecutablePath = Path.Combine(paths.Root, "bin", "java.exe"), MajorVersion = 21, Is64Bit = true };
        return (paths, java);
    }

    [Fact]
    public void OptimizeFlagPrependsG1ParametersOnce()
    {
        var (paths, java) = Fixture();
        var settings = new InstanceLaunchSettings
        {
            MinMemoryMb = 1024,
            MaxMemoryMb = 4096,
            OptimizeJvmArguments = true,
            // A user-supplied flag that also happens to be a managed flag must not duplicate.
            ExtraJvmArguments = new List<string> { "-XX:+UseG1GC", "-Dcustom=1" }
        };
        var plan = new LaunchService(paths).CreatePlan(
            MinimalVersion(), OfflineIdentity.Create("Tester"), java, paths.Root, settings);

        var args = plan.Plan.Arguments;
        Assert.Equal(1, args.Count(a => a == "-XX:+UseG1GC"));
        Assert.Contains("-Dcustom=1", args);
        Assert.Contains("-Xmx4096M", args);
        Assert.Contains("-Xms1024M", args);
        Assert.Contains("-XX:MaxGCPauseMillis=200", args);
    }

    [Fact]
    public void DisabledOptimizationLeavesCustomArgumentsUntouched()
    {
        var (paths, java) = Fixture();
        var settings = new InstanceLaunchSettings
        {
            MinMemoryMb = 512,
            MaxMemoryMb = 2048,
            OptimizeJvmArguments = false,
            ExtraJvmArguments = new List<string> { "-Dcustom=2" }
        };
        var plan = new LaunchService(paths).CreatePlan(
            MinimalVersion(), OfflineIdentity.Create("Tester"), java, paths.Root, settings);

        var args = plan.Plan.Arguments;
        Assert.DoesNotContain("-XX:+UseG1GC", args);
        Assert.Contains("-Dcustom=2", args);
        Assert.Contains("-Xmx2048M", args);
    }
}

public class FlexibleDateTests
{
    [Theory]
    [InlineData("2026-11-15T01:36:43+0000")]   // Fabric loader profile style
    [InlineData("2024-08-08T00:00:00+00:00")]  // Mojang style
    [InlineData("2024-08-08T00:00:00Z")]
    public void VersionInfoParsesLooseTimestamps(string raw)
    {
        var json = "{\"id\":\"1.21.1\",\"releaseTime\":\"" + raw + "\",\"time\":\"" + raw + "\"}";
        var info = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(json, JsonDefaults.Options);
        Assert.NotNull(info);
        Assert.True(info!.ReleaseTime.Year >= 2000, $"releaseTime not parsed from {raw}");
    }

    [Fact]
    public void RealFabricProfileFragmentLoads()
    {
        const string json = """
        {
          "id": "fabric-loader-0.20.1-1.21.1",
          "inheritsFrom": "1.21.1",
          "releaseTime": "2026-11-15T01:36:43+0000",
          "time": "2026-11-15T01:36:43+0000",
          "type": "release",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "arguments": { "game": [], "jvm": ["-DFabricMcEmu= net.minecraft.client.main.Main "] },
          "libraries": [{ "name": "org.ow2.asm:asm:9.10.1", "url": "https://maven.fabricmc.net/" }]
        }
        """;
        var info = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(json, JsonDefaults.Options);
        Assert.NotNull(info);
        Assert.Equal("1.21.1", info!.InheritsFrom);
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", info.MainClass);
        Assert.Single(info.Libraries);
    }
}

public class LoaderLibraryUrlTests
{
    private static VersionInstaller NewInstaller(out AppPaths paths)
    {
        paths = new AppPaths(Path.Combine(Path.GetTempPath(), "ml-test-" + Guid.NewGuid().ToString("N")));
        paths.EnsureDirectories();
        return new VersionInstaller(paths, null!);
    }

    [Fact]
    public async Task LoaderLibrariesUseTheirOwnMaven()
    {
        var installer = NewInstaller(out _);
        var version = new VersionInfo
        {
            Id = "fabric-loader-0.20.1-1.21.1",
            Libraries =
            {
                new Library { Name = "org.ow2.asm:asm:9.10.1", Url = "https://maven.fabricmc.net/" },
                new Library { Name = "net.fabricmc:intermediary:1.21.1", Url = "https://maven.fabricmc.net" }
            }
        };
        var files = await installer.CollectFilesAsync(version, null);
        var asm = files.Single(f => f.DisplayName.Contains("asm"));
        Assert.StartsWith("https://maven.fabricmc.net/", asm.Url);
        var intermediary = files.Single(f => f.DisplayName.Contains("intermediary"));
        // A url without a trailing slash must still produce a valid maven path.
        Assert.Equal("https://maven.fabricmc.net/net/fabricmc/intermediary/1.21.1/intermediary-1.21.1.jar", intermediary.Url);
    }

    [Fact]
    public async Task VanillaLibrariesFallBackToMojangHost()
    {
        var installer = NewInstaller(out _);
        var version = new VersionInfo
        {
            Id = "1.21.1",
            Libraries = { new Library { Name = "com.google.guava:guava:32.1.2-jre" } }
        };
        var files = await installer.CollectFilesAsync(version, null);
        var guava = files.Single(f => f.DisplayName.Contains("guava"));
        Assert.StartsWith("https://libraries.minecraft.net/", guava.Url);
    }
}

public class AgentToolRegistrationTests
{
    private static HashSet<string> RegisteredToolNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in typeof(LauncherTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = m.GetCustomAttribute<KernelFunctionAttribute>();
            if (attr?.Name is { Length: > 0 } n) names.Add(n);
        }
        return names;
    }

    [Fact]
    public void NewFeatureToolsAreExposedToTheModel()
    {
        var names = RegisteredToolNames();
        foreach (var expected in new[]
                 {
                     "deploy_environment", "optimize_memory", "set_memory", "install_mod_full",
                     "get_project_detail", "translate_text", "recommend_mods", "diagnose_crash"
                 })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void PluginFactoryDiscoversTheSameSet()
    {
        // The plugin is what Semantic Kernel actually hands to the model.
        var probe = (LauncherTools)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LauncherTools));
        var plugin = LauncherTools.CreatePlugin(probe);
        var names = plugin.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("deploy_environment", names);
        Assert.Contains("translate_text", names);
        Assert.Contains("list_instances", names);
    }
}
