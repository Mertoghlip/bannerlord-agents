<#
.SYNOPSIS
  Creates (or refreshes) an agent's own workspace, outside this repository, and points the bridge at it.

.DESCRIPTION
  The agent plays a watched realm through the agent channel, answered by the war room (WarRoom.exe, one fresh
  Claude Code call per poke; docs/WARROOM.md). It must not see this repository, so its workspace lives elsewhere
  (default C:\Users\karab\Projects\orc-war-council). This:
  - installs agent\warroom-system.template.md as warroom-system.md (the war room's instructions for the agent),
    agent\chronicler-system.template.md as chronicler-system.md (the chronicler's, who writes the story so far), and the
    older agent chat's agent\CLAUDE.template.md and settings.template.json (always refreshed; in the repository they keep
    template names so no session here ever loads them);
  - leaves the realm's memory alone: the war room keeps it per campaign in memory\<campaign>\ (docs/MEMORY.md);
  - creates the channel folder warroom\ (orders\done, lookups) and writes the order library into it (tools.txt), so the
    war room has it before the game's first session; the bridge rewrites it at every load;
  - sets the bridge's settings.txt: agent_dir = <workspace>\warroom, agent = <Mode>, agent_lords = <Lords>.
  Settings are only written while the game is closed; otherwise the matching console commands are printed.

  A second agent (since 0.8), for another realm: -Realm <kingdom id> with its own -Workspace. The templates are the same;
  only their three lines that name the realm (the title, the opening line and the goal's first line) are rewritten from
  -RealmTitle, -RealmLine and -Theatre. Its warroom.txt starts as a copy of the first agent's (-SettingsFrom), so both
  think alike, with its own log_dir (logs\warroom-<realm>). The bridge's settings gain the realm in watch and
  agent_dir.<realm> = <workspace>\warroom; agent_dir stays the first agent's.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1 -Mode live

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1 -Mode live -Realm dolguldur -Workspace C:\Users\karab\Projects\dolguldur-war-council -RealmTitle "Dol Guldur" -RealmLine "Dol Guldur, the fortress-realm of the Necromancer in southern Mirkwood" -Theatre "Mirkwood and the lands about it"
#>
param(
    [string]$Workspace = 'C:\Users\karab\Projects\orc-war-council',
    [ValidateSet('off', 'shadow', 'live')][string]$Mode = 'shadow',
    [string]$Lords = 'all',
    # a second agent's realm (empty: the first agent's, agent_dir)
    [string]$Realm = '',
    [string]$RealmTitle = '',
    [string]$RealmLine = '',
    [string]$Theatre = '',
    [string]$SettingsFrom = 'C:\Users\karab\Projects\orc-war-council\warroom.txt'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$template = Join-Path $repo 'agent'
if ((Resolve-Path $Workspace -ErrorAction SilentlyContinue) -and ((Resolve-Path $Workspace).Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase))) {
    Write-Host 'The workspace must be outside the repository, so the agent cannot read the code.' -ForegroundColor Red
    exit 1
}
$utf8 = New-Object System.Text.UTF8Encoding $false

$channel = Join-Path $Workspace 'warroom'
New-Item -ItemType Directory -Force (Join-Path $channel 'orders\done') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $channel 'lookups') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $Workspace '.claude') | Out-Null
Copy-Item (Join-Path $template 'warroom-system.template.md') (Join-Path $Workspace 'warroom-system.md') -Force
Copy-Item (Join-Path $template 'chronicler-system.template.md') (Join-Path $Workspace 'chronicler-system.md') -Force
Copy-Item (Join-Path $template 'CLAUDE.template.md') (Join-Path $Workspace 'CLAUDE.md') -Force
Copy-Item (Join-Path $template 'settings.template.json') (Join-Path $Workspace '.claude\settings.json') -Force
# the realm's memory (notes, story so far) is made per campaign by the war room, in memory\<campaign>\ (docs/MEMORY.md)

