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

### 20:24 · The agent's folder readied for the war room (outside the repository)
- **What:** `scripts/setup-agent.ps1 -Mode live`, in its new form (part of version 0.4.0, below):
  - the bridge's settings: `agent = live`, `agent_lords = all`, and `agent_dir = C:\Users\karab\Projects\orc-war-council\warroom` (until then shadow mode and `...\channel`);
  - in the agent's folder: `warroom-system.md`, the war room's instructions for the agent; the channel folder `warroom\`, with `orders\done`, `lookups\` and the order library `tools.txt`;
  - `warroom.txt`, the war room's settings: Opus 5.5 at high effort, thinking capped at 3,000 tokens (5,000 for an urgent poke), 120 s at most per call;
  - the journal: the agent chat's journal is kept as `journal-chat-20261008.md`. `journal.md` carries over its plan in three sections (standing plan, lords and their tasks, lessons), reworded for the new orders, without its list of problems.
- **Why:** Mert's "turn the agent on and let's begin testing": Opus 5.5 commands every orc lord, live.

### 21:14 · Version 0.4.0: tools, armies and the war room · commit 1f87367 (installed 20:59, again 21:10 and 21:14)
- **What:**
  - **The behavior language and the tool engine** (`src/BannerlordAgents/Tools`, [TOOLS.md](TOOLS.md)):
    - a tool is a small JSON file of settings, states, rules and call-backs, read by a checker before it runs;
    - tools run every in-game hour on the helper thread, and see only the realm's own view;
    - for now the engine sits in the bridge as game-free code; it moves into the shared core when the war room needs it.
  - **The library, 16 orders:**
    - moves: goto, patrol, engage, follow, leave, recruit, hold;
    - tools: defend, garrison, besiege, raid, hunt, screen, rest;
    - armies: gather, relieve.
    - Every order carries two rules: shelter from an enemy 1.5 times stronger within 8 (the game switches off a held lord's own fleeing), and buy food with under two days left.
  - **Armies by the game's own rules:** gather forms the army at once (`Kingdom.CreateArmy`) at the meeting place the agent names, at the game's influence price and with its rules on who may be called. Add, release and disband change it. Orders to the leader move the whole army.
  - **Changing orders without re-planning:** adjust changes only the settings, then gives the next order, and an empty answer leaves everything standing.
  - **What the agent reads:**
    - every poke gains three sections: WHAT CHANGED SINCE YOUR LAST ANSWER, YOUR ORDERS IN FORCE, and READY-MADE FACTS;
    - look-up files for every holding, lord and known enemy;
    - the library itself, as `tools.txt`.
  - **The war room** (`src/BannerlordAgents.WarRoom`, [WARROOM.md](WARROOM.md)) makes one fresh Claude Code call per poke, with the thinking capped. It replaces the agent chat a week early, and replay mode measures answer time on recorded pokes.
  - **The console:** `agents.tools` lists the library, and `agents.order` takes settings and lords.
  - **The smoke test** grows to 67 checks, tools included.
- **Why:** Mert's asks of Oct 8:
  - the tools to create and order armies;
  - answers as fast as possible;
  - changing single orders instead of re-planning the whole realm at every poke.

  Moving lords between fronts was put off.
- **Measured** (replays of the first session's pokes; Opus 5.5, high effort, thinking capped at 3,000):

  | Poke | War room | Agent chat |
  |---|---|---|
  | #6 | 28 s | not answered |
  | #10 | 15 s | 93 s |
  | #54 | 11 s | 53 s |
  | #83 | 15 s | 21 s |
  | #124 | 24 s | 33 s |

  - The median answer took 15 s, against 53 s.
  - Answer time follows the thinking at about 110 tokens a second.
  - Fast mode is off for this account ("extra usage" is disabled).
- **Bugs found and fixed on the way:**
  - The war room's first calls hung for 240 s. Claude Code lingers after answering, and the war room missed the result line, whose parts come in any order. Now the result ends a call, and a call silent for 45 s is asked once more.
  - A runaway answer on poke #6 ran to 19,000 characters. The instructions promised look-ups to a replay call that had no tools, so the model typed tool calls as text. The look-ups section is now dropped whenever reading is off, and a runaway is stopped and asked once more.
  - An answer holding two JSON objects was read across both. Now the last complete object wins, and the instructions ask for one.
  - The small model thought 8,800 tokens (85 s) even at low effort. The thinking cap (Claude Code's MAX_THINKING_TOKENS) is what bounds it: the same call then took 10 s.
  - Recruit accepted castles, which give no recruits (fixed for the 21:10 install).
  - A tool's first rules waited an hour, and the logs showed a tool's settings as they were later, not at the moment.
  - Log and console lines that called every lord "he" now say "the lord" (21:14 install).
- **Checks:** the smoke test passed in full (67 checks), with 292 game references and 0 missing. The installed DLL has sha256 `C01CD6034D5C608B…` (`346CD41F5119D59E…` at 20:59, `EBDC30CDABF86E4F…` at 21:10).
- **Outside the repository:**
  - the order library `tools.txt` was rewritten at each install;
  - the war room was started at 21:18 in its own minimized window ("War room"). It runs at below-normal priority and waits for the game.
- **Not yet seen in a game** (the smoke test covers each on a made-up world):
  - an army formed by the agent;
  - held lords sheltering and buying food;
  - a lord stopping near an enemy holding;
  - an idle army breaking up after about two days;
  - answer time with the new, longer pokes.

## Friday, October 9

### About 10:27 · The old agent chat wrote into the old journal (outside the repository)
- **What:** the agent chat of Oct 8, still open in VS Code, added a "Problems" section to `orc-war-council\journal.md`. It said that no pokes had come for about 8 hours, and that Mert had asked it to stop watching for them.
- **Effect:** none on the war room. The journal's plan and lessons become the first campaign's notes (below), and its lords' tasks and problems do not carry over. The chat itself has nothing left to answer and should be closed.

### 10:56 to 11:00 · The memory checked against Claude Code, with a small model
- **The war room's settings** (`orc-war-council\warroom.txt`) gained the memory keys (`memory`, `story_every`, `story_words`, `story_model`, `story_effort`, `story_thinking_cap`, `conversation_limit`) when the new war room first read them. The old values were kept.
- **Both memories, end to end** (replay in order, Haiku 4.5, pokes #6, #10 and #54):

  | Memory | Tokens read per answer | Answer time | Story |
  |---|---|---|---|
  | Written | 11,800, 12,000, 13,500 | 8 to 16 s | rewritten once, 350 words in 15 s |
  | Conversation | 11,800, 21,100, 30,500 | 10 to 12 s | (none) |

  - The conversation was resumed each poke.
  - The notes changed with each answer.

### 11:01 · Version 0.5.0: each realm's memory of the war · commit 999f913 (installed 11:01)
- **What** ([MEMORY.md](MEMORY.md)):
  - **The chronicle (bridge):** every non-quiet notice the realm gets, every enemy army it sees come into view, and every order the agent gives (carried out or refused, with its reason).
    - At the first load, what the game's own history log still holds about the realm fills the time before the chronicle's own lines, marked as such.
    - It is kept per campaign and realm, and mirrored into the channel (`chronicle.txt`, `chronicle.jsonl`).
    - It is cut back to the save's date when an earlier save is loaded.
  - **New notices:**
    - a siege lifted and a lord killed, which poke;
    - a lord set free and an army of the realm's own formed, which go only into the chronicle.
  - **The report:** its NEWS becomes RECENT HISTORY, the chronicle's last two days. `poke.json` tells the war room the poke's hour, the campaign and this load of the game. `agents.chronicle` prints the last lines.
  - **The war room's memory, per campaign:**
    - the agent's notes (plan, a line per enemy, lessons), changed only by its answers through `plan`, `enemies` and `lessons`;
    - the story so far, which the chronicler rewrites in quiet moments, on its own thread, beside the answers;
    - snapshots, so a reload cuts the notes and the story back too.
  - **Each message** carries the story, the notes and any chronicle lines the story does not cover yet.
  - **The one-long-conversation memory** is a setting (`memory = conversation`), for comparison. `replay --memory written|conversation` answers recorded pokes in order with the memory carried along. `WarRoom story` rewrites the story now. `WarRoom selftest` checks the memory without any call, and `deploy.ps1` runs it before installing.
  - **The instructions:** the agent's (`agent/warroom-system.template.md`) describe the memory and the `enemies` field. The chronicler's are new (`agent/chronicler-system.template.md`).
- **Why:** Mert, Oct 9: "each realm should have a continuous memory of the history of the game". Mert approved this design over one long conversation, which fills up within an hour or two and could not carry over to a small local model. The conversation is kept, to measure the difference.
- **Checks:**
  - the smoke test passed in full (77 checks) and the war room's self-test too (25);
  - 327 game references, 0 missing;
  - the installed DLL has sha256 `05AC49E933B36345…`.
- **Outside the repository:**
  - the 0.4.0 war room was stopped at about 10:57 for the rebuild;
  - `scripts/setup-agent.ps1 -Mode live` (11:01) installed the new `warroom-system.md` and `chronicler-system.md`, and refreshed `tools.txt`;
  - the 0.5.0 war room was started at 11:02, and again at 11:03 after a small fix (the self-test no longer reads the live agent folder; the chronicle is read every few seconds at most while idle);
  - at the first poke, `journal.md` becomes `memory\<campaign>\notes.md` and is renamed `journal-before-memory-<date>.md`.
- **Not yet seen in a game:**
  - the game's history read at a real load;
  - the first story;
  - a reload cutting both memories back;
  - answer time with the memory added.
- **Not yet run:** the full comparison of the two memories with Opus 5.5, about 120 calls; it waits for Mert's go-ahead.
