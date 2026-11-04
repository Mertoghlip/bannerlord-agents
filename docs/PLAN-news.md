# A news wake's context: pinpoint, by the clock of the news

Version 0.14. What I asked for (Nov 2): what is relevant to this specific piece of news should be given and the rest should be queryable, with the reach of each report set by the "clock" for the specific new development. This page is the plan as decided, what was built, and what the tests said.

## Why

A poke's call read about 60,000 to 75,000 tokens: the same whole-realm report every time, plus the instructions. On Opus the size made no measurable difference to how fast it answered (768 answers: correlation with the first word's delay 0.03, with thinking −0.02); what costs time is thinking and questions (9.6 s with no question, 24 s with 2 or 3, 44 s with 4 or more), while the game runs at about 3 in-game hours per 10 real seconds. Size matters for quality (one front's news buried in the others), and it decides whether the local model can run at all (an estimated 20 to 30 s of reading per call on the laptop).

## What was decided

My answers (Nov 2 and 3):

1. **The reach comes from the development's own clock.** One day's march only where no clock runs.
2. **A first look after a load keeps the whole report.**
3. **Tool writing moves to councils only.**
4. **A letter's theatre is the places it names.**

Councils keep the whole picture, unchanged.

## The rule

- **Focus:** where the news happens and who is in it (the place and party it names; else the ids and names in its words).
- **Clock:** the hour the game's own rules say it is decided (`Theatre`, table below).
- **Horizon:** the clock, kept between 6 hours and 3 days; a day's march with no clock; at least a day for an enemy only passing near.
- **The agent's own delay:** each lord is marked against the clock with the answer time counted: "in time even if your orders land late", "in time if your orders land as usual", "likely too late". The times are this session's own (the median and the one in ten of the last 60 fast answers), 7 and 20 in-game hours until five answers are in.
- **Enemies by the same clock:** anyone in view or seen earlier who could be there before it is decided, wherever it is now, and any enemy army marching into the theatre from further off.

| News | Its clock |
|---|---|
| An enemy marching on one of the realm's holdings | Its arrival, plus how long the place holds: until the camp can storm (an enemy stronger than the walls hold against), else the food or the fourth day of siege |
| An enemy marching on another holding the realm knows (a relief of its own siege) | Its arrival |
| An enemy in view, no holding as its aim | When it could reach the realm's nearest holding, at least a day |
| A siege | When a storm is likeliest (the agent's own storm rule when it leads the siege), or the food runs out, whichever is first |
| A battle | When it ends, by the game's rules or its measured rates |
| A village being looted | When its health runs out |
| Hunger at a holding | The days of food left; the realm's towns within three days are shown too |
| A tool's call-back | The lord's arrival, while he marches; else a day |
| A letter; an order ended; a capture; an army broken up | No clock: a day around the places and lords it names |
| Votes, offers, wars, peace alone | No theatre: the realm's diplomacy, purse and sum |
| A first look; a quiet day | No theatre: the whole report |

## What a news wake reads

In order: the story so far; the plan and the lessons whole, and the notes on the enemies and realms of this news only; then the report (`Views.NewsReport`): the news, THE CLOCK, what changed since the last answer, the sieges and battles here, the lords who can be there in time (soonest first, marked), the others who can be there (at most 15, never leaving out an enemy army, with their trails), the holdings here, the ready-made facts, the orders in force here (the others in a few words each), open votes, allies with the letters and plans about this news, the purse in one line, the captured lords held here, the chronicle's last two days about this news, THE REST OF THE WAR, and WHAT YOU CAN ASK.

**THE REST OF THE WAR** names up to seven regions, each after one of the realm's castles or towns, as far apart as can be. Each line gives the lords there by name and id, with their men and order or "free"; the strongest enemies in view there with their ids; the holdings in trouble; and the sieges and battles.

**The questions:** `look_up` (one id in full, enemy castles and towns near the realm's lands included: there were no such pages before), `list` (one kind of thing, all of it, or in one region, or with some words) and `chronicle` (the lines about one lord, holding or realm). They answer from the poke's question book, from the same hour and view as the report, fog-checked like it, or from the realm's chronicle and the agent's notes.

**The instructions:** `warroom-news.md`'s sections take the place of the same sections of `warroom-system.md`: the tool-writing manual and the tool check go, and the look-up files give way to the questions. The system prompt falls from 72,344 to 60,576 characters for Dol Guldur, and from 79,063 to 67,295 for the orcs.

## Results so far

The measures are in [reports/2026-11-03-news-context.md](reports/2026-11-03-news-context.md). In short, on run 20261029-1903-A, from recordings, so with no real roads or siege and battle clocks:

- **Size:** a news wake's first read falls 37%, from a median 64,504 to about 40,500 tokens, over all 768 answers of the run: the report 59% to 65% less, the memory 16% to 60% less, the instructions and tools 16% to 18% less. This plan first said 15,000 to 20,000; the instructions and tools alone are 24,000 to 26,000. With the councils, unchanged, all first reads fall 31%.
- **Coverage:** 57% to 60% of what the agent's orders named was in the theatre in full; 95% was named in the report at all. The plan's bar of about 95% in the theatre is not met, and a wider horizon would not meet it: 70% of the lords missed lay over a day beyond it. The agent uses a news wake to run the whole war. That is why THE REST OF THE WAR names every lord, and the other orders are given in a few words.
- **Fog:** across 1,604 rebuilt wakes, a news report named no id its whole report did not.
- **Replays (12 wakes):** five decisions the same, three the same at the core with more added, two the same main move, two different. The median answer time did not change (23.7 s, 23.3 s), but the total rose 16% and questions rose from 25 to 43, over the plan's limit; the sample had no way-finding questions and no look-up pages, so that is likely too high.
- **The helper:** lighter in theatre mode (95th of a hundred 643 ms, against 810 ms).

Changes the tests made to the first version: the roster and the short orders; each theatre lord's two nearest castles or towns; at least a day's horizon for an enemy only passing near; at most 15 others; an enemy's clock is its arrival at any holding the realm knows, not only its own.

## Switching it on

Each agent's `agent.txt` says `news = full`, `shadow` or `theatre` (`scripts\setup-agent.ps1 -Workspace <folder> -News theatre` writes it; the bridge reads it at each load). Both realm agents are on `shadow` since the install, so the next session in play builds both reports, sends the old one, and records the two side by side.

## What is left

1. **Shadow in play:** the next session compares the sizes on live hours, with the game's real road distances and the siege and battle clocks, which the recorded views did not have.
2. **A test run with agents on both sides and no human,** about 4 real hours, in theatre mode: outcomes, questions per answer (39% of answers ask at least one today), answer time and in-game hours late, against the earlier runs of that kind. Watch for a holding lost to an enemy outside the theatre, and for anything the realm could not know.
3. **The record:** an episode now holds the news report plus every question and answer, so examples become shorter conversations, which suits the local model.
