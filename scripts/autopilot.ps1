<#
.SYNOPSIS
  Runs a playerless test with nobody at the game (docs/AUTOPILOT.md): starts the game, loads the save, keeps it waiting at normal speed, saves, restarts,
  pauses for the usage limit, gathers the files of a crash, and quits at the run's end.

.DESCRIPTION
  Nov 22: I want the capability for you to launch the game, launch the test start save, switch my character to a wanderer and move into a safe settlement,
  start waiting there at normal speed, create the save for the playerless save, and do everything else needed (pausing, saving to the test save, quitting)
  so I can run those tests fully autonomously. Nov 23: C1 and C2 first; normal speed; on a crash, note it, gather all the files needed for a fix and wait
  for my answer; on the usage limit, pause and wait for the reset; unknown pop-ups answered no / cancel.

  The game side is the module's autopilot (src\BannerlordAgents\Autopilot.cs), driven through %LOCALAPPDATA%\BannerlordAgents\autopilot:
  in\*.txt (one command each), out\*.json (how each ended), status.json (every two seconds), events.jsonl, on.json (the keepers' switch, naming the game's
  process). This script never touches the game's files.

  Three jobs:
    -MakePlayerless   once: loads -From (TEST START DGG MERGE), makes the playerless switch with the clock stopped, saves it as -Start, quits, and adds
                      -Start to run_save so loading it readies a run. Every agent is switched off for it and switched back after (no model calls).
    -Arm C1|C2        (-RivalsOff: the rivals' agents off, not shadow, in C2) a run: sets the arm, loads -Start, checks the run would be that arm, puts the stand-in to waiting at normal speed (the run begins),
                      and minds it to day 60: the run's own save every in-game day (TEST AUTO <arm>), a restart at -MemoryRestartMb, a pause on the
                      usage limit until it resets, a restart after a crash or freeze (twice at most), the TEST END save, the quit.
    -DryRun           proves the machinery without readying a run (run_save set aside for it, every agent off): loads -Start, the pop-up self-test,
                      waiting at normal speed for -DryMinutes, pause and resume, a move of the stand-in, a save, a restart from that save, the quit.

  Exit codes: 0 done; 2 could not set up (nothing started, or stopped before the run began); 3 held: a crash that waits for my answer, or something
  the script will not decide alone (its files are in D:\BannerlordAgents\crashes); 4 stopped by stop.txt.
  To stop it cleanly: create %LOCALAPPDATA%\BannerlordAgents\autopilot\stop.txt (it saves the run, quits the game and ends).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\autopilot.ps1 -MakePlayerless
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\autopilot.ps1 -DryRun
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\autopilot.ps1 -Arm C1
#>
param(
    [ValidateSet('C1', 'C2')][string]$Arm,
    # Nov 24: the rival realms' agents fully off for a C2 run, not shadow (their continuous conversations stay as the arm A run left them)
    [switch]$RivalsOff,
    [switch]$MakePlayerless,
    [switch]$DryRun,
    [string]$From = 'TEST START DGG MERGE',
    [string]$Start = 'TEST START DGG MERGE PLAYERLESS',
    [int]$MemoryRestartMb = 20000,
    [int]$SaveHours = 24,
    [int]$DryMinutes = 4,
    [int]$MaxRestarts = 2,
    [string]$CrashDir = 'D:\BannerlordAgents\crashes'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$utf8 = New-Object System.Text.UTF8Encoding $false
try { [Console]::OutputEncoding = $utf8 } catch { }   # town names like Damudur (with its accent)

$jobs = @(@($Arm, $(if ($MakePlayerless) { 'make' }), $(if ($DryRun) { 'dry' })) | Where-Object { $_ })
$script:GamePid = 0
$script:LaunchedAt = Get-Date
$script:copiedList = @()
if ($jobs.Count -ne 1) { [Console]::Out.WriteLine('Choose one: -MakePlayerless, -DryRun or -Arm C1|C2.'); exit 2 }

# ---------------------------------------------------------------- places

$Game = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
$Bin = Join-Path $Game 'bin\Win64_Shipping_Client'
$Exe = Join-Path $Bin 'Bannerlord.BLSE.Standalone.exe'
$Root = Join-Path $env:LOCALAPPDATA 'BannerlordAgents'
$Ap = Join-Path $Root 'autopilot'
$InDir = Join-Path $Ap 'in'
$OutDir = Join-Path $Ap 'out'
$StatusFile = Join-Path $Ap 'status.json'
$PlanFile = Join-Path $Ap 'on.json'
$StopFile = Join-Path $Ap 'stop.txt'
$LogFile = Join-Path $Ap 'script.log'
$SettingsFile = Join-Path $Root 'settings.txt'
$Docs = [Environment]::GetFolderPath('MyDocuments')
$Configs = Join-Path $Docs 'Mount and Blade II Bannerlord\Configs'
$Saves = Join-Path $Docs 'Mount and Blade II Bannerlord\Game Saves'
$RglLogs = 'C:\ProgramData\Mount and Blade II Bannerlord\logs'
foreach ($d in $Ap, $InDir, $OutDir) { New-Item -ItemType Directory -Force $d | Out-Null }
if (Test-Path $StopFile) { Remove-Item $StopFile -Force }

function Say([string]$text) {
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $text
    # straight to the console, never into the pipeline: a function's output is its answer, and a line written there would become part of it
    [Console]::Out.WriteLine($line)
    [Console]::Out.Flush()
    [System.IO.File]::AppendAllText($LogFile, $line + "`r`n", $utf8)
}

# ---------------------------------------------------------------- keeping the laptop awake while this runs (nothing changed for good)

Add-Type -Namespace Autopilot -Name Power -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
'@
function Stay-Awake { [void][Autopilot.Power]::SetThreadExecutionState([uint32]2147483649) }   # ES_CONTINUOUS | ES_SYSTEM_REQUIRED
function Let-Sleep { [void][Autopilot.Power]::SetThreadExecutionState([uint32]2147483648) }

# ---------------------------------------------------------------- settings and agents

function Read-Settings { [System.IO.File]::ReadAllLines($SettingsFile, $utf8) }
function Get-Setting([string]$key) {
    foreach ($l in Read-Settings) { if ($l -match ('^\s*' + [regex]::Escape($key) + '\s*=\s*(.*)$')) { return $Matches[1].Trim() } }
    return ''
}
function Set-Setting([string]$key, [string]$value) {
    $lines = Read-Settings
    $done = $false
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { $lines[$i] = "$key = $value"; $done = $true } }
    if (-not $done) { $lines += "$key = $value" }
    [System.IO.File]::WriteAllLines($SettingsFile, $lines, $utf8)
}
function Agent-Folders { (Get-Setting 'agents') -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
function Get-Modes {
    $m = [ordered]@{}
    foreach ($f in Agent-Folders) {
        $file = Join-Path $f 'agent.txt'
        if (-not (Test-Path $file)) { continue }
        foreach ($l in [System.IO.File]::ReadAllLines($file, $utf8)) { if ($l -match '^\s*mode\s*=\s*(\S+)') { $m[$f] = $Matches[1] } }
    }
    return $m
}
function Set-Modes($modes) {
    foreach ($f in @($modes.Keys)) {
        $file = Join-Path $f 'agent.txt'
        $text = [System.IO.File]::ReadAllText($file, $utf8)
        $new = [regex]::Replace($text, '(?m)^(\s*mode\s*=\s*)\S+', '${1}' + $modes[$f])
        if ($new -ne $text) { [System.IO.File]::WriteAllText($file, $new, $utf8) }
    }
}
function All-Off { $m = Get-Modes; $off = [ordered]@{}; foreach ($k in @($m.Keys)) { $off[$k] = 'off' }; Set-Modes $off }
function Run-Saves { (Get-Setting 'run_save') -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
function Save-Exists([string]$name) { Test-Path -LiteralPath (Join-Path $Saves ($name + '.sav')) }

# ---------------------------------------------------------------- the game

function Game-Process { Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+|TaleWorlds\.MountAndBlade\.Launcher\w*)$' } }

function Launch-Args {
    $xml = [xml][System.IO.File]::ReadAllText((Join-Path $Configs 'LauncherData.xml'), $utf8)
    $ids = @($xml.UserData.SingleplayerData.ModDatas.UserModData | Where-Object { $_.IsSelected -eq 'true' } | ForEach-Object { $_.Id })
    if ($ids.Count -lt 5) { throw "LauncherData.xml lists only $($ids.Count) modules for single player" }
    # as the launcher starts it (the last rgl log's "Command Args"), duplicates and no_watchdog included
    return '/singleplayer _MODULES_*' + ($ids -join '*') + '*_MODULES_ no_watchdog'
}

function Start-Game {
    if (Game-Process) { throw 'The game is already running.' }
    if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
        Say 'Steam is not running: starting it.'
        Start-Process 'C:\Program Files (x86)\Steam\steam.exe' -ArgumentList '-silent'
        Start-Sleep -Seconds 60
    }
    foreach ($f in @(Get-ChildItem $OutDir -Filter *.json -ErrorAction SilentlyContinue)) { Remove-Item $f.FullName -Force }
    foreach ($f in @(Get-ChildItem $InDir -Filter *.txt -ErrorAction SilentlyContinue)) { Remove-Item $f.FullName -Force }
    if (Test-Path $StatusFile) { Remove-Item $StatusFile -Force }
    $p = Start-Process -FilePath $Exe -ArgumentList (Launch-Args) -WorkingDirectory $Bin -PassThru
    $script:GamePid = $p.Id
    $script:LaunchedAt = Get-Date
    Say "Game started (process $($p.Id))."
    [void](Wait-Status { param($s) (Prop $s 'state') -eq 'main menu' } 600 'the main menu')
    Say "At the main menu after $([int]((Get-Date) - $script:LaunchedAt).TotalSeconds) s."
}

function Read-Status {
    for ($i = 0; $i -lt 5; $i++) {
        try { if (Test-Path $StatusFile) { return ([System.IO.File]::ReadAllText($StatusFile, $utf8) | ConvertFrom-Json) } else { return $null } }
        catch { Start-Sleep -Milliseconds 200 }
    }
    return $null
}
function Status-Age { if (-not (Test-Path $StatusFile)) { return 99999 } ; ((Get-Date) - (Get-Item $StatusFile).LastWriteTime).TotalSeconds }
function Prop($o, [string]$name) { if ($null -eq $o) { return $null }; $p = $o.PSObject.Properties[$name]; if ($p) { return $p.Value } else { return $null } }

function Game-Alive { $script:GamePid -and (Get-Process -Id $script:GamePid -ErrorAction SilentlyContinue) }

function Wait-Status([scriptblock]$test, [int]$seconds, [string]$what) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        if (-not (Game-Alive)) { throw "The game closed while waiting for $what." }
        $s = Read-Status
        if ($s -and (Prop $s 'pid') -eq $script:GamePid -and (& $test $s)) { return $s }
        Start-Sleep -Seconds 2
    }
    throw "No $what after $seconds s."
}

