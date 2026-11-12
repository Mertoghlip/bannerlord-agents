<#
.SYNOPSIS
  Starts, stops or shows every agent's war room, and the dashboard, in one go.

.DESCRIPTION
  Every agent is a folder (docs/WARROOM.md): the bridge's settings.txt lists them ("agents = <folder>; <folder>"). This starts
  one war room per listed folder (WarRoom.exe run --dir <folder>) and the dashboard, each detached from this window and
  minimised, so closing a terminal never stops them; stop ends them all (and old war room or dashboard windows of
  scripts\warroom.ps1 and scripts\dashboard.ps1); status says what runs and each war room's last line.
  Since Nov 11 it also starts the archive keeper (Runs.exe archive --follow, docs/RUNS.md#the-archive): each test's calls, questions,
  conversations, memory and records kept in archive\(run) (test)\ in the records folder, brought up to date while the game runs.
  The war room, the dashboard and the archive keeper lock their own program files while they run: stop them before building, start them after.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\agents.ps1 start

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\agents.ps1 stop
#>
param(
    [ValidateSet('start', 'stop', 'restart', 'status')][string]$Action = 'status',
    [switch]$NoDashboard,
    [switch]$NoArchive
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$warroom = Join-Path $repo 'src\BannerlordAgents.WarRoom\bin\Release\net8.0-windows\WarRoom.exe'
$dashboard = Join-Path $repo 'src\BannerlordAgents.Dashboard\bin\Release\net8.0-windows\Dashboard.exe'
$runs = Join-Path $repo 'src\BannerlordAgents.Runs\bin\Release\net8.0\Runs.exe'
$utf8 = New-Object System.Text.UTF8Encoding $false

# the agents' folders: "agents" in settings.txt, else (0.8 and before) the folder above agent_dir and each agent_dir.<realm>
function Get-AgentFolders {
    $settings = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\settings.txt'
    if (-not (Test-Path $settings)) { return @() }
    $lines = [System.IO.File]::ReadAllLines($settings, $utf8)
    foreach ($l in $lines) {
        if ($l -match '^\s*agents\s*=\s*(.+)$') {
            $list = @($Matches[1] -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            if ($list.Count -gt 0) { return $list }
        }
    }
    $old = @()
    foreach ($l in $lines) { if ($l -match '^\s*agent_dir(\.[^=\s]+)?\s*=\s*(.+)$') { $old += (Split-Path -Parent $Matches[2].Trim()) } }
    return $old
}

# our war rooms and dashboards (by their program's path), and the old windows that ran them
function Get-Ours {
    Get-CimInstance Win32_Process | Where-Object {
        ($_.Name -eq 'WarRoom.exe' -and $_.ExecutablePath -eq $warroom) -or ($_.Name -eq 'Dashboard.exe' -and $_.ExecutablePath -eq $dashboard) -or
        ($_.Name -eq 'Runs.exe' -and $_.ExecutablePath -eq $runs -and $_.CommandLine -match 'archive\s+--follow') -or
        ($_.Name -match '^(powershell|pwsh)\.exe$' -and $_.CommandLine -match 'scripts\\(warroom|dashboard)\.ps1')
    }
}

function Start-Detached([string]$commandLine) {
    $si = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]7 }
    $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $commandLine; CurrentDirectory = $repo; ProcessStartupInformation = $si }
    if ($r.ReturnValue -ne 0) { throw "could not start: $commandLine (code $($r.ReturnValue))" }
    return $r.ProcessId
}

