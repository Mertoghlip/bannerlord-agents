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
