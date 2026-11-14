using MonoLauncher.Core;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Download;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Launch;
using MonoLauncher.Core.Models;
using MonoLauncher.Core.Security;
using MonoLauncher.Core.Versions;
using Xunit;

namespace MonoLauncher.Tests;

public class JavaTests
{
    [Theory]
    [InlineData("21.0.7", 21)]
    [InlineData("1.8.0_442", 8)]
    [InlineData("17-ea", 17)]
    [InlineData("11", 11)]
    [InlineData("1.7.0_80", 7)]
    public void ParseMajorVersion(string version, int expected)
    {
        Assert.Equal(expected, JavaScanner.ParseMajor(version));
    }

    [Fact]
    public void SelectBestPrefersClosestMatchingMajor()
    {
        var list = new[]
        {
            new JavaRuntime { ExecutablePath = "a", MajorVersion = 8, Is64Bit = true },
            new JavaRuntime { ExecutablePath = "b", MajorVersion = 17, Is64Bit = true },
            new JavaRuntime { ExecutablePath = "c", MajorVersion = 21, Is64Bit = true }
        };
        var best = JavaScanner.SelectBest(list, 17);
        Assert.NotNull(best);
        Assert.Equal(17, best!.MajorVersion);
    }
}

public class OfflineIdentityTests
{
    [Fact]
    public void UuidMatchesVanillaOfflineAlgorithm()
    {
        // Known vanilla offline UUID for "Notch" (MD5 of OfflinePlayer:Notch, version 3).
        var uuid = OfflineIdentity.UuidFor("Notch");
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", uuid);
    }

    [Fact]
    public void RejectsTooLongNames()
    {
        Assert.Throws<ArgumentException>(() => OfflineIdentity.Create("this-name-is-way-too-long"));
    }
}

public class LaunchArgumentTests
{
    [Fact]
    public void TokenizeHandlesQuotes()
    {
        var tokens = LaunchArguments.Tokenize("--a \"b c\" --d");
        Assert.Equal(new[] { "--a", "b c", "--d" }, tokens);
    }

    [Fact]
    public void RedactRemovesAccessToken()
    {
        var cmd = "java --accessToken abc123def456 --version 1.21.1";
        var redacted = LaunchArguments.RedactCommandLine(cmd);
        Assert.DoesNotContain("abc123def456", redacted);
        Assert.Contains("<redacted>", redacted);
    }

    [Fact]
    public void RedactRemovesLegacySessionToken()
    {
        var cmd = "--session token:0123456789abcdef0123456789abcdef:0123456789abcdef0123456789abcdef";
        var redacted = LaunchArguments.RedactCommandLine(cmd);
        Assert.DoesNotContain("token:0123456789", redacted);
    }

    [Fact]
    public void BuildsArgumentsForModernVersion()
    {
        var version = new VersionInfo
        {
            Id = "1.21.1",
            Type = "release",
            MainClass = "net.minecraft.client.main.Main",
            AssetIndex = new AssetIndexRef { Id = "17" },
            Arguments = new Arguments
            {
                Jvm = new List<System.Text.Json.JsonElement>
                {
                    System.Text.Json.JsonSerializer.SerializeToElement("-Djava.library.path=${natives_directory}"),
                    System.Text.Json.JsonSerializer.SerializeToElement("-cp"),
                    System.Text.Json.JsonSerializer.SerializeToElement("${classpath}")
                },
                Game = new List<System.Text.Json.JsonElement>
                {
                    System.Text.Json.JsonSerializer.SerializeToElement("--username"),
                    System.Text.Json.JsonSerializer.SerializeToElement("${auth_player_name}")
                }
            }
        };
        var plan = LaunchArguments.Build(new LaunchOptions
        {
            Version = version,
            Account = OfflineIdentity.Create("Steve"),
            GameDirectory = @"C:\g",
            AssetsDirectory = @"C:\a",
            LibrariesDirectory = @"C:\l",
            NativesDirectory = @"C:\n",
            Classpath = @"C:\l\a.jar",
            JavaPath = "java.exe",
            MaxMemoryMb = 2048
        });
        Assert.Contains("-Xmx2048M", plan.Arguments);
        Assert.Contains("Steve", plan.Arguments);
        Assert.Contains("net.minecraft.client.main.Main", plan.Arguments);
    }
}