# a second agent's realm: the three lines that name the realm, nothing else (read and written as UTF-8, never through Get-Content)
if ($Realm -and $RealmTitle) {
    foreach ($f in @('warroom-system.md', 'CLAUDE.md')) {
        $p = Join-Path $Workspace $f
        $text = [System.IO.File]::ReadAllText($p, $utf8)
        $text = [regex]::Replace($text, '(?m)^# War council of .*$', '# War council of ' + $RealmTitle)
        if ($RealmLine) { $text = [regex]::Replace($text, '(You are the war council of |You command )[^\r\n]*?, in a live campaign', { param($m) $m.Groups[1].Value + $RealmLine + ', in a live campaign' }) }
        if ($Theatre) { $text = $text.Replace('Win the war for the mountains:', 'Win the war for ' + $Theatre + ':') }
        [System.IO.File]::WriteAllText($p, $text, $utf8)
    }
    Write-Host "Instructions written for $RealmTitle ($Realm)." -ForegroundColor Green
}

# a second agent's war room: the first agent's settings, with logs of its own
if ($Realm) {
    $wr = Join-Path $Workspace 'warroom.txt'
    $logs = Join-Path $env:LOCALAPPDATA ('BannerlordAgents\logs\warroom-' + $Realm)
    $wl = @()
    if (Test-Path $wr) { $wl = @([System.IO.File]::ReadAllLines($wr, $utf8)) }
    elseif (Test-Path $SettingsFrom) { $wl = @([System.IO.File]::ReadAllLines($SettingsFrom, $utf8)) }
    $set = $false
    for ($i = 0; $i -lt $wl.Count; $i++) { if ($wl[$i] -match '^\s*log_dir\s*=') { $wl[$i] = "log_dir = $logs"; $set = $true } }
    if (-not $set) { $wl += "log_dir = $logs" }
    [System.IO.File]::WriteAllLines($wr, [string[]]$wl, $utf8)
    Write-Host "War room settings: $wr (logs in $logs)" -ForegroundColor Green
}

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
    if ($Realm) {
        Write-Host "  (close the game first: a second realm's channel, agent_dir.$Realm, is set in settings.txt, and two realms are set up at a load)"
    } else {
        Write-Host "  agents.agent dir $channel"
    }
    Write-Host "  agents.agent $Mode $(if ($Mode -eq 'live' -and $Lords -ne 'all') { $Lords })"
    exit 0
}

$settings = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\settings.txt'
New-Item -ItemType Directory -Force (Split-Path $settings) | Out-Null
$lines = @()
if (Test-Path $settings) { $lines = @([System.IO.File]::ReadAllLines($settings, $utf8)) }
function Get-Key([string]$key) {
    foreach ($l in $script:lines) { if ($l -match ('^\s*' + [regex]::Escape($key) + '\s*=\s*(.*)$')) { return $Matches[1].Trim() } }
    return $null
}
function Set-Key([string]$key, [string]$value) {
    $found = $false
    for ($i = 0; $i -lt $script:lines.Count; $i++) {
        if ($script:lines[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { $script:lines[$i] = "$key = $value"; $found = $true }
    }
    if (-not $found) { $script:lines += "$key = $value" }
}
Set-Key 'agent' $Mode
Set-Key 'agent_lords' $Lords
if ($Realm) {
    $watch = @((Get-Key 'watch') -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($watch.Count -eq 0) { $watch = @('mistymountainorcs') }
    if (-not ($watch | Where-Object { $_ -ieq $Realm })) { $watch += $Realm }
    Set-Key 'watch' ($watch -join ', ')
    Set-Key ('agent_dir.' + $Realm) $channel
    Write-Host "Bridge settings: watch = $($watch -join ', '), agent_dir.$Realm = $channel, agent = $Mode ($settings)" -ForegroundColor Green
} else {
    Set-Key 'agent_dir' $channel
    Write-Host "Bridge settings: agent = $Mode, agent_lords = $Lords, agent_dir = $channel ($settings)" -ForegroundColor Green
}
[System.IO.File]::WriteAllLines($settings, [string[]]$lines, $utf8)
