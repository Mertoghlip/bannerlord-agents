# The strategists' flight recorder

The logs follow every poke, every answer, every order and every lord from start to finish, so a problem shows up the moment it happens instead of hiding until something breaks. They are modelled on the battle logging from the engine work:
- heartbeats that notice when something stops;
- alarms raised the moment a rule is broken;
- a trail that names who did what;
- timings for every step;
- a summary at the end, and a command for the state right now;
- a switch between everyday and full detail.

Installed with version 0.3 of the module (Oct 8); version 0.4 added the tools' and armies' records, and 0.5 the realm's chronicle. The record formats are in [protocol/README.md](../protocol/README.md).

## What the logs show

### Whether everything is alive
- There is a heartbeat for the game's own thread, our background helper, the hourly look at the map, and the check of the agent's folder.
- If one stops, an alarm says which, since when, and what our code was doing at that moment.
- The watchdog knows when the clock stops for a good reason (paused, a menu, a battle, loading, the window in the background), so it does not raise false alarms.

### Where a freeze is
- When the game's thread stops moving for 20 seconds, the freeze probe runs: a small program outside the game.
- It samples the game's main thread ten times in a second and says, in plain words:
  - whether the thread is **busy** (and in one small loop, or moving around), or **waiting**, and for what;
  - its call trail through the engine, with Windows' own functions named;
  - the C# stacks of every thread running C#, read from a snapshot so the game is not held;
  - the busiest threads over the same second.
- It writes `freeze-(time).txt` into the launch's log folder.
- A freeze during play (on the map or in a battle) also raises a red alarm after 2 minutes. A long loading screen is only noted.

### The session
- Which build of the mod ran, with its file time and fingerprint.
- The full mod list with versions, and every setting as read, including any line not understood.
- A start-up self-check: the realm under watch and its lords, the war with you, the calendar, the game's sight rules, Party AI Controls' list, the game hooks, whether held lords are kept from Party AI Controls' hourly look (0.6.3), the log folder, the freeze probe, the agent's folder, and the helper's cores.
- Every console command typed, and the first line of its answer.
- Every change of game speed, pause, battle, menu and screen, and every time the game window loses focus, in real and in-game time.
- Loading another save: proof that nothing from the old one carried over.

### What the orcs see
- **Every in-game hour:** how many parties they see, from how many eyes, which holdings they look over, and how the hourly copy's time split between its parts. (`hour` events, `parts_ms`: kingdoms, holdings, parties with sieges and battles, the realm, sight, names; since 0.7.5 three more at the end splitting the parties part: the rows, the march rules and sieges, the battles.)
- **The sight check, every 3 in-game hours:** our sight rule against the game's own view for your party, with the numbers behind every disagreement.
  - Since 0.10.4 it compares only where the game itself refreshes its "seen" flags: within its largest sight range, plus 5, of your party, and never garrisons or militias. Beyond that the game leaves a party's flag as it was when last refreshed. On Oct 22 an orc army seen days before, 247 away, still counted as seen, which brought agreement down to 86–91%. Without those old flags, every launch from Oct 8 to Oct 23 agrees on 99.9–100%.
- **The fog check, on every report:**
  - every enemy listed in view really is in sight;
  - "seen earlier" never lists a party still in sight;
  - garrisons show only for holdings in sight;
  - every id in brackets belongs to something the realm owns, sees, remembers, knows from the map, or was named by the game's own news to it. "Owns" includes its lords riding in its armies. Both sources were added in 0.10.4, after two false alarms: on Oct 23, YOUR PURSE named lords riding in the orcs' armies; on Oct 22, a poke named a raider of an orc village, known only from the raid notice and gone from sight before the hour's look.
- **Alarms** for:
  - a whole day blind while at war;
  - seeing more than 60% of the enemy lords on the map;
  - the game's sight model failing and a stand-in value being used;
  - garbled names;
  - impossible values in the hourly copy.
- **Report size** in tokens, section by section, and each time the 40-party limit hid parties.

### Pokes
- Every notice is numbered, and followed to the poke that carried it.
- Pokes by reason, and how long after the hour each poke's files were written.
- **What was kept from the agent** (since 0.3.1): small fights, repeated raid notices and lost parties whose capture already says it, each logged with the reason; and each enemy lord newly seen far from the realm's holdings. The summary counts both.
- **Alarms** for:
  - twelve poked hours in a row;
  - the same enemy setting off three sightings in a day at the edge of sight;
  - a lord seen with a new party, which counts as a stranger.

### The agent's answers
- **A timeline for every answer:** when it was written, read and carried out, how many seconds and in-game hours after its poke, and at what game speed.
- **Late and repeated answers:** answers to a poke a newer one had replaced, and second answers to the same poke.
- **Bad files:** broken, unnumbered, left from an earlier session (set aside unread), or answering a poke from before a load (refused).
- **Ids the agent was never shown:** made up, or learned outside its folder. This is the in-game half of the independence check.
- **Silence:** a red alarm when 5 minutes pass with no answer after a poke.

