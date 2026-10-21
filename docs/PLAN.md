# Build plan

Started Tue Oct 6, 2026, the day the proposal went in; updated Thu Oct 8. This is the working plan: the checkboxes are the to-do list, and it changes as the work does. The proposal-stage plan, with the experiments in full and the references, is in [proposal/plan-page.html](proposal/plan-page.html).

## Where things stand

- **Proposal:** submitted Tue Oct 6. The repository and the separate module were set up Oct 6–7.
- **Now:** the first tests, on the Misty Mountain Orcs in your own campaign. They are laid out phase by phase in [FIRST-TESTS.md](FIRST-TESTS.md). Since Oct 8 (version 0.4) the tools, armies and war room are in, so Opus 5.5 commands the whole orc realm live (phase 7), a week ahead of the plan.
- **Checkpoint, Tue Oct 27:** the Mordor, Harad and Rhûn coalition playing on our tools, every decision logged, first numbers against the game's own AI, and a first look at your recorded play.
- **Final report and talk, Tue Dec 1:** tool writing, the trained small models, what your play adds, and the full comparison for the everyday gamer.
- **Budget:** about 90 hours. [worklog.md](worklog.md) keeps the count.

## What changed since the proposal

| Change | Why | Effect on the study |
|---|---|---|
| The clock never stops while an agent thinks | You'd rather play without pauses, and so would any player | Turn-based mode is dropped. Answer time becomes part of the result: in-game hours lost, and orders that arrive too late. |
| The first tests run on the Misty Mountain Orcs, in your own campaign | You're at war with them, so you can watch them while you play | Mordor, Harad and Rhûn stay the coalition for the measured runs |
| Your own play is recorded and becomes core data | A skilled player's decisions, with real outcomes, are the most trustworthy data we can get | New research question 6. The proposal's "human-data mix" stretch item becomes core. |
| The bridge stays off the game's busy core | In big battles the game keeps one processor core near full and uses about 15% of the 24 overall. The campaign map gets measured in the first tests. | New condition: a small model on the idle processor cores instead of the graphics card |
| Two stretch items dropped: the every-move student, and one adapter per faction | To make room for the changes above | None for the core questions |

## Research questions

One line each; the proposal page has the detail.

- **RQ1.** Where should the line between model and code sit: our tools, the model's own tools, or the model making every move?
- **RQ2.** Can the model write tools that pass the checks and match ours, and does a tool library carry its skill to a smaller model?
- **RQ3.** Which strategist is most feasible for an everyday gamer? It weighs quality, answer time in live play, usage limits and hardware.
- **RQ4.** How many agents can one gaming laptop serve beside the game, on the graphics card or on the idle processor cores, and at what cost in frame rate?
- **RQ5.** Do each faction's stated goals survive distillation, in war and in diplomacy?
- **RQ6 (new).** Does your own play make the agents better? This covers examples and a playbook for Opus 5.5, and training data for the small model, alone or mixed with Opus's.

## The pieces, and where each one lives

| Piece | What it does | Folder | Language | Starts |
|---|---|---|---|---|
| The bridge (game module) | Runs inside the game. Works out what each faction can see, spots the moments that need a decision, carries out orders, and runs the tools hour by hour. | `src/BannerlordAgents` | C# | now |
| The recorder (part of the bridge) | Logs your decisions on the campaign map, with what your faction could see at that moment, in the same format as the agents' records | `src/BannerlordAgents` | C# | now |
| The shared core | The message formats and the tool engine, which reads, checks and runs a tool. Used both by the bridge and by tests outside the game. | `src/BannerlordAgents.Core` | C# | Oct 12 |
| The agent channel (part of the bridge) | Files through which an agent plays a realm: pokes and reports in, order files out, a receipt for every order | `src/BannerlordAgents` | C# | Oct 7 |
| The agent chat | A separate Claude Code chat that played the orcs through the channel in the first session. Retired Oct 8. | outside the repository; templates in `agent/` | — | Oct 7 |
| The tool engine (part of the bridge, for now) | The behavior language, its checker, the order library and the runner that carries out tools hour by hour | `src/BannerlordAgents/Tools` | C# | Oct 8 |
| The war room | The program beside the game where the agents decide: a queue per faction, the calls to the models, the checks, the master log, and tool writing. It took over the agent channel from the chat on Oct 8, for one realm. | `src/BannerlordAgents.WarRoom` | C# | Oct 8 |
| Message formats | The exact shape of everything the bridge and the war room send each other, written down once | `protocol/` | JSON | this week |
| Training and serving | Builds datasets from the logs and your recordings, fine-tunes the small models, and runs them beside the game | `training/` | Python | Nov 2 |
| Analysis | The numbers, charts and the everyday-gamer table for the report | `analysis/` | Python | Oct 26 |

