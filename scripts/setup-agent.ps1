<#
.SYNOPSIS
  Creates (or refreshes) the agent's own workspace, outside this repository, and points the bridge at it.

.DESCRIPTION
  The agent plays the watched realm through the agent channel, answered by the war room (WarRoom.exe, one fresh
  Claude Code call per poke; docs/WARROOM.md). It must not see this repository, so its workspace lives elsewhere
  (default C:\Users\karab\Projects\orc-war-council). This:
  - installs agent\warroom-system.template.md as warroom-system.md (the war room's instructions for the agent), and the
    older agent chat's agent\CLAUDE.template.md and settings.template.json (always refreshed; in the repository they keep
    template names so no session here ever loads them);
  - copies agent\journal.md only if there is no journal yet;
  - creates the channel folder warroom\ (orders\done, lookups) and writes the order library into it (tools.txt), so the
    war room has it before the game's first session; the bridge rewrites it at every load;
  - sets the bridge's settings.txt: agent_dir = <workspace>\warroom, agent = <Mode>, agent_lords = <Lords>.
  Settings are only written while the game is closed; otherwise the matching console commands are printed.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1 -Mode live
#>
param(
    [string]$Workspace = 'C:\Users\karab\Projects\orc-war-council',
    [ValidateSet('off', 'shadow', 'live')][string]$Mode = 'shadow',
    [string]$Lords = 'all'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$template = Join-Path $repo 'agent'
if ((Resolve-Path $Workspace -ErrorAction SilentlyContinue) -and ((Resolve-Path $Workspace).Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase))) {
    Write-Host 'The workspace must be outside the repository, so the agent cannot read the code.' -ForegroundColor Red
    exit 1
}

$channel = Join-Path $Workspace 'warroom'
New-Item -ItemType Directory -Force (Join-Path $channel 'orders\done') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $channel 'lookups') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Workspace '.claude') | Out-Null
Copy-Item (Join-Path $template 'warroom-system.template.md') (Join-Path $Workspace 'warroom-system.md') -Force
Copy-Item (Join-Path $template 'CLAUDE.template.md') (Join-Path $Workspace 'CLAUDE.md') -Force
Copy-Item (Join-Path $template 'settings.template.json') (Join-Path $Workspace '.claude\settings.json') -Force
$journal = Join-Path $Workspace 'journal.md'
if (-not (Test-Path $journal)) { Copy-Item (Join-Path $template 'journal.md') $journal }

# the order library, from the same code the bridge uses (the smoke test can write it without the game)
$smoke = Join-Path $repo 'tests\SmokeTest\bin\Release\SmokeTest.exe'
if (Test-Path $smoke) {
    & $smoke --library (Join-Path $channel 'tools.txt') | Out-Host
} else {
    Write-Host 'Build the solution first (dotnet build -c Release) so the order library can be written; the bridge writes it at the next load anyway.' -ForegroundColor Yellow
}
Write-Host "Agent workspace ready: $Workspace (channel $channel)" -ForegroundColor Green

$open = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -match '^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+|TaleWorlds\.MountAndBlade\.Launcher\w*)$'
}
if ($open) {
    Write-Host 'The game is open, so its settings were not touched. In the game console type:' -ForegroundColor Yellow
    Write-Host "  agents.agent dir $channel"
    Write-Host "  agents.agent $Mode $(if ($Mode -eq 'live' -and $Lords -ne 'all') { $Lords })"
    exit 0
}

$settings = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\settings.txt'
New-Item -ItemType Directory -Force (Split-Path $settings) | Out-Null
$lines = @()
if (Test-Path $settings) { $lines = @(Get-Content $settings -Encoding UTF8) }
function Set-Key([string]$key, [string]$value) {
    $found = $false
    for ($i = 0; $i -lt $script:lines.Count; $i++) {
        if ($script:lines[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { $script:lines[$i] = "$key = $value"; $found = $true }
    }
    if (-not $found) { $script:lines += "$key = $value" }
}
Set-Key 'agent' $Mode
Set-Key 'agent_dir' $channel
Set-Key 'agent_lords' $Lords
[System.IO.File]::WriteAllLines($settings, [string[]]$lines, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Bridge settings: agent = $Mode, agent_lords = $Lords, agent_dir = $channel ($settings)" -ForegroundColor Green