### Every order, start to finish
- **How it began:** the words it came from, and which lord and place they matched.
- **Whether it took:** the lord's state just before and just after.
- **Every in-game hour after that:** distance to the target, arrival, every time it had to be given again (and who turned the lord away), the end and its reason, and three hours of watching after release.
- **Alarms** for:
  - a command the game did not take;
  - no progress for 6 in-game hours;
  - given again 3 times in 6 hours;
  - a released lord whose AI is still switched off;
  - something else switching the AI back on for a lord we hold.

### Tools and armies (since 0.4, Oct 8)
- **Every tool, start to finish** (`tool` events, [TOOLS.md](TOOLS.md)):
  - how it began: the tool, the lord, the places and lords, the settings, and the next order;
  - each step from state to state, with the rule that fired and the move made;
  - each call-back to the agent, each shelter from danger and each return, each change of settings;
  - how it ended, and after how many steps and in-game hours.
- **Every move a tool makes** (`order` events, "moved"): what the game was told, and whether it took.
- **Every army** (`army` events): the leader, the meeting place, the lords called, the influence price, and the clan's influence before.
- **Alarm** for a tool in the library that fails the checker, at every load.

- **Join orders** (since 0.6): each lord sent to join an army (`army:sent to join`), each one that joined (`army` "joined", with the influence it cost and the clan's influence before), and each that could not (its order's end, with why).

### Recruitment (since 0.6, Oct 9; logs only, never shown to the agent)
Why the watched realm's parties do or do not refill, measured against the game's own gates (`Recruitment`; read in the 1.4.8 code, `RecruitmentCampaignBehavior` and `HeroHelper`). A lord the game runs may recruit only in an hour it spends in a town (not besieged) or an unraided village, and only when:
- its party's gold is above **50 + 20 a man** (up to 150 men);
- for a lord who does not lead the clan, the clan's gold is above **50 + the clan leader's party wages + 40 a man in that party**;
- the party is below its size limit, not starving, and has room in its wage budget for the recruit;
- a notable there has volunteers waiting, within the slots the notable's relation allows.