$script:Seq = 0
function Send([string]$line, [int]$seconds = 120) {
    $script:Seq++
    $name = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $script:Seq.ToString('0000')
    $tmp = Join-Path $InDir ($name + '.tmp')
    [System.IO.File]::WriteAllText($tmp, $line, $utf8)
    Move-Item $tmp (Join-Path $InDir ($name + '.txt'))
    $out = Join-Path $OutDir ($name + '.json')
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        if (Test-Path $out) {
            Start-Sleep -Milliseconds 100
            $r = [System.IO.File]::ReadAllText($out, $utf8) | ConvertFrom-Json
            Say ("  {0} -> {1}" -f $line, (($r.answer -split "`n")[0]))
            return $r
        }
        if ($line -ne 'quit' -and -not (Game-Alive)) { throw "The game closed while carrying out: $line" }
        Start-Sleep -Milliseconds 500
    }
    throw "No answer to '$line' after $seconds s."
}
function Must([string]$line, [int]$seconds = 120) {
    $r = Send $line $seconds
    if (-not $r.ok) { throw "'$line' failed: $($r.answer)" }
    return $r
}

function Write-Plan([bool]$wait, [string]$saveName, [string]$note) {
    $plan = @{ pid = $script:GamePid; wait = $wait; speed = 'play'; save_name = $saveName; save_hours = $SaveHours; playerless = $true; note = $note }
    $tmp = $PlanFile + '.tmp'
    [System.IO.File]::WriteAllText($tmp, ($plan | ConvertTo-Json -Compress), $utf8)
    Move-Item $tmp $PlanFile -Force
}
function Clear-Plan { if (Test-Path $PlanFile) { Remove-Item $PlanFile -Force } }