function Show-Status {
    $ours = @(Get-Ours)
    foreach ($folder in Get-AgentFolders) {
        $p = $ours | Where-Object { $_.Name -eq 'WarRoom.exe' -and $_.CommandLine -like "*--dir*$folder*" } | Select-Object -First 1
        $realm = '?'
        $agentFile = Join-Path $folder 'agent.txt'
        if (Test-Path $agentFile) { foreach ($l in [System.IO.File]::ReadAllLines($agentFile, $utf8)) { if ($l -match '^\s*realm\s*=\s*(.+)$') { $realm = $Matches[1].Trim() } } }
        $logs = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\logs\warroom'
        $wt = Join-Path $folder 'warroom.txt'
        if (Test-Path $wt) { foreach ($l in [System.IO.File]::ReadAllLines($wt, $utf8)) { if ($l -match '^\s*log_dir\s*=\s*(.+)$' -and $Matches[1].Trim()) { $logs = $Matches[1].Trim() } } }
        $log = Join-Path $logs ('warroom-' + (Get-Date -Format yyyyMMdd) + '.log')
        $last = if (Test-Path $log) { ([System.IO.File]::ReadAllLines($log, $utf8) | Select-Object -Last 1) } else { '(no log today)' }
        Write-Host ("{0,-20} {1,-8} {2}" -f $realm, $(if ($p) { "pid $($p.ProcessId)" } else { 'STOPPED' }), $folder)
        Write-Host ("{0,-29} {1}" -f '', $last)
    }
    $d = $ours | Where-Object { $_.Name -eq 'Dashboard.exe' } | Select-Object -First 1
    Write-Host ("{0,-20} {1,-8} {2}" -f 'dashboard', $(if ($d) { "pid $($d.ProcessId)" } else { 'STOPPED' }), 'http://localhost:7329/')
    $k = $ours | Where-Object { $_.Name -eq 'Runs.exe' } | Select-Object -First 1
    $archive = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\archive'
    $archiveLog = Join-Path $archive 'archive.log'
    $lastArchive = if (Test-Path $archiveLog) { ([System.IO.File]::ReadAllLines($archiveLog, $utf8) | Select-Object -Last 1) } else { '(no archive pass yet)' }
    Write-Host ("{0,-20} {1,-8} {2}" -f 'archive keeper', $(if ($k) { "pid $($k.ProcessId)" } else { 'STOPPED' }), $archive)
    Write-Host ("{0,-29} {1}" -f '', $lastArchive)
}

function Stop-All {
    $ours = @(Get-Ours)
    foreach ($p in $ours) { try { Stop-Process -Id $p.ProcessId -Force -Confirm:$false -ErrorAction Stop } catch { } }
    Start-Sleep -Milliseconds 800
    Write-Host "Stopped $($ours.Count) process(es)." -ForegroundColor Green
}

function Start-All {
    foreach ($exe in @($warroom) + $(if (-not $NoDashboard) { @($dashboard) } else { @() })) {
        if (-not (Test-Path $exe)) { Write-Host "Build first: $exe is missing (dotnet build -c Release)." -ForegroundColor Red; exit 1 }
    }
    $ours = @(Get-Ours)
    foreach ($folder in Get-AgentFolders) {
        if (-not (Test-Path (Join-Path $folder 'warroom-system.md'))) { Write-Host "Not started: $folder has no warroom-system.md (scripts\setup-agent.ps1 makes it)." -ForegroundColor Yellow; continue }
        if ($ours | Where-Object { $_.Name -eq 'WarRoom.exe' -and $_.CommandLine -like "*--dir*$folder*" }) { Write-Host "Already running: $folder"; continue }
        $id = Start-Detached ('"' + $warroom + '" run --dir "' + $folder + '"')
        Write-Host "War room started for $folder (pid $id)." -ForegroundColor Green
    }
    if (-not $NoDashboard) {
        if ($ours | Where-Object { $_.Name -eq 'Dashboard.exe' }) { Write-Host 'Dashboard already running.' }
        else { $id = Start-Detached ('"' + $dashboard + '" run --port 7329'); Write-Host "Dashboard started (pid $id): http://localhost:7329/" -ForegroundColor Green }
    }
    if (-not $NoArchive) {
        if (-not (Test-Path $runs)) { Write-Host "Archive keeper not started: $runs is missing (dotnet build -c Release)." -ForegroundColor Yellow }
        elseif ($ours | Where-Object { $_.Name -eq 'Runs.exe' }) { Write-Host 'Archive keeper already running.' }
        else { $id = Start-Detached ('"' + $runs + '" archive --follow'); Write-Host "Archive keeper started (pid $id)." -ForegroundColor Green }
    }
    Start-Sleep -Seconds 3
    Show-Status
}

switch ($Action) {
    'start' { Start-All }
    'stop' { Stop-All }
    'restart' { Stop-All; Start-All }
    'status' { Show-Status }
}
