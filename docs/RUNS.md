# Test runs (since 0.11)

The measured runs for the checkpoint and after: 60 in-game days each, all from one save, with every number kept per run.

**Mert's words (Oct 23).**
- "The test's starting save is fixed."
- "I will play the game for 60 in-game days and see how well I do against dol guldur and the misty orcs against our AI. If I can, I will also play against raw AI but might not have enough time. I will prioritize playing against our own agents because I already have spent a lot of time playing against raw AI, and have always been able to steamroll it very easily so will be able to compare their performance anecdotally to what I have seen."
- "Then, we will give control of my kingdom first to an agent and have it play against other agents and against the game's raw ai, and see how its performance compares to me. All tests will be for 60 in-game days, starting from the TEST START save."
- For the agent-ruler runs: "switch the player character to a wanderer in a random town, give that character a lot of money and food so it doesn't die, and simply let the game run." The lieutenant stays on when Mert plays. The rivals' agents stay on, watching, in the runs against the game's AI "so long as usage is fine". Track token use too.

## The arms

| Arm | Your kingdom | The Misty Mountain Orcs and Dol Guldur | Lieutenant | Order |
|---|---|---|---|---|
| A | you | their agents, live | on | first |
| B | you | the game's AI (their agents answer, give no order) | on | if time allows |
| C1 | its agent, live (you a wanderer in a quiet town) | their agents, live | off | second |
| C2 | its agent, live | the game's AI (their agents watching) | off | third |

