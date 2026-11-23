# The autopilot: test runs with nobody at the game

Since module 0.23.0 (Nov 23). Nov 22: I want the capability for you to launch the game, launch the test start save, switch my character to a wanderer and move into a safe settlement, start waiting there at normal speed, create the save for the playerless save, and do everything else needed (pausing the game, saving to the test save, quitting) so I can run those tests fully autonomously. Nov 23: build it for C1 and C2 first; normal speed; on a crash, note it, gather all the files needed for a fix and wait for my answer; on the usage limit, pause and wait for the reset; unknown pop-ups answered no or cancel.

Two halves:
- **The game side**, `src/BannerlordAgents/Autopilot.cs`: a command box and a set of keepers inside the module.
- **The run script**, `scripts/autopilot.ps1`: it starts the game, sends the commands, and watches the run to its end.

## Running it

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\autopilot.ps1 -MakePlayerless   # once
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\autopilot.ps1 -DryRun           # proves it, starts no run
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\autopilot.ps1 -Arm C1           # or -Arm C2
```

- **`-MakePlayerless`** loads `TEST START DGG MERGE` (`-From`) and makes the playerless switch (`agents.run playerless go`) with the clock stopped. It saves the result as `TEST START DGG MERGE PLAYERLESS` (`-Start`), quits, and adds that name to `run_save`. Every agent is switched off while it works and switched back afterwards, so it makes no model calls. It refuses if the playerless save exists already, so every arm starts from the same switch. The in-game hour is the same as the merge save's, since the clock never runs.
- **`-DryRun`** sets `run_save` aside and switches every agent off, so nothing readies a run. It then:
  1. loads the playerless save and runs the pop-up self-test (a question answered no, a notice closed, a list cancelled);
  2. waits at normal speed for `-DryMinutes`, pauses, checks the clock stood still, and resumes;
  3. moves the stand-in to another quiet town, waits again and saves as `AUTOPILOT DRY RUN`;
  4. restarts the game from that save, waits again, and quits.

  It puts `run_save` and the agents back as they were.
- **`-Arm C1|C2`** sets the arm with `arm.ps1` and starts the game. It loads the playerless save, which readies the run, and checks the run would be that arm; anything else stops it before the clock runs. Then it puts the stand-in to waiting at normal speed, so the run begins, and minds the run to its end:
  - the run's own save every in-game day (`TEST AUTO <arm>`, made by the module);
  - a restart from that save when the game holds more than `-MemoryRestartMb` (20,000);
  - a pause on the usage limit until it resets;
  - the run's end: the module saves `TEST END <arm> <run>`, and the script quits the game.
- **Stopping it cleanly:** create `%LOCALAPPDATA%\BannerlordAgents\autopilot\stop.txt`. The script saves the run, quits the game and ends with code 4.
- **Exit codes:**
  - 0: done.
  - 2: could not set up; nothing started, or the script stopped before the run began.
  - 3: held. A crash waits for my answer, or something the script won't decide alone.
  - 4: stopped by `stop.txt`.
- **The laptop stays awake while the script runs**, through the script's own request to Windows; nothing is changed for good. The lid must stay open and the laptop plugged in.

## How the game is started

The script starts `bin\Win64_Shipping_Client\Bannerlord.BLSE.Standalone.exe` directly with `/singleplayer _MODULES_*...*_MODULES_ no_watchdog`. The modules are those ticked for single player in `Documents\Mount and Blade II Bannerlord\Configs\LauncherData.xml`, in order and with the duplicates, exactly as the launcher passes them (the rgl log's "Command Args"). Steam must be running; the script starts it if it isn't.

On Nov 22 the main menu came up 48 seconds after the start, with no pop-up. The game keeps running when its window isn't in front (`StopGameOnFocusLost=False`).

The game process is `Bannerlord.BLSE.Standalone`, not `Bannerlord.BLSE.Launcher`; `deploy.ps1` knows both. The game's own shutdown leaves a crash dump of its exit (about 90 MB, seen Nov 20 and Nov 22). The script removes that one after a quit it asked for; it is not a crash of the run.

## The command box

Folder `%LOCALAPPDATA%\BannerlordAgents\autopilot`:
- `in\*.txt` holds one command each, carried out in name order, one at a time; `out\<same name>.json` says how it ended (`ok`, `answer`). The script writes a command as `.tmp` and renames it, so the module never reads half of one.
- `status.json` is rewritten every two seconds. It shows:
  - where the game is (`main menu`, `loading`, `campaign map`, `campaign map, in a menu`, `battle or scene`) and the menu;
  - whether he is waiting, the clock, the save and load, the in-game date;
  - the stand-in and his town, and whether he rules the kingdom;
  - the run (id, arm, status, day, its newest save) and the end save's state;
  - the game's memory, and the pop-ups answered, incidents held back, clock restarts and town moves.
- `events.jsonl` logs every command, pop-up answered (with its full text), clock restart, incident held back, town move and save.
- `on.json`, written by the script, holds the game's process id, `wait`, `speed`, `save_name`, `save_hours`, `playerless` and `note`. The keepers work only while it names this very process. If I start the game myself they stay off, even with an old `on.json` left over.
- `answers.txt` (optional) holds lines like `yes | words in the title or text` or `no | words`. The first match decides a question's answer instead of the default.

The commands:

| command | what it does |
|---|---|
| `status` | writes the status at once |
| `load <save>` | from the main menu, loads the save the way the main menu's Continue does (`SandBoxSaveHelper.TryLoadSave`, then `SandBoxGameManager`). The "Module Mismatch" question that a save made with other mod versions raises is answered yes, for that save only. Done 3 s after the map screen is up. |
| `playerless [go] [name]` | `agents.run playerless` (Playerless.cs); with `go`, fails unless you no longer rule the kingdom afterwards |
| `wait [stopped]` | into the town's (or village's) wait menu, then the plan's speed. The game starts every wait at fast-forward (`GameMenu.StartWait`), so the speed is set after. `stopped` keeps the clock stopped in the same frame, so a run's clock never starts. |
| `speed stop\|play\|fast` | the clock: in a wait the unstoppable modes, else the stoppable ones |
| `pause`, `resume` | the clock stopped and held, so the keepers leave it alone; then released |
| `save <name>` | the game's own save-as. Done when the game says the save is over. Waits for a save already under way. |
| `check` | `agents.run check` |
| `console agents.<...>` | any `agents.*` console command; nothing else |
| `move` | the stand-in to the quietest other town |
| `selftest` | shows a question, a notice and a list, and passes if the keeper answered no, closed and cancelled |
| `menu`, `quit` | to the main menu (`MBGameManager.EndGame`), then for `quit` `Utilities.QuitGame`: the order of the game's own shutdown. The answer and the status are on disk before the game goes. |

## The keepers (only while `on.json` names the game's process)

- **Pop-ups.** The game keeps the pop-up on screen and its buttons in two static fields (`GauntletQueryManager._activeQueryData`, `_activeDataSource`). After a second on screen the keeper presses a button through them, exactly as a click does.
  - A question gets "no" unless `answers.txt` says otherwise.
  - A one-button notice is closed.
  - A list with a cancel button is cancelled; a list without one gets its first allowed choices.
  - Full-screen pictures (births, deaths, coronations in your clan) are closed after two seconds.
  - Each is logged with its full text, so I can see what was refused.
- **Incidents.** Since game version 1.3, waiting in a town starts an "incident" one hour in seven, at least 8 to 15 days apart (`IncidentsCampaignBehaviour`, `DefaultIncidentModel`). An incident is a choice screen that stops the clock. While the autopilot is on, `MapState.NextIncident` is cleared every campaign tick and hour, before the game's tick would start it. Nothing of it goes into the save.
- **The wait.** If the stand-in stands in a town menu, or in the wait with the clock stopped, for 5 seconds with nothing passing in the way (a question, a conversation, a save, a scene), he is put back to waiting at the plan's speed. A load restores the wait menu of a save made while waiting but leaves the clock stopped, and this restarts it.
- **The safe town (hourly).** The stand-in is moved at once to the quietest other town if any of these hold:
  - his town is besieged;
  - his kingdom is at war with its owner;
  - he is out on the map.

  "Quietest" uses the wanderer pick's rule (`Playerless.QuietTowns`). The move happens on a frame of its own, not inside the game's hourly step. If he is a prisoner it is only noted, since the game keeps its clock running then.
- **The run's own save.** Every `save_hours` (24) in-game hours, counted from the load, while a run is going, the module saves as `save_name`.
- **The run's end.** `Run.Finish` saves `TEST END <arm> <run>` without the question. The status shows it under `end_save`.

## Crashes, freezes, memory and the usage limit (the script)

- **A crash or freeze** means the game closed on its own, or there has been no status for 180 s (600 s while loading). The script gathers the files into `D:\BannerlordAgents\crashes\<time>-<pid>\` (the C: drive had only 9.7 GB free on Nov 22):
  - the game's rgl, error and watchdog logs of that process;
  - the engine's crash folder;
  - the TAOM crash zip (unpacked too) and debug log;
  - the TFAOMFixes boot logs;
  - Windows' dump of the process;
  - our session folder, bridge log and run record;
  - the autopilot's status, events and log.

  If the process is still up (a crash window open), it also writes a full dump of the game's memory, which names the dying soldier (`HeapLists --stackagents`). `summary.txt` says what happened, the last status, and the crash lines found.
- **The +0x6013cc engine fault** (`Agent.Die` on a mount whose falls the rider's moves lack): held. The game is left as it is, its crash window open, nothing restarts, and the script ends with code 3 until I answer. Other crashes restart from the run's newest own save, twice at most; a third holds. Before the run's first own save, a restart begins the run anew from the playerless save. In a playerless run I'm never in a battle in person, so this fault is unlikely.
- **Memory:** above `-MemoryRestartMb` the script saves the run, quits, starts the game again, loads that save and puts the stand-in back to waiting. The run carries on as from any of its own saves.
- **The usage limit:** new lines of every war room's `calls.jsonl` are read every 5 s. A failed call saying "You've hit your session limit · resets 10:50pm (America/Los_Angeles)" pauses the game. It resumes 3 minutes after the reset, converted to the laptop's time (Pacific is +11 h in Turkey), or as soon as a later call works. If the reset time can't be read, it waits up to 6 hours or until a call works.
