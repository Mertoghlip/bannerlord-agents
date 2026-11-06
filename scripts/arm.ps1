<#
.SYNOPSIS
  Sets the agents up for one of the test runs' arms (docs/RUNS.md), and says what to do in the game.

.DESCRIPTION
  Every agent stays listed in the bridge's settings; an arm only switches their modes, in each folder's agent.txt, which the
  bridge reads at every load (so no restart is needed: load TEST START after this):
    A   you against the agents:                 the rival realms live, your lieutenant live, the kingdom's agent off
    B   you against the game's AI:              the rival realms shadow (they answer, give no order), lieutenant live, kingdom off
    C1  an agent ruling your kingdom vs agents: the rival realms live, the kingdom's agent live, lieutenant off
    C2  an agent ruling your kingdom vs AI:     the rival realms shadow, the kingdom's agent live, lieutenant off
    C0  the control, every realm by the game's AI: the rival realms shadow, the kingdom's agent off (shadow with
        -KingdomWatches), lieutenant off; you switched to a wanderer as in C1 and C2
  The rival realms are the Misty Mountain Orcs, Dol Guldur, Rhun and Gundabad (since 0.15).
  -RivalsOff switches the rivals' agents fully off in B, C2 and C0 instead of shadow (Oct 23: they stay on
  so long as usage is fine).
  Each change is logged in %LOCALAPPDATA%\BannerlordAgents\runs\arms.log, and the run itself records every agent's mode.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\arm.ps1 -Arm A
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('A', 'B', 'C1', 'C2', 'C0', 'show')][string]$Arm,
    [switch]$RivalsOff,
    [switch]$KingdomWatches,
    [string]$Orcs = 'C:\Users\karab\Projects\orc-war-council',
    [string]$DolGuldur = 'C:\Users\karab\Projects\dolguldur-war-council',
    [string]$Rhun = 'C:\Users\karab\Projects\rhun-war-council',
    [string]$Gundabad = 'C:\Users\karab\Projects\gundabad-war-council',
    [string]$Lieutenant = 'C:\Users\karab\Projects\lieutenant',
    [string]$Kingdom = 'C:\Users\karab\Projects\kingdom-war-council'
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false

function Get-Mode([string]$folder) {
    $f = Join-Path $folder 'agent.txt'
    if (-not (Test-Path $f)) { return '(no agent.txt)' }
    foreach ($l in [System.IO.File]::ReadAllLines($f, $utf8)) { if ($l -match '^\s*mode\s*=\s*(\S+)') { return $Matches[1] } }
    return '(not set)'
}

function Set-Mode([string]$folder, [string]$mode) {
    $f = Join-Path $folder 'agent.txt'
    if (-not (Test-Path $f)) { throw "No agent.txt in $folder" }
    $text = [System.IO.File]::ReadAllText($f, $utf8)
    $new = [regex]::Replace($text, '(?m)^(\s*mode\s*=\s*)\S+', '${1}' + $mode)
    if ($new -ne $text) { [System.IO.File]::WriteAllText($f, $new, $utf8) }
}

$folders = [ordered]@{ 'Misty Mountain Orcs' = $Orcs; 'Dol Guldur' = $DolGuldur; 'Rhun' = $Rhun; 'Gundabad' = $Gundabad; 'your lieutenant' = $Lieutenant; 'the kingdom''s agent' = $Kingdom }
if ($Arm -eq 'show') {
    foreach ($k in $folders.Keys) { "{0,-22} {1}" -f $k, (Get-Mode $folders[$k]) }
    exit 0
}

$rival = if ($Arm -in 'A', 'C1') { 'live' } elseif ($RivalsOff) { 'off' } else { 'shadow' }
$modes = [ordered]@{
    'Misty Mountain Orcs' = $rival
    'Dol Guldur'          = $rival
    'Rhun'                = $rival
    'Gundabad'            = $rival
    'your lieutenant'     = $(if ($Arm -in 'A', 'B') { 'live' } else { 'off' })
    'the kingdom''s agent' = $(if ($Arm -in 'C1', 'C2') { 'live' } elseif ($Arm -eq 'C0' -and $KingdomWatches) { 'shadow' } else { 'off' })
}
$log = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\runs\arms.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
$lines = @()
foreach ($k in $modes.Keys) {
    $was = Get-Mode $folders[$k]
    Set-Mode $folders[$k] $modes[$k]
    $lines += ("{0}: {1} -> {2}" -f $k, $was, $modes[$k])
}
[System.IO.File]::AppendAllText($log, (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "  arm $Arm" + $(if ($RivalsOff) { ' (rivals off)' } else { '' }) + ": " + ($lines -join '; ') + "`r`n", $utf8)
Write-Host "Arm $Arm set:" -ForegroundColor Green
$lines | ForEach-Object { "  $_" }
Write-Host ''
Write-Host 'In the game:' -ForegroundColor Cyan
"  1. Load TEST START. The message log says 'Test run ready'; the run begins on the first in-game hour the clock runs."
switch ($Arm) {
    { $_ -in 'A', 'B' } {
        "  2. Play as you always do. Enter opens your lieutenant. Type agents.run note <text> in the console (Alt + ~) for anything worth remembering."
    }
    { $_ -in 'C1', 'C2', 'C0' } {
        "  2. Before the clock runs, in the console (Alt + ~): agents.run playerless shows the wanderer you would become,"
        "     agents.run playerless go makes the switch (releases the lords holding Party AI Controls orders, 100,000 denars, 100 grain)."
        "  3. In the town menu choose to wait there."
        "  4. agents.run check should say $Arm, with the kingdom commanded by " + $(if ($Arm -eq 'C0') { "the game's AI" } else { "its agent" }) + ". Then set the clock running and leave it."
    }
}
"  At day 60 the game pauses itself and offers a save named 'TEST END <arm> <run>'."