function Quit-Game([int]$seconds = 240) {
    if (-not (Game-Alive)) { return }
    try { [void](Send 'quit' 300) } catch { Say "  the quit got no answer: $($_.Exception.Message)" }
    $until = (Get-Date).AddSeconds($seconds)
    while ((Game-Alive) -and (Get-Date) -lt $until) { Start-Sleep -Seconds 2 }
    if (Game-Alive) { Say '  the game did not close; ending it.'; Stop-Process -Id $script:GamePid -Force; Start-Sleep -Seconds 3 }
    # the game's own shutdown leaves a crash dump of its exit (Nov 20 and 22, about 90 MB each); that one is not a crash of the run
    Get-ChildItem (Join-Path $env:LOCALAPPDATA 'CrashDumps') -Filter ("*." + $script:GamePid + ".dmp") -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item $_.FullName -Force; Say "  removed the exit's dump $($_.Name)" }
    Say 'Game closed.'
}

# ---------------------------------------------------------------- crashes and freezes: note, gather, decide

function Gather([string]$what) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir = Join-Path $CrashDir ($stamp + '-' + $script:GamePid)
    New-Item -ItemType Directory -Force $dir | Out-Null
    $since = $script:LaunchedAt.AddMinutes(-1)
    $alive = [bool](Game-Alive)
    $title = ''
    if ($alive) { try { $title = (Get-Process -Id $script:GamePid).MainWindowTitle } catch { } }
    function Take($path, [string]$as) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            $to = Join-Path $dir $(if ($as) { $as } else { Split-Path $path -Leaf })
            Copy-Item -LiteralPath $path $to -Recurse -Force -ErrorAction SilentlyContinue
            $script:copiedList += $to
        }
    }
    $script:copiedList = @()
    foreach ($n in "rgl_log_$($script:GamePid).txt", "rgl_log_errors_$($script:GamePid).txt", "watchdog_log_$($script:GamePid).txt") { Take (Join-Path $RglLogs $n) $null }
    $crashes = 'C:\ProgramData\Mount and Blade II Bannerlord\crashes'
    if (Test-Path $crashes) { Get-ChildItem $crashes | Where-Object { $_.LastWriteTime -gt $since } | ForEach-Object { Take $_.FullName ('engine-crash-' + $_.Name) } }
    $taomLogs = Join-Path $Bin 'Logs'
    Get-ChildItem $taomLogs -Filter 'taom_crash_*.zip' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $since } | ForEach-Object {
        Take $_.FullName $null
        try { Expand-Archive -LiteralPath $_.FullName -DestinationPath (Join-Path $dir ($_.BaseName)) -Force } catch { }
    }
    Get-ChildItem $taomLogs -Filter 'taom_debug_*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Take $_.FullName $null }
    Take (Join-Path $env:LOCALAPPDATA 'TFAOMFixes\boot_log.txt') 'TFAOMFixes-boot_log.txt'
    Take (Join-Path $env:LOCALAPPDATA 'TFAOMFixes\boot_log_prev.txt') 'TFAOMFixes-boot_log_prev.txt'
    Take (Join-Path $Docs 'Mount and Blade II Bannerlord\TFAOMFixes\boot_log.txt') 'TFAOMFixes-boot_log-documents.txt'
    Get-ChildItem (Join-Path $env:LOCALAPPDATA 'CrashDumps') -Filter ("*." + $script:GamePid + ".dmp") -ErrorAction SilentlyContinue | ForEach-Object { Take $_.FullName $null }
    $session = Get-ChildItem (Join-Path $Root 'logs\sessions') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($session) { Take $session.FullName ('our-session-' + $session.Name) }
    Take (Join-Path $Root 'logs\bridge.log') 'our-bridge.log'
    foreach ($n in 'status.json', 'events.jsonl', 'script.log') { Take (Join-Path $Ap $n) ('autopilot-' + $n) }
    $runFile = Join-Path $Root 'runs\current.json'
    Take $runFile 'run-current.json'
    # the whole process while it is still up (a crash window open): the fix sessions name the dying soldier from it (HeapLists --stackagents)
    $dump = $null
    if ($alive) {
        $dump = Join-Path $dir ('Bannerlord-full-' + $script:GamePid + '.dmp')
        Say "  writing the whole game's memory to $dump (several minutes, about the game's size)..."
        try { Write-FullDump $script:GamePid $dump; Say "  full dump written ($([int]((Get-Item $dump).Length / 1MB)) MB)." }
        catch { Say "  the full dump failed: $($_.Exception.Message)"; $dump = $null }
    }
    # which crash: the engine fault the fixes session waits on (+0x6013cc, Agent.Die on a mount the rider's moves lack) or another
    $hits = @(Get-ChildItem $dir -Recurse -File -Include *.txt, *.log, *.json, *.jsonl, *.xml -ErrorAction SilentlyContinue | Select-String -Pattern '6013cc', 'BAD MEMORY ACCESS', 'AccessViolation', 'EXCEPTION' -SimpleMatch -ErrorAction SilentlyContinue | Select-Object -First 40)
    $known = [bool]($hits | Where-Object { $_.Line -match '6013cc' })
    $s = Read-Status
    $summary = @(
        "What: $what",
        "When: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'); game process $($script:GamePid), started $($script:LaunchedAt.ToString('yyyy-MM-dd HH:mm:ss')), still up: $alive, window: $title",
        "Last status: $(if ($s) { ($s | ConvertTo-Json -Compress) } else { 'none' })",
        "The +0x6013cc engine fault: $(if ($known) { 'YES' } else { 'not seen' })",
        "Full dump: $(if ($dump) { $dump } else { 'none (the game was gone)' })",
        '', 'Lines found:') + @($hits | ForEach-Object { '  ' + (Split-Path $_.Path -Leaf) + ':' + $_.LineNumber + ': ' + $_.Line.Trim() }) + @('', 'Files:') + @($script:copiedList | ForEach-Object { '  ' + $_ })
    [System.IO.File]::WriteAllLines((Join-Path $dir 'summary.txt'), [string[]]$summary, $utf8)
    Say "  files gathered in $dir (summary.txt)."
    return @{ Dir = $dir; Known = $known; Alive = $alive }
}

