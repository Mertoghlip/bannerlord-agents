<#
.SYNOPSIS
  Makes (or tidies) an agent: a folder outside this repository that holds everything that is the agent's own.

.DESCRIPTION
  An agent is a folder (since 0.9; docs/WARROOM.md). The war room (WarRoom.exe, one fresh Claude Code call per poke) runs
  from it, and the game's bridge finds it in its settings ("agents = <folder>; <folder>"). The folder holds:
  - agent.txt: the realm it commands, its mode (off, shadow or live), who obeys it, and its channel folder;
  - warroom-system.md and chronicler-system.md: its instructions, and the chronicler's (who writes the story so far);
  - warroom.txt: its war room's settings (model, effort, thinking caps, where its logs go);
  - warroom\: the channel the bridge and the war room talk through (orders\done, lookups, the order library tools.txt);
  - memory\<campaign>\: its memory, made by the war room (docs/MEMORY.md).
  This script:
  - writes agent.txt (the values given here; any not given are kept from the folder's agent.txt);
  - installs the instructions from agent\*.template.md ONLY where the folder has none, so edits to a prompt are never lost;
    -Force puts fresh copies in. A fresh copy for another realm has the two lines that name the realm rewritten (the title and
    the opening line) from -RealmTitle and -RealmLine;
  - gives a new agent a warroom.txt copied from -SettingsFrom (so agents think alike), with its own log_dir
    (logs\warroom-<realm>); an existing warroom.txt is left alone;
  - writes the order library into the channel;
  - adds the folder to "agents" in the bridge's settings.txt (with the game closed). The first time, the agents the settings
    of 0.8 describe (agent_dir, agent_dir.<realm>) are listed first, in their order.
  Then scripts\agents.ps1 start runs every agent's war room.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1 -Workspace C:\Users\karab\Projects\orc-war-council -Realm mistymountainorcs -Mode live

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1 -Workspace C:\Users\karab\Projects\dolguldur-war-council -Realm dolguldur -Mode live -RealmTitle "Dol Guldur" -RealmLine "Dol Guldur, the fortress-realm of the Necromancer in southern Mirkwood ruled by Khamul, the Easterling"
#>
param(
    [string]$Workspace = 'C:\Users\karab\Projects\orc-war-council',
    [string]$Realm = '',
    [ValidateSet('', 'off', 'shadow', 'live')][string]$Mode = '',
    [string]$Lords = '',
    [string]$RealmTitle = '',
    [string]$RealmLine = '',
    [string]$SettingsFrom = 'C:\Users\karab\Projects\orc-war-council\warroom.txt',
    # put fresh copies of the instructions in, over the folder's own
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$template = Join-Path $repo 'agent'
if ((Resolve-Path $Workspace -ErrorAction SilentlyContinue) -and ((Resolve-Path $Workspace).Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase))) {
    Write-Host 'The workspace must be outside the repository, so the agent cannot read the code.' -ForegroundColor Red
    exit 1
}
$utf8 = New-Object System.Text.UTF8Encoding $false
function Read-Pairs([string]$path) {
    $d = @{}
    if (Test-Path $path) {
        foreach ($l in [System.IO.File]::ReadAllLines($path, $utf8)) { if ($l -match '^\s*([^#=\s][^=]*?)\s*=\s*(.*)$') { $d[$Matches[1].Trim().ToLowerInvariant()] = $Matches[2].Trim() } }
    }
    return $d
}

New-Item -ItemType Directory -Force $Workspace | Out-Null
$Workspace = (Resolve-Path $Workspace).Path

# agent.txt: what is given here, else what the folder already says
$agentFile = Join-Path $Workspace 'agent.txt'
$had = Read-Pairs $agentFile
if (-not $Realm) { $Realm = $had['realm'] }
if (-not $Realm) { Write-Host 'Name the realm: -Realm <kingdom id> (the folder has no agent.txt yet).' -ForegroundColor Red; exit 1 }
if (-not $Mode) { $Mode = $(if ($had['mode']) { $had['mode'] } else { 'shadow' }) }
if (-not $Lords) { $Lords = $(if ($had['lords']) { $had['lords'] } else { 'all' }) }
$channelName = $(if ($had['channel']) { $had['channel'] } else { 'warroom' })
[System.IO.File]::WriteAllLines($agentFile, [string[]]@(
    '# This agent: the realm it commands and how. The game''s bridge reads this at every load (docs/WARROOM.md).',
    "realm = $Realm",
    '# off, shadow (orders checked and answered, none carried out) or live',
    "mode = $Mode",
    '# in live mode, who obeys: all, or hero ids or names, comma-separated',
    "lords = $Lords",
    '# the folder in this one the bridge and the war room talk through',
    "channel = $channelName"), $utf8)
Write-Host "agent.txt: realm $Realm, mode $Mode, lords $Lords, channel $channelName" -ForegroundColor Green

$channel = Join-Path $Workspace $channelName
New-Item -ItemType Directory -Force (Join-Path $channel 'orders\done') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $channel 'lookups') | Out-Null

