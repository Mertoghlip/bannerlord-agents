# First live session: October 8, 2026

**In short:**
- Everything we built ran for 27 minutes with no errors from our code.
- The orc agent played sensibly, stayed inside its folder, and taught itself to answer faster. At the speed you played, though, it was always one to three in-game days behind.
- The three game crashes were ghost troops hidden in battles the save was still fighting. They were not caused by our mod, and they are now fixed in your fixes mod.

Sources:
- this launch's logs (`%LOCALAPPDATA%\BannerlordAgents\logs\sessions\20261008-152439`);
- the campaign's records;
- the agent's folder and journal;
- Claude Code's record of the agent chat;
- TAOM's three crash reports.

## What ran

| | |
|---|---|
| Game launched, campaign loaded, game closed | 15:24, 15:26, 15:51 (27 minutes) |
| Clock running | about 3 minutes, mostly at fast speed: 199 in-game hours (8 days). That's about one in-game hour per real second on average; an uninterrupted day at fast speed took 11 seconds. |
| Paused or in menus | about 22 minutes (the crash dialogs, then the siege menu outside Western Moria) |
| Game window in the background | 21 minutes |
| Phases covered | 0 set-up, 1 watching the orcs, 2 recording your play, 5 Opus watching (shadow mode) |
| Phases still to run | 3 performance on and off, 4 one order by hand, 6 Opus commanding one lord |

**The mod:**
- **Start-up:** the self-check passed 9 of 9, and both game hooks attached.
- **Background work:** all 3,550 background jobs ran on the efficiency cores; none were dropped and none failed.
- **Errors:** none from our code.
- **Exit:** clean, with the final summary written.
- **Logs:** 2.2 MB for the whole session.

## The crashes

- **What you saw:** three "TAOM caught a crash" dialogs in the first two minutes of play, and the crash again after every resume.
- **The cause:** 24 ghost troop types from the October 5 trouble: 12 Harad and 12 Mordor types, each an empty copy beside the real one.
  - They were hiding inside battles that were still being fought when the save was made. A battle keeps its own lists of troops, and the load-time repair never looked there.
  - As those battles ended, the losers became three deserter parties made of ghosts, and two lords picked some up, one of them as prisoners.
  - The game then failed every in-game hour: the food calculation reads a troop's culture, and the battle calculation reads its equipment.
- **Not our mod:**
  - none of our code was on any of the three failing paths;
  - our mod never creates troops;
  - ghost copies can only appear when a save is loaded, and the repair found every party clean at that load.
- **Fixed:** the repair in your fixes mod now also cleans battles in progress, at every load and with `tfaomfix.troop_check fix`.
  - It's installed, and the old version is kept at `C:\BannerlordArchive\TFAOMFixes.dll.148-before-battlerepair-20261008-1551`.
  - The next load's log line will say how many ghosts it found inside battles.

## The orc agent (Opus 5.5, shadow mode)

**How it woke:** you messaged it at 15:27, a few seconds before the game wrote its first poke. Its own launch watcher fired 18 seconds later, but by then it was already working.

**What it did in 6½ minutes:**

| Poke | Answered after the poke | Newer pokes already out | Orders | What became of them |
|---|---|---|---|---|
| #4 | 139 s | 6 | 7 | 7 checked valid |
| #10 | 93 s | 44 | 11 | 7 valid, 4 refused (its target, Ingunde, had left the map) |
| #54 | 53 s | 28 | 11 | 8 valid, 3 refused (its target, Finduilas, had left the map) |
| #83 | 21 s | 30 | 5 | 3 valid, 2 refused (Radbug and Ufza no longer led a party, probably captured) |
| #124 | 33 s | 1 | 5 | never checked: the game stayed paused until you quit |

- **Answer time:** a median of 53 seconds. At the speed you played, that's 21 to 75 in-game hours late, and every answer was to a poke a newer one had already replaced.
- **Stale orders:** 9 of the 34 orders checked (26%) were refused because the world had moved on.
- **Orders that waited for the clock:** all 39. The game was paused each time an answer arrived (the crash dialogs, then the siege menu), and the "received, waiting for the clock" receipt worked every time.
- **The game read each answer file** within half a second of it being written.

