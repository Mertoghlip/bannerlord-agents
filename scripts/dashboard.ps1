<#
.SYNOPSIS
  Starts the dashboard: the live map of the game in your browser, what each agent sees, the planned ways and the crossings,
  and the agent's moves beside it (docs/DASHBOARD.md).

.DESCRIPTION
  Builds it if needed, then runs Dashboard.exe in this window until Ctrl+C and opens http://localhost:7329/ (this laptop
  only). It only reads the files the game's mod and the war room write, so it can be started and closed at any time, with
  the game running or not. Its log is in %LOCALAPPDATA%\BannerlordAgents\logs\dashboard.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\dashboard.ps1
#>
param(
    [int]$Port = 7329,
    [switch]$NoBrowser
)
$ErrorActionPreference = 'Stop'
try { $Host.UI.RawUI.WindowTitle = 'Dashboard' } catch { }
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\BannerlordAgents.Dashboard\bin\Release\net8.0-windows\Dashboard.exe'
if (-not (Test-Path $exe)) {
    dotnet build (Join-Path $repo 'src\BannerlordAgents.Dashboard\BannerlordAgents.Dashboard.csproj') -c Release -nologo -v minimal
    dotnet build-server shutdown | Out-Null
}
$arguments = @('run', '--port', $Port)
if (-not $NoBrowser) { $arguments += '--open' }
& $exe @arguments
