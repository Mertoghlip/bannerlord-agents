<#
.SYNOPSIS
  Builds Bannerlord Agents and installs it into the game's Modules folder.

.DESCRIPTION
  Refuses to do anything while Bannerlord or its launcher is open: game files are never changed while the
  game is running. The game folder comes from -GameDir, else GamePath.props, else the BANNERLORD_DIR
  environment variable, else Steam's default folder. Only Modules\BannerlordAgents is written; nothing
  else in the game is touched. The module stays switched off in the launcher until you tick it.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1
#>
param(
    [string]$GameDir = '',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$moduleId = 'BannerlordAgents'

function Find-GameDir {
    if ($GameDir) { return $GameDir }
    $props = Join-Path $repo 'GamePath.props'
    if (Test-Path $props) {
        $x = [xml](Get-Content -Raw $props)
        $d = $x.Project.PropertyGroup.BannerlordDir
        if ($d) { return [string]$d }
    }
    if ($env:BANNERLORD_DIR) { return $env:BANNERLORD_DIR }
    return 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
}

$game = Find-GameDir
if (-not (Test-Path (Join-Path $game 'bin\Win64_Shipping_Client\TaleWorlds.Library.dll'))) {
    Write-Host "Bannerlord was not found at '$game'. Set it in GamePath.props or pass -GameDir." -ForegroundColor Red
    exit 1
}

# Never touch game files while the game is open. Under BLSE the game itself runs as Bannerlord.BLSE.Launcher.
$open = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -match '^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+|TaleWorlds\.MountAndBlade\.Launcher\w*)$'
}
if ($open) {
    $names = ($open.ProcessName | Sort-Object -Unique) -join ', '
    Write-Host "Bannerlord is open ($names). Close the game first, then run this again." -ForegroundColor Yellow
    exit 2
}

if (-not $NoBuild) {
    dotnet build (Join-Path $repo "src\$moduleId\$moduleId.csproj") -c Release -nologo -v minimal
    if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed; nothing installed.' -ForegroundColor Red; exit $LASTEXITCODE }
}

$stage = Join-Path $repo "build\Modules\$moduleId"
if (-not (Test-Path (Join-Path $stage 'SubModule.xml'))) {
    Write-Host "Nothing staged at $stage. Build first." -ForegroundColor Red
    exit 1
}
$dest = Join-Path $game "Modules\$moduleId"

# Mirror the staged module into the game. robocopy exit codes 0-7 mean success.
robocopy $stage $dest /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Write-Host "Copy failed (robocopy code $LASTEXITCODE)." -ForegroundColor Red; exit 1 }
$global:LASTEXITCODE = 0

Write-Host "Installed $moduleId into $dest" -ForegroundColor Green
Write-Host 'It stays off in the launcher until you tick it. Tick it only in the experiment module list.'
