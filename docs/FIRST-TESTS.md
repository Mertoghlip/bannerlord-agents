# First tests: the Misty Mountain Orcs

**Where:** your own campaign, while you play. You're at war with the Misty Mountain Orcs, TAOM's "Orc-Host of the Misty Mountains" led by their Warlord. Your own armies are what they see, so you can watch them react.

**What the tests prove, in order:**
- the bridge sees what the orcs would see, and notices what they'd notice;
- it records how you play;
- it costs you no frame rate;
- an orc lord obeys an order without the game's AI pulling them away;
- Opus 5.5's orders come back while the game keeps running;
- once the first tools exist, Opus 5.5 can run the whole orc realm.

| Phase | What it proves | Install | Your part |
|---|---|---|---|
| 0 · Set up | The mod loads beside your usual mods | 1 | about 15 minutes, mostly the first slow load |
| 1 · Watch the orcs | Their view and their pokes are right | 1 | normal play, plus one deliberate approach |
| 2 · Record your play | Your decisions are captured correctly | 1 | an evening of normal play, then a 5-minute read |
| 3 · Performance | No frame-rate cost, and our work stays off the busy core | 1 | 10 minutes |
| 4 · One order by hand | An orc lord obeys, and your save stays clean | 1 | about 30 minutes |
| 5 · Opus watches | Answer time and usage while the game runs | 1, plus the agent | normal play |
| 6 · Opus commands one lord | The live loop works end to end | 1, plus the agent | 30–60 minutes |
| 7 · Opus commands the realm | Opus runs a whole realm through tools, live | 0.4, plus the war room | an evening of normal play |

- **Install 1** has been in the game since Oct 7, and carries the agent channel, so phases 0 to 6 can all run on it. Versions 0.3 and 0.3.1 (Oct 8) added the logs and cut the pokes.
- **Oct 8, first live session:** phases 0, 1, 2 and 5 ran. The report is [reports/2026-10-08-first-live-session.md](reports/2026-10-08-first-live-session.md). Phases 3, 4 and 6 are still to run.
- **Version 0.4 (Oct 8)** brings the tools, the armies and the war room, a week early, so phase 7 can run now. **Version 0.5 (in the game since Oct 9, 11:01)** adds each realm's memory of the war ([MEMORY.md](MEMORY.md)). [Testing versions 0.4 and 0.5](#testing-versions-04-and-05-opus-commands-the-orcs-with-tools-and-memory) says how.
- **The agent** is the war room since Oct 8 ([WARROOM.md](WARROOM.md)). The agent chat of the first session ([AGENT-CHAT.md](AGENT-CHAT.md)) is retired.

Each install is one restart of the game.

## Testing versions 0.4 and 0.5: Opus commands the orcs, with tools and memory

**Set up already (Oct 8, 21:14; memory Oct 9, 11:01):**
- the bridge is in **live** mode for **every** orc lord, answering through `C:\Users\karab\Projects\orc-war-council\warroom`;
- the war room is running in the background on this laptop. To run it yourself instead: `powershell -ExecutionPolicy Bypass -File scripts\warroom.ps1`, which runs in its window until Ctrl+C.

**You:**
1. Close the old agent chat: the VS Code window open on `orc-war-council`. It has nothing left to answer, and on Oct 9 it was still waking every 30 minutes and writing into the old journal.
2. Start the game with Bannerlord Agents ticked, and load your campaign.
3. In the console (Alt + ~):
   - `agents.agent` says LIVE, all lords, with the folder above;
   - `agents.tools` says 16 orders and no problems.
4. Play as usual, at war with the orcs. Once or twice, march toward an orc holding.