function Write-FullDump([int]$procId, [string]$path) {
    if (-not ('Autopilot.Dump' -as [type])) {
        Add-Type -Namespace Autopilot -Name Dump -MemberDefinition @'
[DllImport("dbghelp.dll", SetLastError = true)]
public static extern bool MiniDumpWriteDump(System.IntPtr hProcess, uint processId, Microsoft.Win32.SafeHandles.SafeFileHandle hFile, uint dumpType, System.IntPtr exceptionParam, System.IntPtr userStreamParam, System.IntPtr callbackParam);
'@
    }
    $p = Get-Process -Id $procId
    $fs = [System.IO.File]::Create($path)
    try {
        # full memory, handles, unloaded modules, memory info, thread info
        $ok = [Autopilot.Dump]::MiniDumpWriteDump($p.Handle, [uint32]$procId, $fs.SafeFileHandle, [uint32](0x2 -bor 0x4 -bor 0x20 -bor 0x800 -bor 0x1000), [IntPtr]::Zero, [IntPtr]::Zero, [IntPtr]::Zero)
        if (-not $ok) { throw "MiniDumpWriteDump failed ($([Runtime.InteropServices.Marshal]::GetLastWin32Error()))" }
    }
    finally { $fs.Dispose() }
}

# ---------------------------------------------------------------- the usage limit (the war rooms' calls.jsonl: "You've hit your session limit - resets 10:50pm (America/Los_Angeles)")

