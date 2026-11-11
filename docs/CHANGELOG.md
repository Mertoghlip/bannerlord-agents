# Change log

Every change to the project, oldest first, so the checkpoint (Oct 27) and the final report (Dec 1) can say what changed, when, why, and what it did.

**What gets an entry:**
- every push;
- every install into the game;
- every edit outside this repository: other mods, the launcher's module list, the agent's folder, the settings.

**What each entry holds:** the local time (Pacific), what changed, why, the numbers and checks that go with it, and where it lives (a commit, files, or a path outside the repository).

**Commits:** an entry names its commit. An entry written with its own change gets the hash from a small follow-up commit in the same push.

## Tuesday, October 6

### 23:37 · The project set up · commit b80f42e
- **What:**
  - the separate BannerlordAgents game module: it loads, logs the session start and the real seconds per in-game day, and has `agents.status`;
  - the build reads the game's files from the local install, and none are committed;
  - `scripts/deploy.ps1` refuses to install while the game is open;
  - the plan (`docs/PLAN.md`) and the notes for Claude (`CLAUDE.md`).
- **Why:** the proposal was submitted on Oct 6. A mod of its own avoids mix-ups, and everything goes on GitHub.

### 23:42 · The Oct 5 brief saved · commit b840ec2
- **What:** `docs/proposal/brief.md`, exported word for word from the Claude doc "CS 329M Project Brief: Bannerlord Agents".

## Wednesday, October 7

### 14:52 · The plan updated · commit 24af906
- **What:**
  - live play: the clock never stops for an agent;
  - first tests on the Misty Mountain Orcs in my own campaign;
  - my campaign-map play recorded as core data (a new research question, RQ6);
  - the bridge's work kept off the game's busy core;
  - `docs/FIRST-TESTS.md` written, in seven phases.
- **Why:** my decisions of Oct 7.

### 15:01 · The plan: a ruler's decisions and vassal orders · commit 9cfe7aa
- **What:**
  - the recorder also covers realm decisions and the vassal orders I give in conversation, read from Party AI Controls without changing it;
  - phase 7 added (Opus runs the whole orc realm);
  - a drawing board for parked ideas.

### 15:33 · Install 1, version 0.2.0, into the game · commit 1169b94 (pushed 15:37)
- **What:**
  - the orcs' view and pokes, and the written report;
  - the play recorder;
  - the performance log;
  - hand orders, with lords handed back around every save;
  - the smoke test, 7 checks.
- **Before the install:** my 3 newest saves were copied to `C:\BannerlordArchive\save-backups\*.before-agents-20261007-1533.sav` and checked identical.

### About 19:10 · The launcher: Bannerlord Agents ticked (outside the repository)
- **What:** BannerlordAgents was enabled in the launcher's module list (`LauncherData.xml`), after RTSCameraUniversal.
- **Backup:** `LauncherData.xml.bak-20261007-1910-before-agents`.

### 19:28 · The agent channel and the agent chat · commit c79eabe
- **What:**
  - the agent channel: `pokes.log`, `poke.txt`, `report.txt`, answer files in, receipts out, in three modes (off, shadow, live);
  - orders get the recruit and free verbs, and `agents.vassal` orders my own lords;
  - reports show ids in brackets;
  - templates for the agent chat's own folder, and the script that installs them;
  - the smoke test grows to 14 checks.
- **Outside the repository:**
  - the agent's folder `C:\Users\karab\Projects\orc-war-council` was created by `scripts/setup-agent.ps1`;
  - the bridge's settings were set to shadow mode, pointing at that folder.

## Thursday, October 8

### 00:36 · Version 0.3, the strategists' logs · commit fde68bd (installed about 00:35)
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
- **What:** 27 minutes of my campaign with version 0.3 and the orc agent (Opus 5.5) in shadow mode.
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

### 15:54 · The first session's report · commit 856bd87
- **What:** `docs/reports/2026-10-08-first-live-session.md`.

### 16:56 · Why the orcs were poked 125 times · commit 5b8a3b1
- **What:** `docs/reports/2026-10-08-pokes.md`: every poke by type, the battles by who fought whom, and what each cut would save.
- **Finding:** 100 of the 122 battles were small fights, and the cuts would take the 125 pokes to 58.

### 17:14 · Version 0.3.1, fewer pokes · commit 60ab031 (installed 17:14)
- **What:**
  - a battle pokes only when fought at a town, castle or village (an assault, a sally, a raid) or with lords on both sides. Fights with bandits, caravans and villagers go to the logs only, marked as not poked;
  - an enemy lord pokes only within 30 of one of the realm's holdings (the new setting `sighting_distance`), or when heading for, besieging or raiding one, or chasing one of its parties. Armies always poke. A lord who comes close while already in view now pokes too, which the old rule missed;
  - a raid announced again within a day, and a lost party whose lord's capture already says it, are kept quiet;
  - wording: "on the way to raid" until the raid begins; a lord captured in a battle keeps their name in the battle line; "X's party" no longer reads "X's Party's party";
  - the summary gains a "Not poked" line, and the smoke test grows to 41 checks.
- **Why:** my go-ahead on the four cuts after the breakdown.
- **Expected:** about 58 of the first session's 125 pokes, 7 a day instead of 15, plus a few for lords who come close while in view.
- **Checks:** the smoke test passed in full; 253 game references, 0 missing; the installed DLL has sha256 `0E13B00DB78F626D…`.
- **Outside the repository:** the agent's instructions were reinstalled into its folder. They tell it what no longer pokes, and take effect when the agent chat is restarted.

### 20:24 · The agent's folder readied for the war room (outside the repository)
- **What:** `scripts/setup-agent.ps1 -Mode live`, in its new form (part of version 0.4.0, below):
  - the bridge's settings: `agent = live`, `agent_lords = all`, and `agent_dir = C:\Users\karab\Projects\orc-war-council\warroom` (until then shadow mode and `...\channel`);
  - in the agent's folder: `warroom-system.md`, the war room's instructions for the agent; the channel folder `warroom\`, with `orders\done`, `lookups\` and the order library `tools.txt`;
  - `warroom.txt`, the war room's settings: Opus 5.5 at high effort, thinking capped at 3,000 tokens (5,000 for an urgent poke), 120 s at most per call;
  - the journal: the agent chat's journal is kept as `journal-chat-20261008.md`. `journal.md` carries over its plan in three sections (standing plan, lords and their tasks, lessons), reworded for the new orders, without its list of problems.
- **Why:** my decision to turn the agent on and begin testing: Opus 5.5 commands every orc lord, live.

### 21:14 · Version 0.4.0: tools, armies and the war room · commit 76910ac (installed 20:59, again 21:10 and 21:14)
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
- **Why:** my asks of Oct 8:
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
- **What:** the agent chat of Oct 8, still open in VS Code, added a "Problems" section to `orc-war-council\journal.md`. It said that no pokes had come for about 8 hours, and that I had asked it to stop watching for them.
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

### 11:01 · Version 0.5.0: each realm's memory of the war · commit dc40a2d (installed 11:01)
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
- **Why:** Oct 9: each realm should have a continuous memory of the history of the game. I approved this design over one long conversation, which fills up within an hour or two and could not carry over to a small local model. The conversation is kept, to measure the difference.
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
- **Not yet run:** the full comparison of the two memories with Opus 5.5, about 120 calls; it waits for my go-ahead.

### 11:42 to 11:51 · The second live session, with tools and memory ([report](reports/2026-10-09-second-live-session.md))
- **What:** I loaded the Summer 9, 1087 save, the starting point of the first session, and marched on Western Moria. The orcs ran on version 0.5.0, live, under Opus 5.5.
- **Numbers:**
  - the game's history held 318 entries about the orcs, read in 56 ms;
  - 9 pokes and 6 answers, in 8 to 29 s (median about 16 s);
  - 10 orders carried out, 2 refused, and 4 waiting for the clock when the game was paused and closed;
  - the first story so far, 550 words in 14 s.
  - The agent judged my army too strong, and ordered Ulga's army out of Western Moria to join Vrakdush at Methedras. The order arrived while the game was paused and was never carried out.
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

### 11:55 · Version 0.5.1: army fixes · commit 2b652b5 (installed 11:55)
- **What:**
  - gather judges its army from the next hour on, and a new "fail" ending keeps an army that truly broke up from starting its next order, setting its lord free;
  - gather and add form the army with the lords who can come, naming the ones left out;
  - the chronicle names the order's meeting place;
  - poke numbers come from the campaign's whole poke file once, then from `pokes_<realm>.number` beside it, so the next poke is #126;
  - notes taken over from the old journal hold from the first poke of the memory, so a save from before it starts afresh;
  - renaming the journal can no longer collide with an earlier copy.
- **Why:** the second live session (above). I quit the game for the army fixes.
- **Checks:**
  - the smoke test (81 checks) and the war room's self-test (28) passed;
  - 327 game references, 0 missing;
  - the installed DLL has sha256 `384C41BEBCA7AAA9…`.
- **Outside the repository:**
  - the war room was stopped and restarted at 11:55 and again at 11:57 for the rebuilds;
  - `tools.txt` was rewritten;
  - in `memory\Ah2auppicLLJ\snapshots.jsonl`, the old journal's notes were re-dated from "any date" to poke #1 (Summer 9, 1087, 09:00), so reloading that save starts the orcs afresh (backup `snapshots.jsonl.bak-20261009-before-seed-date`).

### 12:01 to 12:15 · The third run, and what the orcs were missing
- **What:** I reloaded the Summer 9, 1087 save on 0.5.1 and marched on Western Moria again (my standing test), then closed the game for a diagnosis.
- **The fixes held:**
  - the chronicle was cut back 24 lines and the memory went back to empty;
  - the pokes went on from #126;
  - Naglur's army formed with 6 lords and 988 men, with no false "broke up";
  - the story so far was rewritten at 531 words.
- **The agent's answer to my host** (5,528 men, seen at 19:00): a relief army under Vrakdush with 10 lords (about 2,219 men, strength about 3,778, `strike_at` 0.8, waiting up to 120 hours). It arrived while the game was paused and was never carried out.
- **The diagnosis, from the game at 19:00** (the question: why the orcs have so few men):
  - **The realm:** 49 lords' parties with 8,257 men, strength 14,494, against my army at 14,674; 12 of those parties (2,275 men) belong to clans outside the ruler's. Garrisons 4,249 men; militia 2,496.
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
- **My decisions (Oct 9):**
  - build plans A to D: the missing facts, a join order, the garrison counted in a relief, recruitment measured;
  - build the council of war (E1);
  - rewrite the agent's instructions as a medieval commander's, from a study of medieval doctrine, without giving it my own plan (E3).

### 13:30 · Version 0.6.0: the council of war, the missing facts, a commander's doctrine · commit d6aed27 (installed about 13:30)
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
  - no plan for this war (it should arrive at that conclusion on its own).
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

### 19:30 · Version 0.6.1: defend and patrol fixed · commit 03dc3d5 (installed 19:29, again 19:30)
- **Why:** I asked how the defend and patrol orders work, and then, after the walkthrough, decided to fix the weak spots. The walkthrough read the game's patrol in the 1.4.8 code and found five:
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

### 15:19 · Version 0.6.2: shelter where the defence would hold, else run · commit 7000b06 (installed 15:19)
- **Why:** the danger rule: if the strength of all lords defending a place plus the garrison of the sheltering location is enough to defend it against the opposing force, they should shelter there. If not, they should run. 0.6.1 never weighed whether a place would hold: it simply kept lords out of the castle the enemy marched on, and out of defend's own holding.
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
  - the war room was not running when I asked how the agent was doing; Claude started it at 15:37:59;
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

### 16:10 · Version 0.6.3: held lords kept held, refilling only when not needed, every enemy army in the report · commit 00c5261 (installed 16:10)
- **Why:** the live session above. On refilling: some lords might be needed urgently and may be weak individually, but massing them together still makes them strong. The agent should be able to order them to mass together and fight if it needs to, rather than being forced to recruit. It is still a good idea for lords to be recruiting if they are not needed urgently.
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
  - **Not changed:** the agent's instructions. Everything new is facts and order rules; nothing of my own plan.
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

### 16:56 · Version 0.6.4: each enemy army's trail, and a 15th principle of doctrine · commit 545c0b2 (installed 16:55)
- **Why:** in an army, I might deviate slightly off course on my way to an objective to recruit or gather supplies, or maybe to engage an enemy I find vulnerable. The AI should still be getting the information it needs to infer what my main objective is, and slight deviations should not have it readjust its main strategy. Of seven ways proposed, I chose two together: 6 and 1 at the same time.
- **What:**
  - **Each enemy army's trail** (proposal 1): a new report section, ENEMY ARMIES' TRAILS, gives, for each enemy army, where the realm saw it in the last two days, oldest first: when, where (and how far from the realm's nearest holding), and what it was doing.
    - A point is added each hour the army is in view that its course changed, after every 10 map units of the same march, every 6 hours, or when it comes back into sight ("(out of sight 4 h)").
    - 12 points at most; only trails of two points or more are shown; a trail ends when its army is seen broken up, or is gone.
    - Only what the realm saw, so the fog rule holds. The enemy's look-up file carries the same trail, and the view's JSON a `trails` list.
  - **Principle 15 of the doctrine** (proposal 6): "Read the enemy's aim from its road, not its errands", grounded in one research pass: Edward III's letter of 1339, *On Skirmishing* 8 and 9, and Philip VI misplaced south of Paris while the English crossed the Seine at Poissy in 1346 ([DOCTRINE.md](DOCTRINE.md#principle-15-oct-16-reading-an-armys-aim), with the caveats). The research corrected the first draft: the sources show the army's parties ranging wide while its main body keeps its road, not the army turning aside. The principle is general; it names nothing of this war or of how I play.
  - The instructions' list of the report's sections names the trails.
- **Checks:**
  - the smoke test passed in full (118 checks, 3 new: points for changes of course and a return into sight, none for a few more units of the same march; the look-up's trail; the two-day limit, and a trail ending with its army) and the war room's self-test too (38);
  - 352 game references, 0 missing;
  - the installed DLL has sha256 `42A4553B47417781…`.
- **Outside the repository:**
  - the war room was stopped before the builds (idle, the game closed), and restarted at 16:56:02;
  - `scripts/setup-agent.ps1 -Mode live` installed the new `warroom-system.md` (principle 15, the trails in the report's list) and rewrote `tools.txt`; the bridge's settings were unchanged.
- **Not yet seen in a game:** a trail; the agent reading one. Proposed for the next run: I turn aside once or twice on the way to Western Moria, and we count how often the agent's plan changes.

### 19:32 to 19:45 · Live session on 0.6.4 (standard save, marching on Western Moria)
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

### 21:10 · Version 0.7.0: the dashboard, ways and crossings, and the agent's "ask the way" tool · commit 2a9f7b4 (installed about 21:03)
- **Why:** diagnostics and monitoring by me should be a lot easier, with a dashboard with a live map of the game. I wanted to be able to see exactly what each agent sees, have everything they see depicted by icons with a legend, switch between agents, and have a log on the side depicting the moves of the agent I am following. When I click on a given icon on the map, I should be able to see where it is moving. On pathfinding: clicking an icon should show how the army would find its way, and the agent should know how pathfinding works, exposed to it only after it specifically asks for it for a specific army. I approved the plan, all to be implemented now, and all its recommendations: a real tool rather than ready-made pages, no doctrine principle on crossings for now, and the rewind built now.
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

### 21:15 to 21:45 · First session on 0.7.0, and two fixes · commit 877191a (war room and dashboard rebuilt; the game module unchanged)
- **Seen working:**
  - the walking map copied in 4 s (272 ms of the game's thread over 68 frames): 1,638 x 1,550 cells, 147 bridge and ford faces;
  - the self-check passed 12 of 12;
  - the dashboard showed the live map, working phenomenally.
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
- **My question, checked against the night's frames:**
  - The agent set Gashnar to "screen Western Moria" to "find Aragorn's army after Hollin Gate".
  - The game's own way from Hollin Gate to Western Moria runs about 260 map units north round the mountains, past Caradhras, although the two are 53 apart as the crow flies. Aragorn's army took it.
  - Gashnar's screen never went more than about 15 from the town, because the ground within its radius is mostly mountain.
  - The agent has no order to post a lord on a road or to shadow an army, and its report gives distances as the crow flies.
- **Checks:**
  - the dashboard's self-test passed (26 checks, 3 new: a river closed without its bridge, a bridge rebuilt from its centre, a way learned across it);
  - the war room's self-test passed (46);
  - `toolcheck --big` passed.

### 22:10 · Version 0.7.1: watch, shadow, and every distance by road too · commit e1e7ab9 (installed 22:09)
- **Why:** on the agent screening round Western Moria to find Aragorn's army: it would be a much smarter move to send a weaker lord to scout on the road to Western Moria from Hollin Gate, to keep tabs on Aragorn's army. I think the AI is smart enough to do this, but just does not have the tools to do so.
  - Checked against the night's frames: every order that places a lord was tied to a holding, and the report gave distances as the crow flies (Hollin Gate to Western Moria 53, by road about 260 north round the mountains).
  - My decision: implement 1 and 2. In the report, distances should be given both as the crow flies and as the actual travel distance.
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

### 22:48 to 23:51 · First live session on 0.7.1 (the standard save; I marched on Western Moria)
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

### 00:30 · Version 0.7.2: the siege alarm counts every lord coming, enemy castles' militia, doctrine principles 16 and 17 · commit 2c85c51 (installed 00:28)
- **Why:** the siege of Hollin Gate (the session above). The question: why the agent went through with the siege of Hollin Gate even though it had a lot of defenders. The defenders had come after the storm began: about 50 of Arnor's lords, one by one, none in an army, so besiege's alarm (an enemy *army* only) never rang; and the agent was told the castle's garrison, not its militia. My decision: make fixes 1 and 2. The rest is more of a siege doctrine question. The agent should (1) post scouts around the army to figure out if a relieving force is coming during the siege, so it can call it off, and (2) understand that when the enemy is stronger, it is important to strike hard and quick, sometimes forgoing smaller targets in favor of guaranteeing a more important hit. I also felt its siege of Hollin Gate should have been a lot quicker than the amount of time it took, and asked for that to be looked into.
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

### 01:55 · Version 0.7.3: the game's clocks (marches by the game's exact speed rule; sieges and battles run ahead) · commit b881f4d (installed 01:48)
- **Why:** our prediction of the marching speed of a force should be based on the game's exact equations, so the AI has a good idea of where its own and enemies' armies will reach their waypoint. The same goes for siege and battle timers, so it can plan ahead.
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

### 17:30 · Version 0.7.4: siege control (the agent's storm rule and relief bars) and battles run ahead before they begin · commit 13b0b29 (installed 17:28)
- **Why:** the walk-through of the Hollin Gate defeat under 0.7.3 left five gaps: no time for a storm or a field battle before it begins; the agent sees only the relief its own lords see; the storm is the game's choice; an answer comes about 10 game hours after the alarm; the enemy's morale is guessed. My decision: fix all gaps now. Gap 1 is the most important, with time estimates both for siege battles and for field battles. On gap 3: I wondered whether the AI could be given a tool to dictate siege decisions, starting with the storm time, gated on storm immediately, storm after x siege engines constructed and so on, based on its understanding of the enemy garrison's strength and the imminence of relief. On gap 4: the agent should set the bar, what relief force strength level forces a withdrawal and what relief force level forces an immediate storm. It should set the bar as it begins the siege, ahead of time, and the logic should run as part of its siege tool.
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

### 22:15 · Version 0.7.5: what the first evening on 0.7.4 taught; orders for lords in your army; the fairer test save · commit 22b6ba9 (installed 22:12)
- **Why:** the first session on 0.7.4 (the save besieging Western Moria, 21:14 to about 21:40). I picked "C" for my own siege: the report keeps treating my camp like any other (the agent is not told which lord is the player). Then: the other problems were to be fixed as well. Then, on Naglur: the agent chose to lift the siege but was still stuck in the storm, and its logs were to be checked to diagnose why the mismatch happened. And: back to the part where I give orders to my lords. I should be able to give orders to lords who are in my army, and once I have given them an order, they should first leave my army and then execute their order. And: I wanted a fairer, longer term test save where the Misty Mountain Orcs are of more even strength to my kingdom (the line I chose: only the old orc fortresses; their garrisons swapped to orc troops; I reach army lords from the army tab's portraits).
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

### 23:35 · Outside the repository: your armies by order, and raids (TFAOMFixes) · commit 5bf07a3
- **Why:** another feature I wanted for my personal orders, so I can keep up with the AI properly: I should be able to get lords to form armies, pick which lords should join that army, and then give orders to lords that are the leaders of armies to attack a settlement, defend a settlement and so on with their armies. If I am ordering them to create an army as the ruler, all influence costs for that army should be borne by me, including keeping army cohesion. The army should only disband when I tell it to disband. Independently formed armies should be exempt from all of this. On what happens when the influence runs out: armies should disband once cohesion and influence run out. Raids: raiding villages added too.
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: new `VassalArmies.cs`; `VassalOrders.cs`; one line each in `SubModule.cs` (arming, saving) and `Annex.cs` (the hourly look); the project file; backups `*.bak-20261020`). In "I have orders for you":
  - **Raise an army.** Pick the lords (nearest first, each with the game's own calling price), then the fief where they gather; the leader waits there. The ruler needs the influence for the calls. The game charges each call to the leader's clan; it is refunded and charged to the ruler. **Call more lords** does the same for a raised army. **Disband your army** ends it; its leader then acts as he sees fit.
  - **Besiege one of their castles or towns** and **Follow me** (Party AI Controls' own orders), and **Raid one of their villages**: not one of its orders, so the raid clears his order there, turns his own decisions off and sends him at the village. When it is looted, an army's leader waits for orders and a lone lord goes back to his own AI. Defend, patrol, wait, recruit and the rest are as before, now with whole armies behind their leaders.
  - **Cohesion:** each hour, below 60, the ruler buys it back up to 100 at the game's own price for that army, or as far as the ruler's influence goes. The leader's clan never pays: the game's own boost (`Army.ThinkAboutCohesionBoost`) is skipped. With the influence gone, cohesion falls and the army breaks up when it reaches 0 (the game's 30 for a lord's army is held back). A warning comes at most every 12 hours.
  - **Break-ups held back** (a prefix on `Army.DisperseInternal`, which every reason passes through; the one-line `DisbandArmyAction.ApplyBy...` wrappers could be compiled into their callers): hunger, no war left, idleness, and the leader's own whim (an "unknown" break-up while the game's hourly decision for a party runs, marked by a prefix and finalizer on `AiPartyThinkBehavior.PartyHourlyAiTick`). Everything else still ends the army: the leader dead or captured, his party gone or disbanding, a marriage into another realm, every lord gone, the ruler's order.
  - Saved with the game (`tfaom_raised_armies`, `tfaom_vassal_raids`). Armies not raised this way are left as the game runs them.
- **Checks:** built clean; 541 game references, 0 missing; every hooked method read in the 1.4.8 code. Not testable outside the game.
- **Installed** at 23:33 (sha256 `87DCBDB120678F5C…`) over the 22:08 build (`AA07F7D3…`, kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-vassalarmies-20261020-2335`). The other session working on TFAOMFixes (crowd AI trims) was told; none of its files changed.
- **Not yet seen in a game:** all of it. The recorder reads Party AI Controls' orders as before; the army raising and the raids are not yet recorded as the player's decisions.

### 23:59 · Version 0.7.6: the test-save command takes Western Moria too, and no longer gets your heroes captured · commit b8e911a (installed 23:58)
- **Why:** the first run of `agents.rebalance apply` (23:54, on a new save). The command should give what it was already giving them plus Western Moria. It also ended up giving a lot of my lords to them as prisoners; why was to be found out, and that fixed as well.
- **Why the heroes were captured:** the game's own rule. When a castle or town changes owner, `PrisonerCaptureCampaignBehavior.HandleSettlementHeroes` (on `OnSettlementOwnerChanged`) takes prisoner every hero staying in it without a party (not the player, a wanderer or a notable) whose realm is at war with the new owner, and sends the lords' parties out. The command moved the parties out but not those heroes (families, lords between parties): 11 were "taken prisoner by" Nanduhirion (9), Caradhras and Sirannon.
- **What:** Western Moria (`town_MM2`) is on the list. Before each handover, those heroes are moved (`TeleportHeroAction.ApplyImmediateTeleportToSettlement`) to their realm's nearest castle or town that is not under siege and not being handed over; a governor first stops governing. The preview counts them.
- **Checks:** the smoke test, the war room's and the dashboard's self-tests passed; 509 game references, 0 missing; the installed DLL has sha256 `1912A0CB25E4C23B…`. The command itself is not testable outside the game. The war room and the dashboard were stopped for the build and restarted, detached, at 23:58:39.

## Thursday, October 22