## Decisions for the build

### Where it runs

1. **Its own module.** "Bannerlord Agents" has its own file, settings and logs. It shares no code with TFAOMFixes and doesn't need it. The August strategist work stays out entirely.
2. **Two setups.**
   - **Your campaign:** your usual mods plus Bannerlord Agents, which records your play and hosts the tests on the orcs.
   - **Measured runs:** only the base game, TAOM's own modules and Bannerlord Agents, from a fixed starting save. Your personal mods change how the game's own AI recruits, garrisons and makes peace, so they would blur the comparison with that AI.
   - A small script switches between the two module lists (week 2). Each switch costs one slow load while the game rebuilds its shaders.
3. **Nothing of ours goes into your save.** The mod keeps its orders, tool states and records in its own files. There is one catch, found in the game's code: the switch that stops a lord making their own decisions is saved with the game. So the mod hands its lords back to the game just before every save, autosaves included, and takes them back right after. Every save stays one the game could have made without the mod, so switching the mod off is always safe. After loading any save, all orders are cleared.

### How it runs

4. **The clock never stops for an agent.** Agents answer while the game runs.
   - While an agent thinks, its lords keep doing what they were doing: their tool, or the game's own AI.
   - Every order is checked again when it arrives: the target still exists and is still hostile, and the lord is still free. A late order that gets refused is reported to the agent at its next poke.
   - Measured runs use one fixed game speed, so answer times compare across runs.
   - If the report later needs judgment measured apart from speed, a few controlled runs could stop the clock. That would be your call, and never in your own play.
5. **The game's busy core stays free.**
   - On the game's own thread, the bridge only takes a quick copy of the numbers it needs. The aim is well under a millisecond per in-game hour.
   - Everything else runs on background threads: working out views, spotting pokes, building reports, recording your play, writing logs, and talking to the war room.
   - Those threads and the war room are marked as low-priority background work. Windows then keeps them on the laptop's 16 efficiency cores, away from the performance core the game depends on.
   - This gets measured, not assumed (first tests, phase 3), because the processor and the graphics card share one power budget.
6. **Everything that runs during play is C#.** The war room is a C# program like the bridge, so the tool engine is one piece of code used in both places. A tool that passes the checks outside the game behaves the same way inside it. A player would need only the mod and one small program. Python arrives in November for training and charts.
7. **The bridge and the war room talk over a private connection on the laptop,** one message per line. Neither waits on the other: the game carries on whatever the war room is doing.
8. **The models are reached through Claude Code, on your subscription,** with no API key. The war room hands it the prompt and reads the orders back as JSON.
   - For the first session (Oct 8), the agent was a separate Claude Code chat ([AGENT-CHAT.md](AGENT-CHAT.md)), with its own folder outside this repository, its own instructions, and no memory of the chat that builds the mod.
   - It plays only through the agent channel: pokes and reports in, order files out, a receipt for every order. That makes it an independent test of the whole loop.
   - **Since Oct 8 the war room program answers instead** ([WARROOM.md](WARROOM.md)), a week early: the chat took a median of 53 s an answer, because its conversation grew with every poke and its thinking had no limit. The war room makes one fresh call per poke, from the same folder and through the same channel, so the independence holds. The agent reads only its channel.
9. **The game's own commands do the moving.**
   - Every primitive move maps to a command the 1.4.8 game already has: go to, engage, besiege, raid, defend, patrol, escort and hold.
   - For a lord holding our order, the game's AI is told to stop making its own decisions. The game still lets such a lord fight an enemy attacking the very place they were sent to; we keep that.
   - Diplomacy uses the game's own war, peace, alliance and call-to-war actions.
