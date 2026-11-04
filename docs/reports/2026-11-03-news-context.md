# A news wake's context: the first measures (Nov 3)

The tests of [the plan](../PLAN-news.md) that need no game, on run 20261029-1903-A (arm A, 60 days). In-play tests are still to come.

## The coverage check

Every recorded news wake of the run was built again from its recorded view, and the lords, holdings and enemies its fast answer's orders named were looked for in it. Built from a recording, so a march's hours are the straight line times 1.3 at the lord's pace, and sieges and battles have no clock (the record holds none before 0.14), so those wakes took a day's march.

| | Dol Guldur | Misty Orcs |
|---|---:|---:|
| News wakes | 662 | 942 |
| Report, median characters: whole, then news | 61,831, then 21,534 (35%) | 66,533, then 27,399 (41%) |
| Largest news report | 49,080 | 58,223 |
| Named by the orders of its answers | 807 | 1,612 |
| Of them in the theatre in full | 57% | 60% |
| Of them named in the news report at all | 95% | 95% |
| Answers whose every name was in the theatre | 51% | 48% |
| Ids a news report names that its whole report does not | 0 | 0 |

In the theatre in full, by kind of news (Dol Guldur, orcs): raid, battle or capture 78%, 97%; order ended 54%, 58%; tool call-back 54%, 57%; enemy in view 58%, 41%; letter 51%, 81%.

**The plan's bar of about 95% is met only for "named at all", not for "in full".** Widening the horizon would not fix that, because most misses are not near the edge:

- Of 514 lords missed, 362 (70%) lay more than a day beyond the horizon.
- 257 of those 514 already held an order; the rest were free lords.
- 306 of the 480 holdings and enemies missed were named by an order already in force.

