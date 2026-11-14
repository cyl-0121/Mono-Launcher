#requires -Version 5.1
<#
.SYNOPSIS
  MonoLauncher 下载站发布器（图形界面）。
.DESCRIPTION
  选一个安装包 zip、填版本号和更新说明，点「上传并更新网站」：
    1. 计算 SHA256 与文件大小
    2. 生成 releases.json（自动合并历史版本）
    3. 上传 zip + releases.json 到服务器
  上传方式支持 FTP（任何面板都支持）和 SFTP/SCP（走系统自带 scp）。
.EXAMPLE
  ./publish-site.cmd
.EXAMPLE
  ./publish-site.ps1 -DryRun
  只生成文件并打印，不联网。
#>
param(
    [switch]$DryRun
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$websiteDir = Join-Path $projectRoot 'website'
$configFile = Join-Path $scriptDir 'publish-site.config.json'

# ---------------------------------------------------------------- helpers

function Get-SavedConfig {
    if (Test-Path $configFile) {
        try { return Get-Content $configFile -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
    }
    return $null
}

function Save-Config($h, $p, $u, $dir, $mode) {
    $obj = [ordered]@{ host = $h; port = $p; user = $u; remoteDir = $dir; mode = $mode }
    [IO.File]::WriteAllText($configFile, ($obj | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}

function Format-Size([long]$bytes) {
    if ($bytes -ge 1MB) { return ('{0:N1} MB' -f ($bytes / 1MB)) }
    if ($bytes -ge 1KB) { return ('{0:N0} KB' -f ($bytes / 1KB)) }
    return "$bytes B"
}

function Build-ReleaseJson([string]$zipPath, [string]$version, [string]$notes) {
    $zip = Get-Item -LiteralPath $zipPath
    $sha = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash
    $size = Format-Size $zip.Length
    $date = Get-Date -Format 'yyyy-MM-dd'
    $fileName = $zip.Name

    $history = @()
    $localJson = Join-Path $websiteDir 'releases.json'
    if (Test-Path -LiteralPath $localJson) {
        try {
            $old = Get-Content -LiteralPath $localJson -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($old.history) { $history = @($old.history) }
        } catch { }
    }
    $history = @($history | Where-Object { $_.version -ne $version })
    $entry = [ordered]@{ version = $version; date = $date; size = $size; download = "downloads/$fileName" }
    $history = @($entry) + $history

    $github = "https://github.com/cyl-0121/Mono-Launcher/releases/download/v$version/MonoLauncher-win-x64.zip"

    $doc = [ordered]@{
        latest  = [ordered]@{
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

function Send-Scp([string]$localPath, [string]$remotePath, [string]$host_, [int]$port, [string]$user) {
    # scp 会在这个控制台窗口里要求输入密码
    & scp.exe -P $port $localPath "${user}@${host_}:${remotePath}"
    return ($LASTEXITCODE -eq 0)
}

function Send-Ftp([string]$localPath, [string]$remotePath, [string]$host_, [int]$port, [string]$user, [string]$password) {
    $uri = "ftp://${host_}:${port}${remotePath}"
    $req = [System.Net.FtpWebRequest]::Create($uri)
    $req.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $req.Credentials = New-Object System.Net.NetworkCredential($user, $password)
    $req.UseBinary = $true
    $req.UsePassive = $true
    $req.Timeout = 600000
    $req.ReadWriteTimeout = 600000
    $stream = $req.GetRequestStream()
    try {
        $file = [IO.File]::OpenRead($localPath)
        try { $file.CopyTo($stream) } finally { $file.Dispose() }
    } finally {
        $stream.Dispose()
    }
    $resp = $req.GetResponse()
    $resp.Dispose()
    return $true
}

# ---------------------------------------------------------------- dry run

if ($DryRun) {
    $zip = Join-Path $projectRoot 'publish\MonoLauncher-win-x64.zip'
    if (-not (Test-Path -LiteralPath $zip)) { Write-Host "找不到 $zip"; exit 1 }
    $info = Build-ReleaseJson $zip '0.9.9' 'DryRun 测试说明'
    Write-Host "releases.json -> $($info.JsonPath)"
    Write-Host "sha256 = $($info.Sha256)"
    Write-Host "size   = $($info.Size)"
    Get-Content -LiteralPath $info.JsonPath -Raw -Encoding UTF8 | Write-Host
    exit 0
}

# ---------------------------------------------------------------- GUI

$saved = Get-SavedConfig

$form = New-Object Windows.Forms.Form
$form.Text = 'MonoLauncher 发布器'
$form.ClientSize = New-Object Drawing.Size(690, 620)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.BackColor = [Drawing.Color]::FromArgb(18, 18, 18)
$form.ForeColor = [Drawing.Color]::White
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI', 9)

$labelColor = [Drawing.Color]::FromArgb(150, 150, 150)
$fieldBg = [Drawing.Color]::FromArgb(10, 10, 10)

function New-Lbl([string]$text, [int]$left, [int]$top, [int]$width) {
    $l = New-Object Windows.Forms.Label
    $l.Text = $text
    $l.Left = $left
    $l.Top = $top
    $l.Width = $width
    $l.Height = 20
    $l.ForeColor = $labelColor
    return $l
}

function New-Txt([string]$text, [int]$left, [int]$top, [int]$width) {
    $t = New-Object Windows.Forms.TextBox
    $t.Text = $text
    $t.Left = $left
    $t.Top = $top
    $t.Width = $width
    $t.BackColor = $fieldBg
    $t.ForeColor = [Drawing.Color]::White
    $t.BorderStyle = 'FixedSingle'
    return $t
}

function New-Btn([string]$text, [int]$left, [int]$top, [int]$width, [int]$height, [bool]$primary) {
    $b = New-Object Windows.Forms.Button
    $b.Text = $text
    $b.Left = $left
    $b.Top = $top
    $b.Width = $width
    $b.Height = $height
    $b.FlatStyle = 'Flat'
    if ($primary) {
        $b.BackColor = [Drawing.Color]::White
        $b.ForeColor = [Drawing.Color]::Black
    } else {
        $b.BackColor = [Drawing.Color]::FromArgb(45, 45, 45)
        $b.ForeColor = [Drawing.Color]::White
    }
    return $b
}

# 上传方式
[void]$form.Controls.Add((New-Lbl '上传方式' 16 16 80))
$cboMode = New-Object Windows.Forms.ComboBox
$cboMode.DropDownStyle = 'DropDownList'
$cboMode.Left = 110
$cboMode.Top = 14
$cboMode.Width = 170
[void]$cboMode.Items.Add('FTP（面板常见）')
[void]$cboMode.Items.Add('SFTP / SCP')
$cboMode.SelectedIndex = 0
if ($saved -and $saved.mode -eq 'scp') { $cboMode.SelectedIndex = 1 }
[void]$form.Controls.Add($cboMode)

[void]$form.Controls.Add((New-Lbl '服务器' 310 16 50))
$txtHost = New-Txt ($(if ($saved) { $saved.host } else { '' })) 365 14 150
[void]$form.Controls.Add($txtHost)
[void]$form.Controls.Add((New-Lbl '端口' 530 16 40))
$txtPort = New-Txt ($(if ($saved) { [string]$saved.port } else { '21' })) 570 14 90
[void]$form.Controls.Add($txtPort)

[void]$form.Controls.Add((New-Lbl '用户名' 16 54 80))
$txtUser = New-Txt ($(if ($saved) { $saved.user } else { '' })) 110 52 170
[void]$form.Controls.Add($txtUser)

[void]$form.Controls.Add((New-Lbl '密码' 310 54 50))
$txtPass = New-Txt '' 365 52 150
$txtPass.UseSystemPasswordChar = $true
[void]$form.Controls.Add($txtPass)

[void]$form.Controls.Add((New-Lbl '远程目录' 16 92 80))
$txtRemote = New-Txt ($(if ($saved) { $saved.remoteDir } else { '/' })) 110 90 550
[void]$form.Controls.Add($txtRemote)
[void]$form.Controls.Add((New-Lbl '例如 /www/wwwroot/你的域名 或 /htdocs' 110 112 500))

$line1 = New-Object Windows.Forms.Panel
$line1.Left = 16
$line1.Top = 140
$line1.Width = 646
$line1.Height = 1
$line1.BackColor = [Drawing.Color]::FromArgb(40, 40, 40)
[void]$form.Controls.Add($line1)

[void]$form.Controls.Add((New-Lbl '安装包 zip' 16 156 80))
$txtZip = New-Txt '' 110 154 450
[void]$form.Controls.Add($txtZip)
$btnBrowse = New-Btn '浏览…' 570 153 90 26 $false
[void]$form.Controls.Add($btnBrowse)

[void]$form.Controls.Add((New-Lbl '版本号' 16 194 80))
$txtVersion = New-Txt '0.9.1' 110 192 100
[void]$form.Controls.Add($txtVersion)
[void]$form.Controls.Add((New-Lbl '不带 v，例如 0.9.1' 220 194 200))

[void]$form.Controls.Add((New-Lbl '更新说明' 16 226 80))
$txtNotes = New-Object Windows.Forms.TextBox
$txtNotes.Multiline = $true
$txtNotes.ScrollBars = 'Vertical'
$txtNotes.Left = 110
$txtNotes.Top = 224
$txtNotes.Width = 550
$txtNotes.Height = 120
$txtNotes.BackColor = $fieldBg
$txtNotes.ForeColor = [Drawing.Color]::White
$txtNotes.Text = "本次更新：`r`n- "
[void]$form.Controls.Add($txtNotes)

$btnUpload = New-Btn '上传并更新网站' 16 358 200 42 $true
[void]$form.Controls.Add($btnUpload)
$btnLocal = New-Btn '只生成 releases.json' 226 358 180 42 $false
[void]$form.Controls.Add($btnLocal)
$btnOpenFolder = New-Btn '打开网站目录' 416 358 140 42 $false
[void]$form.Controls.Add($btnOpenFolder)

$txtLog = New-Object Windows.Forms.TextBox
$txtLog.Multiline = $true
$txtLog.ReadOnly = $true
$txtLog.ScrollBars = 'Vertical'
$txtLog.Left = 16
$txtLog.Top = 414
$txtLog.Width = 646
$txtLog.Height = 190
$txtLog.BackColor = [Drawing.Color]::FromArgb(8, 8, 8)
$txtLog.ForeColor = [Drawing.Color]::FromArgb(170, 170, 170)
$txtLog.Font = New-Object Drawing.Font('Consolas', 9)
[void]$form.Controls.Add($txtLog)

function Write-Log([string]$text) {
    $txtLog.AppendText(("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $text + "`r`n"))
}

function Resolve-ZipPath {
    $p = $txtZip.Text.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($p)) {
        [Windows.Forms.MessageBox]::Show('请先选择安装包 zip。', '提示', 'OK', 'Warning') | Out-Null
        return $null
    }
    if (-not (Test-Path -LiteralPath $p)) {
        [Windows.Forms.MessageBox]::Show("找不到文件：$p", '提示', 'OK', 'Warning') | Out-Null
        return $null
    }
    return (Resolve-Path -LiteralPath $p).Path
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

$btnOpenFolder.Add_Click({ Start-Process explorer.exe $websiteDir })

$btnLocal.Add_Click({
    try {
        $zip = Resolve-ZipPath
        if (-not $zip) { return }
        $ver = $txtVersion.Text.Trim().TrimStart('v', 'V')
        $info = Build-ReleaseJson $zip $ver $txtNotes.Text
        Write-Log "已生成 $($info.JsonPath)（$($info.Size)，SHA256 $($info.Sha256.Substring(0,12))…）"
        [Windows.Forms.MessageBox]::Show("已生成 releases.json：`n$($info.JsonPath)`n`n把它连同 zip 一起手工上传到站点即可。", '完成', 'OK', 'Information') | Out-Null
    } catch {
        Write-Log "失败：$($_.Exception.Message)"
    }
})

$btnUpload.Add_Click({
    try {
        $zip = Resolve-ZipPath
        if (-not $zip) { return }

        $h = $txtHost.Text.Trim()
        $u = $txtUser.Text.Trim()
        $dir = $txtRemote.Text.Trim().TrimEnd('/')
        $port = 21
        if (-not [int]::TryParse($txtPort.Text.Trim(), [ref]$port)) { $port = 21 }
        $ver = $txtVersion.Text.Trim().TrimStart('v', 'V')
        $mode = if ($cboMode.SelectedIndex -eq 1) { 'scp' } else { 'ftp' }

        if ([string]::IsNullOrWhiteSpace($h)) { [Windows.Forms.MessageBox]::Show('请填写服务器地址。', '提示', 'OK', 'Warning') | Out-Null; return }
        if ([string]::IsNullOrWhiteSpace($u)) { [Windows.Forms.MessageBox]::Show('请填写用户名。', '提示', 'OK', 'Warning') | Out-Null; return }
        if ([string]::IsNullOrWhiteSpace($ver)) { [Windows.Forms.MessageBox]::Show('请填写版本号。', '提示', 'OK', 'Warning') | Out-Null; return }

        Save-Config $h $port $u $dir $mode
        $info = Build-ReleaseJson $zip $ver $txtNotes.Text
        Write-Log "版本 $ver ｜ $($info.FileName) ｜ $($info.Size)"
        Write-Log "SHA256 $($info.Sha256)"

        $remoteZip = "$dir/downloads/$($info.FileName)"
        $remoteJson = "$dir/releases.json"

        if ($mode -eq 'scp') {
            Write-Log '正在用 scp 上传 zip（随后会要求输入密码）…'
            if (-not (Send-Scp $zip $remoteZip $h $port $u)) { throw 'scp 上传 zip 失败（密码或目录不对）' }
            Write-Log '正在上传 releases.json …'
            if (-not (Send-Scp $info.JsonPath $remoteJson $h $port $u)) { throw 'scp 上传 releases.json 失败' }
        } else {
            $pwdText = $txtPass.Text
            if ([string]::IsNullOrWhiteSpace($pwdText)) { [Windows.Forms.MessageBox]::Show('FTP 模式需要填密码。', '提示', 'OK', 'Warning') | Out-Null; return }
            Write-Log '正在通过 FTP 上传 zip（约 68 MB，请稍候）…'
            Send-Ftp $zip $remoteZip $h $port $u $pwdText | Out-Null
            Write-Log 'zip 上传完成，正在上传 releases.json …'
            Send-Ftp $info.JsonPath $remoteJson $h $port $u $pwdText | Out-Null
        }

        Write-Log '全部完成。刷新网站页面即可看到新版本。'
        [Windows.Forms.MessageBox]::Show("上传完成。`n刷新网站即可看到 v$ver。", '成功', 'OK', 'Information') | Out-Null
    } catch {
        Write-Log "失败：$($_.Exception.Message)"
        [Windows.Forms.MessageBox]::Show($_.Exception.Message, '失败', 'OK', 'Error') | Out-Null
    }
})

[void]$form.ShowDialog()
