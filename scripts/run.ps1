#requires -Version 7.0
<#
.SYNOPSIS
  以开发模式运行 GlassCraft。

.DESCRIPTION
  使用 dotnet run 启动 Avalonia 应用。可选参数会透传给应用：
    --page <home|versions|instances|mods|downloads|logs|agent|settings>
    --screenshot <path>     渲染一帧并保存后退出（用于自动化验证）
    --agent-message <text>  自动向 Agent 发送一条消息
    --launch-instance <id>  自动启动指定实例

.EXAMPLE
  ./scripts/run.ps1
  ./scripts/run.ps1 -Page versions
  ./scripts/run.ps1 -Screenshot docs/home.png
#>
param(
    [string]$Page,
    [string]$Screenshot,
    [string]$AgentMessage,
    [string]$LaunchInstance
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$args = @('run', '--project', 'src/GlassCraft.App/GlassCraft.App.csproj', '--')
if ($Page) { $args += @('--page', $Page) }
if ($Screenshot) { $args += @('--screenshot', $Screenshot) }
if ($AgentMessage) { $args += @('--agent-message', $AgentMessage) }
if ($LaunchInstance) { $args += @('--launch-instance', $LaunchInstance) }

& dotnet @args