10. **We work out each faction's view ourselves,** using the game's own sight rules: each party's sight range, and how hard the other party is to spot. The game itself tracks only what *your* party can see. That gives us a free check, because for your party our rule must agree with the game's.

### What it records

11. **Your play is data.** The recorder works on the campaign map only, never inside battles.
    - **What a record holds:** each decision you make, with your faction's view at that moment and what you'd just seen. The format is the same as an agent's record, and outcomes are attached afterwards, as for the agents.
    - **Which decisions:** you rule the Kingdom of Arnor and Gondor as Aragorn, so the recorder covers a ruler's decisions as well as your own party's:
      - where your party goes, and what it attacks, besieges, raids, enters or waits in;
      - the armies you form, and whom you call to them;
      - your realm's war, peace, alliance and call-to-war decisions;
      - the orders you give your vassals in conversation: defend a fief, patrol around a fief, patrol their own lands, wait in a fief, go and recruit, or act as they see fit.
    - **Vassal orders without Party AI Controls (checked Oct 7, [notes](notes/vassal-orders.md)):**
      - They can go straight to the lord, with the bridge's own orders, which are the agents' orders. Party AI Controls itself uses the same game commands underneath.
      - `agents.vassal` does this now and records the order with the agents' verbs.
      - Moving the dialogue onto that path is your call.
    - **How dialogue orders are read meanwhile:**
      - Your own mod hands each order to Party AI Controls. The recorder reads it there after every conversation and every in-game hour.
      - It only reads the list of lords who already hold an order. Asking Party AI Controls about any other lord would quietly create a saved entry for that lord, so the recorder never does.
      - This also catches orders you give your clan's parties through Party AI Controls' own screen.
    - **What the records are for:**
      - training data for the small models (Opus only, yours only, or the mix);
      - examples from your own play, picked for the situation at hand, plus a short playbook of how you play, in Opus 5.5's prompt;
      - a new yardstick: how often an agent picks what you picked;
      - stretch: tools written from your demonstrations. That is programming by example, from the course's intention pillar.
    - **Your control:** you can turn it on or off from the console at any time.
    - **Caveat:** you play one realm of the free peoples, while the agents command orc and eastern realms. Your campaign also runs your personal mods, and the measured runs don't.
