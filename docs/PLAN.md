# Build plan

Started Tue Oct 6, 2026, the day the proposal went in. This is the working plan: the checkboxes are the to-do list, and it changes as the work does. The proposal-stage plan, with the research questions, experiments and references, is in [proposal/plan-page.html](proposal/plan-page.html).

## Where things stand

- **Proposal:** submitted Tue Oct 6.
- **Checkpoint, Tue Oct 27:** the Mordor, Harad and Rhûn coalition playing on our tools, every decision logged, and first numbers against the game's own AI.
- **Final report and talk, Tue Dec 1:** tool writing, the trained small models, and the full comparison for the everyday gamer.
- **Budget:** about 90 hours. [worklog.md](worklog.md) keeps the count.

## The pieces, and where each one lives

| Piece | What it does | Folder | Language | Starts |
|---|---|---|---|---|
| The bridge (game module) | Runs inside the game. Works out what each faction can see, wakes its agent when something happens, carries out orders, runs the tools hour by hour, and writes its log. | `src/BannerlordAgents` | C# | now |
| The shared core | The message formats and the tool engine: reading a tool, checking it, and running it. Used both by the bridge and by the tests outside the game, so both follow exactly the same rules. | `src/BannerlordAgents.Core` | C# | Oct 12 |
| The war room | The program beside the game where the agents decide. It holds the poke scheduler (a queue per faction, time limits, a fixed order for applying orders, the master log) and the faction agents, and it checks and tests the tools the agents write. | `src/BannerlordAgents.WarRoom` | C# | Oct 19 |
| Message formats | The exact shape of everything the bridge and the war room send each other, written down once. | `protocol/` | JSON | this week |
| Training and serving | Turns the logs into a dataset, fine-tunes the two small models, and runs them beside the game. | `training/` | Python | Nov 2 |
| Analysis | The numbers, charts and the everyday-gamer table for the report. | `analysis/` | Python | Oct 26 |

## Decisions for the build

1. **Its own module.** The project is its own module, "Bannerlord Agents", with its own file, settings and logs. It shares no code with TFAOMFixes and does not need it. The August strategist work is left out entirely.
2. **Measured runs use the pack alone.** Every measured run loads only the base game, TAOM's own modules, and Bannerlord Agents. Your personal mods (TFAOMFixes, the Host troops, Party AI Controls, Improved Garrisons and the rest) change how the game's own AI recruits, garrisons and makes peace, so they would blur the comparison with that AI. Your own campaign keeps its current module list, with Bannerlord Agents switched off. A small script will switch between the two lists (it comes with run control in week 2). Expect one slow first load after each switch while the game rebuilds its shaders.
3. **Everything that runs during play is C#.** The war room is a C# program like the bridge, so the tool engine is a single piece of code used in both places. A tool that passes the checks outside the game behaves the same way inside it. It also means a player would need only the mod and one small program, with no Python. Python arrives in November for training and charts, as the proposal planned.
4. **The bridge and the war room talk over a private connection on the laptop**, one message per line. The game never waits forever: if the war room goes quiet past its time limit, the game carries on with the orders and tools it already has.
5. **The models are reached through Claude Code, running in the background on your subscription.** The war room hands it the prompt and reads back the orders as JSON. There is no API key. The first test of this happens this week, because the time each answer takes sets how long a run lasts and how many runs fit in a week.
6. **The game's own commands do the moving.** Every primitive move maps to a command the 1.4.8 game already has: go to, engage, besiege, raid, defend, patrol, escort and hold. For any commander holding our order, the game's AI is told to stop making its own decisions. Diplomacy uses the game's own war, peace, alliance and call-to-war actions.
7. **We work out each faction's view ourselves.** The game only tracks what *your* party can see. For every faction we apply the game's own numbers, each party's sight range and how hard the other party is to spot, so the agents see by the game's rules, faction by faction.
8. **Logs and data stay off OneDrive and out of git.** The bridge writes to `%LOCALAPPDATA%\BannerlordAgents`. Code and documents go to GitHub. Run logs, datasets and model files don't, because they are too big. Where they are backed up is decided in November (see open questions).

## Three things to prove first

Each of these could change the design if it fails, so each one gets a small test before anything is built on it.

1. **A commander obeys us, and the game's AI stays out of the way.** Order one Mordor lord to a named castle. He goes there and stays for a few in-game days, and TAOM's own AI does not pull him away.
2. **The clock stops while an agent thinks.** The bridge pauses the campaign, keeps it paused so it can't be unpaused by accident, and resumes on command. While we are there, measure how many in-game days pass per real minute at top speed, including with the pack's own time acceleration. The bridge already logs the real seconds each in-game day takes.
3. **One Opus 5.5 answer from a script, on the subscription.** Send a realistic faction report of about 6,000 tokens and get a valid JSON list of orders back. Time it, and note how much of the usage allowance it took.

## Week by week

The weeks and hours are the proposal's. Each week says what gets built, and how we know it's done.

### Week 1 · Oct 5–11 · 10 h · the game can see and speak

- [x] Private repository, the separate module, the build and install script, this plan
- [ ] Prove 1, 2 and 3 above, on a throwaway save
- [ ] **Faction view:** the faction's own parties, holdings and armies; enemies inside its sight; last-seen positions with times; and the ruler's notices (war declared, a holding besieged, raided or taken, an army destroyed, a diplomatic offer)
- [ ] **Pokes:** an enemy army comes into view, the ruler gets a notice, or a full day passes quietly. Pokes that land close together are merged into one.
- [ ] **The faction report:** compact text of about 4–8k tokens, plus the same content as JSON
- [ ] **The log:** one record per poke, as JSON lines
- [ ] Message formats for pokes, reports and log records written down in `protocol/`

