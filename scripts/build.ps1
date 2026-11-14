#requires -Version 5.1
<#
.SYNOPSIS
  MonoLauncher 可复现构建脚本。

.DESCRIPTION
  还原依赖、编译、运行测试，并按需发布 self-contained 单文件 EXE。

.PARAMETER Publish
  发布 Release 单文件版本到 publish/win-x64。

.PARAMETER SkipTests
  跳过单元测试。

.EXAMPLE
  ./scripts/build.ps1
  ./scripts/build.ps1 -Publish
#>
param(
    [switch]$Publish,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== MonoLauncher build ==" -ForegroundColor Cyan
Write-Host "root: $root"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "未找到 dotnet。请安装 .NET SDK 10.0，或把 SDK 目录加入 PATH。"
}

$sdk = (dotnet --version)
Write-Host "SDK: $sdk"

Write-Host "`n-- restore --" -ForegroundColor Yellow
dotnet restore MonoLauncher.slnx
if ($LASTEXITCODE -ne 0) { throw "restore 失败" }

Write-Host "`n-- build --" -ForegroundColor Yellow
dotnet build MonoLauncher.slnx -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw "build 失败" }

if (-not $SkipTests) {
    Write-Host "`n-- test --" -ForegroundColor Yellow
    dotnet test tests/MonoLauncher.Tests/MonoLauncher.Tests.csproj --no-build -v q
    if ($LASTEXITCODE -ne 0) { throw "测试失败" }
}

if ($Publish) {
    Write-Host "`n-- publish (win-x64 single file) --" -ForegroundColor Yellow
    $out = Join-Path $root 'publish/win-x64'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet publish src/MonoLauncher.App/MonoLauncher.App.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -o $out
    if ($LASTEXITCODE -ne 0) { throw "publish 失败" }
    $exe = Join-Path $out 'MonoLauncher.exe'
    Write-Host "`n产物: $exe" -ForegroundColor Green
    Write-Host ("大小: {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))
}

Write-Host "`n完成。" -ForegroundColor Green
