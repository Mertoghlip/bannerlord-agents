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

## Not yet seen in a game

All of it: the run's start, its day lines, its events, the day-60 pause and save, carrying on after a load, the switch of arms, the kingdom's agent in command, and the wanderer trick with the bridge (who rules, the lieutenant switched off, Party AI Controls).
