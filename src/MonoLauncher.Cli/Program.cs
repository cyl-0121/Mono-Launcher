using System.Text;
using MonoLauncher.Agent.Tools;
using MonoLauncher.Core;
using MonoLauncher.Core.Auth;
using MonoLauncher.Core.Config;
using MonoLauncher.Core.Deploy;
using MonoLauncher.Core.Download;
using MonoLauncher.Core.Instances;
using MonoLauncher.Core.Java;
using MonoLauncher.Core.Launch;
using MonoLauncher.Core.Loaders;
using MonoLauncher.Core.Logging;
using MonoLauncher.Core.Models;
using MonoLauncher.Core.Mods;
using MonoLauncher.Core.Network;
using MonoLauncher.Core.Versions;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var paths = AppPaths.Default;
paths.EnsureDirectories();
var log = new LogService(paths, "cli-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
var handler = new SocketsHttpHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.All,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    UseProxy = false,
    ConnectTimeout = TimeSpan.FromSeconds(20)
};
var http = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(180)
};
http.DefaultRequestHeaders.UserAgent.ParseAdd("MonoLauncher-CLI/0.9");

var versions = new VersionListService(http, paths);
var instancesStore = new InstanceStore(paths);

try
{
    return await RunAsync(args);
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("失败: " + ex.Message);
    Console.ResetColor();
    log.Error("cli", "命令失败", ex);
    if (args.Contains("--verbose")) Console.WriteLine(ex);
    return 1;
}

