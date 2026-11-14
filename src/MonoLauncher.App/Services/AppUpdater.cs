using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace MonoLauncher.App.Services;

/// <summary>
/// Downloads a new build, swaps the running executable and restarts it.
/// The swap cannot happen while the exe is running, so a small .cmd helper waits for this
/// process to exit, replaces the file (keeping a .bak for rollback), then starts it again.
/// </summary>
public static class AppUpdater
{
    /// <summary>Builds the zip URL: prefer the configured mirror, fall back to GitHub.</summary>
    public static string BuildPackageUrl(string? downloadSiteUrl, string repository, string tag)
    {
        // A mirror may carry a share code in its query string (e.g. ?sharecode=xxx);
        // keep it attached to the asset path.
        if (!string.IsNullOrWhiteSpace(downloadSiteUrl) &&
            Uri.TryCreate(downloadSiteUrl.Trim(), UriKind.Absolute, out var site))
        {
            var basePart = site.GetLeftPart(UriPartial.Path).TrimEnd('/');
            var query = site.Query; // includes '?'
            return $"{basePart}/downloads/MonoLauncher-win-x64.zip{query}";
        }

        var cleanTag = tag.Trim();
        if (string.IsNullOrWhiteSpace(cleanTag)) cleanTag = "latest";
        return $"https://github.com/{repository.Trim().Trim('/')}/releases/download/{cleanTag}/MonoLauncher-win-x64.zip";
    }

    /// <summary>
    /// Downloads the package and schedules the replacement. Returns false with a reason when the
    /// update cannot be installed (no write permission, package without the exe, etc.).
    /// The caller should shut the app down when this returns true.
    /// </summary>
    public static async Task<(bool Ok, string Message)> DownloadAndInstallAsync(
        string packageUrl,
        IProgress<(double Fraction, string Stage)> progress,
        CancellationToken ct,
        bool install = true)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return (false, "拿不到当前程序路径，无法自动更新。请手动下载。");

        var exeDir = Path.GetDirectoryName(exePath)!;
        var newExe = exePath + ".new";
        var bakExe = exePath + ".bak";
        var tempDir = Path.Combine(Path.GetTempPath(), "MonoLauncher-update");
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, "MonoLauncher-win-x64.zip");

        // 1) download
        progress.Report((0, "正在连接下载源…"));
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MonoLauncher/updater");
            using var response = await http.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (false, $"下载失败：HTTP {(int)response.StatusCode}。可到官网手动下载。");

            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(zipPath);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                var fraction = total > 0 ? Math.Clamp((double)received / total, 0, 1) : 0;
                progress.Report((fraction, total > 0
                    ? $"下载中 {fraction:P0}（{received / 1024d / 1024d:F1} / {total / 1024d / 1024d:F1} MB）"
                    : $"下载中 {received / 1024d / 1024d:F1} MB"));
            }
        }
        catch (OperationCanceledException)
        {
            return (false, "更新已取消。");
        }
        catch (Exception ex)
        {
            return (false, "下载失败：" + ex.Message);
        }

        // 2) extract the exe next to the running one
        progress.Report((1, "正在解压…"));
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, "MonoLauncher.exe", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return (false, "更新包里没有 MonoLauncher.exe，已放弃。");
            if (File.Exists(newExe)) File.Delete(newExe);
            entry.ExtractToFile(newExe, overwrite: true);
        }
        catch (Exception ex)
        {
            return (false, "解压失败：" + ex.Message);
        }

        if (!install)
        {
            progress.Report((1, "自检完成：包已下载并解压"));
            return (true, $"自检通过：{newExe}（{new FileInfo(newExe).Length / 1024d / 1024d:F1} MB）");
        }

        // 3) make sure we can write next to the exe before promising anything
        try
        {
            var probe = exePath + ".probe";
            await File.WriteAllTextAsync(probe, "ok", ct).ConfigureAwait(false);
            File.Delete(probe);
        }
        catch
        {
            return (false, $"没有写入权限：{exeDir}。请把启动器放到可写目录后再试。");
        }

        // 4) schedule the swap
        // A .cmd file would be read as GBK by cmd.exe and mangle non-ASCII paths
        // (this project lives under a Chinese directory), so drive the swap with PowerShell
        // written as UTF-8 with BOM.
        var script = Path.Combine(tempDir, "apply-update.ps1");
        static string Q(string s) => s.Replace("'", "''");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        sb.AppendLine("Start-Sleep -Seconds 2");
        sb.AppendLine($"$exe = '{Q(exePath)}'");
        sb.AppendLine($"$new = '{Q(newExe)}'");
        sb.AppendLine($"$bak = '{Q(bakExe)}'");
        sb.AppendLine($"$zip = '{Q(zipPath)}'");
        // Wait for this very process to exit before touching the exe.
        sb.AppendLine("$procId = " + Environment.ProcessId + "");
        sb.AppendLine("for ($i = 0; $i -lt 240; $i++) {");
        sb.AppendLine("  if (-not (Get-Process -Id $procId -ErrorAction SilentlyContinue)) { break }");
        sb.AppendLine("  Start-Sleep -Milliseconds 250");
        sb.AppendLine("}");
        sb.AppendLine("Start-Sleep -Milliseconds 800");
        // Rename instead of copy: atomic, and it fails fast if the file is still locked.
        sb.AppendLine("$moved = $false");
        sb.AppendLine("for ($i = 0; $i -lt 60; $i++) {");
        sb.AppendLine("  Move-Item -LiteralPath $exe -Destination $bak -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("  if (Test-Path -LiteralPath $bak) { $moved = $true; break }");
        sb.AppendLine("  Start-Sleep -Milliseconds 300");
        sb.AppendLine("}");
        sb.AppendLine("$installed = $false");
        sb.AppendLine("if ($moved) {");
        sb.AppendLine("  for ($i = 0; $i -lt 60; $i++) {");
        sb.AppendLine("    Move-Item -LiteralPath $new -Destination $exe -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("    if (Test-Path -LiteralPath $exe) { $installed = $true; break }");
        sb.AppendLine("    Start-Sleep -Milliseconds 300");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("if (-not $installed) {");
        sb.AppendLine("  if (-not (Test-Path -LiteralPath $exe)) {");
        sb.AppendLine("    Move-Item -LiteralPath $bak -Destination $exe -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("  }");
        sb.AppendLine("  Add-Type -AssemblyName System.Windows.Forms");
        sb.AppendLine("  [System.Windows.Forms.MessageBox]::Show('更新失败：无法替换程序文件，已保留旧版本。', 'MonoLauncher') | Out-Null");
        sb.AppendLine("  Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("  Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("  exit 1");
        sb.AppendLine("}");
        sb.AppendLine("Start-Process -FilePath $exe");
        sb.AppendLine("Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue");
        await File.WriteAllTextAsync(script, sb.ToString(), new System.Text.UTF8Encoding(true), ct)
            .ConfigureAwait(false);
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = tempDir
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-WindowStyle");
            psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(script);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return (false, "无法启动更新程序：" + ex.Message);
        }

        progress.Report((1, "即将重启完成更新…"));
        return (true, "更新已就绪，程序将自动重启。");
    }
}