public class VersionMergeTests
{
    [Fact]
    public void MergeKeepsParentLibrariesAndChildOverride()
    {
        var parent = new VersionInfo
        {
            Id = "1.21.1",
            MainClass = "parent.Main",
            Assets = "17",
            Libraries = new List<Library>
            {
                new() { Name = "com.example:parent:1.0" }
            },
            Arguments = new Arguments
            {
                Game = new List<System.Text.Json.JsonElement>
                {
                    System.Text.Json.JsonSerializer.SerializeToElement("--parent")
                }
            }
        };
        var child = new VersionInfo
        {
            Id = "neoforge-21.1.1",
            InheritsFrom = "1.21.1",
            MainClass = "child.Main",
            Libraries = new List<Library>
            {
                new() { Name = "com.example:child:2.0" }
            },
            Arguments = new Arguments
            {
                Game = new List<System.Text.Json.JsonElement>
                {
                    System.Text.Json.JsonSerializer.SerializeToElement("--child")
                }
            }
        };

        var merged = VersionListService.Merge(parent, child);
        Assert.Equal("child.Main", merged.MainClass);
        Assert.Equal(2, merged.Libraries.Count);
        Assert.Equal("17", merged.Assets);
        Assert.Equal(2, merged.Arguments!.Game.Count);
    }
}

public class MavenTests
{
    [Fact]
    public void PathFollowsMavenLayout()
    {
        var path = MavenCoordinate.ToPath("org.lwjgl:lwjgl:3.3.3:natives-windows");
        Assert.Equal(Path.Combine("org", "lwjgl", "lwjgl", "3.3.3", "lwjgl-3.3.3-natives-windows.jar"), path);
    }

    [Fact]
    public void ParsesExtension()
    {
        var c = MavenCoordinate.Parse("net.neoforged:neoform:1.21.1-20240808.144430@zip");
        Assert.Equal("zip", c.Extension);
        Assert.Equal("neoform", c.Artifact);
    }
}

public class SecretRedactorTests
{
    [Theory]
    [InlineData("sk-abcdefghijklmnopqrstuvwxyz", "sk-***")]
    [InlineData("authorization: Bearer abcdefghijklmnopqrstuvwxyz", "$1***")]
    public void RedactsCommonKeyShapes(string input, string expectedFragment)
    {
        var output = SecretRedactor.Redact(input);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", output);
        if (expectedFragment != "$1***")
            Assert.Contains(expectedFragment, output);
    }

    [Fact]
    public void RedactsKnownValues()
    {
        var output = SecretRedactor.RedactKnown("token is s3cr3t-value", new[] { "s3cr3t-value" });
        Assert.DoesNotContain("s3cr3t-value", output);
    }
}

public class DownloadVerificationTests
{
    [Fact]
    public async Task Sha1MismatchIsDetected()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, "hello");
            var ok = await DownloadEngine.VerifyFileAsync(tmp, new DownloadItem
            {
                Url = "https://example.com/x",
                Destination = tmp,
                Sha1 = "0000000000000000000000000000000000000000"
            });
            Assert.False(ok);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task SizeMismatchIsDetected()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tmp, "hello");
            var ok = await DownloadEngine.VerifyFileAsync(tmp, new DownloadItem
            {
                Url = "https://example.com/x",
                Destination = tmp,
                Size = 999
            });
            Assert.False(ok);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}

public class RuleTests
{
    [Fact]
    public void OsRuleAllowsWindows()
    {
        var rule = new Rule { Action = "allow", Os = new OsRule { Name = "windows" } };
        Assert.True(rule.Matches(new RuleContext()));
    }

    [Fact]
    public void OsRuleRejectsOtherOs()
    {
        var rule = new Rule { Action = "allow", Os = new OsRule { Name = "osx" } };
        Assert.False(rule.Matches(new RuleContext()));
    }

    [Fact]
    public void LibraryWithNoRulesIsAllowed()
    {
        Assert.True(VersionInstaller.IsAllowed(new Library { Name = "a:b:1" }, new RuleContext()));
    }

    [Fact]
    public void LibraryWithDisallowRuleIsRejected()
    {
        var lib = new Library
        {
            Name = "a:b:1",
            Rules = new List<Rule> { new() { Action = "allow", Os = new OsRule { Name = "osx" } } }
        };
        Assert.False(VersionInstaller.IsAllowed(lib, new RuleContext()));
    }
}
