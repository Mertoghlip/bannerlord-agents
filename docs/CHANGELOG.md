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
  - the smoke test passed in full (77 checks) and the war room's self-test too (26; first written here as 25);
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

### 11:42 to 11:51 · The second live session, with tools and memory ([report](reports/2026-10-09-second-live-session.md))
- **What:** Mert loaded the Summer 9, 1087 save, the starting point of the first session, and marched on Western Moria. The orcs ran on version 0.5.0, live, under Opus 5.5.
- **Numbers:**
  - the game's history held 318 entries about the orcs, read in 56 ms;
  - 9 pokes and 6 answers, in 8 to 29 s (median about 16 s);
  - 10 orders carried out, 2 refused, and 4 waiting for the clock when the game was paused and closed;
  - the first story so far, 550 words in 14 s.
  - The agent judged Mert's army too strong, and ordered Ulga's army out of Western Moria to join Vrakdush at Methedras. The order arrived while the game was paused and was never carried out.
- **Found:**
  - gather's false "broke up before it formed";
  - an army refused whole for one busy lord;
  - the game's meeting place in the chronicle;
  - poke numbers starting again at #1;
  - notes from the Oct 8 run (Summer 17) given to a Summer 9 save.
  - Also seen: 39 ms and 28 ms on the game's thread after the load and at the first orders.
