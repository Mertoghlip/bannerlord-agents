# The next build (0.13): diplomacy, the agents' own tools, and the training record

Written Oct 28 and reviewed by Mert the same night (his decisions below). This is the working plan and the authority for everything in it; [PLAN.md](PLAN.md) points here. Each part has a plain description first and **build notes** after, with the names in the code, for whoever builds it.

**Where it stands (Oct 28, version 0.13.1):** steps 1 to 7 of Part F are built, tested and installed (the record; the language; the agents' own tools with "check a tool"; diplomacy; the allied channel and the lieutenant; the instructions' skeleton; everything together), each in [CHANGELOG.md](CHANGELOG.md). What is built is described in [TOOLS.md](TOOLS.md) (the language and the agents' own tools), [DIPLOMACY.md](DIPLOMACY.md), [LOGGING.md](LOGGING.md#the-training-record-since-013), [WARROOM.md](WARROOM.md) and [LIEUTENANT.md](LIEUTENANT.md). Since then, **0.13.1**: the backup, on the laptop's D: drive (decision 9, decided after the build; C6). Next: step 8 with Mert, then the shake-down (step 9). **On the docket:**
- with Mert (step 8): the instructions' final wording and the doctrine's diplomacy principles (decision 8; the wording was given to him whole in the chat on Oct 28, with points to weigh: the opening's "war council of a realm ruled by its warlord" against Diplomacy's "You rule your realm"; the three diplomacy maxims of DOCTRINE.md to write as principles; an unanswered offer accepted after 12 hours; whether the two example tools besides his circuit steer the agents; the 16,000 characters the new sections add to every call); whether a realm allied to his gets an agent of its own (B5); letters to realms that are not allies;
- only testable in the game (the shake-down): votes held and settled in an agent's realm, offers and their answers, the ruler's choice, mercenaries hired and released, the War of the Ring's answers asked of the pack, the try through the bridge, the lieutenant's tools, diplomacy and letters;
- not built: a setting that is a place or a list (A2; settings stay numbers, and `places` and `lords` carry the lists); the checker's "set before read" (A4: a remembered value read before it is set is none, and the try shows it);
- as built, to weigh in the shake-down: a vote or offer held for an agent is not in the save (a reload drops it); an offer with no answer in 12 hours is accepted, as the game does between the realms it runs; an agent that does not vote leaves its clan out of the count.

**Mert, Oct 28:**
- "Actually, before we do that, lets first implement all of the functionality (diplomacy and the agents being able to write their own tools) into the game and collect with those in action."
- "As an example, the agent might want to keep lords in a specific area to add to its defensive capabilities, or keep them safe as they rebuild etc., whilst also having them recruit, and also having them sell loot at cities as they keep replenishing their money stock (which I believe is also a daily process). It should be able to create a new order like 'recruit in place' where an agent loops around a given list of settlements, visiting each settlement once a day for new recruits, and resting at a castle or a town or maybe patrolling to fight small bandits/raiding lords in the area once it is done with its loop for the day."
- "If the agent wants, it can keep the tool it has written as a one-off order, or if it thinks the tool has reuse value, it can add it to its tool/order library so it is able to reuse it."
- "We will log both my moves and the agents'. We log the agents' moves primarily to be able to train on the stuff that an agent should do which isn't really captured through my gameplay (like tool writing, responses to reports etc.), though if data from me doesn't end up being fruitful, we might just end up training on just the agents' data, so please capture everything from them as well."

**Mert's decisions on this plan (Oct 28), word for word, then what each means for the build:**
1. "If by our own tools you mean the basic tools already given to them, they should still have full access to them. Tool writing should be for custom strategies they want to pursue. I would also give them a couple of examples, including the example I just gave you, in their prompt for inspiration." → Our 20 tools stay fully theirs to order, and their definitions can be read whole (A1); the instructions carry a few example tools, Mert's circuit among them (A3, Part D).
2. "It should be able to write tools, based on my permission." → The lieutenant writes tools too, each one shown to Mert to approve before it runs or is kept (B5).
3. "No, it should have already all of the tools we have built thus far." → No empty libraries: every run starts with our tools and every tool the realm's agent has kept so far (A6).
4. "Yes. The game's hardcoded rules should stay. Another thing we should think about for diplomacy: agents communicating with their allies to coordinate attacks, and the player being able to communicate with their allies (who are agents) through their liutenant." → The War of the Ring's rules stand; the allied channel: letters and joint plans between allied agents, and Mert speaking to his agent allies through his lieutenant (B5).
5. "Good." → An agent ruler may overrule its clans at the game's price.
6. "Good, but in the spirit I described in 4." → Letters serve allies' coordination; letters to Mert's realm come through his lieutenant, not a window of their own (B5).
7. "Good." → Tools are written in answers and councils both, councils with the longer time.
8. "Good, but build all of the skeleton first and once all the building is done, I'll review the wording and we'll write the final prompt." → The instructions get working skeleton sections during the build; their final wording, the doctrine's diplomacy principles included, is written with Mert after all the building is done and before any run (Part D).
9. "Keep on the docket for now, build everything first and then propose the backup location you recommend." → After the build, a proposal for the backup's place (C6). Proposed after the build (the laptop's second drive, and a private Hugging Face dataset off the laptop); Mert, Oct 28: "I would keep the data local for now, at the D: drive." Built in 0.13.1.
10. "Build everything out now. We'll do a long run." → Every diplomatic act the game has, mercenary clans and expelling clans included (B2).
11. "We'll compare everything however we please to spot the added value of new capabilities." → No rule on which runs compare with which.

