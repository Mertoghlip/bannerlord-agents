# Change log

Every change to the project, oldest first, so the checkpoint (Oct 27) and the final report (Dec 1) can say what changed, when, why, and what it did.

**What gets an entry:**
- every push;
- every install into the game;
- every edit outside this repository: other mods, the launcher's module list, the agent's folder, the settings.

**What each entry holds:** the local time (Pacific), what changed, why, the numbers and checks that go with it, and where it lives (a commit, files, or a path outside the repository).

**Commits:** an entry names its commit. An entry written with its own change gets the hash from a small follow-up commit in the same push.

## Tuesday, October 6

### 23:37 · The project set up · commit 94236cf
- **What:**
  - the separate BannerlordAgents game module: it loads, logs the session start and the real seconds per in-game day, and has `agents.status`;
  - the build reads the game's files from the local install, and none are committed;
  - `scripts/deploy.ps1` refuses to install while the game is open;
  - the plan (`docs/PLAN.md`) and the notes for Claude (`CLAUDE.md`).
- **Why:** the proposal was submitted on Oct 6. Mert asked for a mod of its own, to avoid mix-ups, and for everything on GitHub.

### 23:42 · The Oct 5 brief saved · commit 1fb2ef3
- **What:** `docs/proposal/brief.md`, exported word for word from the Claude doc "CS 329M Project Brief: Bannerlord Agents".

## Wednesday, October 7

### 14:52 · The plan updated · commit 7e548af
- **What:**
  - live play: the clock never stops for an agent;
  - first tests on the Misty Mountain Orcs in Mert's own campaign;
  - Mert's campaign-map play recorded as core data (a new research question, RQ6);
  - the bridge's work kept off the game's busy core;
  - `docs/FIRST-TESTS.md` written, in seven phases.
- **Why:** Mert's decisions of Oct 7.

### 15:01 · The plan: a ruler's decisions and vassal orders · commit 149e293
- **What:**
  - the recorder also covers realm decisions and the vassal orders Mert gives in conversation, read from Party AI Controls without changing it;
  - phase 7 added (Opus runs the whole orc realm);
  - a drawing board for parked ideas.

### 15:33 · Install 1, version 0.2.0, into the game · commit 731d76a (pushed 15:37)
- **What:**
  - the orcs' view and pokes, and the written report;
  - the play recorder;
  - the performance log;
  - hand orders, with lords handed back around every save;
  - the smoke test, 7 checks.
- **Before the install:** Mert's 3 newest saves were copied to `C:\BannerlordArchive\save-backups\*.before-agents-20261007-1533.sav` and checked identical.

### About 19:10 · The launcher: Bannerlord Agents ticked (outside the repository)
- **What:** BannerlordAgents was enabled in the launcher's module list (`LauncherData.xml`), after RTSCameraUniversal.
- **Backup:** `LauncherData.xml.bak-20261007-1910-before-agents`.

### 19:28 · The agent channel and the agent chat · commit c21078f
- **What:**
  - the agent channel: `pokes.log`, `poke.txt`, `report.txt`, answer files in, receipts out, in three modes (off, shadow, live);
  - orders get the recruit and free verbs, and `agents.vassal` orders Mert's own lords;
  - reports show ids in brackets;
  - templates for the agent chat's own folder, and the script that installs them;
  - the smoke test grows to 14 checks.
- **Outside the repository:**
  - the agent's folder `C:\Users\karab\Projects\orc-war-council` was created by `scripts/setup-agent.ps1`;
  - the bridge's settings were set to shadow mode, pointing at that folder.

## Thursday, October 8

### 00:36 · Version 0.3, the strategists' logs · commit b6a4c09 (installed about 00:35)
- **What:**
  - a folder of detailed logs for every game launch;
  - a watchdog, the freeze probe, and alarms;
  - fog and sight checks;
  - every order traced;
  - two game hooks: the movement watcher, and the game AI's reasons;
  - save hand-back records and a start-up self-check.
- **Fixes found while planning:**
  - poke numbers carry on across loads;
  - stale answer files are set aside;
  - a RECEIVED receipt while the clock is stopped;
  - files are swapped in whole, never half-written.
- **Checks:** the smoke test passed in full; the reference check found 248 game references, 0 missing.

### 15:24 to 15:51 · The first live session (phases 0, 1, 2 and 5)
- **What:** 27 minutes of Mert's campaign with version 0.3 and the orc agent (Opus 5.5) in shadow mode.
- **Numbers:**
  - no errors from our code;
  - 125 pokes in 199 in-game hours;
  - the agent sent 5 answers, a median of 53 s after each poke;
  - 3 crashes from TAOM, caused by ghost troops.
- **Where:** the logs are in `%LOCALAPPDATA%\BannerlordAgents\logs\sessions\20261008-152439`, and the report is below (15:54).

### About 15:51 · The fixes mod: ghost troops inside battles in progress (outside the repository)
- **What:** TFAOMFixes' troop repair also cleans the battles a save was still fighting: each battle's own troop lists, and each side's chosen troop for the battle calculation. It runs at every load and with `tfaomfix.troop_check fix`.
- **Why:** the first session's 3 crashes came from 24 ghost troop types hidden in saved battles.
- **Where:**
  - the source is `C:\BannerlordArchive\src-TFAOMFixes148\TroopRepair.cs`, with the old source kept in `src-TFAOMFixes148-before-battlerepair-20261008`;
  - the installed DLL has sha256 `774c17b06ab445fc…`;
  - the old DLL is kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-battlerepair-20261008-1551`.
- **Checks:** 502 references, 0 missing.
- **Still to check:** at the next load, the fixes mod's boot log should count the ghosts it found inside battles.

### 15:54 · The first session's report · commit 69fa761
- **What:** `docs/reports/2026-10-08-first-live-session.md`.

### 16:56 · Why the orcs were poked 125 times · commit 0f3aec7
- **What:** `docs/reports/2026-10-08-pokes.md`: every poke by type, the battles by who fought whom, and what each cut would save.
- **Finding:** 100 of the 122 battles were small fights, and the cuts would take the 125 pokes to 58.

### 17:14 · Version 0.3.1, fewer pokes · commit f47a05b (installed 17:14)
- **What:**
  - a battle pokes only when fought at a town, castle or village (an assault, a sally, a raid) or with lords on both sides. Fights with bandits, caravans and villagers go to the logs only, marked as not poked;
  - an enemy lord pokes only within 30 of one of the realm's holdings (the new setting `sighting_distance`), or when heading for, besieging or raiding one, or chasing one of its parties. Armies always poke. A lord who comes close while already in view now pokes too, which the old rule missed;
  - a raid announced again within a day, and a lost party whose lord's capture already says it, are kept quiet;
  - wording: "on the way to raid" until the raid begins; a lord captured in a battle keeps their name in the battle line; "X's party" no longer reads "X's Party's party";
  - the summary gains a "Not poked" line, and the smoke test grows to 41 checks.
- **Why:** Mert's go-ahead on the four cuts after the breakdown.
- **Expected:** about 58 of the first session's 125 pokes, 7 a day instead of 15, plus a few for lords who come close while in view.
- **Checks:** the smoke test passed in full; 253 game references, 0 missing; the installed DLL has sha256 `0E13B00DB78F626D…`.
- **Outside the repository:** the agent's instructions were reinstalled into its folder. They tell it what no longer pokes, and take effect when the agent chat is restarted.