$script:CallOffsets = @{}
function New-CallLines {
    $lines = @()
    foreach ($f in @(Get-ChildItem (Join-Path $Root 'logs') -Directory -Filter 'warroom*' -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'calls.jsonl' } | Where-Object { Test-Path $_ })) {
        $len = (Get-Item $f).Length
        if (-not $script:CallOffsets.ContainsKey($f)) { $script:CallOffsets[$f] = $len; continue }
        $at = $script:CallOffsets[$f]
        if ($len -le $at) { $script:CallOffsets[$f] = $len; continue }
        $fs = [System.IO.File]::Open($f, 'Open', 'Read', 'ReadWrite')
        try { [void]$fs.Seek($at, 'Begin'); $sr = New-Object System.IO.StreamReader($fs, $utf8); $text = $sr.ReadToEnd() } finally { $fs.Dispose() }
        $script:CallOffsets[$f] = $len
        foreach ($l in ($text -split "`n")) { if ($l.Trim()) { $lines += $l } }
    }
    return $lines
}

$TzMap = @{ 'America/Los_Angeles' = 'Pacific Standard Time'; 'America/New_York' = 'Eastern Standard Time'; 'America/Chicago' = 'Central Standard Time'; 'America/Denver' = 'Mountain Standard Time';
            'Europe/Istanbul' = 'Turkey Standard Time'; 'Europe/London' = 'GMT Standard Time'; 'Europe/Berlin' = 'W. Europe Standard Time'; 'UTC' = 'UTC'; 'Etc/UTC' = 'UTC' }