- **Outside the repository:**
  - at the first poke (11:47:48) the war room took the old journal over as the notes of campaign `Ah2auppicLLJ` (`orc-war-council\memory\Ah2auppicLLJ\`) and renamed it `journal-before-memory-20261009-1147.md`;
  - the bridge started `chronicle_mistymountainorcs.jsonl` in the campaign's folder.

### 11:55 · Version 0.5.1: army fixes · commit 8111644 (installed 11:55)
- **What:**
  - gather judges its army from the next hour on, and a new "fail" ending keeps an army that truly broke up from starting its next order, setting its lord free;
  - gather and add form the army with the lords who can come, naming the ones left out;
  - the chronicle names the order's meeting place;
  - poke numbers come from the campaign's whole poke file once, then from `pokes_<realm>.number` beside it, so the next poke is #126;
  - notes taken over from the old journal hold from the first poke of the memory, so a save from before it starts afresh;
  - renaming the journal can no longer collide with an earlier copy.
- **Why:** the second live session (above). Mert quit the game for the army fixes.
- **Checks:**
  - the smoke test (81 checks) and the war room's self-test (28) passed;
  - 327 game references, 0 missing;
  - the installed DLL has sha256 `384C41BEBCA7AAA9…`.
- **Outside the repository:**
  - the war room was stopped and restarted at 11:55 and again at 11:57 for the rebuilds;
  - `tools.txt` was rewritten;
  - in `memory\Ah2auppicLLJ\snapshots.jsonl`, the old journal's notes were re-dated from "any date" to poke #1 (Summer 9, 1087, 09:00), so reloading that save starts the orcs afresh (backup `snapshots.jsonl.bak-20261009-before-seed-date`).

### 12:01 to 12:15 · The third run, and what the orcs were missing
- **What:** Mert reloaded the Summer 9, 1087 save on 0.5.1 and marched on Western Moria again (his standing test), then closed the game for a diagnosis.
- **The fixes held:**
  - the chronicle was cut back 24 lines and the memory went back to empty;
  - the pokes went on from #126;
  - Naglur's army formed with 6 lords and 988 men, with no false "broke up";
  - the story so far was rewritten at 531 words.
- **The agent's answer to Mert's host** (5,528 men, seen at 19:00): a relief army under Vrakdush with 10 lords (about 2,219 men, strength about 3,778, `strike_at` 0.8, waiting up to 120 hours). It arrived while the game was paused and was never carried out.
- **The diagnosis, from the game at 19:00** (Mert: "Why do the orcs have so little men?"):
  - **The realm:** 49 lords' parties with 8,257 men, strength 14,494, against Mert's army at 14,674; 12 of those parties (2,275 men) belong to clans outside the ruler's. Garrisons 4,249 men; militia 2,496.
  - **Thin parties:** 24% full on average (median 22%; 12 lords under 15%).
  - **Losses:** the game's records hold 158 captures of orc lords (27 on Spring 20, 1087, and 18 on Spring 7). Four villages were looted and Sídwic was being raided.
  - **Scattered:** eight lords were "defending Bar-noss" against a 205-man raider.
  - **Command:** only 3 of the 49 lords held the agent's orders. The instructions told it to give only the changes, so the other lords stayed with the game's AI.
  - **Recruiting rules, read in the game's code:**
    - a lord recruits only at a town or village;
    - its party's gold must be above 50 + 20 a man;
    - a lord who does not lead the clan also needs clan gold above the leader's wages + 40 a man in the leader's party;
    - there must be room in the wage budget;
    - TAOM blocks recruiting across good and evil, for the AI too.
    - Gold, wage room and the volunteers waiting were not measured yet.
- **Also seen:** 39 and 26 ms on the game's thread for the first hourly copy after a load, 28 and 32 ms applying the first orders; the hourly copy otherwise about 5 to 7 ms (the goal is under 1 ms).
- **Mert's decisions (Oct 9):**
  - build plans A to D: the missing facts, a join order, the garrison counted in a relief, recruitment measured;
  - build the council of war (E1);
  - rewrite the agent's instructions as a medieval commander's, from a study of medieval doctrine, without giving it his own plan (E3).

### 13:30 · Version 0.6.0: the council of war, the missing facts, a commander's doctrine · commit 71cbbcc (installed about 13:30)
- **What the agent now sees:**
  - **YOUR REALM IN SUM:** the field forces, the castles and towns, the villages, and how many lords are prisoners.
  - **YOUR CAPTURED LORDS:** each one, grouped by where they are held, and since when, from the game's own prisoner records (`Hero.PartyBelongedToAsPrisoner`, `CaptivityStartTime`).
  - **The muster:** how many lords, men and strength could reach a place within one, two and three days, as running totals, and whom a leader there could call into an army at once (the game's calling range). It is in the facts of every poke about a holding, in full in each castle's and town's look-up, and in the council's briefing.
  - **Look-ups** for each captured lord, and `captured.txt`.
  - **The council's briefing** (`council.txt`, every in-game hour): the purse (the ruler's clan's gold and influence, every other clan's influence), every lord in detail, the muster at each castle and town, and the captured lords.
- **New orders** ([TOOLS.md](TOOLS.md)):
  - **join:** a lord marches toward another lord's army and joins it once the game would let it be called, paying the leader's clan's influence then. It fails, and tells the agent, if the army breaks up, its leader is gone, or the clan cannot pay.
  - **gather and add** now send lords too far away to be called on a join order, instead of leaving them out; gather waits for them. The receipt says who comes now, who marches first, and who cannot come.
- **A relief counts the defenders, by the game's rule:**
  - read in the 1.4.8 code: the garrison and the lords inside sally out to join a relief force only when they and the friendly forces at the siege camp are more than 1.5 times the enemy's there. The game checks this when the battle starts and every hour of it. Militia never sally.
  - So relieve and defend now weigh `relief_strength` (and `inside_strength`), not the relief's strength alone, and the facts give the strength inside a besieged holding.
- **Recruitment measured** (logs only, [LOGGING.md](LOGGING.md)):
  - once a day, `recruitment.jsonl` records each lord's gold against the game's two gold bars, its wage room, fill, starving and held, the men it recruited and where, and the hours it sat where it could recruit;
  - each town's and village's volunteers waiting (and within the ruler's relations);
  - a daily sentence in the log, and `agents.recruits`.
- **The council of war** (war room, [WARROOM.md](WARROOM.md#the-council-of-war)):
  - a slower call on its own thread (effort high, up to 12,000 tokens of thinking, 420 s), so the fast answers go on meanwhile;
  - it sits after the first look of a session, after big events (a holding changing hands, war, peace, one of the realm's places besieged, an army formed), at least every 24 in-game hours, and when its last answer asked (`council_in`), never within 6 hours of the last except after the first look;
  - it reads the briefing and the hourly situation, and may order every lord;
  - a fast answer that orders a lord while it sits wins for that lord, and the next poke says which council orders did not go out;
  - `WarRoom council` sits one by hand.
- **New instructions for the agent** (`agent/warroom-system.template.md`; the study is [DOCTRINE.md](DOCTRINE.md)):
  - a medieval commander's persona and 14 principles of doctrine, each tied to a writer or a campaign: Vegetius, the *Strategikon*, *On Skirmishing*, Kekaumenos, Jean de Bueil; Hattin, 1183, Mortemer, Charles V and du Guesclin, the Mongols in 1241, Ain Jalut, Baybars and others;
  - a new goal, without "keep every castle and town you hold";
  - lords without orders are said plainly to follow the game's own AI;
  - "change only what the poke needs" is kept for pokes, with the council setting the realm's course;
  - neutral examples instead of a relief of Western Moria;
  - no plan for this war (Mert: "It should arrive at that conclusion on its own").
- **Why:** the diagnosis above.
  - The orcs were scattered and thin.
  - The agent could not see its captured lords, or how much of the realm could mass where.
  - Far lords were left out of armies, and a relief ignored the garrison.
  - Its instructions pushed it to defend every place with small answers.
- **Checks:**
  - the smoke test passed in full (97 checks, 16 new) and the war room's self-test too (38, 10 new);
  - 348 game references, 0 missing;
  - the installed DLL has sha256 `4161700C8FEDADB0…`.
- **Outside the repository:**
  - the 0.5.0 war room was stopped at 13:30:04 (it was idle);
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` in `C:\Users\karab\Projects\orc-war-council`, and the older chat's `CLAUDE.md` with its goal brought in line. It also rewrote `tools.txt` (17 orders), and rewrote the bridge's settings unchanged (live, all lords);
  - the 0.6.0 war room started at 13:30:46 and added the council's keys to `warroom.txt`, keeping the live values (`max_lessons` 12, `timeout_seconds` 120).
- **Not yet seen in a game:**
  - a council;
  - a join;
  - a relief weighing the garrison;
  - a day of recruitment numbers.

### 19:30 · Version 0.6.1: defend and patrol fixed · commit 013fd4d (installed 19:29, again 19:30)
- **Why:** Mert asked how the defend and patrol orders work, and then, after the walkthrough, "Yes, fix the weak spots." The walkthrough read the game's patrol in the 1.4.8 code and found five:
  - the game's patrol ranges between half and all of half a day's march around a castle (20 to 41 map units at the orcs' median speed), beyond the 20 units defend watches, and defend had the danger rule off;
  - a chase did not watch for a big force coming;
  - under a strong siege, defend sheltered inside the besieged place, so a fall took the lord with it (the agent's own lesson after Spring 20);
  - a lord below the ranks floor hid in a castle, where no one can recruit;
  - only enemies in view counted: no bandits, no army that had just walked out of sight.
- **What:**
  - **A patrol kept within a radius:** a tool with a radius (defend, screen), or the plain patrol given one, now patrols from point to point within a quarter to three quarters of that radius from the gate, instead of the game's patrol. The plain patrol keeps the game's by default (radius 0), and its description now says how far that goes.
  - **defend:**
    - the danger rule is on around the lord itself;
    - a chase breaks off when the threat near the holding reaches the sheltering line;
    - a relief breaks off when enemy forces besides the besiegers come near;
    - it shelters in the nearest other castle or town, not the one it guards, unless the new `stay_inside` setting is 1;
    - from shelter, it strikes a siege that becomes breakable;
    - below the ranks floor it refills in the nearest town or village, with a reminder to the agent after two days.
  - **The threat near a place** (new): enemy lords, armies and bandits in view, and armies seen there in the last 6 hours, at their last place. Defend and raid weigh it when they decide to shelter or pull back.
  - **The danger rule for every order:**
    - it counts bandits;
    - it never shelters a lord in the castle the danger is besieging or marching to besiege;
    - it never puts defend's lord in its own holding unless told to stay inside;
    - it no longer turns a relief back from the siege camp it is striking (the other lords camped there are part of that fight).
  - **adjust** makes a tool's current move again, so a new radius takes effect at once.
  - **The wording** the agent reads (the library, the report's realm in sum, the orders in force, the council's briefing, receipts) now says plainly that lords without orders are led by the game's own AI, as its instructions do.
- **Checks:**
  - the smoke test passed in full (106 checks, 9 new) and the war room's self-test too (38);
  - 348 game references, 0 missing;
  - the installed DLL has sha256 `8066213EEE80389D…`.
- **Outside the repository:**
  - the war room was stopped at 19:29:42 and 19:30:44 (idle) and restarted after each install;
  - `scripts/setup-agent.ps1 -Mode live` rewrote `tools.txt` (17 orders, new defend and patrol) and refreshed the older chat's `CLAUDE.md` (the same wording); the bridge's settings were unchanged.
- **Not yet seen in a game:** the patrol within a radius, a refill, and the new shelter choices.
