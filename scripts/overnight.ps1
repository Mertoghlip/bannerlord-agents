<#
.SYNOPSIS
  Runs the playerless tests one after another with the autopilot (docs/AUTOPILOT.md), each from the playerless test start, nobody at the game.

.DESCRIPTION
  Nov 24: I asked for all the playerless tests to run overnight from the Dol Guldur - Gundabad merge start, 200 in-game days each, the control
  (every realm by the game's AI) at double speed. For each arm in -Arms, in order:
    1. the run's length (run_days) and the map's pace (map_speed: -ControlMapSpeed for C0, else as it was) in settings.txt;
    2. the arm (arm.ps1; the rivals' agents fully off in C2 and C0), then the war rooms restarted, since each reads warroom.txt only when it starts,
       and the war rooms the arm does not use stopped (the rivals' in C2 and C0, the kingdom's in C0);
    3. autopilot.ps1 -Arm <arm>: it loads the test start, makes the run and minds it to its end (restarts, usage-limit pauses, the TEST END save).
  The chain goes on only when a run finished (the autopilot's code 0); any other ending stops it and says why. stop.txt in the autopilot folder
  stops the run under way (it saves and closes the game) and the chain with it. Everything is logged in runs\overnight.log. At the end
  map_speed and run_days are put back as they were.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\overnight.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\overnight.ps1 -Arms C0 -Days 30
#>
param(
    [string[]]$Arms = @('C2', 'C1', 'C0'),
    [int]$Days = 200,
    [string]$ControlMapSpeed = '1'
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
try { [Console]::OutputEncoding = $utf8 } catch { }
$here = $PSScriptRoot
$Root = Join-Path $env:LOCALAPPDATA 'BannerlordAgents'
$SettingsFile = Join-Path $Root 'settings.txt'
$StopFile = Join-Path $Root 'autopilot\stop.txt'
$Log = Join-Path $Root 'runs\overnight.log'
$Orcs = 'C:\Users\karab\Projects\orc-war-council'
$DolGuldur = 'C:\Users\karab\Projects\dolguldur-war-council'
$Kingdom = 'C:\Users\karab\Projects\kingdom-war-council'
$Lieutenant = 'C:\Users\karab\Projects\lieutenant'

function Say([string]$text) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $text
    [Console]::Out.WriteLine($line)
    [System.IO.File]::AppendAllText($Log, $line + "`r`n", $utf8)
}
function Get-Setting([string]$key) {
    foreach ($l in [System.IO.File]::ReadAllLines($SettingsFile, $utf8)) { if ($l -match ('^\s*' + [regex]::Escape($key) + '\s*=\s*(.*)$')) { return $Matches[1].Trim() } }
    return $null
}
function Set-Setting([string]$key, [string]$value) {
    $lines = [System.Collections.Generic.List[string]]::new([string[]][System.IO.File]::ReadAllLines($SettingsFile, $utf8))
    $hit = $false
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { $lines[$i] = "$key = $value"; $hit = $true } }
    if (-not $hit) { $lines.Add("$key = $value") }
    [System.IO.File]::WriteAllLines($SettingsFile, $lines.ToArray(), $utf8)
}
function Stop-WarRoom([string]$folder) {
    $procs = @(Get-CimInstance Win32_Process | Where-Object { ($_.Name -eq 'WarRoom.exe' -or $_.Name -match '^(powershell|pwsh)\.exe$') -and $_.CommandLine -and $_.CommandLine -like "*$folder*" -and $_.CommandLine -notlike '*overnight.ps1*' -and $_.CommandLine -notlike '*autopilot.ps1*' })
    foreach ($p in $procs) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop } catch { } }
    if ($procs.Count -gt 0) { Say "  war room stopped (not used in this arm): $folder" }
}

$speed0 = Get-Setting 'map_speed'
$days0 = Get-Setting 'run_days'
Say "overnight.ps1: arms $($Arms -join ', '), $Days in-game days each, the control at map_speed $ControlMapSpeed (map_speed was $speed0, run_days $days0)"
$code = 0
try {
    Set-Setting 'run_days' ([string]$Days)
    foreach ($arm in $Arms) {
        if (Test-Path $StopFile) { Say 'stop.txt found before the next run: the chain stops.'; Remove-Item $StopFile -Force; $code = 4; break }
        Set-Setting 'map_speed' $(if ($arm -eq 'C0') { $ControlMapSpeed } else { $speed0 })
        $rivalsOff = $arm -in 'C2', 'C0'
        $armArgs = @('-Arm', $arm); if ($rivalsOff) { $armArgs += '-RivalsOff' }
        Say "=== $arm : map_speed $(Get-Setting 'map_speed'), run_days $(Get-Setting 'run_days')"
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'arm.ps1') @armArgs | ForEach-Object { Say "  arm: $_" }
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'agents.ps1') restart | ForEach-Object { Say "  agents: $_" }
        if ($rivalsOff) { Stop-WarRoom $Orcs; Stop-WarRoom $DolGuldur }
        if ($arm -eq 'C0') { Stop-WarRoom $Kingdom }
        Stop-WarRoom $Lieutenant
        Say "  autopilot.ps1 -Arm $arm$(if ($rivalsOff) { ' -RivalsOff' }) starts"
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'autopilot.ps1') @armArgs | ForEach-Object { Say "  | $_" }
        $code = $LASTEXITCODE
        Say "=== $arm ended: autopilot code $code ($(switch ($code) { 0 { 'done' } 2 { 'could not set up' } 3 { 'held: a crash or something it will not decide alone' } 4 { 'stopped by stop.txt' } default { 'unknown' } }))"
        if ($code -ne 0) { Say 'The chain stops here.'; break }
    }
}
catch { Say "STOPPED: $($_.Exception.Message)"; $code = 2 }
finally {
    if ($null -ne $speed0) { Set-Setting 'map_speed' $speed0 }
    if ($null -ne $days0) { Set-Setting 'run_days' $days0 }
    Say "map_speed $speed0 and run_days $days0 put back. overnight.ps1 ends with code $code."
}
exit $code