**Done when:** a console command prints Mordor's report as Mordor sees it, and a day of play leaves a log of Mordor's pokes and their reasons.

### Week 2 · Oct 12–18 · 12 h · tools and control

- [ ] **The behavior language:** what a tool looks like, with states, conditions, actions, settings and poke rules
- [ ] **The tool engine** in the shared core: read, check, and run every in-game hour, deterministically, with tests that run without the game
- [ ] Our first three tools, **defend, besiege and garrison**, with their poke rules
- [ ] **Primitive moves with faction authority:** an agent can order only its own faction's commanders
- [ ] The game's AI switched off for commanders holding an order, and back on with **free**
- [ ] **Run control:** a fixed starting save, a 60-day limit, the player parked far away, fast-forward, a pause while agents answer, and an autosave so a crashed run can resume
- [ ] The starting save, made with the experiment module list, and the script that switches lists

**Done when:** a scripted run, with no model yet, hands Mordor's lords tools by hand. They behave as the tools say for a few days, and the log shows every tool step and every poke.

### Week 3 · Oct 19–25 · 12 h · the coalition plays

- [ ] The other six tools: raid, hunt, gather, screen, rest, free
- [ ] **The war room:** queues per faction, time limits, a fixed order for applying orders, and the master log
- [ ] **Faction agents:** the prompt (goal, rules, tool library, triggers, view, allies' messages), JSON orders back, the checks, and one retry
- [ ] **The allied channel**
- [ ] **Diplomacy:** actions and pokes, refusing whatever the pack forbids
- [ ] Opus 5.5 agents for Mordor, Harad and Rhûn with stated goals
- [ ] First coalition runs, and stock-AI runs from the same save

**Done when:** a full 60-day coalition run finishes unattended, and its log can be replayed.

### Week 4 · Oct 26–Nov 1 · 12 h · checkpoint, then tool writing

- [ ] **Checkpoint, Tue Oct 27:** first numbers against the stock AI (holdings taken and lost, battles, strength)
- [ ] **Tool writing:** the checker, replay tests on recorded game states, each tool's record, repair and retire
- [ ] Every-move runs, with primitive moves only
- [ ] Overnight data collection

**If we fall behind:** the checkpoint needs only defend, besiege, garrison and gather, the war room, and the log. Diplomacy and the remaining tools can land the week after.

### Week 5 · Nov 2–8 · 10 h · the small models arrive

- [ ] Free disk space, then set up Python (natively or under WSL2)
- [ ] The dataset from the logs, split by run
- [ ] Choose the two students, one that fits an 8 GB card's room and one that fits 16 GB. Serve them locally with answers held to valid JSON.
- [ ] The untrained baseline, Sonnet 5 runs, and the scaling test (1 to 4 agents)

### Week 6 · Nov 9–15 · 10 h · training

- [ ] QLoRA fine-tuning of both sizes, with the game closed
- [ ] Offline agreement with the teacher, per faction
- [ ] The with-library and our-tools-only variants

### Week 7 · Nov 16–22 · 10 h · the gamer comparison

- [ ] Distilled and hybrid coalitions against the stock AI
- [ ] Pause time and usage for each strategist
- [ ] The goal and diplomacy tests

### Week 8 · Nov 23–29 · 6 h · break Nov 23–27

- [ ] Overnight runs finish; the analysis, the figures, and the everyday-gamer table

### Week 9 · Nov 30–Dec 1 · 8 h

- [ ] Final report and talk, Tue Dec 1

## Open questions

**For the build**

- Which TAOM realms are Mordor, Harad and Rhûn in the starting save, who leads them, and who is at war or allied with whom on day one.
- Which diplomatic actions the pack allows, since it fixes some pairs of realms as permanently hostile.
- Whether the game's own AI decides from the full map. That matters for how fair the comparison with agents that see only their own view is.
- Whether 60-day runs on the pack alone stay stable without your personal fixes. We decide only if a run actually crashes.
- Where run logs, datasets and model files get backed up. A private Hugging Face repository is the likely answer; decide in November.
- Whether Anthropic's terms let a game mod use a player's own subscription. That doesn't affect this personal study, but it does affect the everyday-gamer recommendation.
- Ask on Ed whether the Oct 27 checkpoint is a report or a talk.

**For the final report** (carried over from the proposal page)

- Week 2 term: "Geometrically" or "Spherically" Constrained Stochasticity.
- A source for the claim that Bannerlord's campaign AI is rule-based.
- Reference 56's volume and pages; TAOM's full name and link; the Steam video-memory rows.
- Whether to run the second-provider and human-commands stretch arms.

## How we work

- Code and documents live in the private GitHub repository `Mertoghlip/bannerlord-agents`. Clone it on any computer and follow the README.
- Nothing is installed into the game while it is open. The install script refuses.
- Every change that touches the game gets a short in-game test before the next piece is built on it.
- Tests use throwaway saves. The starting save is never played or overwritten, only copied.
- Hours go in [worklog.md](worklog.md). Each gate (the checkpoint and the final) gets a tag in git, so the report can point at exactly what ran.