12. **Logs and data stay off OneDrive and out of git.** The bridge writes to `%LOCALAPPDATA%\BannerlordAgents`. Code and documents go to GitHub. Run logs, recordings, datasets and model files don't. Where they get backed up is decided in November.
13. **Everything the strategists do is logged** (Oct 8, [LOGGING.md](LOGGING.md)). Every poke, answer, order and lord is followed end to end, with heartbeats, alarms the moment a rule breaks, a black box, and a freeze probe that says where the game is stuck. The game's AI's own reasons (the options it weighed, with their scores) are recorded for the watched realm's lords; that is also the baseline for comparing with the game's AI. All of it runs off the game's busy core; the logs report their own cost. The tools, the war room and the chronicle log into the same record.
14. **Each realm remembers the war in writing** (Mert, Oct 9; [MEMORY.md](MEMORY.md)). Every answer stays a fresh, short call, and carries the realm's memory: the story so far, the agent's own notes, and the chronicle lines the story does not cover yet. A conversation that keeps everything would fill up within an hour or two and be squeezed into a summary anyway, and a small local model could not hold it. The memory holds only what the realm learned, and follows the game's own time: an earlier save loaded is an earlier memory. The one-long-conversation memory stays as a setting, to measure the difference (RQ3).
15. **Two speeds of command, and doctrine instead of a plan** (Mert, Oct 9; [WARROOM.md](WARROOM.md#the-council-of-war), [DOCTRINE.md](DOCTRINE.md)). The fast answers react to one poke and change only what it needs. Beside them, a **council of war** now and then sees the whole realm (every lord, the muster at each castle and town, the purse, the captured lords) and may set it all on one course; a fast answer given while the council sits wins for the lords it ordered. The instructions give the agent a commander's persona and doctrine drawn from medieval military writing and campaigns, with sources, but **never this war's plan**: whether to mass, where to strike and what to give up is for the agent to work out (Mert: "It should arrive at that conclusion on its own"). The old "keep every castle and town" goal is gone: holdings are means, and a field army lost for a doomed castle is the worse loss.

## Week by week

The weeks and hours are the proposal's. Each week says what gets built, and how we'll know it's done.

### Week 1 · Oct 5–11 · 10 h · see, record, obey

- [x] Private repository, the separate module, the build and install script, this plan
- [ ] First tests, install 1 ([FIRST-TESTS.md](FIRST-TESTS.md), phases 0–4): the orcs' view and pokes, the recorder, the performance check, and one orc lord ordered by hand. *Built, checked outside the game, and installed Oct 7. Oct 8: phases 0–2 ran (and phase 5), see [the report](reports/2026-10-08-first-live-session.md); phases 3 and 4 still to run. Then 0.3.1 cut the pokes to what a ruler should hear ([the breakdown](reports/2026-10-08-pokes.md)).*
- [x] The faction report, as text and as JSON. One record per poke, as JSON lines.
- [x] Record formats written down in [protocol/README.md](../protocol/README.md)

**Done when:**
- the console prints the orcs' report as they see it;
- an evening of your play leaves a recording you recognise;
- the frame rate is unchanged;
- an orc lord holds a hand-given order for three in-game days.

### Week 2 · Oct 12–18 · 12 h · Opus live, and tools

- [ ] First tests, phases 5–6, with the agent chat (the agent channel came with install 1): Opus 5.5 watching the orcs live without acting, then commanding one orc lord. *Phase 5 ran Oct 8 with the agent chat.*
- [x] The behavior language, and the tool engine, with tests that run without the game. *Version 0.4, Oct 8 ([TOOLS.md](TOOLS.md)). The engine is game-free code inside the bridge for now (`src/BannerlordAgents/Tools`); it moves into the shared core when the war room needs it too.*
- [x] defend, besiege and garrison. *Built Oct 8 with every move (go to, patrol, engage, follow, leave, recruit, hold) and raid, hunt, screen and rest from week 3; each order carries a safety and a food rule.*
- [x] Armies, moved up from week 3: gather, relieve, add, release, disband, by the game's own rules and influence price
- [x] Changing orders without re-planning: adjust, next orders (then), empty answers, and the orders in force in every poke
- [x] Look-ups and ready-made facts: one file per holding, lord and known enemy, and the soonest lords for what a poke is about
- [x] The war room's call path, moved up from week 3: one fresh Claude Code call per poke, with the thinking capped; median answer 15 s against 53 on recorded pokes ([WARROOM.md](WARROOM.md))
- [x] Each realm's memory of the war (Oct 9, version 0.5; [MEMORY.md](MEMORY.md)): the chronicle the bridge keeps, the agent's own notes, and the story so far the chronicler rewrites in quiet moments; cut back when an earlier save is loaded
- [ ] The two memories compared on the recorded session (written against one long conversation): answer time, usage and decisions. *The replay is built; the full run with Opus 5.5 waits for your go-ahead (about 120 calls).*
- [x] What the second session showed was missing (Oct 9, version 0.6): the captured lords (where, by whom, since when), the realm in sum, and the muster at each holding with running totals; a join order, and gather sending far lords to join instead of leaving them out; the garrison and the lords inside counted in a relief by the game's own sally rule; recruitment measured in the logs (gold against the game's two bars, wage room, volunteers waiting, recruits per day)
- [x] The council of war (Oct 9, 0.6): a slower whole-realm call beside the fast answers, and new instructions: a medieval commander's persona and doctrine, from a study of medieval military writing and campaigns ([DOCTRINE.md](DOCTRINE.md))
- [ ] First live session with 0.6: does the council mass the realm, and what does recruitment show?
- [x] The dashboard (Oct 16, 0.7; [DASHBOARD.md](DASHBOARD.md)): a live map in the browser of what each realm sees, built from the very view its report was written from, with what the report left out marked; icons and a legend; the planned ways; the agent's moves beside it; a rewind over every hour of a load
- [x] Ways and crossings (Oct 16, 0.7; [ROUTES.md](ROUTES.md)): the game's walking map copied once per map; a way-finder that takes the game's shortest way, checked against the game's own planned ways; the crossings that matter (bridges, fords, passes) and what closing each would cost; the agent's "ask the way" tool, a real tool in each answer and council, facts only
- [ ] First live session with 0.7: the map copy's time, the accuracy check's numbers, and whether the agent asks the way and uses the crossings. *Oct 16: the map copied in 4 s; the war room froze with the tool and the map had false walls, both fixed the same evening.*
- [x] Watch, shadow and distances by road (Oct 16, 0.7.1): a lord can keep watch on a spot of a road or trail an enemy army at a distance; every distance the agent reads is given as the crow flies and by road (the game's own reckoning)
- [x] Sieges after Hollin Gate (Oct 17, 0.7.2): the siege order's relief alarm counts every enemy lord near the castle or heading for it, not armies only; the report gives an enemy castle's militia beside its garrison; two doctrine principles (16 and 17) on a besieger's scouts and on the weaker side's one decisive blow; where the siege's 165 hours went (answer delay, army pace, a detour, the game's fixed siege preparation)
- [x] The game's clocks (Oct 17, 0.7.3): each march timed by the game's own speed rule (one sum, ground and night added in, speed / 1.2 map units an hour, TAOM's peoples' bonuses, the rest measured hourly) along the game's planned way; each siege's preparations, engines and storm chance; each battle's rounds; in the report (arrivals, SIEGES AND BATTLES), the facts and the "ask the way" tool. Checked on Oct 16's 438 real marches: 2.5% median error on those of 12 hours or more (8.8% before)
- [x] Siege control and battles run ahead (Oct 20, 0.7.4): the agent sets when its siege camp storms (the game decides; once ready with so many engines and so much strength; or never) and, when it begins the siege, the relief that makes it storm at once and the relief that makes it withdraw, acted on the hour they are met; every storm and field battle run ahead before it begins by the game's battle rules, with the enemy's unseen morale as a range; each enemy realm's strength from its encyclopedia page; two new questions for the agent (battle odds, roads in)
- [x] Faction authority: an agent orders only its own lords. **free** hands a lord back.
- [ ] First tests, phase 7: Opus 5.5 commands the whole orc realm with those tools, in your campaign. *Set up Oct 8, 21:14; a first short run on Oct 9 ([report](reports/2026-10-09-second-live-session.md)), which led to version 0.5.1.*
- [ ] Run control: a fixed starting save, a 60-day limit, the player parked far away, a fixed speed, and autosave with resume
- [ ] The starting save for measured runs, and the script that switches module lists

**Done when:**
- a scripted run hands the orc lords tools, they behave as the tools say for a few days, and the log shows every tool step and every poke;
- Opus 5.5 then runs the whole orc realm on those tools for an evening of your play.

### Week 3 · Oct 19–25 · 12 h · the coalition plays

- [x] raid, hunt, gather, screen and rest *(built Oct 8 with the week 2 tools)*
- [ ] Fronts: moving lords between the realm's fronts (put off on Oct 8)
- [ ] The full war room: queues per faction, merged pokes, late-order checks, the master log. *The single-realm call path runs since Oct 8.*
- [ ] Faction agents: the prompt (goal, rules, tool library, triggers, view, allies' messages), orders back as JSON, the checks, and one retry
- [ ] The allied channel. Diplomacy: actions and pokes, refusing whatever the pack forbids.
- [ ] Opus 5.5 agents for Mordor, Harad and Rhûn, live. Stock-AI runs from the same save.

**Done when:** a full 60-day coalition run finishes unattended, with every decision logged.

### Week 4 · Oct 26–Nov 1 · 12 h · checkpoint, then tool writing

- [ ] **Checkpoint, Tue Oct 27:** first numbers against the stock AI, and a first summary of your recorded play
- [ ] Tool writing: the checker, replay tests on recorded game states, each tool's record, repair and retire
- [ ] Every-move runs, and overnight data collection
- [ ] Your playbook: Opus 5.5 reads your recordings and writes down how you play

**If we fall behind:** the checkpoint needs only defend, besiege, garrison and gather, the war room, and the logs. Diplomacy and the other tools can land the week after.

### Week 5 · Nov 2–8 · 10 h · the small models arrive

- [ ] Free disk space, then set up Python (natively or under WSL2)
- [ ] Datasets from the logs and your recordings, split by run
- [ ] Choose the two students, for an 8 GB and a 16 GB card's room. Serve them on the graphics card, and the smaller one also on the idle processor cores, with answers held to valid JSON.
- [ ] The untrained baseline, Sonnet 5 runs, and the scaling test (1 to 4 agents)

### Week 6 · Nov 9–15 · 10 h · training

- [ ] QLoRA fine-tuning on Opus-only, yours-only and mixed data, both sizes, with the game closed
- [ ] Offline agreement with Opus and with you, per faction
- [ ] The with-library and our-tools-only variants

### Week 7 · Nov 16–22 · 10 h · the gamer comparison

- [ ] Distilled and hybrid coalitions against the stock AI, live
- [ ] Opus 5.5 with and without your examples and playbook
- [ ] Answer time, late orders and usage for each strategist. Frame rate with the model on the graphics card and on the processor cores.
- [ ] The goal and diplomacy tests

### Week 8 · Nov 23–29 · 6 h · break Nov 23–27

- [ ] Overnight runs finish; the analysis, the figures and the everyday-gamer table

### Week 9 · Nov 30–Dec 1 · 8 h

- [ ] Final report and talk, Tue Dec 1

## Open questions

**For the build**

- The Misty Mountain Orcs' lords and holdings in your campaign today, and where they border you. Phase 1 prints this.
- How hard the campaign map works the processor and the graphics card. Only battle numbers exist so far; phase 3 measures it.
- Which TAOM realms are Mordor, Harad and Rhûn in the measured runs' starting save, who leads them, and who is at war or allied with whom on day one.
- Which diplomatic actions the pack allows, since it fixes some pairs of realms as permanently hostile.
- Whether the game's own AI decides from the full map, which matters for a fair comparison.
- Whether 60-day runs on the pack alone stay stable without your personal fixes. We decide only if a run actually crashes.
- Where run logs, recordings, datasets and model files get backed up. A private Hugging Face repository is the likely answer; decide in November.
- Whether Anthropic's terms let a game mod use a player's own subscription. That doesn't affect this personal study, but it does affect the everyday-gamer recommendation.
- Ask on Ed whether the Oct 27 checkpoint is a report or a talk.

**For the final report** (carried over from the proposal page)

- The Week 2 term: "Geometrically" or "Spherically" Constrained Stochasticity.
- A source for the claim that Bannerlord's campaign AI is rule-based.
- Reference 56's volume and pages; TAOM's full name and link; the Steam video-memory rows.
- Whether to run the second-provider stretch arm. The human-commands arm is now core.
- A course link for RQ6: the Oct 13 lecture on data (ground truth against semi-trusted model output, and model collapse).

## Drawing board

Ideas kept for later, not scheduled.

- **Command through the great lords** (Mert, Oct 7).
  - An agent commands only a realm's most influential lords. They form armies around themselves to mass a defence or lead an offensive, and every other lord stays with the game's own AI.
  - That means fewer decisions and fewer model calls, and it is closer to how a ruler actually commands.
  - It would add another point on the line between model and code (RQ1).
  - Your own play as Aragorn, commanding vassals in conversation, is exactly this kind of command, so the recorder is already collecting data for it.

## How we work

- Code and documents live in the private GitHub repository `Mertoghlip/bannerlord-agents`. Clone it on any computer and follow the README.
- Nothing is installed into the game while it is open; the install script refuses.
- Tests are bundled so that each install costs one restart. Every change that touches the game gets an in-game check before the next piece is built on it.
- Your campaign save is copied aside before the first test. Measured runs only ever start from a copy of the starting save.
- Hours go in [worklog.md](worklog.md). Each gate (the checkpoint and the final) gets a tag in git, so the report can point at exactly what ran.
