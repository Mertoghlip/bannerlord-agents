# First tests: the Misty Mountain Orcs

**Where:** your own campaign, while you play. You're at war with the Misty Mountain Orcs, TAOM's "Orc-Host of the Misty Mountains" led by their Warlord. Your own armies are what they see, so you can watch them react.

**What the tests prove, in order:**
- the bridge sees what the orcs would see, and notices what they'd notice;
- it records how you play;
- it costs you no frame rate;
- an orc lord obeys an order without the game's AI pulling him away;
- Opus 5.5's orders come back while the game keeps running;
- once the first tools exist, Opus 5.5 can run the whole orc realm.

| Phase | What it proves | Install | Your part |
|---|---|---|---|
| 0 · Set up | The mod loads beside your usual mods | 1 | about 15 minutes, mostly the first slow load |
| 1 · Watch the orcs | Their view and their pokes are right | 1 | normal play, plus one deliberate approach |
| 2 · Record your play | Your decisions are captured correctly | 1 | an evening of normal play, then a 5-minute read |
| 3 · Performance | No frame-rate cost, and our work stays off the busy core | 1 | 10 minutes |
| 4 · One order by hand | An orc lord obeys, and your save stays clean | 1 | about 30 minutes |
| 5 · Opus watches | Answer time and usage while the game runs | 1, plus the agent chat | normal play |
| 6 · Opus commands one lord | The live loop works end to end | 1, plus the agent chat | 30–60 minutes |
| 7 · Opus commands the realm | Opus runs a whole realm through tools, live | 3 | an evening of normal play |

- **Install 1** has been in the game since Oct 7. It now also carries the agent channel, so phases 0 to 6 can all run on it.
- **The agent** for phases 5 and 6 is a separate Claude Code chat: see [AGENT-CHAT.md](AGENT-CHAT.md).
- **Phase 7** needs the first tools, so it waits for install 3 at the end of next week.

Each install is one restart of the game.

## Running install 1 (in the game since Wed Oct 7, 15:33)

Your three newest saves were copied to `C:\BannerlordArchive\save-backups` before the install, and checked identical to the originals.

1. Start the launcher and tick **Bannerlord Agents**, which sits below your usual mods. Then load your campaign. The first load is slow once.
2. Open the console with Alt + ~ and type `agents.status`. It should name the Misty Mountain Orcs and their Warlord, and count their lords and holdings. `agents.help` lists every command.
3. For each phase, the commands are:

| Phase | What to type |
|---|---|
| 1 · Watch | `agents.view` prints the orcs' report and the file it is in. `agents.pokes` lists their last pokes. `agents.vision_check` compares our sight rule with the game's own. A yellow message appears on screen whenever the orcs are poked. |
| 2 · Record | `agents.record show 10` lists your last decisions in plain words. `agents.record off` and `agents.record on` switch it. |
| 3 · Performance | Play 3 minutes, then type `agents.bridge off`; play 3 more, then `agents.bridge on`. `agents.perf` compares the two. |
| 4 · One order | `agents.order Throkmaw garrison Caradhras` (any orc lord and any castle they hold; partial names work, accents optional; where two lords share a name, the console shows each one's id to use instead). `agents.orders` shows how it's going; `agents.release Throkmaw` or `agents.release all` ends it. |
| 5 · Opus watches | Open the agent chat ([AGENT-CHAT.md](AGENT-CHAT.md)). The bridge is already in shadow mode, which `agents.agent` confirms. |
| 6 · Opus commands one lord | `agents.agent live Throkmaw` (any one lord). `agents.agent shadow` goes back, and `agents.release all` hands every lord back to the game. |
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
  - a notice their Warlord would get: a war declared, a holding besieged, raided or taken, an army destroyed, or an offer;
  - a full quiet day.

  Pokes that land close together are merged into one.
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
2. Order him to garrison an orc castle, and watch for three in-game days.
3. Order him to patrol around it.
4. Save the game by hand.
5. Release him.

**Check:**
- He goes there and stays. The game's AI doesn't pull him away, except to fight an enemy attacking the very castle he's guarding.
- After release, he goes back to making his own plans.
- Load the save you made in step 4. The log reports no lord still held by us.
- If his target falls, or he's beaten or captured, the order ends with that reason in the log.

## Phase 5 · Opus 5.5 watches (install 1 and the agent chat)

**Needs once:** the agent chat, opened and seeded as in [AGENT-CHAT.md](AGENT-CHAT.md).

**Built:** the agent channel. The bridge writes the orcs' pokes and report into the agent's folder. The agent answers each poke with an orders file. In shadow mode, every order is checked against the game and receipted, and none is carried out.

**You:** play as usual. Glance at the agent chat now and then.

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
- He follows Opus's orders.
- When a late order is refused, the refusal and its reason show up in Opus's next poke.
- Release hands him back cleanly.

## Phase 7 · Opus commands the whole orc realm (install 3, end of week 2)

**Needs:**
- the tool engine with defend, besiege and garrison (week 2);
- phase 5's usage numbers leaving room in your subscription.

**Built:**
- Every orc lord is under Opus 5.5, and each holds a tool that keeps him acting between answers.
- The tools poke Opus when something big happens.
- The realm's pokes are merged, and the late-order checks are on.

**You:** play an evening as usual, at war with them.

**Check:**
- Their lords keep moving sensibly while Opus thinks.
- Pokes per hour and usage stay within what phase 5 showed is affordable.
- One command hands the whole realm back to the game.

This is the rehearsal for the coalition runs in week 3.
