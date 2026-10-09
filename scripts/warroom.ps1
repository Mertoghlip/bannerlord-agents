<#
.SYNOPSIS
  Starts the war room: it answers the agent's pokes while you play, one fresh Claude Code call each (docs/WARROOM.md).

.DESCRIPTION
  Builds it if needed, then runs WarRoom.exe in this window until Ctrl+C. With no pokes coming (the game closed or
  paused), it waits and uses nothing. Its settings are in <workspace>\warroom.txt (model, effort, thinking caps); every
  call is logged in %LOCALAPPDATA%\BannerlordAgents\logs\warroom.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\warroom.ps1
#>
param(
    [string]$Workspace = 'C:\Users\karab\Projects\orc-war-council',
    [ValidateSet('run', 'once')][string]$Mode = 'run'
)
$ErrorActionPreference = 'Stop'
try { $Host.UI.RawUI.WindowTitle = 'War room' } catch { }
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\BannerlordAgents.WarRoom\bin\Release\net8.0-windows\WarRoom.exe'
if (-not (Test-Path $exe)) {
    dotnet build (Join-Path $repo 'src\BannerlordAgents.WarRoom\BannerlordAgents.WarRoom.csproj') -c Release -nologo -v minimal
    dotnet build-server shutdown | Out-Null
}
& $exe $Mode --dir $Workspace