async Task<int> RunAsync(string[] args)
{
    var cmd = args.FirstOrDefault() ?? "help";
    switch (cmd)
    {
        case "memory":
        {
            var (total, free) = MemoryAdvisor.GetMachineMemory();
            Console.WriteLine($"物理内存: {total / 1024.0:0.0} GB，可用: {free / 1024.0:0.0} GB");
            foreach (var aggressive in new[] { false, true })
            {
                var plan = MemoryAdvisor.Recommend(aggressive);
                Console.WriteLine($"{(aggressive ? "激进" : "标准")}档: -Xms{plan.MinMb}M -Xmx{plan.MaxMb}M  ({plan.Note})");
            }
            Console.WriteLine("JVM 调优参数:");
            foreach (var f in MemoryAdvisor.JvmOptimizationFlags) Console.WriteLine("  " + f);
            return 0;
        }
        case "deploy":
        {
            var version = args.ElementAtOrDefault(1) ?? throw new ArgumentException("用法: deploy <版本> [loader]");
            var loader = args.ElementAtOrDefault(2);
            var depInstances = new InstanceStore(paths);
            var depLoaders = new LoaderService(http, paths, versions);
            var depModrinth = new ModrinthService(http);
            var depJava = new JavaScanner();
            var depJavaInstaller = new JavaRuntimeInstaller(http, paths);
            var depMods = new ModManager(paths);
            var deployer = new EnvironmentDeployer(paths, http, versions, depJava, depJavaInstaller,
                depInstances, depLoaders, depModrinth, depMods);
            var progress = new Progress<string>(s => Console.WriteLine("  · " + s));
            var result = await deployer.DeployAsync(version, loader, null, true, true, progress);
            Console.WriteLine(result.Summary);
            return 0;
        }
        case "hardware":
        {
            var hw = MonoLauncher.Core.Hardware.HardwareProbe.Probe();
            Console.WriteLine($"CPU: {hw.CpuText}");
            Console.WriteLine($"内存: {hw.RamText}");
            Console.WriteLine($"显卡: {hw.GpuText}");
            Console.WriteLine($"结论: {hw.VerdictText} （可部署={hw.CanRunLocalAi}）");
            Console.WriteLine("可用模型:");
            foreach (var m in MonoLauncher.Agent.Local.LocalModelCatalog.Usable(hw))
                Console.WriteLine($"  · {m.Name}  {m.SizeText}  {m.RequirementText}");
            return 0;
        }
        case "local-run":
        {
            var modelId = GetOption(args, "--model") ?? "qwen2.5-1.5b";
            var prompt = GetOption(args, "--prompt") ?? "用一句话介绍 Minecraft 是什么。";
            var spec = MonoLauncher.Agent.Local.LocalModelCatalog.ById(modelId)
                       ?? throw new ArgumentException("未知模型 " + modelId);
            var hw = MonoLauncher.Core.Hardware.HardwareProbe.Probe();
            Console.WriteLine($"硬件: {hw.RamText} / {hw.CpuText} → {hw.VerdictText}");
            if (!hw.CanRunLocalAi) { Console.WriteLine("性能过低，无法部署：" + hw.Summary); return 1; }

            var deployer = new MonoLauncher.Agent.Local.LocalAiDeployer(http, paths);
            var last = DateTime.MinValue;
            var progress = new Progress<MonoLauncher.Agent.Local.LocalDeployProgress>(p =>
            {
                if ((DateTime.UtcNow - last).TotalSeconds < 2 && p.Fraction < 1) return;
                last = DateTime.UtcNow;
                Console.WriteLine($"  · {p.Stage} {p.DoneText}");
            });
            var path = await deployer.EnsureModelAsync(spec, progress, CancellationToken.None);
            Console.WriteLine("模型路径: " + path);

            using var llama = new MonoLauncher.Agent.Local.LocalLlamaService();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var threads2 = Math.Max(1, Environment.ProcessorCount - 1);
            var ok = await llama.LoadAutoAsync(path, spec.ContextSize, threads2);
            Console.WriteLine($"加载={ok} 用时 {sw.Elapsed.TotalSeconds:0.0}s 层数={llama.Status.GpuLayers} {(ok ? "" : llama.Status.Error)}");
            if (!ok) return 1;

            sw.Restart();
            var answer = await llama.CompleteAsync("你是 MonoLauncher 内置助手。", prompt, 128);
            Console.WriteLine($"推理耗时 {sw.Elapsed.TotalSeconds:0.0}s");
            Console.WriteLine("回答: " + answer);
            return 0;
        }
        case "nettest":
        {
            foreach (var url in new[]
                     {
                         "https://api.nuget.org/v3/index.json",
                         "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                         "https://www.baidu.com",
                         "https://launchermeta.mojang.com/mc/game/version_manifest.json"
                     })
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var s = await http.GetStringAsync(url);
                    Console.WriteLine($"{url} OK {s.Length} bytes {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{url} FAIL {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException is not null)
                        Console.WriteLine($"    inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    if (ex.InnerException?.InnerException is not null)
                        Console.WriteLine($"    inner2: {ex.InnerException.InnerException.Message}");
                }
            }
            return 0;
        }
        case "auth-test":
        {
            var auth = new MicrosoftAuthService(http);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var code = await auth.StartDeviceCodeAsync();
                sw.Stop();
                Console.WriteLine($"{sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"user_code={code.UserCode}");
                Console.WriteLine($"device_code={(code.DeviceCode.Length > 20 ? code.DeviceCode[..20] + "…" : code.DeviceCode)}");
                Console.WriteLine($"uri={code.VerificationUri}");
                Console.WriteLine($"interval={code.Interval} expires={code.ExpiresIn}");
                return 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"{sw.ElapsedMilliseconds} ms FAILED: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
        case "terracotta":        {
            var tc = new TerracottaService(paths, http);
            Console.WriteLine($"binary: {tc.BinaryPath}");
            var progress = new Progress<string>(s => Console.WriteLine("  " + s));
            if (!await tc.StartAsync(progress))
            {
                Console.WriteLine("启动失败");
                return 1;
            }
            Console.WriteLine($"port: {tc.Port}");
            Console.WriteLine("meta: " + await tc.GetMetaAsync());
            var state = await tc.GetStateAsync();
            Console.WriteLine($"state: {state?.State} / {state?.DisplayText}");
            await Task.Delay(1000);
            state = await tc.GetStateAsync();
            Console.WriteLine($"state(1s): {state?.State}");
            await tc.ShutdownAsync();
            Console.WriteLine("已停止");
            return 0;
        }
        case "import-scan":
        {
            var importer = new MonoLauncher.Core.Importers.InstanceImporter(paths, instancesStore);
            var found = importer.Detect();
            if (found.Count == 0) Console.WriteLine("没有找到可导入的实例");
            foreach (var c in found)
                Console.WriteLine($"{c.Name} | {c.SourceText} | mc={c.MinecraftVersion} loader={c.Loader} mods={c.ModCount} saves={c.SaveCount} | {c.SourceDirectory}");
            return 0;
        }
        case "import":
        {
            var name = args.ElementAtOrDefault(1) ?? throw new ArgumentException("用法: import <实例名>");
            var importer = new MonoLauncher.Core.Importers.InstanceImporter(paths, instancesStore);
            var candidate = importer.Detect().FirstOrDefault(c => c.Name == name)
                            ?? throw new InvalidOperationException($"未找到实例 {name}");
            var result = await importer.ImportAsync(candidate);
            Console.WriteLine($"已导入 {result.Instance.Name} -> {result.TargetDirectory}");
            Console.WriteLine($"存档 {result.CopiedSaves}，Mod {result.CopiedMods}，options.txt={result.CopiedOptions}");
            return 0;
        }
        case "tools":
        {
            var instances = new InstanceStore(paths);
            var accounts = new AccountStore(paths);
            var loaders = new LoaderService(http, paths, versions);
            var modrinth = new ModrinthService(http);
            var launchService = new LaunchService(paths);
            var javaScanner = new JavaScanner();
            var bridge = new DownloadCenterBridge(_ => { }, _ => { }, _ => { });
            var settings = new SettingsService(paths);
            var mods = new ModManager(paths);
            var javaInstaller = new JavaRuntimeInstaller(http, paths);
            var deployer = new EnvironmentDeployer(paths, http, versions, javaScanner, javaInstaller,
                instances, loaders, modrinth, mods);
            var modInstaller = new ModInstaller(http, modrinth, mods, paths);
            var tools = new LauncherTools(paths, instances, versions, loaders, modrinth,
                launchService, javaScanner, accounts, bridge, deployer, modInstaller, mods, settings);
            Console.WriteLine("== list_instances ==");
            Console.WriteLine(tools.ListInstances());
            Console.WriteLine("== list_installed_versions ==");
            Console.WriteLine(tools.ListInstalledVersions());
            Console.WriteLine("== search_modrinth(jei, 1.21.1, neoforge) ==");
            Console.WriteLine(await tools.SearchModrinth("jei", "1.21.1", "neoforge"));
            return 0;
        }
        case "list":
        {
            var manifest = await versions.GetManifestAsync();
            Console.WriteLine($"最新正式版: {manifest.Latest.Release}  最新快照: {manifest.Latest.Snapshot}");
            foreach (var v in manifest.Versions.Take(15))
                Console.WriteLine($"  {v.Id,-18} {v.Type,-10} {v.ReleaseTime:yyyy-MM-dd}");
            return 0;
        }
        case "install":
        {
            var id = args.ElementAtOrDefault(1) ?? "1.21.1";
            var loader = GetOption(args, "--loader");
            var loaderVersion = GetOption(args, "--loader-version");
            var manifest = await versions.GetManifestAsync();
            var summary = manifest.Versions.FirstOrDefault(v => v.Id == id)
                          ?? throw new InvalidOperationException($"版本 {id} 不在清单中");
            Console.WriteLine($"安装 {id} …");
            var info = await versions.DownloadVersionJsonAsync(summary);
            using var engine = new DownloadEngine(new DownloadOptions
            {
                MaxConcurrency = 8,
                MaxRetries = 3,
                ContinueOnError = true
            });
            var installer = new VersionInstaller(paths, versions);
            var lastLine = "";
            var lastUpdate = DateTime.MinValue;
            var progress = new Progress<InstallProgress>(p =>
            {
                if ((DateTime.UtcNow - lastUpdate).TotalMilliseconds < 1500) return;
                lastUpdate = DateTime.UtcNow;
                var line = $"[{p.Stage}] {p.Fraction,6:P0} {p.FilesDone}/{p.FilesTotal} {p.Detail}";
                if (line != lastLine)
                {
                    lastLine = line;
                    Console.WriteLine(line);
                }
            });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resolved = await installer.InstallAsync(info, engine, progress);
            Console.WriteLine($"原版完成 {resolved.Id}，用时 {sw.Elapsed.TotalSeconds:0.0}s");

            if (!string.IsNullOrWhiteSpace(loader))
            {
                var javaList = await new JavaScanner().ScanAsync(paths);
                var java = JavaScanner.SelectBest(javaList, resolved.JavaVersion?.MajorVersion ?? 21);
                var loaderService = new LoaderService(http, paths, versions);
                var textProgress = new Progress<string>(s => Console.WriteLine("  " + s));
                var result = await loaderService.InstallAsync(loader!, id, loaderVersion, java, textProgress);
                Console.WriteLine($"Loader {result.Kind} {result.LoaderVersion} -> 版本 {result.VersionId}");
                var loaderJson = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(
                    await File.ReadAllTextAsync(result.VersionJsonPath!), JsonDefaults.Options)!;
                var loaderResolved = await installer.InstallAsync(loaderJson, engine, progress);
                Console.WriteLine($"Loader 依赖完成 {loaderResolved.Id}");
            }

            sw.Stop();
            Console.WriteLine($"全部完成，用时 {sw.Elapsed.TotalSeconds:0.0}s");
            Console.WriteLine($"目录: {paths.VersionDir(resolved.Id)}");
            return 0;
        }
        case "launch":
        {
            var id = args.ElementAtOrDefault(1) ?? "1.21.1";
            var versionPath = paths.VersionJson(id);
            if (!File.Exists(versionPath))
                throw new FileNotFoundException($"版本 {id} 未安装: {versionPath}");
            var raw = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(
                await File.ReadAllTextAsync(versionPath), JsonDefaults.Options)!;
            var resolved = await versions.ResolveInheritanceAsync(raw);
            Console.WriteLine($"启动 {resolved.Id}，主类 {resolved.MainClass}");

            var javaList = await new JavaScanner().ScanAsync(paths);
            var java = JavaScanner.SelectBest(javaList, resolved.JavaVersion?.MajorVersion ?? 8)
                       ?? throw new InvalidOperationException("没有可用 Java");
            Console.WriteLine($"Java: {java.ExecutablePath} (major {java.MajorVersion})");

            var account = OfflineIdentity.Create(args.ElementAtOrDefault(2) ?? "Player");
            var gameDir = Path.Combine(paths.InstancesDir, "cli-test", "minecraft");
            Directory.CreateDirectory(gameDir);

            var plan = new LaunchService(paths).CreatePlan(resolved, account, java, gameDir, new InstanceLaunchSettings());
            if (!plan.Ready)
                throw new MissingFileException(plan.MissingFiles);

            var session = new GameLauncher(paths).Launch(plan.Plan);
            Console.WriteLine($"已启动 pid={session.ProcessId} 日志={session.LogPath}");
            session.OutputReceived += line =>
            {
                if (line.Contains("Setting user", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("LWJGL", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("OpenAL", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("  " + line);
            };

            var timeout = args.Contains("--wait") ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(40);
            int exit;
            try
            {
                exit = await session.WaitForExitAsync().WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                exit = int.MinValue;
            }
            if (exit == int.MinValue)
            {
                Console.WriteLine("游戏仍在运行（未在超时内退出），结束进程。");
                session.Kill();
            }
            else
            {
                Console.WriteLine($"游戏退出，code={exit}");
                var crash = GameLauncher.FindCrashReport(gameDir, session.StartedAt);
                if (crash is not null)
                {
                    Console.WriteLine("崩溃报告: " + crash);
                    Console.WriteLine(GameLauncher.SummariseCrash(crash));
                }
            }
            return 0;
        }
        case "java":
        {
            var list = await new JavaScanner().ScanAsync(paths);
            foreach (var j in list)
                Console.WriteLine($"{j.MajorVersion,-3} {j.Vendor,-20} {j.ExecutablePath}");
            return 0;
        }
        default:
            Console.WriteLine("""
                MonoLauncher CLI
                  list                 列出最新版本
                  install <version>    下载并安装版本
                  launch <version> [name] [--wait]
                  java                 列出检测到的 Java
                """);
            return 0;
    }
}