### 11:26 · Version 0.7.7: the test-save command no longer stalls on the orcs' own battles, and a refusal says so · commit 7c01f8f (installed 11:31)
- **Why:** running the rebalance command did nothing. Both runs that morning (11:19, `agents.rebalance apply` on a save at Summer 20, 1087, 07:00) refused and changed nothing. The orcs were storming Hollin Gate (`castle_MM6`, the camp of `lord_MM8_1`, in the hourly frame `h2192359`), and the command would not run while any battle was fought at one of the places or their villages. It said so, but below a first line that read "REBALANCE, CARRIED OUT" whatever happened, and that first line is all the log keeps.
- **What** (`TestSetup`):
  - a battle at one of the places or their villages that the orcs' side is fighting against the player's realm (a storm, a raid) is broken off before the handover, as peace breaks one off (`MapEvent.DiplomaticallyFinished`: no winner, no loot, no prisoners; the storm closes with the siege, `SiegeEvent.FinalizeSiegeEvent`, and a raid at the game's next look, `MapEvent.Update`). The game does the same in `ChangeOwnerOfSettlementAction` when a place passes to its attackers' side. The preview names each one;
  - it still waits while the player is in a battle there, while a garrison sallies out from one (the player's lords would be moved out mid-fight), and with the player's party inside one;
  - a siege of a place by an enemy of the orcs goes on against the new owner instead of being ended;
  - the first line says what happened: "REBALANCE NOT RUN, nothing was changed: <why>", "REBALANCE, WHAT IT WOULD DO", or "REBALANCE, CARRIED OUT: <n> of <n> places handed over, ..."; the whole answer goes to the log as well.
- **Checks:** the smoke test passed in full (no new checks: the command needs the game); 512 game references, 0 missing (`MapEvent.DiplomaticallyFinished`, `IsPlayerMapEvent`, `IsSallyOut`, `IsBlockadeSallyOut` read in the 1.4.8 code). All four projects and `SubModule.xml` are at 0.7.7.
- **Installed** at 11:31, after the game closed (sha256 `2A46E12C9EC59C29…`); the war room and the dashboard were stopped for the build (their self-tests passed) and started again, detached, at 11:31 (dashboard answering on 7329, the war room waiting after poke #418). Settings unchanged: watch mistymountainorcs, agent live, agent_lords all.
- **Not yet seen in a game:** the run through a storm.

### 11:46 · Outside the repository: an army a lord formed himself keeps together while he carries out your order (TFAOMFixes) · commit dafc8bb
- **Why:** I asked what happens when I order a lord who leads an army. I imagined that the army follows that order or the lord disbands the army to follow that order, and I wanted the former. Read in the code: nothing of ours or of Party AI Controls breaks the army up for an order (Party AI Controls pulls a lord out only from an army he does not lead), and an army raised by the player's order already stays together. But an army the lord formed himself was left to all the game's own rules, as asked on Oct 20, and one of them, idleness, undoes most orders. `Army.CheckInactivity` counts an hour of idleness for holding still, travelling to a friendly place, or patrolling, and two back for besieging, raiding or defending a place under siege. After 48 it breaks the army up. Waiting in a fief, defending a fief nobody attacks, patrolling and recruiting all look idle. Asked whether to keep such armies together only against that, or to take them over as raised armies, I chose the smaller fix.
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: `VassalArmies.cs`, `VassalOrders.cs`; backups `*.bak-20261022`): while the leader of an army he formed himself carries out an order of the player's (one given in "I have orders for you", a raid included; `VassalOrders.UnderOrder`, which asks Party AI Controls only about lords the player commanded), the break-up for idleness is held back, and so is his own whim in the game's hourly decision (an "unknown" break-up there; Party AI Controls' own guard on `DisbandArmyAction.ApplyByUnknownReason` sits on a one-line wrapper its callers may have compiled in). Each time idleness is held back, the game's count goes back to 0, so when the order ends he has a fresh two days. Cohesion, its cost and its running out stay his; every other reason still ends the army; nothing changes for armies under no order of the player's.
- **Checks:** built clean (sha256 `434511229FDC618B…`), 543 game references, 0 missing; it still holds the other session's CrowdAi, CrushWatch and FastCores (that session agreed to the build). The game was open; it was installed at 14:15, ten seconds after the game closed (sha256 `434511229FDC618B…`; the build before it kept at `C:BannerlordArchiveTFAOMFixes.dll.148-before-ownarmies-20261022-1415`).
- **Not yet seen in a game.**

### 14:39 · Version 0.8.0: a second agent, for Dol Guldur · commit 7d1f22f (installed 14:37)
- **Why:** I wanted to give Dol Guldur an agent as well in the next launch, to see how well I can do against a more evenly matched enemy. Dol Guldur (`dolguldur`, ruled by Khamûl, the Easterling) holds 21 castles and towns, Caras Galadhon and Cerin Amroth among them, and is at war with Gondor, Rohan and the player's realm. Until now the bridge watched one realm only.
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

### 17:40 · Version 0.9.0: an agent is a folder; no agent is special; one script runs them all · commit 02166f0 (installed 17:38)
- **Why:** I asked how 0.8.0's two agents compare to this aim: adding a new agent should only entail strapping on a new modular component with a new system prompt, with tools reused like function calls and no crossover between brains. The comparison found five gaps:
  - the game-side pieces of an agent were kept in five places, each filtered by realm;
  - the first agent was special;
  - the mode and the obeying lords were shared by all agents;
  - setup overwrote the prompts;
  - adding one took a setup, a war room started by hand, and a naming convention for its logs.
  My decision: implement all of the change recommendations.
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

### 17:49 · The agents' goal: no region, the whole map · commit 49afa72
- **Why:** reviewing both agents' system prompts: the goal should not be to win the war for a specific region. It should be to systematically eradicate all enemies and assert its dominance over the game map with its allies.
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

### 18:23 · Version 0.9.1: every save gets exactly its own history back · commit dbece13 (installed 18:22)
- **Why:** I asked how loading different saves keeps histories apart. The cut-back went by date alone and deleted what it cut. So loading a later save from a run played before another one gave the agents the other run's history, and nothing of its own after the branch point. An important fix, to be built now.
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

### 19:39 · Outside the repository: recruiting and garrison training (TFAOMFixes, Improved Garrisons' options) · commit e356783
- **Why:** I asked how recruiting and training work for my garrisons and for the computer's lords. The walk-through found three things, and I asked for all of them. 1: this fix surely must be made. 2: NPC world recruitment turned on. Then: notables should offer each AI lord only that lord's own culture's troops, without us having to convert them after recruitment, and my clan's parties only troops from my troop tree; and a weak party on a recruit order should go to every single settlement, one by one, recruiting every single troop there, irrespective of their template, until they reach 80% full party size. Then, if they do not get another order, they slowly replace off-template troops with on-template troops.
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: new `GarrisonTraining.cs`, `RecruitOrders.cs`; `HostRecruits.cs` (the `Offers` class and the doors); a line each in `SubModule.cs`, `HookCensus.cs` and the project file, backup `TFAOMFixes148.csproj.bak-20261022-recruit`):
  - **Garrison training keeps its experience.** Improved Garrisons gives each kind of troop 20 experience a man a night, but only on a night that promotes at least one man, so a stack needed about cost / 20 men (65 for level 26, 202 for level 51). Its own log showed it: Hithaegrist's last 100 entries were 123 promotions, none past level 21. Now a stack it would have trained but left untouched gets its 20 a man kept, by its own rules (template, top tier, room left on the template's lines). Its saved options are only read. In the player's garrisons trained along a template, the game's own daily promoter (random branch) is held back.
  - **Notables show every buyer his own troops.** While a buyer looks at or buys from a place, its notables' volunteers are shown as his own: the Host for my clan and raised companions; for every other lord, his culture's troop of the same role and nearest level. Every slot he did not take is put back afterwards. This covers the game's purchase round for lords, the AI's count when it weighs a visit (Party AI Controls' template count rides on it), the game's garrison auto-recruit, and Improved Garrisons' daily recruiting (for the holding's owner). The same slot always shows the same troop to the same buyer, and my recruit screen uses the same rule. The old swaps after a purchase stay as a safety net, with counters (`tfaomfix.my_recruits status`).
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

### 19:48 · Outside the repository: computer garrisons up to 250 / 400, and a one-day sweep memory (TFAOMFixes) · commit c69b73c
- **Why:** the limit should be raised to 250 men for castles and 400 men for cities, and for the recruit order: instead of the last 3 days, the bar is set to the last day. Slots refill every day.
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

### 20:19 · Version 0.9.2: an agent's recruit order tours, as my own parties do · commit e56ba49 (installed 20:18)
- **Why:** any lord with a recruit order given by an agent controlling a faction should also follow the same logic as my own parties. Until now the recruit order went to one town or village and stayed up to three days or until 98% full. It bought only through the game's hourly round: one volunteer per notable an hour, on a dice roll.
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
- **Why:** I asked how computer lords recruit. The orc side's daily log over 30+ days:
  - lords at about 25% of their size limit, 9 in 10 below half;
  - about 3 recruits a lord a day;
  - 35–51 weak lords a day never spent an hour where they could recruit, while 300–670 volunteers waited in their lands;
  - money was not the brake: the clan tops each lord up to 5,000 a day.

  Proposed, then: "I would make that change."
- **What** (`C:\BannerlordArchive\src-TFAOMFixes148`: new `LordRecruiting.cs`; `RecruitOrders.cs`'s rules shared with it; a line each in `SubModule.cs` and the project file):
  - **Who:** every computer lord. Left out: my clan, raised companions, lords Party AI Controls manages for me, and anyone already steered by someone else (the game's AI off and the plan not ours, as with an agent's order).
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

### 13:17 · Version 0.9.3: recruit tours say why they stop, the chase question before a hunt, and fixes for a realm of mine with an agent · commit 3296705 (installed 13:16)
- **Why:**
  - **The first evening on 0.9.2** (Oct 22, 20:22 to 20:39, about three in-game days): 13 of Dol Guldur's 14 recruit tours ended in their first hour at 3 to 43%, saying "nothing left to recruit within reach", while 1,500 volunteers waited in its lands. The cause is the game's daily wage cap (`ClanVariablesCampaignBehavior.MakeClanFinancialEvaluation`). It caps each war party's daily wages from the gold of its clan's leader: under 30,000, 200 + (gold / 30,000)² × 400; 30,000 to 90,000, 600 to 1,200; above 90,000, no cap; half again for the leader's own party. 21 of Dol Guldur's 47 lords were at their cap: Skûlgar, for one, had 81 men, wages 354 of 359, and 10,555 gold. The agent was not told why, sent the same five lords again, and three stopped again at once. The orcs were hardly touched (3 lords at the cap); their limit is the volunteers themselves (about 440 waiting, 220 within their standing).
  - **Oct 23, on hunt:** it is completely unnecessary for a party or army to hunt an enemy that is faster than it if it has other competing objectives. The only reason to do this is having nothing else to do or trying to drive the enemy away from something. For this to happen, the agent should be able to ask for the speed of an enemy party in sight before giving a hunt order, and should have this as part of the guide for its use of the hunt order.
  - **The snags found planning the lieutenant** (Oct 22): to be fixed in the code.
- **What:**
  - **A tour's end says why** (`RecruitTour.cs`): when volunteers were within reach but none could be paid for, the end names the commonest bar: his wages at their cap (key `wage cap`, with his wages and the cap, and that the cap comes each day from his clan leader's gold), his gold, or his clan's purse (`no gold`). "Nothing left to recruit within reach" now means just that ([TOOLS.md](TOOLS.md#the-recruit-order-tours-since-092)).
  - **The chase question** (war room, new `ChaseTool.cs`, served beside "ask the way", "battle odds" and "roads in"): `can_catch` (`hunter`, `quarry`) gives each side's pace by the game's rules, the way between them, the hours to reach the enemy if it holds still and to run it down if it flees, how much a faster enemy gains an hour, and who gets first to where it is marching ([TOOLS.md](TOOLS.md#the-chase-question-before-a-hunt-since-093)).
  - **hunt's text** in the library: weigh the two paces first; a faster target is reached only when it stops or turns toward the hunter, so set its hunt only when the hunter has nothing better to do, or to keep it from a place or a lord it threatens. In general words, not mine.
  - **The instructions** (`agent/warroom-system.template.md`, "Asking the way"): one sentence naming `can_catch`, so the question is promised only in calls that have it.
  - **A tool for a realm no agent serves** (`Tools/Runner.cs`) no longer runs on the first agent listed (its view, orders in force and call-backs): alarm `tool_no_agent`, and the lord is let go. An order that names no realm (the tests') keeps the first agent.
  - **Every order door refuses the player, and a lord on a Party AI Controls order** (`Orders.Issue`): until now only `agents.vassal` had these two guards.
  - **The player's own party is never one of a realm's lords** in its report or in the tools' list of own lords (`Views.Build`, `Facts.OwnLords`).
  - **The record of my play** keeps my realm's pokes if that realm gets an agent of its own (`Brain.Process`), and the dashboard still marks that realm's frame as mine.
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

### 13:55 · Version 0.9.4: money for the agents (their own wage caps, a purse, a loot-only sell order, a battle's worth) · commit 03fdda4 (installed 13:54)
- **Why:** Oct 23: the caps should be the agent's call. It should be able to estimate the value of loot and prisoner ransom money from a battle and decide accordingly. An understanding of economics should be given to it now. When needed, it should send armies or lords that have battle loot to friendly settlements, to sell it and ransom prisoners. On the plan: agents should be able to set caps freely. The sell order needs care: an order to sell must actually sell loot and just loot, instead of important food and other things.
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

### 14:17 · Version 0.10.0: the player's lieutenant, in the campaign map's chat box · commit 20c386a (installed 14:16)
- **Why:** Oct 22: an agent that acts as the player's lieutenant. The player types orders into that chat to their lieutenant, which then orders lords to do things on the map; I verify and approve the order; I can also ask it what lords are doing across the realm, any threats spotted on fronts I am far away from, and what lords I gave orders to are currently doing. Oct 23: everything stays verbal in the chat box, and my approval is verbal too. Its answers are short and concise whenever possible, and popups pause the game until the popup is closed. On who it commands: it should be able to command them, including my own clan's parties. It stays in the chat window for now; the companion chat is a quality of life upgrade for later.
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
- I wrote "I would like to get a contingent of dwarven lords to defend goblin town, about 2000 men strong." The chat box sent it (14:29:12); the war room answered in 42 s with six dwarf lords, 2,209 men, all at Goblin Town by Autumn 4, 11:00, and a longer write-up; the bridge read the answer at 14:29:55.
- **The gap:** the answer waited until the game's clock ran again, since the bridge handed it to the game's tick (which stops while paused). The write-up came as a window although I had not asked for one.
- I then asked for a window of the lieutenant's own: a button works as a shortcut (say Enter) to go into the lieutenant pop-up automatically and chat with it through that pop-up rather than through the game's own log. This way it is easier to approve, disapprove, or comment on its proposals as well (possibly with buttons for each).

### 14:43 · Version 0.10.1: the lieutenant's own window, with Approve and Decline · commit d7c830e (built 14:43; installed 14:47, next entry)
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

### 14:47 · Version 0.10.1 installed · commit 3b70807
- The game closed, I said to install. The war rooms (three) and the dashboard were stopped for the build and started again at 14:47; the lieutenant's war room waits for the player's words (last answered #1: the Goblin Town proposal of 14:29 was never answered, so nothing was given).
- **Fixed at the install:** the war room's self-test check of the lieutenant's message looked for a line break as `\n`, but the message's lines end in `\r\n` on Windows; the check now looks for the heading and the note on their own, in order. All of its checks passed, as did the smoke test (225) and the dashboard's (27).
- **Installed:** the module's DLL sha256 `5FB015DC4F311217…`, the war room's `WarRoom.dll` `4DB80E7E8E136400…`; the module's `GUI` holds only `Prefabs\LieutenantWindow.xml` (the mirror removed 0.10.0's `SPChatLog.xml`).
- **Not yet seen in a game:** the window.

### 14:51 · Seen in a game: Enter and T opened the game's chat box, not the lieutenant's window (0.10.1)
- I loaded the campaign (14:50) and closed the game (14:51): Enter and T still opened the chat window. Enter should open the lieutenant window, T should open the chat window.
- The key hook had attached ("the lieutenant's key (Enter, T) attached" in the bridge's log) but was never reached: it sat on the chat box's start of typing (`MPChatVM.StartTyping`), whose body is 15 bytes of code, and the runtime always folds bodies under 16 bytes into the code that calls them, so the chat box's key handling ran its own copy and never our hook. (The same trap as Harmony patches on tiny getters, noted before; the offline dry run only shows that a hook attaches, not that it is reached.)

### 14:57 · Version 0.10.2: Enter opens the lieutenant's window, T the game's chat box · commit 4e4f350 (built and installed 14:57)
- **What** ([LIEUTENANT.md](LIEUTENANT.md#using-it-since-0101-its-own-window); `Lieutenant.cs` `ChatHook`, `GameHooks.InstallChat`):
  - the hook moves to the chat box's look at its keys each frame (`GauntletChatLogView.HandleInput`, 813 bytes, in Native's GauntletUI, found by name): on the campaign map, with a lieutenant and the chat box shut, Enter (read from the map's own keys, as the chat box reads it) opens the lieutenant's window and the chat box does nothing that frame;
  - **T** is left to the game: it opens the game's chat box, as I asked; Enter while the chat box is open stays the chat box's;
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
- After a launch from 14:59 to 15:03: the window worked well, but I should be able to scroll up and down to see the full answers of my lieutenant and the chat's history.
- In that launch I asked for "a contingent of dwarven lords to defend my border with the misty mountain orcs, and a contingent of silvan elven lords to defend my border with dol guldur" (15:02:12); the answer came in 44 s with a proposal of 7 orders (six dwarf lords, 2,209 men, at Goblin Town, and an elven force), not answered before the game closed. Errors: none.
- **The gaps:** the conversation showed only its last 14 lines, cut at the top with no way back; the proposal's list was cut at 140 points, so a 7-order proposal lost its last lines; and the window had no room for a proposal's box at all (its parts added up to more than its height).

### 15:11 · Version 0.10.3: the lieutenant's window scrolls · commit 74c4eef (built and installed 15:11)
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

### 15:34 · Version 0.10.4: the fog check's two false alarms, and the sight check's drop, explained and fixed · commit ce1bebb (built and installed 15:34)
- I asked to look into the problems and figure out why they materialized (the fog check's leaks on Oct 22 and Oct 23, and the sight check's agreement falling from 96–98% to 86–91% on Oct 22).
- **The fog check, Oct 23 (20 flags in 2 reports, 0.10.0):** all 20 were the orcs' own lords, in YOUR PURSE's "Carrying loot" and "Holding prisoners" lines (0.9.4). They were lords riding in the orcs' two armies (12 and 8 parties). The check took the realm's own lords only from the report's own-lords list, which holds only those leading parties. **Not a leak:** a realm knows its own lords.
- **The fog check, Oct 22 (1 flag):** poke #429 was about a raid on the orc village Linhamm, named by the game's own notice ("Linhamm is being raided by Cadda Tordaging"). The raid began at 21:53. By the hour's look 20 minutes later the raider was off the village and out of the orcs' sight, so the poke's fact line named a lord the orcs had never seen ("last seen 9999 hours ago"). **Not a leak either:** the game's news named him to the realm.
- **The sight check's drop:** the game refreshes each party's "seen" flag every frame, but only within its largest sight range, plus 5, of the player's party, and never for garrisons and militias (`CampaignTickCacheDataStore.UpdateVisibilitiesAroundMainParty`, read in the game's code). Beyond that a flag stays as it was. On Oct 22 Naglur's orc army (12 parties), seen days before, was still flagged seen 210–247 away (my sight is 47.7) in all 42 checks. Those flags made 504 of 504 disagreements in one launch and 360 of 364 in the other. Oct 16's main disagreements were garrisons and militia. **Our sight rule was right:** with those taken out, every launch from Oct 8 to Oct 23 agrees on 99.9–100% (recomputed from the logged disagreements).
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

### 16:04 · Version 0.11.0: the test runs' record, scoreboard, setups and watcher · commit 8b661eb (built and installed 16:04)
- My test plan (Oct 23, [RUNS.md](RUNS.md)): all tests run for 60 in-game days, starting from the TEST START save. **A:** me against the orcs' and Dol Guldur's agents (first). **B:** me against the game's own AI (if I can). **C1, C2:** control of my kingdom goes first to an agent, to play against other agents and against the game's raw AI, to see how its performance compares to mine.
- My choices for the agent-ruler runs: switch the player character to a wanderer in a random town, give that character a lot of money and food so it does not die, and simply let the game run. The lieutenant stays on when I play. The rivals' agents stay on, watching, against the game's AI so long as usage is fine. Token usage stats are to be tracked as well.
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
- I loaded TEST START, let the clock run, and the run began at 16:13 as `20261023-1613-A` (A, me against the agents); I saved TEST A at 16:19 and closed the game (4 in-game hours played). The run's record, day 0 line, the agents' first answers (orcs 43 orders, Dol Guldur 11, all carried out) and the lieutenant's first answer are in `runs\20261023-1613-A`.
- Found on the way: the start-up check showed the kingdom's own agent, switched off for run A, as a red problem; cheat mode is on in my game (noted against the run); a save made before the clock first ran would not have counted as the run's; the run's first folder (`20261023-1613`, before it was named) stayed behind; the lieutenant's usage read $0.00 (its calls' log had no price); Dol Guldur is at peace with my kingdom at the start of TEST START (only the Misty Mountain Orcs are at war with me).
- I asked to fix the lieutenant and Party AI Controls before going deeper into test A.

### 16:55 · Version 0.11.1: the lieutenant works with Party AI Controls, and its conversation is the save's · commit 24b7c29 (built and installed 16:54)
- **Oct 23:** the lieutenant should be able to work in tandem with Party AI Controls. Any order given by the lieutenant should override any order given by Party AI Controls, and it should be able to change backup orders, troop templates, and all options as well. All changes made by the lieutenant and all of its orders should also be reflected in the dashboards of Party AI Controls. And: the chat I have with my lieutenant should be persistent within a save, even if each prompt is technically a different session. This is something interacting with a human, and I want to be able to reference previous things that were said in the chat throughout a save. The lieutenant should also have access to the chat I have with it, so I can refer back to it. This is something specific to the lieutenant, so it should not touch the other agents.
- **The lieutenant and Party AI Controls** (new `PacLink.cs`, docs/LIEUTENANT.md "Party AI Controls"):
  - its orders are still carried out by our order layer, as every agent's (the comparison runs need that), and each shows in Party AI Controls' panel as that lord's order: the nearest of its kinds (`PacRules.MirrorOf`), or none for a raid, a shadow, a watch post on open ground, armies. It replaces his Party AI Controls order (0.10 refused such a lord); his queue and backup order stay, and when it ends the next queued order starts there;
  - while it runs, Party AI Controls' hourly look (`KeepHeldPatch`, now a prefix that can skip) and its gate check (new `PacEntryPatch` on `PartyAIThinker.OnSettlementEntered`) pass him by, so the two never pull against each other;
  - the ruler wins: a new Party AI Controls order for him meanwhile (the panel, or "I have orders for you") ends the lieutenant's order. This closes the 0.11.0 gap I was told about;
  - `party_ai`, in the lieutenant's library only: his Party AI Controls order (carried out by Party AI Controls), queue, backup order, template or a new one, troop mix, every per-lord option of the panel, or handing a vassal back;
  - a vassal it commands is put on TFAOMFixes' commanded list (by name; TFAOMFixes is not changed) as the ruler's own talk does, with auto-recruit and dismissal off on a new entry, and taken off again when nothing of Party AI Controls' is set for him;
  - mirrors, and entries and list places made only for them, come out before every save and go back after; what `party_ai` sets stays (Party AI Controls' own state, on purpose), and is recorded in `data\<campaign>\lieutenant\party_ai.jsonl`;
  - each message carries PARTY AI CONTROLS NOW (read when I send words, not hourly); the recorder no longer takes the lieutenant's Party AI Controls orders for mine, and a run's battles say "lieutenant (Party AI Controls)".
  - Checked against the installed Party AI Controls 1.4.5, decompiled today: the old decompile at refsrc\PAI is an older version (no `RecruitFromEnemySettlements`, no `SetPartyTemplate`, `DismissUnwantedTroops` default true there). CLAUDE.md's `Settings(Hero)` rule now names PacLink as the one exception.
- **The conversation of a save** (`Lieutenant.Keep`, `Restore`, `History.HoldsLine`): every line goes into `data\<campaign>\lieutenant\chat.jsonl`, labelled with the session, its in-game hour and the wall clock; a load keeps its save's lines (the chronicle's rule; saves' notes now carry the wall clock, since most of the conversation happens paused), shows them in the window and puts back a proposal still waiting. `chat.json` carries the latest 60 lines, `conversation.txt` the whole; the war room uses them instead of its own memory, which had mixed saves (the first two chats of Oct 23, from another save at a later date, came into TEST START's).
- **The test runs:** a save made before the clock first runs now counts, and loading it readies the run again; the readied folder goes once the run is named; the watcher says "N of 60 days done" (it said "day 0"); the player's own realm's reports (kept, never sent to the lieutenant) are no longer counted as pokes (`sent` in the trace).
- **Smaller:** an agent switched off and set aside for the one switched on is logged, no longer a red self-check problem; the "at war with you" check leaves out your own realm and says "at peace with you now"; the lieutenant's calls log their price and cache writes.
- **Outside the repository:** the lieutenant's instructions `C:\Users\karab\Projects\lieutenant\warroom-system.md` replaced by the new template (Party AI Controls section; the save's conversation and `conversation.txt`; a lord with a Party AI Controls order takes its order once agreed), backup `warroom-system.md.bak-20261023-before-0.11.1`. No strategy in it.
- **Checks:** smoke test passed in full (240 checks; 8 new: the mirrors, party versus holding targets, the options and the troop mix in Party AI Controls' terms, the library section, a save's conversation brought back exactly, this session's and unlabelled lines, notes without a wall clock); war room self-test passed (1 new: the save's conversation and Party AI Controls in the message); dashboard self-test 27 of 27; 642 game references, 0 missing; every Party AI Controls and TFAOMFixes name used found in the installed DLLs. Installed DLL sha256 `941CA2D3B2D974E5…`, `WarRoom.dll` `E9A1884F5B7BE1F6…`. War rooms and dashboard stopped for the build, started again 16:55.
- **Not yet seen in a game:** all of 0.11.1. Run A is to start again from TEST START (the old run, 4 in-game hours, ends unfinished when TEST START is loaded).

### 17:08 · Arm C1 set for the first playerless run
- Oct 23: I was going to the driving range for a bit, so I wanted to start one of the playerless test saves as well. C1 is the plan's next arm (docs/RUNS.md: an agent rules my kingdom against the orcs' and Dol Guldur's agents).
- **Outside the repository** (`scripts\arm.ps1 -Arm C1`, logged in `runs\arms.log`): the kingdom's agent `C:\Users\karab\Projects\kingdom-war-council` off -> live, the lieutenant live -> off; the orcs and Dol Guldur stay live. Read at the next load.
- The old run A (`20261023-1613-A`, 4 in-game hours) ends unfinished when TEST START is loaded; A is to be played again later.

### 17:59 · Version 0.11.2: the playerless switch as a command · commit 53d9f20 (built and installed 17:59)
- **Oct 23:** the switch should be done by a written command, including giving them gold and food, with a good wanderer found to switch into and the command prepared. Then: no need to guard against those pop-ups for now. Just the switch command and a report back; if something does end up blocking us, we respond after.
- **`agents.run playerless [go] [name]`** (new `Playerless.cs`): without `go` it shows the wanderer it would take (a free wanderer, no clan, nobody's companion, no party, in a town at peace with the kingdom, not besieged and not the kingdom's nor a rival's; the town farthest from every castle, town and lord at war with the kingdom or with the town's owner) and the next four; `go` pauses the game, releases every kingdom lord holding a Party AI Controls order (`PacLink.Release`: order, queue and backup order cleared, off TFAOMFixes' commanded list), takes the wanderer into the player's clan as a companion (`AddCompanionAction`), switches to him with the character-switch mod's own `SwitchActions.SwitchPlayer` (by name), lets the old character's party go to the game's AI (that switch holds it with the AI off and a Party AI Controls stay order), gives him 100,000 denars and 100 grain, and takes him into the town. Kept in run.json's `set_up`.
- **Why the wanderer joins the clan first:** the switch mod only switches within the player's clan (its button is on the clan screen), and the game cannot make a party for a hero of no clan (`LordPartyComponent.InitializeLordPartyProperties` reads his clan's party template; read in the 1.4.8 source).
- **Found, not guarded (my call):** the game's player clan is fixed (`Clan.PlayerClan` is the clan `player_faction`, `Campaign.PlayerDefaultFaction`), so after the switch the kingdom's ruling clan is still the player's: its decisions wait for the player once due (`KingdomDecision.NeedsPlayerResolution`) and the map opens the kingdom screen for them. The ruler's choice itself goes to the AI (`IsPlayerChooser` reads `Leader.IsHumanPlayerCharacter`). The switch mod's "Party in combat! Switch?" pop-up (`NotifyWhenAttacked`, on by default) fires for battles of the player character's own clan, which the wanderer now is in. Party AI Controls may still give the clan's parties orders by itself (auto-recruit, backup orders).
- **Checks:** smoke test, war room and dashboard self-tests passed; 652 game references, 0 missing; `PlayerSelectionSwitch.SwitchActions.SwitchPlayer(Hero)` is public static in the installed 1.0.3 (decompiled). Installed DLL sha256 `FBBD66715218D62A…`. War rooms and dashboard stopped for the build, started again 17:59.
- **Not yet seen in a game:** the command.

### 18:05–19:26 · Run C1 played (0.11.2), unattended
- Oct 23: I was going to the driving range for a bit, so I wanted to start one of the playerless test saves as well. I loaded TEST START and typed `agents.run playerless` (it chose Marzuk ibn-Zayak in Damudûr, Âbanissa, 451 from the nearest war), then `agents.run playerless go` (4 lords released from Party AI Controls orders, the switch, 100,000 denars and 100 grain, into the town), and `agents.run check` (C1).
- Run `20261023-1802-C1` began 18:05:12, finished 19:25:43 (60 days, 1 h 20 min at about 80 s an in-game day, no stall or crash). The kingdom's agent ended +1 town, +3 castles, +12 villages, 154 enemy lords taken; the orcs −2 castles, −7 villages; Dol Guldur −1 town, −1 castle, −5 villages. About $276 at list price (orcs $106.69, Dol Guldur $84.79, the kingdom $84.97), no usage limit met. Report: `runs\20261023-1802-C1\report.md`.
- I asked for a dive into the report and everything flagged, and all necessary fixes. Four investigations (the frozen lords; stuck and fought orders with the slowness alarms; the battle counts and the fog check; gold, the failed story, the first timeout and the votes) found what 0.11.3 fixes.

### 20:25 · Version 0.11.3: what run C1 showed, fixed · commit 5c31b20 (built and installed 20:25)
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
  - The 27 of 114 C1 prisoner hand-overs paid 0 (4,051 men) are the game's own rule, not a fault. An enemy lord handed to one's own realm's holding goes to its dungeon as a gift that earns the realm influence, and the game then pays for none of that hand-over, its soldiers included (`SellPrisonersAction`). Oct 23: lords can sometimes place prisoners in the dungeon instead of selling them. That is probably what happened.
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
- **Not changed (still to decide):**
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
- Oct 23: the other non-player run next. C2 (docs/RUNS.md): an agent rules my kingdom against the game's own AI, the orcs' and Dol Guldur's agents watching (they answer, give no order).
- **Outside the repository** (`scripts\arm.ps1 -Arm C2`, logged in `runs\arms.log`): the orcs `C:\Users\karab\Projects\orc-war-council` and Dol Guldur `C:\Users\karab\Projects\dolguldur-war-council` live -> shadow; the kingdom's agent stays live, the lieutenant off. Read at the next load; the war rooms (started 20:25 on 0.11.3) keep running.
- `scripts\arm.ps1`: its in-game steps for C1 and C2 now name `agents.run playerless` and `agents.run playerless go` (they still said to switch by hand with the character-switch mod and give gold and food), and the check names the arm set.

### 20:55–21:45 · Run C2 played (0.11.3), stopped at day 31
- Oct 23: the other non-player run next. I first loaded `TEST C1`, a save of C1's from its setting up, which readies no run (C1 being finished); no run began, and the watch said so. I loaded `TEST START`, typed `agents.run playerless go` (Marzuk ibn-Zayak in Damudûr again) and let the clock run.
- Run `20261023-2101-C2` began 21:01:27 and was left at day 31 (Winter 9, 1087) when the game closed at 21:45. The kingdom's agent ended +1 town (Nanduhirion), −1 castle, +2 villages, 74 enemy lords taken against 76 of its own; Dol Guldur +2 castles; the orcs −1 town, −2 castles, −9 villages. About $106 at list price (the kingdom's agent $39.59, Dol Guldur's $34.41, the orcs' $32.13). Report: `runs\20261023-2101-C2\report.md`.
- **Day 9-11, Imnagath:** Aragorn's and Imák's armies sheltered in the castle on the agent's orders from Lorgath's army (20 lords, 10,302 men, strength 22.6k). The game sent everyone inside (5,938 men, 20 lords) out the hour the siege began (`SallyOutsCampaignBehavior.CheckSallyOut`); 46 hours later all were lost (Lorgath lost 4,103) and the castle fell. Read in the 1.4.8 source, the installed game, TAOM's and Improved Garrisons' code: the game's own rule, which no mod touches; our orders cannot order a sally. Why the game counted so little of Lorgath (it needs the inside above twice the enemies within 3 of the camp) is not known.
- Days 21 and 24: Alenon's army (8 lords) and Val's (10 lords) were each destroyed in the open, by Lorgath and by Gundabad's Mauhur. Day 23: Stein destroyed Naglur's orc army (1,677 of 1,686 men).
- A read of C1 and C2 for my question, why such large armies never came about in our agents' runs (a helper agent, read only):
  - The agents spread their lords over five to eight armies sized to what is in front of each (C2 day 9: 6 armies, 16,478 men, about 2,750 each, against Lorgath's one of 10,302).
  - The news of the big enemy army is buried between answers (all three Lorgath pokes went unanswered while the agent answered raiders).
  - In C1 the rival agents held nearly all their lords, which the game's own army-building passes over.
  - The call-up order refuses when the clan cannot pay every lord at once; the game needs 100 influence and lets the clan go into debt.
  - Not changed.
- This test has to be run again some other time, from the start. What killed it was one bug that forced our troops out of a castle they should have defended. The focus goes to fixing that bug, and then trying again. In the ideal world, our agents should easily beat the raw game's strategy.

### 21:56 · Version 0.11.4: the game's sally kept from our sheltering lords · commit 268c6c8 (built and installed 21:56)
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
- **Oct 23:** the agent's system prompt should also be designed to have it be more focused in its strategy. It is better to mass an overwhelming force in one front to destroy enemies one by one, whilst moving smaller but still sizeable and adequate forces defensively to other fronts to keep the stalemate there, than looking at each front equally.
- Written as a general principle in the doctrine's own words, the same for every realm agent, not as my sentence and with nothing of this war's fronts, armies or places (my rule: the agent is not given the strategy I stated, word for word). The design and its grounding: docs/DOCTRINE.md, "Principle 5 rewritten (Oct 23)".
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
- **Oct 24:** I did not like the wording on principle 5. It should be less strong, telling the agent to strategize such that, if it can win in a front with overwhelming force, it should concentrate overwhelming force there instead of just an adequate one, without taking away so much from the other fronts as to make their forces inadequate. This might work for multiple fronts, just one, or none, based on the relative strength of its enemies.
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
- **Oct 24:** it looked like the only pop-ups that jeopardize the test are the notifications saying "x is besieged, will you answer the call for aid?". They were to be disabled, so we can run the test smoothly.
- **What they are:** TAOM's own siege-defence events (`TAOM.Features.Siege.SiegeDefenseService`, read from TAOM.dll). When a town of the player's realm is besieged, it shows an inquiry ("Gondor Calls For Aid!", from `ModuleData\siege\siege_defense_config.json`) with "For Gondor!" and "Ignore"; answering and reaching the town in 3 days gives the player +10 influence and +5 relation with the realm's ruler. Towns only, the player's realm only; nothing in it moves a lord or an army, or changes what any AI does.
- **The switch:** the pack's own Mod Options setting "Enable Siege Defense Events" (TAOM, group "Siege Defense", `EnableSiegeDefenseEvents`, on by default). The pack's settings had never been saved, so the file was written new with only that setting: `Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\TAOM\TAOM.json`, `{"EnableSiegeDefenseEvents": false}`. Every other setting of the pack keeps its default. Written with the game closed.
- It is global: off for every campaign and every arm until switched back on in Mod Options. I am to confirm the box is unticked in Mod Options before loading TEST START.
- **For comparisons:** C1 and the stopped C2 were played with the pop-ups on; every run from the C2 replay on has them off.

### 15:30–16:52 · Run C2 replayed (0.11.4), finished
- `20261024-1530-C2`: the kingdom's agent live against the game's AI, the orcs' and Dol Guldur's agents watching; the softened principle 5 and the pack's siege pop-ups off. War rooms and dashboard restarted 14:29 before it (`scripts\agents.ps1 restart`).
- 60 days in 1 h 20 min; about $236 at list price; no errors, freezes, fog leaks or failed calls; one mark (cheat mode during setting up, expected).
- The kingdom ended +1 town, ±0 castles, +4 villages, with 180 enemy lords taken; Dol Guldur's great army was destroyed at Lasgalor on day 40, at 17,565 of our men lost. Findings in docs/RUNS.md, "Run C2 replayed (Oct 24)".

### 17:26 · Version 0.11.5: the council of war sits on time in a busy realm · commit 5825ca2 (built and installed 17:26)
- **Oct 24:** I asked for a diagnosis of why the agent was not as aggressive in pressing its advantage after its large victory at Lasgalor against Dol Guldur. Then: build and install the fix.
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
- **Oct 24:** the game paused itself when I switched to a different tab. That was to be disabled, so I can do other things as a test runs.
- **The switch:** the game's own option "Stop game on focus lost" (Options, gameplay), saved as `StopGameOnFocusLost` in `Documents\Mount and Blade II Bannerlord\Configs\BannerlordConfig.txt` (the Documents folder is `OneDrive\Belgeler`). Set from `True` to `False`; that one line changed, nothing else in the file. Written with the game closed (it had last saved the file at 21:05, on exit).
- Nothing of ours, the pack's or the agents' changed; the setting is global, for every campaign and arm, until switched back in Options.
- **For comparisons:** only when the clock runs changes, not what happens in a game day; earlier runs stopped whenever another window was in front. docs/RUNS.md notes it beside the pop-ups.

### 21:10–22:31 · Run C2 played again (0.11.5), finished
- `20261024-2110-C2`: the kingdom's agent live against the game's AI, the orcs' and Dol Guldur's agents watching; the first run with the council fix, the same instructions as the replay, the siege pop-ups off and the game running behind other windows. Watched every 10 minutes (`Runs.exe watch`); report and side-by-side comparison written at the end (`Runs.exe report`, `Runs.exe compare`).
- 60 days in 1 h 20 min, about $307 at list price; no errors, freezes or failed calls. The kingdom's councils sat 45 times (the replay: 20).
- Ending: the kingdom +1 town, −1 castle, +4 villages, 183 enemy lords taken against 52, battles between lords 109-34. The worst loss of all the runs on day 42 (Gárjan's army caught short of Bagbûrz, the sheltering lords sent into the running battle, 7,915 men and 19 lords lost); Dol Guldur's great army destroyed on day 51 in return. docs/RUNS.md has the record and what to look into (battle_odds on a battle in progress, a siege of Nanduhirion that never began, lords sent out of a besieged holding's walls).

## Sunday, October 25

### 13:05 · Why the kingdom's agent never went for Dol Guldur's great army (a read of run 20261024-2110-C2)
- **Oct 25:** I was seeing a general pattern where the agent fails to prioritize engaging an enemy's main force and pays the price. I asked for the logs to be read to find out why it failed to prioritize engaging Dol Guldur's main force, and which parts of the prompt were in play when it was making those incorrect decisions.
- **Read from** the kingdom war room's 267 calls of the run (`logs\warroom-new_kingdom\calls\20261024`, 2110 on): each council's plan, orders, lessons and enemy notes; every `battle_odds` and `can_catch` question with its answer; every poke's reason and orders; the chronicle and the run's days. The calls' thinking comes back empty in the record, so this is what the agent wrote, not what it weighed.
- **What it shows:**
  - From day 5 to day 39, 18 field `battle_odds` against Lorgath's army (16,600 to 20,100 strength), every one a win keeping 75 to 91% of the agent's men; on day 5 `can_catch` said Aragorn's army was 88% faster than Lorgath's (reached in about 33 hours, 71 if it ran). The day-5 plan said "It is slow (speed 1.2), so we can mass and catch it", then "best while he is tied to a siege"; day 7, 25k against 20k, "only narrowly and over about 31 hours, so we strike his siege camp only if the edge holds or grows".
  - No order to attack him until day 39. Meanwhile he took Imnagath (day 11), Framsburg (day 19), Imnagath again (day 25) and Dûglarshun (day 36); the plans let each go ("cannot be relieved in time").
  - 16 of the 46 council plans held "strike him only while he is tied to a siege of one of our places". His sieges ran half a day to five days; the agent's armies stood four or five days off, hunting raiders at Pelan Erui, Eithel Glin and Galadbain. Its own day-1 lesson ("Dol Guldur's lords (speed 3.1) outpace our big armies: we only catch them when they stop to besiege or raid") was about the small raiders and carried over to the slow army.
  - Of 221 pokes, 143 were about raiders or lone enemy lords and 19 named Lorgath; of 386 orders in poke answers, 114 sent lords after raiders and 8 went at Lorgath, all on day 39 or after (6 of them fed lords into the battle already lost at Bagbûrz). The notes' enemy list ended at 60 lines, nearly all small raiders. At day 32 the realm had 78 lords in the field, strength 50,039 (24 holding orders), against his 17,900; Górli's and Ask's 8,600 were on a siege of Nanduhirion that never began.
  - The plans expected his army to break up (8.8 days on day 25, 10 on day 32, by day 44); it stayed whole from day 3 to about day 50.
  - Bagbûrz: a poke answer gave Gárjan `garrison` (stay inside), and 33 s later another swapped it for `defend` radius 8, `shelter_at` 1, `stay_inside` 1, caution 0, "inside Bagbûrz 23h before Lorgath". defend patrols outside the walls and went inside only when the host came within 8 (chronicle, Winter 17, 08:00); he was caught 5 from the gate at 12:00. Caution 0 had switched the wider danger rule off (`Runner.Safety`), as it is meant to.
- **The instructions' lines in play:** principles 2 and 3 ("tied to a siege"); "the edge" with no measure; the persona's du Guesclin line ("let an invader burn itself out, destroy its detachments ... without the battle") with principles 4, 6, 7, 11, 12 and 15 all aimed at the enemy's parties; principle 17's "when time is on your side, wait"; council step 3, every front's share before the blow. One line pointed at the main army ("break the enemy's field armies before you take its castles"). The replay (same instructions) had "tied to a siege" in 2 of 21 plans: the instructions allow it, and an enemy army that stays whole brings it out.
- Nothing changed by the read itself. I took the fixes offered and added two of my own (the next two entries).

### 13:38 · The agents' instructions: the enemy's main army first, raiders to small forces · commit 8701c34 (the template)
- **Oct 25:** I accepted the fix propositions, with more on the main army, something like this: prioritize efforts properly: on seeing an enemy's main force it can beat, the agent should plan on assembling an overwhelming force and meeting it on the field, or as quickly as possible so long as it has the advantage, and crushing it. Then it can be aggressive and quickly take over the enemy's territory as the enemy aims to regroup. Also some notes on the raiding parties: armies should not be spending time chasing raiding parties and should be focused on their primary objectives. Raiding parties should only be answered by smaller groups of lords that can crush them; the focus of large armies should not be diverted.
- A general principle in the doctrine's own words, the same for every realm agent, with nothing of this war in it. The design, its grounding and caveats: docs/DOCTRINE.md, "Principle 18 and the raiders (Oct 25)".
- **What changed** (`agent/warroom-system.template.md`):
  - **Principle 18, new, "The enemy's main army first":** when you find an enemy's main army and can beat it, its destruction is the first aim; gather an overwhelming force and meet it in the field, or strike as soon as you hold the advantage if gathering all would take too long, rather than wait for it to sit down before a castle; lesser prizes wait, raiders go to small forces; then take his places quickly while he regroups (principle 10); when you cannot yet beat it, principles 4 and 17. Saladin 1187 and Mohi 1241.
  - **Principle 7,** now "Raiders: small forces, and on their way home": raiding parties are met by small groups of lords that can crush them, never by the armies, which stay on their tasks and turn aside only for an enemy force worth more than the task (*On Skirmishing* as the frontier districts' manual).
  - **Principle 4:** parties picked off "with small forces".
  - **Principle 3:** the battle odds are the measure of the edge (a win keeping most of your men while most of his fall); judge a battle by what each side has left, not by how long it runs.
  - **Principle 17:** time is on your side only while waiting costs him more than you; an enemy taking your places while you wait has time on his side, however strong you are; the stronger side reads principle 18.
  - **Who you are:** the du Guesclin line now begins "When you cannot yet beat an invader in the field", with "after Crécy and Poitiers, the French could not yet win it".
  - **The council of war:** step 2 begins with where each enemy's main army is and whether it can be beaten; step 3's lesser tasks name "small groups of lords against raiders". Step 3's order (fronts first, then the blows) and principle 5 are mine of Oct 24 and unchanged.
- **Outside the repository:**
  - Each realm agent's `warroom-system.md` (orc, Dol Guldur and kingdom war councils) rebuilt from the new template, each keeping its own name and opening line, otherwise word for word the template (checked by diff: 2 lines differ, the name and the opening). The kingdom's is sha256 `3A3AE3C53DECF372…`.
  - Backups are `warroom-system.md.bak-20261025-before-main-army`.
  - In force from their next answer, no restart. The lieutenant's instructions have no doctrine and are unchanged.
- **For comparisons:** C1 and all three C2 runs were played without principle 18; the runs still to be played are on it. A run's setup records each agent's instructions' fingerprint, so it shows.

### 13:39 · Version 0.11.6: defend goes inside in time; every battle forecast names what the loser has left · commit 8701c34 (installed 13:39)
- **Oct 25:** I accepted the two tool fixes offered after the read above: the guard order's trap at Bagbûrz, and battle forecasts that say how much of the enemy's army falls.
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
- `20261025-1417-C2`: the kingdom's agent live against the game's AI, the orcs' and Dol Guldur's agents watching; the first run with principle 18. I closed the game on day 16 after the lords kept failing to follow their orders.
- **Why:** the bridge's background helper fell further behind the game all run: 31 s at 14:25, 83 s at 14:38, 142 s at the end. Every order, shelter and poke landed late. Beleg's four attack orders on Naglur each waited about 49 s to start, and each new copy replaced the one waiting. Thalion's shelter in Lasgalor was decided 14 game hours before Thrurg came and landed about a day later. Aragorn's landed half an hour after Thrurg caught him.
- Record and what principle 18 showed: docs/RUNS.md, "Run C2 on 0.11.6 (Oct 25)". To be played again from TEST START on 0.12.0.

### 14:45–15:05 · Why the helper fell behind (a read of the stopped run)
- **Oct 25:** I asked why Beleg did not end up engaging Naglur even though he was ordered to, then why this issue, lords failing to follow orders, kept happening. The helper must be on time, every time; the issue was to be diagnosed, with a fix plan to come back.
- **Found** (the session's trace, the freeze probe on the helper's thread, the run's perf log, a scratch benchmark):
  - The game gave an in-game hour every 3.3 s in both runs. The helper, when backed up, needed a median 2.85 s per hour last night (behind 26% of hours, worst 41 s) and 3.47 s today (behind 86%, worst 142 s).
  - All 10 probe samples were in file calls inside `Channel.WriteLookups`. 216 to 339 look-up files per agent were rewritten for each poke, each written beside the old one and swapped in: 1,179 ms for 216 files in the benchmark, against 75 ms written plainly. A swap refused while the virus scanner held the new file slept 15 ms, up to four times.
  - Every hour, for every agent: the realm's view built twice, the situation report and the council briefing rewritten (only a council reads them), the dashboard's map picture (about 400 KB compressed).
  - New orders' starts went into the same first-come queue (`Orders.StartTool` posted `Runner.Start` behind every hourly job).
  - The queue reached 2,051 of its 4,096 jobs; past that, work is dropped. The `helper_backlog` alarm (174 times) was not serious, so the 10-minute watch never showed it.
- On the plan: agreed, implement all fixes (kept on the efficiency cores, urgent work out of efficiency mode; no virus-scanner exclusion).

### 15:16 · Version 0.12.0: the helper on time · commit 9cb88d0 (installed 15:16)
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
- **Stopped** at my ask (I preferred to start a new test with the fix): my kingdom's sieges never began. Górli stood 4.3 from Caradhras' gate (6.8 from its centre) for 15 days and Oropherion's army 4.7 (7.8) for 14, each in besiege's first step waiting for "within 6" of the centre; Val stood 6.2 off Sirannon. I saw it on the dashboard and asked why Oropherion and Górli were simply outside Caradhras but not besieging it.
- Also found: Party AI Controls' own refill sent Aragorn to fill his ranks after the battle with Naglur (day 14) and held him there to the end; the bridge refused the agent's 3 orders to him meanwhile, and the recorder logged the 7 refill steps as "You ordered Aragorn ...".
- Where it stood (docs/RUNS.md): the kingdom −1 castle (Imnagath, to Thrurg, day 20), 46 enemy lords taken, Naglur's army destroyed (day 14), $98.68 list price for the three agents.

### 16:03 · Version 0.12.1: arriving at a castle, and no refill behind the agent's back · commit c3956f3 (installed 16:03)
- **Arrivals measured to the gate** (`World.Near`, the `within` function): to a holding, the nearer of its centre and its gate. The gate lies a median 2.9 from a castle's centre and 3.3 from a town's (up to 7.1; villages have none), read from a saved hour of the run. Before, a siege began only when the game's waiting point fell on the castle's side of its gate (likely also Nanduhirion's siege that "never began" in run 20261024-2110-C2), a watch at a gate over 3 from the centre (105 of 221 castles and towns) never began, and goto's close stop ("within 4") often fell short.
- **`near`** goes to a reachable point 2 to 5 from the gate (was 3 to 6); goto stops close at "within 6" (was 4).
- **A march that stops short is called back** (`Runner.Stalled`): a lord moving to a place (`near`, `goto`, `enter`, `post`, `toward`) who has stood still for 6 in-game hours, more than 6 from it (3 from a spot), out of battle, outside and not besieging, is reported once per stand: "... has not moved for 6 hours while marching on Caradhras, still 9.0 from Caradhras: he may be stuck on the map. The order is kept and he holds where he is; give him another order if he should go elsewhere." It does not end the order (that would hand the lord to the game's AI).
- **The playerless switch** (`agents.run playerless go`, runs C1 and C2) now also switches off Party AI Controls' auto-recruit for every lord it has settings for, and in its clan and kingdom defaults for new parties (`PacLink.AutoRecruitOff`; only existing settings are changed). It is switched off at setup, and off for everyone else as well.
- Versions: `BannerlordAgents.csproj`, `BannerlordAgents.WarRoom.csproj` and `module/SubModule.xml` set to 0.12.1.
- **Checks:**
  - Build with no warnings. Smoke test passed in full (265 checks; 10 new, laid out as Caradhras: besiege arrives 4.3 from the gate and 6.8 from the centre and besieges; goto arrives 4.7 from the gate; a watch at a gate 3.3 from the centre begins; a lord standing still 48 off is told once at 6 hours, not at 5, not again, the order kept, cleared when he moves; ten hours in a battle on the way is no stall).
  - War room self-test passed (67), dashboard self-test passed (27). 688 game references, 0 missing (RefCheck).
  - Installed with `scripts\deploy.ps1` at 16:03, the game closed: `BannerlordAgents.dll` sha256 `DB4C243AEE9A740F…`, `WarRoom.dll` `0EC90B8947814C24…`. War rooms and dashboard stopped for the build and started again 16:04 ("War room 0.12.1.0"); arm C2 as set (kingdom live, the orcs and Dol Guldur shadow, the lieutenant off).
- **Not yet seen in a game:** a siege beginning at a mountain castle, a stall call-back, the auto-recruit switch-off (the playerless switch should say "switched off Party AI Controls' auto-recruit for N lords and for new parties").
- **For comparisons:** in every run before this, sieges, watches and goto stops at castles and towns began only when the waiting point happened to fall near enough the centre, and in C runs the lords Party AI Controls managed with auto-recruit on could be taken from the agent by a refill.

### 16:10–16:31 · Run C2 on 0.12.1, stopped on day 10: does not count
- `20261025-1609-C2`, from TEST START. 0.12.1 seen working: the setup switched off Party AI Controls' auto-recruit (Aragorn, one more lord, its clan and kingdom defaults); every siege went from marching to besieging (Sirannon besieged day 7 and taken day 10, Caradhras besieged from day 7); no stall call-back; no Party AI Controls order on a kingdom lord. The helper on time throughout (0 hours folded over 240 rounds).
- **Stopped** at my word after Alenon's army of 13 lords was destroyed near Torech Emel on day 10. Alenon left the settlement and started fighting the enemy instead. I thought it was something about our defend order's calculation of an enemy host's strength: the enemy was dispersed in a couple of different parties before, and something may have been off again. The issue was to be checked and diagnosed. Then: I stopped the run, and the fix goes into our tooling.
- **Found** (the order's steps and hourly states in the trace, the kingdom's pokes): the guard order (radius 30, `strike_below` 1) struck Narsha (1,751 against Alenon's 8,505), one of a dozen lords marching to join Thrurg (about 17,000 together); the chase ran 33 hours, out of his area into them; the shelter came only when the forces within 30 of the castle reached 1.5 times his strength, 26 from the walls; Bolga and 17 more parties (10,306 men) caught him 5 hours later. The safety rule weighs each enemy party alone and never fired. The old 4-hour give-up never worked (patrol struck the same target again the same hour). The agent's change to radius 20 and `stay_inside` 1 came while he was already in the battle.
- Where it stood: the kingdom +1 castle, +3 villages, 14 of its lords taken against 8; $41.23 list price for the three agents.

### 16:38 · Version 0.12.2: a host split into many parties counts as one · commit 1db15b9 (installed 16:38)
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

### 17:11 · Version 0.12.3: the agent decides who joins a fight, and asks before it engages · commit 7cc733b (installed 17:11)
- **Oct 25:** every lord marching to an army within view should be listed under that army with how far away they are from it. The agent should decide which ones to count as a threat and which ones not to (so if it takes 20 hours for an army to engage an enemy and win, the agent should have all information needed to deduce which lords will make it there within the timeframe of the engagement and decide accordingly). Beyond this, before an engagement, the agent should be able to forecast how long it will take its selected lord or lords to begin and end the engagement, and which enemy lords in view are close enough in terms of marching distance to that engagement to participate in it as it progresses, so it can decide accordingly and set rules accordingly. On how the agent's choice reaches the hourly rules I chose "Hours window only" (over a window with exclusions, named lords only, or a window defaulting to 0).
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
- **Found during it** (my questions and the watch): the first council overturned the first answer's order for Aragorn (sell at Mount Gundabad → Hithaegrist, 75 back); the sell order did not say a castle buys no loot; guards still marching to their area started and broke off 31 strikes; lords on the way to a refuge were caught by parts of the danger that came up later (Haldir, Zlatka, Alvar, Elrohir); one false fog alarm. And, from me at the end: Imak was going back and forth because he was scared of Khamûl's force, even though our lords' combined force was enough to take him. The question: why the strength calculator was not taking into account the strength of the other lords around him. Then: make the fix. The test is done. The final report next, and once all is done, be ready for the control test: all realms playing with the game's AI.

### 20:09 · Version 0.12.4: our side counts too, and the control arm · commit 7519847 (installed 20:09)
- **Our side of a fight** (`World.Friends`, `SideStrength`, new function `side_strength(x)`): the lord, every lord of the realm or an ally camped in the siege he is in or making for, and, within the order's `join_hours`, those marching to join him or bound for the same place or target (besieging or defending it, or holding an agent's order aimed at it). Lords merely near him do not count: a lord on one of the agent's orders joins no fight by himself. Used by caution, besiege's relief alarm and its withdrawal and storm bars, defend's strike, break-off and shelter rules, and hunt; the call-backs say it ("against your side's 6,000 (Imák's 1,500 with your lords camped there or due within 8 hours)").
- **Guards strike only from inside their area**; **a lord on his way to shelter plans again every hour**, and **"can he get there first" is judged against every party of the danger** (`ChooseRefuge`).
- **The sell order** says a castle buys no loot (only a town) and a batch of prisoners holding a captured enemy lord pays nothing (the game's `SellPrisonersAction`).
- **War room:** the lords ordered by a fast answer in the minute before a council sits count as ordered meanwhile (`Room.BeginSitting`), so a council briefed before those orders took effect cannot overturn them.
- **Fog check:** the places named by a poke's own news count as known (`Brain.Known`).
- **The control arm C0** (all realms playing with the game's AI): `RunRules.Arm` gives C0 when the kingdom is the game's (its agent off or watching) and both rivals are the game's; `Runs.exe` and `agents.run check` name it; `scripts\arm.ps1 -Arm C0` (the kingdom's agent off, `-KingdomWatches` for shadow; the rivals shadow, `-RivalsOff` for off; the lieutenant off); the playerless switch's message names C0.
- **Outside the repository:** the agents' instructions (`kingdom-war-council`, `orc-war-council`, `dolguldur-war-council` `warroom-system.md`: join_hours also counts "on your side every lord of yours camped in the same siege and, within those hours, those marching to join the lord or bound for the same place or target"; `lieutenant\warroom-system.md` the same in its question list); backups `*.bak-20261025-before-both-sides`; templates in `agent/` match. `scripts\arm.ps1 -Arm C0` run at 20:09: the kingdom's agent live → off (logged in `runs\arms.log`).
- Versions set to 0.12.4.
- **Checks:**
  - Build with no warnings. Smoke test passed in full (287 checks; new: four lords camped in one siege, 1,500 each, against a relief of 4,155: nobody lifts the siege, the alarm says "against your side's 6,000"; the same relief against one lord: he lifts it; two lords ordered at the same target, 600 each, against 1,200: neither shelters; the arm rules give C0; changed: the relief alarm's wording).
  - War room self-test passed (71; new: an order given the minute before a council sits stands against it). Dashboard self-test passed (27). Speed test within budget. 688 game references, 0 missing.
  - Installed with `scripts\deploy.ps1` at 20:09, the game closed: `BannerlordAgents.dll` sha256 `474540D24DE0487C…`, `WarRoom.dll` `918317060F491648…`. War rooms and dashboard stopped for the build and started again 20:09.
- **Not yet seen in a game:** all of 0.12.4. The control run (C0) gives no agent orders, so none of the order changes affect it.

### 20:12 · The control run set up with every agent off · (no install)
- For C0: all agents disabled, so they do not needlessly take up usage. The game's AI just plays, with Claude watching it and keeping all of its logs, so we can report on the run.
- **Outside the repository:** `scripts\arm.ps1 -Arm C0 -RivalsOff` at 20:12: the orcs' and Dol Guldur's agents shadow → off in their `agent.txt` (the kingdom's agent and the lieutenant were already off); logged in `runs\arms.log`. War rooms stopped (`scripts\agents.ps1 stop`), the dashboard started again alone (detached). The bridge's logs and the run record are unchanged. docs/RUNS.md says so.

### 20:15–20:35 · Run C0 on 0.12.4, all 60 days: counts
- `20261025-2015-C0`, from TEST START, one load, every realm the game's AI, every agent off (0 calls, $0.00), 0.34 real hours. The kingdom ±0 towns (Nanduhirion taken, Goblin Town lost), +2 castles net (Sirannon, Caradhras; Imnagath and Torech Emel lost and retaken), +4 villages; 137 enemy lords taken against 142 of its own; lord battles 120-115; on day 29 Val's army (5,454 men) destroyed outside Glad Thaw with 13 lords taken. Against the counted C2 run: holdings +2 against +4, villages +4 against +12, own lords lost 142 against 29. Details in docs/RUNS.md; report in `runs\20261025-2015-C0\report.md`, the table in `runs\compare.md`.
- **Health:** 0 errors, 0 freezes, 1,440 report rounds with none folded. Two false alarms from a run with every agent off (`Runs.exe watch`'s war-room count; the self-check's "two agents" for the kingdom's own and the lieutenant, both off): to fix when the game is closed.

### 20:51 · Version 0.12.5: the control run's two false alarms, and C1 set again · commit f839570 (installed 20:50)
- **The self-check** (`BridgeBehavior`): two agents of one realm both switched off (C0: the kingdom's own and the lieutenant) are a note ("both are switched off, so neither plays it"), not a failed "realm under watch"; only two switched on is still a problem.
- **`Runs.exe watch`** counts a war room for each agent switched on (live or watching) only, so the control (every agent off) no longer reads "only 0 of 3 war rooms are running".
- Versions set to 0.12.5.
- **Checks:** build with no warnings; smoke test, war room self-test and dashboard self-test passed. The speed test was not run (nothing in the hourly work changed). Installed with `scripts\deploy.ps1` at 20:50, the game closed: `BannerlordAgents.dll` sha256 `1D0B41A42DA9B8F5…`, `WarRoom.dll` `7812E1D47EB06BD8…`. Dashboard stopped for the build.
- I asked to set up the C1 test again. **Outside the repository:** `scripts\arm.ps1 -Arm C1` at 20:51: the kingdom's agent, the orcs' and Dol Guldur's off → live, the lieutenant off (logged in `runs\arms.log`); the four war rooms and the dashboard started (`scripts\agents.ps1 start`). docs/RUNS.md says so.

## Monday, October 26

### 21:03 (Oct 25) – 01:21 · Run C1 again on 0.12.5, all 60 days: counts
- `20261025-2103-C1`, from TEST START, one load, all three realm agents live. Paused by me on day 15 (21:25) when my usage ran low; the computer slept until 00:21; carried on from there. The kingdom +2 towns (Nanduhirion, Eastern Moria), +1 castle net (Sirannon, Caradhras, Hollin Gate taken; Imnagath lost and retaken; Torech Emel and Lasgalor lost to Dol Guldur), +10 villages; 169 enemy lords taken against 12 of its own; lord battles 70-14; the orcs' army destroyed at Daeron's siege camp on day 26 (30 lords), Dol Guldur's two armies on day 54 (21 lords). Agents' list price $350.82 (kingdom $119.06, orcs $145.13, Dol Guldur $86.63); 4 failed calls of 1,081 (3 on the session limit, 1 stopped at 120 s). Against the counted C2: holdings +3 against +4, villages +10 against +12, own lords lost 12 against 29. Details in docs/RUNS.md; report in `runs\20261025-2103-C1\report.md`, the table in `runs\compare.md`.
- **Health:** 1,440 report rounds, none folded. The one "freeze" is the computer's sleep. One handled error (the kingdom's `poke.txt` swap, a minute before the end; that poke was answered). One made-up id (`lord_CharacterObject_8269`), refused for that lord: the order check could accept a `lord_` prefix (not yet done).

## Wednesday, October 28

### 00:30 · Run A set again · (no install)
- I was about to play the game myself for a bit, using the TEST CONTINUOUS save to save my progress, but starting at the "TEST START" save, with everything set up accordingly.
- **Outside the repository:** `scripts\arm.ps1 -Arm A` at 00:30: the lieutenant off → live, the kingdom's agent live → off, the orcs' and Dol Guldur's agents stay live (logged in `runs\arms.log`); the war rooms and the dashboard restarted (`scripts\agents.ps1 restart`). Nothing installed (0.12.5 stays). docs/RUNS.md says how the run's saves carry it on.

### 01:00 · The plan for 0.13 written · (no install)
- First implement all of the functionality (diplomacy and the agents being able to write their own tools) into the game and collect with those in action, with my example of a 'recruit in place' order, the agent's choice of a one-off or a kept tool, and both my moves and the agents' logged, capturing everything from them as well. Then: the final comprehensive plan, for my review.
- **What:** `docs/PLAN-0.13.md`: the agents' own tools (the language's lists, per-place memory and new moves and facts; the checker, a "check a tool" question with a try on the newest hour, the guards, the realm's library with versions and each tool's record; my example written out as a test, never shown to the agents), diplomacy (the game's own votes for war, peace with tribute, alliances, calls to war, trade agreements, policies and fiefs; the ruler's say; offers between realms; letters between agents; the War of the Ring), the training record (my moves only while I rule, the report with each move, the moves not recorded yet; the agents' episode index, outcomes, toolbook and diplomacy book; a packed bundle per run), the instructions, the tests, the order of work, the runs after it, the risks and 11 decisions for me. `docs/PLAN.md` points to it.
- **Found while writing it:** of the recorder's 120 records, 37 proposals and 7 orders from the agent runs were credited to me (the game's player clan stays mine); the agents' event streams carry an empty thinking block; C: is 96% full (48 GB free).

### 01:25 · The plan for 0.13 revised with my decisions · (no install)
- **My eleven answers** are at the head of `docs/PLAN-0.13.md`, each with what it means: our tools stay fully the agents' and their definitions become readable; a couple of example tools in the instructions, my own among them; the lieutenant writes tools with my approval; libraries carry over between runs; the War of the Ring's rules stay; the allied channel (letters and joint plans between allied agents, and me speaking to agent allies through my lieutenant); every diplomatic act, mercenaries and expulsion included; the instructions as a skeleton during the build and their final wording after it, which I review; the backup's place proposed after the build; comparisons free.
- **Added:** B5 (the allied channel and the lieutenant), the order of work's new steps (the allied channel; the skeleton; my decisions after the build), and the open points: an agent of its own for a realm allied to mine, so I have an agent ally to speak to; letters to realms that are not allies.

### 02:12 · 0.13 step 1 of 7, the training record (0.13.0-pre1) · commit 29e12b9 (not installed)
- I asked to log both my moves and the agents', capturing everything from them as well, and to implement the full plan now, not stopping until it is done. This is step 1 of `docs/PLAN-0.13.md` Part F (Part C, the record), first so that everything after is captured.
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

### 02:34 · 0.13 step 2 of 7, the language grows (0.13.0-pre1) · commit 8e9ef99 (not installed)
- Tool writing should be for custom strategies the agents want to pursue, with my circuit order as one of the examples. This is step 2 of `docs/PLAN-0.13.md` Part F (A2): the language the agents will write their own tools in.
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
- **Checks:** build with no warnings; smoke test passed (341 checks; 36 new: lists and tables, what the checker refuses and how it says so, the limits, the list functions in a running tool, recruit_here, wait and arrived, and my circuit end to end on a made-up world: each place once a day, a band too strong left alone and a small one struck, sheltering from an army, selling first once the loot is over its bar, a place lost skipped with one call-back); 722 game references, 0 missing. The war room is unchanged in this step.
- **Outside the repository:** none. Not installed.

### 02:55 · 0.13 step 3 of 7, the agents' own tools (0.13.0-pre1) · commit 5902f6e (not installed)
- If the agent wants, it can keep the tool it has written as a one-off order, or if it thinks the tool has reuse value, it can add it to its tool and order library so it is able to reuse it, and our tools should still have full access. This is step 3 of `docs/PLAN-0.13.md` Part F (A1, A4 to A8).
- **An answer's `tools`** (read before its orders): each tool is checked; then kept (`"keep": true`), used once for that answer's orders, changed (writing a kept tool's name again makes a new version: orders already running keep the version they started with), or retired (`"retire": true`). A tool that fails the checker is refused with its problems, and so are the orders naming it. Each tool gets its own line in receipts.log ("KEPT. circuit v1 is kept in your library"), in the orders' record and in the episode index, and a kept or retired tool a chronicle line. In shadow mode a tool is checked only.
- **The realm's library** (`Tools/OwnLibrary.cs`): in the channel folder's `library\` (an index and one file per version), so it carries over between loads, campaigns and runs; the player's realm has one, shared by its own agent and the lieutenant. At most 30 kept tools; no tool may take the name of one of ours. Each kept tool's record (runs, how they ended, call-backs a day, men, gold and strength over its runs, battles fought and won, lords taken and lost, holdings gained and lost, the runs it was used in) is summed from its runs' outcomes. A run's setup records each realm's kept tools as it begins.
- **What the agent reads:** `tools.txt` per channel, with **YOUR OWN TOOLS** after ours (each with what it takes, its settings and its record, refreshed once a day); `tools\<name>.json`, every tool's whole definition, ours and its own.
- **"Check a tool"** (`check_tool`, served beside "ask the way"): the game's own checker, linked into the war room, with each problem's place; with a lord named, the try: the bridge works the tool out on the realm's newest hour (the first move, which rule would fire and where it leads, anything that comes out none), moving no one. The war room waits up to 10 s, else the checker's result stands alone. Every check and try is logged (`routes.jsonl`, the toolbook).
- **The guards on an agent's own tool:** at most 6 call-backs an in-game day (the rest held and summed in one when the day is out); stopped, with its lord let go and the agent told, when it takes over 5 ms of the helper's time an hour for 3 hours running; the 3 changes of state an hour and the safety and food rules hold as for ours. The war room's runaway guard rises from 16,000 to 48,000 characters for an answer that writes tools.
- **The toolbook** gains every tool written (its whole definition), its check and problems, what became of it, and each try.
- **Checks:** build with no warnings; smoke test passed (365 checks; 24 new: tools kept, used once and refused with their orders, the library's files and listing, two versions running side by side, the same definition unchanged, a kept tool's record, retiring, our names refused, the 30-tool limit, the library carried over a new load, shadow mode, the call-back and time guards, the try asked directly and through the channel); war room self-test passed (83; 8 new: check_tool with the checker alone, an answer's tool form, the try through a stand-in bridge and without one, the logging, the runaway limit); 726 game references, 0 missing.
- **Outside the repository:** none. Not installed.

### 03:18 · 0.13 step 4 of 7, diplomacy (0.13.0-pre1) · commit abe6360 (not installed)
- Build everything out now, for a long run (every diplomatic act, mercenaries and expulsion included); the game's hardcoded rules stay, and a ruler may overrule its clans at the game's price. This is step 4 of `docs/PLAN-0.13.md` Part F (Part B).
- **Read in the game's code first:** a realm the game runs settles each vote the moment it is proposed, and peace, an alliance, a call to war or a trade agreement between two such realms is made by the proposer's vote alone; only the player's own realm is ever asked. The pack (TAOM 2.0.26) blocks peace between hostile realms while the War of the Ring rages, and some wars, inside the game's own actions.
- **The acts** (`Diplomacy.cs`, an answer's `diplomacy`): propose war, peace (with a daily tribute either way and its days), an alliance, a call to war, a trade agreement, a policy (adopt or abolish), expelling a clan; hire and let go mercenary clans; vote; decide as ruler; accept or decline an offer. Each proposal is the game's own vote in the realm at the game's influence price, settled at once with the ruler's clan supporting it fully (`"overrule": true` decides it even against the clans, at the game's price). A mercenary clan is hired only if it would take the game's own terms.
- **The agent's say in its realm's votes:** a vote its other clans propose is held at the moment the game would announce its result, with the clans' support already counted, for up to 12 in-game hours: the agent votes as its clan (the game's price for its weight) and, as ruler, may decide against the clans (the game's price for overruling); otherwise the clans' choice stands. In your kingdom, when an agent rules it, the game holds the votes itself and they are settled the same way.
- **Offers to an agent's realm:** an offer another realm chose to make is held for up to 12 hours until the agent accepts or declines; with no answer it is accepted, as the game does between the realms it runs. A "no" to your kingdom while an agent rules it no longer opens your window.
- **Checked before carried out** (`DiploRules`, which needs no game): the act known, the stance it needs (at war for peace, at peace for war or an alliance, an ally for a call to war), the War of the Ring's rule, the influence, the vote or offer open, the clan in the realm or for hire; then the game's own conditions on its thread. Held votes and offers live in the bridge only, never in the save.
- **What the agent reads:** OPEN VOTES AND OFFERS (each vote's options and the clans' support so far, its vote and decision, what deciding costs, when it closes; each offer's terms and when it lapses) and DIPLOMACY (influence and each act's cost, the War of the Ring, every realm's stance with the war's tally, tribute and trade, the realm's clans, mercenaries in its pay and those for hire, the policies) in its report, copied hourly. A vote opened, closing unvoted or settled, and an offer made or settled, poke the agent; a peace, alliance or call-to-war offer calls a council by default (new agent folders).
- **The record:** `diplomacy.jsonl` (every act and its receipt, each vote held, decided and concluded, each offer held, accepted, declined or lapsed, mercenaries hired and released) and the episode index.
- **Checks:** build with no warnings; smoke test passed (383 checks; 18 new: acts read as written, the checks of every act against a made-up realm's screens, the two report sections, an answer's acts refused, checked in shadow mode and handed to the game's thread when live, the book and the index); war room self-test passed (83); 803 game references, 0 missing; the self-check also looks for the vote's private parts by name. The game-side holding and settling is only testable in the game: it is for the shake-down (step 9).
- **Outside the repository:** none. Not installed.

### 03:26 · 0.13 step 5 of 7, the allied channel and the lieutenant (0.13.0-pre1) · commit 744ec81 (not installed)
- Agents communicate with their allies to coordinate attacks, the player can communicate with their allies (who are agents) through their lieutenant, and the lieutenant writes tools based on my permission. This is step 5 of `docs/PLAN-0.13.md` Part F (B5).
- **Letters** (`Allies.cs`, an answer's `letters`): an agent writes to an allied realm's agent; the letter reaches it in its next poke, saying who wrote and when; your realm is written to through your lieutenant, into its window. A letter binds no one and carries no orders, and tells only what its writer chose to (each realm's fog holds). A letter to a realm not allied, or one no agent reads, is refused with that reason.
- **Joint plans:** a letter may carry one in a fixed form (what, where, when, each side's part), with an id; later letters agree to it or decline it (only the realm it was proposed to), or mark it under way, done or dropped. Plans still open show in both realms' reports under **ALLIES AND JOINT PLANS**, beside each ally, whether its ruler reads letters, and its last letters.
- **The lieutenant:** its proposals now carry tools it would write (each shown with what it does, its settings and the checker's result), diplomatic acts and letters to your allies, beside its orders, all under its yes rule (on your words or your Approve; Decline drops them); a load puts a waiting proposal back whole. Your allies' letters come into its window and into its next message. Its war room passes them through, and offers it "check a tool" too.
- **The record:** `allies.jsonl` (every letter and every change of a plan, labelled by session, so a load keeps those of its save's history), the diplomacy book and the episode index.
- **Checks:** build with no warnings; smoke test passed (393 checks; 10 new: letters refused to a realm not allied and to one no agent reads, a letter with a joint plan delivered in the ally's next poke, the writer agreeing to its own plan refused, the ally agreeing, the section in both reports, the store and the book, an answer's letters receipted, letters and plans read back at a new load); war room self-test passed (85; 2 new: the lieutenant's message with letters and a proposal's letters, the answer file carrying tools, diplomacy and letters); 803 game references, 0 missing.
- **Outside the repository:** none. Not installed.

### 03:33 · 0.13 step 6 of 7, the instructions' skeleton (0.13.0-pre1) · commit 517c1df (not installed)
- Build all of the skeleton first; once all the building is done, I review the wording and we write the final prompt. The circuit goes in their prompt for inspiration, with a couple of other examples. This is step 6 of `docs/PLAN-0.13.md` Part F (Part D). The final wording, and the doctrine's diplomacy principles, are for step 8.
- **The war councils' instructions** (`agent/warroom-system.template.md`) gain working sections: **Writing your own tools** (the tool's form, what it takes and of which kind, settings, remembering, states and rules, every move, value, name and function of the language, the limits, keeping, using once, changing and retiring a tool, the record, and three example tools: my circuit, a watch that waits on an event, an army leader's sortie), **Checking a tool** (left out of a call without the tool), **Diplomacy** (every act and its form, how proposals, the realm's votes and offers go, mercenaries, the War of the Ring), **Allies** (letters and joint plans); and "How you answer" names the answer's `tools`, `diplomacy` and `letters`.
- **The lieutenant's instructions** (`agent/lieutenant-system.template.md`): tools, diplomacy and letters to allies, all on the ruler's yes, its allies' letters, and checking a tool (left out without the tool).
- **A real short call** (`WarRoom toolcheck --write`, new): a small model (claude-haiku-5-5, low effort) given the two tool sections wrote a two-state tool, checked it, had its first draft refused, fixed it and answered with a tool that passes the checker, in 8.8 s. (A second run's model skipped the mistake it was asked to make; the check now passes on the loop itself and says whether a draft was refused and fixed.)
- **Checks:** smoke test passed (395 checks; 2 new: the instructions' three example tools each pass the game's checker as written, and the circuit there is the one the tests run, rule for rule); war room self-test passed (85).
- **Outside the repository** (each backed up first as `*.bak-20261028-before-0.13`):
  - `C:\Users\karab\Projects\orc-war-council\warroom-system.md`, `dolguldur-war-council\warroom-system.md`, `kingdom-war-council\warroom-system.md`: the new template, each keeping its own realm's two name lines (fingerprints be4df7e7, b0d45ad1, ce056fba);
  - `C:\Users\karab\Projects\lieutenant\warroom-system.md`: the new lieutenant template (f28838b5);
  - the three realm councils' `warroom.txt`: `council_after` gains "offers peace | offers an alliance | offers a call to war", so an offer calls a council.
- Not installed.

### 03:41 · 0.13 step 7 of 7, everything together: version 0.13.0 installed · commit fd3db2d
- I asked to implement the full plan now, not stopping until it is done. Step 7 of `docs/PLAN-0.13.md` Part F: all tests together, the documents, version 0.13.0, the install.
- **The speed test** now runs 30 of the agents' own tools (the circuit, on the orcs' first lords of the big made-up world) beside ours: within budget, the 95th slowest of a hundred hours 166 ms (budget 1,000; median 134 ms; 181 ms at step 1), and the time guard stopped none.
- **All tests:** smoke test 395 checks passed; war room self-test 85 passed; dashboard self-test 27 passed; `WarRoom toolcheck --write` passed (step 6); 803 game references, 0 missing, in the installed DLL too.
- **The documents:** a new [DIPLOMACY.md](DIPLOMACY.md) (what the game does and why votes are held, every act, the agent's say in its realm's votes, offers, what it reads, allies, the lieutenant, the record); [TOOLS.md](TOOLS.md) gains "The language grows" and "The agents' own tools"; [WARROOM.md](WARROOM.md) the answer's new parts, `check_tool`, the runaway limit, `thinking_display`, the call lines' new fields, `toolcheck --write` (and a stray character where a backslash belonged, from an earlier edit, put right); [LOGGING.md](LOGGING.md) "The training record"; [LIEUTENANT.md](LIEUTENANT.md) tools, diplomacy and letters; [MEMORY.md](MEMORY.md) what else carries over; [PLAN.md](PLAN.md)'s week-4 list; [PLAN-0.13.md](PLAN-0.13.md) where it stands and the docket.
- **Version 0.13.0** (`SubModule.xml`, the module and the war room).
- **Installed** with `scripts\deploy.ps1`, the game closed (no Bannerlord process, no war room or dashboard running): `Modules\BannerlordAgents` in the game's folder, the DLL's sha256 `06f9169d7afdc627…`, `runs\Runs.exe` beside it. It stays where it was in the launcher's list.
- **On the docket** (for me, step 8, and the shake-down, step 9): in `docs/PLAN-0.13.md` "Where it stands".

### 03:42 · The war rooms and the dashboard started again (outside the repository)
- **What:** `scripts\agents.ps1 start`: the four war rooms (orcs, Dol Guldur, the lieutenant, the kingdom) and the dashboard, detached, as before the build; each waits for the game. On starting, each war room added `thinking_display = summarized` to its folder's `warroom.txt`, as step 1 said it would (all four checked).

### 12:33 · The two instruction drafts put back as committed (nothing to commit)
- **What:** after the morning's report, the repository's two instruction drafts were found changed on disk: in `agent/warroom-system.template.md` the four new sections (Writing your own tools, Checking a tool, Diplomacy, Allies: 136 lines) had been replaced by the word "now", and a stray character had been added to the first line of it and of `agent/lieutenant-system.template.md`. It looks like typing that went into the editor after a link in the report had selected those lines. Both were put back from the last commit (`git checkout`); the changed copies are kept in the session's scratch folder. The copies the agents read were never touched: all four matched their fingerprints (be4df7e7, b0d45ad1, ce056fba, f28838b5). Links in reports now point at one line, not a range.
- **The wording:** I asked for the new sections, to read them for step 8, and got them whole, word for word.

### 12:58 · Version 0.13.1: the backup on the D: drive, installed · commit 40973a9
- **Decision 9** (on the proposal of a copy on the laptop's second drive and one in a private Hugging Face dataset): the data stays local for now, at the D: drive.
- **What:** `Runs.exe backup [--to <folder>]`, and at every run's end the bridge asks for it after the bundle (`Runs.exe bundle <run> --backup`). The records folder (`%LOCALAPPDATA%\BannerlordAgents`) and every agent's folder in `agents` are copied into the place `backup` in `settings.txt` names: `D:\BannerlordAgents-backup`, on the laptop's second disk (789 GB free). It never deletes; a file that only grew gets its new bytes added; one that shrank or was rewritten is copied whole, and the copy it replaces is kept under `earlier\<time>\`; a file another program holds shut is named and left for the next backup. It works at background priority, refuses a place inside what it copies or a drive without room, and writes `backup.log` and `README.txt` beside the copy. The bridge knows the new setting (it writes `settings.txt` back with only the keys it knows, so an unknown key would be lost). [LOGGING.md](LOGGING.md#the-backup-since-0131), [RUNS.md](RUNS.md).
- **Also:** two fields of the tool checker that only the game's side sets now start at their defaults, so the war room's build has no warnings. Its build had shown these two warnings since step 3 (commit 5902f6e): the entries for steps 3 to 5 said "build with no warnings", which held for the module but missed the war room.
- **Checks:** a full rebuild with no warnings; smoke test passed (405 checks; 10 new: the first backup whole, nothing written when nothing changed, a grown file's new bytes added to the same copy, a shrunk file copied whole with its old copy kept, nothing deleted, a file held shut left for the next backup and copied by it, a place inside what it copies refused, `bundle --backup`, the bridge keeping the setting and asking for the backup only when a place is set); war room self-test passed; dashboard self-test passed (27); 803 game references, 0 missing.
- **Version 0.13.1** (`SubModule.xml`, the module, Runs.exe; the war room stays 0.13.0).
- **Installed** with `scripts\deploy.ps1`, the game closed and the war rooms stopped: the DLL's sha256 `d4a75205b821ae56…`, `runs\Runs.exe` 0.13.1.
- **Outside the repository:**
  - the war rooms and the dashboard stopped for the build (about 12:45) and started again after the first backup (12:56, `scripts\agents.ps1 start`, detached; each waits for the game);
  - `%LOCALAPPDATA%\BannerlordAgents\settings.txt`: `backup = D:\BannerlordAgents-backup` added, with its comment line, the game closed;
  - `D:\BannerlordAgents-backup`: the first backup, made by hand with the installed Runs.exe: 35,062 files from the records folder and the four agent folders, 7,480.6 MB, in 6 min 34 s at background priority, none in use or failed. Checked against the originals: the same files and bytes in each of the five folders, and the same fingerprints for the files tried (the orcs' pokes, `settings.txt`, `bridge.log`, the orcs' instructions). A second backup at once found all 35,062 unchanged and wrote nothing. 777.7 GB left on D:.
- **On the docket** (step 8): the instructions' final wording and the doctrine's diplomacy principles; whether a realm allied to mine gets an agent; letters to realms that are not allies. A copy off the laptop can follow when I want one.

### 19:35 · Version 0.13.2: the records moved to the D: drive, installed · commit 5e9516b
- **Decision** (asked after 0.13.1 whether the records themselves should move too): yes, the records themselves move to D: as well.
- **What:** the records folder now lives at `D:\BannerlordAgents`, on the laptop's second disk. `%LOCALAPPDATA%\BannerlordAgents` is a link (a junction) to it, so the bridge, the war rooms, the dashboard, Runs.exe and the scripts all find it where they always did, and none of them changed. The four agent folders stay where they are (`C:\Users\karab\Projects\...`, 80 MB). [LOGGING.md](LOGGING.md#where-the-files-are); CLAUDE.md's rule on logs says to keep to the usual path.
- **The backup's check** that its place is not inside what it copies now follows links (Runs.exe). Through the link, a place inside `D:\BannerlordAgents` looked as if it were outside the records folder, so each backup would have copied the one before it into the records until D: was full. The installed 0.13.1 Runs.exe did just that on a made-up folder; 0.13.2 refuses it, there and on the real folders (`backup --to D:\BannerlordAgents\copy-test`: refused, nothing made).
- **The open files' lock** (the module): the smoke test, run again after a first install, ended in two runs out of three with the worker's flush of the open files meeting a file the test had just opened on its own thread ("Collection was modified"). In the game only the worker writes those files (the game's thread hands its lines to it), and no crash record names the module (Windows' application log, the last 30 days: only these test runs). One lock now guards them. A new check opens 400 files on one thread while another flushes them all: with the old code it failed with the same error, with the lock it passes.
- **Checks:** the smoke test passed three times running, then again in `deploy.ps1` (407 checks; 2 new: a place inside where a linked records folder really is is refused; the open files on two threads at once); war room self-test passed (85); dashboard self-test passed (27); a build with no warnings; 803 game references, 0 missing, in the installed DLL.
- **Version 0.13.2** (`SubModule.xml`, the module, Runs.exe; the war room stays 0.13.0).
- **Installed** with `scripts\deploy.ps1`, the game closed and the war rooms stopped: the DLL's sha256 `036ec390ad8baf63…`, `runs\Runs.exe` 0.13.2 (`Runs.dll` `5d704cd86618ab5e…`). A first install at about 19:20, after one smoke test that passed, had put in Runs.exe 0.13.2 beside the unchanged module (still 0.13.1, DLL `787aeab0641b8148…`); the two test runs after it failed as above, and this install replaces it.
- **Outside the repository:**
  - the war rooms and the dashboard stopped at 19:15, started again at 19:22 after the move, stopped at about 19:28 for the second build and started at 19:33 (`scripts\agents.ps1`, detached; each waits for the game);
  - the move, the game closed: the records folder renamed to `%LOCALAPPDATA%\BannerlordAgents-old-20261028` (so nothing could write to it, and the rename proved nothing held a file in it); copied with robocopy to `D:\BannerlordAgents` (28,674 files, 6,003 folders, 7.22 GB, in 4.5 s), whose permissions were set as the old folder's (my account, the administrators and the system only); checked against the old folder file by file: the same files, sizes and times, every byte the same (7,757,658,160), nothing extra; then the link made at the old name. Reads and writes through it land on D: (the war rooms' and the dashboard's logs since 19:22);
  - the old copy stays on C: as it was at the move (28,674 files, 7.2 GB) until I say it can go; C: has 42 GB free with it, D: 770 GB;
  - `D:\BannerlordAgents-backup`: a backup through the link at 19:22 found 35,056 of the 35,062 files unchanged and added the new bytes of 6 logs that had grown.
- **On the docket:** deleting the old copy on C: (my word); a copy off D:, now that the records and their backup share that disk; step 8 as before.

## Thursday, October 29

### 15:08 · The agents' instructions: raiders struck while they are alone · commit 26259c9 (the template)
- **Oct 29**, reading the instructions (step 8), on the general system prompt: telling the AI to attack raiders "on their way home" is wrong in terms of the game mechanics. The smarter move is to attack them when they are isolated from their allies and we can either catch them before they get to safety, or when they are already bogged down in a raid, which means they are stuck, isolated, and therefore in a vulnerable position.
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

### 15:44 · Version 0.13.3: recruiting at towns and castles, in the instructions and the circuit example; installed · commit e18e955
- **Oct 29**, on the instructions given whole: the instruction set is good, but the recruitment loop example needs a revision. The game allows recruitment at towns and castles, and towns actually have the most notables to recruit from, so are recruitment centers. This must be known, and the recruitment circuit state machine must also include recruitment at a town and at a castle, so the example does not confuse the agent.
- **The game's rules, read for it:**
  - the base game gives notables to towns (2 merchants, 2 gang leaders, 1 artisan) and villages (1 headman, 2 rural notables) only (`DefaultNotableSpawnModel`, `NotablesCampaignBehavior`);
  - TAOM keeps those counts, some peoples' feats changing them (`TaomNotableSpawnModel`), and adds castle recruiting (`TAOM.Features.CastleRecruitment`): 3 notables a castle by default, with the game's own lords recruiting there too. My TAOM settings file holds no changes, so the defaults are in force;
  - each notable holds up to 6 volunteers (`Hero.VolunteerTypes`);
  - our records agree: of the 268,289 men the watched realms' lords recruited in the record (`recruitment.jsonl`, 1,356 realm-days), 147,430 came from castles, 62,101 from towns and 58,758 from villages; one lord's day at one place gave about 10.3 men in a town, 5.8 in a castle and 5.6 in a village;
  - our recruit order and the `recruit at` move already take volunteers in any town, castle or village with notables (`RecruitTour.MayRecruitHere`).
- **The instructions** (`agent/warroom-system.template.md`):
  - **a new "Recruiting" section,** before "Money": volunteers come from the notables of towns, castles and villages, up to six a notable, new ones over the days; a town has the most notables, about five, so towns are the centres of recruiting, a castle or a village about three; which men a lord may take depends on his standing with the notable, and each must fit his gold and his wage cap; a besieged town or castle, a village raided or looted, or an enemy's place gives none. Game facts only: none of the records' numbers is in it.
  - **the circuit example, mine,** now recruits each round first at the realm's nearest town, where its loot is sold as it rides in (the module's loot-only sale on entering a town), then at each of its places, then at its home castle (`recruit at at`), and between rounds rests or patrols at home as before. When its loot is worth `sell_above` it goes to the town at once, unless already inside it. A town lost or besieged on the way is passed over, and so is a besieged home. The separate "sell" state is gone: the town's stop sells. Its lead-in, `about` and `takes` say so.
- **The order library and the module** (version 0.13.3): the guard order's description no longer says "castles give no recruits" (it refills in the nearest town or village, as before), and a refusal for a tool's `friendly_town_or_village` place no longer says "only those give recruits". Both are what the agents read, and both contradicted the new section. TOOLS.md says the same. The war room carries the same library for its tool checks.
- **Checks:**
  - smoke test passed, 410 checks, 3 new (the town first, then home's castle, back to the town with loot), and the circuit in the instructions is still the one the tests run, rule for rule; it passed again in `deploy.ps1`;
  - the speed test with 30 of the new circuits: WITHIN BUDGET (95th slowest hour of a hundred 113.8 ms of 1,000);
  - war room self-test passed; dashboard self-test passed; build with no warnings; 803 game references, 0 missing, in the installed DLL.
- **Installed** with `scripts\deploy.ps1` at 15:43, the game closed and the war rooms stopped: the DLL's sha256 `bfec99f90f069fab…`, `SubModule.xml` v0.13.3; the old line is no longer in the DLL. The war room and Runs.exe keep their versions (the war room rebuilt for its copy of the library).
- **Outside the repository:**
  - each realm agent's `warroom-system.md` (orc, Dol Guldur and kingdom war councils) rebuilt from the new template at about 15:41, keeping its own name and opening line (compared line by line: the orcs' differs nowhere, the other two in lines 1 and 3). Fingerprints (sha256), before → after: orcs `e16d43b6…` → `a299989f…`, Dol Guldur `0a4a33b1…` → `ddc12c12…`, kingdom `dd98fbd5…` → `a399c6a1…`. Backups are `warroom-system.md.bak-20261029-before-recruiting`. The lieutenant's instructions have no circuit and are unchanged;
  - the war rooms and the dashboard stopped at about 15:42 for the build and started again at 15:44 (`scripts\agents.ps1`, detached; each waits for the game). Each channel's `tools.txt` takes the guard order's new words when the next game session writes it.
- **On the docket:** the recruitment day log (`Recruitment.Day`, `Recruitment.OnHour`) still counts the volunteers waiting and the hours where lords could recruit in towns and villages only; castles should count too (our measurement only; the agents never read it). And, for me to decide: the guard order could refill at the castle it guards when that castle has volunteers, instead of riding to the nearest town or village.

### 18:53 · Version 0.13.4: the guard order takes its own castle's volunteers first; the recruitment log counts castles; installed · commit 955df7b
- **Oct 29**, to the offer that the guard order refill at the castle it guards now that castles recruit: yes. The recruitment log is to be fixed as well, and once that is done, everything made ready for the test with me playing as Aragorn (Arnor & Gondor).
- **The guard order** (`Tools/Library/defend.json`): when its ranks fall below the floor and it is not needed (the same three tests as before), a guard of a castle or town now first goes to the holding itself and takes its volunteers (a new state, `restock`, with the move `recruit at`), staying in its area and calling nobody back. Back to its patrol once its ranks are over the floor (after at least 3 hours in the state). Still short 2 hours after arriving, it rides on to the nearest town or village of the realm or an ally (`nearest_supply()`) with a call-back, and from there the old refill rules hold (12 hours without 5 points gained, two days in all). When the nearest town or village is the holding itself or there is none, it stays there under those rules, quietly. It breaks off when an enemy lord or army comes within its radius or the holding is besieged (the patrol's rules decide what next), and gives up after a day without reaching the holding (back to its patrol, trying again in two days, with a call-back). A guard of a village goes straight to the old refill. Its description (`about`), `calls_back` and the `floor` setting say so; the agents read them in `tools.txt` and `tools\defend.json`, both written by the bridge at the next session.
- **The recruitment log** (`Recruitment.OnHour`, `Recruitment.Day`): castles now count among the places where a lord could recruit (not besieged), for the hours each lord spent there, and among the realm's places with volunteers waiting (kind `castle` in `places`); the day's sentence says "towns, castles and villages". The log is our measurement only; the agents never read it. Days logged before today counted towns and villages only, so the waiting volunteers and the hours in recruiting places jump from today's days on: compare across with care.
- **Docs:** TOOLS.md (defend's refilling), LOGGING.md (the day's sentence), CLAUDE.md (the "Where volunteers are" note).
- **Checks:**
  - smoke test passed, 411 checks: the guard's first refill test now checks the castle first, no call-back, and the ride on to the village 2 hours after arriving still short; a new one, a lord the castle's own volunteers bring over the floor goes back to its patrol without leaving its area or calling back; the "few recruits in 12 hours" test now runs after the castle's 2 hours; the others take the castle as the first stop;
  - the speed test: WITHIN BUDGET (95th slowest hour of a hundred 124.3 ms of 1,000);
  - war room self-test passed; dashboard self-test 27 passed; build with no warnings; 803 game references, 0 missing.
- **Installed** with `scripts\deploy.ps1` at 18:53, the game closed and the war rooms stopped: the DLL's sha256 `e25cd5c4211d56e1…`, `SubModule.xml` v0.13.4; the new state and wording are in the DLL, the old log wording is not.
- **Ready for my test as Aragorn (arm A, my call for the shake-down instead of the plan's C1):** the modes were already arm A (`arm.ps1 -Arm show`: the orcs' and Dol Guldur's agents live, the lieutenant live, the kingdom's agent off; set Oct 28 00:30); `TEST START` unchanged since Oct 22 (sha256 `8E2FF80DE13C9D5C…`, as in every run's setup); the launcher's modules the same 22 as run `20261025-2103-C1`, ours ticked; every agent's own-tools library empty (no folder yet), so the run starts with our tools only; the realm agents' instructions as rebuilt at 15:41, the lieutenant's equal to its template; C: 37.9 GB free, D: 770 GB.
- **Outside the repository:** the war rooms and the dashboard stopped at about 18:50 for the build and started again at 18:56 (`scripts\agents.ps1`, detached; each waits for the game); no idle host windows left.

### 19:16 · The lieutenant's time limit raised to 5 minutes (outside the repository), in the run's setting up
- **Oct 29**, during the setting up of run A (`20261029-1903`, readied, the clock not yet run): my lieutenant was not able to answer my ask, and I asked why.
- **Why:** my words (#4, 19:11:29: station the realm's forces evenly and defensively across the Dol Guldur and orc fronts) took the lieutenant 122.6 s, and its folder's `warroom.txt` stops a call at 120 s (`timeout_seconds = 120`; the realm agents run on the 240 s default). It read two look-ups, asked "roads in" of 8 holdings and the way once, thought about 9,800 characters, and was writing the last of 64 proposed orders (every lord of the realm to a holding on one front: Lasgalor, Imnagath and Torech Emel against Dol Guldur; Goblin Town, Rivendell, Baracharn and Fennas Drúnin against the orcs; with a warning that the northern castles would be left to their garrisons) when it was stopped. A stopped call is not retried and its words are not read, so I got "(I could not answer that: the call took over 120 s and was stopped)". The answer as far as it got is kept in `logs\warroom-lieutenant\calls\20261029\191129-chat-4\raw.jsonl`. On Oct 23 a smaller ask of the same kind (19 orders) took 57.6 s.
- **Changed:** `C:\Users\karab\Projects\lieutenant\warroom.txt`, `timeout_seconds` 120 → 300 (sha256 `a2b2c8a4…` → `7a75a73c…`; backup `warroom.txt.bak-20261029-before-timeout`). The game's clock is stopped while the lieutenant's window is open, so the longer limit costs waiting, not game time. The war room reads its settings at its start, so only the lieutenant's was restarted (19:16:07, pid 57136, through `scripts\agents.ps1 start`, which leaves running ones alone); the orcs' and Dol Guldur's carried on undisturbed. Nothing in the game or the module changed; the game was running.
- **For me:** ask again; the same ask should now get its answer.

### 19:36 · Version 0.13.5: the test carries on across closing the game and loading its save; the lieutenant with no time limit; installed · commit cc95982
- **Oct 29**, after the first hours of run A (`20261029-1903-A`, about 8 in-game hours, saved as `TEST CONTINUOUS SAVE` at 19:15, game closed): two things. 1: remove the time limit on the lieutenant's thinking. 2: make sure that the test logs and the agents' records survive the game being closed and reloaded into the same save, so the test continues when I have to step off the game and back on it with time.
- **What already survived** (read in the code before changing anything): the run (a load of one of its own saves carries it on, `Run.OnLoad`, since 0.11; `TEST CONTINUOUS SAVE` is among its saves), the agents' memory and chronicle and the lieutenant's conversation (labelled per session, found again by the save's note, since 0.9.1 and 0.11.1; the 19:15 save's note is in `saves.jsonl`), the training record (append-only, tagged by run), the agents' own tools (their libraries). **What did not:** every order in force (cleared at every load, `Orders.Reset`, `Runner.Reset`): the agents' lords went to the game's own AI until the agent ordered them again, and each tool's progress was lost; the agents' wage caps; `run.json`'s last hour (written only at day lines and marks, so hours played after a save and lost within a day went unmarked when replayed); a war room not running after a restart of the laptop (nothing started it). Also found: the run's readied folder `runs\20261029-1903` stayed behind with `status: armed`, because a `Runs.exe watch` at 19:08 put its cursor (`watch.json`) in it and the tidy-up removes a folder holding only `run.json`; left as it is (nothing is deleted without my word).
- **The orders carried on** (new `CarryOn.cs`): at every save (`OnSaveOver`, after the take-back) the game's thread lists the tool orders held (`Orders.KeepForSave`) and the wage caps (`Wages.KeepForSave`), and the helper (urgent lane, as each hour's steps) writes `data\<campaign>\orders\<save>.json`: each tool run with the very definition it runs (`Runner.Export`: its order, settings, state and the hour it entered it, remembered values with tables and lists, the move in force, the safety and food rules' places, its call-back count) and its record so far (`Outcomes.Export`). Never in the save; overwritten with the save of the same name. At a load (`Bridge.Start`, after the reset is posted) the file is read when it is that save's at that hour (History's rule, 0.05 h); each order is checked on the game's thread as at its start (`Orders.Resume`: alive, free, leading his party in the realm, on no Party AI Controls order unless the lieutenant's, among the lords who obey the agent, the realm's agent the same folder and live) and the lord held again under the same tool run's id (new runs are numbered past it), shown in Party AI Controls' panel for the lieutenant's; on the helper the tool is taken up where it was and its move given again (`Runner.Resume`, `Outcomes.Resume` with a `run_resumed` line in the toolbook); a wage cap is put back (`Wages.Resume`). What could not carry on is let go and said: the realm's first look reads "The game was loaded from a save: the N orders you had in force then carry on as they stood (YOUR ORDERS IN FORCE lists them). Not carried on: ..." (still matching the council's "First look" words).
- **The run record:** `run.json` written at every save (not only at a new save name) and when the campaign ends (`Run.OnStop`), so a load knows how far the run had gone. The readied folder's tidy-up passes over `watch.json`.
- **The war rooms:** at each load the bridge looks for each switched-on agent's war room by the lock it holds (`WarRooms.Check`, the name made as the war room's `LiveLock` makes it) and starts any that is missing, with no window, from `war_room` in settings.txt (new key; empty: the alarm `war_room_down`). Checked from outside with the same name: the running orcs' and lieutenant's war rooms found, a folder with none not found.
- **The lieutenant:** no time limit and no thinking cap. The war room reads `timeout_seconds = 0` as no limit (`Claude.Run`; its settings reader took only values over 10 before); a hung call is still stopped after 45 s of silence; a proposal (`"propose"`) gets the longer runaway limit (48,000 characters, as an answer writing tools), since one naming every lord of the realm could pass 16,000. Its folder's `warroom.txt`: `thinking_cap` 0 (was 2000) and `timeout_seconds` 0 (300 since 19:16); backup `warroom.txt.bak-20261029-before-no-limit`. Its start line now reads "thinking as long as it needs, no time limit".
- **Checks:** smoke test 420 (9 new: remembered values of every kind out and back; the circuit written down three stops into its round, cleared, taken up in the same state with its move given again, numbered past, patrolling within its own radius setting, and going back to none of the places it saw before the save; the toolbook's `run_resumed` with its record kept; a guard carried on an hour into its castle's refill riding on at the hour it would have; a run whose state its tool lacks refused with the reason); war room self-test (new: limits of 0 read as none, the proposal's runaway limit); dashboard self-test 27; 814 game references, 0 missing; the speed test WITHIN BUDGET both times (95th slowest hour 222.6 ms beside the self-tests, 204.3 ms alone, of 1,000; this afternoon's were 113.8 and 124.3, the difference in the file-writing parts: look-up files, files, dashboard).
- **Installed** with `scripts\deploy.ps1` at 19:36, the game closed and the war rooms stopped: DLL sha256 `8054e03af5669e23…`, `SubModule.xml` v0.13.5; the new texts are in the DLL.
- **Outside the repository:** `%LOCALAPPDATA%\BannerlordAgents\settings.txt` gained `war_room = C:\Users\karab\Projects\bannerlord-agents\src\BannerlordAgents.WarRoom\bin\Release\net8.0-windows\WarRoom.exe` (backup `settings.txt.bak-20261029-before-war-room`); the lieutenant's `warroom.txt` as above; the war rooms and dashboard stopped at about 19:24 and started at 19:36 (`scripts\agents.ps1`).
- **Not yet seen in the game:** a carry-on at a load, a war room started by the game. The 19:15 save was made before 0.13.5, so it has no orders file: its first load carries the run, the memory and the conversation on, but not the orders; every save from now on carries them.

### 19:42 · Version 0.13.6: the fog check knows the ruler's own diplomacy screens; installed · commit d53cca4
- **Found by the run's watch** (19:4x, the game closed): run `20261029-1903-A`'s first hours raised `fog_leak` on 20 reports, every one the realms' ids in the agents' new DIPLOMACY section ("Kingdom of Arnor and Gondor [new_kingdom]: at war since ...", "Gondor [empire_w]", "Rohan [vlandia]"). A false alarm: the section is what the realm's ruler sees on the game's own kingdom and diplomacy screens (PLAN-0.13 B3 meant its ids to be allowed), but the fog check's list of what a realm may know (`Brain.Known`) never took them. Left alone, every report would raise it, and a real leak could hide among them.
- **Fixed:** `DiploRules.Ids` lists every id the DIPLOMACY and OPEN VOTES AND OFFERS sections may name (the realm, its ruling clan, every realm on the stances' list, the votes and offers with their realms, the realm's clans and those for hire, the policies), and `Brain.Known` gives them to the check; the check also passes over our own numbering of held votes and offers (`[v3]`, `[o2]`), which the news names after they close. Nothing else is allowed: lords, parties and holdings are checked as before.
- **Checks:** smoke test 421 (new: the diplomacy test's whole report passes the fog check with what the realm knows, and without it still fails on the realms' and policies' ids, so the check still bites); war room and dashboard self-tests passed; 814 game references, 0 missing.
- **Installed** with `scripts\deploy.ps1` at 19:41, the game closed and the war rooms stopped: DLL sha256 `c62a9443cf21bc94…`, `SubModule.xml` v0.13.6. War rooms and dashboard stopped at about 19:39 and started again at 19:42.

### 20:05 · Version 0.13.7 built: on screen only the lieutenant's and my own orders; to be installed when the game is next closed · commit a3f240b
- **Oct 29**, during run A (`20261029-1903-A`, the game running): in the next run, the other agents' orders come out of the logs that show up in-game. I only want to see what comes from my lieutenant and the normal game logs and updates in-game; the dashboard stays the same.
- **What reached the game's message log from us:** the agents' pokes ("Dol Guldur: First look since ...", yellow, `Brain`), every order's start, arrival and end ("Order: ...", "... reached ...", "Order for ... ended: ...", cyan, `Orders.Say`), the red alarms (`Diag.Screen`), the lieutenant's answers when its window is closed (`LieutenantWindow.Add`), and the test run's lines (`Run.Say`).
- **Changed:** an agent's poke no longer goes on screen; an order's lines go on screen only when it is the lieutenant's (its realm is the lieutenant's) or my own (`hand`: the console's `agents.order`; `you`: my vassal orders), by `Orders.Shown`. The lieutenant's own lines, the red alarms, the run's lines and the game's own messages stay. Nothing changes in the logs, the run's record, the war rooms or the dashboard.
- **Checks:** smoke test 422 (new: the on-screen rule: the lieutenant's and your own shown, another agent's and its next orders not); the module builds with no warnings. Not yet the war room and dashboard self-tests and the install: the game was running (they run in `deploy.ps1` at the install).
- **Install:** waits until the game is next closed (never while it runs); then `scripts\deploy.ps1`, the war rooms stopped and started around it.

### 20:23 · Version 0.13.8: the lieutenant sees and orders lords in armies; approval in words only, no buttons; installed with 0.13.7 · commit 5a6f911
- **Oct 29**, during run A: it felt as if some lords that should be controlled by my lieutenant kept escaping to the game's AI (like Stein), and my lieutenant could not see lords in armies. Both were to be investigated and fixed. I also did not want to have to click "approve" or "decline" to approve the lieutenant's orders: I should only have to approve them in the chat, since many times I approve some but have comments on others. I was saving and closing the game, so the logger updates and these fixes were to be installed when done.
- **What the record showed:**
  - **Stein:** he never held an order of the lieutenant's. The game's own AI formed his army and called the ruler's clan lords Balakhôr and Oryon into it (Party AI Controls lords with joining armies allowed: nothing held them). Asked to station Stein and Górli, the lieutenant gave each a `disband` (chat 10, 20:08), which hands the leader and every lord of the army back to the game's own AI, and no order after it, though its words said "each now holds a castle".
  - **Armies unseen:** the report named an army's size only ("army of 4,577 men in 13 parties"); the lieutenant said so twice ("I couldn't see who is in Górli's army", "Any lord I didn't name goes back to the game's own AI once his army breaks up"). The realms' agents had the same blind spot.
  - **Not an escape route:** the lieutenant's orders ended early only twice this run (a lord taken prisoner, a join that failed), and the game's own AI never calls a lord we hold into its armies (`DefaultArmyManagementCalculationModel`, 1.4.8: parties with `DoNotMakeNewDecisions` are skipped).
  - **The buttons:** the words "give those orders now" (chat 8) came after the ruler had declined the waiting proposal with the button, so no proposal was waiting and the answer's orders were held for a button ("waits for your yes") though its words said "Orders given"; and an answer that gave the orders agreed to dropped any changed proposal beside them.
- **Changed:**
  - **Every army's lords in the report** (`ViewParty.Members`, `Views.ArmyMembers`, `MemberLines`): under each army of the realm's, its lords with ids, men and strength (and the lieutenant's order, if one holds him); in the lieutenant's report, the lords in the ruler's own army under the YOUR RULER line. The fog check knows them (`Audit.Fog`).
  - **The lieutenant orders a lord in an army:** he is taken out of it first, once the order passed its checks (`Orders.LeaveArmy`, as `release` does; the receipt says so). The realms' agents still order the army's leader (`release`, `disband`), as before, so the rival agents' rules are unchanged in the run.
  - **`disband`'s receipt names** the leader and every lord it hands back to the game's own AI, "unless each is given an order of his own".
  - **Approval in words only** (`LieutenantRules.Gate`, `Lieutenant.OnAnswer`): every answer is to the ruler's words, so its `carry_out` orders (those the words asked for or agreed to) are given, and its `propose` waits for the next words beside them ("carry+propose") or alone; `drop` forgets the waiting proposal. The window's Approve and Decline buttons are gone (`LieutenantWindow.xml`). A verbal agreement is recorded in the ruler's record as `lieutenant_agreed` (`Recorder.OnLieutenantAgreed`).
  - **The lieutenant's instructions** (`agent/lieutenant-system.template.md`, copied to its folder, backup `warroom-system.md.bak-20261029-before-words-only`, sha256 `f28838b5…` → `95f2d4a0…`): "Orders: always ask first" became "Orders: on the ruler's word" (give what the words ask for; propose plans and its own ideas; give what is agreed, propose what is to be changed in the same answer, leave out what is refused; never an order the words did not ask for or agree to; the buttons line gone), and a line on armies (their lords are listed; an order takes a lord out first; a disband hands everyone back unless each gets an order in the same answer). Its war room's message about the waiting proposal says the same (`LieutenantRoom.Message`).
- **0.13.7 installed with it** (built at 20:05 while the game ran): on screen only the lieutenant's and my own order lines.
- **Checks:** smoke test 423 (new: an army's lords named under it in the report; the new gate: given, given with a changed proposal beside, proposed, dropped); war room self-test (its check on the waiting proposal's words follows them); dashboard self-test 27; 813 game references, 0 missing; the speed test WITHIN BUDGET (95th slowest hour 327.0 ms of 1,000: up from 204 to 222 earlier today, most of it in the parts not changed, dashboard 3,799 ms and view 458 ms over the test; the army lists scan the hour's parties once an army, under a millisecond; to watch).
- **Installed** with `scripts\deploy.ps1` at 20:23, the game closed and the war rooms stopped (the first try at about 20:20 stopped at the war room self-test's old wording check: nothing was installed then): DLL sha256 `2794f41727606bfd…`, `SubModule.xml` v0.13.8; the installed window has no Approve or Decline. War rooms and dashboard stopped at about 20:17 and started at 20:25.
- **Seen on disk:** my save at 20:12 (`TEST CONTINUOUS SAVE`, Autumn 2, 1087, 17:00) wrote its orders file: 98 orders in force and 5 wage caps (0.13.5); `run.json` has its hour. Its load will be the first carry-on in the game.

### 20:42 · Version 0.13.9: the lieutenant forms the ruler's own army; a fresh report straight after loading; installed · commit 21255b9
- **Oct 29**, after loading on 0.13.8: new feature: the lieutenant should be able to form an army for me, under my name (so for example if I ask it to get every lord stationed at Imnagath to join my army, it should be able to form an army for me and automatically have them join it). It also said that it still could not see the lords in Stein's army, which was to be checked and fixed. I was closing the game, so the install could follow when done.
- **Why it still could not see Stein's army's lords:** its message at 20:31 (chat 11) carried the report written at Autumn 2, 16:00, by the session before (0.13.7, before the army lists), just before the save. The game loads paused, and the lieutenant's report is written each in-game hour, so words said before the clock first ran read the last session's report. The report written at 20:34 (Autumn 3, 07:00) lists Stein's army with its 7 lords: the 0.13.8 lists work.
- **Fixed:** until the first hour since a load, each message of the ruler's copies the realm as it stands (`Capture.Take`, the game's thread, the clock held by the window) and the helper writes the lieutenant's report and look-ups from it before the message (`Brain.LieutenantReportNow`, `Lieutenant.OnPlayerSays`): nothing else of an hour (no orders stepped, no pokes, no day lines, the run untouched).
- **The ruler's own army** (`Orders.RulersArmy`, the order `my_army`, the lieutenant's only, on the ruler's word): `{"do": "my_army", "lords": [...], "at": "<where it gathers, optional>"}`, no lord of its own. The player's party leads it: formed as the game's own army screen forms one when the ruler leads none (`Kingdom.CreateArmy(Hero.MainHero, place or the ruler's home or the realm's nearest castle or town, Defender)`, after `CanPlayerCreateArmy`), else added to. Each lord named is called in at the game's price (`CalculatePartyInfluenceCost`), paid from the ruler's clan as that screen charges it (`ChangeClanInfluenceAction`; read in 1.4.8: `Army.OnAddPartyInternal` charges nothing for a lord joining the player's army, and `ArmyManagementVM.ExecuteDone` charges the clan itself); refused when the clan cannot pay. A lord held by an order is let go so the game marches him to the ruler; one in another lord's army is taken out of it first (never that army's own leader, `CannotCall` with `fromOtherArmies`); one too far to be called yet gets a join order and is called once close enough, the join now charging the ruler's clan too when the army is the player's (`TryJoin`). Shown in the window's proposal as "Your army: call ...". The war room lets this one order through without a "lord" (`LieutenantRoom.ExtractAnswer`). The lieutenant's instructions describe it (template and its folder's copy, backup `warroom-system.md.bak-20261029-before-my-army`, sha256 `95f2d4a0…` → `e38c4a78…`).
- **Seen in the game on 0.13.8** (20:29 load): the carry-on worked: "orders: 98 carried on from TEST CONTINUOUS SAVE (Autumn 2, 1087, 17:00), 0 not, 5 wage caps".
- **Checks:** smoke test 424 (new: the report written on the spot before any hour, nobody poked); war room self-test (new: `my_army` needs no lord, every other order still does); dashboard self-test 27; 816 game references, 0 missing. The speed test was not run again: the hour's work is unchanged since 0.13.8's run (the new parts run only when the ruler speaks).
- **Installed** with `scripts\deploy.ps1` at 20:42, the game closed and the war rooms stopped: DLL sha256 `24993e6968d1ee66…`, `SubModule.xml` v0.13.9. War rooms and dashboard stopped at about 20:38 and started at 20:43.
- **Not yet seen in the game:** the ruler's army formed or added to by the lieutenant, its influence charge, a far lord joining it; the report written on the spot.

### 20:45 · Version 0.13.10: no false "caught by the save" alarm for orders carried on; installed · commit ee44624
- **Found by the run's watch** (the game closed after the 20:29 session): the 20:29 load raised `load_frozen` (serious, on screen): "After loading, 99 lords we once held have the game's AI switched off ... A save may have caught them." They were the lords whose orders 0.13.5 had just carried on. The saves were clean: at 20:12 98 lords were handed back before the game wrote and taken back after, at 20:34 108, each "all free while saving".
- **Why:** the load's sweep of lords with the game's AI off (`Orders.FrozenAtLoad`) ran after `CarryOn.AtLoad` had held them again, and the past hand-backs' check (`CheckPastHandbacks`, later on the game's tick) counted every lord we ever handed back that is held now, our own carried-on orders included.
- **Fixed:** the sweep runs on the save as loaded, before any order is carried on (`Bridge.Start`); the check passes over a lord an order of ours holds now (`Orders.Holds`). Its words say the orders written at the save are carried on next.
- **Checks:** smoke test 424, war room and dashboard self-tests passed (in `deploy.ps1`); 816 game references, 0 missing.
- **Installed** with `scripts\deploy.ps1` at 20:44, the game closed and the war rooms stopped (about 20:44, started again 20:45): DLL sha256 `f82e4b9de6ca972d…`, `SubModule.xml` v0.13.10.

### 21:04 · Version 0.13.11 built: guards join the ruler's own attack; to be installed when the game is next closed · commit 0a45498
- **Oct 29**, in the game: I was chasing Mauhur, who had 700 men, with my army of more than 4000, but allied lords around me were still not engaging him and were instead running from him. They should be engaging him normally, since they know I am around and that their strength is greater than Mauhur's. I asked why they were not giving chase with me.
- **What the record showed** (session 20261029-204801): at Autumn 5, 09:00, the hour I went after Mauhur (Gundabad, 685 men against my 3,865), 13 guards of Lasgalor (Alenon, Oropherion, Lothuial, Amarië, Orophin, Anairë, Elladan, Celebrían, Elrohir, Chägermeister, Iminyar, Nimrien, Elbereth) struck him; at 12:00 all broke off together by the guard's leash (`distance(target, at) > radius`: Mauhur had run more than 20 from Lasgalor), and patrolled back toward it. None fled: no safety or shelter step named Mauhur. My own party broke off two hours later. A guard weighed only itself and lords bound for its holding or target: a lord attacking the same enemy, the ruler included, did not count.
- **Changed** (the same for every realm):
  - `ruler_target()` (new in the language; `World.RulerTarget`): the enemy party the realm's ruler (the ruling clan's leader) is attacking in person (EngageParty), else none.
  - `World.Friends`: a lord of ours or an ally's attacking the same target counts on our side (the strike's bar, `side_strength`).
  - `World.RulerHunts` (once an hour's world): that enemy and every enemy lord or army that could join the fight within 8 hours, when the ruler's force is the stronger, count neither as a threat near a holding (`Threat`) nor as a danger to flee (`DangerAround`): the ruler's fight.
  - `defend.json`: a guard within its radius of the ruler's target, when its company is within the side's bar, strikes it ("joins your ruler's attack on ..."; a new call-back); while the ruler keeps attacking it, the guard's leash is three times its radius from the holding (both break-off rules); otherwise as before. Its `about` and `calls_back` say so.
- **Checks:** smoke test 425 (new: a guard joins its ruler's attack on an enemy just outside its area, keeps up the chase past it while the ruler does, and goes back once he stops; on the way the test caught `hostile()` taking holdings only, so `ruler_target()` names only an enemy itself). The war room's copy of the library, its self-test, the dashboard's, the speed test and the game references wait for the install (the game was running, the war rooms with it).
- **Install:** when the game is next closed (`scripts\deploy.ps1`, the war rooms stopped and started around it).
- **Installed** at 21:07 on my word, the game closed, with `scripts\deploy.ps1`, the war rooms stopped first (started again 21:08): DLL sha256 `6361510b59cf2bfd…`, `SubModule.xml` v0.13.11, the new rule's words in the DLL. Checks then: smoke test, war room self-test (its copy of the library rebuilt) and dashboard self-test passed; 816 game references, 0 missing; the speed test WITHIN BUDGET (95th slowest hour of a hundred 369.9 ms of 1,000; 327.0 at 0.13.8: orders 191 ms over the test against 140, the rest again in the dashboard 4,903 ms and view 595 ms, which this did not change; still creeping, to look into).

### 21:17 · Version 0.13.12 built: the lieutenant's carried-on orders known as its own at a load; to be installed when the game is next closed · commit 2a14214
- **Found by the run's watch** (the 21:12 load, 0.13.11): "orders: 122 carried on from TEST CONTINUOUS SAVE (Autumn 5, 1087, 15:00), 2 not, 6 wage caps". The two were the lieutenant's recruiting rounds (`recruit_round`, its own tool) for Theorin and Himrion, refused as "follows an order from his ruler's own dialogue (patrol around Goblin Town)": the lieutenant's orders replace a Party AI Controls order, but at the load `Lieutenant.IsRealm` read the helper's agents (`Agents.All`), which its reset fills only after the load's carry-on, so the order counted as another agent's. The same test showed the lieutenant's carried-on orders could not be mirrored in Party AI Controls' panel at a load (`PacLink.OnOrderStarted` asks the same).
- **Fixed:** `Lieutenant.IsRealm` also reads the load's own agents (`Bridge.Configs`, set on the game's thread as the load begins).
- **Also seen, left as it is:** the run's watch flagged the sight check at 122 of 134, once, at 21:13, a minute after the load: the 12 were Khamûl's army besieging Torech Emel, 60 to 65 from the player's party, which the realm's besieged castle sees and the game had not yet marked visible to the player's party just after the load; every check after agreed fully (134 of 134, 135 of 135, 132 of 132, ...).
- **Seen working on 0.13.9 to 0.13.11:** the lieutenant's report written on the spot right after the load ("report on the spot", 21:1x); the carry-on of 122 orders and 6 wage caps.
- **Checks:** smoke test 426 (new: the lieutenant's realm known from the load's settings before the helper's agents are filled). The rest at the install.
- **Install:** when the game is next closed.

### 21:32 · Version 0.13.12 installed, with two small fixes added before it went in · commit 2302318
- **The session it follows** (20261029-211041, 21:10 to 21:28, run 20261029-1903-A, day 9 of 60):
  - **The lieutenant built my army on my words**, the first time in the game. At Autumn 6, 19:00, its answer had the 14 Lasgalor lords follow Aragorn, the 10 at Torech Emel garrison it and Stein called. I replied: "Have all of the lasgalor lords join my army for now then. Have the torech emel lords join my army as well: the siege has already begun so they would only be walking into a trap." The next answer called all 25 lords into my army, and when the clock ran (21:17:33) all 25 joined, Stein included. The first answer's 24 orders had waited for the clock. They were issued in the same moment and ended at once, "called to your army". The end was right, but it was noise in the story.
  - **The battle that followed**: Aragorn's army (8,999 men) went after Khamûl, who was storming Torech Emel with three armies. The field held 11,198 at once (9,990 soldiers and 1,208 horses; the battle helper's battle size is 10,000). It loaded in 280 s, about 1,040 shaders compiled in that time (21:22 to 21:26). Our watchdog called the load a freeze ("game_frozen", 120 s, main thread waiting while 10+ engine workers ran at a full core each). It then ran at 11 fps (median frame 84 ms, the engine's own battle step 43 ms of it). There were no faults and no armour dropped: the armour list peaked at 409,584 of 1,048,576. I closed the game in the battle as too slow (21:27:56). The last save is TEST CONTINUOUS SAVE at 21:22:04, just before it.
  - Also seen: Legolas's guard order flipped between sheltering in Baracharn and going inside Torech Emel across the first hours after the load. Then four lords bound for besieged Torech Emel were flagged stuck 4 to 5 away (Silivren, Calathiel, Fin, Legolas), until the army call took them. Wardens of the Naith (a Lothlórien clan, now landless) declared war on my kingdom after the orcs took Galadriel.
- **Added to 0.13.12 before the install:**
  - `Orders.Describe`: a tool's "toward" step with no party (the safety step's run across open ground, `Runner.Shelter` when no refuge can be reached) read "marches to where  was last seen", 14 times this session (Tûba, Shagash, Naghza). It now reads "falls back across open ground, toward a point N <direction> of <place>".
  - `Run.OnSaved`: the save just made goes to the end of the run's list of saves, so the watch's "load ... to carry it on" names the newest. It named BACKUPS (21:21:53), not TEST CONTINUOUS SAVE (21:22:04).
- **Checks:** the build passed, along with the smoke test (426 pass, 0 fail), the war room's self-test and the dashboard's. RefCheck on the installed DLL against the game's, TAOM.Dependencies', SandBox's and SandBoxCore's bins: 269 game types and 816 member references checked, 0 missing. The war rooms were stopped before the build and started after it (orcs, Dol Guldur, the lieutenant, the kingdom's council, the dashboard).
- **Installed:** 21:31:54. `BannerlordAgents.dll` sha256 `CA6D4E504E3E8BC2CA10F3B4A338EC0AA25E5C0A43B5447064DB5DE394CEACA1`, SubModule v0.13.12.


### 12:52 (Oct 30) · Version 0.13.13 built: the lieutenant's orders wait for its answer's own tools; to be installed when the game is next closed · commit 078f000
- **Seen in the run** (session 20261029-232345, Autumn 9, 15:00): I asked for Himrion and Theorin to recruit together, each taking half at every stop. The lieutenant wrote a new tool, `recruit_pair` (the first tool it has written itself in a run). In the same answer (chat-18.json) it ordered both lords onto it and told me "Done, sire". The orders were refused as unknown ("'recruit_pair' is not an order ... and your own: recruit_round"), and the tool was kept in the realm's library a moment after (receipts.log 12:41:04). On my next words ("Place the orders") the lieutenant gave a different plan with tools already kept (recruit_round for Theorin; Himrion, already full, to follow), so nothing was lost in the game.
- **Cause:** `Lieutenant.Carry` gave the answer's orders on the game's thread first, and only then posted its tools to the helper. An agent's answer takes its tools first (`Channel`, docs/PLAN-0.13.md A8).
- **Changed:**
  - `Lieutenant.Carry`: an answer with tools of its own holds its orders, takes the tools on the helper's thread, then gives the orders through `GiveAfterTools`.
  - `GiveAfterTools` (new): an order naming a refused tool is refused for it, with a receipt and a line in the window. The others carry the answer's own definitions (`Channel.Attach`, now internal; a tool used once is in no library) and go to the game's thread as before.
  - An answer without tools is carried out exactly as before.
- **Checks:** smoke test 427 (new: the lieutenant's orders follow its answer's tools; one naming the tool it just wrote carries that tool, and one naming a refused tool is refused with it). The war room's and the dashboard's self-tests, the speed test and the game references wait for the install (the game was running, the war rooms with it).
- **Install:** when the game is next closed (`scripts\deploy.ps1`, the war rooms stopped and started around it).
- **Installed:** 13:38 (Oct 30), with the game closed after I stormed Glad Thaw.
  - The war rooms were stopped before the build and started after it: orcs, Dol Guldur, the lieutenant, the kingdom's council and the dashboard.
  - `scripts\deploy.ps1` built the mod with 0 warnings, and the smoke test, the war room's self-test and the dashboard's all passed.
  - RefCheck on the installed DLL against the game's, TAOM.Dependencies', SandBox's and SandBoxCore's bins: 269 game types and 816 member references, 0 missing.
  - `BannerlordAgents.dll` sha256 `AB143E555E2D07CDA530E35036C94C612677AA5F5C62D0FEAF1B61552A765556`, SubModule v0.13.13.
- **Also seen working on 0.13.12, in the same session:**
  - The fall-back step's new wording: "Skarvûk falls back across open ground, toward a point 24 south-east of Gwígar".
  - The run's watch now names the newest save: "load TEST CONTINUOUS SAVE to carry it on".


### 19:07 (Oct 30) · Version 0.13.14 installed: a lord's side counts the lords following him inside the holding he is in · commit 5d1c4db
- **Oct 30:** the logic for our orders might still be discounting the strength of nearby lords and lords following other lords when deciding whether to attack or retreat. To be checked and verified from my lieutenant's readings on the Torech Emel to Gwígar corridor's defence. (The lieutenant had just reported the corridor's defence going "badly": Alenon, Oropherion and Arien sheltering in Gwígar, Glin-edhel and Tinnulas raided.)
- **What the record showed** (session 20261030-155534, the lieutenant's report of Winter 1, 1087, 17:00):
  - At 12:00 the lieutenant ordered "Group A": Alenon to defend Tinnulas (radius 30, strike_below 1, shelter_at 1.5, join_hours 12), with Oropherion and Arien to follow him (caution 0).
  - The same hour, Alenon's step "patrol → shelter" fired on `threat_near(at, radius) >= side_strength(at) * shelter_at`: "Forces too strong for Alenon are near Tinnulas: strength 2,087 against your side's 1,135".
  - 1,135 was Alenon alone (1,137). Oropherion (961) and Arien (860) were inside Gwígar with him, and `World.Friends` skipped every lord inside a holding before it asked who marches with him, so they were not counted. With them, 2,958 × 1.5 stands well above 2,087.
  - All three stayed inside Gwígar, every hour, by the same count. Azgar (163 men) looted Tinnulas.
- **Changed** (`World.Friends`, the same for every realm and for both the defend rules and the safety rule, which share it):
  - A lord who marches with him counts in the field within `join_hours`, as before, and now also inside the very holding he is in, which that lord leaves with him.
  - "Marches with him" (`World.MarchesWith`, new) means our `follow` order aimed at his party, the game's `EscortParty` toward him, or joining his army.
  - Another lord inside that holding on no order with him still does not count; nor do guards of other places, which by design join no fight by themselves.
- **Checks:**
  - The build passed, along with the smoke test (428; new: a lord's side counts his two followers inside the same castle, 2,958 as Alenon with Oropherion and Arien, but not another lord there on no order with him, nor a follower beyond the hours given), the war room's self-test and the dashboard's.
  - RefCheck on the installed DLL against the game's, TAOM.Dependencies', SandBox's and SandBoxCore's bins: 269 game types and 816 member references, 0 missing.
  - The war rooms were stopped before the build and started after it.
- **Installed:** 19:07, with the game closed (it closed at 19:06). `BannerlordAgents.dll` sha256 `1EDACBFD829F136BE8BF5E540E7A357E532DF9DAC3509E43F6E193AC107F01D7`, SubModule v0.13.14. `docs/TOOLS.md` updated (our side of a fight).


### 19:19 (Oct 30) · Version 0.13.15 installed: every held lord rides to relieve a lord of ours in a fight he can reach and win · commit d673cac
- **Oct 30:** lords under any order and not in an army should always ride with all haste to relieve lords in a fight if their combined strength beats the combined strength of the enemies in the area for the expected duration of the fight. To be verified: whether this happens now, and if not, to make sure it does, with a report of exactly which orders changed, how, and how lords in armies behave. Armies should still try to relieve lords if they are close enough to reach them before the fight is done and their combined strength is stronger than the combined strength of the enemies in the area, but should never break up because of this.
- **Before:** it did not happen. No order sent a lord to a friendly lord's fight. The one partial case was defend's strike, which attacks enemies near its own holding (within its radius, weighed with the lords bound for that holding or target), and so sometimes an enemy already fighting a lord of ours there. Every other order (patrol, garrison, recruit, sell, follow, hold, wait, raid, besiege's camp and the rest) left a lord in a fight alone.
- **Changed** (a third rule every order carries, in `Runner`, checked each hour before safety and food; the same for every realm's held lords):
  - `Runner.ReliefPlans` (new), once a realm's hour, for each battle in the field, at a siege camp or over a raid, with a lord of ours or an ally's on one side and an enemy realm's on the other:
    - **Its hours left** by the battle clock (`Forecast.Measured`, else `Forecast.Battle` at middling morale), at most `Library.MaxReliefHours` (24).
    - **Its riders:** every held lord who can get there in that time at his own pace (`CanRide`: out of an army or leading one, not in a fight, not camped in a siege, not inside a besieged castle or town, not on his way to buy food).
    - **Our side** (the parties in it on ours, each by itself, and every rider, an army leader with his army's strength) against **theirs** (the parties in it on theirs, and every enemy lord and army in view who could join within those hours, `World.Joiners`).
  - `Runner.Relief` (new): a rider whose nearest such fight is outweighed in our favour rides to strike the enemy side's leader (else its strongest party), leaving shelter if he was in it. The agent is told once a fight. In the fight the order waits; once it is over, no longer outweighed, or out of reach, the order resumes.
  - `ToolInstance.ReliefOf`, `ReliefTarget`: carried across save and load (CarryOn). The order in force reads "riding to relieve a fight against X (resumes once it is over)".
  - `Library.Text`: the agents' library says so (every realm's agents, the lieutenant's too).
  - **Armies:** the leader's ride moves the whole army, members attached as they were. Nothing detaches or disbands it. A lord marching in another's army does not ride by himself.
- **Checks:**
  - Smoke test 429 (new: in a made-up fight, a guard from inside its castle, a lord holding and an army's leader with his army ride to strike the enemy's leader. A lord in that army, one camped in a siege and one 28 hours away do not. None rides while the enemy with its joiners outweighs. Each goes back to his order once the fight is over).
  - The war room's and the dashboard's self-tests passed. RefCheck on the installed DLL against the game's, TAOM.Dependencies', SandBox's and SandBoxCore's bins: 269 game types and 816 member references, 0 missing.
- **On the way:**
  - The first `deploy.ps1` at 19:18 failed to build: the war room compiles `ToolDef.cs` without `Runner`, and the library text read `Runner.MaxReliefHours`. Nothing was installed (0.13.14 stayed), and the war rooms were started again. The constant moved to `Library.MaxReliefHours`.
  - The war room project was then built once with the war rooms running. The copy step failed on the locked `WarRoom.exe`, the running one untouched; the rule is to stop them first.
  - The second deploy, with the war rooms stopped, went through.
- **Installed:** 19:19, with the game closed. `BannerlordAgents.dll` sha256 `1FA917ABCB1A4BECE88EB91881040B708BDC88BF90A255F561ECCBB199DEE0D4`, SubModule v0.13.15. `docs/TOOLS.md` updated ("Three rules every order carries").
- **Not yet seen in the game:** that a held lord striking an enemy already in a battle joins that battle on our side. The game's encounter rules should make him join it; to watch in the run.

## Sunday, November 1

### 16:53 · Version 0.13.16 built: the lieutenant reads the whole chronicle, the report's recent history keeps the news, battle lines say who fought, the relief counts bands and wants time to spare; to be installed when the game is next closed · commit 21da25d
- **Nov 1:** the lieutenant said that it was not able to see what happened at Nanduhirion, and I asked for a diagnosis of why. Then: the fixes for the lieutenant, and the other proposed fixes as well, made and installed once the game is closed.
- **Why the lieutenant could not tell** (its call of Oct 30, 18:58, chat 33, "What happened at the siege of nanduhirion? Was it relieved by the dol guldur?"):
  - It answered "I can't confirm Dol Guldur helped". The notices had all reached the realm and its chronicle: Dáin, Náth, Lek, Isvan and Malthorn taken by Dramthak (Dol Guldur), Asta, Durin and Tyaska by three other Dol Guldur lords, Svedorn, Ask, Daeron and Iridi by orc lords, all at Autumn 21, 22:00.
  - Its report's RECENT HISTORY was the chronicle's last 25 lines of 48 hours. A day of order lines (sheltering, few recruits) filled it, and the cut fell inside that very hour: it kept "the siege was lifted" and "the army broke up", and dropped the battle and the twelve captures just before them.
  - The whole chronicle was `chronicle.txt` in its own folder; its instructions named only the look-ups and `conversation.txt`. It read `conversation.txt` looking for the answer.
  - The battle line itself read "Battle at Nanduhirion: Dáin II Ironfoot against Nanduhirion; Nanduhirion won": the side's leader was the town's garrison, and nothing named Dramthak's army or the twenty orc lords on it.
- **Changed:**
  - **The lieutenant** (`agent/lieutenant-system.template.md`, Look-ups and How you are asked; `LieutenantRoom.ChronicleIndex`): told of `chronicle.txt` and to read it around the day before answering that it cannot tell; each message says how many lines it has and at which line each of its last ten days begins, for the Read tool's offset. The installed copy (`C:\Users\karab\Projects\lieutenant\warroom-system.md`) is replaced at the install.
  - **The report's recent history** (`src/Common/RecentHistory.cs`, new, linked into the module and `BannerlordAgents.Routes`; `ChronicleBook.Recent`, `Views`, the war room's `Room.Gap`): the news of the last 48 hours first (every kind but `tool`, `order`, `order_ended`, `refused`), up to 40 lines; the orders' lines fill what is left of 25; whole in-game hours only, newest first, the newest hour of news always. For every realm's report, the agents' as well as the lieutenant's. The war room's "chronicle since the story" leaves out exactly the lines the report shows, by the same rule.
  - **Battle lines** (`Notices.WhoFought`, `SideText`): when either side was more than its leader, "Who fought: on X's side, ...; on Y's side, ...": the garrison, each realm's lords with up to three names and their men as they joined (`MapEventParty.HealthyManCountAtStart`), and other parties. **Captures** (`Notices.OnPrisonerTaken`) name the captor's realm: "taken prisoner by Dramthak (Dol Guldur)". `Text.List` (new) joins names.
  - **The relief rule** (`Runner.ReliefPlans`, `EndsIn` new, `World.BandsOf` new, `Library.ReliefSpare` new, `Library.Text`):
    - Osven's case (Winter 2): the clock had been measured (three hours in, Osven winning in about 9 by the 04:00 report); five more bands of Mirkwood Stalkers joined, 577 men by noon. `World.Joiners` counts enemy lords and armies only ("bandits do not join lords' battles"), so the bands were neither weighed nor run into the clock.
    - Now: the bands of the enemy's own kind in view who could join count when the enemy is bandits; the clock is run a second time with every enemy who could join coming in as he would arrive, and the shorter is used; a rider must get there with a fifth of the hours left to spare, at least an hour.
    - The first proposal ("until a fight's length has been measured, use a quicker estimate instead of the upper limit") read the case wrong: the clock was measured. The fix above is for what happened.
  - **The relief's wording** (`Runner.FightPlace`): "fighting Mirkwood Stalkers 2 north-west of Düglar-mauz", "at Brak-gash", "at the spot p673_994"; "the 16 of your lords who can reach it with time to spare"; "enemy lords, armies or bands".
  - **A lord in his own `lords` list** (`Orders`, the `def.Lords` loop): left out with a note in the receipt instead of the order refused. The orcs' council (poke 5807, Nov 1, 16:24) gave 13 lords each a besiege of Sirannon naming all 13 to wait for, and all 13 were refused "Hrokmaw is the lord being ordered." Each besiege waiting for the others is a fair use of the tool (its `lords` are only waited for), so the proposal to point them at one order for the leader was dropped. The council then gathered an army at Nanduhirion for Sirannon by itself (order #145).
  - **The fog check** (`Runner.Ids`): the ids an order's next orders name (`then`, nested) are known, as its own are. From Nov 1, 16:32 Dol Guldur's every report was flagged for `[castle_MM7]` in its own "Dramthak gather at Caradhras, then besiege [castle_MM7]"; Sirannon's id came from the orcs' joint plan jp7. A false alarm: not in my list of fixes, told to me with this build.
  - Version 0.13.16 (SubModule.xml, csproj).
- **Checks:**
  - Smoke test 433, all passed. New: the recent history keeps a battle with its 12 captures past 28 order lines and fills the rest by whole hours; an hour of 45 captures is shown whole. A relief does not ride against a band fight with four more bands near, and rides when they are far off; `ReliefSpare`; the relief's place words; the fog check knows a `then` order's ids.
  - The war room's self-test passed (built into the scratchpad, since the running war rooms lock their own folder). New: the lieutenant's chronicle line, its place in the message, and the instructions' text.
- **Not installed:** the game is running (my test since 16:19). To go in with the game closed, the war rooms stopped first.

### 17:14 · Version 0.13.16 installed · commit ba0a5f9
- **What:** the 0.13.16 build of 16:53 (commit 21da25d), unchanged.
- **When:** I closed the game at 17:12 (its last save, TEST CONTINUOUS SAVE at Winter 5, 1087, 22:00, wrote the orders in force down). The war rooms and the dashboard were stopped first (`scripts\agents.ps1 stop`, 5 processes), and started again after (17:13, detached; each waiting for its next poke or words).
- **Installed:** `scripts\deploy.ps1`: build, smoke test (433), war room and dashboard self-tests all passed. `BannerlordAgents.dll` sha256 `87BA751D020B502972EE0906E8F6290A1D9667AD6E96EB663D70C30DDA328401`, SubModule v0.13.16. RefCheck on the installed DLL against the game's, TAOM.Dependencies', SandBox's and SandBoxCore's bins: 269 game types and 817 member references, 0 missing.
- **Outside the repo:** `C:\Users\karab\Projects\lieutenant\warroom-system.md` replaced by `agent\lieutenant-system.template.md` (the Look-ups paragraph on `chronicle.txt`, and How you are asked); the old copy is `warroom-system.md.bak-20261101-before-chronicle`.
- **Seen in the session before it (0.13.15, 16:19 to 17:12):** the relief rule fired for Osven (16 riders, turned back; the bands that took him uncounted, the reason for this version) and for Kisha raiding Hrak-dum (up to 20 riders, 9,253 against 7,119 with Dramthak's army counted, on a measured clock of 62 hours capped at 24; more orc lords out of sight came to Vugra and the fight ended before any rider arrived; all turned back within 7 hours). On the orcs' side Ufthak joined Vrakdush's fight at Brak-gash (two orc lords on the winning side). Dol Guldur's Dramthak, about 7,000 strong, rode 17 hours to relieve Vugra (2,731) against Kisha (about 450): the rule does not ask whether the friend needs the help; told to me as a proposal (ride only while the friend's side would not win alone).
- **To watch in the game:** battle lines with "Who fought:", captures naming the captor's realm, the relief's "with time to spare" and bands counted, the lieutenant's "YOUR WHOLE CHRONICLE" line and whether it reads `chronicle.txt` when asked about the past; Dol Guldur's `[castle_MM7]` fog alarm gone.

### 17:19 · Version 0.13.17 built: a relief sends lords from other orders only when needed, and enough of them; lords on defend, patrol and recruiting always go · commit 411e85c
- **Nov 1** (on the proposal that a relief rides only while the friend's side would not win alone): I accepted the proposed fix on the relief rule. Beyond it, the logic should assign enough lords to tip the balance for relief, and lords that have a defend, patrol or recruit order (so lords that are basically not doing anything important) should still go to relieve another lord regardless of how the battle is going, since a crushing victory reduces losses compared to a close victory.
- **Changed** (`Runner.ReliefPlans`, `FreeToRide` new, `Library.ReliefWinBar` new = 1.5, `ReliefPlan.Free/Busy/Needed`, the call-back, `Library.Text`):
  - Nobody rides while our side, with every lord who can get there with time to spare, would not outweigh the enemy (as before).
  - Else every lord free to ride goes, however the fight goes: an order to defend, patrol, hold, recruit or rest, or a realm's own recruiting tool (any with "recruit" in its name: the orcs' and the lieutenant's recruit_round, home_recruit, safe_recruit). Garrison, sell, goto and every other order are not free.
  - Lords on other orders go only while the lords in the fight would not win alone (under 1.5 times the enemy with its joiners), nearest first, one already riding to that fight first (no flip-flop as others come in reach), until our side there is 1.5 times the enemy; the rest keep to their orders.
  - The call-back says why: "Your lords there should win alone; he goes so they win it bigger, with fewer losses", or "He leaves his order for it: your lords there would not win alone, and he is one of the N nearest lords sent from other orders to tip the balance". The trace's "relief" event carries free, busy, needed and free_to_ride.
  - The case: Dol Guldur's Dramthak (gather at Caradhras, then besiege Sirannon), about 7,000 strong, rode 17 hours to relieve Vugra (2,731) against Kisha (about 450) on Winter 4-5. Now a gather is not free to ride and Vugra won alone: he stays.
  - Version 0.13.17.
- **Checks:** smoke test 434, all passed (new: a defend lord rides to a fight his side would not win alone and the one goto lord nearest goes with him, enough for 1.5 times, the two farther ones do not; with the fight won alone, the defend lord still goes and no goto lord does; which orders are free). The war room's self-test passed (scratch build).

### 17:20 · Version 0.13.17 installed · commit b41301a
- **What:** the 0.13.17 build (commit 411e85c), unchanged, with the game closed (since 17:12).
- **Installed:** war rooms and dashboard stopped first (5 processes), `scripts\deploy.ps1`: smoke test (434), war room and dashboard self-tests all passed. `BannerlordAgents.dll` sha256 `CA942B6D883271000CE3B729053DDBD834D3A559A0B674A5F1478AE16D4FB477`, SubModule v0.13.17. RefCheck: 269 game types, 817 member references, 0 missing. War rooms and dashboard started again (17:20, detached).
- **To watch in the game:** relief call-backs saying "so they win it bigger" (a lord free to ride, the fight won alone) or "to tip the balance" (another order); no gather, garrison or other busy lord riding to a fight won alone; enough riders for 1.5 times the enemy.

### 17:44 · Version 0.13.18 built: a holding taken by storm no longer has its siege "lifted" · commit 37b13ca
- **Nov 1:** make the siege fix. (The record said "The siege of Felegoth was lifted; Aragorn had laid it" right before "Felegoth passed from Gundabad to Kingdom of Arnor and Gondor, taken by Aragorn", at Winter 5, when he stormed it.)
- **Why:** in 1.4.8 a storm won ends the siege before the holding changes hands. `KingdomManager.SiegeCompleted` removes the besiegers (`BesiegerCamp.RemoveAllSiegeParties`, which ends the siege and fires `OnSiegeEventEnded`), and only then calls `ChangeOwnerOfSettlementAction.ApplyBySiege`. So in `Notices.OnSiegeEnded` the holding was still its old owner's, and its test "taken: the change of hands says it" never held for a storm.
- **Changed** (`Notices.OnSiegeEnded`, `Flush` new, `OnOwnerChanged`, `Send`'s `hours`, `BridgeBehavior.OnTick`):
  - The "lifted" notice waits for the end of the game's step, kept by holding, with the hour of the siege's end.
  - The holding changing hands drops it (its own notice says who took it).
  - Each tick sends the ones left. With nothing waiting, the tick costs one count check.
- **Checks:** smoke test 434, all passed (no game objects in the smoke test, so the case itself is to be seen in the game: the next holding stormed).
- Version 0.13.18.

### 17:45 · Version 0.13.18 installed · commit d7ff3e7
- **What:** the 0.13.18 build (commit 37b13ca), unchanged, with the game closed.
- **Installed:** war rooms and dashboard stopped first (5 processes), `scripts\deploy.ps1`: smoke test (434), war room and dashboard self-tests all passed. `BannerlordAgents.dll` sha256 `B176FD48CAE5C9AB1117817F84A228EAD0FE00F7F15188EFFD4EC325C5060A6B`, SubModule v0.13.18. RefCheck: 269 game types, 817 member references, 0 missing. War rooms and dashboard started again (17:45, detached).
- **The watch** (outside the repo, Claude Code's session; Nov 1: the watchers should only wake Claude Code when the game is running, not all the time): the 10-minute watch now runs only while the game is up. While it is closed, one silent background check (every 30 s, no output) waits for `Bannerlord.BLSE.Launcher` and wakes the session once when it starts; the watch and the battle alarms then run until the alarms see the game close, and the silent check takes over again.

### 17:47 · Pushed to GitHub: versions 0.13.16 to 0.13.18
- **Nov 1:** push them now; I restart playing once that is done.
- **What:** the 12 commits from 21da25d (0.13.16) to 74872e4 (0.13.18's install entry), and this entry, to `origin/main` (github.com/Mertoghlip/bannerlord-agents, private).

### 18:31 · Version 0.13.19 built: the supply order, the garrison floor, food and garrison strength in the report, the lieutenant keeps unsent words · commit fce7910
- **Nov 1:**
  - I asked whether NPC lords can also be allowed to donate food to garrisons. I was seeing a lot of castles at my fronts with zero food due to raids on their villages, and agents and the lieutenant should be able to order lords to donate extra food to settlements to make sure they can outlast sieges. Then, on the two choices put to me (buy first; every realm's agents): agreed.
  - Orders should also get lords to go to their clan's settlements and take troops from those garrisons as they recruit, up until the strength of the garrison falls to a floor set by the agent.
  - What I type into the chat of the lieutenant should be persistent, meaning what I have written into the prompt menu should not be wiped when I close the lieutenant screen.
- **The game's rules read first** (1.4.8): `DefaultSettlementFoodModel` (lands, unraided villages, buildings; people prosperity / 40, garrison men / 20; food sold to a market +1 each; nothing under siege; stores at most 300, a castle 450, `FoodStocksUpperLimit`); `Fief.FoodStocks` settable; `PartiesBuyFoodCampaignBehavior` (towns and villages, `PartyFoodBuyingModel.FindItemToBuy` + `SellItemsAction`); `PartyBase.EstimatedStrength` worked out again when the roster changes.
- **Changed:**
  - **supply** (new move, `Tools/Library/supply.json`, verb `supply`, `Supply.cs` on the game's thread; `Orders`: `ToolMove`, `Possible`, `Apply`, `OnCourse`, the hour's step, `Describe`; `PacLink` shows it as VisitSettlement): fills a castle or town of the realm or an ally to `days` (10) of siege at most its stores; buys first when he carries too little beyond `keep_days` (3), at the least-detour market (town or unraided village, not an enemy's, within four town gaps), keeping half his gold and within his carrying room; hands over one food item per day's portion; ends supplied, needing nothing, nothing to bring, or the place besieged or lost.
  - **garrison_floor** (`Library.GarrisonFloor`, default 0, on every move and tool; `Library.Shared` now lists the four shared settings, described once in the library): a recruiting lord (the tour, a recruit stop) at a castle or town of his own clan takes healthy garrison men, highest tier first, by the recruit order's rules, while the garrison's strength stays at or above the floor (`RecruitTour.GarrisonGives`, `FromGarrison`, `Takes`); the tour counts such places as stops.
  - **Orders carry their settings** to the game's thread (`HandOrder.Settings`, each given one within its range, else the default) and across a reload (the carry-on file's `settings`; a save of 0.13.18 or before has none: the defaults).
  - **The report** (`SettlementRow.Food/FoodCap/FoodPerDay` in the hourly copy; `ViewHolding.GarrisonStrength/Food/FoodCap/FoodPerDay` for the realm's own castles and towns only): "garrison 450 (strength 600)" and "food 64 of 450, about 2 days under siege (it eats 32 a day)", with a line on how food comes and goes. Never an enemy's.
  - **The lieutenant's window** (`LieutenantWindow._draft`): what is typed and not sent is kept when it closes and put back when it opens; the hint says so.
  - Version 0.13.19.
- **Checks:** smoke test 436, all passed (new: the report's food and garrison strength for the realm's own and none for an enemy's; garrison_floor on every order and described once; the supply order in the library). The two checks the new parts changed were updated (21 orders, the holding line's strength). The game-thread parts (buying, handing over, taking from a garrison, the window) are to be seen in the game.

### 18:32 · Version 0.13.19 installed, and pushed with 0.13.19's commits · commit e7d2fa5
- **What:** the 0.13.19 build (commit fce7910), unchanged, with the game closed (since 18:25; its last save, TEST CONTINUOUS SAVE at Winter 14, 1087, 08:00, wrote the orders in force down).
- **Installed:** war rooms and dashboard stopped first (5 processes), `scripts\deploy.ps1`: smoke test (436), war room and dashboard self-tests all passed. `BannerlordAgents.dll` sha256 `B07746A4F0034B9D6823F862BF9826826785B6D990C3992D607E98FCE12E03B1`, SubModule v0.13.19. RefCheck: 271 game types, 834 member references, 0 missing (the food store, the stores' limit, the food-buying model, `SellItemsAction`, the item roster's counts, `InventoryCapacity`, `TotalWeightCarried`, the estimated strength). War rooms and dashboard started again (18:31, detached).
- **Pushed:** this entry and commits fce7910, 3d5a4ad to `origin/main` (I had asked for the last batch to be pushed once done).
- **To watch in the game:** supply orders (the market stop, "handed over N food at X"), garrison men taken ("from garrison" in the trace), the food line in YOUR HOLDINGS, the lieutenant's window keeping unsent words.

### 18:47 · Version 0.13.20 built: the hunger warning, food in the order language, doctrine principle 19, and the realm agents' instructions brought up to date · commit f72c774
- **Nov 1** (my question: do the agents' system prompts and the lieutenant's prompt contain instructions for these orders, and do they have the needed context; on the three fixes and three extras proposed): I accepted the fix proposals and all the optional extras, to be implemented as well.
- **What was found:**
  - The order library (`tools.txt`, written by the bridge at every load, `AgentChannel.WriteLibrary`, and put into every call's system prompt by the war room) carries the supply order and `garrison_floor` from the next load.
  - The report carries food and garrison strength (0.13.19).
  - The lieutenant needs nothing more: it acts on the ruler's words, from the library.
  - The realm template's own lines were out of date (the moves list, the shared settings, Recruiting).
- **Changed:**
  - **The realm agents' template** (`agent/warroom-system.template.md`):
    - Writing your own tools: `garrison_floor` among the shared settings, `supply at X` among the moves, `food_at(h)` and `food_days_at(h)` among the functions.
    - Recruiting: the garrison floor.
    - Doctrine principle 19, "Victual your castles before the siege" (Vegetius IV.7; Calais 1347, Rouen 1419, Kenilworth 1266, Harlech 1295; in the doctrine's own words, naming nothing of this war). Its study is in `docs/DOCTRINE.md`, which now says 19 principles.
  - **The hunger warning** (`Brain.LowFood`, `Memory.HungerTold`, `LowFoodDays` 3, `LowFoodNear` 40, `LowFoodEvery` 24): a `low_food` poke for an own castle or town not besieged, short of food for a siege, with an enemy lord or army in view near it.
  - **The order language:** `food_at`, `food_days_at` (`ToolDef.Functions`/`FunctionWords`, `Runner`'s scope), for own holdings only.
  - Version 0.13.20.
- **Checks:** smoke test 438, all passed. New:
  - The warning fires for a castle with 1 day of food and Kralza 20 away. It is not told again the next hour, is told again once the food falls a day more, and stays silent with the enemy far off.
  - An agent-style tool reading `food_days_at(at)` and `food_at(at)` moves on a castle short of food.

### 18:47 · Version 0.13.20 installed, the realm agents' instructions replaced, pushed · commit 7f32596
- **What:** the 0.13.20 build (commit f72c774), unchanged, with the game closed.
- **Installed:**
  - War rooms and dashboard stopped first (5 processes).
  - `scripts\deploy.ps1`: smoke test (438), war room and dashboard self-tests all passed.
  - `BannerlordAgents.dll` sha256 `226B3356EA0C4DAF5D1AB8FDB195A66FCB7AE649A39FD4DAEAB3D1B3BAF2B5B6`, SubModule v0.13.20.
  - RefCheck: 271 game types, 834 member references, 0 missing.
  - War rooms and dashboard started again (18:47, detached). Each reads its instructions afresh at its next call.
- **Outside the repo:** each realm agent's `warroom-system.md` (`C:\Users\karab\Projects\orc-war-council`, `dolguldur-war-council`, `kingdom-war-council`) was rebuilt from the new template.
  - Each keeps its own two realm lines (the title and the opening line): the orcs' copy is the template word for word, and the other two differ only there, as before.
  - Eight lines changed in each, CRLF kept. The old copies are `warroom-system.md.bak-20261101-before-supply`.
  - The lieutenant's `warroom-system.md` is unchanged.
- **The order libraries** (`tools.txt`) in the agents' channels are rewritten by the bridge at the next load, with the supply order, `garrison_floor` and the food functions.
- **Pushed:** this entry, f72c774 and b7e9ecd to `origin/main`.

### 19:23 · Version 0.13.21 built: food coming and each holding's clan in the report, the lords-eat-their-own rule, no hunger warning for full stores · commit dc1a8b2
- **Nov 1:**
  - Asking why Zulgora's army left Gwígar: the castle actually had food, since it had already been restocked, and I asked why it did not see that.
  - The lieutenant before that, on Oropher's loop with `garrison_floor` 600: "The game only lets him do this in your clan's own holdings; the report does not show me which ones those are."
- **What happened** (Dol Guldur's call on poke 2344, report of Winter 16, 10:00, answered after 79 s; its order took effect about Winter 17, 06:00):
  - The report showed Gwígar at "food 0 of 450", with Dûrnia "bringing food to Gwígar, there in about 7 hours", but not how much. Dûrnia handed over 219 at about 17:00 (`supplied`, h 2193281).
  - The agent wondered whether lords inside eat the castle's food, and planned as if Gwígar would stay empty: "Gwígar has no food to shelter his 17 lords". It fell back to Westoft, and Aragorn caught it.
  - In 1.4.8 a lord's party eats only what it carries (`DoesPartyConsumeFood`: lords yes, garrisons no). A town's market sells to parties inside every hour while it has food, besieged or not (`PartiesBuyFoodCampaignBehavior.HourlyTickParty`). A castle has none.
- **Changed:**
  - **YOUR HOLDINGS** (`Views.FoodComing`, `ViewHolding.Clan/FoodComing`; `SettlementRow.OwnerClan/OwnerClanName`; `PartyRow.FoodItems/FoodEats` for own lords):
    - Each own castle or town shows its clan: "(castle of House X)".
    - It shows the food coming with each supply order to it: "food coming: Dûrnia with about 219 (about 9 days more), there about …". When he still buys first: "buying food on the way first, with up to N".
    - The food note adds that lords inside eat their own food, that a town's market sells to them, that a castle has none, and that the owning clan is what `garrison_floor` goes by.
  - **The hunger warning** (`Brain.LowFood`) is not given for stores too full to take another day's food. Caras Galadhon, 800 of 800, eating 284 a day, had been warned.
  - **`garrison_floor`'s description** (`Library.GarrisonFloor`) and the realm template's Recruiting line now say where the clans are shown.
  - Version 0.13.21.
- **Checks:** smoke test 439, all passed. New: a castle with 0 food and a supply lord carrying 225 (3 days of 2 kept) reads "(castle of House of Durbag)" and "food coming: Durnia with about 219 (about 9 days more)", with the rule. A full store gives no warning.
- **Not done, waiting for me:**
  - the late-answer warning (what changed at the places an answer's orders name while it was being written);
  - the "can they cut me off" check in the race question.

### 19:24 · Version 0.13.21 installed after the Zulgora battle crash, realm instructions updated, pushed · commit 9ebc2e5
- **The crash first** (TFAOMFixes' to fix, not ours; told to its session with the bundle):
  - My battle against Dol Guldur's Zulgora (about 6,000 soldiers and horses) hung at Winter 17, 19:18 real time. TAOM caught an AccessViolationException in `Agent.ParallelUpdateCachedAndFormationValuesForAIAgent` (bundle `taom_crash_20261102_031828_653c8d22.zip`).
  - The game's log had three "as_spider does not contain act_warg_attack_running" lines less than a second before the first engine fault (+0x6f909d reading 0x25c0, then +0x5f132d reading 0x88, the same pair as Oct 29 23:18). The main thread then spun at TaleWorlds.Native+0x1f3344 (our freeze probe at 19:18:50).
  - I closed the game at 19:19:34. The session had not saved: the run carries on from TEST CONTINUOUS SAVE, Winter 14, 08:00.
- **Installed:**
  - With the game closed, war rooms and dashboard stopped first.
  - `scripts\deploy.ps1`: smoke test (439), war room and dashboard self-tests all passed.
  - `BannerlordAgents.dll` sha256 `73A72475F3F72E9E4F9CB5CE9CA75CEEBAF66BF6D978EF8A779CB159CBD0DE82`, SubModule v0.13.21.
  - RefCheck: 271 game types, 834 member references, 0 missing.
  - War rooms and dashboard started again (19:24, detached).
- **Outside the repo:** the three realm agents' `warroom-system.md` rebuilt from the template as before (each keeps its title and opening line). Two lines changed in each: the Recruiting line on clans. The old copies are `.bak-20261101-before-clans`.
- **Pushed:** this entry with dc1a8b2 and 3830934.

### 20:30 · Version 0.13.22 built: the late-answer warning, the cut-off check in the race question, the tool language for the lieutenant · commit f591aae
- **Nov 1**, on the two proposals left from the Zulgora analysis and the lieutenant's gap: implement both ideas, and fix the gap for the lieutenant as well.
- **The late answer** (`LateAnswer.cs`, new; `Channel`'s `LateAnswer.Book`; `Brain.Poke` passes each poke's view):
  - Measured first in the channels' receipts (last 400 orders each): answers took effect a median 6 in-game hours after their poke for the orcs (one in ten 18 or more, the most 33) and 8 for Dol Guldur (one in ten 24 or more, the most 52).
  - When an answer's orders took effect 6 hours or more after its poke (`LateAnswer.Hours`), the next poke's WHAT CHANGED SINCE YOUR LAST ANSWER opens with it: "Your answer to poke #N took effect late: it answered the report of ..., and its orders took effect ..., 18 in-game hours later. Since that report, at the places and lords those orders named:", then a line for each.
    - A holding: its owner, siege or raid, garrison, food, food coming, the nearest enemy in view.
    - An enemy lord or army: in view or not, its men, what it does, where.
    - A lord of the realm: only his men and capture, never his own moves (the orders' doing).
  - It covers the next orders (`then`) too, and lords named by name. It is told once per answer, the newest two, and only when something changed.
  - A council's answers are left out (they answer their own briefing). A next order's later start does not count as the answer taking effect.
  - Each channel keeps the notes of its newest 30 pokes (`LateAnswer.PokesKept`), copied, since the realm's memory changes its sightings in place.
  - The trace gets `late` events, plus the counter `agent:late answer told`.
- **Can they cut the way** (`src/BannerlordAgents.WarRoom/CutOff.cs`, new, in `ask_the_way`):
  - The check: for each lord or army of the other side in `also_from`, the first point of the way it could reach before the one asked about passes there ("could be on the way about 48 along, 12 west of Bree [p68_30], by about ...: 4 hours before Bolg would pass there ... From there on, Bolg could not keep to this way without meeting it"), or "could reach no point of the way".
  - How: the asked army's hourly places along its way, at most 24 (`CutPoints`). A point out of reach even at 1.3 times the other's open-ground pace is skipped without a search. It is facts only, like the rest of the tool.
  - The tool's description, and its `also_from`, say so. So do the realm template's Asking the way and the lieutenant's.
- **The lieutenant's tool language** (`agent/lieutenant-system.template.md`, new section The tool language):
  - It carries the form, takes, settings, init, states, moves, values, names, functions and limits as the realm agents have them, with a tool's call-backs told in the ruler's window.
  - Until now it was only told to copy the library's tools. `ToolDef.FunctionWords`/`VerbWords` are printed nowhere, so it knew nothing of words such as `food_days_at`.
  - The realm template's lists gain the two words they had missed: `army_joining` (since gather) and `ruler_target()` (0.13.11).
  - The realm template's poke section says what the late-answer lines are.
- **Docs:** ROUTES.md (the cut), TOOLS.md (the late answer), LIEUTENANT.md (the tool language).
- Version 0.13.22.
- **Checks:**
  - **Smoke test 442, all passed.** New:
    - an answer that took effect 18 hours after its report is told once, with a castle's food 0 then 219, its lord's 300 men then 200, and the enemy its next order named marching on Orc Keep, never a lord it did not name; an answer on time, or one where nothing it named changed, is not told;
    - both templates list every move, name and function of the language (`ToolDef.Verbs`, `Functions`, `BuiltIns`).
  - **War room self-test, all passed.** New: on the made-up map, Hirgon standing on Bolg's road east can be on it about 7 along an hour before him, and Aragorn's slow army near its end about 48 along; nobody can reach the pass road south first.

### 20:32 · Version 0.13.22 installed, the realm agents' and the lieutenant's instructions replaced, pushed · commit 8b86b36
- **What:** the 0.13.22 build (commit f591aae), unchanged, with the game closed.
- **Installed:**
  - War rooms and dashboard stopped first (5 processes).
  - `scripts\deploy.ps1`: smoke test (442), war room and dashboard self-tests all passed.
  - `BannerlordAgents.dll` sha256 `B1680E26F72FDB4AEAFDB91446030CC5CE853C0FF95120F86565D20395D4831D`, SubModule v0.13.22.
  - RefCheck: 271 game types, 834 member references, 0 missing.
  - War rooms and dashboard started again (20:32, detached). Each reads its instructions afresh at its next call, and serves the new cut-off check from its new build.
- **Outside the repo:**
  - Each realm agent's `warroom-system.md` (`orc-war-council`, `dolguldur-war-council`, `kingdom-war-council`) rebuilt from the template, keeping its title and opening line. Four lines changed in each (the poke section, the names, the functions, Asking the way), CRLF kept; the old copies are `warroom-system.md.bak-20261101-before-late-answer`.
  - The lieutenant's `warroom-system.md` (`C:\Users\karab\Projects\lieutenant`) replaced by its template (The tool language, Asking the way); the old copy is `warroom-system.md.bak-20261101-before-language`.
- **Pushed:** this entry with f591aae and the follow-up naming it.

## 2026-11-02

### 00:08 · Version 0.13.23 built and installed: the late note names no id its report no longer shows; the game killed for the battle helper's freeze fix, pushed · commit 6e7cfcc
- **The fog check flagged two late notes** (Nov 1, the orcs' pokes #6258 and #6295): "Osven [lord_2_121_party_1]: no longer in your report." and the same for Silivren. Both were lords of mine that an order had named, captured meanwhile. The agent had seen those ids in the report it answered, so nothing it could not know reached it, but a report names only the ids it shows.
- **Changed** (`LateAnswer.For`): each line names the place or lord by the id the newest report shows (a captured lord's own, not his party's), and with no id at all when it has gone from the report. Version 0.13.23.
- **Checks:** smoke test 443, all passed (new: Baranthir gone from the newest report reads "- Baranthir: no longer in your report." with neither of his ids). War room and dashboard self-tests passed. RefCheck: 271 game types, 834 member references, 0 missing.
- **Installed:** `BannerlordAgents.dll` sha256 `CD629B6A576B9A590537907590442A539F578B2709099FE6E89220CD4D5BAD2E`, SubModule v0.13.23. War rooms and dashboard stopped first and started again at 00:08, detached. No instructions changed.
- **The game was killed at my word** (00:07): it froze at Nov 1 23:54:53 in my field battle against Wruk (Gundabad), the main thread looping at TaleWorlds.Native+0x605aa8 with no worker busy. The TFAOMFixes session read it live: the soldier row's "first place in use" helper (+0x59ea00, a leaf .pdata does not list) still read the high-water through the container base at the stock offset `[rcx+62ACF4h]`, so with place 0 free the walk bounced between 0 and -1 for ever. Its fix is one more soldier-ceiling edit (1,580 -> 1,581 in every table, installed 00:06 as data files; boot log "soldier ceiling: applied 1581 edits"). The last save, TEST CONTINUOUS SAVE at 23:46, is just before that battle, so only that battle is lost.
- **Pushed:** this entry with the build commit and the follow-up naming it.

### 13:51–14:16 · Run A (20261029-1903-A) finished, all 60 days: counts (with its marks) · (no install)
- I loaded TEST CONTINUOUS SAVE (Nov 1 23:46) on 0.13.23 with the battle helper's soldier table v6 ("soldier ceiling: applied 1581 edits"; the warg, slot and ladder guards on). 131 orders carried on, 0 not. The Wruk battle that froze at Nov 1 23:54 played through: 23,294 frames at 60 fps, up to 2,619 soldiers, no fault, 48 stale creature wrappers replaced. The run ended at 14:16:51 and the game closed cleanly (no errors in the session summary).
- The run in all: 91 real hours, 16 loads, versions 0.13.4 to 0.13.23 installed along the way, 19 marks (cheat mode, crash reloads, the day-36 replay). My kingdom +3 towns, +4 castles, +19 villages, 189 enemy lords taken (89 held at the end); the orcs −1 town −1 castle, Dol Guldur −1 town −3 castles. Agents at list price: orcs $228.30, Dol Guldur $175.53, the lieutenant $47.43. `Runs.exe report` wrote `runs\20261029-1903-A\report.md`; `Runs.exe compare` wrote `runs\compare.md`.
- **Found in the final read, still open:** none of Dol Guldur's 3 diplomatic acts went through (the Sand-Riders' hire refused by the game at −79,259; its two votes, Gwígar's owner and the Rhûn trade agreement, answered 13 and 14.6 in-game hours after they opened, when a vote is held 12); the bundle's check misses each realm's first look (mistymountainorcs #5453, dolguldur #1963, written at 19:12:23 without the run's tag); the run report has no crash mark for Oct 29 22:30 (TAOM's crash window let the game close normally); 82 order_stuck alarms are real (lords sent into a holding just put under siege stop outside it).

### 14:52 · Python 3.12.10 set up on the D: drive, for the training work (outside the repository) · (no commit of its own)
- I asked to set up Python on the D: drive. The python.org installer (`python-3.12.10-amd64.exe`, sha256 `67b5635e80ea5107…`, Authenticode signed by the Python Software Foundation) installed per user, quietly, to `D:\Python\Python312` (PATH: my user PATH now starts with it, ahead of the Microsoft Store stub; the `py` launcher per user). 3.12 is the newest line every training library ships wheels for on Windows.
- The project's environment `D:\ml\venv` (pip 26.3), with `D:\ml\cache\pip` and `D:\ml\cache\huggingface` as the download caches (user variables `PIP_CACHE_DIR` and `HF_HOME`) and `D:\ml\models` for model files, so nothing large lands on C: (45 GB free of 952). No libraries installed yet: which ones depends on the model size and the serving choice.
- Windows' long-path setting is off (`LongPathsEnabled` 0); the short `D:\ml` paths keep clear of it. Turning it on needs an administrator.

### 15:10–15:29 · The game's graphics-card and memory use measured, to size the local model (outside the repository) · (no install)
- I asked for the measurement for the campaign map. A logger from outside the game (scratchpad `gpu-logger.ps1`: nvidia-smi, Windows' GPU and memory counters, the bridge's health.csv for the state; nothing in the game changed) every 3 s, written to `D:\ml\measure\game-gpu-20261102-151026.csv` (262 samples). Browsers closed; VS Code, the four war rooms and Steam open. RTX 5090 Laptop, 24,463 MB.
- **Whole card in use** (min / median / top): nothing running 1.6 GB; the campaign map before a battle 8.6 / 9.4 / 9.6 GB (top in a town menu), 60 fps (3,500–3,600 frames a minute), the card 42% busy at ~75 W; a 5,001-soldier siege 10.0 / 10.6 / 11.0 GB, 28 fps, 31% busy at ~55 W; the map after it 8.3 / 8.5 / 8.7 GB (two minutes). Free at the top: map 14.9 GB, battle 13.5 GB. Windows' per-process figure overcounts (the desktop at 3,522 MB on a card holding 1,743 MB), so the whole-card figure is the one used.
- **The battle's memory comes back:** 10.6 GB held through the results screen, then 5.7 GB at once ~17 s after the battle ended, the map reloaded at ~8.4 GB.
- **System memory:** the game 16.1 GB on the map before the battle, 20.7 GB in it, 18.1 GB on the map after (2 GB not handed back); free 8.5 / 6.7 / 6.4 GB; commit 79 / 84 / 81%.
- Reading for the model choice (I chose a ~20B mixture model trained on a rented card): gpt-oss-20b (~12–13 GB) fits beside the campaign map with little to spare, and only off the card during battles (reload from D: after each); a 35B mixture at ~3 bits does not fit; a 14B ordinary model at 4 bits (~9 GB) could stay on the card through battles.

## Tuesday, November 3

### 20:35 · Install, version 0.14.0: a news wake's context, pinpoint by the clock of the news · commit d7c89a8
- **Why:** Nov 2: what is relevant to this specific piece of news should be given, and the rest should be queryable; the reach is set by the "clock" for the specific new development. My other answers: a first look keeps the whole report; tool writing moves to councils only; a letter's theatre is the places it names. Plan and decisions: `docs/PLAN-news.md`.
- **What, game side:**
  - `Theatre`: a poke's focuses, their clocks and horizons (6 hours to 3 days, a day with no clock), the lords marked against the clock with the agent's measured answer time, and who else can be there in time.
  - `Views.NewsReport`: the news report, with THE CLOCK and THE REST OF THE WAR (regions, every lord by name and id). The whole report's text is unchanged; its lines moved into shared functions.
  - `Views.Book`: the question book, `book\poke-N.json`, fog-checked and counted as shown. `Facts.EnemyHoldings`: look-ups for enemy castles and towns, which had none.
  - Clock items in the view. Captures and broken armies now carry a party in their notice.
  - `agent.txt`'s `news`: full, shadow or theatre.
- **What, war room:** `look_up`, `list` and `chronicle` (`BookTools.cs`); the news instructions (`agent/warroom-news.template.md`, each section replacing the same one); notes and chronicle lines cut to the news; tools written in a news answer not kept, and the next council told; no Read and no `check_tool` in a news wake. `replay --news`. Councils unchanged.
- **Mode now:** shadow, so the agents still get the whole report and the instructions as before; only the record grows. Switching to theatre is one line in `agent.txt`.
- **Tests:** smoke test 457 checks, war room self-test, dashboard self-test, all passed; references 834, missing 0 (run just after the install, not before: the first try had a wrong path). Speed test on the run's last saved hours, 95th of a hundred: full 810 ms, theatre 643 ms, budget 1,000 ms.
- **Measures (`docs/reports/2026-11-03-news-context.md`), from run 20261029-1903-A's recordings:**
  - Report 35% (Dol Guldur) and 41% (orcs) of the whole one. 1,604 wakes rebuilt, no id beyond the whole report.
  - Of what the orders named, 57% and 60% in the theatre in full, 95% named at all. **The plan's bar of about 95% in the theatre is not met:** the agent runs the whole war from news wakes, and 70% of the lords missed lay over a day beyond the horizon. The tests added the roster, the short orders, the nearest refuges, a day's floor for a passing enemy, a cap of 15 others, and an enemy's arrival at any holding as its clock.
  - Twelve replays with real calls: first read 62,040 to 35,806 tokens (the plan said 15,000 to 20,000; the fixed part alone is about 24,500), median time 23.7 to 23.3 s, total time 16% up, questions 25 to 43 (over the plan's limit, with no way-finding questions or look-up pages in the replays); decisions: 5 the same, 3 the same at the core with more added, 2 the same main move, 2 different.
- **Found, not fixed (still to decide):** `Room.Section` reads each section to the end of the report, since the report's lines end in \r\n. So 375 of Dol Guldur's 375 answers and 280 of the orcs' 394 counted as urgent, and 47 of 142 councils were called by words in the news or the report, some of them old chronicle lines. Left as it is so the theatre's test changes one thing at a time.
- **Outside the repository:** `warroom-news.md` installed in `orc-war-council`, `dolguldur-war-council` and `kingdom-war-council`, each `agent.txt` given `news = shadow`. War rooms and dashboard stopped for the builds and started again.
- **Still to do:** shadow in play, then a both-sides test run in theatre mode.

### 21:03 · War room 0.14.1: a report's sections read to their end, not the report's; the news context measured · commit b838f99
- **Why:** fix the bug, and report by how much we were able to reduce the context.
- **The fix:** `Room.Section` looked for \n\n in text whose lines end in \r\n, so it read each section to the end of the report. It now stops at the section's blank line, whichever way the lines end. Two new self-test checks; war room self-test (100 checks) and smoke test passed.
- **What it changes, in every news mode:** urgency and a council's words are read from the news only. On the run's 768 written answers, urgent falls from 654 to 61, so most pokes take the 3,000 thinking cap, not 5,000. Of the 142 councils, 31 had a dated chronicle line as their reason and need not have sat. Runs from now on differ from run A in this, beside the news report.
- **No game files changed:** the module stays 0.14.0. War rooms and dashboard stopped for the build and started again.
- **The measure (`docs/reports/2026-11-03-news-context.md`):** a news wake's first read falls 37%, from a median 64,504 to about 40,500 tokens, over all 768 answers: report 59% to 65% less, memory 16% to 60% less, instructions and tools 16% to 18% less. With the councils, unchanged, all first reads fall 31% (60.7M to 42.1M). Only in theatre mode; the agents are on shadow, so none of it is live yet. Reckoned from recordings, and checked within 240 tokens on five real calls.
- **Corrects the 20:35 entry:** its 35,806 and "about 24,500 fixed" came from replays without the way-finding section and tools; with them, about 38,500 on those twelve, and 24,000 to 26,000 fixed.
- **Also:** `replay --campaign` and `--lookups` (a replay with the whole tool set or the Read tool, to measure a call's size), `SmokeTest --coverage --sizes`.

### 21:35 · The repository's wording in my own voice; the Claude notes kept local · commits 40d7f46 and bfdac71
- **Why:** lines such as "for review by the author" in the third person read like an assistant's notes, not a project's.
- **The wording (40d7f46):** the change log, the docs and the code comments now read first person where a person matters, with no attribution elsewhere: about 640 lines in 70 files. Dates, numbers, hashes, ids and links are kept. Quoted requests became plain statements of what was wanted.
- **Checks:** no code changed. Compared with the last commit, all 177 changed C# lines differ only in a comment; the other four are a help block, an XML comment and one test description. Nothing under `agent/` changed. Smoke test 457 checks, war room self-test 100, dashboard self-test, all passed. No install: the module's behaviour is unchanged.
- **The Claude notes (bfdac71):** `CLAUDE.md` and `agent/CLAUDE.template.md` are out of the repository and ignored by git; they stay on this laptop, and Claude Code still reads `CLAUDE.md` here. `src/BannerlordAgents.WarRoom/Claude.cs` stays: it is source code the war room needs. Four pointers to the removed files were dropped from the docs and one comment.
- **Left as they were:** `docs/proposal/` (dated snapshots, which also state that Claude Code helps with implementation, as the course allows), the commit history and its `Co-Authored-By` lines, and every other mention of Claude Code.

### 21:44 · The history rewritten; the engine notes in the docs · commit d944db5 (force-pushed)
- **The history:** `CLAUDE.md` and `agent/CLAUDE.template.md` are out of all 212 commits, and 40 commit messages are reworded in the first person. Authors, dates and `Co-Authored-By` lines are unchanged. Every commit has a new id; this log's 119 citations now give the new ones.
- **Checks:** commit by commit against the old history, the only files that differ are those two; authors, emails and dates are identical; the latest tree is byte for byte the same.
- **Not cleaned:** old versions of the docs and comments, in earlier commits, still have their old wording. Only the latest version is rewritten.
- **The way back:** `D:\BannerlordAgents-backup\repo-history-rewrite-20261103\` holds a bundle of the old history, the old-to-new id map (`map.txt`) and copies of the two files as they stood. Run records and notes written before today may cite old ids; `map.txt` translates them.
- **Other copies:** any other clone of this repository needs cloning afresh.
- **The engine notes:** moved word for word from the local `CLAUDE.md` to `docs/ENGINE-NOTES.md`. `CLAUDE.md` keeps my rules, the build notes and the code map, and points to them.

## Friday, November 6

### 14:58 · Install, version 0.14.2: supply and recruit orders start their next order · commit 3f9c8eb
- **Why:** the news wake study of run A found that a supply or recruit order's next order (`then`) never started on its own: 0 of 92. Those two end on the game's side, and only a tool that ended by its own rules started its next order. The lord went back to the game's AI until a later answer ordered him again; 5 of the 70 supply lords got no new order for 48 in-game hours.
- **How those 92 ended:** 70 supply orders handed over their food; 18 recruit orders ran out of gold and 4 hit their wage cap, at between 14% and 82% of their ranks. All 92 would now go on to their next order. Next orders are mostly `defend` (47), `home_recruit` (23) and `garrison` (17). In 52 of the 92 the later answer gave the same kind of order anyway.
- **The fix:**
  - A supply order goes on when the food is in or the place needs none. It doesn't go on when the place was besieged or lost first, or when the lord had nothing to bring, since the next order assumed it worked.
  - A recruit order goes on at every end, including when the lord can't pay for more: the order was done as far as it could be.
  - The news of the end now says "its next order starts" or "so its next order does not start". The agent is still told and woken as before, and can replace the next order. Whether that wake is needed is for the wake rule to settle.
  - If the next order is refused by the time it starts, the agent hears that as news. A lord who has been given a newer order in the meantime keeps that one.
  - The run's record says "done, next order" for these, as for any other tool. An order carried across a save keeps its next order.
  - The agent's descriptions of `supply` and `recruit` say when the next order starts, and `recruit` now also says it can end for lack of money.
- **Checks:** smoke test 467 checks, 10 of them new, war room and dashboard self-tests all passed. The tests cover the part that decides what starts and when. The part that runs inside the game (`Orders.MoveThen`, `Orders.EndMove`) can't run in the smoke test and hasn't run in play yet; the next test run is its first real test.
- **Install:** module 0.14.2 into the game, with the game closed. War rooms and dashboard stopped for the build and started again; the war rooms themselves did not change in behaviour.

### 15:14 · The news wake study in the reports folder · commit 58b2e32
- **What:** `docs/reports/2026-11-06-news-wake-study.md`: which news wakes are needless and what each kind needs, from run A's 744 fast answers, 4,611 older answers and 73 new replays (about $24), plus the new design. Claude Code ran the analysis and replays and drafted it. Its figures match the study's working page number for number; only the wording and the design section changed.
- **What I decided:**
  - The wake rule goes straight in, on by default from now on, with no log-only step first.
  - Test runs only of the finished version: first with the vanilla rules, then with the agent able to edit them and write its own.
  - Any answer can change rules. 700 strength and the 6-hour wait are starting settings the agent can edit. First look, the council clock, marked letters, a lord taken, an own holding besieged or lost, and votes about to close stay locked.
  - The lists of lords with no order and chances to strike stay in the design.
- **Found while planning:** the war room checks whether a council is due only when a wake arrives, and reads the big-event words only after a fast answer. With fewer wakes, councils would slip, so the wake rule has to wake the agent when one is due.
- **Not built yet:** everything except the follow-up fix (0.14.2). No install; the game, the agents and the code didn't change.

### 15:52 · Install, version 0.15.0: the wake rule on by default, letter marks, the news report on, the two short lists, the council clock; agents for Rhun and Gundabad · commit e5e165e
- **Why:** the design from the news wake study ([report](reports/2026-11-06-news-wake-study.md)), as the finished version for the first test run. The agents can't edit the rules yet: that is the second test. I play the Kingdom of Arnor and Gondor in every test, and Rhun's and Gundabad's lords were already in the war in my last test.
- **The wake rule:** the game sorts every line of news before any call, into wake now, wait up to 6 in-game hours, or digest (never wakes, rides the next call). The rules are data (`src/BannerlordAgents/Wake/wake-rules.json`), read over each line's facts. On by default (`wake = rule` in `agent.txt`, `all` for the old way). No log-only step first, by decision. While a poke has no answer the news waits, at most 12 in-game hours, so the next report is fresh.
- **Letter marks:** a letter with `"urgent": true` wakes its reader at once; others wait. The instructions say when to mark one, and not to write only to acknowledge or thank.
- **The news report on:** every realm agent is on `news = theatre`. A wake with a letter reads the whole report (the Nov 3 decision that a letter's theatre is the places it names is replaced: the study's replays saved 13% on a small report and took twice as long).
- **The two short lists:** LORDS WITH NO ORDER and CHANCES TO STRIKE in every report of a news wake and council. Fights near one holding are one line, measured together; at most 5 chances; reach in hours of march.
- **Too poor to recruit:** a lord's gold shows beside him when it is within a fifth of the game's floor for recruiting.
- **Councils:** the game writes the hour into the channel every hour (`clock.txt`), so a council sits on time with no wake and no extra fast answer. A council reads the lines held back, which then no longer wake the agent. Three lords stuck in one wake's news, or a fast answer that runs out of time, also call one.
- **The record:** `wake_<realm>.jsonl` in the campaign's data folder holds each line's kind, lane, rule and facts, each wake and why it came, and what each council took up.
- **Checks:**
  - `SmokeTest --wake-check` ran all 3,465 recorded news lines of run A through the code: every one went in the same lane as the study's script, no differences. Letters all count as unmarked in that check.
  - The two lists on run A's 1,604 recorded wakes: chances 0.6 a wake for the Orcs and 0.1 for Dol Guldur, at most 5; lords with no order 7.2 and 4.1, at most 13 shown; together 890 and 381 characters on average, at most 2,823. #6301 lists Celeborn and Morznak; #6248, 16 lines before, gives one.
  - Smoke test 501 checks, 34 of them new, war room and dashboard self-tests all passed. The reference check reports 834 game members, 0 missing.
  - **Not tested:** nothing here has run in the game, and no real model has read the new instructions. Wait times and call counts are still the study's estimates.
- **Outside the repository:**
  - New agents: `C:\Users\karab\Projects\rhun-war-council` (`khuzait`) and `C:\Users\karab\Projects\gundabad-war-council` (`gundabad`), live, fresh from the templates, in the bridge's `agents` list and `run_rivals`.
  - `warroom-system.md` and `warroom-news.md` in `orc-war-council`, `dolguldur-war-council` and `kingdom-war-council` have the same new text as the templates, with copies kept as `.bak-20261106-before-wake-rule`. Each `agent.txt` has `news = theatre` and `wake = rule`.
  - `scripts\arm.ps1` sets the two new agents with the other rivals. The modes now are arm A.
  - The study's scripts and working files are in `D:\BannerlordAgents-backup\news-wake-study-20261106` (239 MB), to reuse on the new runs.
  - War rooms and dashboard stopped for the build and started again, six war rooms now. The game was closed.
- **What to expect:** the study's figures are for two agents, so with four, about twice the calls and cost. Runs from now on are not like for like with run A: several things changed at once, and two more realms have agents.

### 21:06 · Setup: Rhun and Gundabad put off to the last test · commit 40437bb
- **Why:** the tests should measure the wake rule and the context on the same two rival realms as run A. Agents for Rhun and Gundabad are the last step, once the tools and the shaping of the context are final. This replaces the 15:52 entry's setup and its "what to expect".
- **What changed in the repository:** `scripts\arm.ps1` no longer sets the two; `docs/RUNS.md` and the [study report](reports/2026-11-06-news-wake-study.md) have the new setup and the last test as step 7. No game code changed, so nothing was installed: version 0.15.0 stands.
- **Outside the repository:**
  - The bridge's `settings.txt`: `agents` back to the Orcs, Dol Guldur, the lieutenant and the kingdom's agent, `run_rivals` back to `mistymountainorcs; dolguldur`.
  - `C:\Users\karab\Projects\rhun-war-council` and `C:\Users\karab\Projects\gundabad-war-council` are kept, with `mode = off` in each `agent.txt`.
  - Their two war rooms are stopped. The other four and the dashboard kept running. The game was closed.
- **What to expect:** the study's estimates for two agents apply again. The test is still not like for like with run A, since several things changed at once.

### 21:45 · Install, version 0.15.1: an extra council as my battle begins · commit 4c0990b
- **Why:** my battle stops the game's clock for minutes, and a regular council loses a median of 5 to 8 in-game hours while it thinks. One sitting in a battle loses none. Battles only: menus are too short and stops on the map can't be foretold ([the table](WARROOM.md#the-extra-council-as-my-battle-begins-since-0151)).
- **What:**
  - The game, at the screen change into a scene while the player is in a battle on the map (`Watchdog.SceneBegan`, `MapEvent.PlayerMapEvent`), writes `spare.txt` in each realm agent's channel on the helper thread.
  - The war room (`Room.SpareOffered`, `SpareDue`, `SpareSat`, `BriefingFor`) sits one extra council. The regular councils' times and gap are not touched, its `council_in` can only bring the next one sooner, and its failure holds none up. It doesn't sit while a council sits, when a regular one is due, when the last council read this same in-game hour's report, on a word over two minutes old, or on an old briefing. `council_at_battle = off` turns it off.
  - The agent reads only that it is an extra look over the realm. The instructions didn't change, so the cached prompt stays.
- **Checks:**
  - Smoke test and war room self-test pass, with 2 and 15 new checks; dashboard self-test passes. The reference check reports 835 game members, 0 missing.
  - A rehearsal on the real `WarRoom.exe` in a scratch folder, with stand-ins for the model and the game: 14 of 14. The game's word brought an extra council, marked in `calls.jsonl`; a second word at the same hour brought none; the regular council still sat 24 hours after the last regular one; a word during a council brought no second one; a failed extra council left the next regular one on time; `council_in` brought the next one sooner.
  - **Not tested:** nothing here has run in the game, so the battle check itself hasn't been seen working. No real model has read the new reason line.
- **Cost to expect:** run A had 10 separate battles, so about $7 to $12 a run for the two agents, at $0.55 to $0.69 a council.
- **Outside the repository:** war rooms and dashboard stopped for the build and started again, four war rooms. The game was closed. The screen-time scripts, the rehearsal and `wake_compare.py` are copied to `D:\BannerlordAgents-backup\news-wake-study-20261106` to reuse on the new runs.

## Saturday, November 7

### 14:55 · Install, version 0.15.2: the lieutenant's answers land in order, it hears which orders wait for the clock, and it has room for long answers · commit d61f91d
- **Why:** test A1 (Nov 6), with the game paused from the load:
  - The report kept saying no orders were in force, so the lieutenant took its orders for lost and wrote all 80, and two whole tools, again in every answer: 3.5 to 6.6 minutes each. Once I told it the game was paused, it answered in 33 s.
  - When the clock first ran, the queued answers landed in the order 74, 78, 76, 77. The NOLDOR template that 78 took off my four clan lords came back on with 77.
  - An audit's answer was stopped at 48,000 characters on its 60th order of 70, twice, after 16 minutes, and all of it was lost.
- **What:**
  - `Lieutenant.Carry`: every answer goes through the helper's thread, with tools or without, first in its queue; then its diplomatic acts and orders go to the game's thread. So answers land in the order they came.
  - `chat.json` says how many orders and answers are waiting for the clock (`orders_waiting`, `answers_waiting`, counted from the clock's last tick). The lieutenant's message then has ORDERS WAITING FOR THE CLOCK: what waits, that nothing is wrong with them, and not to give them again.
  - The limit that stops a runaway is 150,000 characters for the lieutenant's answers, up from 48,000. A realm agent's limits are unchanged.
  - [LIEUTENANT.md](LIEUTENANT.md) says all three.
- **Checks:**
  - Smoke test, war room self-test and dashboard self-test all pass, with 2 and 2 new checks.
  - The order check fails on the old code (lord_azog before lord_bolg) and passes on the new.
  - The reference check reports 835 game members, 0 missing.
  - **Not tested:** none of it has run in the game yet.
- **Outside the repository:**
  - War rooms and dashboard stopped for the build and started again.
  - The game was closed. I saved `TEST A1` at 14:53, on day 9.
  - My review scripts for test A1 (screen time, Celebrían's recruiting, the agents' first nine days) are kept in `D:\BannerlordAgents-backup\news-wake-study-20261106\test-a1-review`.

### 15:36 · Install, version 0.16.0: the guard loop is the standard defend, the stop check, recruiting orders refused when the lord cannot pay, the lieutenant's talks in other saves, and a box to write in that grows · commit f5a7165
- **Why:** Nov 7, after test A1's first nine days:
  - The custom orders my lieutenant wrote in this test should be the standard defend order from now on, for the agents too. Its last guard loop (`guard_supply_loop` v7) fought 76 times and won 75, took 17 enemy lords and lost 1, over 161 runs.
  - Celebrían rode into Mount Gundabad and took no one: Elrohir, on the same loop 14 hours ahead, had taken every volunteer her standing reached, and the loop could not tell.
  - 47 of Dol Guldur's 64 recruit orders ended at once or soon at the wage cap or the gold floor, 29 lords again and again.
  - I want the lieutenant to have access to the chats of my other saves, so I can reference them; no limit on what I write to it; and the box to grow as I write, up to a limit, so it is easier to edit.
- **What:**
  - `defend.json` is the guard loop, with four changes. Guards ride into the fort when an enemy marches on it only where its defenders, with them, would hold, else they shelter elsewhere (`stay_inside` 1 keeps v7's way). They join the ruler's own attack, as the old defend did. A fight is weighed and begun in the hour it is seen (v7 took up to three hours: one change of state each for its three passes), with the lieutenant's own numbers and formula. A group's size comes from its `lords`. With no `places`, the loop is the holding and then its villages, nearest first (`places_default`). The old defend is gone from the library; orders carried on from a save keep the definition they were given with.
  - The stop check: `offers(h)`, `offers_of(lord, h)` and `can_recruit` in the order language (`Offers.cs`, on the game's thread in each own lord's hourly copy: the volunteers within his reach at each holding his order names, by the game's own reach rule, kept a day; and his wage cap, gold floor and clan purse). The standard defend rides on after an hour from a stop where no lord of its group has anything left.
  - A recruiting order (ours, or any whose first move recruits) for a lord who cannot pay for a single recruit is refused at once, with the numbers and what to do (`Orders.RecruitRefusal`). An answer's wage orders go first, so a cap raised in the same answer counts (`AgentChannel.WagesFirst`, the lieutenant's too).
  - Each lord's wages and cap show beside him in the report when he is at it or within 15% of it (`PurseView.RecruitNote`).
  - The lieutenant: at each load, `other_chats\` in its folder holds the whole talk of each of my other saves with something said in it, and an index (`LieutenantChats`, `History.FromNote`); its message names them and its instructions say to read the one I mean. Its box has no length limit (it had 400 letters) and grows from one line to eight, then scrolls, keeping the end in view while I type.
  - Both instruction templates name `offers`, `offers_of` and `can_recruit`, the example answer gives a pair of guards their turns, and `relief_within` is written with its values in their real order (hours, then reach; it said the reverse).
  - [TOOLS.md](TOOLS.md) and [LIEUTENANT.md](LIEUTENANT.md) describe all of it.
- **Checks:**
  - Smoke test 516 checks, all pass, 12 of them new (the standard defend's loop, the stop check, its fight weighed in one hour, riding in or not by whether the fort would hold, the language's new words, wage caps beside the lords, wage orders first, the other saves' talks). The checks written for the old defend run on its definition, kept in `tests/SmokeTest/Fixtures/defend-0.15.json`.
  - War room self-test (1 new check) and dashboard self-test pass.
  - The reference check reports 835 game members, 0 missing.
  - The other saves' talks, written from the real records into a scratch folder: 3 saves with talks (BACKUPS, TEST END A, TEST CONTINUOUS SAVE), about 500 KB each, in 0.3 s.
  - **Not tested:** none of it has run in the game yet, the growing box least of all (only the game draws it).
- **Outside the repository:**
  - The agents' instructions rebuilt from the templates, each keeping its own title and opening line: Misty Mountain Orcs, Dol Guldur, Kingdom of Arnor and Gondor, and the lieutenant (backups `warroom-system.md.bak-20261107-before-0.16`). Rhun and Gundabad stay as they are until the last test.
  - War rooms and dashboard stopped for the build and started again.
  - The lieutenant's own `guard_supply_loop` and `safe_recruit_loop` stay in its library as they are; it can move its guards to the standard defend and add the stop check to its recruiting loop when I ask.

### 16:06 · Install, version 0.17.0: the lieutenant's ways as general rules for every order, and words for the lieutenant at my next load · commit d0a6431
- **Why:** Nov 7, after 0.16.0: I liked all of the ideas drawn from the custom orders of both tests for the other orders, and asked for them as general rules. And I asked for my lieutenant to be told, at my next load, to bring every order in force to its final version.
- **What** (docs/TOOLS.md, "The lieutenant's ways as general rules"):
  - Hours, not map units: the danger rule every order carries counts what could reach a lord before he is behind walls (the hours to the nearest castle or town he could shelter in, plus 2, at most `danger_hours`, a new shared setting, default 3; never less than `danger_radius`). Raid (`reach_hours`, `avoid_hours`) and screen (`reach_hours`) reach in hours of the lord's march.
  - One weighing of a fight in the language (`fight_hours`, `fight_odds`: the march, the battle's own hours, the enemy there by its middle and end), used by defend, hunt, engage (new `give_up_odds`), relieve (strikes isolated besiegers only) and besiege's relief bars (`relief_hours` 0, the new default, weighs by that battle).
  - Just enough men (`needed`, defend's `enough`): the nearest guards of a holding go at an enemy until they are twice the strength it fights with; the rest keep to the loop.
  - The recruit tour passes by stops under threat (from the realm's own sightings, `Tools.Threats`) and stops another lord of the realm is already bound for.
  - Screen tells of each army at most once a day.
  - Food: a lord rides to buy once his food would last less than the trip to market and a day and a half (it was under 2 days anywhere).
  - Shortcuts for a group (`lords_near`, `group_strength`, `group_offers`, `marching_on`), and bigger limits on a tool of one's own: 20 states, 80 rules, 12 settings, 1,200 characters an expression (12, 40, 8 and 600).
  - The standard defend uses the weighing, `needed` and the shortcuts.
  - Words for the lieutenant at the next load (`Lieutenant.WordsAtLoad`): a file `at-next-load.txt` in its folder is sent as my words on the first frame back on the map after a load, once the carried-on orders are in force, then kept as `at-next-load.sent-<time>.txt`; a first line `for save: NAME` holds it for that save. [LIEUTENANT.md](LIEUTENANT.md).
  - Both instruction templates name the new words, `danger_hours` and the new limits.
- **Checks:**
  - Smoke test 524 checks, all pass, 8 of them new (`General016.cs`). The checks written before this version run with `danger_hours` 0, since their enemies are placed by the old rule; with the new rule some of them shelter or run first, as they should.
  - War room and dashboard self-tests pass. The helper's speed test is within budget (the 95th slowest hour of a hundred 97 ms, budget 1,000).
  - The reference check reports 837 game members, 0 missing.
  - **Not tested:** none of it has run in the game yet. The tour's threat and claim rules and the words at load run only on the game's side, so no check reaches them.
- **Outside the repository:**
  - `C:\Users\karab\Projects\lieutenant\warroom\at-next-load.txt`, for the save TEST A1, written at my request: the standard defend for every lord on a guard loop (same fort, loop, partners, start and turn, and settings), the next version of the safe recruiting loop with the stop check and every lord on it moved onto it, every other order of its own onto its newest version, nothing given twice, and a short account of what it changed.
  - The agents' instructions rebuilt from the templates again (Misty Mountain Orcs, Dol Guldur, Kingdom of Arnor and Gondor, the lieutenant; backups `warroom-system.md.bak-20261107-before-0.17`).
  - War rooms and dashboard stopped for the build and started again.

## Sunday, November 8

### 00:04 · Install, version 0.17.1 and 0.17.2: the lieutenant's work at a load waits for its agent, and attacks sent together count together · commit 1b65ea5
- **Why:** Nov 7, test A1 at 23:40 on 0.17.0:
  - The words left for the lieutenant at that load were not sent, and its other saves' talks were not written: both looked for the lieutenant before the helper had set up the agents (`Lieutenant.Restore` runs before `Brain.Reset`), found none, and did nothing. I pasted the words myself (message #92) and asked for them not to be sent again.
  - 19 attacks broke off in the hour they were given, among them 12 of the orcs' 13 on Stein ("584% of your side's 376": Krithak alone) and most of Dol Guldur's 16 on my army, whose lords then fell back toward their forts. The attack order's new give-up rule weighed the fight as the order started, before the other lords of the same answer held their orders, so each counted only himself.
- **What:**
  - 0.17.1: `Lieutenant.AfterLoad` does both on the campaign map once the lieutenant's agent is there (tried each frame, at most about ten seconds).
  - 0.17.2: engage's `give_up_odds` and hunt's `bigger_ratio` are weighed from the order's second hour on, when every lord sent with it is counted on our side.
- **Checks:** smoke test 525 checks, all pass, 1 new (three lords sent at one target keep at it; a lone lord against five times his strength breaks off in his second hour), which fails on 0.17.0. War room and dashboard self-tests pass; the reference check reports 837 game members, 0 missing. **Not tested in the game yet.**
- **Outside the repository:**
  - The lieutenant's `at-next-load.txt` set aside unsent as `at-next-load.cancelled-20261107-234312.txt`, as I asked once I had pasted it.
  - War rooms and dashboard stopped for the builds and started again (0.17.1 at 23:59, 0.17.2 at 00:04).

### 01:20 · Install, version 0.17.3: the human player, told plainly; his battles' rule in the tools; attacks on him close in and strike together; his record · commit 8028416
- **Why:** Nov 8: battle times work differently when I fight the battle. Dol Guldur's lords attacked me in force, but only a few could join, because they were not within the reinforcement radius in time; storms are the same. I asked how the mechanic works and for a proposal; then chose: tell the agents plainly which party is the human player's (reversing my Oct 20 choice not to), attacks on me close in and wait to strike together automatically, each realm's record of my battles from what it saw, and record each side's strength and troops in my battles.
- **The game's rule** (1.4.8, read in its code): when a battle with the player begins, every lord, patrol or bandit band within 3 of it that may take a side joins (3.75 while he is in a siege), parties in an army with their leader, none from inside a settlement, another battle or a siege; a storm takes only the siege camp and those inside; the clock stands still until it ends. Battles without him run for hours and take in arrivals.
- **What** (docs/TOOLS.md, "The human player's battles"):
  - The reports name him (YOUR ENEMY, THE HUMAN PLAYER; for his own realm YOUR OWN BATTLES), mark his party wherever they list it, and say the rule; the fog check allows his party and hero. Both instruction templates have the rule; the agents keep a note on him in `enemies` (up to five sentences), revised after every battle with him.
  - The shared weighing, the danger rule and the relief rule count only the ring of 3 when he is on a side; `battle_odds` and `engagement` say when he fights in a battle.
  - Any attack on him closes in to 6 of him and strikes together: once in position and four fifths of the strength sent at him that could come within 4 hours is there, or one of them has struck, or he comes within the ring, or 4 hours in position have passed; never in the hour the order came.
  - His record (`PlayerBattles`, `InPerson`): each battle he fights, both sides as it began and as he fights it (men, strength, lords, realms, foot, archers, horse and horse archers), the losses and the winner, into `in_person.jsonl`; a realm knows of one its lords fought in or that happened within two hours of its seeing him; the council's briefing and `lookups/the_human_player.txt` give it, with the realm's chronicle lines about him.
- **Checks:** smoke test 532 checks, all pass, 7 new (`InPerson0173.cs`); war room self-test (1 new) and dashboard self-test pass; the reference check reports 837 game members, 0 missing; the helper's speed test is within budget. **Not tested in the game yet**, the battle recorder least of all (only the game fights a battle).
- **Outside the repository:** the agents' instructions rebuilt from the templates (Misty Mountain Orcs, Dol Guldur, Kingdom of Arnor and Gondor, the lieutenant; backups `warroom-system.md.bak-20261108-before-0.17.3`); war rooms and dashboard stopped for the build and started again.

### 20:45 · Install, version 0.17.4: the standard defend's food steps no longer end it; fixing the orders in force at the next load · commit 85e1b56
- **Why:** Nov 8, the second start of test A1 (run 20261108-2016-A, from TEST START at 20:15). In its first in-game day and a half, 11 defend orders of all three realms ended at their food step: 6 of my lieutenant's, 3 of Dol Guldur's (Caras Galadhon twice, an hour after its agent gave it again), 2 of the orcs'. Four ended at full stores ("Mount Gundabad needs no food: it has 800 of 800, about 3 days under siege"). A supply move ends on the game's side, and that end was taken for its order's end, as it is for the supply order, whose move is its whole job. Many castles hold only about 3 days of siege food when full, so a guard with `days` 10 would also have ridden back to feed a full castle every 12 hours. I asked for the fix now, for the defend to be given again for every realm, and for the recruiting loops I had asked for to be reinstated, early in the test.
- **What** (docs/TOOLS.md, "The standard defend"):
  - A move whose state has rules to leave it is a step (`Move.Step`, `Runner.IsStep`). When the game's side ends a step's supply move, the lord rides into the holding (or holds where he may not) and the tool's rules read `arrived` (`Orders.StepDone`, `Runner.StepDone`, `Supply.Forget`). The supply order still ends with its move.
  - `food_full_days_at(h)`: the days a holding's full stores last a siege. defend feeds below half of `min(days, food_full_days_at(at))` plus the march, only while the holding is under nine tenths of it, where `feed` ends.
  - Our rest is no longer refused at a lord's gold floor (an orc lord's was, on day 2): it heals the wounded too, which needs no gold.
  - `Intervention`: a file `intervention-at-next-load.txt` in the campaign's data folder, read once at a load. `upgrade: defend` gives every carried run of that order of ours the library's version, keeping its state, settings and memory (`Runner.UpgradeAtLoad`), and each realm's first look says so. `order: <words>` gives an order by hand as `agents.order` does, only to a lord holding none then. The run gets the mark "intervention".
  - Both instruction templates name `food_full_days_at`.
- **Checks:** smoke test 535 checks, all pass, 3 new (`Defend0174.cs`: the food run is a step and the order goes on; the supply order's move is not a step; a castle full at 3 days is not fed again, one low within its full stores is), and the recruiting check now covers rest. The helper's speed test is within budget (the 95th slowest hour of a hundred 112 ms, budget 1,000). The reference check reports 837 game members, 0 missing. Installed module file EB5FB1B195D8B881. The war room is unchanged (0.17.3). **Not tested in the game yet**: the step's end and the intervention run only on the game's side.
- **Outside the repository:**
  - `D:\BannerlordAgents\data\Ah2auppicLLJ\intervention-at-next-load.txt`, for the save TEST A1: `upgrade: defend`, and Thrurg's guard of Bûrzkala (Dol Guldur, days 6, turn 0) given again by hand. The orcs' two ended guards were given other orders by their agent afterwards, so they are left alone; Dol Guldur had already given Caras Galadhon's again.
  - `C:\Users\karab\Projects\lieutenant\warroom\at-next-load.txt`, for the save TEST A1, written at my request: give again the six guard orders that ended (Ratagost at Fanuidhol, Gárjan at Mount Gundabad, Náth at Goblin Town, Lothuial at Lasgalor, Alenon at Imnagath, Oropherion at Torech Emel, each with his partner), and the 12 recruiting loops refused for their wage caps on Summer 20 (the caps were lifted at my word right after).
  - The agents' instructions updated with the one changed line (Misty Mountain Orcs, Dol Guldur, Kingdom of Arnor and Gondor, the lieutenant; backups `warroom-system.md.bak-20261108-before-0.17.4`).
### 21:10 · Install, version 0.17.5: a siege order given again keeps the siege; carried food runs marked as steps; the human player's battle taken as he joins it · commit e861a6a
- **Why:** Nov 8, test A1's second start, on 0.17.4:
  - Dol Guldur gave its siege orders again three times in forty minutes to change their numbers, partners or next order (Lasgalor at 20:43 and 21:01, Imnagath at 20:43), and each new order took the lords out of the camp: the Lasgalor siege that began at 21:00:26 ended at 21:01:09. I asked for the fix now and closed the game for it.
  - A food run carried on from the save was given again unmarked, so it still ended its defend once: Lothuial's brace at Lasgalor (20:50) and Górli's feed at Mazôglod (21:00).
  - My first battle of the run (Autumn 3: Alenon's fight with Narsha, which I joined; Narsha taken) was taken only at its end, when Narsha's party was gone: her side had 657 men but no lords, troops by kind or strength.
- **What:**
  - Our siege order given again to a lord whose siege order names the same place goes on where it stands, with the new order's numbers (defaults for those left out), partners, next order and words (`Orders.Issue`, `Runner.GivenAgain`); its receipt says so. A siege order for another place is a new order, as before.
  - `Runner.Resume` marks the carried move a step as `Issue` does.
  - `PlayerBattles.Frame` (every frame, a look-up while I am in no battle): a battle I am in that was not taken yet is taken now.
- **Checks:** smoke test 537 checks, all pass, 2 new (a defend carried on mid–food-run takes the library's version under an intervention and its food run is a step; a siege order given again keeps its state with the new numbers, partners and next order, and makes no new move). The helper's speed test is within budget; the reference check reports 837 game members, 0 missing. Installed module file 625B99D1DFF0B189. **Not tested in the game yet:** the siege kept and the battle taken as I join it run only on the game's side.
- **Outside the repository:** nothing.
### 21:46 · Install, version 0.17.6: the lieutenant's window shows only my words and its answers · commit 9a413bc
- **Why:** Nov 8, test A1's second start: "For the next load: please remove the lieutenant's blue log of what lords are doing and why from my chat with it. I just want to see my prompts and its answers to them going forward."
- **What:** `Lieutenant.ShowLog` (off): the call-backs and ends of its orders (blue, `Lieutenant.Heard`) and the refusals (red) no longer go into the window or the game's message log, at once or at a load (`Lieutenant.Shown`). They are still kept in the conversation (`chat.jsonl`) and told to the lieutenant with my next words. docs/LIEUTENANT.md says so.
- **Checks:** smoke test 537 checks, all pass; the reference check reports 837 game members, 0 missing. Installed module file BBF8F30876ED32F5. **Not seen in the game yet.**
- **Outside the repository:** nothing.

## Monday, November 9

### 12:27 · War room 0.17.7: each call's writes go to the five-minute prompt cache · commit 59808fd
- **Why:** the agents were taking far more of my usage than a long coding session. Last night's 204 calls (Nov 8, 20:17–22:32, run 20261108-2016-A) cost $60 at Claude Code's list prices, which fit every call exactly at $0.20 a million tokens read from the cache, $8 a million written to it and $20 a million written out. Writing into the cache was 71–74% of the cost, nearly all of it each call's own report written at its first step (a fast answer about 15,000 tokens, a council about 46,000, a lieutenant chat about 52,000), used for under a minute and then dropped. The standing prompt (22,000–33,000 tokens) was already read back from the cache. Claude Code writes the main conversation of a `-p` call to the one-hour cache on the subscription, at twice the input price; the five-minute cache costs 1.25 times.
- **What:**
  - `cache_ttl` in `warroom.txt` (5m by default; 1h, or empty for Claude Code's default), passed to each fresh call as `CLAUDE_CODE_PROMPT_CACHE_TTL` (`Claude.CacheTtl`). The one long conversation (`memory = conversation`, the comparison arm) keeps the default, since it is read again poke after poke.
  - Each line of `calls.jsonl` has `cache_write_1h`, the part of `cache_write` that went to the one-hour cache; `WarRoom toolcheck` prints both parts.
  - docs/WARROOM.md lists the setting.
- **Numbers:** replayed call by call on the recorded calls (a cache hit renews its five minutes; a call that comes later than that writes the standing prompt again): Nov 8 $59.67 to $46.36 (−22%), Nov 6–7 $82.06 to $64.56 (−21%). Cheaper for every kind of call even with those rewrites: answers −25%, councils −17 to −20%, lieutenant chats −25%, stories −18 to −20%. Nothing the agents see changes.
- **Checks:** war room self-test, all pass, 4 new (the default is written for the agent's folder, `cache_ttl` sets it, a value Claude Code would not take leaves its default, an older settings file gets the line). `WarRoom toolcheck`, two real calls on the subscription: 5,186 and 912 tokens written for five minutes, none for an hour. The game module is unchanged (0.17.6).
- **Outside the repository:**
  - The four agents' `warroom.txt` (Misty Mountain Orcs, Dol Guldur, the lieutenant, Kingdom of Arnor and Gondor) rewritten by the war room at its start with `cache_ttl = 5m` and `council_at_battle = on` (that one was missing from the files and already on by default); every other setting unchanged; backups `warroom.txt.bak-20261109-before-cache-ttl`.
  - War rooms and dashboard stopped for the build and started again.

## Tuesday, November 10

### 18:32 · The fresh-call design frozen at tag `fresh-calls-0.17.7`; test A1 stops at day 12 · commit 605bea9
- **Why:** a fresh call at every wake takes too much of my plan to keep testing on it. I'm moving the realm agents to one continuous conversation each, which sets its own wakes and its own strategy reviews and compacts at quiet moments. It is still being designed; nothing is built yet. The fresh-call design stays runnable, so later tests can compare the two. Test A1's second start (run 20261108-2016-A) stops at day 12 (save TEST A1, Autumn 10, 1087).
- **What:** git tag `fresh-calls-0.17.7` on commit 38f939a: game module 0.17.6, war room 0.17.7. No code changes. To run it again: check out the tag and build, or use the builds in the backup below, and restore the agents' folders from it.
- **Numbers (Nov 8, 20:15–22:35, Claude Code list prices):**
  - The agents: $59.67 (Misty Mountain Orcs $24.20, Dol Guldur $28.46, the lieutenant $7.01). The orcs woke about 29 times an hour of play (22 answers, 7 councils) and Dol Guldur about 37.
  - The Claude Code session watching and building beside them: $71.87, more than the agents (677 requests, each reading about 342,000 tokens of its own conversation from the cache).
  - Projected for one continuous conversation per realm from the same calls: at the same number of wakes and compacting at 500,000 tokens, about the same as now (orcs $10.60 an hour against $10.76, Dol Guldur $12.50 against $12.78). Compacting at 250,000 tokens saves about 17%, half the wakes about half, a third of the wakes with smaller strategy reviews about three quarters.
- **Outside the repository:**
  - Backup `D:\BannerlordAgents-backup\fresh-call-architecture-20261110`: the four agents' folders as they stand (instructions, settings, memory, channels), the war room and dashboard builds, the module build 0.17.6, and this repository's CLAUDE.md and agent/CLAUDE.template.md (both kept out of git).
  - The game watch is stopped. The war rooms and dashboard are still running.

### 21:05 · Version 0.18.0: continuous agents (module and war room), installed; the orcs and Dol Guldur switched to it · commit 734b9cc
- **Why:** the realm agents move from a fresh call at every wake to one conversation each, which costs far less of my plan once the agent decides itself when to wake. Each agent sets its own watches on the parts of the map that matter to its wars and on the orders it is testing. It holds its strategy reviews in the same conversation and writes its own handover before the conversation grows too long. A handover also carries a short history of the war from the agent's side.
- **What (module 0.18.0):**
  - Watches (`Watches.cs`). An answer's `"watch"` list keeps, changes or ends watches: a lord, names for ids, up to 8 conditions in the behavior language with words to wake with, lords to follow, a check-in every N hours, and an end hour or end condition. They are read every in-game hour on the realm's own view (`Runner.WatchScope`). A condition wakes the agent when it turns true. Each watch gets a receipt.
  - `agent.txt`'s `wake = self` (`WakeRules.SortSelf`): only watches, check-ins, followed lords' order news, the locked rules, war, peace, offers and a quiet day wake the agent. Everything else rides the next wake.
  - YOUR WATCHES comes after WHY YOU ARE ASKED NOW and in the review's briefing. Watches are kept in the save's carry-on file and taken up at its load. `AgentConfig.Save` now keeps `news` and `wake`.
- **What (war room 0.18.0):**
  - `memory = continuous` (`Room.Continuous.cs`): one Claude Code session per realm, resumed at every wake and review, with one system prompt, one set of tools, one model (`claude-opus-5-5[1m]`), one effort and one thinking cap for its whole life. Every call uses the one-hour cache, with Claude Code's own compaction off.
  - Each wake's message is the news report less what the conversation already holds. The opening carries the handover, notes, chronicle since and the whole realm. Strategy reviews come at the agent's `review_in` hour or after big events.
  - The handover is written at my battle once the conversation holds 500,000 tokens, at the game's close once it holds 500,000, or at any wake past 850,000. It is kept as the memory's story with its snapshot, so a reload of an earlier save finds the matching handover.
  - Its own tools changed since a conversation began are told in the next message. Each call's own cost is logged (a resumed session reports its running total).
  - The instructions: `agent/warroom-continuous.template.md`. It covers strategy reviews in the council's place, watches with an offensive and a hungry-castle example, wakes, attention (in my order, with "Orders you just gave, and the sections of the map that are critical for your wars that you are watching" second), memory, the handover's nine parts, the answer, following one's own orders, learning from mistakes and from what worked (anyone's, the human player's above all), the book questions, allies, the human player (neither too eager nor too afraid, judged by his record), and time.
  - `scripts\setup-agent.ps1 -Continuous` / `-FreshCalls`. Design notes: [WARROOM.md](WARROOM.md#continuous-agents-since-018).
- **Checks:**
  - Smoke test ALL CHECKS PASSED (572 checks, new: `Watch018.cs`). War room self-test passed (new: `SelfTestContinuous.cs`, including that every continuous section lands in place over the realm's instructions). Dashboard self-test passed. RefCheck: 839 references, missing 0.
  - A real check, in a scratch folder the game never reads: `claude-opus-5-5[1m]` runs on the subscription with a 1,000,000-token window. One wake from a real orc report wrote 37,543 tokens into the one-hour cache ($0.32 at list prices) and gave one order and two well-formed watches. The second wake in the same conversation read those back and wrote 7,951 new tokens ($0.08). Its message was 14,458 characters against the full report's 25,637. The test calls together: $0.40.
- **Installed** into the game's Modules\BannerlordAgents (v0.18.0) with the game closed.
- **Outside the repository:**
  - `orc-war-council` and `dolguldur-war-council`: `agent.txt` now says `wake = self` (old copy `agent.txt.bak-20261110-before-continuous`), `warroom.txt` says `memory = continuous` with the new continuous settings added (old copy `warroom.txt.bak-*-before-continuous`), `warroom-continuous.md` installed, and `warroom\tools.txt` written afresh (the game writes the full library at the next load).
  - The lieutenant and the kingdom agent are unchanged. All war rooms and the dashboard were stopped for the build and started again on 0.18.0.

### 21:31 · War room 0.18.1: the handover lists every order in force · commit 334ed68
- **Why:** the next conversation's fresh report says what each lord is doing, but not why. The handover now carries every order in force with its purpose. The old "orders I am still testing" part folds into it, so no order is written down twice.
- **What:**
  - The handover's nine parts (`agent/warroom-continuous.template.md`): part 4 is now ORDERS IN FORCE. It covers every lord holding an order and every lord with none, grouped by front or task, each group with its order, why and what would end it, and the orders on trial marked ON TRIAL with what is expected by when and what happens if they fail. Watches move to part 5. The length limit rises from 2,500 to 3,000 words.
  - The request to write it carries the game's own list (`Room.HandoverFacts`): the orders in force, the lords with no order and the watches, so the agent writes those parts from facts rather than memory. While the game runs, a fresh briefing is asked for first. At the game's close, the list comes from the newer of the last situation report and the last wake's report. The handover's line in `calls.jsonl` gets `facts_chars`.
- **Checks:** war room self-test passed. New checks: the list from the situation report and its briefing when they are newer, from the last wake's report when that is newer, none without an orders section, and the nine parts in order over the realm's instructions. The game module is unchanged (0.18.0), so nothing was installed into the game.
- **Outside the repository:** `warroom-continuous.md` in `orc-war-council` and `dolguldur-war-council` replaced by the new template (old copies `warroom-continuous.md.bak-20261110-before-orders-part`). War rooms and dashboard stopped for the build and started again on 0.18.1.

## Wednesday, November 11

### 10:40 · War room 0.18.2: the agent alone sets when its strategy reviews sit · commit (pending)
- **Why:** an experiment. The agent decides when it reviews its war, with no review called for it.
- **What:**
  - A strategy review sits only at the hour the agent set with `review_in`, now 1 to 240 in-game hours from its answer (was 4 to 72), given at a review or at any wake.
  - Gone: the review after a big event, the daily review when no hour is set, and the 6 hours between reviews. A review that sets no next hour leaves none set. Big events still wake the agent, and it can set `"review_in": 1` to review within the hour.
  - The first answer of each conversation is still a review.
  - The instructions say so (`agent/warroom-continuous.template.md`: Strategy reviews, its step 7, and How you answer), as does the line that opens a conversation.
- **Checks:** war room self-test passed. New checks: a review sits an hour after the last when set so; none sits with no hour set; `review_in` is kept between 1 and 240; a review that sets none leaves none set; a wake's answer may set it. Nothing was installed into the game (the module is unchanged, 0.18.0).
- **Outside the repository:** `warroom-continuous.md` in `orc-war-council` and `dolguldur-war-council` replaced by the new template (old copies `warroom-continuous.md.bak-20261111-before-agent-set-reviews`). The war rooms had stopped overnight; all of them and the dashboard were started on 0.18.2.