The agent uses a news wake to run the whole war, not only to answer the news. After a shadow's report on Aragorn (Dol Guldur #2452), it sent Dramthak's army, 196 hours off, against an unrelated weak enemy; after one lord fell back (#2007), it reordered six weak lords across the realm.

So the first version of the report grew a short roster, which took "named at all" to 95% for about 3 KB: every lord by name and id in THE REST OF THE WAR, the strongest enemies of each region, the other orders in a few words each, and each theatre lord's two nearest castles or towns.

Miss counts: Dol Guldur 190 lords, 109 holdings, 46 enemies or places (55 of the lords holding an order; 103 of the holdings and enemies named by an order in force); orcs 324 lords, 238 holdings, 87 enemies or places (202; 203).

## The replays

Twelve recorded wakes (7 of Dol Guldur, 5 of the orcs, of six kinds) answered again by the same model and effort, as news wakes, each with the memory its original call read, cut to the news. Against the original answers:

| | Original | News wake |
|---|---:|---:|
| Tokens read on the first step, median | 62,040 | 38,468 (38% less), with the way-finding section and tools these replays lacked added back |
| Answer time, median | 23.7 s | 23.3 s |
| Answer time, all twelve together | 415 s | 483 s (16% more) |
| Longest answer | 78.9 s | 120.6 s |
| Answers that asked at least one question | 7 of 12 | 7 of 12 |
| Questions in all | 25 | 43 |

| Wake | Kind | Original | News wake | Decision |
|---|---|---|---|---|
| Dol Guldur #2041 | enemy in view | 73 s, 5 questions, 3 orders | 121 s, 5 questions, 8 orders | Same three armies on one withdraw bar; adds a second force at Glad Thaw |
| #2007 | tool | 20 s, 2, 6 orders | 17 s, 0, 3 orders | Differs: fewer lords, other places |
| #2212 | capture | 18 s, 0, 1 order | 20 s, 0, no order | Differs: the sale of the loot not ordered |
| #2310 | letter | 7 s, 0, 1 order | 10 s, 2, 1 order | Same |
| #2344 | order ended | 79 s, 4, 3 orders | 79 s, 9, 5 orders | Same main move (Zulgora defends Westoft); the rest differs |
| #2373 | enemy in view | 42 s, 3, 2 orders | 27 s, 2, 2 orders | Same main move (Zulgora to Westoft); shadow against garrison |
| #2452 | tool | 35 s, 5, 1 order | 55 s, 8, 3 orders | Same strike on the same enemy; adds two defends |
| Orcs #5460 | enemy in view | 73 s, 4, 20 orders | 71 s, 8, 23 orders | Mostly the same; Naglur at another place |
| #5559 | tool | 7 s, 0, 1 order | 7 s, 0, 1 order | Same |
| #5592 | letter | 22 s, 0, 7 orders | 50 s, 9, 7 orders | Same seven orders |
| #5598 | battle | 14 s, 0, 2 orders | 18 s, 0, 2 orders | Same |
| #5679 | order ended | 25 s, 2, 1 order | 11 s, 0, 1 order | Same |

Five the same, three the same at the core with more added, two the same main move, two different. **The plan's limit of no more questions than today is not met in this sample:** 3.6 a wake against 2.1, and they cost time.

What this sample cannot say:

- It is twelve wakes, and no original was repeated, so how much the same call differs from itself is not known; the two "different" ones may be no more than that.
- The replays had none of the way-finding questions (no battle odds, way or chase), and their `look_up` found no full pages, since recordings keep none. All 18 of their look-ups got only list lines. So the question count is likely too high.
- It had no real road distances or siege and battle clocks.
- Nothing can say here whether the extra orders, or the missing ones, were better or worse.

## How much the context falls

**A news wake's first read falls by 37%, from a median 64,504 to about 40,500 tokens,** over all 768 fast answers of the run. This holds only in theatre mode, which no agent is on yet. The plan's 15,000 to 20,000 was wrong, and so was the 36,000 first written here: those replays had no map, so they lacked the way-finding section of the instructions and five tool definitions, about 2,500 tokens.

How it was worked out:

- **Before:** each call's own token count, from its record.
- **The fixed part:** real calls with a one-line message under each set of instructions and tools, each realm's library as it stands now.
- **The message:** 0.456 tokens a character (2.19 characters a token), from five real news calls with the whole tool set.
- **After:** the fixed part, plus the call's recorded message with its report swapped for the news report and its notes and chronicle lines cut to the news.
- **Check:** on those five real calls this lands within 240 tokens of the true count.

| Tokens, the median wake | Dol Guldur | Misty Orcs |
|---|---:|---:|
| Report | 27,340 to 9,670 (65% less) | 29,728 to 12,048 (59% less) |
| Memory (story, notes, chronicle lines) | 4,161 to 3,490 (16% less) | 9,891 to 3,983 (60% less) |
| Instructions and tool definitions | 28,994 to 23,903 (18% less) | 31,477 to 26,381 (16% less) |
| Whole first read | 60,654 to 37,305 (38% less) | 69,505 to 42,778 (38% less) |

System prompt in characters: Dol Guldur 72,344 to 60,576, the orcs 79,063 to 67,295.

By kind of news: tool call-backs and ended orders 40% less, enemies in view 39%, raids, battles and captures 38%, letters 35%, a first look 8% (it keeps the whole report).

- **The whole run:** councils keep the whole picture, at a median first read of 71,697. All first reads together, 142 councils and 768 answers: 60.7 million to 42.1 million tokens, 31% less.
- **Within a call (the twelve replays):** questions add to the context. The largest context in a call falls from a median 63,932 to 42,086, 34% less, but two wakes that asked much came back near the original: 60,917 to 57,567, and 63,671 to 55,260. All steps of all twelve together: 2.29 million to 1.24 million tokens.

What this cannot say:

- It comes from recordings, so it has no real road distances and no siege or battle clocks. A long clock widens a theatre up to three days, so a live news report may be larger.
- The fixed part uses the realms' libraries as they stand at the end of the run, so early wakes are slightly overstated.
- The local model's target of 8,000 to 16,000 is still far off: the instructions and tool definitions alone are 24,000 to 26,000.

## The helper

`SmokeTest --perf` on the hours saved at the end of the run, three realms, each agent in the given mode:

| News mode | Whole hour, median | 95th of a hundred |
|---|---:|---:|
| full | 262 ms | 810 ms |
| theatre | 193 ms | 643 ms |

Budget 1,000 ms. Theatre mode is lighter since it writes no look-up files per wake (1,640 ms over the test, against 80 ms for the question book and 42 ms for the news reports).

## Found on the way: a bug older than this work, fixed in 0.14.1

`Room.Section` looked for a section's end with a blank line in "\n" form, but the report's lines end in "\r\n", so it read every section to the end of the report. Two things read through it:

- **Urgency:** its words were searched in the whole report. Of the run's 768 written answers, 654 counted as urgent (the larger thinking cap, 5,000 against 3,000); by the news alone, 61 would have (Dol Guldur 375 to 41, the orcs 279 to 20).
- **Calling a council:** its words were searched there too, so old chronicle lines still in the two-day history could call one. Of the 142 councils, 47 were called by a word in the report: 31 with a dated chronicle line as the reason ("Summer 20, 1087, 23:00 · Narsha's army has formed"), 16 with the current news. The other 95 sat on the hour or after a first look.

Mert asked for the fix on Nov 3. It holds in every news mode, so runs from now on think less on most pokes and may sit fewer councils than run A: a difference from its baseline, beside the news report.