**How it played:**
- **It saw you coming.** On Summer 10 it noticed your army of 5,528 heading for Western Moria, called off its own attack on Hollin Gate, and ordered the Moria lords inside the walls. You laid siege to Western Moria on Summer 17.
- **The game's own AI did the opposite.** It took Gashnar out of Western Moria, and Isvan captured him on Summer 17.
- **It changed its own method mid-session.** It noticed it was too slow, cut its answers from 11 orders to 5, and wrote two lessons in its journal: chasing a moving enemy lord fails by the time the order is read, so defend or garrison holdings instead; and answer only the newest poke.
- **It made no reckless orders,** and its answers name who, where and why in plain words.

**Boundaries:**
- It used only the files in its folder, plus one read of its own watcher's output file in Claude Code's temporary folder.
- It ran no commands other than its poke watch.
- It never named an id it had not been shown.

**Usage:**

| | |
|---|---|
| Model steps | 13 |
| Tokens written | 44,500 (about 8,900 per answer; the first answer alone took 14,600 and over two minutes of thinking) |
| Tokens re-read from cache | 1.72 million |
| Its context | grew from 66,000 to 193,000 tokens: about 25,000 more per answer |

One long chat won't last an evening at this rate. The war room should send each poke as a fresh call carrying the journal, not one growing conversation.

## What the orcs saw

**Pokes:**
- 125 pokes in 199 in-game hours: almost one every hour, and the "every hour for 12 hours" alarm fired.
- 87 of them were battles, many small ones, such as a caravan against bandits. That's too noisy.
- The breakdown, type by type and poke by poke, is in [2026-10-08-pokes.md](2026-10-08-pokes.md).

**Reports:**
- Each report is about 5,200 tokens, and the 40-party limit cut parties from every one of them.
- The fog check covered 324 reports and found no leak.

**Sight rule:**
- Our rule agreed with the game on 5,356 of 5,470 checks (97.9%).
- Every miss was the same two militia parties standing inside their own settlements (Rad-snar and Hollin Gate). The game shows them; our rule hides them.

## The game's own AI (new in this version)

- **What was recorded:** the orc lords weighed their options 1,412 times and changed plan 776 times, and `lords.log` holds 1,312 plan changes in all.
- **Each decision lists its options and scores.** Bolg, for example, chose to go to Gramburg (31.6) over Galadbost (7.5) and defending Bar-noss (6.4).
- **No real dice roll.** Scores in this pack run from about 3 to 30, so a lord always takes his best option when he rethinks; the random element in the game's AI never comes into play.
- **One recurring option:** many lords had "defend Bar-noss" among their top options.

## Your play (the recorder)

**Eight decisions:**
- three marches to Western Moria;
- two marches to a point;
- two orders to your vassal Himrion, given in your dialogue, to fill his ranks (read from Party AI Controls);
- the siege of Western Moria.

Each movement settled within a quarter of an in-game hour, and one quick correction was merged.

## Cost

- **The game's main thread:**
  - Our code took about 5 ms per in-game hour, about 1 second over the 27 minutes.
  - Most of it is the hourly copy of the map: 4.3 ms on average, 25 ms the first hour. That's above my 1 ms target and raised 36 "slow call" alarms.
  - The logging itself took under 1 ms in all.
- **Frame rate:**
  - About 69 frames a second at play speed.
  - 10 to 15 at fast speed. That's the game's own simulation, limited by its main thread. Our share was 8 to 27 ms per second.
- **Memory:**
  - The game used 15 GB, and Windows was at 89% memory use.
  - The crash reports flagged this. It wasn't the cause, but closing OneDrive and browsers before playing gives the game room.

## To fix in our mod

1. Cut the hourly copy to well under 1 ms per in-game hour.
2. Fewer, better pokes: count only battles that involve a lord or a holding, so the agent isn't poked every hour.
3. Correct the sight rule for militia inside their own settlement.
4. Clearer refusal reasons: say "his target is no longer on the map" or "Radbug is a prisoner" instead of listing 25 other lords.
5. Small log fixes:
   - the lords' first hour shows place ids instead of names;
   - mercenary bands that weigh no options shouldn't get a line.

## For the next session

- **Check the fix:** on the first load, the fixes mod's log should report the ghosts it found inside battles.
- **The agent:**
  - At normal speed it has four times as long per in-game hour: a day takes about 45 seconds, against the 11 measured at fast speed.
  - At fast speed it will always lag.
  - Phase 6, where one lord obeys, will show what that lag costs.
- **Still to run:** phases 3, 4 and 6.
