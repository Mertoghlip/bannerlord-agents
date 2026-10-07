# CS 329M Project Brief: Bannerlord Agents

Oct 5, 2026 · @Mert Karabiyik

## How to use this brief

The project proposal is due **Tue Oct 6, 2026 at 5:59pm PT**. This brief hands the CS 329M project to a Claude Code session: the course rules, what the proposal must contain, and the plan as it stands on Oct 5.

- **Settled:** the game (Mount & Blade II: Bannerlord), the two-phase idea, a solo project, and the pitch framing (adaptation pillar first).
- **Open:** unit count (3 or 4), the instructor's reply to the pitch, and exact scope. Confirm these with Mert rather than deciding them.
- **Not covered here:** what Mert has already built for Bannerlord on his gaming PC. Inspect that code directly; the plan assumes the project extends it.

## Course context

CS 329M is Stanford's Machine Programming course (Fall 2026), taught by Justin Gottschlich ("Goju") with CA Anni Zimina. It meets Tuesdays 6:00–8:50pm. The [course site](https://sites.google.com/view/gottschlich/mp-stanford-cs-329m) is the source of truth; Canvas is course 233419, and announcements go through Ed.

The course defines machine programming (MP) as any system that assists in automating any aspect of software development. Its core framework is the three pillars, from Gottschlich et al., *The Three Pillars of Machine Programming* (Week 1 reading):

| Pillar | What it means in the course | Course examples |
| --- | --- | --- |
| Intention | Discover what the user wants. "Intention over implementation": the human states the *what*, the machine owns the *how*. | Programming by example (FlashFill), natural language to code, Halide's split of what an operation computes from how it is scheduled |
| Invention | Create the algorithms and data structures that meet the intent. | Program synthesis as search, ML-guided search (DeepCoder) |
| Adaptation | Keep software correct and fast as hardware and environments change: re-implement for new hardware, tune, repair, port. | Autotuning (ATLAS, FFTW), Halide, automatic bug repair, preventing bit-rot |
| Data (underneath all three) | Every pillar depends on data. | Code repositories, synthetic data, model collapse, code's verification advantage (tests can check it) |

Later lectures that touch this project: **Oct 13** principles of data (ground truth, semi-trust, static vs continuous learning); **Oct 27** performance implications of MP (cloud, local, hybrid); **Nov 17** intent-based programming languages.

## Project requirements

The project is 50% of the course grade and runs from the Oct 6 proposal to a Dec 1 report and in-class presentation.

| Deliverable | Due | Weight |
| --- | --- | --- |
| Proposal | Tue Oct 6, 5:59pm PT | 5% |
| Checkpoint ("report" in the schedule and prompt, "presentation" in the grading table) | Tue Oct 27 | 5% |
| Final project report | Tue Dec 1 | 30% |
| Final presentation, in class | Tue Dec 1 | 10% |

Rest of the grade: attendance 20%, engagement 5%, weekly in-class evaluations 30%, plus up to 5% extra credit.

- **Topic rule (Lecture 1):** relate to automating software, keep the scope small (about 10 weeks), and look for new possibilities opened by new capabilities.
- **TA clarification (Sep 30):** the project doesn't have to strictly automate software, but it must connect well to at least one of the three pillars. This is the bar the pitch has to clear.
- **Project types:** build an MP system, or analyze an existing one. The instructor calls unbiased third-party analysis of new MP systems critically important.
- **Teams:** solo or up to 4 people, one submission per team. Mert is doing this project solo.
- **Units:** 3 or 4 per member. A 4-unit student does about 30 extra hours, and a 4-unit analysis project should read like a paper ready for a peer-reviewed conference (submitting it isn't required).
- **AI policy:** AI tools and coding agents are "fully permitted and encouraged" for building the project.

## Proposal requirements

One PDF per team, **3–5 pages not counting references**, worth 5 points, due Tue Oct 6 at 5:59pm PT. (The Lecture 1 slide says 2–5 pages; the Canvas prompt says 3–5. Go with Canvas.)

**Required sections:**

1. Title
2. Team info: each member's name, Stanford email, and 3 or 4 units (solo, so just Mert's)
3. Introduction / motivation, with references for any claim about why it matters
4. Related work, with every claim referenced
5. Week-by-week timeline through the Oct 27 checkpoint and the Dec 1 final, with hours per week and who does what (Nov 23–27 is break)
6. References

**Optional sections** (the prompt calls them especially important for 4-unit students):

- Potential design
- Potential experiments: datasets, baselines, metrics

The Lecture 1 slide's suggested outline is topic, motivation, goal, related work, timeline, design, implementation, experiments, conclusion.

## The idea and the pillar case

Replace Bannerlord's hand-scripted opponents with LLM agents, then distill those agents into a small model that runs locally on a gaming PC. Pitch it as an **adaptation** project first and an **intention** project second.

**Mert's idea (Sep 30):** AI agents play against the player instead of the game's simple deterministic AI. Phase 1 makes Claude Opus 5.5 proficient at the game. Phase 2 uses Opus's gameplay data to train smaller models that run locally on powerful gaming PCs.