function Reset-Time([string]$problem) {
    if ($problem -notmatch 'resets\s+(?<when>[^()]+?)\s*\((?<tz>[^)]+)\)') { return $null }
    $when = $Matches['when']; $tzName = $Matches['tz'].Trim()
    if (-not $TzMap.ContainsKey($tzName)) { return $null }
    $tz = [TimeZoneInfo]::FindSystemTimeZoneById($TzMap[$tzName])
    if ($when -notmatch '(?<h>\d{1,2})(:(?<m>\d{2}))?\s*(?<ap>am|pm)') { return $null }
    $h = [int]$Matches['h'] % 12; if ($Matches['ap'] -eq 'pm') { $h += 12 }
    $m = if ($Matches['m']) { [int]$Matches['m'] } else { 0 }
    $nowThere = [TimeZoneInfo]::ConvertTime((Get-Date), $tz)
    $day = $nowThere.Date
    if ($when -match '(?<mon>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\w*\s+(?<d>\d{1,2})') {
        $mon = [array]::IndexOf(@('Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'), $Matches['mon']) + 1
        $day = New-Object DateTime $nowThere.Year, $mon, ([int]$Matches['d'])
    }
    $there = $day.AddHours($h).AddMinutes($m)
    if ($there -lt $nowThere.AddMinutes(-5) -and $when -notmatch '\d{1,2}\s*,|\w{3}\s+\d') { $there = $there.AddDays(1) }
    $utc = [TimeZoneInfo]::ConvertTimeToUtc([DateTime]::SpecifyKind($there, 'Unspecified'), $tz)
    return $utc.ToLocalTime()
}

$script:LimitUntil = $null      # paused for the usage limit until this time (or a call that works)
function Mind-Limit {
    $lines = New-CallLines
    $limit = $null; $worked = $false
    foreach ($l in $lines) {
        try { $c = $l | ConvertFrom-Json } catch { continue }
        $okProp = Prop $c 'ok'
        $problem = [string](Prop $c 'problem')
        if ($okProp -eq $false -and $problem -match 'hit your .*limit') { $limit = $problem }
        elseif ($okProp -eq $true) { $worked = $true }
    }
    if ($limit -and -not $script:LimitUntil) {
        $until = Reset-Time $limit
        $script:LimitUntil = if ($until) { $until.AddMinutes(3) } else { (Get-Date).AddHours(6) }
        Say "USAGE LIMIT: $limit. Pausing the game until $($script:LimitUntil.ToString('HH:mm')) or until an agent's call works again."
        [void](Send 'pause')
        return
    }
    if ($script:LimitUntil -and (($worked -and -not $limit) -or (Get-Date) -ge $script:LimitUntil)) {
        Say "The usage limit is over ($(if ($worked) { 'an agent''s call worked' } else { 'the reset time passed' })): resuming."
        $script:LimitUntil = $null
        [void](Send 'resume')
    }
}

# ---------------------------------------------------------------- the jobs

function Make-Playerless {
    if (-not (Save-Exists $From)) { throw "No save named '$From' in $Saves." }
    if (Save-Exists $Start) { throw "A save named '$Start' exists already; remove or rename it first, so the playerless start is made only once." }
    $modes = Get-Modes
    $runSave = Get-Setting 'run_save'
    Say ("Every agent switched off for the switch (was: " + (($modes.GetEnumerator() | ForEach-Object { (Split-Path $_.Key -Leaf) + ' ' + $_.Value }) -join ', ') + '), and run_save set aside so no run is readied.')
    All-Off
    Set-Setting 'run_save' '(making the playerless save: none)'
    try {
        Start-Game
        Write-Plan $false $null 'making the playerless start save'
        $r = Must ("load " + $From) 900
        $r = Must 'playerless go'
        Say ("  " + ($r.answer -replace "`n", ' | '))
        $r = Send 'check'
        $s = Read-Status
        Say "  the stand-in: $(Prop $s 'hero') in $(Prop $s 'place') ($(Prop $s 'menu')), the clock $(Prop $s 'clock')."
        if (-not (Prop $s 'place')) { throw 'The stand-in is in no town after the switch.' }
        $r = Must ("save " + $Start) 300
        Quit-Game
    }
    finally {
        Clear-Plan
        if (Game-Alive) { Quit-Game }
        Set-Setting 'run_save' $runSave
        Set-Modes $modes
        Say 'The agents and run_save put back as they were.'
    }
    if (-not (Save-Exists $Start)) { throw "The save '$Start' is not on disk." }
    $list = @(Run-Saves)
    if ($list -notcontains $Start) { Set-Setting 'run_save' ((@($list) + $Start) -join '; '); Say "run_save is now: $(Get-Setting 'run_save')" }
    Say "The playerless start save is made: $Start."
}

