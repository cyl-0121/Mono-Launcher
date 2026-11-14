#requires -Version 5.1
<#
.SYNOPSIS
  MonoLauncher 下载站发布器（图形界面）。
.DESCRIPTION
  选择安装包 zip，填好服务器信息，点「上传并更新」：
    1. 计算 SHA256、大小
    2. 生成 releases.json（自动合并历史版本）
    3. 调用系统自带 scp 上传 zip 和 releases.json
  密码在随后弹出的命令行窗口里输入（scp 不支持参数传密码）。
.EXAMPLE
  ./scripts/publish-site.ps1
.EXAMPLE
  ./scripts/publish-site.ps1 -DryRun
  只生成文件并打印命令，不真正上传。
#>
param(
    [switch]$DryRun
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$websiteDir = Join-Path $projectRoot 'website'
$configFile = Join-Path $scriptRoot 'publish-site.config.json'

function Get-SavedConfig {
    if (Test-Path $configFile) {
        try { return Get-Content $configFile -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
    }
    return $null
}

function Save-Config($host_, $port, $user, $remoteDir) {
    $obj = [ordered]@{ host = $host_; port = $port; user = $user; remoteDir = $remoteDir }
    ($obj | ConvertTo-Json) | Set-Content -Path $configFile -Encoding UTF8
}

function Format-Size([long]$bytes) {
    if ($bytes -ge 1MB) { return ('{0:N1} MB' -f ($bytes / 1MB)) }
    if ($bytes -ge 1KB) { return ('{0:N0} KB' -f ($bytes / 1KB)) }
    return "$bytes B"
}

function Build-ReleaseJson([string]$zipPath, [string]$version, [string]$notes, [string]$remoteBaseUrl) {
    $zip = Get-Item $zipPath
    $sha = (Get-FileHash $zip.FullName -Algorithm SHA256).Hash
    $size = Format-Size $zip.Length
    $date = Get-Date -Format 'yyyy-MM-dd'
    $fileName = $zip.Name

    # 以本地 website/releases.json 为历史基础
    $history = @()
    $localJson = Join-Path $websiteDir 'releases.json'
    if (Test-Path $localJson) {
        try {
            $old = Get-Content $localJson -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($old.history) { $history = @($old.history) }
        } catch { }
    }

    # 去掉同版本旧记录，插到最前
    $history = @($history | Where-Object { $_.version -ne $version })
    $entry = [ordered]@{ version = $version; date = $date; size = $size; download = "downloads/$fileName" }
    $history = @($entry) + $history

    $github = "https://github.com/cyl-0121/Mono-Launcher/releases/download/v$version/MonoLauncher-win-x64.zip"

    $doc = [ordered]@{
        latest = [ordered]@{
            version  = $version
            date     = $date
            size     = $size
            sha256   = $sha
            download = "downloads/$fileName"
            github   = $github
            notes    = $notes
        }
        history = $history
    }

    $json = $doc | ConvertTo-Json -Depth 8
    $outPath = Join-Path $env:TEMP "releases-$version.json"
    [IO.File]::WriteAllText($outPath, $json, (New-Object Text.UTF8Encoding($false)))
    return [pscustomobject]@{
        JsonPath = $outPath
        Sha256   = $sha
        Size     = $size
        Date     = $date
        FileName = $fileName
    }
}

function New-UploadScript($zipPath, $jsonPath, $srvHost, $port, $user, $remoteDir) {
    $zip = Get-Item $zipPath
    $batPath = Join-Path $env:TEMP "monolauncher-upload-$(Get-Date -Format 'HHmmss').cmd"
    $lines = @(
        '@echo off',
        'chcp 65001 >nul',
        'echo ============================================',
        'echo   MonoLauncher: uploading to the server',
        'echo ============================================',
        'echo.',
        "echo 1/2  上传安装包 $($zip.Name) ...",
        "scp -P $port `"$($zip.FullName)`" ${user}@${srvHost}:${remoteDir}/downloads/",
        'if errorlevel 1 goto failed',
        'echo.',
        'echo 2/2  更新 releases.json ...',
        "scp -P $port `"$jsonPath`" ${user}@${srvHost}:${remoteDir}/releases.json",
        'if errorlevel 1 goto failed',
        'echo.',
        'echo 完成！刷新网站页面即可看到新版本。',
        'pause',
        'exit /b 0',
        ':failed',
        'echo.',
        'echo 上传失败。常见原因：密码错误、目录不存在、服务器未开 SFTP。',
        'echo 可手工执行下面这条命令排查：',
        "echo   scp -P $port `"$($zip.FullName)`" ${user}@${srvHost}:${remoteDir}/downloads/",
        'pause',
        'exit /b 1'
    )
    [IO.File]::WriteAllLines($batPath, $lines, (New-Object Text.UTF8Encoding($false)))
    return $batPath
}

if ($DryRun) {
    $zip = Join-Path $projectRoot "publish\MonoLauncher-win-x64.zip"
    if (-not (Test-Path $zip)) { Write-Host "找不到 $zip"; exit 1 }
    $info = Build-ReleaseJson $zip "0.9.1" "DryRun 测试说明"
    Write-Host "releases.json -> $($info.JsonPath)"
    Get-Content $info.JsonPath -Raw -Encoding UTF8 | Write-Host
    $bat = New-UploadScript $zip $info.JsonPath "example.com" 22 "root" "/var/www/monolauncher"
    Write-Host "upload script -> $bat"
    Get-Content $bat -Raw -Encoding UTF8 | Write-Host
    exit 0
}

# ---------------- GUI ----------------

$saved = Get-SavedConfig

$form = New-Object Windows.Forms.Form
$form.Text = 'MonoLauncher 下载站发布器'
$form.Size = New-Object Drawing.Size(700, 640)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.BackColor = [Drawing.Color]::FromArgb(18, 18, 18)
$form.ForeColor = [Drawing.Color]::White
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI', 9)

function New-Label($text, $x, $y, $w) {
    $l = New-Object Windows.Forms.Label
    $l.Text = $text
    $l.Location = New-Object Drawing.Point($x, $y)
    $l.Size = New-Object Drawing.Size($w, 20)
    $l.ForeColor = [Drawing.Color]::FromArgb(160, 160, 160)
    return $l
}

function New-TextBox($text, $x, $y, $w) {
    $t = New-Object Windows.Forms.TextBox
    $t.Text = $text
    $t.Location = New-Object Drawing.Point($x, $y)
    $t.Size = New-Object Drawing.Size($w, 24)
    $t.BackColor = [Drawing.Color]::FromArgb(10, 10, 10)
    $t.ForeColor = [Drawing.Color]::White
    $t.BorderStyle = 'FixedSingle'
    return $t
}

$y = 16
$form.Controls.Add((New-Label '服务器地址（IP 或域名）', 16, $y, 200))
$txtHost = New-TextBox ($(if ($saved) { $saved.host } else { '' })) 220 $y 200
$form.Controls.Add($txtHost)
$form.Controls.Add((New-Label '端口', 436, $y, 40))
$txtPort = New-TextBox ($(if ($saved) { [string]$saved.port } else { '22' })) 476 $y 80
$form.Controls.Add($txtPort)

$y += 40
$form.Controls.Add((New-Label '用户名（SSH 登录名）', 16, $y, 200))
$txtUser = New-TextBox ($(if ($saved) { $saved.user } else { 'root' })) 220 $y 200
$form.Controls.Add($txtUser)

$y += 40
$form.Controls.Add((New-Label '远程目录', 16, $y, 200))
$txtRemote = New-TextBox ($(if ($saved) { $saved.remoteDir } else { '/var/www/monolauncher' })) 220 $y 440
$form.Controls.Add($txtRemote)

$y += 40
$form.Controls.Add((New-Label '安装包 zip', 16, $y, 200))
$txtZip = New-TextBox '' 220 $y 340
$form.Controls.Add($txtZip)
$btnBrowse = New-Object Windows.Forms.Button
$btnBrowse.Text = '浏览…'
$btnBrowse.Location = New-Object Drawing.Point(570, $y - 1)
$btnBrowse.Size = New-Object Drawing.Size(90, 26)
$btnBrowse.BackColor = [Drawing.Color]::FromArgb(240, 240, 240)
$btnBrowse.ForeColor = [Drawing.Color]::Black
$form.Controls.Add($btnBrowse)

$y += 40
$form.Controls.Add((New-Label '版本号', 16, $y, 200))
$txtVersion = New-TextBox '0.9.1' 220 $y 120
$form.Controls.Add($txtVersion)
$form.Controls.Add((New-Label '（不带 v，例如 0.9.1）', 350, $y, 200))
$form.Controls.Add((New-Label '版本号示例 0.9.1', 1, 1, 1))

$y += 32
$form.Controls.Add((New-Label '更新说明（会显示在网站和启动器首页）', 16, $y, 360))
$y += 24
$txtNotes = New-Object Windows.Forms.TextBox
$txtNotes.Multiline = $true
$txtNotes.ScrollBars = 'Vertical'
$txtNotes.Location = New-Object Drawing.Point(16, $y)
$txtNotes.Size = New-Object Drawing.Size(644, 120)
$txtNotes.BackColor = [Drawing.Color]::FromArgb(10, 10, 10)
$txtNotes.ForeColor = [Drawing.Color]::White
$txtNotes.Text = "本次更新：`r`n- `r`n- "
$form.Controls.Add($txtNotes)

$y += 132
$btnUpload = New-Object Windows.Forms.Button
$btnUpload.Text = '上传并更新网站'
$btnUpload.Location = New-Object Drawing.Point(16, $y)
$btnUpload.Size = New-Object Drawing.Size(200, 38)
$btnUpload.BackColor = [Drawing.Color]::White
$btnUpload.ForeColor = [Drawing.Color]::Black
$btnUpload.FlatStyle = 'Flat'
$form.Controls.Add($btnUpload)

$btnLocal = New-Object Windows.Forms.Button
$btnLocal.Text = '只生成 releases.json'
$btnLocal.Location = New-Object Drawing.Point(226, $y)
$btnLocal.Size = New-Object Drawing.Size(170, 38)
$form.Controls.Add($btnLocal)

$txtLog = New-Object Windows.Forms.TextBox
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = 'Vertical'
$txtLog.Location = New-Object Drawing.Point(16, $y + 48)
$txtLog.Size = New-Object Drawing.Size(644, 100)
$txtLog.BackColor = [Drawing.Color]::FromArgb(8, 8, 8)
$txtLog.ForeColor = [Drawing.Color]::FromArgb(180, 180, 180)
$txtLog.Font = New-Object Drawing.Font('Consolas', 9)
$form.Controls.Add($txtLog)

function Write-Log([string]$text) {
    $txtLog.AppendText(("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $text + "`r`n"))
}

function Resolve-ZipPath {
    $p = $txtZip.Text.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($p)) {
        [Windows.Forms.MessageBox]::Show('请先选择安装包 zip。', '提示', 'OK', 'Warning') | Out-Null
        return $null
    }
    if (-not (Test-Path $p)) {
        [Windows.Forms.MessageBox]::Show("找不到文件：$p", '提示', 'OK', 'Warning') | Out-Null
        return $null
    }
    return (Resolve-Path $p).Path
}

$btnBrowse.Add_Click({
    $dlg = New-Object Windows.Forms.OpenFileDialog
    $dlg.Filter = 'zip 安装包 (*.zip)|*.zip|所有文件 (*.*)|*.*'
    $dlg.InitialDirectory = Join-Path $projectRoot 'publish'
    if ($dlg.ShowDialog() -eq 'OK') {
        $txtZip.Text = $dlg.FileName
        if ($dlg.FileName -match '(\d+\.\d+\.\d+)') { $txtVersion.Text = $Matches[1] }
    }
})

$btnLocal.Add_Click({
    try {
        $zip = Resolve-ZipPath
        if (-not $zip) { return }
        $info = Build-ReleaseJson $zip $txtVersion.Text.Trim() $txtNotes.Text
        Write-Log "已生成：$($info.JsonPath)"
        Write-Log "SHA256: $($info.Sha256)"
        [Windows.Forms.MessageBox]::Show("已生成 releases.json：`n$($info.JsonPath)`n`n把它手工上传到站点根目录即可。", '完成', 'OK', 'Information') | Out-Null
    } catch {
        Write-Log "失败：$($_.Exception.Message)"
    }
})

$btnUpload.Add_Click({
    try {
        $zip = Resolve-ZipPath
        if (-not $zip) { return }
        $srvHost = $txtHost.Text.Trim()
        $user = $txtUser.Text.Trim()
        $remoteDir = $txtRemote.Text.Trim().TrimEnd('/')
        $port = 22
        if (-not [int]::TryParse($txtPort.Text.Trim(), [ref]$port)) { $port = 22 }
        $version = $txtVersion.Text.Trim().TrimStart('v', 'V')
        if ([string]::IsNullOrWhiteSpace($srvHost)) { [Windows.Forms.MessageBox]::Show('请填写服务器地址。', '提示', 'OK', 'Warning') | Out-Null; return }
        if ([string]::IsNullOrWhiteSpace($user)) { [Windows.Forms.MessageBox]::Show('请填写用户名。', '提示', 'OK', 'Warning') | Out-Null; return }
        if ([string]::IsNullOrWhiteSpace($remoteDir)) { [Windows.Forms.MessageBox]::Show('请填写远程目录。', '提示', 'OK', 'Warning') | Out-Null; return }
        if ([string]::IsNullOrWhiteSpace($version)) { [Windows.Forms.MessageBox]::Show('请填写版本号。', '提示', 'OK', 'Warning') | Out-Null; return }

        Save-Config $srvHost $port $user $remoteDir
        $info = Build-ReleaseJson $zip $version $txtNotes.Text
        Write-Log "安装包：$($info.FileName)（$($info.Size)）"
        Write-Log "SHA256：$($info.Sha256)"
        Write-Log "版本：$version，日期：$($info.Date)"

        $bat = New-UploadScript $zip $info.JsonPath $srvHost $port $user $remoteDir
        Write-Log "上传脚本：$bat"

        if ($DryRun) {
            Write-Log 'DryRun 模式：不执行上传。'
            Write-Host "----- $bat -----"
            Get-Content $bat | Write-Host
            return
        }

        Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$bat`"" -WorkingDirectory $env:TEMP
        Write-Log '已启动上传窗口，请在黑色窗口里输入服务器密码。'
    } catch {
        Write-Log "失败：$($_.Exception.Message)"
        [Windows.Forms.MessageBox]::Show($_.Exception.Message, '出错', 'OK', 'Error') | Out-Null
    }
})

[void]$form.ShowDialog()