What is recorded:
- **Once a day** (game's thread, a few milliseconds), one line in `recruitment.jsonl` in the campaign's folder: for each lord, its gold against the party bar, its clan's gold against the clan bar, men against the size limit, wages against the wage limit, starving, in an army, held by our order, inside where, whether every gate is open, and the men it recruited that day and where; for each town and village of the realm, its state, the volunteers waiting, and how many of them the ruler's relations would reach (a stand-in for every lord's); and the totals.
- **Every recruit** by the realm's lords as it happens (the game's `OnTroopRecruitedEvent`, counted on the game's thread without allocating for anyone else's).
- **Every in-game hour** (worker), which lords sat where they could recruit.
- **A sentence a day** in `story.log`, for example: "recruitment, Summer 10, 1087: 49 lords at 24% of their size limits on average; 31 below the party-gold bar, 12 below the clan-gold bar, 5 without wage room, 0 full, 2 starving; 140 men recruited by 14 lords; 210 volunteers waiting in 6 of the realm's 11 towns and villages (95 within what the ruler's relations allow); 19 of 49 lords spent at least one of the last 24 hours where they could recruit".
- `agents.recruits` prints the same as of now, and the last whole day.

### The realm's memory (since 0.5, Oct 9)
- **At every load** (`chronicle` events): how many entries of the game's own history log were read, how many were about the watched realm, any that could not be read, and the time it took on the game's thread; then the chronicle's lines, how many were cut because an earlier save was loaded, and how many came from the game's log.
- **The chronicle itself** is the record: one dated line per thing the realm learned, in the campaign's folder ([MEMORY.md](MEMORY.md)). `agents.chronicle [n]` prints its last lines.

### The war room
The war room keeps its own logs, beside the game's: every call with its times and tokens, the prompt, Claude Code's full event stream, and the answer; every council of war, with why it sat and which of its orders were dropped because a faster answer had ordered those lords; and every rewrite of the story so far, with what the memory carried into each answer ([WARROOM.md](WARROOM.md)).

### Who moved a lord, and why the game's AI chose
These are the only two pieces that hook into the game's code instead of reading it. Each has its own switch.
- **The movement watcher** (full detail only):
  - Every movement command in the game passes through one common step.
  - When anything other than us uses it on a lord holding our order, `lords.log` names who: the game's own decision, its army logic, its quick reactions (chase or flee), Party AI Controls, or another mod.
- **The AI's reasons:**
  - Each time the game's AI weighs its options for a lord of the watched realm, `lords.log` records what it chose.
  - When it changes plan: up to eight of the options it weighed, with their scores.
  - When it keeps its plan: the plan, and the best other option with its score.
  - The game moves to its best option only when a random roll falls under that option's score and the score clears a small bar; the log gives both.
- **Every plan change:** `lords.log` also gets a line whenever the game changes what a lord of the watched realm is doing, whether or not anyone orders them.

### Saves
- At every save: which lords were handed back, checked free before the game wrote, and taken back after. `handbacks.jsonl` keeps the list.
- At every load: every party in the game whose AI is switched off, by realm, and a check that every lord we ever handed back is free.
- Once a day: lords whose AI is switched off by anyone but us (Party AI Controls is named).

### Your own play
- For each decision: how long it took to settle and how many quick corrections were merged.
- When the recorder pauses (you march in someone else's army).
- Each change in Party AI Controls' list, and an alarm if it cannot be read.

### Cost
- Time on the game's thread for each of our hooks, every minute, and the slowest single call. An alarm for any call over 5 ms.
- The background helper's processor time, and how much of it ran on the efficiency cores.
- Memory, threads, open files, garbage collections, and our caches, once a minute.

### Errors and crashes
- Every error is counted by where it happened; the first three of each kind go to `errors.log` with the full trace. 26 places that used to swallow errors silently now report them.
- **The black box:** the last 1,000 things the mod saw and did, written on a serious alarm, an uncaught exception, or `agents.blackbox`.
- If a launch does not close cleanly, the next launch says so and names what it left.

## What runs where

- **The game's own thread** only copies numbers and stamps heartbeats. It never writes a file, and it never waits on the disk.
- **Everything else** runs on two threads that Windows keeps on the efficiency cores:
  - the background helper: all writing, checks and formatting, at low priority in efficiency mode, except its urgent work (orders and each hour's order rules), which runs at normal priority out of efficiency mode (since 0.12.0, [below](#the-helper-on-time-since-0120));
  - the watchdog, at low priority.
- **The freeze probe** is a separate program, started only when the game freezes.
- **The graphics card** is not used: logging is small branching work, and moving it there and back would cost more than doing it. The card's spare room is kept for the small model in November.
- **Folder watching:** the agent's orders folder is watched by Windows (no polling); a slow check every 10 seconds is the safety net.

## The helper on time (since 0.12.0)

**Why:** in run 20261025-1417-C2 the background helper fell up to 142 s behind the game, about two game days at the runs' speed (an in-game hour every 3.3 s). Every order, every shelter and every poke landed that late: Beleg's four attack orders each waited about 49 s to start, and the agent's order to go inside Lasgalor landed after Thrurg had caught Aragorn. Mert: "The helper needs to be able to be on time, every time."
- **What was found:**
  - Last night's run had already needed a median 2.85 s of the helper's time per game hour when it was backed up, against the 3.3 s the game gives; that run was behind for 26% of its hours. This one needed 3.47 s and was behind for 86%.
  - A probe of the helper caught it writing look-up files in 10 samples out of 10. 216 to 339 files per agent were rewritten for each poke, each written beside the old one and swapped in. That took 1,179 ms for 216 files, against 75 ms written plainly. A swap refused while the virus scanner held the file slept 15 ms, up to four times a file.
  - Every hour, for every agent, it also rebuilt the realm's view twice and wrote the situation report and the council briefing, which only a council reads.
  - New orders started in the same first-come queue as all of that.
  - The backlog alarm (up to 2,051 of 4,096 jobs waiting, 174 times) was not marked serious, so no watch saw it.
- **The three lanes** (Background; one thread still does everything, so nothing it keeps is shared between threads):
  - **urgent:** the agents' answers, an order's start, change and end, and each hour's order rules and look (`Brain.Decide`: the tools, what each realm sees, the trails, the recruitment log). Always first, at normal priority and out of Windows' efficiency mode, on the same efficiency cores;
  - **regular:** logs and records, first come first served, as before;
  - **reporting:** the views, pokes, look-ups and the dashboard's picture of the newest hour only (`Brain.NextStep`), one step at a time and only when nothing else waits: the snapshot checks, then each watched realm, then the dashboard. Hours that come while a round runs are folded into the next. What each hour saw waits in the realm's memory (`Memory.Waiting`), with its ruler's notices, so the next poke carries all of it.
- **Less work:**
  - look-up files are written in place, and only when their text changed (`Files.WriteIfChanged`);
  - swaps no longer sleep: one more try at once, then the file is written in place;
  - one view per realm and hour serves both its poke and the dashboard;
  - a realm agent's situation report and council briefing are written when its war room asks (`Brain.Briefing`, [WARROOM.md](WARROOM.md#the-council-of-war));
  - the very same order given again keeps the one already running ("already holds this very order ... not started again").
- **What it records:**
  - **Each hour's `hour` event:** `behind_hours` (how far the order rules were behind the game), `orders_ms` (calibrate, orders, look, recruitment) and `orders_total_ms`.
  - **Each report round's `reports` event:** `folded` hours, `behind_hours`, `ms` by part (checks, sight, view, facts, report, fog check, look-ups, look-up files, files, dashboard), and look-up files written and left unchanged.
  - **`agent` / `briefing written`** with its ms.
  - **summary.txt:** each hour's orders and look, the report rounds and the briefings (median, average, largest), hours folded, look-up files written and left unchanged, swaps tried again, files written in place, and orders kept as they were.
  - **The run's day lines:** `helper` (the most the orders and the reports were behind that day, report rounds, hours folded); the run report has the worst of the run.
  - **A poke folded from hours the game has moved on from** (two hours or more) says so in its head.
- **Alarms, all serious:**
  - `helper_late`: the order rules 2 or more in-game hours behind the game while the clock runs;
  - `helper_reports_late`: the reports 6 or more;
  - `helper_backlog`: over 1,000 jobs waiting;
  - `helper_dropped`: work dropped because the queue was full.
- **The speed test** (`tests\SmokeTest\bin\Release\SmokeTest.exe --perf [folder] [--budget ms]`): the helper's whole hour, its orders and look and then its report round, with a watching agent per watched realm, on the helper's own cores and priority, timed by part. It fails when the 95th slowest hour in a hundred takes more than the budget (1,000 ms, a third of a game hour). With no folder it uses a big made-up world (nine realms, three watched, 828 holdings, about 2,950 parties): on 0.12.0, median 62 ms an hour, 95th 67 ms, most 615 ms (the first look). The real test is hours the game saved: each launch saves `perf_record_hours` (12) consecutive hourly copies of the world from `perf_record_after` (24) in-game hours into a load, to `perf\snapshots\<campaign>-<load>\` (`PerfRecord`, on the regular lane).

## Detail levels

| Level | What it leaves out | Use |
|---|---|---|
| **full** (default for the first tests) | nothing | the first tests |
| **everyday** | the 4-times-a-second watch on ordered lords, and the movement watcher | long sessions and measured runs |

Each summary reports what the logging itself cost on the game's thread.

## Where the files are

`%LOCALAPPDATA%\BannerlordAgents\logs\`:
- `bridge.log`: the readable story, across launches;
- `sessions\(date-time)\`: one folder per game launch. The newest 30 are kept, 2 GB at most in all;
- `warroom\`: the war room's calls ([WARROOM.md](WARROOM.md)), and `routes.jsonl`, every question to the agent's "ask the way" tool (0.7);
- `dashboard\`: the dashboard's own log, and `accuracy.jsonl`, our way-finder against the game's planned ways (0.7, [DASHBOARD.md](DASHBOARD.md), [ROUTES.md](ROUTES.md)).

| File in a session folder | What it holds |
|---|---|
| `story.log` | the readable story of this launch; alarms start with `!!` |
| `trace.jsonl` | everything, one record per line (capped at 200 MB a launch) |
| `lords.log` | what the game's AI did with the watched realm's lords, and why |
| `health.csv` | one row a minute |
| `summary.txt` | the totals, every 10 minutes and at the end |
| `errors.log` | full traces of the first errors of each kind |
| `blackbox-*.txt`, `freeze-*.txt`, `crash-*.txt`, `watchdog.log` | only when something goes wrong |

The agent cannot read any of this: a war room call can read only the channel folder. (The agent chat's settings blocked the whole folder.)

## Commands

| Command | What it does |
|---|---|
| `agents.health` | alarms so far and the state right now |
| `agents.blackbox` | writes the black box now |
| `agents.probe` | runs the freeze probe once, to check it works |
| `agents.detail full` or `everyday` | the detail level |
| `agents.hooks on` or `off`; `agents.hooks witness` or `reasons on` or `off` | the two game hooks |
| `agents.alarms on` or `off` | red alarms on screen |
| `agents.recruits` | why the watched realm's parties do or do not refill, as of now and for the last whole day |
| `agents.map` or `agents.map again` | where the copy of the walking map stands, and the dashboard's frames this session; again: copy the map anew (0.7) |

Outside the game, `scripts\probe.ps1` runs the probe on the running game, for example during a loading-screen hang before the mod is up.

## What the logs cannot see

- **What the agent chat read outside its folder:** for the first session this shows only in Claude Code's own record of the chat. Since Oct 8 the war room keeps every call's event stream, so each read the agent tries, and each one refused, is in its logs.
- **Inside battles:** by design. Battle results arrive as notices.
- **The engine's own reasons:** the probe names where in the engine a thread is, as an address; what that code does needs the disassembly tools from the battle work.