function Dry-Run {
    if (-not (Save-Exists $Start)) { throw "No save named '$Start': make it first (-MakePlayerless)." }
    $modes = Get-Modes
    $runSave = Get-Setting 'run_save'
    Say 'Dry run: every agent off and run_save set aside, so nothing readies or starts a run.'
    All-Off
    Set-Setting 'run_save' '(dry run: none)'
    $dry = 'AUTOPILOT DRY RUN'
    try {
        Start-Game
        Write-Plan $false $null 'dry run'
        [void](Must ("load " + $Start) 900)
        $s = Read-Status
        $run = Prop $s 'run'
        if ($null -ne $run) {
            # a run being set up when the playerless save was made counts that save as its own and readies again from it (Runs.OnLoad); stopped here, before any clock
            if ((Prop $run 'status') -ne 'armed') { throw "A run is $(Prop $run 'status') after the load: the dry run stops." }
            [void](Must 'console agents.run stop')
            Say "  run $(Prop $run 'id') was readied again from the playerless save (it was being set up when the save was made): stopped before its clock ran."
        }
        [void](Must 'selftest' 60)
        Write-Plan $true $null 'dry run'
        Start-Sleep -Seconds 3
        [void](Must 'wait')
        $h0 = [double](Prop (Read-Status) 'hours')
        Start-Sleep -Seconds ($DryMinutes * 30)
        [void](Must 'pause'); Start-Sleep -Seconds 8
        $a = [double](Prop (Read-Status) 'hours'); Start-Sleep -Seconds 6; $b = [double](Prop (Read-Status) 'hours')
        if ($b -ne $a) { throw "The clock moved while paused ($a -> $b)." }
        [void](Must 'resume')
        [void](Must 'move' 60)
        Start-Sleep -Seconds 12
        $s = Read-Status
        Say "  after the move: $(Prop $s 'place'), menu $(Prop $s 'menu'), clock $(Prop $s 'clock') (the keeper puts him back to waiting)."
        Start-Sleep -Seconds ($DryMinutes * 30)
        $h1 = [double](Prop (Read-Status) 'hours')
        Say ("  the clock ran {0:0.0} in-game hours in about {1} real minutes." -f ($h1 - $h0), $DryMinutes)
        [void](Must ("save " + $dry) 300)
        Say '  restart, as for memory:'
        Quit-Game
        Start-Game
        Write-Plan $false $null 'dry run, after the restart'
        [void](Must ("load " + $dry) 900)
        $s = Read-Status
        Say "  back: $(Prop $s 'hero') in $(Prop $s 'place'), menu $(Prop $s 'menu'), waiting $(Prop $s 'waiting'), clock $(Prop $s 'clock')."
        Write-Plan $true $null 'dry run, after the restart'
        [void](Must 'wait')
        Start-Sleep -Seconds 30
        $s = Read-Status
        Say "  waiting $(Prop $s 'waiting'), clock $(Prop $s 'clock'), $(Prop $s 'date'); pop-ups answered $(Prop $s 'popups_answered'), memory $(Prop $s 'memory_mb') MB."
        Quit-Game
    }
    finally {
        Clear-Plan
        if (Game-Alive) { Quit-Game }
        Set-Setting 'run_save' $runSave
        Set-Modes $modes
        Say 'run_save and the agents put back as they were.'
    }
    Say 'Dry run done.'
}