**What this build is for:** the research questions it serves are RQ2 (can the model write tools that pass the checks and match ours, and does a tool library carry its skill to a smaller model), RQ5 (do goals survive distillation, in war and in diplomacy) and RQ6 (does Mert's play make the agents better), and the training sets of November (Opus only, Mert only, mixed).

## Contents

1. [Where we stand](#1-where-we-stand)
2. [Goals, and what "done" means](#2-goals-and-what-done-means)
3. [Part A: the agents write their own tools](#part-a-the-agents-write-their-own-tools)
4. [Part B: diplomacy](#part-b-diplomacy)
5. [Part C: the training record](#part-c-the-training-record)
6. [Part D: what the agents are told](#part-d-what-the-agents-are-told)
7. [Part E: tests](#part-e-tests)
8. [Part F: order of work](#part-f-order-of-work)
9. [Part G: collecting, after the build](#part-g-collecting-after-the-build)
10. [Part H: risks](#part-h-risks)
11. [Decided, and still open](#decided-and-still-open)
12. [What stays out](#what-stays-out)

---

## 1. Where we stand

- **Tools (since 0.4):** every agent order is a move or a tool from our library of 20 (defend, besiege, recruit, sell, garrison, gather, ...). A tool is a small JSON state machine: settings, remembered values, states, and rules checked once an in-game hour against the realm's own view (never the game itself), each rule able to change state, make a move, set a value and call the agent back. Every order also carries the safety rule (shelter or run from danger) and the food rule. A checker reads every tool before it runs. The language and the library are in [TOOLS.md](TOOLS.md).
- **The agents' answers** are one JSON object per poke or council: `poke`, `orders`, an optional `plan` and `lessons` ([WARROOM.md](WARROOM.md)).
- **Diplomacy:** none for the agents. The game's own AI makes every realm's wars, peace, alliances and votes; for the player's realm when an agent rules it, `Votes` settles the realm's votes as for a computer realm. The agents hear of wars, peace and alliances as notices. The doctrine keeps its diplomacy maxims for later ([DOCTRINE.md](DOCTRINE.md), "the agent has no diplomatic orders yet").
- **What is logged already:**
  - **Mert's play** (the recorder, since Oct 7): his party's moves, attacks, sieges, raids, armies formed and joined, his proposals, and his orders to vassals through Party AI Controls, each with what he could see and a realm summary (`data\<campaign>\record\decisions.jsonl`, raw clicks in `clicks.jsonl`, entering and leaving in `events.jsonl`). 120 records so far, of which about 90 are really his (Oct 8 to 23); the rest leaked in from the agent runs (below).
  - **The agents:** every call's prompt, Claude Code's whole event stream and the answer (`logs\warroom*\calls\<date>\<time>-poke-N\`), one line per call with tokens and timings (`calls.jsonl`), each system prompt by its hash, every poke with the realm's whole view and report (`data\<campaign>\pokes_<realm>.jsonl`), the orders, receipts and the tools' steps and endings (the session trace, and the run's `events.jsonl`), the questions to "ask the way", "battle odds", "roads in", "can catch" and "engagement" (`routes.jsonl`), the memory (story, notes, chronicle).
  - **The runs:** the day lines, every battle, holding, siege and lord taken (`runs\<run>\`).
- **What is not logged, or not well enough,** is listed in [Part C](#part-c-the-training-record).
- **Disk:** C: has 48 GB free of 952 (96% full). Our folders hold about 3.3 GB (data 2.0 GB, sessions 0.6 GB, the three war rooms' logs 0.7 GB).

## 2. Goals, and what "done" means

1. **The agents write tools:** an agent can write a new order in the tool language in an answer or a council, have it checked and tried before it runs, use it once, or keep it in its realm's own library and use it again by name, change it (a new version) and retire it. Every tool written, every check, every use and what came of it is recorded.
2. **The agents do diplomacy:** an agent that rules a realm can propose war, peace (with tribute), an alliance, a call to war, accept a call to war and propose a trade agreement, vote (and, as ruler, decide) on its realm's votes, fiefs and policies included, answer offers made to it, hire and let go mercenary clans, and write letters and joint plans to its allies' agents (and, through the lieutenant, to and from Mert). Everything goes through the game's own decisions, costs and rules, and the pack's (the War of the Ring). Everything is recorded.
3. **Everything both sides do is recorded,** linked from what was seen to what was done to what came of it, tagged by run and arm, and packed per run for the dataset builder in November.
4. **Done when:** all tests pass (smoke test, war room self-test, dashboard self-test, the speed test), a real short call writes and checks a tool, a shake-down run in the game shows tools written and kept, a diplomatic act carried out, and the run's bundle holds every record with nothing missing by its own check. Then version 0.13.0 is installed and the collection runs begin.

---

## Part A: the agents write their own tools

### A1. What the agent can do

- **Our tools stay fully its own** (decision 1): it orders all 20 as now, and since 0.13 it can also read each one's whole definition (a look-up file per tool, `tools\<name>.json` in its channel, read on demand), so a tool of its own can start from one of ours. Tool writing is for the strategies of its own that ours do not cover.
- **Write a tool** in its answer, in the same language as ours: settings, remembered values, states and rules, using our moves and facts (A2).
- **Use it once:** the orders in the same answer name it, and it runs like any tool. It is kept in the record, not in the library.
- **Keep it:** with a name and a line on what it does, it goes into the realm's own library, listed in every call from then on beside ours, and any later answer can order it by name with its own settings.
- **Change it:** writing a kept tool's name again with a new definition makes a new version. Orders already running keep the version they started with; new orders get the new one.
- **Retire it:** it leaves the library; orders running it finish as they are.
- **Try it before it runs:** a new question in the war room ("check a tool", beside "ask the way" and "battle odds") runs the checker on a definition and tries it on the realm's newest hour for a named lord: which rule would fire now, the first move, any expression that cannot be worked out, and each problem with its place in the definition. The agent can fix and ask again within the same call. A tool that fails the checker is never run; the answer's receipt says why.
- **See how its tools did:** each kept tool is listed with its record: times used, how its runs ended (finished, failed, replaced, the lord taken or killed), call-backs per day, and over its runs what changed for its lords (men, gold, strength), battles fought and won, lords taken and lost.

It cannot replace or change our tools (it can write its own instead), it cannot reach beyond the moves and facts the language has (so a tool sees only what the realm sees, and does only what our moves do), and the safety and food rules apply to every tool, its own included.

### A2. The language grows

What Mert's example needs and the language lacks today, and what we add (the example is written out in A3):

- **Lists of places and lords.** A tool's order can take a list of places (`places`) besides `at`, `target` and `lords`, and a setting may be a place or a list. Functions on lists: how many (`count`), the first, the item at a position, whether one is in it, the next one after one (going round), the nearest of them, and the next one "due" (below).
- **Remembering per place or per lord.** A remembered value can be a table keyed by a place or a lord: `"set": {"visited[stop]": "now"}`, read back as `visited[stop]` (none when never set). That is what "visit each settlement once a day" needs.
- **Time of day and "due":** `hour_of_day`, `day`, `hours_since(x)`, and `next_due(places, visited, 24)`: the nearest place of the list not visited in the last 24 hours, or none when all are done for the day.
- **Moves it lacks:**
  - `recruit_here` / `recruit at X`: go to one place and take the volunteers there by the recruit order's own rules (his standing, his gold, the 80% line, his clan's troop lines), then stop; not the recruit order's tour of the neighbourhood.
  - `sell at X`: go to a town or castle and sell loot only, as the sell order does (prisoners handed over by the game's rule).
  - `wait N`: stay inside the settlement he is in for N hours (resting), then go on.
  - Our other moves stay as they are: goto, enter, near, toward, leave, patrol, defend, engage, follow, join, besiege, raid, post, shadow, hold.
- **Facts it lacks:**
  - about the lord: `gold`, `loot_value` (what his loot would fetch), `prisoners` and `ransom`, `wage_cap_reached`, `ranks_full` (the 80% line and his clan's lines);
  - about the last stop: `recruited_last` (men taken at the last recruit stop), `sold_last` (gold from the last sale), `arrived` (he is at the current move's place);
  - about the area: `weakest_bandit_near(place, r)` and `bandits_near(place, r)`, beside the enemy-lord functions we have, so a tool can hunt small bandits and raiders and leave armies alone;
  - about money: `clan_gold`, and the realm's purse lines the report already gives.

Mert's note that money comes in daily is right: the game pays wages every day and the clan's leader tops a lord up daily (TOOLS.md, "Money"). Tools see it through `gold` and `hours_since`.

### A3. Mert's example, written in the language (an example in the instructions, and a test)

A **circuit** order: a list of places to recruit at, each once a day, a home holding, selling loot in a town when it is worth it, and between rounds either resting at home or patrolling there against small bands. By Mert's decision 1 it is one of the few example tools in the agents' instructions, for inspiration (Part D), and the test suite runs it on a made-up world. The other examples are written in the build, each a different kind (one that waits on an event, one that moves several lords), and shown to Mert with the instructions' wording.

```json
{ "about": "recruits at each of its places once a day, sells loot in a town when worth it, and between rounds rests at home or patrols there against small bands",
  "takes": { "places": "a list of the realm's or allies' towns, castles and villages", "at": "home: a castle or town of the realm" },
  "settings": { "sell_above": [3000, 0, 100000, "sell loot once it is worth this much"],
                "patrol": [1, 0, 1, "1: patrol around home between rounds, striking small bands; 0: rest inside"],
                "strike_below": [0.5, 0, 2, "strike a band at most this strong against the lord"],
                "radius": [15, 3, 40, "the patrol's reach around home"] },
  "init": { "visited": "{}", "stop": "none" },
  "start": "next",
  "states": {
    "next": { "about": "choosing the next stop",
      "when": [ { "if": "loot_value >= sell_above and nearest_town_of_realm() != none", "go": "sell" },
                { "if": "next_due(places, visited, 24) != none", "go": "recruit", "set": { "stop": "next_due(places, visited, 24)" } },
                { "if": "true", "go": "between" } ] },
    "recruit": { "about": "recruiting at {stop}", "do": "recruit at stop",
      "when": [ { "if": "arrived and hours_in_state >= 1", "go": "next", "set": { "visited[stop]": "now" } },
                { "if": "not friendly(stop)", "go": "next", "set": { "visited[stop]": "now" }, "poke": "{stop} is no longer ours or an ally's; skipped." } ] },
    "sell": { "about": "selling loot", "do": "sell at nearest_town_of_realm()",
      "when": [ { "if": "arrived", "go": "next" } ] },
    "between": { "about": "between rounds at {at}", "do": "enter at",
      "when": [ { "if": "patrol == 1", "go": "watch" },
                { "if": "next_due(places, visited, 24) != none", "go": "next" } ] },
    "watch": { "about": "patrolling around {at}", "do": "patrol at",
      "when": [ { "if": "next_due(places, visited, 24) != none", "go": "next" },
                { "if": "weakest_bandit_near(at, radius) != none and strength_of(weakest_bandit_near(at, radius)) <= strength * strike_below",
                  "go": "strike", "set": { "target": "weakest_bandit_near(at, radius)" } } ] },
    "strike": { "about": "striking {target}", "do": "engage target",
      "when": [ { "if": "gone(target) or hours_in_state >= 6 or distance(target, at) > radius", "go": "watch" } ] } } }
```

The tests check that it passes the checker, visits each place once a day on the made-up world, sells when its loot passes the bar, patrols and strikes only small bands, shelters from an army by the safety rule, and calls back only when a place is lost.

### A4. Checks before a tool runs

1. **The checker** (today's, extended): its parts are known (settings with default, min, max and a line; states; rules; moves; functions with how many values each takes; names), every state reachable from the start, every `go` names a state, values set before they are read or given in `init`, settings kept to their ranges, and the limits (A5). Each problem names where it is ("state recruit, rule 2: next_due takes 3 values, given 2").
2. **The try** (new): on the realm's newest hour, for a named lord: works out every rule of the start state and says which would fire, the first move and its place, and any expression that cannot be worked out (none where a place was expected, an unknown lord). It does not move anyone.
3. **When the answer arrives,** the bridge runs the checker again, then each order's own checks as for our tools (the lord leads a party, the places are of the kind the tool takes). A tool that fails is not kept and its orders are refused, with the reasons in the receipts.

### A5. Guards while a tool runs

- **Fog by construction:** tools run on the realm's view, as ours do; the language has no way to the game itself.
- **Safety and food rules:** always on, as for ours (`caution` 0 still switches safety off, and the agent is told it did).
- **Limits** (to be confirmed in testing): 12 states, 40 rules, 600 characters an expression, 8 settings; at most 3 state changes a lord an hour (a rule that would change state more often in one hour waits for the next); at most 6 call-backs a tool a day (after that its call-backs are held and summed in one at the end of the day, and the agent is told); a tool's hourly work counted, and a tool that takes over 5 ms an hour for a lord several hours running is stopped with an alarm and a call-back.
- **The library:** at most 30 kept tools a realm; names of letters, digits and underscores, never one of ours.
- **Nothing in the save:** the library and the tools' states live in the agent's folder and our files, as our tools' do.

### A6. The realm's library

- Kept in the agent's channel folder (`library\`), one file per version, with an index: name, version, what it does, written at which poke or council, checks passed, and the record (A7).
- Read by the bridge at every load and whenever an answer keeps, changes or retires a tool; listed in the realm's `tools.txt` under **YOUR OWN TOOLS**, after ours, with each one's settings, what it takes, its call-backs and its record. `tools.txt` is part of the cached system prompt, so a change to the library costs one fresh cache write; the library changes rarely.
- **It carries over** (decision 3): a new campaign, a reload and a new test run from TEST START all keep it, so every run starts with our tools and every tool the realm's agent has kept so far. Each run's setup records the library as it stood at the start (names, versions, hashes), and each tool's record says on which runs it was used, so the comparisons can tell what the kept tools added.
- **Each realm's own:** a realm's agent keeps its tools for its realm. The lieutenant's kept tools are the player's realm's library, shared with that realm's own agent when it plays (C1, C2), since both command the same realm.

### A7. Each tool's record

For every run of every tool (ours too, which gives the comparison RQ2 asks for): who wrote it (ours, or the agent at which poke), its version, the lord, the settings, when it started and ended and how, the states it went through with their hours, its call-backs, and over its run what changed for its lord (men, wounded, gold, loot, strength, food), battles fought and won, lords taken and lost, holdings gained and lost near its places. Summed per tool and version for the library listing, and kept whole in the record (Part C).

### A8. Answering with a tool

```json
{ "poke": 812,
  "tools": [ { "name": "circuit", "keep": true, "about": "one line: what it does", "definition": { "...": "settings, init, start, states" } } ],
  "orders": [ { "lord": "lord_MM2_3", "do": "circuit", "places": ["village_MM2_1", "castle_MM5"], "at": "town_MM2", "sell_above": 2500,
                "why": "rebuild near the capital and keep its villages clear" } ] }
```

- `keep: false` (or no name) is a one-off: it runs for this answer's orders and is recorded, not listed.
- `{"name": "circuit", "retire": true}` retires one.
- The war room's guard against runaways (16,000 characters) is raised for answers with tools, and tool writing is welcome in councils (longer calls, more thinking: decision 7).

### Build notes (Part A)

- **Language:** `Tools/Expr.cs` (lists, indexing `a[b]`, the new functions; values gain a list type and a table type), `Tools/ToolDef.cs` (the format's new parts: `takes` with `places`, settings that are places or lists, the limits; the checker's new checks and its located messages; `Library` gains per-realm tools: `Library.For(realm)`, versions), `Tools/Runner.cs` (tool instances hold their definition object and version; the hourly limits; the call-back limit; per-tool timing), `Tools/World.cs` (the new facts: gold, loot value, prisoners, ransom, wage cap, ranks full, recruited and sold last, bandits near; from rows the hourly copy already has or gains in `Capture`/`Purse`), `Orders.cs` (`ToolMove` for `recruit at` one stop: reuse `RecruitTour`'s buying with a one-stop route; `sell at`: reuse the sell tool's entry sale; `wait N`).
- **The order path:** `AgentOrder` and the answer reader gain `tools`; the bridge's answer handling (`Orders.FromAgent`) processes `tools` before `orders` in the same answer: check, keep or retire, write the library, then the orders. Receipts for each tool ("kept circuit v2", "refused: ...").
- **The library store:** a new `Tools/OwnLibrary.cs` (the channel folder's `library\`: index, versions, records), read at load (`Bridge.Start`) and after each change; `AgentChannel` writes `tools.txt` per channel with the realm's own section (today one text for all channels, line ~155), and each of our tools' whole definition as `tools\<name>.json` in every channel (decision 1), refreshed when the DLL's library changes. The lieutenant's library is the player's realm's, the same store the kingdom's own agent uses.
- **The try:** the war room's tool server (`RouteTool`, its MCP tools list) gains `check_tool(definition, lord?)`. The war room links the game-free `Tools/` code (the checker runs in process); the try needs the realm's newest hour, so it is asked of the bridge as the council's briefing is (`orders\toolcheck-request-<id>.json` with the definition and the lord; the bridge's helper answers `toolcheck-<id>.txt` on its urgent lane; the war room waits up to 10 s, else answers with the checker's result alone and says the try was not available).
- **The record:** `Runner` writes each run's start, steps, call-backs and end with the lord's numbers at start and end into the run's `events.jsonl` and a new `toolbook.jsonl` (Part C); `OwnLibrary` sums them for the listing.
- **Tests:** the circuit tool of A3 and others on the smoke test's made-up world (Part E).

---

## Part B: diplomacy

### B1. What the game has (1.4.8, read in its code)

- **Votes in a realm** (`KingdomDecision`): declare war (`DeclareWarDecision`), make peace with a daily tribute for so many days (`MakePeaceKingdomDecision(proposer, other, dailyTribute, days)`, applied by `MakePeaceAction.ApplyByKingdomDecision`), start an alliance (`StartAllianceDecision`), ask an ally to join a war (`ProposeCallToWarAgreementDecision`), accept an ally's call (`AcceptCallToWarAgreementDecision`), a trade agreement (`TradeAgreementDecision`), a policy (`KingdomPolicyDecision`), who gets a holding (`SettlementClaimantDecision`, with its preliminary vote), expelling a clan, choosing a new ruler. Each has the game's influence cost (`DiplomacyModel`, `AllianceModel`, `TradeAgreementModel`) and is voted on by the realm's clans.
- **The other side:** the player gets the game's own offer windows (`PeaceOfferMapNotification`, `AllianceOfferMapNotification`, the call-to-war offers); computer realms judge by the game's own scores (`GetScoreOfDeclaringPeace`, `IsPeaceSuitable`, `GetScoreOfStartingAlliance`, `CanMakeAlliance`, `GetScoreOfJoiningWar`, `CanMakeTradeAgreement`). How each offer is accepted between two computer realms is to be read in `AllianceCampaignBehavior`, `TradeAgreementsCampaignBehavior` and the peace path before building.
- **The pack:** TAOM's War of the Ring table decides some wars over the stance (`FactionManager.IsAtWarAgainstFaction` asks the diplomacy model first), and TFAOMFixes' ring wars copy Gondor's wars into the player's realm and declare them daily, with "no peace while it rages" (memory: TFAOM ring wars). So peace between the Kingdom of Arnor and Gondor and the orcs or Dol Guldur will not hold while the war rages, whoever proposes it. **These rules stay** (decision 4: "The game's hardcoded rules should stay"); the agents are told which acts the War of the Ring forbids, so they do not spend influence on them. Peace and alliances with the minor realms and the clans at war (Lothlórien's, Erebor's, Gundabad, Rohan and the rest) are possible.
- **Mercenary clans** (`DiplomacyModel.GetScoreOfKingdomToHireMercenary`, `GetScoreOfMercenaryToJoinKingdom`, the game's hiring and its daily pay) and **expelling a clan** (`ExpelClanFromKingdomDecision`) are in too (decision 10); their paths are read before building, as the rest.

### B2. What an agent can do

As the ruler of its realm (a live agent; never in shadow; the lieutenant on Mert's word, B5):
- **Propose:** war on a realm; peace with a realm at war, with a daily tribute either way and how many days; an alliance; a call to an ally to join one of its wars; a trade agreement; a policy (adopt or abolish); expelling a clan. Each is the game's own vote in its realm, proposed by the ruler's clan at the game's influence price, voted by its clans as the game does.
- **Hire and let go mercenary clans,** at the game's own terms and pay.
- **Vote:** on every open vote in its realm (holdings to give out, policies, war, peace, alliances, expulsions), as the ruler's clan, with the support weight the game allows.
- **Decide as ruler:** when a vote closes, the ruler may overrule the clans at the game's influence price, as a player ruler can (decision 5).
- **Answer offers:** another realm's offer of peace, an alliance or a call to war comes as a poke; the agent accepts or declines in its answer (accepting is its realm's vote, as the game does it).
- **Talk with its allies** (decisions 4 and 6): letters and joint plans, below in B5.

Each diplomatic act is checked when it arrives: the realms exist, the act is open to them now by the game's own conditions (at war for peace, at peace for war or an alliance, an ally for a call to war, the game's limits and cooldowns), the pack allows it (the War of the Ring), and the ruler's clan can pay the influence. A refused act has its reason in the receipts, as for orders.

### B3. What the agent sees

- **DIPLOMACY** in the report and the council's briefing, from what the realm's diplomacy screen shows its ruler: every realm it knows of with its stance (war, peace, alliance, call to war, trade agreement), since when, the war's tally (casualties, sieges, raids, holdings taken each way), tribute paid and received and for how long, the realms' strength (as ENEMY REALMS gives it now), the ruler's influence and what each act would cost, and which acts the War of the Ring forbids. Nothing of the other side's own reckoning (how willing it is): a ruler learns that by asking.
- **OPEN VOTES** in its realm: each with its options, the support so far, when it closes and what voting or overruling would cost.
- **Letters received** and offers made to it, in WHAT CHANGED and as triggers.
- **New triggers for a poke:** an offer to the realm; a vote opened or about to close; a letter; a war, peace, alliance or call to war involving the realm (today's notices, which already poke).

### B4. Mert's realm when he plays (A and B)

The agents' offers to him arrive as the game's own windows (peace, alliance, calls to war). Their letters come through his lieutenant (B5). His diplomacy is his own, through the game's screens, and the recorder keeps it (Part C).

### B5. The allied channel, and the lieutenant

Mert, decision 4: "agents communicating with their allies to coordinate attacks, and the player being able to communicate with their allies (who are agents) through their liutenant."

- **Letters between allies:** an agent writes to an allied realm's agent (an alliance, or a realm fighting the same enemy in a call to war); a few lines, delivered in that agent's next poke as a trigger, saying who wrote and when; the answer may reply. A letter binds no one and carries no orders: each agent commands only its own lords.
- **Joint plans:** a letter can carry a plan in a fixed form, so both sides see the same thing: what (a siege, a field attack, holding a place, raiding), where, when (an in-game day and hour, or "when your army is within a day"), with what (each side's part in words and strength), and its state (proposed, agreed, declined, under way, done, dropped). Agreed plans appear in both realms' reports under **ALLIES AND JOINT PLANS**, beside each ally's stance, its wars, and what it has said of its forces; the record keeps every plan and what came of it.
- **What an ally knows:** only what it is told. A letter may pass on what the writer's realm saw; nothing else is shared between realms (the fog rule holds for each).
- **Who can be written to:** agents of allied realms; and Mert's realm, through his lieutenant, when Mert plays it. A letter to a computer realm has no reader and is refused with that reason. (Letters to realms that are not allies are a later question.)
- **Mert through his lieutenant:** what he says to the lieutenant about his allies ("tell them to besiege Lasgalor with me on the 12th") becomes a letter or a joint plan to that ally's agent, under the lieutenant's yes rule (on his word it goes; on the lieutenant's own idea it waits for his Approve). Letters and plans from his allies arrive in the lieutenant's window, which tells him what they say and what they ask, and his reply goes back the same way.
- **The lieutenant writes tools** (decision 2): a tool it writes, on Mert's word or its own idea, is shown in its window with what it does and its settings, and runs or is kept only once Mert approves; it may order kept tools and carry out the realm's diplomacy on his word, under the same yes rule.
- **Which realms have agent allies today:** the orcs and Dol Guldur are at peace with each other and may ally; the Kingdom of Arnor and Gondor's possible allies (Rohan, Lothlórien's and Erebor's clans and the rest) are computer realms. For Mert to talk to an agent ally through his lieutenant, one of those realms needs an agent of its own: a new agent folder (`scripts\setup-agent.ps1`, same instructions as the others), which realm being Mert's choice before the long run.

### Build notes (Part B)

- **Read first:** the acceptance paths of peace, alliance, call to war and trade agreements between computer realms and with the player (`AllianceCampaignBehavior`, `TradeAgreementsCampaignBehavior`, `KingdomDecisionProposalBehavior`, the offer notifications), how a vote's ruler decision and overrule are priced (`KingdomElection`, `Supporter`, `DecisionOutcome`), the cooldowns (`DiplomacyModel`), and TAOM's diplomacy model's War of the Ring check (decompile the installed TAOM DLL) and TFAOMFixes `RingWars`.
- **Game side:** a new `Diplomacy.cs` (game thread): carries out the acts by adding the game's own decisions to the realm (`Kingdom.AddDecision`, the ruler's clan as proposer, the game's influence cost), sets the ruler clan's support on open votes, applies the ruler's overrule at the game's price when a vote ends for a realm whose agent asked to decide, and routes offers to agent realms as pokes (a Harmony hook where the game asks a computer realm to judge an offer from another realm, when that realm has a live agent; the answer comes back within the game's own time window, and the game's own judgment stands if the agent has not answered in time). `Votes` keeps settling the player's realm's votes when nobody rules it (C0).
- **The checks:** `Diplomacy.Check(act)`, the game's own conditions and the pack's, the reason worded for the receipt.
- **The answer:** `diplomacy` (a list of acts: `propose_war`, `propose_peace` with `tribute` and `days`, `propose_alliance`, `call_to_war` with `against`, `accept_call`, `decline_call`, `accept_peace`, `decline_peace`, `accept_alliance`, `decline_alliance`, `propose_trade`, `propose_policy` with `adopt` or `abolish`, `propose_expel` with `clan`, `hire_mercenary` and `release_mercenary` with `clan`, `vote` with `vote_id` and `option`, `decide` with `vote_id` and `option`) and `letters` (`to`, `text`, optional `plan` with `what`, `where`, `when`, `parts`, and `plan_id` and `state` to answer or update one); read in the war room's answer parser and by the bridge's answer handling beside `orders`.
- **Mercenaries and expulsion:** read the game's mercenary hiring and pay (`ChangeKingdomAction.ApplyByJoinFactionAsMercenary`, the mercenary award, `ClanFinanceModel`'s pay) and the AI's own hiring behaviour before building; the agent's hire uses the game's own terms.
- **The report:** `Views` gains DIPLOMACY and OPEN VOTES (from rows `Capture` copies hourly: stances, war tallies `StanceLink`'s numbers, tribute, alliances and calls to war, trade agreements, open decisions with supporters), the fog check learns them (they are the ruler's own screens, so their ids are allowed).
- **Pokes:** `Notices` gains offers, votes opened and closing, letters; the war room's `urgent` and `council_after` words gain them where fitting.
- **Letters and joint plans:** `Allies.cs` (new, worker): who is allied with whom (alliances, shared wars in calls to war), the letters' and plans' store per campaign (`allies.jsonl`), each plan's state; written by the bridge into the receiving channel's next poke (a `letter` trigger, the text in WHAT CHANGED) and the run's events; `Views` gains ALLIES AND JOINT PLANS. To the player's realm: into the lieutenant's channel (`Lieutenant`, its chat box and window), never stopping the clock.
- **The lieutenant:** its answers gain `tools`, `diplomacy` and `letters`, all through `LieutenantRules.Gate` (on Mert's word they go; on its own idea they wait for Approve); a tool it writes is shown in `LieutenantWindow` with its `about`, settings and the checker's result before Approve; its library is the player's realm's (A6). `PacRules` stays for Party AI Controls' orders.
- **A fourth agent** for an ally of Mert's realm, if Mert chooses one: `scripts\setup-agent.ps1` with the same instructions, `scripts\arm.ps1` learning it, `Runs` listing it among the run's realms.

---

## Part C: the training record

### C1. Principles

- **Everything, from both sides:** what each side saw, what it did, and what came of it, linked by ids.
- **Tagged:** every record says its run, its arm, who commanded each realm, the module's version and the library's version.
- **Nothing in the game changes** for the record, and the game's thread only copies; the writing is on the helper thread, as now.
- **Off OneDrive and out of git** (plan, decision 12). Packed per run (C5).

### C2. Mert's moves

**Fixes:**
1. **Only Mert's moves:** a record is kept as his only while he rules his realm himself (arms A and B, and his own campaign). In the agent runs the game's player clan is still his, so today the realm's proposals in those runs and Party AI Controls' own refill trips (Aragorn, Oct 25) were credited to him: 37 proposals and 7 orders of the 120 records. The old records are marked by their times against the runs' spans, not deleted.
2. **The input with each move:** the realm's report as of that hour (the same text an agent of his realm would read), kept with each move, plus one copy each in-game hour during A and B (for the moments he chose to let things run). The hourly report is already built every hour while his lieutenant is on (Brain, line ~383); with the lieutenant off it is built at each move only.
3. **Party AI Controls' own trips** (its recruiting refills) are no longer counted as his orders.

**New kinds of moves** (the ones the agents also make):
- **In towns and castles:** troops recruited (where, which, how many, what they cost), prisoners and loot sold or handed over, troops left in or taken from a garrison, his wage settings, food bought.
- **The realm:** his votes on every vote (not only his own proposals), fiefs he hands out, policies, mercenaries hired and let go, his answers to offers (peace, alliance, call to war) in the game's windows, and what he tells his allies through his lieutenant and what they answer.
- **Armies:** lords called later, an army disbanded, a lord sent away.
- **On the map:** fights declined or fled (leaving an encounter without a battle), waiting in a town and for how long, a siege stormed at once or waited out, a sally.
- **His lieutenant:** each thing he says and what it did (already in the lieutenant's folder), linked into the record by time.

**Still not recorded** (no agent makes these choices): inside battles, trading goods, troop upgrades, buildings, companions, marriages, the character's skills.

### C3. The agents' moves

**Kept already** (C1's list in [section 1](#1-where-we-stand)), and to add:
1. **Run tags** on every call, poke, answer, receipt and tool step (today the run is found by time spans).
2. **The episode index** (`episodes.jsonl`, one line per answer): the poke (number, triggers, the report's hash and where it is), the call (its folder, kind, model, effort, tokens, seconds, retries, failure), the answer (orders, tools, diplomacy, letters, plan, lessons), each order's receipt (applied, refused and why), each order's tool run (its id in the toolbook), each diplomatic act's outcome, and, once known, what came of it (C4).
3. **Outcomes per order:** when an order ends, the lord's numbers at its start and end (men, wounded, gold, loot, strength, food), its battles (won, lost, men lost on each side), lords it took or lost, holdings gained or lost at its places, how it ended, hours held. Per council: the same summed over its orders.
4. **The toolbook** (`toolbook.jsonl`): every tool written (in answers, and each try asked of "check a tool" within a call), every check and its problems, every repair (the next version or the next try), every keep, change and retirement, every run and its outcome (A7).
5. **The diplomacy book** (`diplomacy.jsonl`): every act proposed, its check, the vote (support for each option, the clans' votes), the outcome, the influence spent, what followed (tribute paid, war tally after 5 and 10 days), every offer received and the answer, every letter (from, to, text, read in which poke), every joint plan with each change of its state and what came of it (the battles and sieges at its place in its time, each side's part kept or not).
6. **Memory at each call:** the story, notes and chronicle as the call saw them are already in its prompt; their versions are also kept whole when they change (notes and story history), so a training example can be rebuilt without the prompt.
7. **Failures kept whole:** failed and stopped calls, retries, runaways, unreadable answers, usage limits, with their event streams (kept today; the index points to them).
8. **The model's thinking:** the event stream today carries an empty thinking block (the text is not returned). Find whether Claude Code can return the thinking or its summary in the stream; if it can, keep it. Either way the orders' `why`, the answer's `plan` and its `lessons` are the reasoning we keep, and the instructions keep asking for a `why` on every order and diplomatic act.
9. **The library's version and the instructions' hash** with every call (the system prompt is kept by hash already).

### C4. What came of it: outcomes joined

At each order's end (C3.3) and at the end of each in-game day (the realm's day line), the bridge writes the outcomes into the index. The dataset builder can then weigh or filter examples by what followed, for Mert's moves as for the agents' (joined by time and lord from the run's battles, holdings and day lines).

### C5. The run's bundle

At a run's end (and with `Runs.exe bundle <run>` for any run, older ones included): everything of the run in one packed file in its folder, `bundle.zip`, with a manifest (each file, its lines, its hash, the time span) and a check that nothing expected is missing (every answer's call folder, every poke the index names, every receipt):
- the run's folder (run.json, setup, day lines, events, report);
- for each agent: its call folders and `calls.jsonl` lines within the run, `routes.jsonl` lines, the system prompts used, its library's versions, its memory files' versions, `episodes.jsonl`, `toolbook.jsonl`, `diplomacy.jsonl`;
- the bridge's pokes (view and report) within the run, per realm;
- Mert's records within the run: moves, clicks, events, hourly reports, the lieutenant's chats.

### C6. Disk

- Per run, raw: about 0.1 GB of calls (1,081 calls in C1 at ~100 KB), 0.3 GB of pokes with views, 0.07 GB of hourly reports in A and B; text packs about five to eight times.
- With 48 GB free, a pack of about 0.1 GB a run is fine, but the raw files stay too. Nothing is thinned or deleted without Mert's word.
- **The backup** (decision 9; 0.13.1): Mert chose the laptop's second drive ("I would keep the data local for now, at the D: drive"), its own disk, with 789 GB free on Oct 28. Everything is copied to `D:\BannerlordAgents-backup` at every run's end, after the bundle, and by hand (`Runs.exe backup`): the records folder (7.2 GB on Oct 28) and every agent's folder (80 MB). It never deletes, adds only the new bytes of a file that grew, and keeps the old copy of a file that shrank or was rewritten ([LOGGING.md](LOGGING.md#the-backup-since-0131)). The sizes per run are measured on the shake-down, as planned; a copy off the laptop (the private Hugging Face dataset proposed) can follow later.

### C7. The dataset builder (November, outline only)

In `training/` (Python): read bundles only; build examples in one form for all: the system prompt (by hash), the message (memory, why asked, what changed, orders in force, facts, report), and the answer (orders, tools, diplomacy, letters, plan, lessons); for Mert's moves, his realm's report at that hour as the message and his moves, translated into the agents' order language by a fixed table, as the answer (with "let it run" examples from his quiet hours, at the rate the agents answer with no orders); split by run; check every example's report against the fog rule; the three sets (Opus only, Mert only, mixed), plus tool-writing examples (the situation, the tool written, its checks and repairs) and diplomacy examples.

### Build notes (Part C)

- **Recorder** (`Recorder.cs`): `On` also needs the player to rule his realm (`Hero.MainHero` is his kingdom's ruler or a lord of it and no live agent commands it; `Runs` knows the ruler); a `Run`/`Arm` on each `DecisionRecord`; the report at the move (worker: `Views.Report` on the latest view, kept in `record\reports\<hour>.txt.gz` keyed by hour and named in the record), hourly copies when the run's ruler is "you"; new hooks: `OnTroopRecruitedEvent` (the player's party), the prisoner and loot sales (our `PrisonerSalePatch`/`LootSalePatch` measure them already for lords), garrison transfers (`OnTroopsTransferred`/party screen done), wage changes, `KingdomElection` support chosen by the player, the offer windows' accept and decline, `PlayerEncounter` left without battle, siege assault started, settlement wait (the menu's wait). `PacReader` changes made by Party AI Controls' own refill marked and left out.
- **Agents' side:** `Brain.Poke` and the answer handling stamp the run; `Episodes.cs` (new, worker) writes the index lines as pokes, answers, receipts and endings come; `Runner` ends carry the outcome numbers; `Diplomacy` writes its book; the war room adds `run` and the library version to `calls.jsonl` and keeps notes/story versions (`MemoryStore`).
- **The bundle:** `Runs.exe bundle` and at the run's end (the day-60 save offer), `System.IO.Compression.ZipFile`, the manifest, the missing-check; `Runs.exe report` gains a "record" section (counts per kind, anything missing).
- **Thinking:** try the war room's call with Claude Code's settings for thinking display in a `toolcheck`; keep what comes.

---

## Part D: what the agents are told

- **Skeleton first, final wording last** (decision 8): during the build the instructions get working skeleton sections, enough for the tests and the toolcheck call to exercise every new part. Once all the building is done, Mert reviews the wording and we write the final prompt together, the doctrine's diplomacy principles included, before any run.
- **The test-pollution rule** (Mert, Oct 23: "I don't want to give it the strategy that I just gave you word for word") still governs strategy: the instructions say what the agent can do and how, not what to do. By Mert's decision 1, his circuit tool is an exception he chose: it goes in as an example tool, for inspiration, beside a couple of others. The instructions stay the same for every realm's agent.
- **New sections** in the instructions template and the realm agents' copies (backups first, as before):
  - **Writing your own tools:** what a tool is, that ours remain theirs to order and read whole (A1), the language in full (types, moves, facts, functions with their values, the limits), how to try one ("check a tool"), keep, change and retire it, how the record is shown, and the example tools (A3).
  - **Diplomacy:** the acts, what they cost, who decides, how offers come and go, that the War of the Ring forbids some, and how the realm's votes, mercenaries and expulsions work.
  - **Allies:** letters and joint plans (B5), what an ally knows and does not.
  - The answer's form gains `tools`, `diplomacy` and `letters`.
- **The doctrine:** its diplomacy maxims (DOCTRINE.md: sow discord among allies, accept peace on good terms, Kekaumenos on paying tribute) become principles, from their classical sources, as the other principles are, written with Mert at the end.
- **The lieutenant's instructions** gain tools (written only with Mert's approval), diplomacy and letters to his allies on his word (B5).
- **Never promise a tool the call does not have:** "check a tool" is offered only when the war room serves it, as "ask the way" is.

## Part E: tests

- **Smoke test** (made-up world, outside the game):
  - the language: lists, tables, `next_due`, the new facts and moves, the checker's new checks and their messages, the limits;
  - the circuit tool of A3 end to end (once a day each, sells over the bar, patrols and strikes small bands only, shelters from an army, calls back when a place is lost);
  - kept, changed (two versions running side by side), retired, the library listing and records;
  - a tool that fails the checker: refused with its reasons; a tool over the limits;
  - diplomacy checks on a made-up world (at war, at peace, allies, the War of the Ring rule, influence, mercenaries, expulsion), the answer's `diplomacy` and `letters` read and receipted;
  - the allied channel: a letter and a joint plan from one agent realm to its ally, delivered in the next poke, answered, agreed, shown in both reports, and recorded; a letter to a computer realm refused; a letter to the player's realm delivered to the lieutenant;
  - the lieutenant: a tool it writes waits for Approve, runs after it, and is refused on Decline;
  - the recorder's new kinds and its "only when Mert rules" rule; the episode index, toolbook and diplomacy book lines; the bundle and its missing-check.
- **War room self-test:** "check a tool" (checker only, and with a stub bridge answering the try), answers with tools, diplomacy and letters parsed, the runaway limit for answers with tools.
- **A real short call** (`WarRoom toolcheck`): a small model writes a two-state tool, checks it, fixes it and answers.
- **The speed test** (`SmokeTest --perf`): within budget with 30 kept tools running.
- **Game references check** (RefCheck): every new game member exists.
- **In the game, a shake-down** (Part G).

## Part F: order of work

Each step ends with every test green, a commit and push with its change-log entry, and an install only with the game closed.

1. **The record first** (Part C: the recorder's fixes and new kinds, run tags, the episode index, outcomes, the bundle), so everything after is captured. Version 0.13.0-pre1.
2. **The language grows** (A2) with its tests, the circuit tool among them.
3. **The agents' own tools** (A1, A4 to A8): the answer's `tools`, the library, the checker's messages, "check a tool" with the bridge's try, the guards, the toolbook, `tools.txt` per realm.
4. **Diplomacy** (Part B): read the game's paths first, then the acts (mercenaries and expulsion included), checks, votes and ruler's say, offers to agents, the report's sections, pokes, the diplomacy book.
5. **The allied channel and the lieutenant** (B5): letters, joint plans, ALLIES AND JOINT PLANS, the lieutenant's tools under Approve, its diplomacy and letters on Mert's word.
6. **The instructions' skeleton** (Part D): working sections for every new part, enough for the tests and a real toolcheck call.
7. **Everything together:** all tests, the docs (TOOLS.md, WARROOM.md, LOGGING.md, LIEUTENANT.md, a new DIPLOMACY.md, MEMORY.md where needed, PLAN.md's checklist), version 0.13.0, install, push.
8. **With Mert:** the instructions' final wording and the doctrine's diplomacy principles (decision 8); the backup's place proposed (decision 9; done: D:, built in 0.13.1); whether a realm allied to his gets an agent of its own (B5).
9. **The shake-down** in the game (Part G), fixes, then the long run.

This is the biggest build since 0.4: several long sessions. Each step stands on its own, so Mert can stop after any step.

## Part G: collecting, after the build

1. **Shake-down** (about 10 in-game days, arm C1 so all three agents are live): tools written, checked, kept and used; a diplomatic act carried out and one refused; letters and a joint plan between the orcs and Dol Guldur (once allied); the bundle complete. Fixes from it before the long run.
2. **The long run** (decision 10) and the runs after it, on 0.13, compared with every earlier run however serves (decision 11: "We'll compare everything however we please to spot the added value of new capabilities"): C1 (all three agents: the most tool writing and diplomacy), A (Mert against the agents: his moves with the new record, the agents' diplomacy toward him, his allies through his lieutenant if one has an agent), and C2 and C0 again as the comparisons need.
3. **Usage:** C1 cost $351 at list price on 0.12.5 and hit the session limit once. Tool writing and diplomacy add thinking and a longer library. Councils take most of the tool writing; effort and thinking caps stay as they are unless the shake-down says otherwise.

## Part H: risks

- **Usage limits** mid-run (as on Oct 25): the runs need a pause-and-resume that loses nothing (it does today: pausing the game pauses the run).
- **Disk** (45 GB free on C: on Oct 28): the bundles; the backup is on D: since 0.13.1 (C6).
- **Bad tools** wasting lords: the checker, the try, the limits, the safety rule, the record shown to the agent.
- **Diplomacy against the pack:** peace with the orcs and Dol Guldur cannot hold while the War of the Ring rages (B1); the agents are told, so they do not spend influence on it.
- **The game's own AI in agent realms** still proposes and votes for its other clans; the agent rules with its ruler's say, not alone.
- **Saves:** diplomatic acts change the game as the game's AI's would (wars, peace and votes are the game's own state); our records and the library stay out of the save.
- **Training data balance:** the agents' records will far outnumber Mert's; the plan keeps them separate sets so either can be used alone.

## Decided, and still open

**Decided by Mert on Oct 28:** the eleven decisions at the head of this plan, each with what it means for the build.

**Still open, for after the build** (Part F, step 8):
- the instructions' final wording and the doctrine's diplomacy principles, written together (decision 8);
- whether a realm allied to Mert's gets an agent of its own, and which, so he can speak to an agent ally through his lieutenant (B5);
- letters to realms that are not allies (B5 keeps them to allies and Mert's lieutenant for now).

## What stays out

- Battles themselves (the game's own battle AI or auto-resolve), trading goods, troop upgrades, buildings, companions, marriages and the character's skills, for agents and recorder alike.
- Anything that stops the game's clock for an agent.
- The small models and their training (November; only the record they need is built here).