**What to watch:**
- **Cyan messages** when the agent gives an order (an army included), when a lord arrives, and when an order ends, with the reason.
- **`agents.orders`:** every orc lord holding an order, its order, and the move it is making right now: into a castle to shelter, after an enemy, to a village for food.
- **Armies:** when the agent gathers one, the game's own army forms at once, and the called lords march to the meeting place.
- **Your approach:** orc lords near your army should take shelter in a castle or town, and come out again once you leave.
- **Answer time:** each receipt line gives the seconds from poke to answer.
- **The memory** ([MEMORY.md](MEMORY.md)):
  - `agents.chronicle` prints the last lines of the orcs' chronicle. At the first load it starts with what the game remembers of the last week, marked "(from the game's records)".
  - The War room window says when the story so far is rewritten, and what each answer changed in the notes.
  - The notes and the story are plain files: `C:\Users\karab\Projects\orc-war-council\memory\<campaign>\notes.md` and `story.md`.
  - If you load an earlier save, the War room window says the memory went back to that date.

**Not yet seen in a real game** (the tests cover each one outside the game):
- an army formed by the agent, with its lords marching to it;
- a held lord sheltering from you, and going to buy food;
- a lord stopping near an enemy holding, rather than inside it;
- an idle army breaking up after about two days, as the game does;
- the game's own history read at a real load, and the first story written from it;
- answer time with the new, longer pokes and the memory.

**Stop at any time:** `agents.agent shadow` stops new orders from being carried out, and `agents.release all` hands every orc lord back to the game at once. Closing the war room alone stops new answers, but the lords keep their current orders until released.

## Running the tests (install 1 since Wed Oct 7, 15:33; version 0.5 since Oct 9, 11:01)

Your three newest saves were copied to `C:\BannerlordArchive\save-backups` before the install, and checked identical to the originals.

1. Start the launcher and tick **Bannerlord Agents**, which sits below your usual mods. Then load your campaign. The first load is slow once.
2. Open the console with Alt + ~ and type `agents.status`. It should name the Misty Mountain Orcs and their Warlord, and count their lords and holdings. `agents.help` lists every command.
3. For each phase, the commands are:

| Phase | What to type |
|---|---|
| 1 · Watch | `agents.view` prints the orcs' report and the file it is in. `agents.pokes` lists their last pokes. `agents.vision_check` compares our sight rule with the game's own. A yellow message appears on screen whenever the orcs are poked. |
| 2 · Record | `agents.record show 10` lists your last decisions in plain words. `agents.record off` and `agents.record on` switch it. |
| 3 · Performance | Play 3 minutes, then type `agents.bridge off`; play 3 more, then `agents.bridge on`. `agents.perf` compares the two. |
| 4 · One order | `agents.order Throkmaw garrison Caradhras` (any orc lord and any castle they hold; partial names work, accents optional; where two lords share a name, the console shows each one's id to use instead). `agents.orders` shows how it's going; `agents.release Throkmaw` or `agents.release all` ends it. Since 0.4, every order of the library works by hand, with settings and lords: `agents.tools` lists them ([TOOLS.md](TOOLS.md)). |
| 5 · Opus watches | Start the war room ([WARROOM.md](WARROOM.md)) and type `agents.agent shadow`: every order is checked and none is carried out. |
| 6 · Opus commands one lord | `agents.agent live Throkmaw` (any one lord). `agents.agent shadow` goes back, and `agents.release all` hands every lord back to the game. |
| 7 · Opus commands the realm | `agents.agent live` (every lord; set already). |
| Your vassals | `agents.vassal <your lord> defend <your castle>` orders one of your own lords directly, with the agents' orders, and records it as your decision ([notes](notes/vassal-orders.md)). |

Nothing needs sending: I read the files myself. Just tell me which phases you ran, and anything that looked wrong. Everything is in `%LOCALAPPDATA%\BannerlordAgents`, laid out in [protocol/README.md](../protocol/README.md).

**The logs (since Oct 8, version 0.3).** Everything the strategists do is recorded in detail ([LOGGING.md](LOGGING.md)). A red "[Agents] Problem: ..." line on screen means a serious alarm (at most one a minute). `agents.health` lists the alarms so far and the state right now; `agents.blackbox` writes the black box if something looks wrong and you want me to see that moment. If the game freezes, the freeze probe runs by itself after 20 seconds; if it hangs on a loading screen, `scripts\probe.ps1` runs it by hand.

## Ground rules for every phase

- Before the first install, your newest save is copied aside and never touched.
- Nothing of ours goes into your save (plan, decision 3). Unticking the mod in the launcher always returns you to a normal game.
- Only orc lords are ever ordered, only in phases 4 and 6, and only when you or the test says so. One console command hands every one of them back to the game at once.
- Installs happen only with the game closed.
- Every phase ends with something you can see in the game, plus a line in the log that confirms it.

## Phase 0 · Set up (install 1, game closed)

**Built:** a settings file that names the realm under watch, the Misty Mountain Orcs by default. The status command reports the realm it found.

**You:**
1. Close the game.
2. I copy your newest save aside and install the mod.
3. You tick Bannerlord Agents in the launcher, below your usual mods, and start the game.

Expect one slow load, because the game rebuilds its shaders after any change to the mod list.

**Check:** in the console (Alt + ~), `agents.status` names the Misty Mountain Orcs and their Warlord, and counts their lords, parties and holdings.

**Undo:** untick the mod.

## Phase 1 · Watch the orcs (nothing is ordered)

**Built:**
- **The orcs' view**, worked out every in-game hour:
  - their own lords, parties and holdings;
  - every party inside the sight of any orc party or holding, by the game's own sight rules;
  - parties that drop out of sight, kept as "last seen at … on …".
- **Their pokes:**
  - an enemy army comes into view, for the first time or after a long absence;
  - an enemy lord comes near their holdings, heads for one, or chases one of their parties;
  - a notice their Warlord would get: a war declared, a holding besieged, raided or taken, an army destroyed, a battle between lords, a lord captured, or an offer;
  - a full quiet day.

  Pokes that land close together are merged into one. Since Oct 8 (version 0.3.1), fights with bandits, caravans and villagers, and enemy lords far from orc land, no longer poke: they were 67 of the first session's 125 pokes ([the breakdown](reports/2026-10-08-pokes.md)).
- **`agents.view`** prints the report an agent would get, word for word.
- **A sight check for your own party:** our rule against the game's own view.

**You:** play as usual. Once, march an army toward an orc army or holding, let them see you, then pull back out of sight.

**Check:**
- The log shows "enemy army in view: (your army)" at about the moment, and at about the distance, they'd plausibly spot you. Afterwards it lists your army as last seen, with the time.
- `agents.view` matches the map: the orc holdings, their owners, and the lords' positions.
- For your own party, our sight rule agrees with the game's on at least 95 in 100 nearby parties.
- The orcs behave as before, since nothing is ordered yet.

**Measured:**
- time spent on the game's thread per in-game hour;
- report length in tokens;
- pokes per in-game day.

## Phase 2 · Record your play

**Built:** the recorder.
- **What it records:**
  - your party's decisions: where you go; what you attack, besiege, raid, enter or wait in; and the armies you form or join;
  - your decisions as ruler of the Kingdom of Arnor and Gondor: war, peace, alliances and calls to war;
  - the orders you give your vassals in conversation. Your own mod hands them to Party AI Controls, and the recorder reads them there after every conversation. It reads only lords who already hold an order, so nothing in Party AI Controls changes.
- **What each record holds:** your faction's view at that moment, and the pokes you'd just had.
- **Clicks:** map clicks you correct within moments are merged into one decision. The raw clicks are kept separately.
- **Control:** `agents.record on`, `off` or `status`.

**You:** play an evening as usual. Afterwards I print a few records in plain words, and you confirm they match what you did. Two examples of the form, with made-up details:
- "Day 412, 14:00: you turned from Caras Galadhon toward an orc army near Mount Gram (3,200 against your 2,100), six hours after first seeing it."
- "Day 412, 15:00: you told a vassal to defend Fornost while that army was a day's march away."

**Check:** your real decisions are there, and nothing you didn't do is.

**Measured:**
- decisions per hour of play;
- how many clicks were corrections;
- recording size per hour.

## Phase 3 · Performance (10 minutes)

**Built:**
- an on/off switch for all of the bridge's work, without a restart;
- a once-a-second frame-rate log on the campaign map;
- a core-by-core load sample.

**You:** stand still on the campaign map. Then play at your usual speed for 3 minutes with the bridge on, and 3 minutes with it off.

**Check:**
- The frame rate with the bridge on and off differs by no more than normal noise.
- The bridge's time on the game's thread stays well under a millisecond per in-game hour.
- Our threads run on the efficiency cores.

This also gives the first campaign-map numbers for the busy core and the idle ones. Until now we have only measured battles.

## Phase 4 · One order, by hand

**Built:**
- **Hand orders for one orc lord:** go to, garrison, patrol around, engage, besiege, raid and hold, plus release.
- **The game's AI held off that lord** while the order stands.
- **The order in the log:** its progress, and how it ended and why.
- **The save-time hand-back** (plan, decision 3).

**You:**
1. Pick an orc lord near your army.
2. Order that lord to garrison an orc castle, and watch for three in-game days.
3. Order the same lord to patrol around it.
4. Save the game by hand.
5. Release the lord.

**Check:**
- The lord goes there and stays. The game's AI doesn't pull them away, except to fight an enemy attacking the very castle they're guarding.
- After release, the lord goes back to making their own plans.
- Load the save you made in step 4. The log reports no lord still held by us.
- If the target falls, or the lord is beaten or captured, the order ends with that reason in the log.

## Phase 5 · Opus 5.5 watches (install 1 and the agent)

**Needs once:** the agent. On Oct 8 that was the agent chat ([AGENT-CHAT.md](AGENT-CHAT.md)); since then it is the war room ([WARROOM.md](WARROOM.md)).

**Built:** the agent channel. The bridge writes the orcs' pokes and report into the agent's folder. The agent answers each poke with an orders file. In shadow mode, every order is checked against the game and receipted, and none is carried out.

**You:** play as usual. Glance at the agent now and then.

**Measured:**
- answer time, in seconds and in in-game hours at your usual speed;
- how often a new poke arrives before the last answer;
- how many orders would already be out of date when they arrived;
- usage per hour, against your plan's limits.

**Check:** we read a few answers side by side with what the orcs actually did. Do Opus's orders make sense for their situation?

## Phase 6 · Opus commands one orc lord

**Why one lord, not the whole realm:**
- The tools don't exist yet at this point. Without them, every order is a single move, like "go there" or "attack that". A lord who arrives just stops and waits for the next answer.
- A whole realm run that way would stand still between answers, and would use far more of your subscription.
- That is the every-move setup, which the study expects to do worst in live play.

One lord shows whether the live loop works. The whole realm follows in phase 7, once the lords have tools.

**Built:** `agents.agent live <lord>`. Opus's orders are carried out for that one lord. They pass the same checks as hand orders, and are checked again on arrival (plan, decision 4).

**You:** keep that lord in view for a while.

**Check:**
- The lord follows Opus's orders.
- When a late order is refused, the refusal and its reason show up in Opus's next poke.
- Release hands the lord back cleanly.

## Phase 7 · Opus commands the whole orc realm (version 0.4, since Oct 8)

**Needs:**
- the tool engine and the tools: built in version 0.4, a week ahead of the plan ([TOOLS.md](TOOLS.md));
- the war room, for short answers ([WARROOM.md](WARROOM.md));
- phase 5's usage numbers leaving room in your subscription. On Oct 8 the first session used 6% of the 5-hour limit.

**Built:**
- Every orc lord is under Opus 5.5, and each holds a tool that keeps the lord acting between answers.
- The tools poke Opus when something big happens. A lord the agent has not ordered stays with the game's own AI.
- Opus can form armies with the game's own rules, and order them like a single lord.
- The realm's pokes are merged, and the late-order checks are on.

**You:** play an evening as usual, at war with them. The steps are in [Testing version 0.4](#testing-versions-04-and-05-opus-commands-the-orcs-with-tools-and-memory).

**Check:**
- Their lords keep moving sensibly while Opus thinks.
- Pokes per hour and usage stay within what phase 5 showed is affordable.
- One command hands the whole realm back to the game.

This is the rehearsal for the coalition runs in week 3.
