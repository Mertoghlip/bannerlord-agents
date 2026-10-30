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

## Friday, October 16

### 15:19 · Version 0.6.2: shelter where the defence would hold, else run · commit e4fc561 (installed 15:19)
- **Why:** Mert, on the danger rule: "IF the strength of all lords defending a place + the garrison of the sheltering location is enough to defend it against the opposing force, they SHOULD shelter there. If not, they should run." 0.6.1 never weighed whether a place would hold: it simply kept lords out of the castle the enemy marched on, and out of defend's own holding.
- **What:**
  - **Would it hold?** By the game's own test for whether besiegers should assault (read in 1.4.8): the defenders (garrison, militia, the realm's lords inside, the realm's lords holding a defend or garrison order for it within 20, and the lord itself), times what the walls are worth (2.4, 2.8 or 3.2 for wall levels 1 to 3, with a ram and two towers built), against the danger and every force threatening that place. The bridge now copies the militia's strength and the walls' level for the realm's and its allies' castles and towns.
  - **The danger rule:**
    - shelter in the first castle or town the lord reaches before the enemy (by both sides' speed) that would hold, even the one the enemy is marching on;
    - if none would, fall back toward the nearest one it reaches first that is farther from the enemy (never the one the enemy marches on), stopping outside; if none, straight away from the enemy. It reconsiders every hour.
    - A lord inside a place that would not hold now leaves; in a village it is treated as in the open.
  - **defend** shelters inside its own holding when the defence would hold (even to reinforce it under siege), else in the nearest place that would; `stay_inside` 1 now means "inside whatever the odds".
  - **garrison** gets `stay_inside` 1, so a garrisoning lord never walks out.
  - The agent is told which it did and why, with the numbers.
- **Checks:**
  - the smoke test passed in full (109 checks; 3 new, 4 rewritten for the new rule) and the war room's self-test too (38);
  - 351 game references, 0 missing;
  - the installed DLL has sha256 `7908EFDBAA786E4A…`.
- **Outside the repository:** `scripts/setup-agent.ps1 -Mode live` rewrote `tools.txt` (the danger rule's and defend's descriptions, garrison's new setting). The war room was not running and was left off.
- **Not yet seen in a game:** a lord running from a place that would not hold, or reinforcing one that would.

### 15:22 to 15:47 · A live session on 0.6.2 (the Summer 9, 1087 save)
- **What happened:**
  - the bridge loaded cleanly (self-check 9 of 9; one 27 s stall while the save loaded, not ours); 70 in-game hours, Summer 9, 09:00 to Summer 12, 06:00; 40 pokes, 16 answers, 3 councils of war, 4 stories; fog check 0 leaks in 110 reports;
  - the war room was not running when Mert asked how the agent was doing; Claude started it at 15:37:59;
  - **a mistake of Claude's:** a `WarRoom once` run by hand beside it answered the first poke (#133) too, and the bridge carried out both answers. The opening was a mix of two plans: two of the agent's army calls were refused, and its 8-lord siege of Eastern Moria was split between two smaller armies. The agent recovered within a few game hours, but the run is not a clean test. 0.6.3 makes it impossible (below);
  - **the shelter rule of 0.6.2, seen working:** Othrod sheltered in Gramburg (defenders 1,207 behind walls worth 2.4, against 220); Morzash went inside Starkmoore from 3,675; Naglur stayed inside Eaworth from 8,501;
  - the agent massed three armies against Rohan's raiders, beat Amalgun and Galenor, and lost Othrod, Krumbash, Mughrim and Urshza to capture, each marching alone through the raiders' ground (and Sorgard of Woses to Corsairs of Umbar);
  - in the north it called off its siege of Eastern Moria when Aragorn's army (5,532 men, strength 14,695) was seen heading for Western Moria, and set two armies to defend it.
- **Found in the logs** (fixed in 0.6.3):
  - Party AI Controls switched the game's AI back on for held lords standing still, 40 times; orders were given again 92 times, 8 fought over, 6 "stuck";
  - defend's refill rule sent nearly every defender off to recruit: the realm's lords stood at 25% of their size limits on average, against a floor of 40%. Both armies sent to defend Western Moria left it this way (Gashnar's two days at 31% in Western Moria, then a village; Vrakdush's to Naerondol, 35 hours away);
  - the report's list of parties in view hit its cap of 40 in every report (30 to 97 cut, 11 to 24 of the 40 shown being bandits) and cut Aragorn's army after its one sighting, though the realm kept seeing it;
  - the report never said what a castle would hold against, so the agent could not match a defence to an attack;
  - a lord sent to stand near a far place had its order given again every check until it got there; one call-back named a party by its code (`lord_4_18_party_1`).
- **Western Moria against Aragorn, by the game's assault test:** garrison 767 and militia 280 (an estimated 1,500 strength; its wall level was not in the report) behind walls worth 2.4 to 3.2 hold against about 3,600 to 4,800, against Aragorn's 14,695. With both armies inside (about 2,000 more) it held against about 8,400 to 11,200, still too little. The muster showed 15 lords, strength 5,157, within two days, which would have held at any wall level. Aragorn never came into sight of Western Moria in the 70 hours played.

### 16:10 · Version 0.6.3: held lords kept held, refilling only when not needed, every enemy army in the report · commit b6e2a9f (installed 16:10)
- **Why:** the live session above. Mert, on refilling: "some lords might be needed urgently and may be weak individually but massing them together still makes them strong. It should be able to order them to mass together and fight if it needs to rather than being forced to recruit. It's still a good idea for lords to be recruiting if they are not needed urgently."
- **What:**
  - **Held lords kept held:** a hook right after Party AI Controls' hourly look at a party (`PartyAIThinker.OnHourlyTickParty`) holds again a lord one of our orders holds. That look switches the game's AI back on for every lord it does not manage that stands still with the AI off (read in its decompile); the game puts lords into that state in some 36 places, so our own moves could not avoid it. A new self-check says whether the hook attached.
  - **defend refills only when the lord is not needed:**
    - never an army's leader: the agent massed it for a reason;
    - never while the holding is besieged or an enemy lord or army is within twice the radius (bandits do not count);
    - it breaks off when enemy lords come near, leaves a place that gives fewer than 5 points of ranks in 12 hours (trying again two days later), and comes back after two days at most;
    - `floor` 0 still means it never leaves to refill.
  - **The report:**
    - the list of parties in view is cut allies and neutrals first, then bandits, then enemy lords, and never an enemy army;
    - a sighting and the last-seen list give an army's strength;
    - each castle and town shows its wall level, its defenders' strength (garrison, militia, the lords inside) and the besiegers' strength it holds against, with one line on the game's test (above it, besiegers may storm at any hour; below, they wait, but the bar falls after four days of siege, to the defenders' bare strength by about the sixth day).
  - **Smaller:**
    - a lord sent near a far place is on course while it heads there;
    - a call-back names a party that is gone by its name;
    - `WarRoom run` holds a lock for its agent folder, so a second `run`, `once` or `council` refuses to answer beside it.
  - **Not changed:** the agent's instructions. Everything new is facts and order rules; nothing of Mert's own plan.
- **Checks:**
  - the smoke test passed in full (115 checks, 6 new) and the war room's self-test too (38);
  - 352 game references, 0 missing; Party AI Controls' names (`PartyAIThinker`, `OnHourlyTickParty`) are in its installed `PartyAIUpdated.dll`;
  - the lock tried by hand: `once` and a second `run` both refused beside the running war room;
  - the installed DLL has sha256 `1947ADBAF5338759…`.
- **Outside the repository:**
  - the war room was stopped at about 16:05 and 16:09 (idle, the game closed) for the builds, and restarted at 16:10:32 as 0.6.3;
  - an install at about 16:06 still carried the number 0.6.2 and was replaced at 16:10 by the same code numbered 0.6.3;
  - `scripts/setup-agent.ps1 -Mode live` rewrote `tools.txt` at 16:13 (defend's new refilling); the bridge's settings were unchanged.
- **Not yet seen in a game:** the hook attaching and holding a lord; a refill broken off; the new report lines.

### 16:56 · Version 0.6.4: each enemy army's trail, and a 15th principle of doctrine · commit 5c8950d (installed 16:55)
- **Why:** Mert: "In an army, I might deviate slightly off course on my way to an objective to recruit or gather supplies, or maybe to engage an enemy I find vulnerable. The AI should still be getting the information it needs to infer what my main objective is, and slight deviations should not have it readjust its main strategy." Of seven ways proposed, Mert chose two together: "Lets try 6 and 1 at the same time."
- **What:**
  - **Each enemy army's trail** (proposal 1): a new report section, ENEMY ARMIES' TRAILS, gives, for each enemy army, where the realm saw it in the last two days, oldest first: when, where (and how far from the realm's nearest holding), and what it was doing.
    - A point is added each hour the army is in view that its course changed, after every 10 map units of the same march, every 6 hours, or when it comes back into sight ("(out of sight 4 h)").
    - 12 points at most; only trails of two points or more are shown; a trail ends when its army is seen broken up, or is gone.
    - Only what the realm saw, so the fog rule holds. The enemy's look-up file carries the same trail, and the view's JSON a `trails` list.
  - **Principle 15 of the doctrine** (proposal 6): "Read the enemy's aim from its road, not its errands", grounded in one research pass: Edward III's letter of 1339, *On Skirmishing* 8 and 9, and Philip VI misplaced south of Paris while the English crossed the Seine at Poissy in 1346 ([DOCTRINE.md](DOCTRINE.md#principle-15-oct-16-reading-an-armys-aim), with the caveats). The research corrected the first draft: the sources show the army's parties ranging wide while its main body keeps its road, not the army turning aside. The principle is general; it names nothing of this war or of how Mert plays.
  - The instructions' list of the report's sections names the trails.
- **Checks:**
  - the smoke test passed in full (118 checks, 3 new: points for changes of course and a return into sight, none for a few more units of the same march; the look-up's trail; the two-day limit, and a trail ending with its army) and the war room's self-test too (38);
  - 352 game references, 0 missing;
  - the installed DLL has sha256 `42A4553B47417781…`.
- **Outside the repository:**
  - the war room was stopped before the builds (idle, the game closed), and restarted at 16:56:02;
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` (principle 15, the trails in the report's list) and rewrote `tools.txt`; the bridge's settings were unchanged.
- **Not yet seen in a game:** a trail; the agent reading one. Proposed for the next run: Mert turns aside once or twice on the way to Western Moria, and we count how often the agent's plan changes.

### 19:32 to 19:45 · Live session on 0.6.4 (standard save, Mert marching on Western Moria)
- **The war room** was not running when the game started: the one started from an earlier Claude session had stopped with that session, and pokes #173 to #176 went unanswered. It was restarted at 19:32:41.
- **Seen working:**
  - 0.6.4 loaded; the self-check passed 10 of 10, and the hook that keeps held lords held attached;
  - no "the game's AI switched back on" alarm and no order given again;
  - the agent set `floor` 0 on Gashnar's defend;
  - #189's siege of Hollin Gate, received while the clock was stopped, was carried out at 19:38:16 when it ran.
- **The army seen:** a caravan saw Aragorn's army (5,532 men, strength 14,710) at Summer 13, 12:00, 63 from Western Moria and marching on it (#217).
  - The answer to #220 sent no relief: "Aragorn (14,710) is marching on Western Moria, which can only hold against 5,964. Send no relief and keep the northern lords out of his way."
  - The council of war (19:43:23 to 19:44:42, 7,462 tokens of thinking) gave one order, Grakmaw to hunt Ulf and then defend Hollin Gate, and a new plan.
- **A gap found:** the army dropped out of the report's SEEN EARLIER list, cut by bands seen since (its cap of 25 kept the most recent). Fixed in 0.7.0.

### 21:10 · Version 0.7.0: the dashboard, ways and crossings, and the agent's "ask the way" tool · commit f80d788 (installed about 21:03)
- **Why:** Mert: "I want to make diagnostics and monitoring by me a lot easier ... a dashboard with a live map of the game. I basically want to be able to see exactly what each agent sees, have everything they see be depicted by icons with a legend, switch between agents, and have a log depicting the moves of the agent I am following on the side. When I click on a given icon on the map, I should be able to see where it is moving." And on pathfinding: clicking an icon should show how the army would find its way, and the agent should know how pathfinding works, "only exposed to it after it specifically asks for it for a specific army". Mert approved the plan ("Perfect. Implement all now.") and all its recommendations: a real tool rather than ready-made pages, no doctrine principle on crossings for now, and the rewind built now.
- **What:**
  - **The dashboard** (`src/BannerlordAgents.Dashboard`, `scripts\dashboard.ps1`, http://localhost:7329/; [DASHBOARD.md](DASHBOARD.md)):
    - the live map on the game's own walking map;
    - "What it sees", built from the very view each realm's report was written from, with what the report cut or only counted marked, and "Everything";
    - a switch between the agent's realm and the player's;
    - icons with a legend;
    - cards with each party's planned way (the game's own) or predicted way (ours), its crossings and its time, and "ask the way from here";
    - the agent's moves on the side, from its pokes, answers, councils, receipts, ended orders, call-backs and questions, one load at a time, with names that select on the map;
    - a rewind over every hour of the newest 12 loads;
    - a Checks panel, and links that open a view.
  - **The bridge:**
    - the game's walking map copied once per map (`MapExport`), in slices: 0.8 ms a frame while the clock runs, 4 ms while stopped, never in battle;
    - the hourly copy adds each marching lord's and caravan's planned corners, where every party walks to, and lords' speed with ground and night;
    - the worker writes an hourly frame, `live\current.json` and the agent's own view file (`LiveMap`), and marks each load's place in the record files (`loads.jsonl`);
    - the console command `agents.map`;
    - two self-checks, "planned ways" and "walking map";
    - the settings `dashboard` and `keep_loads`.
  - **The way-finder** (`src/BannerlordAgents.Routes`; [ROUTES.md](ROUTES.md)):
    - the shortest way over the walking map, as the game's pathfinder takes it, pulled into straight legs;
    - march times with the game's slowing by ground and night;
    - the crossing atlas: bridges, fords and passes on the ways between neighbouring holdings, each closed in turn to see what it costs, the critical ones marked;
    - the words shared by the cards and the tool.
  - **The agent's "ask the way" tool** (`RouteTool`): a real tool in each fast answer and each council, served by the war room inside the call (an in-process MCP server), which Claude Code's safe mode keeps.
    - It reads only the realm's view file, so it knows no more than the realm saw.
    - It answers with the way, the hours at the army's pace (an enemy's from its trail), the ground, the crossings with what closing each would cost, the narrowest place, and optionally who gets there first.
    - Its first answer in a call says how marching works (also `marching.txt`).
    - Every question is logged (`routes.jsonl`, and `ways_asked` in `calls.jsonl`).
    - The instructions gained a short "Asking the way" section, removed whenever the tool is not offered. It describes the tool only, with nothing on what to do with crossings.
  - **The accuracy check:** the dashboard compares our ways with the game's own planned ways for marching lords, under three guesses about the engine's unknown extra cost, and measures the slowing on each ground from the lords' speeds (`accuracy.jsonl`, `speed_factors.json`).
  - **Fixed:** SEEN EARLIER now cuts in the same order as IN VIEW and never cuts an enemy army.
- **Checks:**
  - the smoke test passed in full (126 checks, 8 new);
  - the war room's self-test passed (46 checks, 8 new: the tool's protocol, a lord over the made-up bridge, an enemy at its trail's pace and who first, an unseen party refused, the log, the instructions with and without the tool, `marching.txt`);
  - the dashboard's self-test passed (23 checks: the way-finder, the bridge and pass found critical, timing, words, the picture, the log by load);
  - `WarRoom toolcheck`, one real call with a small model through the whole path, passed: it asked the way once and answered with it, in 3 s;
  - 382 game references, 0 missing, and the two private fields read by name are in the installed game;
  - the page was looked at in a headless browser on the smoke test's frames and a made-up map, in both themes;
  - the installed DLL has sha256 `4186496129CC33D9…`.
- **Outside the repository:**
  - the war room was stopped at about 20:47 (idle, the game closed) for the build;
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` (the "Asking the way" section) and rewrote `tools.txt` at 21:04;
  - the war room started as 0.7.0 at 21:04:39 and added `route_tool = on` to `warroom.txt`;
  - the dashboard was started at 21:04:39 on port 7329, both detached from Claude Code's session so they outlive it;
  - the bridge writes its two new settings into `settings.txt` at its next load.
- **Not yet seen in a game:** the map copy (its time, and whether north is up); the frames' real size; the accuracy check's numbers; the atlas on the real map; the agent asking the way.

### 21:15 to 21:45 · First session on 0.7.0, and two fixes · commit 7c61e4d (war room and dashboard rebuilt; the game module unchanged)
- **Seen working:**
  - the walking map copied in 4 s (272 ms of the game's thread over 68 frames): 1,638 x 1,550 cells, 147 bridge and ford faces;
  - the self-check passed 12 of 12;
  - the dashboard showed the live map; Mert: "Working phenomenally."
- **Found 1, the war room froze** on every call with the new tool (a council at 21:18:16 and poke #222 at 21:18:47; pokes #222 to #250 went unanswered for about 15 minutes, the first answer after the load, #221, having come before the tool was offered).
  - The cause: the war room wrote the message on standard input while Claude Code was busy with the tool's handshake. A message of a real poke's size (35 to 48 KB) filled the pipe, the tool's answer queued behind it, and both sides waited for good. The call's own time limit never came into play, as the write came before it.
  - The 3 s check on Oct 16 had used a short message. `WarRoom toolcheck --big` (a 50 KB message) reproduced it.
  - Fixed: standard input has a writer thread of its own fed from a queue, and the message goes only after Claude Code has answered the handshake. `toolcheck --big` now passes in about 4 s, and a plain call (the chronicler's kind) also passes.
  - The stuck calls and the war room were stopped at about 21:31; it was restarted at 21:33:28, and again at 21:41:07 with the fix below.
- **Found 2, our copy of the map had false walls.** On the first real map, about one in four of the game's own planned ways came out much longer on ours, some with no way at all: bridges thinner than a cell fell between the samples.
  - Fixed: each bridge and ford is rebuilt bank to bank from its face's centre, over water only, and the map learns from the game's own planned ways in every frame (`learned.bin`). The war room's tool reads the map again when it learns, and the atlas is worked out again once it has learned 300 cells more.
  - Measured held out (`Dashboard learncheck`: learn from the even hours, test on the odd), on 306 lords' ways:
    - as copied: 39 over 20% too long, 56 with no way;
    - bridges rebuilt: 32 and 8;
    - rebuilt and learned: none over 5%, median gap 0.4%.
  - At 21:40:53 the dashboard learned 2,143 cells from the 68 hours kept, and the atlas was worked out again (233 crossings, from 523 ways where the first copy found 368).
- **Mert's question, checked against the night's frames:**
  - The agent set Gashnar to "screen Western Moria" to "find Aragorn's army after Hollin Gate".
  - The game's own way from Hollin Gate to Western Moria runs about 260 map units north round the mountains, past Caradhras, although the two are 53 apart as the crow flies. Aragorn's army took it.
  - Gashnar's screen never went more than about 15 from the town, because the ground within its radius is mostly mountain.
  - The agent has no order to post a lord on a road or to shadow an army, and its report gives distances as the crow flies.
- **Checks:**
  - the dashboard's self-test passed (26 checks, 3 new: a river closed without its bridge, a bridge rebuilt from its centre, a way learned across it);
  - the war room's self-test passed (46);
  - `toolcheck --big` passed.

### 22:10 · Version 0.7.1: watch, shadow, and every distance by road too · commit 359149f (installed 22:09)
- **Why:** Mert, on the agent screening round Western Moria to find Aragorn's army: "It would be a much smarter move to send a weaker lord to scout on the road to western moria from hollin gate to keep tabs on Aragorn's army ... I think the AI is smart enough to do this, but just doesn't have the tools to do so."
  - Checked against the night's frames: every order that places a lord was tied to a holding, and the report gave distances as the crow flies (Hollin Gate to Western Moria 53, by road about 260 north round the mountains).
  - Mert: "Implement 1 and 2. In the report, distances should be given both as the crow flies and the actual travel distance."
- **What:**
  - **watch** (a spot or a holding): the lord goes to its post and stands there. It reports when it takes up the watch, each enemy army within `watch_radius` (12) with its strength, what it is doing and where it is, and again when that army has gone out of sight; with `also_lords` 1, enemy lords too. Its caution makes it fall back and return.
  - **shadow** (an enemy lord or army): keeps about `keep` (8) map units from its target, its point worked out again every hour. It reports at once, when the target heads for a new place, and every `report_hours` (6). Out of sight, it searches where the target was last seen and gives up after `lose_hours` (8). It falls back if the target turns to chase it.
  - **Spots** (`p673_994`): the "ask the way" answers name the spot of each crossing, of the narrowest place, and of up to 6 points along the way. The bridge checks a spot is walkable ground (else the nearest walkable within 4, or refuses). The instructions' "Asking the way" section says a watch can be posted on one.
  - **Distances by road:** everywhere the agent reads a distance (the report, the sightings, the trails, the facts, the look-ups), it reads "63.0 (260.0 by road)", the road figure being the game's own.
    - The game's table of road distances between holdings is copied at load; each party's entrance is copied hourly.
    - The nearest holding is the nearest by road, the poke rule's reach is by road, and the facts' travel hours are by road.
    - The report's header says what the two numbers are.
  - **The dashboard** draws each watch's post, tied to its lord.
  - New in the order language: the moves `post` and `shadow`, the name `now`, and the functions `doing_of`, `toward_of` and `where_of`.
- **Checks:**
  - the smoke test passed in full (145 checks, 7 new for watch, shadow and spots, 7 for road distances);
  - the war room's self-test passed (46; the answer names the bridge's spot and the spots along the way);
  - the dashboard's self-test passed (26);
  - 390 game references, 0 missing;
  - the installed DLL has sha256 `C4A6D0851389AB80…`.
  - Found while testing: a rule's `set` is made before its call-back is filled in, so watch's "gone out of sight" first named "none". It now keeps the army under its own name.
- **Outside the repository:**
  - the war room and the dashboard were stopped at 22:08 (idle, the game closed), and restarted, detached, at 22:09:45;
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` and wrote `tools.txt` with 19 orders.
- **Not yet seen in a game:** the road table's size and copy time at load; the hourly cost of the entrances; a watch or a shadow in play.

### 22:48 to 23:51 · First live session on 0.7.1 (the standard save; Mert marched on Western Moria)
- **What happened:**
  - at load the game's road table was copied: 829 holdings, 110,316 pairs, in 100 ms; the self-check passed 12 of 12;
  - the agent asked the way 14 times (councils and fast answers alike), among them how Aragorn's army reaches Western Moria: 234 by road against 52 as the crow flies, about 183 hours at its trail's pace. Its plan used it: Aragorn "marched the long road west", leaving Eastern Moria and Hollin Gate thinly held, so Naglur's army struck east;
  - no watch or shadow order was given; the lords on Aragorn's road were a screen round Western Moria and one round Caradhras, ordered before the first question;
  - Naglur's army (12 lords, 1,930 men) took Galenor prisoner (Summer 12), was ordered on Hollin Gate, chased and took Vidar on the way, laid siege on Summer 18 at 14:00, and was stormed into the castle's defence on Summer 19 at about 17:00. About 50 of Arnor's lords had turned toward Hollin Gate within six hours of the siege, none in an army; 21 of them were in the battle, strength about 7,700, when the game closed, Naglur's army down from 3,456 to 1,621. The siege's relief alarm never rang (it waited for an army);
  - Aragorn took Western Moria on Summer 20.
- **Numbers:** 135 pokes, 90 answers, 89 orders; answer time median 14 s, largest 89 s (the game kept running: about 1 in-game hour every 3.5 seconds, so a 30-second answer is about 9 in-game hours). The hourly copy cost 6.4 ms on average (294 copies), against 7.0 in the 0.7.0 session: the entrances copied hourly for the road distances added nothing measurable. Fog check: 429 reports, 0 leaks.
- **Freezes:** one of 221 seconds at 23:29 to 23:33, in a battle or scene, the game's thread waiting, none of our code on it (probe files `freeze-232932.txt`, `freeze-233112.txt`, `freeze-233318.txt` in the session folder); not looked into further.
- **Where it lives:** `%LOCALAPPDATA%\BannerlordAgents\logs\sessions\20261016-224809`, the frames in `live\Ah2auppicLLJ\20261016-224917-997`.

## Saturday, October 17

### 00:30 · Version 0.7.2: the siege alarm counts every lord coming, enemy castles' militia, doctrine principles 16 and 17 · commit 81e7399 (installed 00:28)
- **Why:** the siege of Hollin Gate (the session above). Mert: "Why did the agent go through with the siege of hollin gate even though it currently has a lot of defenders?" The defenders had come after the storm began: about 50 of Arnor's lords, one by one, none in an army, so besiege's alarm (an enemy *army* only) never rang; and the agent was told the castle's garrison, not its militia. Mert: "Make fixes 1 and 2. The rest is more of a siege doctrine question. It should (1) post scouts around the army to figure out if a relieving force is coming during the siege so it can call it off and (2) understand that when the enemy is stronger, it is important to strike hard and quick, sometimes forgoing smaller targets in favor of guaranteeing a more important hit. I also feel like its siege of hollin gate should have been a lot quicker than the amount of time it took. Can you look into why please?"
- **What:**
  - **besiege's relief alarm** counts every enemy lord and army in view within `relief_radius` of the holding or seen heading for it from anywhere, and those seen so in the last 6 hours, all together (`World.ReliefComing`); it calls back at `relief_ratio` times the besieger's strength and again each time they have grown by half its strength, naming them nearest first with strength, both distances and what each is doing. Still a call-back only. New functions `relief_coming`, `relief_count`, `relief_of`.
  - **Enemy castles' militia** in ENEMY HOLDINGS NEAR YOUR LANDS ("garrison 94, militia 100"), only where the realm's eyes reach; out of sight the view file now holds -1 for it too, and the fog check checks it. The dashboard's card says "not in sight" likewise.
  - **Doctrine principles 16** (a besieger keeps eyes on the roads: Kekaumenos, the *Strategikon*, Mountsorrel and Lincoln 1217, Calais 1347, Antioch 1098, Nicopolis 1396) **and 17** (the weaker side's one blow, and when to wait instead: the *Arrivall* of Edward IV 1471, Lincoln 1217, the *Strategikon*, Kekaumenos, Vegetius), from a research pass with the texts quoted ([DOCTRINE.md](DOCTRINE.md#principles-16-and-17-oct-17-the-besiegers-eyes-and-the-weaker-sides-one-blow)). Principle 2's last sentence points to 16. Neither names this war.
  - **Where the siege's hours went** (Summer 12 21:00 to the storm on Summer 19 about 17:00, about 165 in-game hours), read in the frames and the 1.4.8 code ([TOOLS.md](TOOLS.md#besieging-the-relief-alarm-counts-lords-and-the-castles-militia-shows-since-072)): about 10 hours before the order reached Naglur (the council's 70 seconds of thought, the clock running); about 80 marching about 94 by road (62 as the crow flies) at about 1.25 an hour, a 12-lord army of 1,930 men walking at 4 × (200/2,130)^0.4 ≈ 1.6 before nights and forest; about 42 on the agent's chase after Vidar (ordered 9 hours after the sighting, as the army reached the castle; Vidar was taken) and the walk back; about 29 in the siege camp, the game's own fixed work (48 man-days of preparations at √2,000 a day, about 26 hours, then one engine, then a roll). Not changed: these are the game's rules and the agent's choices. Not yet given to the agent as facts.
- **Checks:**
  - the smoke test passed in full (152 checks; 7 new: the alarm silent under the bar with a far idle lord not counted, ringing at 2,800 against 0.8 × 3,400 counting lords, naming them, not again for the same relief, again at 4,600; militia in sight shown, out of sight neither shown nor kept);
  - **replayed on the real hours** of Oct 16 (only the parties the orcs had in sight): the new alarm would have rung at about h2192325, 7 hours after the siege was laid (5 lords, strength 3,093, against 3,427) and about 22 hours before the storm;
  - the war room's self-test passed (46), the dashboard's (26); 390 game references, 0 missing; the installed DLL has sha256 `1715B73430935CE6…`.
- **Outside the repository:**
  - the war room and the dashboard were stopped at 00:12 (idle, the game closed) and restarted, detached, at 00:31:19;
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` (principles 16 and 17) and wrote `tools.txt` with 19 orders.

### 01:55 · Version 0.7.3: the game's clocks (marches by the game's exact speed rule; sieges and battles run ahead) · commit 66226fe (installed 01:48)
- **Why:** Mert: "We should be adjusting our prediction of the marching speed of a force based on the game's exact equations, so the ai has a good idea on where its own/enemies' armies will reach their waypoint. We should do the same about siege and battle timers so it can plan ahead."
- **The research** (three passes on the 1.4.8 code, the decompiled TAOM 2.0.26, and the pack's CheatsForBannerlord, ImprovedGarrisons and TFAOMFixes; [TOOLS.md](TOOLS.md#the-games-clocks-marches-sieges-and-battles-run-ahead-since-073)):
  - the models in force: TAOM's speed, siege event, combat simulation and military power models (the last registered wins);
  - **a party covers its speed / 1.2 map units an hour**, and every factor, the ground and the night included, adds into one sum (base x (1 + sum), at least 1). Our old reckonings took the speed as map units an hour (some 20% too quick) and multiplied the ground in;
  - TAOM adds its peoples' ground and night bonuses; a siege camp's preparations (48 man-days at √men a day), its engines one at a time, a storm rolled about 3 times an hour by the game's formula; a storm fought one round an hour (two in any other battle), the attackers' blows growing only with the defenders' number.
- **What:**
  - `src/Common/GamePace.cs`, the marching rule, one file built into the module and the way-finder;
  - the hourly copy reads each lord's speed sum from the game's own cache, the ground under it, its people, its scout's perks, snowfall; once a load the speed model, the peoples' bonuses (from the game's feats) and the perk values; every siege and every battle with their numbers; the whole planned way (128 corners) of every marching lord;
  - what the rules leave unexplained in a lord's speed is measured hourly and carried (on Oct 16 about +0.19 a lord, likely the scout's Uncanny Insight), sampled as `march:unexplained`;
  - the report: arrival times for the realm's lords and for lords in view ("there in about 52 hours (Summer 12, 04:00)"), "if it kept on" for those seen earlier, and a new **SIEGES AND BATTLES** section with each siege's and battle's clock and how the game runs them; the facts', look-ups' and `hours_to` travel hours by the same rule; the "ask the way" tool and the dashboard's ways from each party's speed sum, the marching notes with the game's numbers and the peoples' bonuses;
  - `Dashboard etacheck`: the rule against the arrivals in the frames.
- **Checks:**
  - **on the real hours** (Oct 16's load, 438 marches to a holding with their arrival seen, the base parts worked out from the men since those frames predate 0.7.3): median error 4.2% (+1.4% signed) against 9.0% for the old sum and 6.8% for the old way-finder; marches of 12 hours or more: 2.5% (+0.5%) against 8.8% and 6.1%. 301 lone lords moved 0.81 of their speed in an hour;
  - the siege clock on the code's worked example (2,000 men, a level-1 castle): preparations 25.8 hours, a ram at 32.3, a storm more likely than not by 49.8 hours (the research pass: about 50); a storm of 960 against 4,500: 28 strikes a round against 58, the attackers losing about 20 men an hour (Hollin Gate: about 25);
  - the smoke test passed in full (168 checks; 16 new), the war room's self-test (46), the dashboard's (27, 1 new); 477 game references, 0 missing; the installed DLL has sha256 `BF050D93806A44DC…`.
- **Outside the repository:**
  - the war room and the dashboard were stopped for the build (idle, the game closed) and restarted, detached, at 01:53:40;
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` (the report's arrivals and clocks named) and wrote `tools.txt` (19 orders); `marching.txt` is rewritten at the first answer of the next game.
- **Not yet seen in a game:** the new copy's cost on the game's thread; how much of each lord's speed the named rules explain with the real base parts; a siege's or a battle's clock against what then happens.

## Tuesday, October 20

### 17:30 · Version 0.7.4: siege control (the agent's storm rule and relief bars) and battles run ahead before they begin · commit fd7b47e (installed 17:28)
- **Why:** the walk-through of the Hollin Gate defeat under 0.7.3 left five gaps: no time for a storm or a field battle before it begins; the agent sees only the relief its own lords see; the storm is the game's choice; an answer comes about 10 game hours after the alarm; the enemy's morale is guessed. Mert: "We should fix all gaps now. Gap 1 is the most important, and we should give time estimates both for siege battles and for field battles. Touching on 3: I wonder if there is any way we can give the AI a tool to dictate siege decisions, starting with the storm time. It can be gated on storm immediately, storm after x siege engines constructed etc. based on its understanding of the enemy garrison's strength and the imminence of relief." And: "For gap 4: the agent should set the bar here - what relief force strength level forces a withdrawal, what relief force level forces an immediate storm? it should set the bar as it begins the siege, prematurely, and the logic should run as part of its siege tool."
- **What** ([TOOLS.md](TOOLS.md#siege-control-the-storm-rule-the-relief-bars-and-battles-run-ahead-before-they-begin-since-074)):
  - **the storm rule** (gap 3): besiege's `storm` (0 the game decides, 1 storm once ready with `storm_engines` built and the besiegers `storm_ratio` times the defenders, 2 do not storm), handed hourly from the siege stage to `StormGate`, which a Harmony prefix on the game's storm test (`BesiegerCamp.StartingAssaultOnBesiegedSettlementIsLogical`) answers from for that camp; every other camp keeps the game's own test. The game asks it only once the camp's preparations are done, so no rule storms earlier, as the player cannot. The second hook that changes the game's behaviour; a new self-check "storm rule";
  - **the relief bars** (gap 4), set with the order and acted on the hour they are met: `withdraw_on_relief` (lift the siege, march into the nearest castle or town of the realm, then stop there, its next order not started: the new ending `stop`) and `storm_on_relief` (storm as soon as the camp may), both against the relief due within `relief_hours` (24) by the game's marching rules; the relief list now gives each lord's arrival ("due in about 23 hours"); lords already inside the place are no longer counted as relief; new functions `relief_within`, `prep_hours`, `storm_text`;
  - **battles run ahead before they begin** (gap 1): the game's battle rules in one shared file (`src/Common/GameBattle.cs`) for the module and the war room; "A storm begun now would last ..." in every siege line, counting the lords seen marching in; the siege clock by the agent's own storm rule; a field battle against each enemy in the facts (the nearest lord alone, the three nearest together); the field retreat; and the war room's new `battle_odds` question (field battles and storms, with the camp's preparation time);
  - **morale** (gap 5): the realm's own lords' morale and Tactics copied; the other side's run at 30, 50 and 70 and given as a range;
  - **the relief the realm can know of** (gap 2): a new report section, ENEMY REALMS, AS THEIR ENCYCLOPEDIA PAGES SHOW ANY RULER (each enemy realm's lords and their parties' strength in all, beside the part in sight), and the war room's `roads_in` question (the roads into a holding from the enemy's castles and towns, and a spot on each some hours out with the warning a watch there gives). Facts and mechanics only: neither tells the agent when to scout, storm or withdraw;
  - the frontier's enemy castles count only the lords inside that are in sight; the hourly copy keeps garrison and militia strength and wall level for every castle and town (the agent still sees another's only where its eyes reach).
- **Checks:**
  - the smoke test passed in full (178 checks; 10 new: the storm rule handed over in the siege stage; the storm bar met by a relief of 2,000 due within the day, with the camp's hours to ready; the withdrawal bar at 5,000, the rule cleared, the march into Orc Keep; the stop inside without the next order; the clock by rule 1 (the ram at 32.2 hours) and rule 2 (never); the rule in the siege line; a storm begun now, 2,086 against 194 behind level-1 walls, about 60 hours (60 to 67), and held by the defenders with 4,000 men marching in; ENEMY REALMS, with the fog check; a field battle in the facts). One 0.7.3 check was reworded for the siege line's new sentence order;
  - the war room's self-test passed (50; 4 new: the three tools listed, field odds, storm odds with the preparations, roads in), the dashboard's (27); 480 game references, 0 missing; the storm test read in the 1.4.8 code (`IsReadyToBesiege`, `MobilePartyAi.GetBesiegeBehavior`); the installed DLL has sha256 `0C8D89D4540A0928…`.
- **Outside the repository:**
  - the war room and the dashboard were stopped at 17:24 for the build (idle, the game closed) and restarted, detached, at 17:28:43;
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` (the two new questions named) and wrote `tools.txt` (19 orders, besiege with its new settings).
- **Not yet seen in a game:** the hook attaching under the pack (the self-check says); a storm by the agent's rule; the relief bars on real reliefs; a run-ahead storm or field battle against what then happens.

### 22:15 · Version 0.7.5: what the first evening on 0.7.4 taught; orders for lords in your army; the fairer test save · commit 1f53891 (installed 22:12)
- **Why:** the first session on 0.7.4 (the save besieging Western Moria, 21:14 to about 21:40). Mert picked "C" for his own siege: the report keeps treating his camp like any other (the agent is not told which lord is the player). Then: "We should fix the other problems as well." Then, on Naglur: "the agent chose to lift the siege but is still stuck in the storm ... Please check its logs and diagnose why the mismatch happened." And: "Go back to the part where I give orders to MY lords. I should be able to give lords who are in MY army, and once I have given them an order, they should first leave my army and then execute their order." And: "I want a fairer, longer term test save where the misty mountain orcs are of more even strength to my kingdom ..." (the line he chose: only the old orc fortresses; their garrisons swapped to orc troops; he reaches army lords from the army tab's portraits).
- **What the session showed** ([TOOLS.md](TOOLS.md#what-the-first-evening-on-074-taught-since-075)):
  - the start-up checks passed (13 of 13), the storm hook attached, no errors; the agent set storm rules and withdrawal bars on its own and asked the new battle-odds question once;
  - the fog check flagged all 46 reports: SIEGES AND BATTLES listed every ally's siege, storm and raid far off;
  - raids with nobody left to defend were "won in about an hour" every hour;
  - Naglur stormed Hollin Gate at 07:50 by the agent's own rule (3,457 against 312); at 13:47 the withdrawal bar was met and the order announced the siege lifted and gave the march, which the game took but cannot carry out until the battle ends (the siege and storm went on). The bar was met only because two lords walking to a waypoint 8.6 ahead were counted "due in about 3 hours", 96.5 away by road (without them 3,047 against the bar of 4,073);
  - the storm's forecast at its start (defenders win in 3.4 days, the orcs losing 16 an hour, 7 relief lords joining from the first hour) against the frames: the orcs lost about 7 an hour, kept morale 100, and had worn the defenders from 197 to 65 after 22 hours; no relief lord joined (Asta stood 5.5 off throughout);
  - Latif's 7.2 days to Starkmoore was right (488 on his way; the agent read his distance to his nearest holding as the distance to Starkmoore), but after his release the report said "there in about 3 hours" (a walk to a point 7 ahead);
  - the hourly copy cost about 11 ms an in-game hour against 6.4 on Oct 16 (the parties part 7.7 ms against 3.2).
- **What changed:**
  - the tool language's `in_battle`; besiege's two bars wait while the lord is in a battle; `storm_text` says when a storm is being fought; a receipt says when the lord is in a battle;
  - arrivals at a holding along the game's way only when it ends there, else by road at the lord's pace (`Forecast.ToHolding`), in the relief lists and bars, battle joiners and the report, which now says what is left to go;
  - joiners only lords walking in; battles under way two rounds or more run ahead at their own measured rates (`Forecast.Measured`, a blow factor per side in `GameBattle`);
  - raids by the village's health (copied hourly as `HitPoints`): what is left, the fall an hour, when it is looted; "led by the village";
  - SIEGES AND BATTLES: the realm's own and what it sees only; the fog check allows the places its eyes reach;
  - cost: lords inside a holding skip the ground, weather and scout reads; a troop's power asked once an hour per kind; the hourly copy's parties part timed in three;
  - `agents.rebalance [apply]` (`TestSetup`, [FIRST-TESTS.md](FIRST-TESTS.md#the-fairer-test-save-agentsrebalance-since-075)): the five old orc fortresses to the orc clans with the fewest holdings (towns first), their garrisons to orc troops tier for tier, the parties inside at war with the orcs moved out, sieges of them ended, and every orc lord held prisoner released; it shows first and refuses during a battle there.
- **Checks:** the smoke test passed in full (186 checks; 8 new: a waypoint walker due by road, 23.5 hours for 60, a walker along its way 11.3 for 30; the arrival text; the bar waiting through a storm and acting after it; a lord standing off not a joiner; Hollin Gate 8 hours in, at measured rates the orcs take it in about 27 hours more, losing 6 an hour; a raid's looting measured at 2% an hour, 19 hours left; an ally's unseen siege left out); the war room's self-test (50) and the dashboard's (27); 502 game references, 0 missing; the installed DLL has sha256 `2BA7FAEB495092ED…`. `agents.rebalance` and the army orders are not testable outside the game: their game calls were read in the 1.4.8 code and RefCheck finds them all.
- **Outside the repository:**
  - **TFAOMFixes** (`C:\BannerlordArchive\src-TFAOMFixes148\VassalOrders.cs`, backup `VassalOrders.cs.bak-20261020`): "I have orders for you" is offered to a lord riding in the player's own army (no longer only to lords in no army or leading one); once the conversation is over he leaves the army, the influence spent calling him refunded to the player's clan (as Party AI Controls' own LeaveArmy does), then takes the order; his reply says so. Lords in someone else's army are still not offered it. Built at 22:08 with another session's 22:04 changes in the same folder (CrowdAi, Trims, CrushWatch; that session agreed); installed at 22:12 (sha256 `AA07F7D34D934CF3…`, 520 game references, 0 missing) over that session's build `88C452D6…`, kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-armyorders-20261020-2213`;
  - the war room and the dashboard were found stopped after the game closed and were started again, detached, at 22:13:05; `scripts/setup-agent.ps1 -Mode live` wrote `tools.txt` (19 orders, besiege's settings saying the bars wait out a battle).
- **Not yet seen in a game:** `agents.rebalance` on a save; ordering a lord from the army tab; the measured battle rates and the raid clock against what then happens; the cost trims' effect.

### 23:35 · Outside the repository: your armies by order, and raids (TFAOMFixes) · commit 781e9a1
- **Why:** Mert: "Another feature I want for my personal orders, so I can keep up with the AI properly: I should be able to get lords to form armies, and pick which lords should join that army, and then be able to give orders to lords that are the leaders of armies to attack a settlement, defend a settlement etc. with their armies. If I am ordering them to create an army as the ruler, all influence costs for that army should be borne by me, including keeping army cohesion. The army should only disband when I tell it to disband. Independently formed armies should be exempt from all of this, of course." Asked what happens when the influence runs out: "Armies should disband once cohesion and influence run out." Raids: "Add raiding villages too".
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: new `VassalArmies.cs`; `VassalOrders.cs`; one line each in `SubModule.cs` (arming, saving) and `Annex.cs` (the hourly look); the project file; backups `*.bak-20261020`). In "I have orders for you":
  - **Raise an army.** Pick the lords (nearest first, each with the game's own calling price), then the fief where they gather; the leader waits there. The ruler needs the influence for the calls. The game charges each call to the leader's clan; it is refunded and charged to the ruler. **Call more lords** does the same for a raised army. **Disband your army** ends it; its leader then acts as he sees fit.
  - **Besiege one of their castles or towns** and **Follow me** (Party AI Controls' own orders), and **Raid one of their villages**: not one of its orders, so the raid clears his order there, turns his own decisions off and sends him at the village. When it is looted, an army's leader waits for orders and a lone lord goes back to his own AI. Defend, patrol, wait, recruit and the rest are as before, now with whole armies behind their leaders.
  - **Cohesion:** each hour, below 60, the ruler buys it back up to 100 at the game's own price for that army, or as far as the ruler's influence goes. The leader's clan never pays: the game's own boost (`Army.ThinkAboutCohesionBoost`) is skipped. With the influence gone, cohesion falls and the army breaks up when it reaches 0 (the game's 30 for a lord's army is held back). A warning comes at most every 12 hours.
  - **Break-ups held back** (a prefix on `Army.DisperseInternal`, which every reason passes through; the one-line `DisbandArmyAction.ApplyBy...` wrappers could be compiled into their callers): hunger, no war left, idleness, and the leader's own whim (an "unknown" break-up while the game's hourly decision for a party runs, marked by a prefix and finalizer on `AiPartyThinkBehavior.PartyHourlyAiTick`). Everything else still ends the army: the leader dead or captured, his party gone or disbanding, a marriage into another realm, every lord gone, the ruler's order.
  - Saved with the game (`tfaom_raised_armies`, `tfaom_vassal_raids`). Armies not raised this way are left as the game runs them.
- **Checks:** built clean; 541 game references, 0 missing; every hooked method read in the 1.4.8 code. Not testable outside the game.
- **Installed** at 23:33 (sha256 `87DCBDB120678F5C…`) over the 22:08 build (`AA07F7D3…`, kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-vassalarmies-20261020-2335`). The other session working on TFAOMFixes (crowd AI trims) was told; none of its files changed.
- **Not yet seen in a game:** all of it. The recorder reads Party AI Controls' orders as before; the army raising and the raids are not yet recorded as the player's decisions.

### 23:59 · Version 0.7.6: the test-save command takes Western Moria too, and no longer gets your heroes captured · commit 0d8852b (installed 23:58)
- **Why:** the first run of `agents.rebalance apply` (23:54, on a new save). Mert: "The command should give what it is already giving them + western moria. It also ended up giving a lot of my lords to them as prisoner ... Can you figure out why and fix that as well please?"
- **Why the heroes were captured:** the game's own rule. When a castle or town changes owner, `PrisonerCaptureCampaignBehavior.HandleSettlementHeroes` (on `OnSettlementOwnerChanged`) takes prisoner every hero staying in it without a party (not the player, a wanderer or a notable) whose realm is at war with the new owner, and sends the lords' parties out. The command moved the parties out but not those heroes (families, lords between parties): 11 were "taken prisoner by" Nanduhirion (9), Caradhras and Sirannon.
- **What:** Western Moria (`town_MM2`) is on the list. Before each handover, those heroes are moved (`TeleportHeroAction.ApplyImmediateTeleportToSettlement`) to their realm's nearest castle or town that is not under siege and not being handed over; a governor first stops governing. The preview counts them.
- **Checks:** the smoke test, the war room's and the dashboard's self-tests passed; 509 game references, 0 missing; the installed DLL has sha256 `1912A0CB25E4C23B…`. The command itself is not testable outside the game. The war room and the dashboard were stopped for the build and restarted, detached, at 23:58:39.

## Thursday, October 22

### 11:26 · Version 0.7.7: the test-save command no longer stalls on the orcs' own battles, and a refusal says so · commit f52ccf5 (installed 11:31)
- **Why:** Mert: "currently, running the rebalance command does nothing." Both runs that morning (11:19, `agents.rebalance apply` on a save at Summer 20, 1087, 07:00) refused and changed nothing. The orcs were storming Hollin Gate (`castle_MM6`, the camp of `lord_MM8_1`, in the hourly frame `h2192359`), and the command would not run while any battle was fought at one of the places or their villages. It said so, but below a first line that read "REBALANCE, CARRIED OUT" whatever happened, and that first line is all the log keeps.
- **What** (`TestSetup`):
  - a battle at one of the places or their villages that the orcs' side is fighting against the player's realm (a storm, a raid) is broken off before the handover, as peace breaks one off (`MapEvent.DiplomaticallyFinished`: no winner, no loot, no prisoners; the storm closes with the siege, `SiegeEvent.FinalizeSiegeEvent`, and a raid at the game's next look, `MapEvent.Update`). The game does the same in `ChangeOwnerOfSettlementAction` when a place passes to its attackers' side. The preview names each one;
  - it still waits while the player is in a battle there, while a garrison sallies out from one (the player's lords would be moved out mid-fight), and with the player's party inside one;
  - a siege of a place by an enemy of the orcs goes on against the new owner instead of being ended;
  - the first line says what happened: "REBALANCE NOT RUN, nothing was changed: <why>", "REBALANCE, WHAT IT WOULD DO", or "REBALANCE, CARRIED OUT: <n> of <n> places handed over, ..."; the whole answer goes to the log as well.
- **Checks:** the smoke test passed in full (no new checks: the command needs the game); 512 game references, 0 missing (`MapEvent.DiplomaticallyFinished`, `IsPlayerMapEvent`, `IsSallyOut`, `IsBlockadeSallyOut` read in the 1.4.8 code). All four projects and `SubModule.xml` are at 0.7.7.
- **Installed** at 11:31, after the game closed (sha256 `2A46E12C9EC59C29…`); the war room and the dashboard were stopped for the build (their self-tests passed) and started again, detached, at 11:31 (dashboard answering on 7329, the war room waiting after poke #418). Settings unchanged: watch mistymountainorcs, agent live, agent_lords all.
- **Not yet seen in a game:** the run through a storm.

### 11:46 · Outside the repository: an army a lord formed himself keeps together while he carries out your order (TFAOMFixes) · commit e81cc9e
- **Why:** Mert asked what happens when they order a lord who leads an army: "I would imagine that the army follows that order or the lord disbands the army to follow that order. I would want the former to be the case." Read in the code: nothing of ours or of Party AI Controls breaks the army up for an order (Party AI Controls pulls a lord out only from an army he does not lead), and an army raised by the player's order already stays together. But an army the lord formed himself was left to all the game's own rules, as asked on Oct 20, and one of them, idleness, undoes most orders. `Army.CheckInactivity` counts an hour of idleness for holding still, travelling to a friendly place, or patrolling, and two back for besieging, raiding or defending a place under siege. After 48 it breaks the army up. Waiting in a fief, defending a fief nobody attacks, patrolling and recruiting all look idle. Asked whether to keep such armies together only against that, or to take them over as raised armies: "Build the smaller fix."
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: `VassalArmies.cs`, `VassalOrders.cs`; backups `*.bak-20261022`): while the leader of an army he formed himself carries out an order of the player's (one given in "I have orders for you", a raid included; `VassalOrders.UnderOrder`, which asks Party AI Controls only about lords the player commanded), the break-up for idleness is held back, and so is his own whim in the game's hourly decision (an "unknown" break-up there; Party AI Controls' own guard on `DisbandArmyAction.ApplyByUnknownReason` sits on a one-line wrapper its callers may have compiled in). Each time idleness is held back, the game's count goes back to 0, so when the order ends he has a fresh two days. Cohesion, its cost and its running out stay his; every other reason still ends the army; nothing changes for armies under no order of the player's.
- **Checks:** built clean (sha256 `434511229FDC618B…`), 543 game references, 0 missing; it still holds the other session's CrowdAi, CrushWatch and FastCores (that session agreed to the build). The game was open; it was installed at 14:15, ten seconds after the game closed (sha256 `434511229FDC618B…`; the build before it kept at `C:BannerlordArchiveTFAOMFixes.dll.148-before-ownarmies-20261022-1415`).
- **Not yet seen in a game.**

### 14:39 · Version 0.8.0: a second agent, for Dol Guldur · commit 74c9c5e (installed 14:37)
- **Why:** Mert: "I now want to give dol guldur an agent as well in the next launch, to see how well I can do against a more evenly matched enemy." Dol Guldur (`dolguldur`, ruled by Khamûl, the Easterling) holds 21 castles and towns, Caras Galadhon and Cerin Amroth among them, and is at war with Gondor, Rohan and the player's realm. Until now the bridge watched one realm only.
- **What:**
  - `watch` takes a list (`mistymountainorcs, dolguldur`). Each realm after the first has its own channel folder, `agent_dir.<realm id>`. The first keeps `agent_dir`, and nothing of the orcs' channel changes.
  - One hourly copy of the world, with each realm's own part beside it (`Snapshot.Seats`: its sight, its enemies, its captives and clans). The Brain runs each realm's hour on its own view: its pokes, numbered on their own (`pokes_<realm>.jsonl`), its report, council briefing, look-ups, chronicle (`chronicle_<realm>.jsonl`), the ids it was shown, its tools and its map view for "ask the way".
  - An answer carries the realm of the folder it came in. `Orders.FromAgent` looks for its lords in that realm only, so each agent can order only its own. Receipts, order news and tool call-backs go back to the order's own channel and chronicle. The order records name their realm.
  - Notices reach every watched realm. The lord watch, the AI-reasons hook and the recruitment log cover both. The self-check checks every realm and folder. `agents.view [realm]` and `agents.chronicle [n] [realm]` show either one.
  - The war room has a `log_dir` setting, so a second one keeps its own logs (`logs\warroom-<realm>`). The dashboard's log reads them, and filters receipts by realm.
  - `scripts\setup-agent.ps1 -Realm` sets up a second agent's workspace from the same templates. Only the three lines that name the realm are rewritten (the title, the opening line, and "Win the war for ...:"). Its `warroom.txt` is the first agent's plus its own `log_dir`.
- **Outside the repository:**
  - the new workspace `C:\Users\karab\Projects\dolguldur-war-council`. Its `warroom-system.md` and `CLAUDE.md` differ from the orcs' only in those three lines: "War council of Dol Guldur", "Dol Guldur, the fortress-realm of the Necromancer in southern Mirkwood ruled by Khamûl, the Easterling", and "Win the war for Rhovanion:". Its `chronicler-system.md` is the same. Its `warroom.txt` is the orcs' plus `log_dir`;
  - `settings.txt`: `watch = mistymountainorcs, dolguldur` and `agent_dir.dolguldur = C:\Users\karab\Projects\dolguldur-war-council\warroom`. Agent live and all lords obey, as before;
  - both war rooms and the dashboard were started, detached, at 14:38.
- **Checks:** the smoke test passed in full, with 7 new checks. Two realms are each poked in their own folder, with their own report. Each report holds only what its own eyes saw. Each realm has its own poke file, hourly report and council briefing. A receipt goes to the channel its order came by, and nothing of it reaches the other realm's chronicle. A realm's orders in force list only its own lords. The war room's (50) and the dashboard's (27) self-tests passed. 512 game references, 0 missing. The installed DLL has sha256 `A766D8F10D995239…`.
- **Also answered:** why the lords near Eastern Moria "keep wandering off" although under orders. They were not under orders. Ufza and Shagash had ridden in Naglur's army, whose members hold no order of their own. When it broke up from lost cohesion (Autumn 5, about 08:00), the game's own AI sent both at once to defend Skar-ghâsh, a raided village by Sirannon, towards Górli's host. The agent's poke at 10:00 was answered 21 s later, but the game ran 5 in-game hours meanwhile and was then paused. Its defend order and the next poke's garrison order were carried out together at 15:00, and from then on both stayed in Eastern Moria.
- **Not yet seen in a game:** two agents at once.

### 17:40 · Version 0.9.0: an agent is a folder; no agent is special; one script runs them all · commit 1d28917 (installed 17:38)
- **Why:** Mert asked how 0.8.0's two agents compare to "adding a new agent should only entail strapping on a new modular component with a new system prompt", with tools reused like function calls and no crossover between brains. The comparison found five gaps:
  - the game-side pieces of an agent were kept in five places, each filtered by realm;
  - the first agent was special;
  - the mode and the obeying lords were shared by all agents;
  - setup overwrote the prompts;
  - adding one took a setup, a war room started by hand, and a naming convention for its logs.
  Mert: "I like all of your change recommendations. You can implement all of them."
- **What:**
  - **An agent is a folder:**
    - `agent.txt` holds `realm`, `mode` (off, shadow, live), `lords` and `channel`, beside its `warroom-system.md`, `chronicler-system.md`, `warroom.txt` (with `log_dir`), channel and memory;
    - the bridge's settings list the folders (`agents = <folder>; <folder>`), and the settings of 0.8 still work without that list;
    - a folder that cannot be used is named in the log and the self-check.
  - **One bundle per agent in the game** (`RealmAgent`, made from its folder at each load by `Agents`):
    - it holds its channel, chronicle, memory of sightings, orders in force (the runner keeps each agent's tools in it, not one list filtered by realm) and latest view;
    - the Brain runs each agent's hour on its own realm's view;
    - orders take the agent's own mode and obeying lords;
    - an agent that is off gets nothing in its channel, while its realm's pokes are still kept.
  - **No first agent:**
    - the sight check and the daily recruitment summary run for every agent;
    - the ids-shown file is per realm (`ids_shown.txt` was renamed for the realm it belonged to, the one listed first);
    - each agent's war room logs are found from its own `warroom.txt`, and the load record names each agent with its log folder (`calls.jsonl@<realm>` offsets), which the dashboard reads.
  - **`scripts\agents.ps1 start | stop | restart | status`** starts a war room for every listed folder, and the dashboard, each detached. `status` shows each agent's realm, process and last line.
  - **`scripts\setup-agent.ps1`:**
    - writes `agent.txt`, keeping values not given;
    - installs the instructions only where the folder has none (`-Force` for fresh copies);
    - gives a new agent another's `warroom.txt` with its own `log_dir`;
    - adds the folder to `agents`.
  - **Console:** `agents.agent [realm] off|shadow|live [lords]` changes one agent's mode at once and writes its `agent.txt`.
- **Outside the repository:**
  - `agent.txt` written in both agents' folders: `mistymountainorcs` and `dolguldur`, both live, all lords;
  - their prompts and `warroom.txt` are unchanged (the same checksums before and after);
  - `settings.txt`: `agents = C:\Users\karab\Projects\orc-war-council; C:\Users\karab\Projects\dolguldur-war-council`;
  - the war rooms and the dashboard were stopped by the new script for the build, and started by it at 17:39.
- **Checks:** the smoke test passed in full, with 9 new checks:
  - an `agent.txt` read, its log folder from `warroom.txt`;
  - the default channel and logs;
  - a folder without `agent.txt` reported;
  - the folders list making the agents in order;
  - each agent's own mode;
  - an agent that is off getting nothing in its channel;
  - a tool living in its own agent's orders in force, and leaving them when it ends;
  - the load naming each agent's log folder.

  The war room's (50) and the dashboard's (27) self-tests passed. 512 game references, 0 missing. The installed DLL has sha256 `A691501CF37904FD…`.
- **Not yet seen in a game.**

### 17:49 · The agents' goal: no region, the whole map · commit 04aeabd
- **Why:** Mert, reviewing both agents' system prompts: "The goal shouldn't be to win the war for a specific region. It should be to systematically eradicate all enemies and assert its dominance over the game map with its allies."
- **What:** the goal now reads "Systematically eradicate all your enemies, and assert your dominance over the whole map together with your allies." It replaces "Win the war for the mountains:" (Dol Guldur's: "... for Rhovanion:") and its three points:
  - break the enemy's power around your lands;
  - keep your own;
  - grow by taking holdings you can hold.

  The line "The person running this test may change this goal" stays.
  - Where it changed:
    - `agent\warroom-system.template.md`, and `agent\CLAUDE.template.md` (the old chat's; not read by the war room);
    - outside the repository, both agents' `warroom-system.md` and `CLAUDE.md` (the old copies are kept in the session's scratchpad). Nothing else in them changed (diff);
    - the war rooms read the instructions anew before every call, so the change holds from each agent's next call, with no restart.
  - `scripts\setup-agent.ps1` loses `-Theatre`: the goal names no region now, so only the title and the opening line name the realm.
- **Checks:** both scripts parse.

### 18:23 · Version 0.9.1: every save gets exactly its own history back · commit 46b8447 (installed 18:22)
- **Why:** Mert asked how loading different saves keeps histories apart. The cut-back went by date alone and deleted what it cut. So loading a later save from a run played before another one gave the agents the other run's history, and nothing of its own after the branch point. Mert: "Yes, this is an important fix. Build it now please."
- **What** (`History`):
  - every chronicle line, in the module, and every copy of the notes and the story, in the war room's `snapshots.jsonl`, is labelled with the game session (load id) that wrote it, and nothing is deleted any more;
  - at every successful save (`OnSaveOverEvent`) the bridge appends a note to the campaign's own `saves.jsonl`: the save's name, its in-game hour, this session, and this session's own history. Nothing goes into the save itself, as the project's rule says;
  - at a load, the save's name (`MBSaveLoad.ActiveSaveSlotName`) and hour find its note. Its history is the sessions its world lived through, each up to the hour it was saved at. The chronicle shows only those lines, the load record and every poke carry the history, and each war room puts back the last memory copy written within it (`Memory.Restore`);
  - a save with no note (made before 0.9.1, renamed, overwritten without this mod: its hour no longer matches) gets the unlabelled lines up to its date, the old rule. A new game starts empty.
- **Checks:**
  - the smoke test passed in full, with 7 new checks: a run played on two branches from one save, then a save of the first branch loaded after the second, gets exactly its own lines; the file keeps every branch's lines; a mismatched hour counts as no note; a new game starts empty; the load records what it found. The old check that the campaign's file was cut now checks that it keeps every line;
  - the war room's self-test (55) passed, with 5 new checks: session A's save gets A's memory, not B's, though B came later; B's gets B's; the shared start gets the memory from before both; no copy is dropped; an unlabelled copy counts only for the history from before labels;
  - the dashboard's (27) passed; 514 game references, 0 missing; the installed DLL has sha256 `3D4F551CEA13A24A…`. The war rooms and the dashboard were stopped for the build and started again at 18:23.
- **Not yet seen in a game.**

### 19:39 · Outside the repository: recruiting and garrison training (TFAOMFixes, Improved Garrisons' options) · commit 962469e
- **Why:** Mert asked how recruiting and training work for his garrisons and for the computer's lords. The walk-through found three things, and Mert asked for all of them: "1: this fix surely must be made. 2: turn on npc world recruitment please." Then: notables should offer each AI lord "only THEIR (the lord's) culture's troops ... without us having to convert them after recruitment", his clan's parties "ONLY TROOPS FROM MY TROOP TREE"; and a weak party on a recruit order should go "to every single settlement, one by one, ... recruiting every single troop there, irrespective of their template, until they reach 80% full party size. Then, if they don't get another order, ... slowly replacing off-template troops with on-template troops."
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: new `GarrisonTraining.cs`, `RecruitOrders.cs`; `HostRecruits.cs` (the `Offers` class and the doors); a line each in `SubModule.cs`, `HookCensus.cs` and the project file, backup `TFAOMFixes148.csproj.bak-20261022-recruit`):
  - **Garrison training keeps its experience.** Improved Garrisons gives each kind of troop 20 experience a man a night, but only on a night that promotes at least one man, so a stack needed about cost / 20 men (65 for level 26, 202 for level 51). Its own log showed it: Hithaegrist's last 100 entries were 123 promotions, none past level 21. Now a stack it would have trained but left untouched gets its 20 a man kept, by its own rules (template, top tier, room left on the template's lines). Its saved options are only read. In the player's garrisons trained along a template, the game's own daily promoter (random branch) is held back.
  - **Notables show every buyer his own troops.** While a buyer looks at or buys from a place, its notables' volunteers are shown as his own: the Host for Mert's clan and raised companions; for every other lord, his culture's troop of the same role and nearest level. Every slot he did not take is put back afterwards. This covers the game's purchase round for lords, the AI's count when it weighs a visit (Party AI Controls' template count rides on it), the game's garrison auto-recruit, and Improved Garrisons' daily recruiting (for the holding's owner). The same slot always shows the same troop to the same buyer, and Mert's recruit screen uses the same rule. The old swaps after a purchase stay as a safety net, with counters (`tfaomfix.my_recruits status`).
  - **Party AI Controls' recruit order.** As written, it picked the nearest village or town (never a castle) whose culture is in the party's template, with 3 or more volunteers that fit, and bought only what fit, until the party was completely full. Its `ImplementRecruitFromTemplate` is replaced:
    - below 80%: the nearest town, castle or village it may recruit in and hasn't swept in 3 days, one after another, taking every volunteer it may and can pay for, whatever the template;
    - at 80%: an order queued behind the recruit order resumes;
    - otherwise it keeps touring, taking only template troops its composition still wants. Once full, it lets one off-template man go (lowest tier, wounded first) for each template man it takes. Party AI Controls' all-at-once dismissal waits meanwhile;
    - it ends when full and all on template, or when the template wants nothing more.
    
    The danger check, food and army rules are as before. Console: `tfaomfix.recruit_orders`, `tfaomfix.garrison_training`.
- **Improved Garrisons' options:** "NPC world recruitment" switched on in all 13 option files of this campaign (`IGConfiguration_*_Ah2auppicLLJ.xml`, written byte for byte apart from that value; backups in `C:\BannerlordArchive\IGConfigBackup-20261022-preNpcRecruit`). Computer garrisons now recruit from their own region daily, up to its 50-man threshold. Until now nothing refilled them: Improved Garrisons switches the game's own one-a-day garrison recruiting off hourly for every holding. In the orc reports over 18.7 days, Celebdil, Eaworth, Langhold, Methedras and Talas Duiren didn't change by one man, and Hiltbolt and Starkmoore stayed empty.
- **For the agents' realms:** their lords are now shown their own culture's troops before buying (orc lords in orc lands see what they saw before), and their garrisons of 50 men or fewer recruit daily.
- **Checks:**
  - built clean;
  - 591 game references, 0 missing (RefCheck);
  - an offline dry run applied all 10 new or changed patches to the real game, Improved Garrisons and Party AI Controls methods with the pack's Harmony 2.4.2, and all bound;
  - the 31 names reached by reflection exist in the installed mods;
  - a probe confirmed that Harmony 2.4.2 hands a prefix's state to a finalizer, also when the original throws;
  - the DLL still holds the other sessions' CrowdAi, CrushWatch, FastCores, VassalArmies and RingWars. Their settings were left alone, and both sessions were told and agreed.
- **Installed** at 19:38 (sha256 `2D3A3986EC987787…`, 434,688 bytes), the game closed, over `434511229FDC618B…`, which is kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-recruit-20261022-1938`. The `.cs` files were edited in place: the source before this change is the one that built that kept DLL.
- **Not yet seen in a game.**

### 19:48 · Outside the repository: computer garrisons up to 250 / 400, and a one-day sweep memory (TFAOMFixes) · commit e2a440f
- **Why:** Mert: "the limit should be raised to 250 men for castles and 400 men for cities", and for the recruit order: "Instead of the last 3 days, set the bar to the last day. Slots refill every day."
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`):
  - new `ComputerGarrisons.cs`. Improved Garrisons has one limit for all computer garrisons (50). Every time it asks for a computer holding's settings, it builds a fresh object that is never saved. A postfix on `GarrisonBehavior.GetTownSettings` sets that object's limit to 250 for a castle and 400 for a town. The player's saved settings and its options file are untouched, so its options screen still shows 50. File `computer_garrisons.txt`, console `tfaomfix.computer_garrisons`;
  - `RecruitOrders.cs`: a party skips a place it swept in the last 24 hours (was 72);
  - a line each in `SubModule.cs` and `HookCensus.cs`, and the project file.
- **Checks:**
  - built clean;
  - 592 game references, 0 missing;
  - the offline dry run bound all 11 patches;
  - the limit was set on a real computer-garrison settings object offline (0, then 250).
- **Installed** at 19:48 (sha256 `A263BA3D602E8BA8…`, 437,760 bytes), the game closed, over `2D3A3986EC987787…`, kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-limits-20261022-1948`. CrowdAi, CrushWatch, FastCores, VassalArmies and RingWars are still in it.
- **Not yet seen in a game.**

### 20:19 · Version 0.9.2: an agent's recruit order tours, as Mert's own parties do · commit 6adf9ac (installed 20:18)
- **Why:** Mert: "make sure that any lord with a recruit order given by an agent controlling a faction also follows the logic as my own parties." Until now the recruit order went to one town or village and stayed up to three days or until 98% full. It bought only through the game's hourly round: one volunteer per notable an hour, on a dice roll.
- **What** (new `RecruitTour.cs`; `Orders.cs`; `Tools/Library/recruit.json`; the move kind `tour` in `ToolDef.cs`; [TOOLS.md](TOOLS.md#the-recruit-order-tours-since-092)):
  - **Where:** from the order's place (now any town, castle or village of the realm or an ally), the lord rides to the nearest town, castle or village of the realm, an ally or a realm at peace with it. The place must be within reach (four times the usual gap between neighbouring towns) and not swept by this order in the last day. One after another;
  - **What he buys:** in each, every volunteer his standing reaches and he can pay for (the game's own gold and wage rules), with no dice, until 80%. Then only men of his clan's troop lines (its party recipe, what grows into it and what it grows into), and once he is full he lets a man from outside them go for each one taken;
  - **How he buys:** through the game's own purchase, so with TFAOMFixes loaded he gets his own people's troops;
  - **The end:** full and all on his clan's lines, or nothing within reach. The agent hears why with the next poke. There is no three-day limit, and a stop that falls or empties is passed over;
  - **The refill stop of defend and patrol** stays a single stop, but buys the same way there, so an agent's lords refill as fast as the computer's (TFAOMFixes, next entry);
  - **Hand orders:** `agents.vassal ... recruit <place>` tours too;
  - the library's text for recruit says all this to the agent (`tools.txt` is written from it at the next load).
- **For the test:** both agents' lords now recruit much faster when ordered to. Every computer lord does too, through TFAOMFixes (next entry), which leaves alone any lord an agent is steering.
- **Checks:**
  - the smoke test passed in full (210 checks): 1 new, that the recruit order's move is a tour from its place, a castle accepted, with no stop after three days, and the library saying how it works;
  - the war room's and the dashboard's self-tests passed;
  - 538 game references, 0 missing;
  - the installed DLL has sha256 `DDD9623F3D35A6C6…`;
  - the war rooms and the dashboard were stopped for the build and started again at 20:19.
- **Not yet seen in a game.**

### 20:19 · Outside the repository: computer lords tour to recruit (TFAOMFixes)
- **Why:** Mert asked how computer lords recruit. The orc side's daily log over 30+ days:
  - lords at about 25% of their size limit, 9 in 10 below half;
  - about 3 recruits a lord a day;
  - 35–51 weak lords a day never spent an hour where they could recruit, while 300–670 volunteers waited in their lands;
  - money was not the brake: the clan tops each lord up to 5,000 a day.

  Proposed, then: "I would make that change."
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: new `LordRecruiting.cs`; `RecruitOrders.cs`'s rules shared with it; a line each in `SubModule.cs` and the project file):
  - **Who:** every computer lord. Left out: Mert's clan, raised companions, lords Party AI Controls manages for him, and anyone already steered by someone else (the game's AI off and the plan not ours, as with an agent's order).
  - **Tour:** below 50% of his size limit, and free (no army, siege, battle, hunger or stronger enemy close by), he tours the nearest towns, castles and villages of his realm, an ally or a realm at peace with his. Each must be within reach and not swept in the last day. One after another, until 80%.
  - **Sweep:** in any place he stands in, touring or staying (then at most every 6 hours), he takes every volunteer his standing reaches and he can pay for, with no dice, until 80%. Above that, only his clan's troop lines, one out for one in once full.
  - **The end:** a tour ends at 80%, on an army, a siege or a battle, in danger (no new tour for 6 hours) or with nothing in reach (12).
  - **Saves:** lords on tour are handed back to the game around every save.
  - **Controls:** file `lord_recruiting.txt`, console `tfaomfix.lord_recruiting [on|off|start <pct>|fill <pct>|status]`.
- **Not changed:** the game's gold and wage rules, the standing limit (3 of 6 slots at ordinary standing), and how many volunteers a realm produces.
- **Checks:**
  - built clean;
  - 601 game references, 0 missing;
  - the offline dry run bound all 11 patches (the tours use the game's hourly event, no patch);
  - the DLL still holds the other sessions' CrowdAi, CrushWatch, FastCores, VassalArmies and RingWars.
- **Installed** at 20:19 (sha256 `133342C6545379D0…`, 448,512 bytes), the game closed, over `A263BA3D602E8BA8…`, kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-lordtours-20261022-2019`.
- **Not yet seen in a game.**

## Friday, October 23

### 13:17 · Version 0.9.3: recruit tours say why they stop, the chase question before a hunt, and fixes for a realm of Mert's with an agent · commit 25cda5f (installed 13:16)
- **Why:**
  - **The first evening on 0.9.2** (Oct 22, 20:22 to 20:39, about three in-game days): 13 of Dol Guldur's 14 recruit tours ended in their first hour at 3 to 43%, saying "nothing left to recruit within reach", while 1,500 volunteers waited in its lands. The cause is the game's daily wage cap (`ClanVariablesCampaignBehavior.MakeClanFinancialEvaluation`). It caps each war party's daily wages from the gold of its clan's leader: under 30,000, 200 + (gold / 30,000)² × 400; 30,000 to 90,000, 600 to 1,200; above 90,000, no cap; half again for the leader's own party. 21 of Dol Guldur's 47 lords were at their cap: Skûlgar, for one, had 81 men, wages 354 of 359, and 10,555 gold. The agent was not told why, sent the same five lords again, and three stopped again at once. The orcs were hardly touched (3 lords at the cap); their limit is the volunteers themselves (about 440 waiting, 220 within their standing).
  - **Mert, Oct 23, on hunt:** "It is completely unneccessary for a party/army to hunt an enemy that is faster than them if they have other competing objectives. The only reason they should be doing this is if they have nothing else to do/are trying to drive them away from something. For this to happen, the agent should be able to ask for the speed of an enemy party in sight before giving a hunt order, and should have this as part of the guide for its use of the hunt order."
  - **The snags found planning the lieutenant** (Oct 22): Mert, "Fix the snags in the code."
- **What:**
  - **A tour's end says why** (`RecruitTour.cs`): when volunteers were within reach but none could be paid for, the end names the commonest bar: his wages at their cap (key `wage cap`, with his wages and the cap, and that the cap comes each day from his clan leader's gold), his gold, or his clan's purse (`no gold`). "Nothing left to recruit within reach" now means just that ([TOOLS.md](TOOLS.md#the-recruit-order-tours-since-092)).
  - **The chase question** (war room, new `ChaseTool.cs`, served beside "ask the way", "battle odds" and "roads in"): `can_catch` (`hunter`, `quarry`) gives each side's pace by the game's rules, the way between them, the hours to reach the enemy if it holds still and to run it down if it flees, how much a faster enemy gains an hour, and who gets first to where it is marching ([TOOLS.md](TOOLS.md#the-chase-question-before-a-hunt-since-093)).
  - **hunt's text** in the library: weigh the two paces first; a faster target is reached only when it stops or turns toward the hunter, so set its hunt only when the hunter has nothing better to do, or to keep it from a place or a lord it threatens. In general words, not Mert's.
  - **The instructions** (`agent/warroom-system.template.md`, "Asking the way"): one sentence naming `can_catch`, so the question is promised only in calls that have it.
  - **A tool for a realm no agent serves** (`Tools/Runner.cs`) no longer runs on the first agent listed (its view, orders in force and call-backs): alarm `tool_no_agent`, and the lord is let go. An order that names no realm (the tests') keeps the first agent.
  - **Every order door refuses the player, and a lord on a Party AI Controls order** (`Orders.Issue`): until now only `agents.vassal` had these two guards.
  - **The player's own party is never one of a realm's lords** in its report or in the tools' list of own lords (`Views.Build`, `Facts.OwnLords`).
  - **The record of Mert's play** keeps his realm's pokes if that realm gets an agent of its own (`Brain.Process`), and the dashboard still marks that realm's frame as his.
  - **Lords on TFAOMFixes' recruiting tour** (asked by name, `TFAOMFixes.LordRecruiting._tours`) are named as such in the daily sweep (`on_tfaomfixes_recruiting_tour`), no longer counted as "held by something else": on Oct 22 that raised the `held_by_other` alarm for 12 lords.
  - Version 0.9.3 (module and war room).
- **Outside the repository:** the same sentence on `can_catch` in both agents' instructions, `C:\Users\karab\Projects\orc-war-council\warroom-system.md` and `C:\Users\karab\Projects\dolguldur-war-council\warroom-system.md`, edited in place.
- **Checks:**
  - the smoke test passed in full (213 checks): 3 new, that a tool for a realm no agent serves is not run on another's agent and lets the lord go, that the player's own party is not among a realm's lords, and that hunt's text says to weigh the paces;
  - the war room's self-test passed: 2 new, the chase on the made-up map (Bolg at his own pace against Aragorn's army at its trail's pace: faster, run down in about 28 hours, at Edoras first) and the chase refused for an enemy as hunter;
  - the dashboard's 27 checks passed;
  - 539 game references, 0 missing (RefCheck);
  - the installed DLL has sha256 `A64709B5A6457BB8…`, the war room's `WarRoom.dll` `5A5A9197059AAF20…`;
  - the war rooms and the dashboard were stopped for the build and started again at 13:16.
- **Not yet seen in a game.**

### 13:55 · Version 0.9.4: money for the agents (their own wage caps, a purse, a loot-only sell order, a battle's worth) · commit 3f6dd19 (installed 13:54)
- **Why:** Mert, Oct 23: "The caps should be the agent's call. It should be able to estimate the value of loot and prisoner ransom money from a battle and decide accordingly. An understanding of economics should be given to it now. When needed, it should send armies/lords to friendly settlements that have battle loot to go and sell stuff and ransom prisoners." On the plan: "Agents should be able to set caps freely. I would also be careful with the sell order and make sure that an order to sell actually sells loot and just loot, instead of important food and other things."
- **The game's rules it rests on** (1.4.8, read in its code): each lord pays his men every day from his own purse; his clan's leader tops him back up to 5,000 while the leader has 4,000 or more; a lord above 10,000 hands a tenth of the excess to his clan daily; unpaid wages cost morale and men desert. The wage cap only stops recruiting. Computer lords sell everything but food and good riding horses on entering a friendly town (pack animals too), and hand prisoners over on entering a friendly castle or town; a soldier fetches a quarter of his recruit price.
- **What** ([TOOLS.md](TOOLS.md#money-since-094); new `Purse.cs`, `PurseView.cs`, `Tools/Library/sell.json`):
  - **The purse in the hourly copy:** each own lord's gold, wages and cap (and whether the agent set it), the loot he carries (its worth and pieces, worked out again only when his goods change), his prisoners and their ransom by the game's reckoning, and his last sale; every clan's gold, no longer the ruler's only, and its change a day by the game's own reckoning (the clan screen's number, worked out at each clan's daily tick).
  - **YOUR PURSE** in the report: the clans' gold and its day, the lords at their wage cap, the caps the agent set, loot and prisoners carried, and the battles' takings; in full in the council's briefing; per lord in the look-ups.
  - **The wages order** (plain, like adjust): a cap a day, no cap, or the game's own again, for a lord and any lords listed with him. Put back right after the game's daily setting and checked hourly; handed back to the game around every save, since a party's cap is saved with the game; cleared at every load like every order.
  - **The sell order** (tool, at a castle or town of the realm or an ally): rides in, sells, and calls back with what it got. **Loot only:** weapons, armour, shields, ammunition, harness and plunder (goods that are not food); never food, horses (riding, war or pack), herds, books or banners. For every lord holding one of our orders, the game's own sale on entering a town is replaced by this loot-only one, so recruit tours sell loot only too. Prisoners go by the game's own sale, measured.
  - **A battle's worth:** each won battle's takings (loot, prisoners, ransom, the enemy's fallen shared among the winners by men) are recorded into the campaign's `takings.jsonl` and read back at every load. With 3 won battles on record, the war room's `battle_odds` adds to a win what it would bring in, from the realm's own battles.
  - **Three new game hooks** (always on): the loot-only sale for held lords, the prisoner sale measured, and a won battle's results counted before and after.
  - **The instructions** (`agent/warroom-system.template.md`): a "Money" section with the game's rules above in plain words, and in "Asking the way" that `battle_odds` says what a win would bring in.
  - The library's text: a Money part (the wages order, the sell order).
  - Version 0.9.4 (module and war room).
- **Outside the repository:** the same "Money" section and sentence in both agents' instructions, `C:\Users\karab\Projects\orc-war-council\warroom-system.md` and `C:\Users\karab\Projects\dolguldur-war-council\warroom-system.md`.
- **Checks:**
  - the smoke test passed in full (219 checks): 6 new (the report's purse; no reckoning before 3 battles; the reckoning after them; the takings read back at a load, each realm its own; the sell order riding in, calling back with the sale and letting the lord go; the library's two orders), and 2 changed (the first report test reads the whole report, not the console's first 24 lines, which the purse pushed the enemy lines out of; the library now holds 20 orders);
  - the war room's self-test passed: 1 new, a won field battle's takings;
  - the dashboard's 27 checks passed;
  - 571 game references, 0 missing (RefCheck);
  - an offline dry run applied all 6 of the module's game hooks (the 3 new and the 3 older) to the real game's and Party AI Controls' methods with the pack's Harmony 2.4.2: all bound;
  - the installed DLL has sha256 `10DB38BEE54EB417…`, the war room's `WarRoom.dll` `F2D6660262D0825E…`;
  - the war rooms and the dashboard were stopped for the build and started again at 13:55.
- **Not yet seen in a game.**

### 14:17 · Version 0.10.0: the player's lieutenant, in the campaign map's chat box · commit 57cf33c (installed 14:16)
- **Why:** Mert, Oct 22: "lets build an agent that acts as the player's 'liutenant' ... have the player type orders into that chat to their liutenant, which then orders lords to do stuff on the map ... I would verify and approve the order ... I could also ask it what lords are doing across the realm, any threats that were spotted in fronts I'm far away from, and also what lords I gave orders to are currently doing." Oct 23: "Lets keep everything verbal in the chatbox. My approval should also be verbal. Keep its answers short and concise whenever possible, make popups pause the game until the popup is closed"; on who it commands: "Yes, it should be able to (including my own clan's parties). Lets keep it in the chat window for now, the companion chat will be a quality of life upgrade for later."
- **What** ([LIEUTENANT.md](LIEUTENANT.md); new `Lieutenant.cs`, `LieutenantRoom.cs`, `module\GUI\Prefabs\SPChatLog.xml`, `agent\lieutenant-system.template.md`):
  - **The chat box on the campaign map:** the game's single-player chat log is the multiplayer chat box with its typing line taken out (its `TextInputWidget` names a widget the prefab no longer has) and its channels closed. The module brings the game's own prefab back with the typing line in it, and five Harmony patches on `MPChatVM` (`ChatHook`): on the campaign map with a lieutenant, the closed channels become the open "party" channel, named "(Lieutenant)"; what is sent goes to the lieutenant instead of a server; messages may be 400 letters (100 before); the clock stops while the player types and runs again after (`lieutenant_pause`). Missions and multiplayer are untouched; the patches attach only when a lieutenant is in the agents' settings.
  - **The lieutenant's folder** (`kind = lieutenant`, `realm = player`, resolved at each load to the player's realm): the player's realm is a seat like an agent's (its sight, an hourly report with a YOUR RULER line saying where the player's party is, look-ups every hour, its view file), but never poked. The player's words go into `chat.json` and `chats.log`.
  - **Its war room** (`LieutenantRoom`; `WarRoom run` and `once` for a folder of kind lieutenant): one call per message, with the conversation so far (its memory, `memory\<campaign>\chat.jsonl`), what became of its orders lately, the proposal waiting, the realm's report and the player's words; it reads its look-ups and asks the same questions as the agents. It answers in `orders\chat-N.json`: a few words, a proposal, orders agreed to, a drop, or a longer text for a window that pauses the game.
  - **Never unasked** (`LieutenantRules.Gate`): orders are carried out only when the player's words answered the proposal still waiting, by the war room's `pending_chat` from `chat.json`, never the model's word; any others become the proposal and wait for a yes.
  - **Its orders** go through `Orders.FromAgent` for every lord of the player's realm, the player's clan's parties too, never the player's own party and never a lord holding a dialogue order (Party AI Controls); refusals are said in the chat box at once (a new `told` call-back), and the orders' call-backs and endings come into the chat box. The yellow poke lines and the cyan order-ended lines stay off for its realm.
  - `AgentConfig.Kind`, `Bridge.Resolve("player")`, `Views.RulerLine`; the module references the game's `TaleWorlds.MountAndBlade.ViewModelCollection` and `Diamond`; the build stages `module\GUI`.
  - `scripts\setup-agent.ps1 -Kind lieutenant`: `realm = player`, the lieutenant's instructions, no chronicler, a quick war room (effort medium, thinking cap 2000, timeout 120 s, no council), logs in `logs\warroom-lieutenant`.
  - Version 0.10.0 (module and war room).
- **Outside the repository:**
  - the lieutenant's folder `C:\Users\karab\Projects\lieutenant` (agent.txt, warroom-system.md from the template, warroom.txt, warroom\ with the order library), made by `setup-agent.ps1`;
  - the bridge's settings `%LOCALAPPDATA%\BannerlordAgents\settings.txt`: `agents` now lists it after the two realms' agents (backup `settings.txt.bak-20261023-before-lieutenant`); the next load adds `lieutenant_pause = on`.
- **Checks:**
  - the smoke test passed in full (225 checks): 6 new (the folder's kind; the lieutenant's hourly report with the ruler's party and without it among the lords; no pokes in its channel, its realm's pokes and look-ups kept; the player's record kept; the player's words written and numbered on; the yes rule in six cases);
  - the war room's self-test passed: 4 new (the kind read from agent.txt; the message; the answer read and checked; the answer file with the war room's `pending_chat`);
  - the dashboard's 27 checks passed;
  - 580 game references, 0 missing (RefCheck);
  - an offline dry run applied all 11 of the module's game hooks (the 5 chat box hooks, the 3 money hooks, the 3 older) to the real game's and Party AI Controls' methods with the pack's Harmony 2.4.2: all bound;
  - our chat log prefab differs from the game's only by its typing line (compared line by line);
  - **a real end-to-end run** of the lieutenant's war room in a scratch folder, with the orcs' real report standing in for the realm: "Which of my lords are weakest right now, and could two of them go fill their ranks?" was answered in 25 s with a proposal of two recruit orders; "yes, do it" in 6 s with the same orders carried out and `pending_chat` 1; with the shorter-answer rule, a question about a far front in 14 s, in three sentences;
  - the installed DLL has sha256 `9744690DB8543D18…`, the war room's `WarRoom.dll` `614277F9AB9AB6FC…`;
  - the war rooms (now three) and the dashboard were stopped for the build and started again at 14:17; the lieutenant's war room waits for the player's first words.
- **Not yet seen in a game:** the chat box itself (the typing line, Enter, the clock), the player's clan parties under Party AI Controls, and the cost of the player's realm as a seat.

### 14:29 · Seen in a game: the lieutenant's first message (0.10.0)
- Mert wrote "I would like to get a contingent of dwarven lords to defend goblin town, about 2000 men strong." The chat box sent it (14:29:12); the war room answered in 42 s with six dwarf lords, 2,209 men, all at Goblin Town by Autumn 4, 11:00, and a longer write-up; the bridge read the answer at 14:29:55.
- **The gap:** the answer waited until the game's clock ran again, since the bridge handed it to the game's tick (which stops while paused). The write-up came as a window although Mert had not asked for one.
- Mert then asked for a window of the lieutenant's own: "have a button work as a shortcut (say 'enter') to go into the liutenant pop-up automatically and chat with it through that pop-up rather than through the game's own log. This way its easier to approve, disapprove, or comment on its proposals as well (we could have buttons for each)."

### 14:43 · Version 0.10.1: the lieutenant's own window, with Approve and Decline · commit bf0b406 (built 14:43; installed 14:47, next entry)
- **What** ([LIEUTENANT.md](LIEUTENANT.md#using-it-since-0101-its-own-window); new `LieutenantWindow.cs`, `module\GUI\Prefabs\LieutenantWindow.xml`; `Lieutenant.cs` rewritten around it):
  - **The window:** on the campaign map, the game's chat keys (Enter, T) open it (one Harmony prefix on `MPChatVM.StartTyping`; the game's chat box stays as it is). In the look of the game's own text pop-up: the conversation (the last 14 lines, newest at the bottom), the proposal waiting with **Decline** and **Approve**, the line to type in (focused on opening; Enter sends), **Close** (Esc). The game stands still while it is open, the clock locked (`lieutenant_pause`), and runs again as it ran.
  - **Approve and Decline:** the button carries out the proposal waiting as it stands, or drops it; the next message tells the war room which ("notes" in `chat.json`, WHAT YOUR RULER DID SINCE YOUR LAST ANSWER in its message). Words still work ("yes", "only Dain and Thorin"), and a comment typed instead goes with the proposal, to change it.
  - **Answers show at once, even paused:** they come in on the frame's queue (`Background.ToUi`); only orders go to the game's tick, as every agent's do. A longer write-up follows the words in the window, no separate pop-up. With the window closed, answers and what becomes of its orders also show in the message log.
  - **Gone:** 0.10.0's override of the game's chat log prefab (`SPChatLog.xml`) and its five chat box patches.
  - The lieutenant's instructions: the buttons, and "detail" now shows in its window (`agent\lieutenant-system.template.md`; the same copied into `C:\Users\karab\Projects\lieutenant\warroom-system.md`, outside the repository).
  - The module references the game's `TaleWorlds.Engine.GauntletUI`, `GauntletUI`, `ScreenSystem` and `InputSystem`.
  - Version 0.10.1 (module and war room).
- **Checks:**
  - the smoke test passed in full (225 checks);
  - the war room's self-test: the lieutenant's message check now covers the buttons' notes (it runs at the install, with the war rooms stopped);
  - 597 game references, 0 missing (RefCheck, with Native's assemblies too);
  - the offline dry run applied all 7 of the module's hooks (the new key hook, the 3 money hooks, the 3 older) with the pack's Harmony 2.4.2: all bound;
  - every sprite and brush of the window is one the game's own text pop-up or chat log uses.
- **Not yet seen in a game:** the window itself (its look, focus, Enter and Esc, the buttons, the clock lock).

### 14:47 · Version 0.10.1 installed · commit 930342d
- The game closed, Mert said to install. The war rooms (three) and the dashboard were stopped for the build and started again at 14:47; the lieutenant's war room waits for the player's words (last answered #1: the Goblin Town proposal of 14:29 was never answered, so nothing was given).
- **Fixed at the install:** the war room's self-test check of the lieutenant's message looked for a line break as `\n`, but the message's lines end in `\r\n` on Windows; the check now looks for the heading and the note on their own, in order. All of its checks passed, as did the smoke test (225) and the dashboard's (27).
- **Installed:** the module's DLL sha256 `5FB015DC4F311217…`, the war room's `WarRoom.dll` `4DB80E7E8E136400…`; the module's `GUI` holds only `Prefabs\LieutenantWindow.xml` (the mirror removed 0.10.0's `SPChatLog.xml`).
- **Not yet seen in a game:** the window.

### 14:51 · Seen in a game: Enter and T opened the game's chat box, not the lieutenant's window (0.10.1)
- Mert loaded the campaign (14:50) and closed the game (14:51): "enter and t still open the chat window. enter should open the liutenant window, t should open the chat window."
- The key hook had attached ("the lieutenant's key (Enter, T) attached" in the bridge's log) but was never reached: it sat on the chat box's start of typing (`MPChatVM.StartTyping`), whose body is 15 bytes of code, and the runtime always folds bodies under 16 bytes into the code that calls them, so the chat box's key handling ran its own copy and never our hook. (The same trap as Harmony patches on tiny getters, noted before; the offline dry run only shows that a hook attaches, not that it is reached.)

### 14:57 · Version 0.10.2: Enter opens the lieutenant's window, T the game's chat box · commit 697bd79 (built and installed 14:57)
- **What** ([LIEUTENANT.md](LIEUTENANT.md#using-it-since-0101-its-own-window); `Lieutenant.cs` `ChatHook`, `GameHooks.InstallChat`):
  - the hook moves to the chat box's look at its keys each frame (`GauntletChatLogView.HandleInput`, 813 bytes, in Native's GauntletUI, found by name): on the campaign map, with a lieutenant and the chat box shut, Enter (read from the map's own keys, as the chat box reads it) opens the lieutenant's window and the chat box does nothing that frame;
  - **T** is left to the game: it opens the game's chat box, as Mert asked; Enter while the chat box is open stays the chat box's;
  - while the lieutenant's window is open, the chat box does not look at its keys, so Enter in the window only sends and the chat box stays shut beneath;
  - the bridge's log now says "the lieutenant's key (Enter) attached".
  - Version 0.10.2 (the module; the war room is unchanged at 0.10.1).
- **Checks:**
  - the smoke test passed in full; the war room's and the dashboard's self-tests passed at the install;
  - 603 game references, 0 missing (RefCheck, with Native's assemblies);
  - the offline dry run applied all 7 of the module's hooks with the pack's Harmony 2.4.2, the new key hook among them (its reading of the chat box's own state by name included): all bound; it also measured the two targets (the old 15 bytes, the new 813);
  - the installed DLL has sha256 `4E17EC9235073C04…`, the war room's `WarRoom.dll` (rebuilt, same code) `C8CE07BFE3BAB9AA…`;
  - the war rooms (three) and the dashboard were stopped for the build and started again at 14:57; the lieutenant's war room waits for the player's words (last answered #1).
- **Not yet seen in a game:** Enter opening the window, and the window itself (its look, focus, Enter and Esc, the buttons, the clock lock).

### 15:02 · Seen in a game: Enter opens the lieutenant's window (0.10.2)
- Mert, after a launch from 14:59 to 15:03: "The window works well, but I should be able to scroll up and down to see the full answers of my liutenant and the chat's history."
- In that launch Mert asked for "a contingent of dwarven lords to defend my border with the misty mountain orcs, and a contingent of silvan elven lords to defend my border with dol guldur" (15:02:12); the answer came in 44 s with a proposal of 7 orders (six dwarf lords, 2,209 men, at Goblin Town, and an elven force), not answered before the game closed. Errors: none.
- **The gaps:** the conversation showed only its last 14 lines, cut at the top with no way back; the proposal's list was cut at 140 points, so a 7-order proposal lost its last lines; and the window had no room for a proposal's box at all (its parts added up to more than its height).

### 15:11 · Version 0.10.3: the lieutenant's window scrolls · commit b5e273d (built and installed 15:11)
- **What** ([LIEUTENANT.md](LIEUTENANT.md#using-it-since-0101-its-own-window); `module\GUI\Prefabs\LieutenantWindow.xml`, `LieutenantWindow.cs`, `Lieutenant.cs`):
  - the conversation is the game's own scrolling panel, set up as the game's chat log's is (`SPChatLog.xml`: newest at the bottom), with a bar in the look of the game's selection pop-up (`MultiSelectionQueryPopup.xml`): the mouse wheel over it or the bar scrolls back through everything since the load (up to the last 200 lines, was 60 kept and 14 shown);
  - it opens at the newest line and follows each new one, unless the player has scrolled back to read (then it stays put); the player's own words always bring it to the end (`LieutenantWindow.Add(mine)`; the window finds its bar by `LinesScrollbar` and holds it at the end for 8 frames, as a new line's height is known a frame or two after it is added);
  - the conversation takes the room the rest of the window leaves (more with no proposal waiting); the proposal's list has its own scrolling box (130 points);
  - Approve now leaves what was approved in the conversation ("You approved:" and the orders);
  - a new line is added to the shown list instead of the list being rebuilt;
  - the hint under the typing line says "Scroll for earlier lines".
  - Version 0.10.3 (the module; the war room is unchanged at 0.10.1).
- **Checks:**
  - the smoke test passed in full; the war room's and the dashboard's self-tests passed at the install;
  - 610 game references, 0 missing (RefCheck, with Native's assemblies);
  - the offline dry run applied all 7 of the module's hooks: all bound;
  - the window's file reads as well-formed XML; every widget, sprite and brush in it is one the game's own pop-ups or chat log use (the bar's handle `FaceGen.Scrollbar.Handle` is Native's); the game's stacking layout was read to confirm a stretching part gets only the room left;
  - the installed DLL has sha256 `FABBE79B9454D6BE…`, the war room's `WarRoom.dll` (rebuilt, same code) `76AECB0C43C94EC8…`;
  - the war rooms (three) and the dashboard were stopped for the build and started again at 15:11; the lieutenant's war room waits for the player's words (last answered #2).
- **Not yet seen in a game:** the scrolling itself, following the newest line, and the proposal's box with a long proposal.

### 15:34 · Version 0.10.4: the fog check's two false alarms, and the sight check's drop, explained and fixed · commit f1c3a0d (built and installed 15:34)
- Mert asked: "Look into the problems now and figure out why they materialized" (the fog check's leaks on Oct 22 and Oct 23, and the sight check's agreement falling from 96–98% to 86–91% on Oct 22).
- **The fog check, Oct 23 (20 flags in 2 reports, 0.10.0):** all 20 were the orcs' own lords, in YOUR PURSE's "Carrying loot" and "Holding prisoners" lines (0.9.4). They were lords riding in the orcs' two armies (12 and 8 parties). The check took the realm's own lords only from the report's own-lords list, which holds only those leading parties. **Not a leak:** a realm knows its own lords.
- **The fog check, Oct 22 (1 flag):** poke #429 was about a raid on the orc village Linhamm, named by the game's own notice ("Linhamm is being raided by Cadda Tordaging"). The raid began at 21:53. By the hour's look 20 minutes later the raider was off the village and out of the orcs' sight, so the poke's fact line named a lord the orcs had never seen ("last seen 9999 hours ago"). **Not a leak either:** the game's news named him to the realm.
- **The sight check's drop:** the game refreshes each party's "seen" flag every frame, but only within its largest sight range, plus 5, of the player's party, and never for garrisons and militias (`CampaignTickCacheDataStore.UpdateVisibilitiesAroundMainParty`, read in the game's code). Beyond that a flag stays as it was. On Oct 22 Naglur's orc army (12 parties), seen days before, was still flagged seen 210–247 away (Mert's sight is 47.7) in all 42 checks. Those flags made 504 of 504 disagreements in one launch and 360 of 364 in the other. Oct 16's main disagreements were garrisons and militia. **Our sight rule was right:** with those taken out, every launch from Oct 8 to Oct 23 agrees on 99.9–100% (recomputed from the logged disagreements).
- **What changed** ([LOGGING.md](LOGGING.md#what-the-orcs-see)):
  - the fog check also takes the realm's own lords riding in its armies, and the parties its poke is about, which the game's news named to it (`Brain.Known`);
  - a party the news named but the realm never saw now reads "not seen by your eyes, only named in the news" instead of "last seen 9999 hours ago" (`Facts.ForPoke`);
  - the sight check compares only where the game itself refreshes its flags: from the player's party (or the port town it besieges or raids, with a third more sight, as the game does), within the game's largest sight range plus 5, garrisons and militias left out (`Vision.Audit`).
  - Nothing the agents see changed.
  - Version 0.10.4 (the module; the war room is unchanged at 0.10.1).
- **Checks:**
  - the smoke test passed in full, with 2 new checks rebuilding both cases (an army rider carrying loot, and a raider the news named but the realm never saw). The new fog check test fails on the old code (3 leaks) and passes on the new;
  - the war room's and the dashboard's self-tests passed at the install;
  - 612 game references, 0 missing; all 7 hooks bound in the offline dry run;
  - the installed DLL has sha256 `63B98CE5EBAA2859…`, the war room's `WarRoom.dll` (rebuilt, same code) `D837EF65793F935D…`;
  - the war rooms (three) and the dashboard were stopped for the build and started again at 15:34.
- **Not yet seen in a game:** the sight check's new numbers (it needs the game; expected near 100%).

### 16:04 · Version 0.11.0: the test runs' record, scoreboard, setups and watcher · commit d416ad5 (built and installed 16:04)
- Mert's test plan (Oct 23, [RUNS.md](RUNS.md)): "All tests will be for 60 in-game days, starting from the TEST START save". **A:** Mert against the orcs' and Dol Guldur's agents (first). **B:** Mert against the game's own AI ("If I can"). **C1, C2:** "give control of my kingdom first to an agent and have it play against other agents and against the game's raw ai, and see how its performance compares to me".
- His choices for the agent-ruler runs: "switch the player character to a wanderer in a random town, give that character a lot of money and food so it doesn't die, and simply let the game run". The lieutenant stays on when he plays. The rivals' agents stay on, watching, against the game's AI "so long as usage is fine". "lets try to track token usage stats as well."
- **The run record in the bridge** (new `Runs.cs`: `Run`, `RunRules`, `RunDay`, `RunFiles`):
  - Loading TEST START (`run_save`) readies a run. It begins on the first in-game hour the clock runs, named by who commands each side (A, B, C1, C2). It ends after `run_days` (60) × 24 in-game hours: the game pauses and offers a save "TEST END (arm) (run)".
  - A load of one of the run's own saves carries it on, an autosave after a crash included; reloaded hours are marked. Loading TEST START again ends the old run unfinished.
  - **Its folder** `%LOCALAPPDATA%\BannerlordAgents\runs\(run)\`:
    - `run.json`: who commands what, the loads, the saves, the marks against it, the running tally, the game's war tally at the start;
    - `setup.json`: the versions, every mod, each agent's mode, war room settings and fingerprints of its instructions, the save's fingerprint;
    - `days.jsonl`: the scoreboard at day 0 and every 24 in-game hours, per realm (holdings by kind, garrisons, lords, men, strength, armies, loot, prisoners, lords alive, held and holding, clans' gold and daily change, influence, recruits that day, the tally), the game's own war tally per pair (casualties, sieges, towns and raids won, tribute), the player's party;
    - `events.jsonl`: battles with each side's realm, leader, who commanded it (you, your lieutenant, an agent, the game), men and losses; holdings changing hands; sieges; lords taken, killed and freed; villages looted; armies; war and peace; `agents.run note` lines.
  - **The run's copy of the detailed log:** `Diag` tees the run kinds into `events.jsonl`, kept whole past the trace's 200 MB cap: pokes, answers and receipts, orders, the lieutenant, decisions, alarms, errors, freezes, the fog and sight checks, speed, saves and loads. Left out: an order's hourly line, lord look-ups and tool steps (4,100 hourly lines in one 3-hour launch).
  - **Marks against a run:** true sight, cheat mode, a change of commander after the start, a late start, a crash, replayed hours, a stop by hand.
  - **Console:** `agents.run [check|note (text)|stop]`. **Settings:** `run_save`, `run_days`, `run_rivals`, `run_kingdom`.
  - **Two agents naming one realm:** the one switched on plays it (`Bridge.Start`), so the lieutenant and the kingdom's agent can both stay listed. `Orders.SourceOf` says who gave a lord's order.
- **`Runs.exe`** (new project `src/BannerlordAgents.Runs`, read-only):
  - `watch`: what is new since the last look, with NEEDS A LOOK first and exit code 1 then;
  - `status`;
  - `report`: `report.md` and `report.json`;
  - `compare`: `runs\compare.md`.
  - **Token use** per agent and kind of call, from each war room's `calls.jsonl` within the run's real-time spans: tokens read (fresh, from the cache, written to it), written and thinking, list price, per in-game day, per real hour, the most in any 5 hours, failed calls and usage-limit hits.
- **`scripts\arm.ps1 -Arm A|B|C1|C2|show`:** switches the four agents' modes in their `agent.txt` (read at every load, so no restart), logs each switch in `runs\arms.log`, and prints the in-game steps.
- **Docs:** [RUNS.md](RUNS.md) (new), PLAN.md's checkpoint, CLAUDE.md. A control character in CLAUDE.md's code map (`scripts\agents.ps1` had lost its backslash-a in an earlier edit) is put right.
- **Outside the repository:**
  - **The kingdom's agent** `C:\Users\karab\Projects\kingdom-war-council` (`scripts\setup-agent.ps1 -Realm new_kingdom -Mode off`): its instructions are the realm template word for word, only the title and opening line its own ("the Kingdom of Arnor and Gondor, the reunited realm of the Dúnedain of the north and the south"; no ruler named). Its war room settings are the orcs' (effort high, council on), logs in `logs\warroom-new_kingdom`.
  - **The bridge's settings:** `agents` now lists it fourth (backup `settings.txt.bak-20261023-before-kingdom-agent`); the next launch writes the `run_*` keys.
  - **Arm A set** at 16:01 (no mode changed: orcs and Dol Guldur live, lieutenant live, the kingdom's agent off); the memory index's same lost backslash-a is put right.
- **Checks:**
  - the smoke test passed in full, with 5 new checks: the arms from who commands; the run's days and its end at 1,440 hours; the day line from an hour's copy; the state read back as written; the run's copy of the log holding only its kinds while it runs;
  - the war room's and the dashboard's self-tests passed at the install;
  - 630 game references, 0 missing (the new ones: the game's war tally, saving under a name, a pop-up's two buttons, heroes held prisoner); all 7 hooks bound;
  - `Runs.exe` tried on a made-up run whose agents' logs were the real ones of Oct 22, 20:22: status, two watches (the second only what was new), report and compare;
  - the installed DLL has sha256 `063E6A5FEF59F2CC…`, the war room's `WarRoom.dll` (rebuilt, same code) `FA4847C5D6BA8A3E…`;
  - the war rooms, now four, and the dashboard were stopped for the build and started again at 16:04.
- **Not yet seen in a game:** all of it (see RUNS.md).

### 16:20 · Run A started and paused (0.11.0)
- Mert loaded TEST START, let the clock run, and the run began at 16:13 as `20261023-1613-A` (A, him against the agents); he saved TEST A at 16:19 and closed the game (4 in-game hours played). The run's record, day 0 line, the agents' first answers (orcs 43 orders, Dol Guldur 11, all carried out) and the lieutenant's first answer are in `runs\20261023-1613-A`.
- Found on the way: the start-up check showed the kingdom's own agent, switched off for run A, as a red problem; cheat mode is on in Mert's game (noted against the run); a save made before the clock first ran would not have counted as the run's; the run's first folder (`20261023-1613`, before it was named) stayed behind; the lieutenant's usage read $0.00 (its calls' log had no price); Dol Guldur is at peace with Mert's kingdom at the start of TEST START (only the Misty Mountain Orcs are at war with him).
- Mert asked to fix the lieutenant and Party AI Controls "before going deeper into test A".

### 16:55 · Version 0.11.1: the lieutenant works with Party AI Controls, and its conversation is the save's · commit e6f41d7 (built and installed 16:54)
- **Mert, Oct 23:** "The liutenant should be able to work in tandem with party ai controls. Any order given by the liutenant should override any order given by party ai controls, and it should be able to change backup orders, troop templates, and all options as well. All changes made by the liutenant and all of its orders should also be reflected in the dashboards of party ai controls as well." And: "the chat I have with my liutenant should be persistent within a save, even if each prompt is technically a different session. This is something interacting with a human, and I want to be able to reference previous things that were said in the chat throughout a save. The liutenant should also have access to the chat I have with it so I can refer back to it. This is something specific to the liutenant, so shouldn't touch the other agents."
- **The lieutenant and Party AI Controls** (new `PacLink.cs`, docs/LIEUTENANT.md "Party AI Controls"):
  - its orders are still carried out by our order layer, as every agent's (the comparison runs need that), and each shows in Party AI Controls' panel as that lord's order: the nearest of its kinds (`PacRules.MirrorOf`), or none for a raid, a shadow, a watch post on open ground, armies. It replaces his Party AI Controls order (0.10 refused such a lord); his queue and backup order stay, and when it ends the next queued order starts there;
  - while it runs, Party AI Controls' hourly look (`KeepHeldPatch`, now a prefix that can skip) and its gate check (new `PacEntryPatch` on `PartyAIThinker.OnSettlementEntered`) pass him by, so the two never pull against each other;
  - the ruler wins: a new Party AI Controls order for him meanwhile (the panel, or "I have orders for you") ends the lieutenant's order. This closes the 0.11.0 gap Mert was told about;
  - `party_ai`, in the lieutenant's library only: his Party AI Controls order (carried out by Party AI Controls), queue, backup order, template or a new one, troop mix, every per-lord option of the panel, or handing a vassal back;
  - a vassal it commands is put on TFAOMFixes' commanded list (by name; TFAOMFixes is not changed) as the ruler's own talk does, with auto-recruit and dismissal off on a new entry, and taken off again when nothing of Party AI Controls' is set for him;
  - mirrors, and entries and list places made only for them, come out before every save and go back after; what `party_ai` sets stays (Party AI Controls' own state, on purpose), and is recorded in `data\<campaign>\lieutenant\party_ai.jsonl`;
  - each message carries PARTY AI CONTROLS NOW (read when Mert sends words, not hourly); the recorder no longer takes the lieutenant's Party AI Controls orders for Mert's, and a run's battles say "lieutenant (Party AI Controls)".
  - Checked against the installed Party AI Controls 1.4.5, decompiled today: the old decompile at refsrc\PAI is an older version (no `RecruitFromEnemySettlements`, no `SetPartyTemplate`, `DismissUnwantedTroops` default true there). CLAUDE.md's `Settings(Hero)` rule now names PacLink as the one exception.
- **The conversation of a save** (`Lieutenant.Keep`, `Restore`, `History.HoldsLine`): every line goes into `data\<campaign>\lieutenant\chat.jsonl`, labelled with the session, its in-game hour and the wall clock; a load keeps its save's lines (the chronicle's rule; saves' notes now carry the wall clock, since most of the conversation happens paused), shows them in the window and puts back a proposal still waiting. `chat.json` carries the latest 60 lines, `conversation.txt` the whole; the war room uses them instead of its own memory, which had mixed saves (the first two chats of Oct 23, from another save at a later date, came into TEST START's).
- **The test runs:** a save made before the clock first runs now counts, and loading it readies the run again; the readied folder goes once the run is named; the watcher says "N of 60 days done" (it said "day 0"); the player's own realm's reports (kept, never sent to the lieutenant) are no longer counted as pokes (`sent` in the trace).
- **Smaller:** an agent switched off and set aside for the one switched on is logged, no longer a red self-check problem; the "at war with you" check leaves out your own realm and says "at peace with you now"; the lieutenant's calls log their price and cache writes.
- **Outside the repository:** the lieutenant's instructions `C:\Users\karab\Projects\lieutenant\warroom-system.md` replaced by the new template (Party AI Controls section; the save's conversation and `conversation.txt`; a lord with a Party AI Controls order takes its order once agreed), backup `warroom-system.md.bak-20261023-before-0.11.1`. No strategy in it.
- **Checks:** smoke test passed in full (240 checks; 8 new: the mirrors, party versus holding targets, the options and the troop mix in Party AI Controls' terms, the library section, a save's conversation brought back exactly, this session's and unlabelled lines, notes without a wall clock); war room self-test passed (1 new: the save's conversation and Party AI Controls in the message); dashboard self-test 27 of 27; 642 game references, 0 missing; every Party AI Controls and TFAOMFixes name used found in the installed DLLs. Installed DLL sha256 `941CA2D3B2D974E5…`, `WarRoom.dll` `E9A1884F5B7BE1F6…`. War rooms and dashboard stopped for the build, started again 16:55.
- **Not yet seen in a game:** all of 0.11.1. Run A is to start again from TEST START (the old run, 4 in-game hours, ends unfinished when TEST START is loaded).

### 17:08 · Arm C1 set for the first playerless run
- Mert, Oct 23: "Going to the driving range for a bit, so i want to start one of the playerless test saves as well." C1 is the plan's next arm (docs/RUNS.md: an agent rules his kingdom against the orcs' and Dol Guldur's agents).
- **Outside the repository** (`scripts\arm.ps1 -Arm C1`, logged in `runs\arms.log`): the kingdom's agent `C:\Users\karab\Projects\kingdom-war-council` off -> live, the lieutenant live -> off; the orcs and Dol Guldur stay live. Read at the next load.
- The old run A (`20261023-1613-A`, 4 in-game hours) ends unfinished when TEST START is loaded; A is to be played again later.

### 17:59 · Version 0.11.2: the playerless switch as a command · commit 0b6d40a (built and installed 17:59)
- **Mert, Oct 23:** "The switch should be done by a command that you write, including the giving them gold and food part. Please find a good wanderer to switch into and prepare the command." Then: "no need to guard against those [pop-ups] for now. Just write the switch command and report back, if something does end up blocking us, we'll respond after."
- **`agents.run playerless [go] [name]`** (new `Playerless.cs`): without `go` it shows the wanderer it would take (a free wanderer, no clan, nobody's companion, no party, in a town at peace with the kingdom, not besieged and not the kingdom's nor a rival's; the town farthest from every castle, town and lord at war with the kingdom or with the town's owner) and the next four; `go` pauses the game, releases every kingdom lord holding a Party AI Controls order (`PacLink.Release`: order, queue and backup order cleared, off TFAOMFixes' commanded list), takes the wanderer into the player's clan as a companion (`AddCompanionAction`), switches to him with the character-switch mod's own `SwitchActions.SwitchPlayer` (by name), lets the old character's party go to the game's AI (that switch holds it with the AI off and a Party AI Controls stay order), gives him 100,000 denars and 100 grain, and takes him into the town. Kept in run.json's `set_up`.
- **Why the wanderer joins the clan first:** the switch mod only switches within the player's clan (its button is on the clan screen), and the game cannot make a party for a hero of no clan (`LordPartyComponent.InitializeLordPartyProperties` reads his clan's party template; read in the 1.4.8 source).
- **Found, not guarded (Mert's call):** the game's player clan is fixed (`Clan.PlayerClan` is the clan `player_faction`, `Campaign.PlayerDefaultFaction`), so after the switch the kingdom's ruling clan is still the player's: its decisions wait for the player once due (`KingdomDecision.NeedsPlayerResolution`) and the map opens the kingdom screen for them. The ruler's choice itself goes to the AI (`IsPlayerChooser` reads `Leader.IsHumanPlayerCharacter`). The switch mod's "Party in combat! Switch?" pop-up (`NotifyWhenAttacked`, on by default) fires for battles of the player character's own clan, which the wanderer now is in. Party AI Controls may still give the clan's parties orders by itself (auto-recruit, backup orders).
- **Checks:** smoke test, war room and dashboard self-tests passed; 652 game references, 0 missing; `PlayerSelectionSwitch.SwitchActions.SwitchPlayer(Hero)` is public static in the installed 1.0.3 (decompiled). Installed DLL sha256 `FBBD66715218D62A…`. War rooms and dashboard stopped for the build, started again 17:59.
- **Not yet seen in a game:** the command.

### 18:05–19:26 · Run C1 played (0.11.2), unattended
- Mert, Oct 23: "Going to the driving range for a bit, so i want to start one of the playerless test saves as well." He loaded TEST START and typed `agents.run playerless` (it chose Marzuk ibn-Zayak in Damudûr, Âbanissa, 451 from the nearest war), then `agents.run playerless go` (4 lords released from Party AI Controls orders, the switch, 100,000 denars and 100 grain, into the town), and `agents.run check` (C1).
- Run `20261023-1802-C1` began 18:05:12, finished 19:25:43 (60 days, 1 h 20 min at about 80 s an in-game day, no stall or crash). The kingdom's agent ended +1 town, +3 castles, +12 villages, 154 enemy lords taken; the orcs −2 castles, −7 villages; Dol Guldur −1 town, −1 castle, −5 villages. About $276 at list price (orcs $106.69, Dol Guldur $84.79, the kingdom $84.97), no usage limit met. Report: `runs\20261023-1802-C1\report.md`.
- Mert: "dive into the report and all things you flagged. Make all necessary fixes." Four investigations (the frozen lords; stuck and fought orders with the slowness alarms; the battle counts and the fog check; gold, the failed story, the first timeout and the votes) found what 0.11.3 fixes.

### 20:25 · Version 0.11.3: what run C1 showed, fixed · commit daf0ec7 (built and installed 20:25)
- **Kingdom votes never settled in C1** (`Votes.cs`, new). The game's player clan is fixed (`Clan.PlayerClan` is `player_faction`), so after the playerless switch the kingdom was still the player's to the game. A due vote waits for the player (`KingdomDecision.NeedsPlayerResolution`), and the map opens the kingdom screen only when no menu is up.
  - In 60 days: 2 votes proposed (Bailiffs, the owner of Sirannon), 0 settled.
  - The game puts one holding's vote at a time, so none of the 6 castles and the town taken were given out. They stayed with the ruler's clan.
  - Now, while the player's clan rules and the player is not its ruler, a due vote is settled as for a computer-run realm (`KingdomElection.StartElectionWithoutPlayer`).
  - Any of our realms' votes still open a day after its time raises `vote_waiting`.
- **Ordered lords never "arrived" at villages.** The game steps a lord into a village and straight back out (`EncounterManager.StartSettlementEncounter`), and our checks looked only at "inside".
  - 1,606 of 1,771 orders given again in C1 were lords standing at their village.
  - Recruit trips bought at 2 of 1,562 village stops, and bounced between places.
  - `Orders.Where`/`At` now use the game's own test (`MobilePartyHelper.GetCurrentSettlementOfMobilePartyForAICalculation`), in the arrival check, on course, the trips' purchases and their next stop.
  - The next stop is now the nearest by road, the game's distance through the lord's entrance; a tenth of C1's legs ran 2.6 times the straight line.
- **The battle record missed the losing side's lords.** The game takes them before it tells of the battle's end.
  - The record said 2 battles between lords; Runs.exe's re-score of C1 says 109.
  - `Run.OnBattleStarted` now keeps each side's men, lords, leader and commander at the start, and lords are also found among the men, wounded and dead.
  - Leaders are named through the party's owner.
  - The report re-scores older runs with the same-hour capture test. "Men lost" now says killed, wounded or routed.
- **28 "still held after release" alarms were TFAOMFixes' recruiting trip** (`LordRecruiting`), which takes a lord below half his ranks the hour after our order ends.
  - `LordTours.cs` (new, by name, read and drop only) names the trip in the check instead of alarming.
  - Any order of ours now takes a lord off a trip: the trip keeps a lord whose place matches ours, and could later switch his AI on under our order.
  - Lords who left the realm are no longer watched after release.
- **The fog check's 70 flagged reports were false.** There was one real leak behind them.
  - The false ones: 61 were spots of the realm's own past watch posts, now skipped. 9 were the holding of a siege seen only by its camp, now known.
  - The leak: such a siege's line showed the defenders' strength, the food and the storm's timing (`Forecast.SiegeText(known: false)`, `Views`). Now it shows only the besiegers.
- **Gold:**
  - The orcs' −10.6M was mostly five hired clans leaving with 8.9M. Day lines now have `hired_clans`, `hired_gold` and `sworn_gold`, and clans joining or leaving are run events ("clan moved", with gold).
  - Holdings given out within a realm are events ("holding given", with the clans); holding events carry the clans.
  - The 27 of 114 C1 prisoner hand-overs paid 0 (4,051 men) are the game's own rule, not a fault. An enemy lord handed to one's own realm's holding goes to its dungeon as a gift that earns the realm influence, and the game then pays for none of that hand-over, its soldiers included (`SellPrisonersAction`). Mert, Oct 23: "Lords can sometimes place prisoners in the dungeon instead of selling them. That's probably what happened."
  - A split sale (soldiers apart from lords, written first) was rolled back: it would have paid the agents' sell order where the game's own lords get nothing. `Purse.CashIn` keeps the game's single hand-over, and the record now names these "prisoners given to a dungeon", with the lords counted.
- **The alarms:**
  - `order_stuck` (470) now needs the lord to have stood still for the 6 hours too. It skips a besieger at his camp and a lord near his place.
  - `slow_call`: the hourly copy (10 to 18 ms, every hour) now alarms over 33 ms, other hooks still over 5 ms.
  - An answer's orders spread over ticks (2 ms a tick); the 5 to 34 ms spikes were 8 orders at once.
  - The look at the answer folders goes before the worker's queue (`Background.PostFirst`): `scan_stopped` came with 214 to 621 jobs queued, answers waiting up to 30 s.
- **The defend order** gives up a chase that has not closed in after 4 hours (`chase_gap`), and its `about` says so. 88 C1 "stuck" alarms were chases of a faster enemy.
- **War room:**
  - Raw line breaks inside an answer's text are now read (`Room.EscapeBreaks`, in both rooms). The orcs' story was thrown away once for six of them.
  - The first look since a load gets the council's time (420 s). The kingdom's first answer was stopped at 122 s of 120.
- **For comparisons:** C1 was played with the vote and village faults, so it should be played again on 0.11.3 before it stands beside A, B and C2 (RUNS.md).
- **Not changed (for Mert):**
  - The rhythm of the agents' pokes: 86% of the orcs' answers answered a poke already replaced, and enemy-in-view pokes could be thinned.
  - Party AI Controls' own orders for the clan's parties in C runs.
- **Checks:**
  - Smoke test passed in full: 242 checks, 2 new (a map spot in a past order is no leak; a siege seen only by its camp shows only the besiegers).
  - War room self-test passed (new: raw line breaks read). Dashboard 27 of 27.
  - 671 game references, 0 missing. Every TFAOMFixes and game member by name found in the installed DLLs and the 1.4.8 source.
  - Installed DLL sha256 `FCF324DE2CF03F6D…`, `WarRoom.dll` `D67C698333409FDD…`.
  - War rooms and dashboard stopped for the build, started again 20:25.
- **Not yet seen in a game:** all of 0.11.3.

### 20:46 · Arm C2 set for the second playerless run
- Mert, Oct 23: "Lets do the other non-player run next." C2 (docs/RUNS.md): an agent rules his kingdom against the game's own AI, the orcs' and Dol Guldur's agents watching (they answer, give no order).
- **Outside the repository** (`scripts\arm.ps1 -Arm C2`, logged in `runs\arms.log`): the orcs `C:\Users\karab\Projects\orc-war-council` and Dol Guldur `C:\Users\karab\Projects\dolguldur-war-council` live -> shadow; the kingdom's agent stays live, the lieutenant off. Read at the next load; the war rooms (started 20:25 on 0.11.3) keep running.
- `scripts\arm.ps1`: its in-game steps for C1 and C2 now name `agents.run playerless` and `agents.run playerless go` (they still said to switch by hand with the character-switch mod and give gold and food), and the check names the arm set.

### 20:55–21:45 · Run C2 played (0.11.3), stopped at day 31
- Mert, Oct 23: "Lets do the other non-player run next." He first loaded `TEST C1`, a save of C1's from its setting up, which readies no run (C1 being finished); no run began, and the watch said so. He loaded `TEST START`, typed `agents.run playerless go` (Marzuk ibn-Zayak in Damudûr again) and let the clock run.
- Run `20261023-2101-C2` began 21:01:27 and was left at day 31 (Winter 9, 1087) when the game closed at 21:45. The kingdom's agent ended +1 town (Nanduhirion), −1 castle, +2 villages, 74 enemy lords taken against 76 of its own; Dol Guldur +2 castles; the orcs −1 town, −2 castles, −9 villages. About $106 at list price (the kingdom's agent $39.59, Dol Guldur's $34.41, the orcs' $32.13). Report: `runs\20261023-2101-C2\report.md`.
- **Day 9-11, Imnagath:** Aragorn's and Imák's armies sheltered in the castle on the agent's orders from Lorgath's army (20 lords, 10,302 men, strength 22.6k). The game sent everyone inside (5,938 men, 20 lords) out the hour the siege began (`SallyOutsCampaignBehavior.CheckSallyOut`); 46 hours later all were lost (Lorgath lost 4,103) and the castle fell. Read in the 1.4.8 source, the installed game, TAOM's and Improved Garrisons' code: the game's own rule, which no mod touches; our orders cannot order a sally. Why the game counted so little of Lorgath (it needs the inside above twice the enemies within 3 of the camp) is not known.
- Days 21 and 24: Alenon's army (8 lords) and Val's (10 lords) were each destroyed in the open, by Lorgath and by Gundabad's Mauhur. Day 23: Stein destroyed Naglur's orc army (1,677 of 1,686 men).
- A read of C1 and C2 for Mert's question, "why did such large armies never come about in our agents' runs?" (a helper agent, read only):
  - The agents spread their lords over five to eight armies sized to what is in front of each (C2 day 9: 6 armies, 16,478 men, about 2,750 each, against Lorgath's one of 10,302).
  - The news of the big enemy army is buried between answers (all three Lorgath pokes went unanswered while the agent answered raiders).
  - In C1 the rival agents held nearly all their lords, which the game's own army-building passes over.
  - The call-up order refuses when the clan cannot pay every lord at once; the game needs 100 influence and lets the clan go into debt.
  - Not changed.
- Mert: "We are going to have to run this test again some other time, from the start. What killed it was one bug that forced our troops out of a castle they should have defended. We should focus on fixing that bug, and then try again. In the ideal world, our agents should easily beat the raw game's strategy."

### 21:56 · Version 0.11.4: the game's sally kept from our sheltering lords · commit 9066778 (built and installed 21:56)
- **The sally rule** (`SallyGate.cs`, new; a prefix on `SallyOutsCampaignBehavior.CheckSallyOut`, by name):
  - While a lord one of our orders holds is inside a besieged castle or town, the game's own test does not send the defenders out, as it spares a defence the player leads from inside.
  - They still join a relief's battle at the camp by the game's bar (1.5 times), which the relieve and defend orders count on.
  - Every other holding keeps the game's test untouched.
  - For the holdings of the realms with agents, every test is recorded with the game's own numbers, a "sally" event in the trace: inside, friends and enemies as it counts them, the whole camp, and each besieging party it did not count, with its distance, aggressiveness and whether attached. The bridge log names each change of decision.
- **Each agent's poke and report name its own mode** (`Channel.ModeText`). They named the first agent's (`AgentChannel.ModeText()`), so in C2 the kingdom's live agent read "SHADOW ... none is carried out" in every poke (arm B too).
- **The facts on a besieged holding** say the defenders never go out on their own while a lord holding your order is inside, and that otherwise the game sends them out by itself above twice the besiegers at their camp (every 4 hours).
- **Labels:**
  - The run's war tally reads the game's full test (`FactionManager.IsAtWarAgainstFaction`), not the stance alone: C2's record had Dol Guldur and the kingdom at peace.
  - The self-check "at war with you" runs at the first in-game hour, not at the load: TFAOMFixes fills in the War of the Ring's enemies for the player's realm just after this mod, so every load warned of peace with Dol Guldur.
  - Armies formed by our gather are recorded as the order's giver's (`Orders.FormingBy`); they read "the game's".
- **Checks:**
  - Smoke test passed in full (244 checks; 2 new: each agent's poke and report name its own mode, shown to fail on 0.11.3's line).
  - War room and dashboard self-tests passed.
  - 679 game references, 0 missing; `CheckSallyOut(Settlement, bool, out bool)` confirmed by name in the installed `TaleWorlds.CampaignSystem.dll`.
  - Installed DLL sha256 `3BCF6C85749079C1…`; `WarRoom.dll` unchanged (`20C913C4C4819064…`).
  - War rooms and dashboard stopped for the build, started again 21:56.
- **Not yet seen in a game:** all of 0.11.4.

### 22:11 · The agents' instructions: one main effort, the other fronts held
- **Mert, Oct 23:** "We should also design the agent's system prompt to have it be more focused in its strategy. It is better to mass an overwhelming force in one front to destroy enemies one by one, whilst moving smaller but still sizeable and adequate forces defensively to other fronts to keep the stalemate there, than looking at each front equally."
- Written as a general principle in the doctrine's own words, the same for every realm agent, not as Mert's sentence and with nothing of this war's fronts, armies or places (his rule: "I don't want to give it the strategy that I just gave you word for word"). The design and its grounding: docs/DOCTRINE.md, "Principle 5 rewritten (Oct 23)".
- **What changed** (`agent/warroom-system.template.md`):
  - **Principle 5,** "Concentrate", is now "One main effort; hold the other fronts":
    - one front's army is sized to beat the strongest enemy army that can reach it by a wide margin, destroys the enemy's armies there one after another, then turns to the next front;
    - every other front gets a smaller holding force, enough to keep its castles and drive off its raiders;
    - every army is sized against the strongest enemy army within a few days' reach, and two armies in reach of one stronger enemy army join or keep out of its reach.
  - **Who you are:** a ninth line, weight on one front and the others held with less (Edward III, 1346: Crécy, while Aiguillon held and Neville's Cross was won).
  - **The council of war:** step 2 names the main effort and the held fronts. Step 3, "Give every lord a part in it", is now "Fill the main army first", then the holding forces, then the rest. Step 4 writes them into the plan.
  - **Time:** what a front's holding force can meet is met from its own lords, with the main army kept on its task; an enemy army stronger than any of the realm's, or able to reach the main army, is answered for the whole realm.
- **Outside the repository:**
  - Each realm agent's `warroom-system.md` was rewritten from the new template: `C:\Users\karab\Projects\orc-war-council`, `dolguldur-war-council` and `kingdom-war-council`. Each keeps its own name and opening line, and is otherwise word for word the template.
  - Backups are `warroom-system.md.bak-20261023-before-main-effort`.
  - The war rooms read the file at each call, so it holds from their next answer, with no restart.
  - The lieutenant's instructions have no doctrine and are unchanged.
- **For comparisons:** C1 and the stopped C2 were played on the old instructions; the runs still to be played (A, B, C1 and C2 again) are on these.

## Saturday, October 24

### 14:05 · The agents' instructions: principle 5 softened, overwhelm where a win is open
- **Mert, Oct 24:** "I don't like the wording on principle 5. It should be less strong, telling the agent to strategize such that, if it can win in a front with overwhelming force, it should concentrate overwhelming force there instead of just an adequate one, without taking away so much from the other fronts to make their forces inadequate. This might work for multiple fronts, just one, or none, based on the relative strength of its enemies."
- Again a general principle in the doctrine's own words, the same for every realm agent, with nothing of this war in it. The design: docs/DOCTRINE.md, "Principle 5 softened (Oct 24)".
- **What changed** (`agent/warroom-system.template.md`):
  - **Principle 5** is now "Overwhelm where you can win; keep enough everywhere else":
    - where an enemy army can be beaten, bring all the strength you can spare, not only enough, so the win is by a wide margin and the army stays whole for the next blow;
    - what can be spared is what the other fronts do not need: each keeps enough to hold its places and turn back the enemies that can reach it, never less;
    - such blows on several fronts against weak enemies, perhaps one against strong ones, none against enemies stronger everywhere (then hold and wait, principles 4, 6 and 17);
    - kept: every army sized against the strongest enemy army in reach, march apart and meet, never strip every garrison. Gone: one chosen main effort and "holding forces" told not to win.
  - **Who you are:** the ninth line is now "You bring all you can spare to the blow, and leave each other front enough to hold" (1346, cited for the fronts that held, not for Crécy).
  - **The council of war:** step 2 names what each front needs and where a blow can be won, if anywhere; step 3 is "Give every lord a part in it" again: the fronts' needs first, then all that can be spared to the blows, then lesser tasks; step 4 writes both into the plan.
  - **Time:** a front's own lords meet what they can and striking armies stay on their tasks; an enemy army stronger than the lords facing it, or able to reach one of the realm's armies, is answered for the whole realm.
- **Outside the repository:**
  - Each realm agent's `warroom-system.md` (orc, Dol Guldur and kingdom war councils) rewritten from the new template, each keeping its own name and opening line, otherwise word for word the template.
  - Backups are `warroom-system.md.bak-20261024-before-softer-5`.
  - In force from their next answer, no restart. The lieutenant's instructions are unchanged.
- **For comparisons:** no run was played on the Oct 23 wording; the runs still to be played are on this one.

### 14:40 · The pack's "calls for aid" pop-ups switched off for the test runs (outside the repository)
- **Mert, Oct 24:** "Looks like the only pop-ups that jeopardize the test are the notifications saying 'x is besieged, will you answer the call for aid?'. Can you figure out a way to disable them so we can run the test smoothly?"
- **What they are:** TAOM's own siege-defence events (`TAOM.Features.Siege.SiegeDefenseService`, read from TAOM.dll). When a town of the player's realm is besieged, it shows an inquiry ("Gondor Calls For Aid!", from `ModuleData\siege\siege_defense_config.json`) with "For Gondor!" and "Ignore"; answering and reaching the town in 3 days gives the player +10 influence and +5 relation with the realm's ruler. Towns only, the player's realm only; nothing in it moves a lord or an army, or changes what any AI does.
- **The switch:** the pack's own Mod Options setting "Enable Siege Defense Events" (TAOM, group "Siege Defense", `EnableSiegeDefenseEvents`, on by default). The pack's settings had never been saved, so the file was written new with only that setting: `Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\TAOM\TAOM.json`, `{"EnableSiegeDefenseEvents": false}`. Every other setting of the pack keeps its default. Written with the game closed.
- It is global: off for every campaign and every arm until switched back on in Mod Options. Mert to confirm the box is unticked in Mod Options before loading TEST START.
- **For comparisons:** C1 and the stopped C2 were played with the pop-ups on; every run from the C2 replay on has them off.

### 15:30–16:52 · Run C2 replayed (0.11.4), finished
- `20261024-1530-C2`: the kingdom's agent live against the game's AI, the orcs' and Dol Guldur's agents watching; the softened principle 5 and the pack's siege pop-ups off. War rooms and dashboard restarted 14:29 before it (`scripts\agents.ps1 restart`).
- 60 days in 1 h 20 min; about $236 at list price; no errors, freezes, fog leaks or failed calls; one mark (cheat mode during setting up, expected).
- The kingdom ended +1 town, ±0 castles, +4 villages, with 180 enemy lords taken; Dol Guldur's great army was destroyed at Lasgalor on day 40, at 17,565 of our men lost. Findings in docs/RUNS.md, "Run C2 replayed (Oct 24)".

### 17:26 · Version 0.11.5: the council of war sits on time in a busy realm · commit e2f0ef6 (built and installed 17:26)
- **Mert, Oct 24:** "Can you try to diagnose why the agent wasn't as aggressive in pressing its advantage after its large victory at lasgalor against dol guldur?" Then: "Yes, build and install the fix please."
- **What was found** (the kingdom war room's log and calls in run C2):
  - The war room looked for a due council only when no poke was waiting (`Room.Run`). The kingdom's pokes came every few seconds against answers of 10 to 30 s, so its war room was idle 44 times in the run's 80 minutes; the two watching agents' war rooms, with fewer pokes, idled often.
  - About 20 councils sat in 60 days instead of at least one a day; the log shows councils sitting "129 hours" and "95 hours" after the last. The council of Winter 16 asked for the next in 12 hours; it sat on Spring 3, 8.7 in-game days later. In the 11 real minutes after the victory at Lasgalor the war room answered 51 pokes, almost all about small raiders in view, and was idle once.
  - Between councils the fast answers did what their instructions say (decide what the poke needs, leave the realm's course to the council): defend 10, engage 6, gather 3, no siege. The first council after the victory said at once "Dol Guldur has no field army left near us" and sent Stein, Dáin and Imák to besiege Imnagath, then Caras Laerolin.
  - The same gap (Winter 9 to Winter 14, 129 hours) fell while Lasgalor starved and its relief came in pieces.
- **The fix** (`src/BannerlordAgents.WarRoom/Room.cs`): after every answer the war room looks whether a council is due and, if so, sets it sitting on its own thread beside the next answers, as it already did in a quiet moment. Nothing in the agents' instructions, the bridge or the game changed. docs/WARROOM.md says so under "When it sits".
- Versions: `BannerlordAgents.WarRoom.csproj`, `BannerlordAgents.csproj` and `module/SubModule.xml` set to 0.11.5 (the module's code is unchanged; the version marks the runs played with the fix).
- **Checks:** build with no warnings; smoke test, war room self-test and dashboard self-test passed. The loop itself makes calls and has no self-test: the next run's councils, one at least every in-game day, will show it. `WarRoom.dll` sha256 `14436EDCE3DE5495…`, installed `BannerlordAgents.dll` `153EA7BA7FE78D83…`. War rooms and dashboard stopped for the build and started again 17:26 (each reports "War room 0.11.5.0").
- **For comparisons:** C1 and both C2 runs were played with councils starved in the busiest realm; expect more councils, and more council tokens, from now on.

### 21:06 · The game no longer stops when its window is not in front (outside the repository)
- **Mert, Oct 24:** "The game currently pauses itself when I switch to a different tab. Can you disable that so I can do other stuff as a test runs?"
- **The switch:** the game's own option "Stop game on focus lost" (Options, gameplay), saved as `StopGameOnFocusLost` in `Documents\Mount and Blade II Bannerlord\Configs\BannerlordConfig.txt` (the Documents folder is `OneDrive\Belgeler`). Set from `True` to `False`; that one line changed, nothing else in the file. Written with the game closed (it had last saved the file at 21:05, on exit).
- Nothing of ours, the pack's or the agents' changed; the setting is global, for every campaign and arm, until switched back in Options.
- **For comparisons:** only when the clock runs changes, not what happens in a game day; earlier runs stopped whenever another window was in front. docs/RUNS.md notes it beside the pop-ups.

### 21:10–22:31 · Run C2 played again (0.11.5), finished
- `20261024-2110-C2`: the kingdom's agent live against the game's AI, the orcs' and Dol Guldur's agents watching; the first run with the council fix, the same instructions as the replay, the siege pop-ups off and the game running behind other windows. Watched every 10 minutes (`Runs.exe watch`); report and side-by-side comparison written at the end (`Runs.exe report`, `Runs.exe compare`).
- 60 days in 1 h 20 min, about $307 at list price; no errors, freezes or failed calls. The kingdom's councils sat 45 times (the replay: 20).
- Ending: the kingdom +1 town, −1 castle, +4 villages, 183 enemy lords taken against 52, battles between lords 109-34. The worst loss of all the runs on day 42 (Gárjan's army caught short of Bagbûrz, the sheltering lords sent into the running battle, 7,915 men and 19 lords lost); Dol Guldur's great army destroyed on day 51 in return. docs/RUNS.md has the record and what to look into (battle_odds on a battle in progress, a siege of Nanduhirion that never began, lords sent out of a besieged holding's walls).

## Sunday, October 25

### 13:05 · Why the kingdom's agent never went for Dol Guldur's great army (a read of run 20261024-2110-C2)
- **Mert, Oct 25:** "I'm seeing a general pattern where the agent fails to prioritize engaging an enemy's main force and pays the price. Can you go into the logs and figure out why it failed to prioritize engaging dol guldur's main force? Which parts of the prompt do you think were in play when it was making those incorrect decisions?"
- **Read from** the kingdom war room's 267 calls of the run (`logs\warroom-new_kingdom\calls\20261024`, 2110 on): each council's plan, orders, lessons and enemy notes; every `battle_odds` and `can_catch` question with its answer; every poke's reason and orders; the chronicle and the run's days. The calls' thinking comes back empty in the record, so this is what the agent wrote, not what it weighed.
- **What it shows:**
  - From day 5 to day 39, 18 field `battle_odds` against Lorgath's army (16,600 to 20,100 strength), every one a win keeping 75 to 91% of the agent's men; on day 5 `can_catch` said Aragorn's army was 88% faster than Lorgath's (reached in about 33 hours, 71 if it ran). The day-5 plan said "It is slow (speed 1.2), so we can mass and catch it", then "best while he is tied to a siege"; day 7, 25k against 20k, "only narrowly and over about 31 hours, so we strike his siege camp only if the edge holds or grows".
  - No order to attack him until day 39. Meanwhile he took Imnagath (day 11), Framsburg (day 19), Imnagath again (day 25) and Dûglarshun (day 36); the plans let each go ("cannot be relieved in time").
  - 16 of the 46 council plans held "strike him only while he is tied to a siege of one of our places". His sieges ran half a day to five days; the agent's armies stood four or five days off, hunting raiders at Pelan Erui, Eithel Glin and Galadbain. Its own day-1 lesson ("Dol Guldur's lords (speed 3.1) outpace our big armies: we only catch them when they stop to besiege or raid") was about the small raiders and carried over to the slow army.
  - Of 221 pokes, 143 were about raiders or lone enemy lords and 19 named Lorgath; of 386 orders in poke answers, 114 sent lords after raiders and 8 went at Lorgath, all on day 39 or after (6 of them fed lords into the battle already lost at Bagbûrz). The notes' enemy list ended at 60 lines, nearly all small raiders. At day 32 the realm had 78 lords in the field, strength 50,039 (24 holding orders), against his 17,900; Górli's and Ask's 8,600 were on a siege of Nanduhirion that never began.
  - The plans expected his army to break up (8.8 days on day 25, 10 on day 32, by day 44); it stayed whole from day 3 to about day 50.
  - Bagbûrz: a poke answer gave Gárjan `garrison` (stay inside), and 33 s later another swapped it for `defend` radius 8, `shelter_at` 1, `stay_inside` 1, caution 0, "inside Bagbûrz 23h before Lorgath". defend patrols outside the walls and went inside only when the host came within 8 (chronicle, Winter 17, 08:00); he was caught 5 from the gate at 12:00. Caution 0 had switched the wider danger rule off (`Runner.Safety`), as it is meant to.
- **The instructions' lines in play:** principles 2 and 3 ("tied to a siege"); "the edge" with no measure; the persona's du Guesclin line ("let an invader burn itself out, destroy its detachments ... without the battle") with principles 4, 6, 7, 11, 12 and 15 all aimed at the enemy's parties; principle 17's "when time is on your side, wait"; council step 3, every front's share before the blow. One line pointed at the main army ("break the enemy's field armies before you take its castles"). The replay (same instructions) had "tied to a siege" in 2 of 21 plans: the instructions allow it, and an enemy army that stays whole brings it out.
- Nothing changed by the read itself. Mert took the fixes offered and added two of his own (the next two entries).

### 13:38 · The agents' instructions: the enemy's main army first, raiders to small forces · commit 9e6d5cf (the template)
- **Mert, Oct 25:** "I like your fix propositions. I'd add more to the main army part though, something like this: Prioritize your efforts properly: if you see an enemy's main force and you can beat them, you should plan on assembling an overwhelming force and meeting them on the field, or as quickly as possible so long as you are advantageous, and crushing them. Then, you can be agressive and quickly take over their territory as they aim to regroup. I'd also add some notes on the raiding parties: armies should not be spending time chasing raiding parties and be focused on their primary objectives. Raiding parties should only be answered by smaller groups of lords that can crush them, the focus of large armies should not be diverted."
- A general principle in the doctrine's own words, the same for every realm agent, with nothing of this war in it. The design, its grounding and caveats: docs/DOCTRINE.md, "Principle 18 and the raiders (Oct 25)".
- **What changed** (`agent/warroom-system.template.md`):
  - **Principle 18, new, "The enemy's main army first":** when you find an enemy's main army and can beat it, its destruction is the first aim; gather an overwhelming force and meet it in the field, or strike as soon as you hold the advantage if gathering all would take too long, rather than wait for it to sit down before a castle; lesser prizes wait, raiders go to small forces; then take his places quickly while he regroups (principle 10); when you cannot yet beat it, principles 4 and 17. Saladin 1187 and Mohi 1241.
  - **Principle 7,** now "Raiders: small forces, and on their way home": raiding parties are met by small groups of lords that can crush them, never by the armies, which stay on their tasks and turn aside only for an enemy force worth more than the task (*On Skirmishing* as the frontier districts' manual).
  - **Principle 4:** parties picked off "with small forces".
  - **Principle 3:** the battle odds are the measure of the edge (a win keeping most of your men while most of his fall); judge a battle by what each side has left, not by how long it runs.
  - **Principle 17:** time is on your side only while waiting costs him more than you; an enemy taking your places while you wait has time on his side, however strong you are; the stronger side reads principle 18.
  - **Who you are:** the du Guesclin line now begins "When you cannot yet beat an invader in the field", with "after Crécy and Poitiers, the French could not yet win it".
  - **The council of war:** step 2 begins with where each enemy's main army is and whether it can be beaten; step 3's lesser tasks name "small groups of lords against raiders". Step 3's order (fronts first, then the blows) and principle 5 are Mert's of Oct 24 and unchanged.
- **Outside the repository:**
  - Each realm agent's `warroom-system.md` (orc, Dol Guldur and kingdom war councils) rebuilt from the new template, each keeping its own name and opening line, otherwise word for word the template (checked by diff: 2 lines differ, the name and the opening). The kingdom's is sha256 `3A3AE3C53DECF372…`.
  - Backups are `warroom-system.md.bak-20261025-before-main-army`.
  - In force from their next answer, no restart. The lieutenant's instructions have no doctrine and are unchanged.
- **For comparisons:** C1 and all three C2 runs were played without principle 18; the runs still to be played are on it. A run's setup records each agent's instructions' fingerprint, so it shows.

### 13:39 · Version 0.11.6: defend goes inside in time; every battle forecast names what the loser has left · commit 9e6d5cf (installed 13:39)
- **Mert, Oct 25:** "I like your fix propositions" (the two tool fixes offered after the read above: the guard order's trap at Bagbûrz, and battle forecasts that say how much of the enemy's army falls).
- **defend, the early shelter** (`Tools/Library/defend.json`): with `stay_inside` 1 at a castle or town, it goes inside as soon as enemy lords and armies at least `shelter_at` times its strength are due at the holding, by the game's marching rules, within the hours it would take to ride back from the edge of its patrol plus six (`relief_within(at, radius / max(pace, 0.3) + 6, radius)`, the siege order's relief count), whatever its caution; the same rule breaks off a strike, and it patrols again only when that force is no longer due. Without `stay_inside` nothing changed (choosing another refuge would need the coming force weighed, and the danger rule covers it while caution is on). The library's text and the `shelter_at` setting say so. New built-in `pace` (the lord's map units an hour over a day, by the game's marching rules; `Runner`, `ToolDef.BuiltIns`).
- **Battles run ahead name the loser's remnant:**
  - the `battle_odds` question (`BattleTools.Outcome`): "you win in about 31 hours (...), with about 8,089 of your 10,284 still standing; they retreat with about 3,024 of their 9,185 (a side that retreats gets away with its lords, and nothing is captured)" (the day-7 forecast of run 20261024-2110-C2 put in the new words); "all N of their men are down" or "they break with about N of their M left"; storms the same with the besiegers' and defenders' numbers;
  - the pokes' field-battle facts (`Forecast.FieldOddsText`): "... 351 of yours still standing and 45 of its 300 left (it gets away with its lords)" (the smoke test's world);
  - the report's battles under way (`Views`): "... N of them still standing and about M of the other side left".
  - The numbers were already run; only the words changed. The win's takings line is unchanged.
- Versions: `BannerlordAgents.WarRoom.csproj`, `BannerlordAgents.csproj` and `module/SubModule.xml` set to 0.11.6.
- **Checks:**
  - Build with no warnings.
  - Smoke test passed in full (246 checks; 2 new). The new defend check (a slow army of 1,200 patrolling within 8 of its castle with `stay_inside` 1 and caution 0 goes inside when a 1,700 host bound for the castle is due before it could ride back, and keeps patrolling while that host is far off) was shown to fail on the old `defend.json`. The other checks that the field facts say what the beaten enemy has left.
  - War room self-test passed (65 checks; 1 new: the run ahead names both sides' remnants). Dashboard self-test passed (27).
  - 679 game references, 0 missing.
  - Installed with `scripts\deploy.ps1` at 13:39, the game closed: `BannerlordAgents.dll` sha256 `3F9E86946DBC7C2F…` (the new defend text found inside it), `WarRoom.dll` `FEDA8570E3A216DA…`. War rooms and dashboard stopped for the build and started again 13:40 (each reports "War room 0.11.6.0"). Each agent's `tools.txt` is rewritten by the game at the next campaign load.
- **Not yet seen in a game:** all of 0.11.6.
- **For comparisons:** every run so far had the old defend and the old wording.

### 14:18–14:43 · Run C2 on 0.11.6, stopped on day 16 (does not count)
- `20261025-1417-C2`: the kingdom's agent live against the game's AI, the orcs' and Dol Guldur's agents watching; the first run with principle 18. Game closed on day 16 by Mert after the lords kept failing to follow their orders.
- **Why:** the bridge's background helper fell further behind the game all run: 31 s at 14:25, 83 s at 14:38, 142 s at the end. Every order, shelter and poke landed late. Beleg's four attack orders on Naglur each waited about 49 s to start, and each new copy replaced the one waiting. Thalion's shelter in Lasgalor was decided 14 game hours before Thrurg came and landed about a day later. Aragorn's landed half an hour after Thrurg caught him.
- Record and what principle 18 showed: docs/RUNS.md, "Run C2 on 0.11.6 (Oct 25)". To be played again from TEST START on 0.12.0.

### 14:45–15:05 · Why the helper fell behind (a read of the stopped run)
- **Mert, Oct 25:** "Why did beleg not end up engaging naglur even though he was ordered to?", then "Why is this issue where lords fail to follow orders happening continuously?", and "The helper needs to be able to be on time, every time. Please go in and diagnose the issue and come back to me with a fix plan."
- **Found** (the session's trace, the freeze probe on the helper's thread, the run's perf log, a scratch benchmark):
  - The game gave an in-game hour every 3.3 s in both runs. The helper, when backed up, needed a median 2.85 s per hour last night (behind 26% of hours, worst 41 s) and 3.47 s today (behind 86%, worst 142 s).
  - All 10 probe samples were in file calls inside `Channel.WriteLookups`. 216 to 339 look-up files per agent were rewritten for each poke, each written beside the old one and swapped in: 1,179 ms for 216 files in the benchmark, against 75 ms written plainly. A swap refused while the virus scanner held the new file slept 15 ms, up to four times.
  - Every hour, for every agent: the realm's view built twice, the situation report and the council briefing rewritten (only a council reads them), the dashboard's map picture (about 400 KB compressed).
  - New orders' starts went into the same first-come queue (`Orders.StartTool` posted `Runner.Start` behind every hourly job).
  - The queue reached 2,051 of its 4,096 jobs; past that, work is dropped. The `helper_backlog` alarm (174 times) was not serious, so the 10-minute watch never showed it.
- Mert, on the plan: "SOunds good. Implement all fixes." (kept on the efficiency cores, urgent work out of efficiency mode; no virus-scanner exclusion).

### 15:16 · Version 0.12.0: the helper on time · commit 44f2b66 (installed 15:16)
- **Three lanes on the helper's one thread** (`Background`; one thread, so nothing it keeps is shared):
  - **urgent** (`PostFirst`): the agents' answers, every order's start, change and end (`Orders`), and each hour's order rules and look (`Background.PostHour` → `Brain.Decide`: the tools, what each realm sees, the trails, the recruitment log). It runs at normal priority, out of Windows' efficiency mode, on the same efficiency cores (`Cpu.SetCurrentThreadUrgent`);
  - **regular** (`Post`): logs and records, as before;
  - **reporting** (`Background.Publisher = Brain.NextStep`): the newest hour only, one step at a time when nothing else waits. The steps are the snapshot checks, each watched realm (`PublishSeat`), then the dashboard. Hours that came meanwhile are folded in: what each hour saw waits in `Memory.Waiting` with the ruler's notices.
  - `Brain.OnHour` (the smoke test's) is the two run back to back.
- **Less work:**
  - Look-up files are written in place, and only when changed (`Files.WriteIfChanged`).
  - A refused swap is tried again at once, then written in place, with no sleeps.
  - One view per realm and hour serves its poke and the dashboard.
  - A realm agent's `report.txt` and `council.txt` are written when its war room asks (`orders\council-request-*.json`; `Brain.Briefing` on the urgent lane). The war room (`Room.RequestBriefing`) waits up to 20 s, else uses the last one; "is the game running" is now `poke.json` within ten minutes. The lieutenant's hourly report stays.
- **The very same order given again keeps the running one** (`ToolOrder.Signature`, `HandOrder.Signature`): the receipt says "already holds this very order ... it goes on as it was, not started again". Gather orders are not compared.
- **A poke from an hour the game has moved on from by 2 or more** says "This picture is N in-game hours old ..." in its head.
- **Measured and alarmed:**
  - The `hour` event: `behind_hours`, `orders_ms` by part. A `reports` event per round: folded hours, behind, ms by part, look-up files written and unchanged. `agent` / `briefing written`.
  - summary.txt: the helper's hour, rounds and briefings; files written, unchanged, swaps tried again, written in place; orders kept as they were.
  - The run's day lines: `helper` (most behind with orders and reports, rounds, hours folded); the run report: the worst of the run.
  - Serious alarms: `helper_late` (order rules 2+ in-game hours behind), `helper_reports_late` (6+), `helper_backlog` (over 1,000 jobs), `helper_dropped`.
- **The speed test** (`SmokeTest --perf [folder] [--budget ms]`): the helper's whole hour with a watching agent per realm, on its cores, timed by part; it fails when the 95th slowest hour in a hundred exceeds the budget (1,000 ms).
  - On the big made-up world (9 realms, 3 watched, 828 holdings, 2,946 parties): median 62.5 ms an hour, 95th 67.3 ms, most 614.8 ms (the first look).
  - Each launch now saves 12 real hourly copies of the world from 24 in-game hours into a load (`PerfRecord`; settings `perf_record_hours`, `perf_record_after`) to `perf\snapshots\`, for the real test.
- Versions: `BannerlordAgents.csproj`, `BannerlordAgents.WarRoom.csproj` and `module/SubModule.xml` set to 0.12.0.
- **Checks:**
  - Build with no warnings. Smoke test passed in full (255 checks). Changed: the situation report and the briefing come on request (4 checks rewritten). New: the urgent lane runs before regular work queued ahead of it; three hours folded into one poke with what each saw and the ruler's notice; the stale-picture line; an order told apart from a changed one; look-ups written only when changed; a saved hour read back whole; the speed test on saved hours.
  - War room self-test passed (67; 2 new: the briefing asked for and had; no game, given up and the request taken back). Dashboard self-test passed (27).
  - 688 game references, 0 missing.
  - Installed with `scripts\deploy.ps1` at 15:16, the game closed: `BannerlordAgents.dll` sha256 `C4F9E163D081B2D9…`, `WarRoom.dll` `7B839AEE539D1697…`. War rooms and dashboard stopped for the build and started again 15:17 ("War room 0.12.0.0").
- **Not yet seen in a game:** all of 0.12.0. Before the next counted run: a short soak in the game at run speed with all three agents (no helper alarms, its `behind_hours` under 1 throughout), then `SmokeTest --perf` on the hours it saved.
- **For comparisons:** every run before this had the helper falling behind at busy times; their orders and pokes were late by up to its delay. The runs' day lines now record it.

### 15:24–15:52 · Run C2 on 0.12.0, stopped on day 20: does not count
- `20261025-1524-C2`, from TEST START. The helper was on time throughout: the lords' orders at most 1 in-game hour behind (the hour just handed over), 0 hours folded into a later report over 480 report rounds; answers picked up within 0.74 s and carried out within 0.3 s; no serious alarm.
- **Stopped** at Mert's ask ("I'd rather start a new test with the fix"): his kingdom's sieges never began. Górli stood 4.3 from Caradhras' gate (6.8 from its centre) for 15 days and Oropherion's army 4.7 (7.8) for 14, each in besiege's first step waiting for "within 6" of the centre; Val stood 6.2 off Sirannon. Mert saw it on the dashboard: "Why are oropherion and gorli simply outside caradhras but not besieging it?"
- Also found: Party AI Controls' own refill sent Aragorn to fill his ranks after the battle with Naglur (day 14) and held him there to the end; the bridge refused the agent's 3 orders to him meanwhile, and the recorder logged the 7 refill steps as "You ordered Aragorn ...".
- Where it stood (docs/RUNS.md): the kingdom −1 castle (Imnagath, to Thrurg, day 20), 46 enemy lords taken, Naglur's army destroyed (day 14), $98.68 list price for the three agents.

### 16:03 · Version 0.12.1: arriving at a castle, and no refill behind the agent's back · commit 8114d16 (installed 16:03)
- **Arrivals measured to the gate** (`World.Near`, the `within` function): to a holding, the nearer of its centre and its gate. The gate lies a median 2.9 from a castle's centre and 3.3 from a town's (up to 7.1; villages have none), read from a saved hour of the run. Before, a siege began only when the game's waiting point fell on the castle's side of its gate (likely also Nanduhirion's siege that "never began" in run 20261024-2110-C2), a watch at a gate over 3 from the centre (105 of 221 castles and towns) never began, and goto's close stop ("within 4") often fell short.
- **`near`** goes to a reachable point 2 to 5 from the gate (was 3 to 6); goto stops close at "within 6" (was 4).
- **A march that stops short is called back** (`Runner.Stalled`): a lord moving to a place (`near`, `goto`, `enter`, `post`, `toward`) who has stood still for 6 in-game hours, more than 6 from it (3 from a spot), out of battle, outside and not besieging, is reported once per stand: "... has not moved for 6 hours while marching on Caradhras, still 9.0 from Caradhras: he may be stuck on the map. The order is kept and he holds where he is; give him another order if he should go elsewhere." It does not end the order (that would hand the lord to the game's AI).
- **The playerless switch** (`agents.run playerless go`, runs C1 and C2) now also switches off Party AI Controls' auto-recruit for every lord it has settings for, and in its clan and kingdom defaults for new parties (`PacLink.AutoRecruitOff`; only existing settings are changed). Mert: "Switch it off at setup, and make sure it is off for everyone else as well."
- Versions: `BannerlordAgents.csproj`, `BannerlordAgents.WarRoom.csproj` and `module/SubModule.xml` set to 0.12.1.
- **Checks:**
  - Build with no warnings. Smoke test passed in full (265 checks; 10 new, laid out as Caradhras: besiege arrives 4.3 from the gate and 6.8 from the centre and besieges; goto arrives 4.7 from the gate; a watch at a gate 3.3 from the centre begins; a lord standing still 48 off is told once at 6 hours, not at 5, not again, the order kept, cleared when he moves; ten hours in a battle on the way is no stall).
  - War room self-test passed (67), dashboard self-test passed (27). 688 game references, 0 missing (RefCheck).
  - Installed with `scripts\deploy.ps1` at 16:03, the game closed: `BannerlordAgents.dll` sha256 `DB4C243AEE9A740F…`, `WarRoom.dll` `0EC90B8947814C24…`. War rooms and dashboard stopped for the build and started again 16:04 ("War room 0.12.1.0"); arm C2 as set (kingdom live, the orcs and Dol Guldur shadow, the lieutenant off).
- **Not yet seen in a game:** a siege beginning at a mountain castle, a stall call-back, the auto-recruit switch-off (the playerless switch should say "switched off Party AI Controls' auto-recruit for N lords and for new parties").
- **For comparisons:** in every run before this, sieges, watches and goto stops at castles and towns began only when the waiting point happened to fall near enough the centre, and in C runs the lords Party AI Controls managed with auto-recruit on could be taken from the agent by a refill.

### 16:10–16:31 · Run C2 on 0.12.1, stopped on day 10: does not count
- `20261025-1609-C2`, from TEST START. 0.12.1 seen working: the setup switched off Party AI Controls' auto-recruit (Aragorn, one more lord, its clan and kingdom defaults); every siege went from marching to besieging (Sirannon besieged day 7 and taken day 10, Caradhras besieged from day 7); no stall call-back; no Party AI Controls order on a kingdom lord. The helper on time throughout (0 hours folded over 240 rounds).
- **Stopped** at Mert's word after Alenon's army of 13 lords was destroyed near Torech Emel on day 10. Mert: "Alenon left the settlement and started fighting the enemy instead. I think its something about our defend order's calculation of an enemy host's strength - the enemy was dispersed in a couple of different parties before and something may have been off again. Check and diagnose the issue." Then: "I've stopped the run. Please make the fix to our tooling."
- **Found** (the order's steps and hourly states in the trace, the kingdom's pokes): the guard order (radius 30, `strike_below` 1) struck Narsha (1,751 against Alenon's 8,505), one of a dozen lords marching to join Thrurg (about 17,000 together); the chase ran 33 hours, out of his area into them; the shelter came only when the forces within 30 of the castle reached 1.5 times his strength, 26 from the walls; Bolga and 17 more parties (10,306 men) caught him 5 hours later. The safety rule weighs each enemy party alone and never fired. The old 4-hour give-up never worked (patrol struck the same target again the same hour). The agent's change to radius 20 and `stay_inside` 1 came while he was already in the battle.
- Where it stood: the kingdom +1 castle, +3 villages, 14 of its lords taken against 8; $41.23 list price for the three agents.

### 16:38 · Version 0.12.2: a host split into many parties counts as one · commit 57944ee (installed 16:38)
- **The host** (`World.HeadOf`, `HostOf`): a lord marching to join an army or a lord of his realm (`EscortParty`) belongs to its head; his host is the head and every party in view marching to join it within 20 of him (`World.HostReach`).
- **The safety rule counts the danger as a group** (`World.DangerAround`, `Runner.Safety`): every hostile lord, army and bandit band in view within `danger_radius`, and the host of each of those lords (also of the lord's own target, which itself stays spared), together against `caution` times his strength. Shelter, run and "would it hold" take the group's whole strength (`ChooseRefuge` with the group, no part counted twice); the call-back names it ("from Grisha and 1 more with it (strength 980 together against 600)"). A shadow spares the army it shadows and its host unless they chase it.
- **`threat_near`** also counts the host of every enemy lord within its radius.
- **New functions** `company(x)` (`World.Company`: x, its host and every enemy within `danger_radius` of it) and `easiest_enemy_near(at, r)`.
- **defend**: strikes the enemy whose company is weakest and only when that company is at most `strike_below` times its strength; breaks off when the company passes 1.5 times that, when the chase would take it out of its area, when the target leaves the area, or after 6 hours no closer (each with a call-back), and leaves that target be for 6 hours; comes out of shelter only once its area and half as far again beyond are clear.
- **engage** gives up a chase not closing in for 8 hours (out of battle), and its next order starts; **hunt** stops when its target's company reaches `bigger_ratio`.
- The library's caution text and the tools' own texts say so. Versions set to 0.12.2.
- **Checks:**
  - Build with no warnings. Smoke test passed in full (280 checks; 15 new, replaying the three losses: Gralzuk (906) with her host is 9,734 against Alenon's 8,505, so no strike, and a lone raider (651) is struck instead; Grisha (500) and Muzgur (480) near Thalion (600) are 980 together and he shelters or falls back; the army his target marches to join (1,200, 15 off) is a danger; a guard breaks off 21 from its castle with an area of 20 and leaves that lord be for 6 hours, then strikes again; gives up after 6 hours no closer; engage gives up after 8 hours a mile behind and lets the lord go; a guard stays sheltered while a force is parked 27 off its area of 20 and comes out at 40).
  - Speed test on the big made-up world: the helper's whole hour median 65.2 ms, 95th 70.1 ms (budget 1,000 ms).
  - War room self-test passed (67), dashboard self-test passed (27). 688 game references, 0 missing.
  - Installed with `scripts\deploy.ps1` at 16:38, the game closed: `BannerlordAgents.dll` sha256 `A5F24CDA8530F6EE…`, `WarRoom.dll` `1A900315BDB26DDF…`. War rooms and dashboard stopped for the build and started again 16:39; arm C2 unchanged.
- **Not yet seen in a game:** all of it. To watch in the next run: shelters and falls back "from X and N more with it", strikes broken off or given up, a guard staying inside while a host stands just outside its area, and whether the group rule makes lords shelter too often (bandit bands near each other now count together too).
- **For comparisons:** in every run before this, a lord's safety rule weighed each enemy party alone, strikes could pick one lord out of a gathering host and chase it out of their area, and a broken-off strike began again the same hour.

### 17:11 · Version 0.12.3: the agent decides who joins a fight, and asks before it engages · commit a4076b7 (installed 17:11)
- **Mert, Oct 25:** "every lord marching to an army within view should be listed under that army with how far away they are from it. The agent should decide which ones to count as a threat and which ones not to (so if it takes 20 hours for an army to engage an enemy and win, the agent should have all information needed to deduce which lords will make it there within the timeframe of the engagement and decide accordingly). Beyond this, before an engagement, the agent should be able to forecast how long it will take its selected lord/lords to begin and end the engagement, and which enemy lords in view are close enough in terms of marching distance to that engagement to participate in it as it progresses, so it can decide accordingly and set rules accordingly." On how the agent's choice reaches the hourly rules he chose "Hours window only" (over a window with exclusions, named lords only, or a window defaulting to 0).
- **The report** lists under each army in view the lords marching to join it (in its army but not yet with it, or following it or a lord of their own realm: `Views.JoiningHead`), each with men, strength, distance from it and the hours at his pace while it holds where it is; the lord's own line says how far he is from it. Given only toward an army the realm can see.
- **`join_hours`** (default 8, 0 to 96) on every move and tool: its rules count with a fight every enemy lord and army in view that could march to it within that many hours at its own pace (`World.Joiners`, `HoursTo`). Caution, once anything hostile is within `danger_radius`, counts those who could reach the lord (`DangerAround`); `company`, `easiest_enemy_near` (defend's strike, hunt) those who could reach the target; `threat_near` in tools' rules those who could reach the place. 0.12.2's fixed "host within 20" (`HostOf`, `HostReach`) is gone; the shadow's spared host stays (`HeadOf` now uses the same joining rule).
- **New war room question `engagement`** (`EngagementTool.cs`): `ours` against an enemy `target` in view: each of ours routed to where it is now, the fight from the first arrival with the rest joining as they come, run by the game's battle rules against the target alone and again with every enemy lord and army in view that would arrive while it is still going on (30 nearest routed), each marked in time or too late; when it would end from now; "give that order join_hours N". A target on the march is flagged.
- The library text (caution, the new join_hours line), defend's and hunt's texts say so.
- **Outside the repository** (the same text in each, written as how the tools work, not strategy): the agents' instructions `kingdom-war-council\warroom-system.md`, `orc-war-council\warroom-system.md`, `dolguldur-war-council\warroom-system.md` ("Four more questions": `engagement`, `join_hours`, the joiners in the report) and `lieutenant\warroom-system.md` (its question list); backups `*.bak-20261025-before-engagement`. The repo's templates `agent/warroom-system.template.md` and `agent/lieutenant-system.template.md` match.
- Versions set to 0.12.3.
- **Checks:**
  - Build with no warnings. Smoke test passed in full (284 checks; new: with join_hours 0 Gralzuk stands alone (906) and the same guard strikes her; the report lists Thrurg's three joiners ("Marching to join it (3 lords, strength 4,134 together)", "Narsha [lord_nar]: 875 men, strength 1,751, 11.0 from it, about N hours") and gives no distance toward an army out of sight; changed: a raider struck only when more than 8 hours from the host; a chase broken off when a strong army could join falls back toward the other castle when that castle would not hold).
  - War room self-test passed (70; new: the five questions listed; Bolg against Hirgon: Bolg there in about 3 hours, wins in about 2, Aragorn's army 47 hours after it begins, too late, "give that order join_hours 6"; against Aragorn's army on the march: flagged, Bolg loses; an unseen target refused). Dashboard self-test passed (27).
  - Speed test: the helper's whole hour median 63.7 ms, 95th 68.6 ms (budget 1,000). 688 game references, 0 missing.
  - Installed with `scripts\deploy.ps1` at 17:11, the game closed: `BannerlordAgents.dll` sha256 `4282EF3B4AF98588…`, `WarRoom.dll` `0FC3292EF8460C5C…`. War rooms and dashboard stopped for the build and started again 17:11 ("War room 0.12.3.0"); arm C2 unchanged.
- **Not yet seen in a game:** all of it. To watch: whether the agent asks `engagement` before attacks and sets `join_hours` from it; the joiners lists in the report; how often caution now shelters a lord.

### 18:32–19:56 · Run C2 on 0.12.3, all 60 days: counts
- `20261025-1831-C2`, from TEST START, one load. The kingdom +2 towns (Nanduhirion, Eastern Moria), +2 castles net (Caradhras, Hollin Gate, Sirannon taken; Torech Emel lost, retaken, lost), +12 villages; 182 enemy lords taken against 29 of its own; lord battles 106-32; Dol Guldur's main host destroyed at Imnagath (day 23, 19 lords, Thrurg and Lorgath among them), Naglur's army on day 11. Agents' list price $294.42. The helper on time throughout (0 hours folded over 1,440 report rounds). Details in docs/RUNS.md.
- **Found during it** (Mert's questions and the watch): the first council overturned the first answer's order for Aragorn (sell at Mount Gundabad → Hithaegrist, 75 back); the sell order did not say a castle buys no loot; guards still marching to their area started and broke off 31 strikes; lords on the way to a refuge were caught by parts of the danger that came up later (Haldir, Zlatka, Alvar, Elrohir); one false fog alarm. And, Mert at the end: "imak is going back and forth because he is scared of khamul's force, even though our lords' combined force is enough to take him. Why is the strength calculator not taking into account the strength of the other lords around him?" Then: "Yes. Make the fix. The test is done. Give me the final report. Once all is done, be ready for the control test: all realms playing with the game's ai."

### 20:09 · Version 0.12.4: our side counts too, and the control arm · commit d4ad9dd (installed 20:09)
- **Our side of a fight** (`World.Friends`, `SideStrength`, new function `side_strength(x)`): the lord, every lord of the realm or an ally camped in the siege he is in or making for, and, within the order's `join_hours`, those marching to join him or bound for the same place or target (besieging or defending it, or holding an agent's order aimed at it). Lords merely near him do not count: a lord on one of the agent's orders joins no fight by himself. Used by caution, besiege's relief alarm and its withdrawal and storm bars, defend's strike, break-off and shelter rules, and hunt; the call-backs say it ("against your side's 6,000 (Imák's 1,500 with your lords camped there or due within 8 hours)").
- **Guards strike only from inside their area**; **a lord on his way to shelter plans again every hour**, and **"can he get there first" is judged against every party of the danger** (`ChooseRefuge`).
- **The sell order** says a castle buys no loot (only a town) and a batch of prisoners holding a captured enemy lord pays nothing (the game's `SellPrisonersAction`).
- **War room:** the lords ordered by a fast answer in the minute before a council sits count as ordered meanwhile (`Room.BeginSitting`), so a council briefed before those orders took effect cannot overturn them.
- **Fog check:** the places named by a poke's own news count as known (`Brain.Known`).
- **The control arm C0** (Mert: "all realms playing with the game's ai"): `RunRules.Arm` gives C0 when the kingdom is the game's (its agent off or watching) and both rivals are the game's; `Runs.exe` and `agents.run check` name it; `scripts\arm.ps1 -Arm C0` (the kingdom's agent off, `-KingdomWatches` for shadow; the rivals shadow, `-RivalsOff` for off; the lieutenant off); the playerless switch's message names C0.
- **Outside the repository:** the agents' instructions (`kingdom-war-council`, `orc-war-council`, `dolguldur-war-council` `warroom-system.md`: join_hours also counts "on your side every lord of yours camped in the same siege and, within those hours, those marching to join the lord or bound for the same place or target"; `lieutenant\warroom-system.md` the same in its question list); backups `*.bak-20261025-before-both-sides`; templates in `agent/` match. `scripts\arm.ps1 -Arm C0` run at 20:09: the kingdom's agent live → off (logged in `runs\arms.log`).
- Versions set to 0.12.4.
- **Checks:**
  - Build with no warnings. Smoke test passed in full (287 checks; new: four lords camped in one siege, 1,500 each, against a relief of 4,155: nobody lifts the siege, the alarm says "against your side's 6,000"; the same relief against one lord: he lifts it; two lords ordered at the same target, 600 each, against 1,200: neither shelters; the arm rules give C0; changed: the relief alarm's wording).
  - War room self-test passed (71; new: an order given the minute before a council sits stands against it). Dashboard self-test passed (27). Speed test within budget. 688 game references, 0 missing.
  - Installed with `scripts\deploy.ps1` at 20:09, the game closed: `BannerlordAgents.dll` sha256 `474540D24DE0487C…`, `WarRoom.dll` `918317060F491648…`. War rooms and dashboard stopped for the build and started again 20:09.
- **Not yet seen in a game:** all of 0.12.4. The control run (C0) gives no agent orders, so none of the order changes affect it.

### 20:12 · The control run set up with every agent off · (no install)
- **Mert:** "For c0: please disable all agents so they don't needlessly take up usage. Just let the game's ai play and watch it yourself. Do keep all of your logs for it though so we can report on the run."
- **Outside the repository:** `scripts\arm.ps1 -Arm C0 -RivalsOff` at 20:12: the orcs' and Dol Guldur's agents shadow → off in their `agent.txt` (the kingdom's agent and the lieutenant were already off); logged in `runs\arms.log`. War rooms stopped (`scripts\agents.ps1 stop`), the dashboard started again alone (detached). The bridge's logs and the run record are unchanged. docs/RUNS.md says so.

### 20:15–20:35 · Run C0 on 0.12.4, all 60 days: counts
- `20261025-2015-C0`, from TEST START, one load, every realm the game's AI, every agent off (0 calls, $0.00), 0.34 real hours. The kingdom ±0 towns (Nanduhirion taken, Goblin Town lost), +2 castles net (Sirannon, Caradhras; Imnagath and Torech Emel lost and retaken), +4 villages; 137 enemy lords taken against 142 of its own; lord battles 120-115; on day 29 Val's army (5,454 men) destroyed outside Glad Thaw with 13 lords taken. Against the counted C2 run: holdings +2 against +4, villages +4 against +12, own lords lost 142 against 29. Details in docs/RUNS.md; report in `runs\20261025-2015-C0\report.md`, the table in `runs\compare.md`.
- **Health:** 0 errors, 0 freezes, 1,440 report rounds with none folded. Two false alarms from a run with every agent off (`Runs.exe watch`'s war-room count; the self-check's "two agents" for the kingdom's own and the lieutenant, both off): to fix when the game is closed.

### 20:51 · Version 0.12.5: the control run's two false alarms, and C1 set again · commit 4f36147 (installed 20:50)
- **The self-check** (`BridgeBehavior`): two agents of one realm both switched off (C0: the kingdom's own and the lieutenant) are a note ("both are switched off, so neither plays it"), not a failed "realm under watch"; only two switched on is still a problem.
- **`Runs.exe watch`** counts a war room for each agent switched on (live or watching) only, so the control (every agent off) no longer reads "only 0 of 3 war rooms are running".
- Versions set to 0.12.5.
- **Checks:** build with no warnings; smoke test, war room self-test and dashboard self-test passed. The speed test was not run (nothing in the hourly work changed). Installed with `scripts\deploy.ps1` at 20:50, the game closed: `BannerlordAgents.dll` sha256 `1D0B41A42DA9B8F5…`, `WarRoom.dll` `7812E1D47EB06BD8…`. Dashboard stopped for the build.
- **Mert:** "set up the c1 test again." **Outside the repository:** `scripts\arm.ps1 -Arm C1` at 20:51: the kingdom's agent, the orcs' and Dol Guldur's off → live, the lieutenant off (logged in `runs\arms.log`); the four war rooms and the dashboard started (`scripts\agents.ps1 start`). docs/RUNS.md says so.

## Monday, October 26

### 21:03 (Oct 25) – 01:21 · Run C1 again on 0.12.5, all 60 days: counts
- `20261025-2103-C1`, from TEST START, one load, all three realm agents live. Paused by Mert on day 15 (21:25) when his usage ran low; the computer slept until 00:21; carried on from there. The kingdom +2 towns (Nanduhirion, Eastern Moria), +1 castle net (Sirannon, Caradhras, Hollin Gate taken; Imnagath lost and retaken; Torech Emel and Lasgalor lost to Dol Guldur), +10 villages; 169 enemy lords taken against 12 of its own; lord battles 70-14; the orcs' army destroyed at Daeron's siege camp on day 26 (30 lords), Dol Guldur's two armies on day 54 (21 lords). Agents' list price $350.82 (kingdom $119.06, orcs $145.13, Dol Guldur $86.63); 4 failed calls of 1,081 (3 on the session limit, 1 stopped at 120 s). Against the counted C2: holdings +3 against +4, villages +10 against +12, own lords lost 12 against 29. Details in docs/RUNS.md; report in `runs\20261025-2103-C1\report.md`, the table in `runs\compare.md`.
- **Health:** 1,440 report rounds, none folded. The one "freeze" is the computer's sleep. One handled error (the kingdom's `poke.txt` swap, a minute before the end; that poke was answered). One made-up id (`lord_CharacterObject_8269`), refused for that lord: the order check could accept a `lord_` prefix (not yet done).

## Wednesday, October 28

### 00:30 · Run A set again · (no install)
- **Mert:** "I'm about to play the game myself for a bit. I will use the TEST CONTINUOUS save to save my progress, but start at the 'TEST START' save. Set everything up accordingly."
- **Outside the repository:** `scripts\arm.ps1 -Arm A` at 00:30: the lieutenant off → live, the kingdom's agent live → off, the orcs' and Dol Guldur's agents stay live (logged in `runs\arms.log`); the war rooms and the dashboard restarted (`scripts\agents.ps1 restart`). Nothing installed (0.12.5 stays). docs/RUNS.md says how the run's saves carry it on.

### 01:00 · The plan for 0.13 written · (no install)
- **Mert:** "lets first implement all of the functionality (diplomacy and the agents being able to write their own tools) into the game and collect with those in action", with his example of a 'recruit in place' order, the agent's choice of a one-off or a kept tool, and "We will log both my moves and the agents' ... please capture everything from them as well." Then: "Give me the final comprehensive plan for my review now."
- **What:** `docs/PLAN-0.13.md`: the agents' own tools (the language's lists, per-place memory and new moves and facts; the checker, a "check a tool" question with a try on the newest hour, the guards, the realm's library with versions and each tool's record; Mert's example written out as a test, never shown to the agents), diplomacy (the game's own votes for war, peace with tribute, alliances, calls to war, trade agreements, policies and fiefs; the ruler's say; offers between realms; letters between agents; the War of the Ring), the training record (Mert's moves only while he rules, the report with each move, the moves not recorded yet; the agents' episode index, outcomes, toolbook and diplomacy book; a packed bundle per run), the instructions, the tests, the order of work, the runs after it, the risks and 11 decisions for Mert. `docs/PLAN.md` points to it.
- **Found while writing it:** of the recorder's 120 records, 37 proposals and 7 orders from the agent runs were credited to Mert (the game's player clan stays his); the agents' event streams carry an empty thinking block; C: is 96% full (48 GB free).

### 01:25 · The plan for 0.13 revised with Mert's decisions · (no install)
- **Mert's eleven answers** are at the head of `docs/PLAN-0.13.md`, word for word, each with what it means: our tools stay fully the agents' and their definitions become readable; a couple of example tools in the instructions, his own among them; the lieutenant writes tools with his approval; libraries carry over between runs; the War of the Ring's rules stay; the allied channel (letters and joint plans between allied agents, and Mert speaking to agent allies through his lieutenant); every diplomatic act, mercenaries and expulsion included; the instructions as a skeleton during the build and their final wording with Mert after it; the backup's place proposed after the build; comparisons free.
- **Added:** B5 (the allied channel and the lieutenant), the order of work's new steps (the allied channel; the skeleton; the decisions with Mert after the build), and the open points: an agent of its own for a realm allied to Mert's, so he has an agent ally to speak to; letters to realms that are not allies.

### 02:12 · 0.13 step 1 of 7, the training record (0.13.0-pre1) · commit f2a1d4f (not installed)
- **Mert:** "We will log both my moves and the agents' ... please capture everything from them as well", and "Implement the full plan now, do not stop until you are done." This is step 1 of `docs/PLAN-0.13.md` Part F (Part C, the record), first so that everything after is captured.
- **Your moves** (`Recorder`, `RecordHooks`):
  - only while you rule your realm yourself (`RecordRules.Yours`): your own character leads it, or is a lord of a realm no live agent commands. In the C runs a wanderer stands in for you and the kingdom's agent is live, so nothing is recorded as yours there (before, 37 proposals and 7 Party AI Controls refills of the agent runs were credited to you);
  - Party AI Controls' own refill (a recruit order with no target, set with no conversation before it) is an event, not your order (`RecordRules.PacRefill`);
  - new kinds: recruits at a stop (troops, cost, ranks), trade (goods sold and bought, food), prisoners sold and put into a dungeon, troops left in or taken from a garrison or a clan party (the party screen's Done, from its own rosters before and after), your party's and your garrisons' wage limits and their recruiting, storms (hours of siege, engines built), sallies, fights declined (met in the encounter menu, left without a battle), escapes (men left behind), stays in a holding of an hour or more, lords called into your army and sent from it, your army broken up, your votes and your choice as ruler, fiefs given, mercenary clans hired and let go, your answers to offers of peace, alliance and calls to war, and what you say to the lieutenant and do with its proposals;
  - each move keeps your realm's report of that hour (`record\reports\h<hour>.txt.gz`, the text an agent of your realm reads, with its fingerprint), its run and arm, this load, and its details; in a run of yours (A, B) the report is kept every hour too (`record\hours.jsonl`).
- **The agents' moves:**
  - run tags (`RunTags`) on every poke line, `poke.json`, receipt, order record, notice, tool step and the war room's call lines;
  - the episode index (`episodes.jsonl`, `Episodes`): each poke with its report's fingerprint, each answer with its call's folder (the war room now writes it into the orders file) and its tools, diplomatic acts and letters, each receipt with the tool run it started, each order's outcome, and each realm's numbers at every day's start;
  - the toolbook (`toolbook.jsonl`, `Outcomes`): every tool run's start (who wrote the tool and its version, the lord, its settings, its poke), its states and call-backs, and its end with the outcome: the lord's numbers at start and end, battles fought and won with men lost on each side, lords taken and lost, holdings gained and lost at its places, how it ended;
  - the model's thinking: Claude Code's `--thinking-display summarized` (found in 2.1.309; a test call returned the thinking's text) on every call, kept in `raw.jsonl` and in each call's `thinking.txt`; `thinking_display` in each agent's `warroom.txt` (summarized by default);
  - each call line also has its run, arm, realm, the instructions' fingerprint and the order library's, and counts of tools, diplomatic acts and letters; each memory snapshot has its run.
- **The run's bundle** (`Runs.exe bundle [run]`, `Bundle`): everything of a run in `runs\<run>\bundle.zip` with a manifest (each file's lines, bytes, fingerprint, span) and a check of what is missing (an answer's call folder, a poke the index names, a move's report). Lines by the run's tag; for older runs by the run's loads (loads.jsonl's offsets) and hours, or its wall-clock windows. The bridge packs it itself when a run reaches day 60 (Runs.exe is staged into the module's `runs` folder). `Runs.exe report` gains "The training record". **`Runs.exe marks`** marks your records made inside the agent runs before 0.13 as not yours (`record\marks.jsonl`), deleting nothing.
- **Checks:** build with no warnings; speed test within budget (the 95th slowest of a hundred hours 181 ms, budget 1,000; median 137 ms); smoke test passed (305 checks; 18 new: the run's tags, the episode index's poke, answer, receipt, outcome and day lines, the toolbook, a move with its report kept and read back, the hourly copy, the recorder's two rules, and the bundle on a made-up run with a call folder missing); war room self-test passed (75; 4 new: the thinking read from the stream, its setting, `thinking.txt`); 722 game references, 0 missing.
- **Outside the repository:** none yet. When the war rooms start next, each agent folder's `warroom.txt` gains `thinking_display = summarized` (the war room adds missing keys). Not installed: the build goes on to step 2; 0.13.0 is installed at step 7.

### 02:34 · 0.13 step 2 of 7, the language grows (0.13.0-pre1) · commit c49a8ef (not installed)
- **Mert:** "Tool writing should be for custom strategies they want to pursue", with his circuit order as one of the examples. This is step 2 of `docs/PLAN-0.13.md` Part F (A2): the language the agents will write their own tools in.
- **New in the language** (`Expr`, `ToolDef`, `Runner`):
  - lists (`[a, b]`, an order's `places` and `lords`) and tables (`{}`, read as `visited[stop]`, set as `"visited[stop]": "now"`); each `{}` is a new table;
  - `takes` with each thing's kind first and the writer's own words after it (`"places": "friendly_holding: the towns to recruit at"`); `places`, a list of holdings, each checked against the game when the order is given;
  - moves: `sell at X` (into a town or castle to sell loot), `wait N` (hours where he stands), `recruit_here`, and `recruit at X`;
  - facts: gold, loot_value, prisoners, ransom, wage_cap_reached, ranks_full, recruited_last, sold_last, arrived, clan_gold, hour_of_day, day;
  - functions: count, first, last, item, contains, next, nearest_of, next_due, hours_since, weakest_bandit_near, bandits_near, nearest_town, nearest_town_of_realm;
  - a state with no move only decides: the lord keeps the move he has.
- **The checker** names each problem's place (`state recruit, rule 2 ("..."): next_due takes 3 values, given 2`), finds states no rule reaches and dead ends, and holds an agent's own tool to its limits: 12 states, 40 rules, 600 characters an expression, 8 settings, a name of small letters, digits and underscores, no army tools, the safety rule always on, no setting or remembered value named like a function or a built-in.
- **Fixes found on the way:**
  - an agent's own tool now carries the safety rule's numbers as ours do (they were added only to the library's tools, so an agent's tool would never have sheltered);
  - a lord standing still on his way to a stop to recruit or sell at is now reported as stuck, as one on his way to any other place.
- **Checks:** build with no warnings; smoke test passed (341 checks; 36 new: lists and tables, what the checker refuses and how it says so, the limits, the list functions in a running tool, recruit_here, wait and arrived, and Mert's circuit end to end on a made-up world: each place once a day, a band too strong left alone and a small one struck, sheltering from an army, selling first once the loot is over its bar, a place lost skipped with one call-back); 722 game references, 0 missing. The war room is unchanged in this step.
- **Outside the repository:** none. Not installed.

### 02:55 · 0.13 step 3 of 7, the agents' own tools (0.13.0-pre1) · commit 9cdcf03 (not installed)
- **Mert:** "If the agent wants, it can keep the tool it has written as a one-off order, or if it thinks the tool has reuse value, it can add it to its tool/order library so it is able to reuse it", and our tools "should still have full access". This is step 3 of `docs/PLAN-0.13.md` Part F (A1, A4 to A8).
- **An answer's `tools`** (read before its orders): each tool is checked; then kept (`"keep": true`), used once for that answer's orders, changed (writing a kept tool's name again makes a new version: orders already running keep the version they started with), or retired (`"retire": true`). A tool that fails the checker is refused with its problems, and so are the orders naming it. Each tool gets its own line in receipts.log ("KEPT. circuit v1 is kept in your library"), in the orders' record and in the episode index, and a kept or retired tool a chronicle line. In shadow mode a tool is checked only.
- **The realm's library** (`Tools/OwnLibrary.cs`): in the channel folder's `library\` (an index and one file per version), so it carries over between loads, campaigns and runs; the player's realm has one, shared by its own agent and the lieutenant. At most 30 kept tools; no tool may take the name of one of ours. Each kept tool's record (runs, how they ended, call-backs a day, men, gold and strength over its runs, battles fought and won, lords taken and lost, holdings gained and lost, the runs it was used in) is summed from its runs' outcomes. A run's setup records each realm's kept tools as it begins.
- **What the agent reads:** `tools.txt` per channel, with **YOUR OWN TOOLS** after ours (each with what it takes, its settings and its record, refreshed once a day); `tools\<name>.json`, every tool's whole definition, ours and its own.
- **"Check a tool"** (`check_tool`, served beside "ask the way"): the game's own checker, linked into the war room, with each problem's place; with a lord named, the try: the bridge works the tool out on the realm's newest hour (the first move, which rule would fire and where it leads, anything that comes out none), moving no one. The war room waits up to 10 s, else the checker's result stands alone. Every check and try is logged (`routes.jsonl`, the toolbook).
- **The guards on an agent's own tool:** at most 6 call-backs an in-game day (the rest held and summed in one when the day is out); stopped, with its lord let go and the agent told, when it takes over 5 ms of the helper's time an hour for 3 hours running; the 3 changes of state an hour and the safety and food rules hold as for ours. The war room's runaway guard rises from 16,000 to 48,000 characters for an answer that writes tools.
- **The toolbook** gains every tool written (its whole definition), its check and problems, what became of it, and each try.
- **Checks:** build with no warnings; smoke test passed (365 checks; 24 new: tools kept, used once and refused with their orders, the library's files and listing, two versions running side by side, the same definition unchanged, a kept tool's record, retiring, our names refused, the 30-tool limit, the library carried over a new load, shadow mode, the call-back and time guards, the try asked directly and through the channel); war room self-test passed (83; 8 new: check_tool with the checker alone, an answer's tool form, the try through a stand-in bridge and without one, the logging, the runaway limit); 726 game references, 0 missing.
- **Outside the repository:** none. Not installed.

### 03:18 · 0.13 step 4 of 7, diplomacy (0.13.0-pre1) · commit 4ae1c50 (not installed)
- **Mert:** "Build everything out now. We'll do a long run" (every diplomatic act, mercenaries and expulsion included), "The game's hardcoded rules should stay", and a ruler may overrule its clans at the game's price. This is step 4 of `docs/PLAN-0.13.md` Part F (Part B).
- **Read in the game's code first:** a realm the game runs settles each vote the moment it is proposed, and peace, an alliance, a call to war or a trade agreement between two such realms is made by the proposer's vote alone; only the player's own realm is ever asked. The pack (TAOM 2.0.26) blocks peace between hostile realms while the War of the Ring rages, and some wars, inside the game's own actions.
- **The acts** (`Diplomacy.cs`, an answer's `diplomacy`): propose war, peace (with a daily tribute either way and its days), an alliance, a call to war, a trade agreement, a policy (adopt or abolish), expelling a clan; hire and let go mercenary clans; vote; decide as ruler; accept or decline an offer. Each proposal is the game's own vote in the realm at the game's influence price, settled at once with the ruler's clan supporting it fully (`"overrule": true` decides it even against the clans, at the game's price). A mercenary clan is hired only if it would take the game's own terms.
- **The agent's say in its realm's votes:** a vote its other clans propose is held at the moment the game would announce its result, with the clans' support already counted, for up to 12 in-game hours: the agent votes as its clan (the game's price for its weight) and, as ruler, may decide against the clans (the game's price for overruling); otherwise the clans' choice stands. In your kingdom, when an agent rules it, the game holds the votes itself and they are settled the same way.
- **Offers to an agent's realm:** an offer another realm chose to make is held for up to 12 hours until the agent accepts or declines; with no answer it is accepted, as the game does between the realms it runs. A "no" to your kingdom while an agent rules it no longer opens your window.
- **Checked before carried out** (`DiploRules`, which needs no game): the act known, the stance it needs (at war for peace, at peace for war or an alliance, an ally for a call to war), the War of the Ring's rule, the influence, the vote or offer open, the clan in the realm or for hire; then the game's own conditions on its thread. Held votes and offers live in the bridge only, never in the save.
- **What the agent reads:** OPEN VOTES AND OFFERS (each vote's options and the clans' support so far, its vote and decision, what deciding costs, when it closes; each offer's terms and when it lapses) and DIPLOMACY (influence and each act's cost, the War of the Ring, every realm's stance with the war's tally, tribute and trade, the realm's clans, mercenaries in its pay and those for hire, the policies) in its report, copied hourly. A vote opened, closing unvoted or settled, and an offer made or settled, poke the agent; a peace, alliance or call-to-war offer calls a council by default (new agent folders).
- **The record:** `diplomacy.jsonl` (every act and its receipt, each vote held, decided and concluded, each offer held, accepted, declined or lapsed, mercenaries hired and released) and the episode index.
- **Checks:** build with no warnings; smoke test passed (383 checks; 18 new: acts read as written, the checks of every act against a made-up realm's screens, the two report sections, an answer's acts refused, checked in shadow mode and handed to the game's thread when live, the book and the index); war room self-test passed (83); 803 game references, 0 missing; the self-check also looks for the vote's private parts by name. The game-side holding and settling is only testable in the game: it is for the shake-down (step 9).
- **Outside the repository:** none. Not installed.

### 03:26 · 0.13 step 5 of 7, the allied channel and the lieutenant (0.13.0-pre1) · commit 4afd4e9 (not installed)
- **Mert:** "agents communicating with their allies to coordinate attacks, and the player being able to communicate with their allies (who are agents) through their liutenant", and the lieutenant writes tools "based on my permission". This is step 5 of `docs/PLAN-0.13.md` Part F (B5).
- **Letters** (`Allies.cs`, an answer's `letters`): an agent writes to an allied realm's agent; the letter reaches it in its next poke, saying who wrote and when; your realm is written to through your lieutenant, into its window. A letter binds no one and carries no orders, and tells only what its writer chose to (each realm's fog holds). A letter to a realm not allied, or one no agent reads, is refused with that reason.
- **Joint plans:** a letter may carry one in a fixed form (what, where, when, each side's part), with an id; later letters agree to it or decline it (only the realm it was proposed to), or mark it under way, done or dropped. Plans still open show in both realms' reports under **ALLIES AND JOINT PLANS**, beside each ally, whether its ruler reads letters, and its last letters.
- **The lieutenant:** its proposals now carry tools it would write (each shown with what it does, its settings and the checker's result), diplomatic acts and letters to your allies, beside its orders, all under its yes rule (on your words or your Approve; Decline drops them); a load puts a waiting proposal back whole. Your allies' letters come into its window and into its next message. Its war room passes them through, and offers it "check a tool" too.
- **The record:** `allies.jsonl` (every letter and every change of a plan, labelled by session, so a load keeps those of its save's history), the diplomacy book and the episode index.
- **Checks:** build with no warnings; smoke test passed (393 checks; 10 new: letters refused to a realm not allied and to one no agent reads, a letter with a joint plan delivered in the ally's next poke, the writer agreeing to its own plan refused, the ally agreeing, the section in both reports, the store and the book, an answer's letters receipted, letters and plans read back at a new load); war room self-test passed (85; 2 new: the lieutenant's message with letters and a proposal's letters, the answer file carrying tools, diplomacy and letters); 803 game references, 0 missing.
- **Outside the repository:** none. Not installed.

### 03:33 · 0.13 step 6 of 7, the instructions' skeleton (0.13.0-pre1) · commit 59327cf (not installed)
- **Mert:** "build all of the skeleton first and once all the building is done, I'll review the wording and we'll write the final prompt", and the circuit "in their prompt for inspiration" with a couple of other examples. This is step 6 of `docs/PLAN-0.13.md` Part F (Part D). The final wording, and the doctrine's diplomacy principles, are for step 8, with Mert.
- **The war councils' instructions** (`agent/warroom-system.template.md`) gain working sections: **Writing your own tools** (the tool's form, what it takes and of which kind, settings, remembering, states and rules, every move, value, name and function of the language, the limits, keeping, using once, changing and retiring a tool, the record, and three example tools: Mert's circuit, a watch that waits on an event, an army leader's sortie), **Checking a tool** (left out of a call without the tool), **Diplomacy** (every act and its form, how proposals, the realm's votes and offers go, mercenaries, the War of the Ring), **Allies** (letters and joint plans); and "How you answer" names the answer's `tools`, `diplomacy` and `letters`.
- **The lieutenant's instructions** (`agent/lieutenant-system.template.md`): tools, diplomacy and letters to allies, all on the ruler's yes, its allies' letters, and checking a tool (left out without the tool).
- **A real short call** (`WarRoom toolcheck --write`, new): a small model (claude-haiku-5-5, low effort) given the two tool sections wrote a two-state tool, checked it, had its first draft refused, fixed it and answered with a tool that passes the checker, in 8.8 s. (A second run's model skipped the mistake it was asked to make; the check now passes on the loop itself and says whether a draft was refused and fixed.)
- **Checks:** smoke test passed (395 checks; 2 new: the instructions' three example tools each pass the game's checker as written, and the circuit there is the one the tests run, rule for rule); war room self-test passed (85).
- **Outside the repository** (each backed up first as `*.bak-20261028-before-0.13`):
  - `C:\Users\karab\Projects\orc-war-council\warroom-system.md`, `dolguldur-war-council\warroom-system.md`, `kingdom-war-council\warroom-system.md`: the new template, each keeping its own realm's two name lines (fingerprints be4df7e7, b0d45ad1, ce056fba);
  - `C:\Users\karab\Projects\lieutenant\warroom-system.md`: the new lieutenant template (f28838b5);
  - the three realm councils' `warroom.txt`: `council_after` gains "offers peace | offers an alliance | offers a call to war", so an offer calls a council.
- Not installed.

### 03:41 · 0.13 step 7 of 7, everything together: version 0.13.0 installed · commit 9d0e902
- **Mert:** "Implement the full plan now, do not stop until you are done." Step 7 of `docs/PLAN-0.13.md` Part F: all tests together, the documents, version 0.13.0, the install.
- **The speed test** now runs 30 of the agents' own tools (the circuit, on the orcs' first lords of the big made-up world) beside ours: within budget, the 95th slowest of a hundred hours 166 ms (budget 1,000; median 134 ms; 181 ms at step 1), and the time guard stopped none.
- **All tests:** smoke test 395 checks passed; war room self-test 85 passed; dashboard self-test 27 passed; `WarRoom toolcheck --write` passed (step 6); 803 game references, 0 missing, in the installed DLL too.
- **The documents:** a new [DIPLOMACY.md](DIPLOMACY.md) (what the game does and why votes are held, every act, the agent's say in its realm's votes, offers, what it reads, allies, the lieutenant, the record); [TOOLS.md](TOOLS.md) gains "The language grows" and "The agents' own tools"; [WARROOM.md](WARROOM.md) the answer's new parts, `check_tool`, the runaway limit, `thinking_display`, the call lines' new fields, `toolcheck --write` (and a stray character where a backslash belonged, from an earlier edit, put right); [LOGGING.md](LOGGING.md) "The training record"; [LIEUTENANT.md](LIEUTENANT.md) tools, diplomacy and letters; [MEMORY.md](MEMORY.md) what else carries over; [PLAN.md](PLAN.md)'s week-4 list; [PLAN-0.13.md](PLAN-0.13.md) where it stands and the docket.
- **Version 0.13.0** (`SubModule.xml`, the module and the war room).
- **Installed** with `scripts\deploy.ps1`, the game closed (no Bannerlord process, no war room or dashboard running): `Modules\BannerlordAgents` in the game's folder, the DLL's sha256 `06f9169d7afdc627…`, `runs\Runs.exe` beside it. It stays where it was in the launcher's list.
- **On the docket** (for Mert, step 8, and the shake-down, step 9): in `docs/PLAN-0.13.md` "Where it stands".

### 03:42 · The war rooms and the dashboard started again (outside the repository)
- **What:** `scripts\agents.ps1 start`: the four war rooms (orcs, Dol Guldur, the lieutenant, the kingdom) and the dashboard, detached, as before the build; each waits for the game. On starting, each war room added `thinking_display = summarized` to its folder's `warroom.txt`, as step 1 said it would (all four checked).

### 12:33 · The two instruction drafts put back as committed (nothing to commit)
- **What:** after the morning's report, the repository's two instruction drafts were found changed on disk: in `agent/warroom-system.template.md` the four new sections (Writing your own tools, Checking a tool, Diplomacy, Allies: 136 lines) had been replaced by the word "now", and a stray character had been added to the first line of it and of `agent/lieutenant-system.template.md`. It looks like typing that went into the editor after a link in the report had selected those lines. Both were put back from the last commit (`git checkout`); the changed copies are kept in the session's scratch folder. The copies the agents read were never touched: all four matched their fingerprints (be4df7e7, b0d45ad1, ce056fba, f28838b5). Links in reports now point at one line, not a range.
- **The wording:** Mert asked for the new sections in the chat, to read them there for step 8; they were given to him whole, word for word.

### 12:58 · Version 0.13.1: the backup on the D: drive, installed · commit 9d73c9c
- **Mert** (decision 9, on the proposal of a copy on the laptop's second drive and one in a private Hugging Face dataset): "I would keep the data local for now, at the D: drive."
- **What:** `Runs.exe backup [--to <folder>]`, and at every run's end the bridge asks for it after the bundle (`Runs.exe bundle <run> --backup`). The records folder (`%LOCALAPPDATA%\BannerlordAgents`) and every agent's folder in `agents` are copied into the place `backup` in `settings.txt` names: `D:\BannerlordAgents-backup`, on the laptop's second disk (789 GB free). It never deletes; a file that only grew gets its new bytes added; one that shrank or was rewritten is copied whole, and the copy it replaces is kept under `earlier\<time>\`; a file another program holds shut is named and left for the next backup. It works at background priority, refuses a place inside what it copies or a drive without room, and writes `backup.log` and `README.txt` beside the copy. The bridge knows the new setting (it writes `settings.txt` back with only the keys it knows, so an unknown key would be lost). [LOGGING.md](LOGGING.md#the-backup-since-0131), [RUNS.md](RUNS.md).
- **Also:** two fields of the tool checker that only the game's side sets now start at their defaults, so the war room's build has no warnings. Its build had shown these two warnings since step 3 (commit 9cdcf03): the entries for steps 3 to 5 said "build with no warnings", which held for the module but missed the war room.
- **Checks:** a full rebuild with no warnings; smoke test passed (405 checks; 10 new: the first backup whole, nothing written when nothing changed, a grown file's new bytes added to the same copy, a shrunk file copied whole with its old copy kept, nothing deleted, a file held shut left for the next backup and copied by it, a place inside what it copies refused, `bundle --backup`, the bridge keeping the setting and asking for the backup only when a place is set); war room self-test passed; dashboard self-test passed (27); 803 game references, 0 missing.
- **Version 0.13.1** (`SubModule.xml`, the module, Runs.exe; the war room stays 0.13.0).
- **Installed** with `scripts\deploy.ps1`, the game closed and the war rooms stopped: the DLL's sha256 `d4a75205b821ae56…`, `runs\Runs.exe` 0.13.1.
- **Outside the repository:**
  - the war rooms and the dashboard stopped for the build (about 12:45) and started again after the first backup (12:56, `scripts\agents.ps1 start`, detached; each waits for the game);
  - `%LOCALAPPDATA%\BannerlordAgents\settings.txt`: `backup = D:\BannerlordAgents-backup` added, with its comment line, the game closed;
  - `D:\BannerlordAgents-backup`: the first backup, made by hand with the installed Runs.exe: 35,062 files from the records folder and the four agent folders, 7,480.6 MB, in 6 min 34 s at background priority, none in use or failed. Checked against the originals: the same files and bytes in each of the five folders, and the same fingerprints for the files tried (the orcs' pokes, `settings.txt`, `bridge.log`, the orcs' instructions). A second backup at once found all 35,062 unchanged and wrote nothing. 777.7 GB left on D:.
- **On the docket** (step 8, with Mert): the instructions' final wording and the doctrine's diplomacy principles; whether a realm allied to his gets an agent; letters to realms that are not allies. A copy off the laptop can follow when he wants one.

### 19:35 · Version 0.13.2: the records moved to the D: drive, installed · commit cd13859
- **Mert** (asked after 0.13.1 whether the records themselves should move too): "Yes, move the records themselves to D: as well."
- **What:** the records folder now lives at `D:\BannerlordAgents`, on the laptop's second disk. `%LOCALAPPDATA%\BannerlordAgents` is a link (a junction) to it, so the bridge, the war rooms, the dashboard, Runs.exe and the scripts all find it where they always did, and none of them changed. The four agent folders stay where they are (`C:\Users\karab\Projects\...`, 80 MB). [LOGGING.md](LOGGING.md#where-the-files-are); CLAUDE.md's rule on logs says to keep to the usual path.
- **The backup's check** that its place is not inside what it copies now follows links (Runs.exe). Through the link, a place inside `D:\BannerlordAgents` looked as if it were outside the records folder, so each backup would have copied the one before it into the records until D: was full. The installed 0.13.1 Runs.exe did just that on a made-up folder; 0.13.2 refuses it, there and on the real folders (`backup --to D:\BannerlordAgents\copy-test`: refused, nothing made).
- **The open files' lock** (the module): the smoke test, run again after a first install, ended in two runs out of three with the worker's flush of the open files meeting a file the test had just opened on its own thread ("Collection was modified"). In the game only the worker writes those files (the game's thread hands its lines to it), and no crash record names the module (Windows' application log, the last 30 days: only these test runs). One lock now guards them. A new check opens 400 files on one thread while another flushes them all: with the old code it failed with the same error, with the lock it passes.
- **Checks:** the smoke test passed three times running, then again in `deploy.ps1` (407 checks; 2 new: a place inside where a linked records folder really is is refused; the open files on two threads at once); war room self-test passed (85); dashboard self-test passed (27); a build with no warnings; 803 game references, 0 missing, in the installed DLL.
- **Version 0.13.2** (`SubModule.xml`, the module, Runs.exe; the war room stays 0.13.0).
- **Installed** with `scripts\deploy.ps1`, the game closed and the war rooms stopped: the DLL's sha256 `036ec390ad8baf63…`, `runs\Runs.exe` 0.13.2 (`Runs.dll` `5d704cd86618ab5e…`). A first install at about 19:20, after one smoke test that passed, had put in Runs.exe 0.13.2 beside the unchanged module (still 0.13.1, DLL `787aeab0641b8148…`); the two test runs after it failed as above, and this install replaces it.
- **Outside the repository:**
  - the war rooms and the dashboard stopped at 19:15, started again at 19:22 after the move, stopped at about 19:28 for the second build and started at 19:33 (`scripts\agents.ps1`, detached; each waits for the game);
  - the move, the game closed: the records folder renamed to `%LOCALAPPDATA%\BannerlordAgents-old-20261028` (so nothing could write to it, and the rename proved nothing held a file in it); copied with robocopy to `D:\BannerlordAgents` (28,674 files, 6,003 folders, 7.22 GB, in 4.5 s), whose permissions were set as the old folder's (Mert's account, the administrators and the system only); checked against the old folder file by file: the same files, sizes and times, every byte the same (7,757,658,160), nothing extra; then the link made at the old name. Reads and writes through it land on D: (the war rooms' and the dashboard's logs since 19:22);
  - the old copy stays on C: as it was at the move (28,674 files, 7.2 GB) until Mert says it can go; C: has 42 GB free with it, D: 770 GB;
  - `D:\BannerlordAgents-backup`: a backup through the link at 19:22 found 35,056 of the 35,062 files unchanged and added the new bytes of 6 logs that had grown.
- **On the docket:** deleting the old copy on C: (Mert's word); a copy off D:, now that the records and their backup share that disk; step 8 as before.

## Thursday, October 29

### 15:08 · The agents' instructions: raiders struck while they are alone · commit 4fc6b84 (the template)
- **Mert, Oct 29**, reading the instructions (step 8): "And for the general system prompt: telling the ai to attack raiders 'on their way home' is wrong in terms of the game mechanics. The smarter move is to attack them when they are isolated from their allies and we can either catch them before they get to safety or if they are already bogged down in a raid, which means they are stuck, isolated, and therefore in a vulnerable position."
- A correction in the doctrine's own words, the same for every realm agent, with nothing of this war in it. The reasons, the game's rules behind it, the grounding and the caveats: docs/DOCTRINE.md, "Principle 7 corrected (Oct 29)".
- **What changed** (`agent/warroom-system.template.md`, principle 7 only):
  - now "Raiders: small forces, struck while they are alone": a raider is weakest cut off from its own (*On Skirmishing* 9: once the raiders, "rushing to get to the villages and gather as much booty as possible", are too far from their main body to fall back on it, the general falls on them "as they are scattered all around"); strike it while it is at work on a village, where the raid holds it and it does not flee as it would on the march; strike one on the march only if it can be reached before it reaches safety, its own walls, its army or its allies;
  - kept from Oct 25: small groups of lords strong enough to crush them, never the armies; an army turns aside only for an enemy force worth more than its task; never chase a raider into the arms of its main army;
  - gone: raiders "slow and careless" with plunder, the "return home unscathed" quotation (*On Skirmishing* 4, which is about a whole host coming home through the passes), and "Strike them on their road home, not in the strength of their first advance".
- **The game's rules behind it** (the 1.4.8 source): a raid is a battle at the village, the militia first and then the looting until the village's health runs out (`RaidEventComponent`); a party in it makes no quick moves (`MobilePartyAi.TickInternal`), so it makes no move to flee an attacker; its leader still decides every hour and can end the raid for a better aim (`AiPartyThinkBehavior`); on the march a lord flees a stronger party it can outpace.
- **Checks:** the smoke test passed (407 checks, among them the instructions' example tools and the circuit read from the template). No code changed.
- **Outside the repository:**
  - each realm agent's `warroom-system.md` (orc, Dol Guldur and kingdom war councils), edited in place at 15:08: the one paragraph replaced. Each still matches the template word for word apart from its own name and opening line (compared line by line: the orcs' differs nowhere, the other two in lines 1 and 3). Fingerprints (sha256), before → after: orcs `be4df7e7…` → `e16d43b6…`, Dol Guldur `b0d45ad1…` → `0a4a33b1…`, kingdom `ce056fba…` → `dd98fbd5…`;
  - backups are `warroom-system.md.bak-20261029-before-raiders-alone`;
  - the game was closed and the war rooms kept running; each call reads the instructions afresh, so the new text is in force from their next answer, with no restart. The lieutenant's instructions have no doctrine and are unchanged.
- **For comparisons:** every run so far was played with the old principle 7; the shake-down and the runs after it are on the new one. A run's setup records each agent's instructions' fingerprint, so it shows.

### 15:44 · Version 0.13.3: recruiting at towns and castles, in the instructions and the circuit example; installed · commit 7727645
- **Mert, Oct 29**, on the instructions given whole in the chat: "I like the instruction set, but would want a revision to the recruitment loop example. The game allows recruitment at towns and castles, and towns actually have the most amount of notables to recruit from so are recruitment centers. Make sure this is known, and that the recruitment circuit state machine also includes recruitment at a town and at a castle so the example doesn't confuse the agent."
- **The game's rules, read for it:**
  - the base game gives notables to towns (2 merchants, 2 gang leaders, 1 artisan) and villages (1 headman, 2 rural notables) only (`DefaultNotableSpawnModel`, `NotablesCampaignBehavior`);
  - TAOM keeps those counts, some peoples' feats changing them (`TaomNotableSpawnModel`), and adds castle recruiting (`TAOM.Features.CastleRecruitment`): 3 notables a castle by default, with the game's own lords recruiting there too. Mert's TAOM settings file holds no changes, so the defaults are in force;
  - each notable holds up to 6 volunteers (`Hero.VolunteerTypes`);
  - our records agree: of the 268,289 men the watched realms' lords recruited in the record (`recruitment.jsonl`, 1,356 realm-days), 147,430 came from castles, 62,101 from towns and 58,758 from villages; one lord's day at one place gave about 10.3 men in a town, 5.8 in a castle and 5.6 in a village;
  - our recruit order and the `recruit at` move already take volunteers in any town, castle or village with notables (`RecruitTour.MayRecruitHere`).
- **The instructions** (`agent/warroom-system.template.md`):
  - **a new "Recruiting" section,** before "Money": volunteers come from the notables of towns, castles and villages, up to six a notable, new ones over the days; a town has the most notables, about five, so towns are the centres of recruiting, a castle or a village about three; which men a lord may take depends on his standing with the notable, and each must fit his gold and his wage cap; a besieged town or castle, a village raided or looted, or an enemy's place gives none. Game facts only: none of the records' numbers is in it.
  - **the circuit example, Mert's,** now recruits each round first at the realm's nearest town, where its loot is sold as it rides in (the module's loot-only sale on entering a town), then at each of its places, then at its home castle (`recruit at at`), and between rounds rests or patrols at home as before. When its loot is worth `sell_above` it goes to the town at once, unless already inside it. A town lost or besieged on the way is passed over, and so is a besieged home. The separate "sell" state is gone: the town's stop sells. Its lead-in, `about` and `takes` say so.
- **The order library and the module** (version 0.13.3): the guard order's description no longer says "castles give no recruits" (it refills in the nearest town or village, as before), and a refusal for a tool's `friendly_town_or_village` place no longer says "only those give recruits". Both are what the agents read, and both contradicted the new section. TOOLS.md says the same. The war room carries the same library for its tool checks.
- **Checks:**
  - smoke test passed, 410 checks, 3 new (the town first, then home's castle, back to the town with loot), and the circuit in the instructions is still the one the tests run, rule for rule; it passed again in `deploy.ps1`;
  - the speed test with 30 of the new circuits: WITHIN BUDGET (95th slowest hour of a hundred 113.8 ms of 1,000);
  - war room self-test passed; dashboard self-test passed; build with no warnings; 803 game references, 0 missing, in the installed DLL.
- **Installed** with `scripts\deploy.ps1` at 15:43, the game closed and the war rooms stopped: the DLL's sha256 `bfec99f90f069fab…`, `SubModule.xml` v0.13.3; the old line is no longer in the DLL. The war room and Runs.exe keep their versions (the war room rebuilt for its copy of the library).
- **Outside the repository:**
  - each realm agent's `warroom-system.md` (orc, Dol Guldur and kingdom war councils) rebuilt from the new template at about 15:41, keeping its own name and opening line (compared line by line: the orcs' differs nowhere, the other two in lines 1 and 3). Fingerprints (sha256), before → after: orcs `e16d43b6…` → `a299989f…`, Dol Guldur `0a4a33b1…` → `ddc12c12…`, kingdom `dd98fbd5…` → `a399c6a1…`. Backups are `warroom-system.md.bak-20261029-before-recruiting`. The lieutenant's instructions have no circuit and are unchanged;
  - the war rooms and the dashboard stopped at about 15:42 for the build and started again at 15:44 (`scripts\agents.ps1`, detached; each waits for the game). Each channel's `tools.txt` takes the guard order's new words when the next game session writes it.
- **On the docket:** the recruitment day log (`Recruitment.Day`, `Recruitment.OnHour`) still counts the volunteers waiting and the hours where lords could recruit in towns and villages only; castles should count too (our measurement only; the agents never read it). And, for Mert to decide: the guard order could refill at the castle it guards when that castle has volunteers, instead of riding to the nearest town or village.

### 18:53 · Version 0.13.4: the guard order takes its own castle's volunteers first; the recruitment log counts castles; installed · commit 3c78666
- **Mert, Oct 29**, to the offer that the guard order refill at the castle it guards now that castles recruit: "Sure, lets do that. Please fix the recruitment log as well. Once you are done, please make ready for the test with me playing as Aragorn (Arnor & Gondor)".
- **The guard order** (`Tools/Library/defend.json`): when its ranks fall below the floor and it is not needed (the same three tests as before), a guard of a castle or town now first goes to the holding itself and takes its volunteers (a new state, `restock`, with the move `recruit at`), staying in its area and calling nobody back. Back to its patrol once its ranks are over the floor (after at least 3 hours in the state). Still short 2 hours after arriving, it rides on to the nearest town or village of the realm or an ally (`nearest_supply()`) with a call-back, and from there the old refill rules hold (12 hours without 5 points gained, two days in all). When the nearest town or village is the holding itself or there is none, it stays there under those rules, quietly. It breaks off when an enemy lord or army comes within its radius or the holding is besieged (the patrol's rules decide what next), and gives up after a day without reaching the holding (back to its patrol, trying again in two days, with a call-back). A guard of a village goes straight to the old refill. Its description (`about`), `calls_back` and the `floor` setting say so; the agents read them in `tools.txt` and `tools\defend.json`, both written by the bridge at the next session.
- **The recruitment log** (`Recruitment.OnHour`, `Recruitment.Day`): castles now count among the places where a lord could recruit (not besieged), for the hours each lord spent there, and among the realm's places with volunteers waiting (kind `castle` in `places`); the day's sentence says "towns, castles and villages". The log is our measurement only; the agents never read it. Days logged before today counted towns and villages only, so the waiting volunteers and the hours in recruiting places jump from today's days on: compare across with care.
- **Docs:** TOOLS.md (defend's refilling), LOGGING.md (the day's sentence), CLAUDE.md (the "Where volunteers are" note).
- **Checks:**
  - smoke test passed, 411 checks: the guard's first refill test now checks the castle first, no call-back, and the ride on to the village 2 hours after arriving still short; a new one, a lord the castle's own volunteers bring over the floor goes back to its patrol without leaving its area or calling back; the "few recruits in 12 hours" test now runs after the castle's 2 hours; the others take the castle as the first stop;
  - the speed test: WITHIN BUDGET (95th slowest hour of a hundred 124.3 ms of 1,000);
  - war room self-test passed; dashboard self-test 27 passed; build with no warnings; 803 game references, 0 missing.
- **Installed** with `scripts\deploy.ps1` at 18:53, the game closed and the war rooms stopped: the DLL's sha256 `e25cd5c4211d56e1…`, `SubModule.xml` v0.13.4; the new state and wording are in the DLL, the old log wording is not.
- **Ready for Mert's test as Aragorn (arm A, Mert's call for the shake-down instead of the plan's C1):** the modes were already arm A (`arm.ps1 -Arm show`: the orcs' and Dol Guldur's agents live, the lieutenant live, the kingdom's agent off; set Oct 28 00:30); `TEST START` unchanged since Oct 22 (sha256 `8E2FF80DE13C9D5C…`, as in every run's setup); the launcher's modules the same 22 as run `20261025-2103-C1`, ours ticked; every agent's own-tools library empty (no folder yet), so the run starts with our tools only; the realm agents' instructions as rebuilt at 15:41, the lieutenant's equal to its template; C: 37.9 GB free, D: 770 GB.
- **Outside the repository:** the war rooms and the dashboard stopped at about 18:50 for the build and started again at 18:56 (`scripts\agents.ps1`, detached; each waits for the game); no idle host windows left.

### 19:16 · The lieutenant's time limit raised to 5 minutes (outside the repository), in the run's setting up
- **Mert, Oct 29**, during the setting up of run A (`20261029-1903`, readied, the clock not yet run): "My liutenant was not able to answer my ask. Why is this?"
- **Why:** his words (#4, 19:11:29: station the realm's forces evenly and defensively across the Dol Guldur and orc fronts) took the lieutenant 122.6 s, and its folder's `warroom.txt` stops a call at 120 s (`timeout_seconds = 120`; the realm agents run on the 240 s default). It read two look-ups, asked "roads in" of 8 holdings and the way once, thought about 9,800 characters, and was writing the last of 64 proposed orders (every lord of the realm to a holding on one front: Lasgalor, Imnagath and Torech Emel against Dol Guldur; Goblin Town, Rivendell, Baracharn and Fennas Drúnin against the orcs; with a warning that the northern castles would be left to their garrisons) when it was stopped. A stopped call is not retried and its words are not read, so he got "(I could not answer that: the call took over 120 s and was stopped)". The answer as far as it got is kept in `logs\warroom-lieutenant\calls\20261029\191129-chat-4\raw.jsonl`. On Oct 23 a smaller ask of the same kind (19 orders) took 57.6 s.
- **Changed:** `C:\Users\karab\Projects\lieutenant\warroom.txt`, `timeout_seconds` 120 → 300 (sha256 `a2b2c8a4…` → `7a75a73c…`; backup `warroom.txt.bak-20261029-before-timeout`). The game's clock is stopped while the lieutenant's window is open, so the longer limit costs waiting, not game time. The war room reads its settings at its start, so only the lieutenant's was restarted (19:16:07, pid 57136, through `scripts\agents.ps1 start`, which leaves running ones alone); the orcs' and Dol Guldur's carried on undisturbed. Nothing in the game or the module changed; the game was running.
- **For Mert:** ask again; the same ask should now get its answer.

### 19:36 · Version 0.13.5: the test carries on across closing the game and loading its save; the lieutenant with no time limit; installed · commit 008c946
- **Mert, Oct 29**, after the first hours of run A (`20261029-1903-A`, about 8 in-game hours, saved as `TEST CONTINUOUS SAVE` at 19:15, game closed): "Lets do two things: 1: remove the time limit on the liutenant's thinking. 2: make sure that the test logs and the agents' records survive the game being closed and reloaded into the same save, so the test continues when I have to step off the game and back on it with time."
- **What already survived** (read in the code before changing anything): the run (a load of one of its own saves carries it on, `Run.OnLoad`, since 0.11; `TEST CONTINUOUS SAVE` is among its saves), the agents' memory and chronicle and the lieutenant's conversation (labelled per session, found again by the save's note, since 0.9.1 and 0.11.1; the 19:15 save's note is in `saves.jsonl`), the training record (append-only, tagged by run), the agents' own tools (their libraries). **What did not:** every order in force (cleared at every load, `Orders.Reset`, `Runner.Reset`): the agents' lords went to the game's own AI until the agent ordered them again, and each tool's progress was lost; the agents' wage caps; `run.json`'s last hour (written only at day lines and marks, so hours played after a save and lost within a day went unmarked when replayed); a war room not running after a restart of the laptop (nothing started it). Also found: the run's readied folder `runs\20261029-1903` stayed behind with `status: armed`, because my `Runs.exe watch` at 19:08 put its cursor (`watch.json`) in it and the tidy-up removes a folder holding only `run.json`; left as it is (nothing is deleted without Mert's word).
- **The orders carried on** (new `CarryOn.cs`): at every save (`OnSaveOver`, after the take-back) the game's thread lists the tool orders held (`Orders.KeepForSave`) and the wage caps (`Wages.KeepForSave`), and the helper (urgent lane, as each hour's steps) writes `data\<campaign>\orders\<save>.json`: each tool run with the very definition it runs (`Runner.Export`: its order, settings, state and the hour it entered it, remembered values with tables and lists, the move in force, the safety and food rules' places, its call-back count) and its record so far (`Outcomes.Export`). Never in the save; overwritten with the save of the same name. At a load (`Bridge.Start`, after the reset is posted) the file is read when it is that save's at that hour (History's rule, 0.05 h); each order is checked on the game's thread as at its start (`Orders.Resume`: alive, free, leading his party in the realm, on no Party AI Controls order unless the lieutenant's, among the lords who obey the agent, the realm's agent the same folder and live) and the lord held again under the same tool run's id (new runs are numbered past it), shown in Party AI Controls' panel for the lieutenant's; on the helper the tool is taken up where it was and its move given again (`Runner.Resume`, `Outcomes.Resume` with a `run_resumed` line in the toolbook); a wage cap is put back (`Wages.Resume`). What could not carry on is let go and said: the realm's first look reads "The game was loaded from a save: the N orders you had in force then carry on as they stood (YOUR ORDERS IN FORCE lists them). Not carried on: ..." (still matching the council's "First look" words).
- **The run record:** `run.json` written at every save (not only at a new save name) and when the campaign ends (`Run.OnStop`), so a load knows how far the run had gone. The readied folder's tidy-up passes over `watch.json`.
- **The war rooms:** at each load the bridge looks for each switched-on agent's war room by the lock it holds (`WarRooms.Check`, the name made as the war room's `LiveLock` makes it) and starts any that is missing, with no window, from `war_room` in settings.txt (new key; empty: the alarm `war_room_down`). Checked from outside with the same name: the running orcs' and lieutenant's war rooms found, a folder with none not found.
- **The lieutenant:** no time limit and no thinking cap. The war room reads `timeout_seconds = 0` as no limit (`Claude.Run`; its settings reader took only values over 10 before); a hung call is still stopped after 45 s of silence; a proposal (`"propose"`) gets the longer runaway limit (48,000 characters, as an answer writing tools), since one naming every lord of the realm could pass 16,000. Its folder's `warroom.txt`: `thinking_cap` 0 (was 2000) and `timeout_seconds` 0 (300 since 19:16); backup `warroom.txt.bak-20261029-before-no-limit`. Its start line now reads "thinking as long as it needs, no time limit".
- **Checks:** smoke test 420 (9 new: remembered values of every kind out and back; the circuit written down three stops into its round, cleared, taken up in the same state with its move given again, numbered past, patrolling within its own radius setting, and going back to none of the places it saw before the save; the toolbook's `run_resumed` with its record kept; a guard carried on an hour into its castle's refill riding on at the hour it would have; a run whose state its tool lacks refused with the reason); war room self-test (new: limits of 0 read as none, the proposal's runaway limit); dashboard self-test 27; 814 game references, 0 missing; the speed test WITHIN BUDGET both times (95th slowest hour 222.6 ms beside the self-tests, 204.3 ms alone, of 1,000; this afternoon's were 113.8 and 124.3, the difference in the file-writing parts: look-up files, files, dashboard).
- **Installed** with `scripts\deploy.ps1` at 19:36, the game closed and the war rooms stopped: DLL sha256 `8054e03af5669e23…`, `SubModule.xml` v0.13.5; the new texts are in the DLL.
- **Outside the repository:** `%LOCALAPPDATA%\BannerlordAgents\settings.txt` gained `war_room = C:\Users\karab\Projects\bannerlord-agents\src\BannerlordAgents.WarRoom\bin\Release\net8.0-windows\WarRoom.exe` (backup `settings.txt.bak-20261029-before-war-room`); the lieutenant's `warroom.txt` as above; the war rooms and dashboard stopped at about 19:24 and started at 19:36 (`scripts\agents.ps1`).
- **Not yet seen in the game:** a carry-on at a load, a war room started by the game. The 19:15 save was made before 0.13.5, so it has no orders file: its first load carries the run, the memory and the conversation on, but not the orders; every save from now on carries them.