# the instructions: only where the folder has none (or with -Force), so the agent's own prompts are never lost
$fresh = @()
foreach ($pair in @(@('warroom-system.template.md', 'warroom-system.md'), @('chronicler-system.template.md', 'chronicler-system.md'))) {
    $to = Join-Path $Workspace $pair[1]
    if ((Test-Path $to) -and -not $Force) { Write-Host "Kept the folder's own $($pair[1])."; continue }
    Copy-Item (Join-Path $template $pair[0]) $to -Force
    $fresh += $to
    Write-Host "Installed $($pair[1]) from the template." -ForegroundColor Green
}
# a fresh copy for another realm: the two lines that name the realm, nothing else (read and written as UTF-8, never through Get-Content)
if ($RealmTitle) {
    foreach ($p in $fresh) {
        $text = [System.IO.File]::ReadAllText($p, $utf8)
        $text = [regex]::Replace($text, '(?m)^# War council of .*$', '# War council of ' + $RealmTitle)
        if ($RealmLine) { $text = [regex]::Replace($text, '(You are the war council of |You command )[^\r\n]*?, in a live campaign', { param($m) $m.Groups[1].Value + $RealmLine + ', in a live campaign' }) }
        [System.IO.File]::WriteAllText($p, $text, $utf8)
    }
    if ($fresh.Count -gt 0) { Write-Host "Fresh instructions written for $RealmTitle ($Realm)." -ForegroundColor Green }
}

# a new agent's war room: another agent's settings, so they think alike, with logs of its own
$wr = Join-Path $Workspace 'warroom.txt'
if (-not (Test-Path $wr)) {
    $wl = @()
    if ((Test-Path $SettingsFrom) -and ((Resolve-Path $SettingsFrom).Path -ne $wr)) { $wl = @([System.IO.File]::ReadAllLines($SettingsFrom, $utf8)) }
    $logs = Join-Path $env:LOCALAPPDATA ('BannerlordAgents\logs\warroom-' + $Realm)
    $set = $false
    for ($i = 0; $i -lt $wl.Count; $i++) {
        if ($wl[$i] -match '^\s*log_dir\s*=') { $wl[$i] = "log_dir = $logs"; $set = $true }
        if ($wl[$i] -match '^\s*channel\s*=') { $wl[$i] = "channel = $channelName" }
    }
    if (-not $set) { $wl += "log_dir = $logs" }
    [System.IO.File]::WriteAllLines($wr, [string[]]$wl, $utf8)
    Write-Host "War room settings: $wr (from $SettingsFrom; logs in $logs)" -ForegroundColor Green
} else { Write-Host "Kept the folder's own warroom.txt." }

# the order library, from the same code the bridge uses (the smoke test can write it without the game)
$smoke = Join-Path $repo 'tests\SmokeTest\bin\Release\SmokeTest.exe'
if (Test-Path $smoke) { & $smoke --library (Join-Path $channel 'tools.txt') | Out-Host }
else { Write-Host 'Build the solution first (dotnet build -c Release) so the order library can be written; the bridge writes it at the next load anyway.' -ForegroundColor Yellow }

$open = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+|TaleWorlds\.MountAndBlade\.Launcher\w*)$' }
if ($open) {
    Write-Host "The game is open, so its settings were not touched: close it, run this again, and the bridge takes the agent at the next load." -ForegroundColor Yellow
    exit 0
}

# the bridge's list of agents (the first time, the agents of the 0.8 settings come first, in their order)
$settings = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\settings.txt'
New-Item -ItemType Directory -Force (Split-Path $settings) | Out-Null
$lines = @()
if (Test-Path $settings) { $lines = @([System.IO.File]::ReadAllLines($settings, $utf8)) }
$list = $null
foreach ($l in $lines) { if ($l -match '^\s*agents\s*=\s*(.*)$') { $list = @($Matches[1] -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) } }
if ($null -eq $list -or $list.Count -eq 0) {
    $list = @()
    foreach ($l in $lines) { if ($l -match '^\s*agent_dir\s*=\s*(.+)$') { $list += (Split-Path -Parent $Matches[1].Trim()) } }
    foreach ($l in $lines) { if ($l -match '^\s*agent_dir\.[^=\s]+\s*=\s*(.+)$') { $list += (Split-Path -Parent $Matches[1].Trim()) } }
    $list = @($list | Where-Object { $_ -and (Test-Path (Join-Path $_ 'agent.txt')) -or ($_ -eq $Workspace) } | Select-Object -Unique)
}
if (-not ($list | Where-Object { $_ -ieq $Workspace })) { $list += $Workspace }
$value = ($list -join '; ')
$found = $false
for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^\s*agents\s*=') { $lines[$i] = "agents = $value"; $found = $true } }
if (-not $found) {
    $lines = @('# the agents: their folders, each with its own agent.txt (realm, mode, lords); when set, watch, agent, agent_lords and agent_dir are not used', "agents = $value") + $lines
}
[System.IO.File]::WriteAllLines($settings, [string[]]$lines, $utf8)
Write-Host "Bridge settings: agents = $value ($settings)" -ForegroundColor Green
Write-Host 'Run scripts\agents.ps1 start to start every agent''s war room and the dashboard.'
