<#
.SYNOPSIS
  Runs the freeze probe by hand: where is the game's main thread right now, in the engine and in the game's code?

.DESCRIPTION
  The Bannerlord Agents module runs the probe by itself when the game's thread stops moving for 20 seconds. Run this
  instead when the game hangs before the module is up (a loading screen), or to look at any moment. It finds the game's
  process, takes its oldest thread as the main thread, and writes freeze-(time).txt into the newest launch's log folder
  (%LOCALAPPDATA%\BannerlordAgents\logs\sessions\...). It only reads the game; it pauses one thread for microseconds at a
  time and reads the rest from a snapshot of the process.

  -TestSpin tries the probe on a deliberately stuck test program (tests\SmokeTest --spin) instead of the game.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\probe.ps1
#>
param([switch]$TestSpin)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

function Find-Probe {
    $props = Join-Path $repo 'GamePath.props'
    $game = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
    if (Test-Path $props) { $d = ([xml](Get-Content -Raw $props)).Project.PropertyGroup.BannerlordDir; if ($d) { $game = [string]$d } }
    foreach ($p in @((Join-Path $game 'Modules\BannerlordAgents\probe\FreezeProbe.exe'), (Join-Path $repo 'build\Modules\BannerlordAgents\probe\FreezeProbe.exe'))) {
        if (Test-Path $p) { return $p }
    }
    throw 'The freeze probe is not built. Run: dotnet build -c Release (in the repository folder).'
}

$probe = Find-Probe
$sessions = Join-Path $env:LOCALAPPDATA 'BannerlordAgents\logs\sessions'
$folder = if (Test-Path $sessions) { Get-ChildItem $sessions -Directory | Sort-Object Name | Select-Object -Last 1 -ExpandProperty FullName } else { $env:TEMP }
$out = Join-Path $folder ('freeze-' + (Get-Date -Format 'HHmmss') + '-by-hand.txt')

if ($TestSpin) {
    $exe = Join-Path $repo 'tests\SmokeTest\bin\Release\SmokeTest.exe'
    $p = Start-Process $exe -ArgumentList '--spin' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    $main = ($p.Threads | Sort-Object StartTime | Select-Object -First 1).Id
    & $probe --pid $p.Id --thread $main --out (Join-Path $env:TEMP 'freeze-test.txt') --why 'a test of the probe on a stuck test program' | Out-Host
    Stop-Process -Id $p.Id -Force
    exit 0
}

$game = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+)$' -and $_.ProcessName -notmatch 'Launcher$' -or $_.ProcessName -eq 'Bannerlord.BLSE.Launcher' } |
        Sort-Object WorkingSet64 -Descending | Select-Object -First 1
if (-not $game) { Write-Host 'Bannerlord is not running.' -ForegroundColor Yellow; exit 1 }
$main = ($game.Threads | Sort-Object StartTime | Select-Object -First 1).Id
Write-Host "Probing $($game.ProcessName) (pid $($game.Id)), main thread $main ..."
& $probe --pid $game.Id --thread $main --out $out --why 'run by hand (scripts\probe.ps1)' | Out-Host
Write-Host "Written: $out" -ForegroundColor Green