function Begin-Run([string]$load, [bool]$fresh) {
    Start-Game
    Write-Plan $false $null "run $Arm, setting up"
    [void](Must ("load " + $load) 900)
    $s = Read-Status
    $run = Prop $s 'run'
    if ($fresh) {
        if (-not $run -or (Prop $run 'status') -ne 'armed') { throw "Loading $load readied no run (run_save is: $(Get-Setting 'run_save'))." }
        $c = Send 'check'
        if ($c.answer -notmatch ('would be ' + $Arm + '\b')) { throw "The run would not be ${Arm}: $(($c.answer -split "`n")[0])" }
    }
    elseif (-not $run -or (Prop $run 'status') -ne 'running') { throw "Loading $load did not carry the run on." }
    Write-Plan $true ("TEST AUTO " + $Arm) "run $Arm"
    [void](Must 'wait' 60)
}

function Mind-Run {
    $restarts = 0
    $memoryRestarts = 0
    $lastRun = $null; $lastSave = $null
    $staleSince = $null
    while ($true) {
        Stay-Awake
        if (Test-Path $StopFile) {
            Say 'stop.txt found: saving the run and closing the game.'
            Remove-Item $StopFile -Force
            try { if ($lastSave) { [void](Send ("save " + $lastSave) 300) } } catch { }
            Quit-Game
            return 4
        }
        $alive = [bool](Game-Alive)
        $s = Read-Status
        $age = Status-Age
        $run = Prop $s 'run'
        if ($run) {
            $lastRun = Prop $run 'id'
            $ls = Prop $run 'last_save'
            if ($ls -and $ls -like 'TEST AUTO*') { $lastSave = $ls }
            if ((Prop $run 'status') -eq 'finished') {
                $end = [string](Prop $s 'end_save')
                if ($end -like 'saved *' -or $end -like 'NOT saved*') {
                    Say "The run $lastRun is over (day $(Prop $run 'day')): $end."
                    Quit-Game
                    return $(if ($end -like 'saved *') { 0 } else { 3 })
                }
            }
        }
        # a crash: the game gone without being asked, or no status for minutes
        $limitSeconds = if ((Prop $s 'state') -eq 'loading') { 600 } else { 180 }
        if (-not $alive -or $age -gt $limitSeconds) {
            $what = if (-not $alive) { 'the game closed by itself' } else { "the game stopped answering ($([int]$age) s without a status)" }
            Say "CRASH OR FREEZE: $what."
            $g = Gather $what
            if ($g.Known) {
                Say "It is the +0x6013cc engine fault. Holding: the game is left as it is$(if ($g.Alive) { ' with its crash window open' }) and nothing restarts until Mert answers. Files: $($g.Dir)"
                return 3
            }
            if ($restarts -ge $MaxRestarts) { Say "A crash or freeze again after $restarts restarts: holding for Mert. Files: $($g.Dir)"; if ($g.Alive) { Stop-Process -Id $script:GamePid -Force }; return 3 }
            if ($g.Alive) { Stop-Process -Id $script:GamePid -Force; Start-Sleep -Seconds 5 }
            $restarts++
            $from = if ($lastSave) { $lastSave } else { $Start }
            Say "Restart $restarts of $MaxRestarts from $from$(if (-not $lastSave) { ' (no run save yet: the run begins anew)' })."
            Begin-Run $from (-not $lastSave)
            continue
        }
        if ($s -and (Prop $s 'pid') -eq $script:GamePid) {
            Mind-Limit
            $mem = [int](Prop $s 'memory_mb')
            if ($mem -gt $MemoryRestartMb -and $run -and (Prop $run 'status') -eq 'running' -and -not (Prop $s 'busy') -and -not $script:LimitUntil) {
                $memoryRestarts++
                $name = 'TEST AUTO ' + $Arm
                Say "MEMORY: the game holds $mem MB (limit $MemoryRestartMb): saving as $name and restarting ($memoryRestarts so far)."
                [void](Must ("save " + $name) 300)
                Quit-Game
                Begin-Run $name $false
                continue
            }
        }
        Start-Sleep -Seconds 5
    }
}

# ---------------------------------------------------------------- go

$code = 0
Say ("autopilot.ps1 " + ($jobs -join ' ') + ', module ' + $(try { (Select-Xml -Path (Join-Path $Game 'Modules\BannerlordAgents\SubModule.xml') -XPath '//Version').Node.value } catch { '?' }))
$free = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
if ($free -lt 3) { Say "Only $free GB free on C: (saves and the page file live there): not starting."; exit 2 }
if ($free -lt 8) { Say "Note: only $free GB free on C:." }
Stay-Awake
try {
    if ($MakePlayerless) { Make-Playerless }
    elseif ($DryRun) { Dry-Run }
    else {
        if (-not (Save-Exists $Start)) { throw "No save named '$Start': make it first (-MakePlayerless)." }
        if (@(Run-Saves) -notcontains $Start) { throw "run_save does not name '$Start' (it is: $(Get-Setting 'run_save'))." }
        $armArgs = @('-Arm', $Arm); if ($RivalsOff) { $armArgs += '-RivalsOff' }
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'arm.ps1') @armArgs | ForEach-Object { Say "  arm: $_" }
        Begin-Run $Start $true
        Say "Run $Arm begun: the stand-in waits at normal speed. Minding it to its end."
        $code = Mind-Run
    }
}
catch {
    Say "STOPPED: $($_.Exception.Message)"
    if (Game-Alive) {
        if ($Arm) { Say 'The game is left open for a look (the keepers off).'; Clear-Plan } else { Quit-Game }
    }
    $code = 2
}
finally {
    if ($code -ne 3) { Clear-Plan }
    Let-Sleep
}
Say "autopilot.ps1 ends with code $code."
exit $code
