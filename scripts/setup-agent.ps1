<#
.SYNOPSIS
  Creates (or refreshes) the agent chat's own workspace, outside this repository, and points the bridge at it.

.DESCRIPTION
  The agent chat is a separate Claude Code session that plays the watched realm through the agent channel.
  It must not see this repository, so its workspace lives elsewhere (default C:\Users\karab\Projects\orc-war-council).
  This installs agent\CLAUDE.template.md as CLAUDE.md and agent\settings.template.json as .claude\settings.json
  there (always refreshed; in the repository they keep template names so no session here ever loads them as its own
  instructions), copies agent\journal.md only
  if there is no journal yet, creates channel\orders\done, and sets the bridge's settings.txt:
  agent_dir = <workspace>\channel and agent = shadow (orders checked, not carried out).
  Settings are only written while the game is closed; otherwise the matching console commands are printed.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1
#>
param(
    [string]$Workspace = 'C:\Users\karab\Projects\orc-war-council',
    [ValidateSet('off', 'shadow', 'live')][string]$Mode = 'shadow'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$template = Join-Path $repo 'agent'
if ((Resolve-Path $Workspace -ErrorAction SilentlyContinue) -and ((Resolve-Path $Workspace).Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase))) {
    Write-Host 'The workspace must be outside the repository, so the agent cannot read the code.' -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force (Join-Path $Workspace 'channel\orders\done') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Workspace '.claude') | Out-Null
Copy-Item (Join-Path $template 'CLAUDE.template.md') (Join-Path $Workspace 'CLAUDE.md') -Force
Copy-Item (Join-Path $template 'settings.template.json') (Join-Path $Workspace '.claude\settings.json') -Force
$journal = Join-Path $Workspace 'journal.md'
if (-not (Test-Path $journal)) { Copy-Item (Join-Path $template 'journal.md') $journal }
$channel = Join-Path $Workspace 'channel'
Write-Host "Agent workspace ready: $Workspace" -ForegroundColor Green

$open = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -match '^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+|TaleWorlds\.MountAndBlade\.Launcher\w*)$'
}
if ($open) {
    Write-Host 'The game is open, so its settings were not touched. In the game console type:' -ForegroundColor Yellow
    Write-Host "  agents.agent dir $channel"
    Write-Host "  agents.agent $Mode"
    exit 0
}

$settings = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\settings.txt'
New-Item -ItemType Directory -Force (Split-Path $settings) | Out-Null
$lines = @()
if (Test-Path $settings) { $lines = @(Get-Content $settings) }
function Set-Key([string]$key, [string]$value) {
    $found = $false
    for ($i = 0; $i -lt $script:lines.Count; $i++) {
        if ($script:lines[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { $script:lines[$i] = "$key = $value"; $found = $true }
    }
    if (-not $found) { $script:lines += "$key = $value" }
}
Set-Key 'agent' $Mode
Set-Key 'agent_dir' $channel
if (-not ($lines -match '^\s*agent_lords\s*=')) { Set-Key 'agent_lords' 'all' }
Set-Content -Path $settings -Value $lines -Encoding ASCII
Write-Host "Bridge settings: agent = $Mode, agent_dir = $channel ($settings)" -ForegroundColor Green