| Pillar | How the project hits it | Role in the pitch |
| --- | --- | --- |
| Adaptation | Behavior that only exists on a cloud frontier model gets re-implemented for a consumer GPU that is also rendering the game. Same *what*, new *how* for new hardware, which is the paper's "right algorithm for the right hardware" theme and the Oct 27 cloud/local/hybrid lecture. | Lead |
| Intention | The stock AI is pure implementation: developers hand-coded every rule. Here a designer or player states the opponent they want ("a cautious lord who avoids fights he can't win") and the machine works out the behavior. Halide analogy: Opus defines the *what*, the small model is a new *how*. | Second |
| Data | The training set is Opus's games, Mert's own games, or a mix. Opus's play is semi-trusted, not ground truth, so filter by outcome. Training only on model output risks model collapse; mixing in human play hedges against it. Games share code's verification advantage: a built-in verifier (win or loss). | Foundation |
| Invention | One could argue the agent finds strategies nobody scripted, but the pillar is about creating algorithms and data structures, so it is the weakest link. | Don't lean on it |

**Fallback if the instructor wants a tighter MP fit:** have Opus *write and repair* the C# code that drives Bannerlord's AI, with battles against the stock AI as the test harness. That is program synthesis with an execution check. It was suggested on Sep 30 but not adopted.

## Current plan

Phase 2, moving the agent onto local hardware, is the core of the project. Phase 1, getting Opus to play well, exists to produce training data, so it stays narrow. Scope details below are proposals from the Sep 30 discussion; confirm them with Mert before they go in the proposal.

**Phase 1: Opus as the opponent and the data source**

- The agent plays at the campaign / commander level (decisions between battles), not real-time combat. An LLM can't react at combat speed.
- Keep it narrow, for example campaign-map decisions for one faction.
- The opponent is given a stated intent or persona, which is what makes the intention pillar real.
- Log every decision (game state in, action out) with its outcome, from Opus's games and from Mert's own. Those logs are the dataset.

**Phase 2: adapt it to the gaming PC**

- Fine-tune a small open model that runs locally alongside the game. Train it on Opus's decisions, Mert's own play, or a mix of both, filtered by outcome.
- Optional experiment: train on Opus only, Mert only and the mix, then compare. That tests the data questions directly (semi-trust, model collapse).
- Compare three setups: cloud (Opus), local (small model), and hybrid (local for routine calls, cloud for hard ones).
- Target hardware is Mert's gaming PC. Record its GPU, VRAM and RAM in the proposal.

**What to measure**

| Metric | Why it matters |
| --- | --- |
| Decision quality vs Opus (agreement on held-out game states) | How much behavior survives the move to local |
| Win rate against the stock Bannerlord AI | Whether the agent is actually better than the scripted baseline |
| Latency per decision | Whether it keeps up with the game |
| VRAM use and game frame rate with the model loaded | The cost of sharing one GPU with the game |
| Cost per game (cloud) | Why local or hybrid is worth it |

**Suggested gates for the timeline:** phase 1 running and logging data by the Oct 27 checkpoint; phase 2 and the full comparison by Dec 1.

## Open questions and risks

Three things still shape the proposal: the unit count, the instructor's reaction to the pitch, and exactly which decisions the agent controls.

- [ ] Units: 3 or 4 (solo project; 4 units means about 30 extra hours and the design and experiments sections)
- [ ] Instructor's reply to the pitch below. If he names a pillar to emphasize, lead with it.
- [ ] Scope: campaign level only, and for which faction
- [ ] Inventory what already exists on the gaming PC (mod, game-state access, agent harness) so the timeline starts from it
- [ ] Check Anthropic's terms on training models with Claude outputs before building phase 2 on Opus data
- [ ] Confirm on Ed whether the Oct 27 checkpoint is a report or a presentation

**Risks:**

- **Scope:** this is two projects in one. If phase 1 slips, phase 2 has no data, so keep phase 1 to a small set of decision types.
- **Speed:** LLM calls are too slow for real-time combat, which is why the agent stays at the commander level.
- **Distillation quality:** a small model trained on a limited number of logged decisions may lose most of Opus's skill. That is still a valid finding for an adaptation study, as long as it is measured.

## Pitch to the instructor

The Sep 30 pitch, written to run the idea by Goju before the proposal. Its framing is the one the proposal's motivation section should carry.

> Hi Goju, I'd like to run a project idea by you before the proposal. Mount & Blade II: Bannerlord's opponents run on hand-scripted rules, which is pure implementation. I want to replace them with intent-driven agents: a designer or player describes the opponent they want, and Claude Opus 5.5 plays that role at the campaign level. The core of the project is adaptation. I'd distill Opus's gameplay into a small model that runs locally on a gaming GPU alongside the game, and measure what survives the move (decision quality vs Opus, latency, VRAM, frame rate) across cloud, local and hybrid setups. It also tests the course's data principles, since the training data is semi-trusted model output with a built-in verifier (win/loss). I'd be extending \[what you've already built for Bannerlord\]. Does this fit, and is there a pillar you'd want me to emphasize?