- **The save:** `TEST START` (`run_save`), made Oct 22 with `agents.rebalance`. A run's setup keeps the save file's fingerprint, so a changed save shows.
- **Same instructions for every agent:** the kingdom's agent (`C:\Users\karab\Projects\kingdom-war-council`, made Oct 23) has the orcs' and Dol Guldur's instructions word for word, only its name and opening line its own ("the Kingdom of Arnor and Gondor, the reunited realm of the Dúnedain of the north and the south"). Nothing of Mert's own play is in them.
- **Setting an arm:** `powershell -ExecutionPolicy Bypass -File scripts\arm.ps1 -Arm A` (or B, C1, C2; `-Arm show` shows the modes now). All four agents stay listed in the bridge's settings; the script switches their modes in each folder's `agent.txt`, which the bridge reads at every load. When two agents name one realm (the lieutenant and the kingdom's agent), the one switched on plays it. Each switch is logged in `runs\arms.log`.
- **In the game:** load TEST START. For C1 and C2, before the clock runs: `agents.run playerless` shows the wanderer it would make you (since 0.11.2; Mert, Oct 23: "The switch should be done by a command that you write, including the giving them gold and food part"), and `agents.run playerless go [name]` does it: every lord of the kingdom holding a Party AI Controls order is released, the wanderer (free, in the quietest town at peace with the kingdom and outside the run's realms) joins your clan as a companion and you become him through the character-switch mod's own switch, your old party goes to the game's AI for the kingdom's agent, he gets 100,000 denars and 100 grain and goes into the town. Then choose to wait in the town. `agents.run check` says which arm it would be and what would spoil it. Then let the clock run. (Your clan stays the game's player clan whoever you play, so the kingdom is still yours to the game: its decisions may wait for you. Not guarded yet, by Mert's call.)

## What a run keeps

Loading TEST START readies a run. It begins on the first in-game hour the clock runs (after any setting up), named by who commands each side, and ends after 60 × 24 in-game hours: the game pauses and offers a save named `TEST END <arm> <run>`. A load of one of the run's own saves (an autosave after a crash) carries it on; loading TEST START again ends the old one unfinished; any other save leaves it waiting. Since 0.11.1 a save made while setting up, before the clock first ran, counts too: loading it readies the run again from there (Mert saved TEST A first, Oct 23), and the folder the run was readied under goes once the run is named by its arm. Every run of a campaign replays the same days, so a run's own folder, not the campaign's, tells runs apart.

`%LOCALAPPDATA%\BannerlordAgents\runs\<run>\` (`current.txt` names the open one):
- `run.json`: the arm, who commands each side, its loads (wall clock and in-game hours), its saves, the marks against it, its running tally, the game's war tally at its start.
- `setup.json`: the module's version, every mod with its version, each agent's realm, kind, mode, war room settings and the fingerprints of its instructions, the save's fingerprint, the detail level, cheat mode and true sight at the start, Party AI Controls' orders.
- `days.jsonl`: the scoreboard at the start (day 0) and every 24 in-game hours after, for the kingdom and each rival: towns, castles, villages; men in garrisons and militia; lords in the field, their men, wounded and strength; armies and their men; loot, prisoners and ransom carried; lords alive, held by enemies, enemy lords held; clans' gold and its daily change; influence; recruits that day; the run's tally so far. Also the game's own war tally for each pair of realms (casualties, sieges, towns and raids won, tribute), your party, the speed and the real seconds the day took.
- `events.jsonl`: every battle with these realms in it (kind, place, each side's realm, leader, who commanded it: you, your lieutenant, an agent or the game's AI, men at the start, men lost, lords, winner, whether you fought); every holding changing hands; sieges begun and ended; lords taken, killed and freed; villages looted; armies formed and broken up; war and peace; your notes (`agents.run note <text>`). Plus the run's copy of the detailed log: the agents' pokes, answers and orders' receipts, the orders' starts and ends, the lieutenant, your decisions, alarms, errors, freezes, the fog and sight checks, speed changes, saves and loads. Not the lords' hourly progress, the AI's reasons or the hourly sight lines, which stay in each launch's trace.

**Marks against a run** (in `run.json`, shown by `agents.run`): true sight, cheat mode on (setting up a C run needs it; noted, not fatal), a change in who commands a side after the start, a late start, a crash, in-game hours played again after a reload, a stop by hand.

**Agents' token use** is read from each war room's own call log (`calls.jsonl`, in the log folder its setup names) for the run's real-time spans: per agent and kind of call (answers, councils, the chronicler's stories, the lieutenant's chats), tokens read (fresh, from the cache, written to the cache), tokens written and thinking, the list price, per in-game day, per real hour, the most in any 5 hours (the subscription's limits run on 5-hour windows), failed calls and usage-limit hits.

## Watching and reporting (`Runs.exe`)

`src\BannerlordAgents.Runs\bin\Release\net8.0\Runs.exe`, read-only:
- `status [run]`: where the open run stands: each side now and since day 0, the agents' calls so far, the marks.
- `watch [run]`: what is new since the last look (the cursor is `watch.json` in the run's folder): the new day lines' changes, holdings, battles between lords, sieges, lords taken and killed, your notes, the agents' orders and pokes, your decisions, the lieutenant, and each agent's calls and tokens. First, under NEEDS A LOOK (exit code 1): new marks, serious alarms, errors, freezes outside loading, fog leaks, a sight check under 97%, failed calls, usage limits, the game or a war room not running, the bridge silent for 10 minutes. The coding session runs it every few minutes during a run.
- `report [run]`: the run in full, `report.md` and `report.json` in its folder: where each side ended, the run's tally, the game's war tally over the run, the holdings that changed hands, the biggest battles, the lords killed, the agents' calls, times and tokens (and by in-game day in the JSON), their orders by status and kind with the commonest refusals, the pokes and their report sizes, your decisions, the lieutenant, the run's health, your notes.
- `compare`: every run side by side, `runs\compare.md`.

## Run C1 (Oct 23) and what it changed (0.11.3)

The first finished run: C1, `20261023-1802-C1`, 60 days in 1 h 20 min, about $276 at list price. What its record and alarms showed, and what changed:
- **Battles between lords were counted as 2; they were at least 109.** The game takes the losing lords from their parties before it tells of a battle's end, so the record counted none on the losing side. The record now counts each side at the battle's start (and from the lords among the men, the wounded and the dead). Runs.exe re-scores older runs: a battle the winner fought with lords counts when a lord of the losing realm was taken or killed within the hour. "Men lost" counts the killed, wounded and routed alike, and says so.
- **The orcs' gold "fell" 10.6 million:** 8.9 of it was five hired clans leaving with their purses. The day lines now keep `hired_clans`, `hired_gold` and `sworn_gold`, and a clan joining or leaving is an event ("clan moved").
- **The kingdom's votes never settled** (the player's clan is fixed, so the game waited for the player): no holding it took was given out, and no policy, war or peace vote could follow. `Votes` now settles them while the player is not the ruler, and a vote still open a day after its time is an alarm. Holdings given out within a realm are events ("holding given", with the clans).
- **Recruiting at villages never worked** for ordered lords (they are never "inside" a village); fixed, so ordered lords now recruit at villages. Recruit trips choose their next stop by road, not as the crow flies.
- **28 "still held after release" alarms** were TFAOMFixes' recruiting trip taking lords after our orders ended: named now, and our orders take a lord off it.
- **The fog check's 70 flagged reports** were all false (spots of its own past watch posts; the holding of a siege seen by its camp). One small real leak behind them: a siege seen only by its camp showed the defenders' strength, the food and the storm's timing. Fixed.
- **470 "stuck" and 136 "fought" alarms** were mostly lords on the move along bending roads, besiegers at their camps, and the village arrivals above. The stuck check now also needs the lord to have stood still. The defend order gives up a chase not closing in after 4 hours.
- Smaller: the hourly copy's slowness limit is two frames (it alarmed every hour at 5 ms); an answer's orders spread over ticks; the look at answer folders goes before the worker's queue; answers with raw line breaks are read; the first look since a load gets the council's time.

**For comparisons:** C1 was played with the village and vote faults; later runs are not. Its agent ruled a kingdom whose conquered holdings stayed with its ruler's clan, and its lords recruited only at towns and castles on their orders. C1 should be played again on 0.11.3 before its numbers stand beside A, B and C2.

## Run C2 (Oct 23), stopped at day 31, and what it changed (0.11.4)

`20261023-2101-C2`, on 0.11.3: the kingdom's agent against the game's own AI, the orcs' and Dol Guldur's agents watching. Mert first loaded `TEST C1` (C1's save from its setting up), which readies no run, as a finished run's saves do not; reloading `TEST START` readied it. It ran 31 days in 44 minutes, about $106 at list price, and was left there. Mert: "We are going to have to run this test again some other time, from the start. What killed it was one bug that forced our troops out of a castle they should have defended. We should focus on fixing that bug, and then try again. In the ideal world, our agents should easily beat the raw game's strategy."
- **The sally (day 9-11):** Aragorn's and Imák's armies sheltered in Imnagath on the agent's orders, against Lorgath's army (20 lords, 10,302 men, strength 22.6k), the shelter judging the walls would hold. The game's own sally test sent everyone inside (5,938 men, 20 lords) out the hour his siege began; after 46 hours of battle in the open all were lost and the castle fell. Since 0.11.4 the defenders stay inside while a lord one of our orders holds is there, as the game spares a defence the player leads; they still join a relief's battle. Every sally test at the holdings of the realms with agents is recorded with the game's own numbers: by ours, it should have needed under 8k of Lorgath's 22.6k counted, and why it counted so little is still to be seen.
- **The kingdom's agent read "SHADOW ... none is carried out"** at the head of every poke and report: the mode of the first agent listed (the orcs, in shadow). Its orders were carried out and its receipts said so; it never mentioned the line. Fixed in 0.11.4. Arm B had the same fault.
- **Labels:** the run's record had Dol Guldur and the kingdom at peace (the stance alone; the game goes by the War of the Ring's table) and the agents' own armies as "the game's" (read before the order was in force). Both fixed.
- **Not changed, for Mert:** the agents spread their lords over five to eight armies sized to what is in front of each, while the game's AI massed one army of 20 lords. The news of the big enemy army is often buried under raids and sightings between answers, and the call-up order is stricter about influence than the game's own (it refuses when the clan cannot pay all at once; the game needs 100 and lets the clan go into debt).
- **C2 is to be played again from the start**, on 0.11.4 or later.

## Not yet seen in a game

All of it: the run's start, its day lines, its events, the day-60 pause and save, carrying on after a load, the switch of arms, the kingdom's agent in command, and the wanderer trick with the bridge (who rules, the lieutenant switched off, Party AI Controls).
