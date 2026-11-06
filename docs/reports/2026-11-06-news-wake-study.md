# News wakes: which are needless, and what each kind needs (Nov 6)

A study of every news wake in run 20261029-1903-A (arm A, 60 in-game days, both agents playing against me), and the new design that came out of it. Claude Code ran the analysis and the replays, read the wakes by hand and drafted this report. I set the questions and made the decisions, which are in [the new design](#3-the-new-design). So far only the follow-up fix is built.

**Words used here:**
- A **wake** (or poke) is the game calling an agent because something happened. The war room answers only the newest one, and news from the ones it skips goes into its next report. Numbers like #6301 are wake numbers.
- A **fast answer** is the model's reply to a wake. A **council** is a longer call with the whole picture, about every 18 to 21 in-game hours in practice.
- The **whole report** is what every wake got in run A. The **news report** is the smaller one built on Nov 3 ([the plan](../PLAN-news.md), [its first measures](2026-11-03-news-context.md)), which was not yet in use in that run.
- **Today** means the way run A ran.

We can stop waking the agent for close to half of what wakes it today. That saves more than any trimming of the report, and the news that matters gets answered about an hour of game time sooner, because the agent is less often busy with something else.

Numbers come from run A's 744 fast answers (24 first looks and one answer with no saved poke are left out), 4,611 usable older answers, and 73 new replays that cost about $24. Everything about the new design is a test on past data. None of it has been played live.

## The short version

- **A third of wakes can simply go.** 264 of 744 wakes ($89.66) carried only order reports and weak enemies passing by. On 198 of them the agent gave no order and wrote no letter.
- **Sightings almost never lead to a strike on that enemy.** Of 1,190 enemy-in-view lines, 4 got a strike on that enemy in the same answer and 16 within a day, counting every order. The older logs agree: 68 of 4,059 lines.
- **Waking less makes the agent faster where it counts.** In a walk through the whole run, the new rule makes about 44% fewer calls. A raid gets answered in about 4.1 hours of game time instead of 5.0, and a siege in 3.3 instead of 4.8.
- **One bug causes a share of the wakes.** A supply or recruit order's follow-up never started on its own: 0 of 92 in the logs. The agent got woken and had to give that order again. Fixed in 0.14.2.
- **The agent needs more than the news, but less than the whole report.** It leans on its lessons, the settings of its orders and clocks of fights. It almost never touches diplomacy, enemy realms or the realm summary. In 34 replays, smaller reports differed from the original about as much as the agent differs from itself. But they save little beyond today's news report, and letters need the whole picture.

| Fast answers over the 60-day run | Calls | Cost | Status |
|---|---|---|---|
| Today: the whole report | 768 | $295.59 | Measured |
| **Wake rule alone, whole report** | about 430 | about $175 | Walked through the run |
| **Wake rule and today's news report, letters keep the whole report** | about 430 | about $145 | Priced from the twelve Nov 3 replays |
| Wake rule and reports by kind, letters keep the whole report | about 430 | about $125 | Priced from this study's replays |

Councils cost another $87.74, which makes the whole run $383 today and about $233 with the wake rule and the news report. Calls include the 24 first looks, which keep the whole report.

## How far to trust it

Some of this is solid and some isn't, so here is the split up front.

| Result | Rests on | Trust |
|---|---|---|
| Which wakes gave orders, letters or nothing; what came of each order; the follow-up bug | The game's own records, checked by id, and the code | **High** |
| Calls saved and news answered sooner | A walk through the run's real news. It gives 816 calls for today against 768 real ones, so it runs about 6% high. | **Fair.** Good for comparing rules. It assumes the agent would answer the same way. |
| Which report sections it used | A script reading the logged thinking. **The first version failed the hand check**: 70% of its marks were right against a bar of 90%, and its verdict held in only 12 of 42 wakes. It was rebuilt. The new version got 85 of 90 name-and-number marks right in a second hand check, which passes. Its softer word-based marks did not pass for orders in force (6 of 14) or sieges and battles (10 of 14), so the tables use names and numbers only. | **Fair for big differences.** Don't read small ones. Only about 40% of the thinking is logged, so low numbers are a floor. |
| Why it gave an order: safety, money, strike | Word matching on the agent's reason for each order. 213 of 905 orders fell into no group. | **Rough** |
| Whether smaller reports give equal decisions | 34 replays against the original answers, and 10 wakes run twice on the same report to show how much the agent varies by itself | **Fair, from a small sample.** Needs a live test. |

## 1. Needless wakes

### The count: what each wake produced

| What the wake produced | Wakes | Cost | Median seconds |
|---|---|---|---|
| Orders | 276 | $116.88 | 22.7 |
| A letter or a deal, no orders | 129 | $51.74 | 15.6 |
| Only rewrote its own notes | 223 | $78.74 | 9.9 |
| Nothing at all | 116 | $36.13 | 8.0 |

**339 of 744 wakes changed nothing in the world, at $114.87.** That's the size of the prize, but a rule can't simply drop them, for two reasons:

- A rule has to decide before the call, from the news alone. The same kind of line sometimes gives orders and sometimes doesn't.
- Wakes that did give orders may still be needless. 310 of the 905 orders name nothing in the news or the skipped news, so the wake only served as a chance to look around.

The notes aren't lost either way, because the same news reaches the next call. Many of them shouldn't exist at all. A typical one, from #6128: *"Small Rohan lord (69 men, 97) riding to Oreham inside Rohan's crowd... Bait: leave him alone."* The Orcs' notes on enemies have grown to about 6,000 tokens, read again on every whole-report wake.

So news kinds were tested one by one: how often does this line lead to anything, and what do we lose if it waits?

### Enemy in view: the biggest source of wakes

This one kind makes up 1,190 of the 3,474 news lines sent to the two agents. Most of it is one thing: **873 of the 1,149 lone-lord lines are the Orcs seeing Rohan lords**, nearly all near Linhamm, Grafton or Horsham. 576 of the 1,149 are a lord already sighted in the past three days.

Each line was checked by id against every strike order in the run, in fast answers and councils alike:

| The sighting | Lines | Struck in that answer | Struck within 12 hours | Struck within 24 hours |
|---|---|---|---|---|
| Weak lord (under 700) passing by | 907 | 2 | 3 | 6 |
| Weak lord on its way to raid | 89 | 2 | 4 | 8 |
| Weak lord chasing ours, or inside or besieging a holding | 62 | 0 | 0 | 0 |
| An army, or a lord of 700 or more | 132 | 0 | 0 | 2 |

Strikes do happen, but they come from elsewhere. The studied wakes gave 182 strike orders, 176 of them naming a target. 77 targets were in the wake's own news line, mostly raids and letters, 46 in its skipped news, and 53 nowhere in the news: the agent found them in the report (below).

Here's the agent's own reading of the Rohan lines, in its logged thinking:

- **#5796:** "The Rohan lords near Linhamm heading to Æcertun/Marton look like crowd bait, so no action needed there."
- **#5853:** "This poke about Urundulf heading to Marton is just bait, no orders needed."
- **#5648:** "Rohan's lords near Linhamm are heading to Merham, so no orders needed there."

"Bait", "crowd" or "let it burn" appear in the logged thinking of 54% of wakes with a weak enemy. **Strong enemies are different:** those wakes rarely strike either, but 26 of 57 gave orders, mostly moving lords behind walls or changing how they behave. They stay.

### Order reports: an order saying it's working

These lines report an order doing what it was told, and it has already acted. The table counts wakes where this was the only kind of line in the wake's own news.

| The line | Real example | Wakes | No action | Orders | Cost |
|---|---|---|---|---|---|
| Lord sheltering | Grishûl is sheltering in Erebor from Imák... | 52 | 33 | 16 | $17.74 |
| Watch report | Dûrnia, shadowing Alenon...: besieging Gwígar | 40 | 25 | 9 | $13.05 |
| Lord falling back | Throgma is falling back, away from Gundabad Orc Raiders... | 27 | 19 | 8 | $10.29 |
| Refilling | Skûzla took the volunteers at Ashúrz... refilling at Pushlug, then back | 20 | 17 | 3 | $6.02 |
| Relief ride starts | Gûthrak rides with all haste to relieve Orbug... | 17 | 11 | 6 | $6.34 |
| Cashed in | Lorgath cashed in: handed over 153 prisoners... | 16 | 10 | 6 | $4.98 |
| Few recruits | Zulgora found few recruits at Liutburg... | 9 | 6 | 3 | $2.62 |

No action plus orders can fall short of the wakes, because a few wrote only a letter.

The older logs show the same order, though those agents gave orders more readily: no action on 59% of sheltering wakes, 69% of falling back, 62% of cashed in and 63% of few recruits.

### Why it gives orders on these wakes anyway

Reading the wakes by hand, the pattern is plain. The agent dismisses the news in one sentence, then looks around for something to do:

Wake #5602, Misty Orcs ($0.35, 1 model step):

**The news:** Enemy lord in view: Thomund (Rohan), 171 men, strength 391, 27.6 from Horsham; going to Merham...

**Its thinking, as logged:** "I'm noting Thomund's move toward Merham as a possible bait near Rohan's forces, but setting that aside for now. *More pressing is Naghza*, sitting at 19% ranks with 125 men and heading to raid Oreham under AI control..."

**What it did:** Naghza garrisons Starkmoore. "out of gold, drifting toward Rohan's crowd"

This housekeeping has real value. Here's what it does:

| 905 orders from fast answers | Orders | Next council, median wait | Fought | Won | Lords taken |
|---|---|---|---|---|---|
| Names something in the wake's news | 386 | 13 hours | 57 | 36 | 6 |
| Names something in the skipped news | 209 | 19 hours | 34 | 28 | 5 |
| **Names nothing in the news** | **310** | 13 hours | 36 | 28 | 7 |

Three things stand out.

- **Lords with no order are few, and they get a lot of the attention.** 360 of the 905 orders went to a lord with no order in force, yet a typical wake has only about 4 such lords out of 48. One line listing them costs almost nothing and hands the agent what it goes looking for.
- **Most of it could wait for the council.** Among the 310, only 40 are strikes. They came on 17 wakes: 27 fought, 21 won, 6 lords taken, and 10 of those fights were over before the next council sat. Those are worth protecting, but they're the exception.
- **Moving weak lords to safety shows no clear benefit.** Of 136 safety and money orders, 13 lords (9.6%) were still taken within a day, against 5.5% of weak lords left with no order. Lords ordered to safety were probably in more danger to begin with, so this doesn't prove the orders are useless. It only means the data can't show they help.

**Strikes on enemies the news never named are the real risk.** There were 53, on 23 wakes, with 8 lords taken. Today's news report still shows 40 of those targets near the news, 9 more only in its summary of the rest of the war, and 4 not at all. So the design adds a short list of chances to every report: lone enemy lords under 700 who are raiding or fighting, with 1.5 times their strength of ours within reach. That list holds 65 of the 176 strikes that could be checked. It's empty at about half of all wakes and averages 1.3 lines.

### Letters: useful, but they feed each other

The two agents exchanged 298 letters that reached a wake. A letter wakes its reader, the reader often replies, and that reply wakes the sender. Of the 94 wakes with only a letter as news, 63 did nothing but write back. 18 gave orders.

- **Replies often just acknowledge.** 65 of 138 replies open with thanks or agreed. At #2330 the thinking says "a brief acknowledgment should suffice".
- **Chains run long.** There were 46 runs of letter wakes each within 8 hours of the last, the longest 15 wakes. Past the second wake of a run there are 93 wakes costing $36.47, and 50 of them only wrote another letter.
- **But a third can't wait.** Of the 228 letters naming a future time, 75 name one within 6 hours, such as a joint strike. On 20 letter wakes the orders included a strike.

So a fixed delay on all letters is wrong. The writer knows which letter is urgent, so the writer should mark it: wake them now, or let it wait for their next look.

### A bug that wakes the agent

An order can name what comes next: "Thorzag supply Dannenglor, then defend Bûrzkala". For supply and recruit orders, **that next order never started on its own.**

| Orders with a follow-up that ended | Started on its own | Only by a later answer | Nothing for 48 hours |
|---|---|---|---|
| Supply | 0 | 65 | 5 |
| Recruit | 0 | 22 | 0 |

The code explains it. These two ended by way of the older order path (`Orders.End`), which only freed the lord. The follow-up was started only when a tool finished the normal way (`Runner.Finish`), and engage and sell orders did go that way: they show 58 and 59 follow-ups started.

The agent has noticed. At #2339: "his follow-up defend order for Dannenglor doesn't seem to have kicked in yet since he's not showing up in orders in force. To be safe, I'll just issue the defend Dannenglor order explicitly." In 36 of the 70 supply cases, the same answer gave that lord a new order at the place he had just supplied.

So 92 order-ended lines woke the agent over a lord who should already have moved on. It probably also left lords under the game's own AI for hours: after a supply order, the median wait for the next order was about 9 hours. [The fix](#the-follow-up-fix) is in 0.14.2.

### The wake rule: three lanes

The game sorts every line of news before any model call. A wake happens if any line in the "wake now" lane has arrived, or if something in the "wait" lane has waited 6 in-game hours. Everything else rides along with the next call, and the council reads it all anyway.

**Wake now:**

- A village of ours raided, or a fight over a raid
- A battle with our lord in it; any lord taken; a party destroyed
- A siege laid or lifted; a relief coming; a holding changed hands; a hunger warning
- An enemy army, or an enemy lord of 700 or more
- Any enemy lord chasing ours, or raiding, besieging or inside a holding
- A weak raider on its way, when we have 1.5 times its strength within 40 map units
- A lord stuck; an army broken up; votes, peace, war
- A letter marked to wake its reader
- The first look after a load

**Wait up to 6 hours:**

- A letter not marked urgent
- An order that ended: supply done, recruiting stopped, lord taken
- A chase given up, or a target gone
- A weak raider on its way with none of our strength near

**Digest, never wakes:**

- Lord sheltering; lord falling back
- Relief ride starting
- Routine watch reports
- Refilling; few recruits; cashed in; rested; "clear again, going back"
- An army forming
- A weak enemy lord (under 700) passing, patrolling or on the move
- Village looted, proposals, alliances, votes settled

The two numbers, 700 and 6 hours, are first settings that come from this run. The agent can edit both once rule editing is on.

### Testing the rule on the whole run

First, the 744 real wakes, sorted by the most urgent line each one carried, skipped news included:

| Lane | Wakes | Cost | No action | Letter only | Orders | Strike orders | Fights won | Lords taken |
|---|---|---|---|---|---|---|---|---|
| Digest only | 264 | $89.66 | 198 | 15 | 51 | 9 | 4 | 1 |
| Can wait | 213 | $80.86 | 42 | 80 | 91 | 51 | 14 | 6 |
| Wakes now | 267 | $112.97 | 99 | 34 | 134 | 139 | 55 | 10 |

The rule does what it should: the lane that wakes now holds most of the strikes. It isn't perfect, since 99 wake-now wakes still did nothing. Some wakes are only known to be empty after the fact.

**Then the run's real news was walked hour by hour**, one call at a time, each holding the war room for 3.3 hours of game time, with councils as they were. Letters used a stand-in for the writer's mark: a time within 6 hours, or words like "at once". That marks 114 of the 298.

| Rule | Calls | Fewer than today |
|---|---|---|
| Today, as the walk models it (768 in reality) | 816 |  |
| Digest lane only; letters and ended orders wake now | 586 | 28% |
| Wait lane at 3 hours, urgent letters wake now | 511 | 37% |
| **Wait lane at 6 hours, urgent letters wake now** | **456** | **44%** |
| Wait lane at 12 hours, urgent letters wake now | 425 | 48% |

The design takes 6 hours. Going to 12 saves only 31 more calls, and one stopped recruit order in ten would then sit for over 13 hours.

**How long news waits for an answer**, in game hours, median then one in ten, answer time included:

| News | Today | With the rule |
|---|---|---|
| Village raided | 5.0 / 6.0 | **4.1 / 5.9** |
| Battle | 4.8 / 5.9 | **3.9 / 5.6** |
| Holding besieged | 4.8 / 6.2 | **3.3 / 5.6** |
| Enemy army in view | 4.6 / 5.9 | **3.6 / 5.6** |
| Letter | 4.9 / 6.2 | 4.6 / 9.3 |
| Supply order ended | 4.8 / 5.9 | 5.3 / 9.3 |
| Recruiting stopped | 4.9 / 6.2 | 5.3 / 9.3 |
| Enemy lord in view | 4.9 / 6.2 | 5.0 / 10.0 |
| Lord sheltering | 4.9 / 6.1 | 5.2 / 10.3 |

So the urgent news gains about an hour, because the war room is free more often. The rest typically loses very little, though the slow cases get about four hours slower. These figures run until the answer arrives. Orders take effect later still: the news report's own measure is about 7 hours after the report.

**What the digest lane would have delayed.** The 51 digest wakes that gave orders hold 135 orders: 85 naming nothing in the news, 27 about the news, 23 about skipped news. In the walk, they would wait a median 5 hours, and one in ten over 10 hours. They split into cashing in loot (25), refilling (21), safety (18), joint plans (10), strikes (9) and others, by rough word matching. These five wakes held the strikes:

| Wake | The order | Wait under the rule | What came of it |
|---|---|---|---|
| #1965 Dol Guldur | Narsha's army storms weakly held Imnagath | 8 hours | No fight under that order |
| #2231 Dol Guldur | Zulgora strikes while Aragorn and Imák are pinned at Felegoth | 2 hours | Fought and won |
| #2517 Dol Guldur | Shelgra and Dramthak on Nimlothiel, slowed by a fight | 3 hours | Both fought and won, 1 lord taken |
| #5963 Misty Orcs | Three lords answer Dísa's raid on Gor-mîr | 1 hour | No fight under those orders |
| #5988 Misty Orcs | Muzgash and Grizznak on Bit Róri, a lone weak lord | 3 hours | One fought and won |

That's the price of the digest lane: 1 to 8 hours' delay on 9 strike orders out of 199, against $89.66 and 264 wakes saved. It's worth paying.

## 2. What each kind needs

### What it quoted, section by section

Share of wakes whose logged thinking states a fact from that section of the whole report: a name and its number, on the same line. A second hand check found 85 of 90 of these marks correct. These are floors, because only about 40% of the thinking is logged.

| Section | Tokens | Raid | Fight | Strong enemy | Siege | Free lord | Letter | Order report | Weak enemy |
|---|---|---|---|---|---|---|---|---|---|
| READY-MADE FACTS | 520 | 31 | 17 | 44 | 43 | 12 | 6 | 12 | 17 |
| YOUR LORDS AND ARMIES | 3,652 | 20 | 26 | 39 | 39 | 27 | 21 | 22 | 14 |
| YOUR HOLDINGS | 4,608 | 16 | 17 | 19 | 22 | 27 | 10 | 12 | 8 |
| YOUR NOTES | 5,202 | 11 | 10 | 12 | 30 | 14 | 22 | 14 | 9 |
| RECENT HISTORY | 2,642 | 4 | 5 | 21 | 30 | 18 | 19 | 14 | 4 |
| YOUR PURSE | 836 | 0 | 17 | 2 | 0 | 13 | 5 | 3 | 1 |
| IN VIEW | 3,746 | 7 | 10 | 5 | 4 | 5 | 5 | 4 | 9 |
| ALLIES AND JOINT PLANS | 1,234 | 4 | 5 | 2 | 4 | 6 | 13 | 4 | 2 |
| THE STORY SO FAR | 1,437 | 2 | 5 | 5 | 4 | 8 | 7 | 6 | 3 |
| YOUR ORDERS IN FORCE | 2,469 | 0 | 0 | 2 | 0 | 4 | 1 | 2 | 0 |
| SIEGES AND BATTLES | 1,366 | 0 | 0 | 0 | 0 | 1 | 1 | 0 | 1 |
| YOUR CAPTURED LORDS | 82 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| SEEN EARLIER | 1,846 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| ENEMY ARMIES' TRAILS | 367 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| ENEMY HOLDINGS | 222 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| DIPLOMACY | 2,106 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| ENEMY REALMS | 172 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| YOUR REALM IN SUM | 142 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

"Free lord" means an order ended, a lord got stuck or an army broke up. "Strong enemy" means an army or a lord of 700 or more. Wakes counted: raid 45, fight 42, strong enemy 57, siege 23, free lord 85, letter 174, order report 332, weak enemy 230. A wake with several kinds counts under each.

**Never quoted, in any kind:** DIPLOMACY (about 2,100 tokens), SEEN EARLIER (1,850), ENEMY ARMIES' TRAILS, ENEMY HOLDINGS, ENEMY REALMS and YOUR REALM IN SUM. The hand check did find two exceptions: the influence figure in DIPLOMACY mattered at #5752 and #2587, and SEEN EARLIER mattered on 5 of the 42 wakes it checked. Those stay one question away.

### What it needs beyond the news

Name-and-number matching can't see everything. On 42 wakes read by hand, the answer leaned on these sections in ways the first script missed. The counts are minimums.

| Section | Of 42 wakes | What it leaned on | Real example |
|---|---|---|---|
| YOUR NOTES | 17 | Lessons and plans, which have no number to match | #2212: selling a batch with a lord in it pays nothing, so it sends Dramthak to an allied dungeon |
| SIEGES AND BATTLES | 15 | Clocks: when a storm ends, when a place falls | #2176: Caras Laerolin's fall time decides what the stuck lords should do |
| YOUR ORDERS IN FORCE | 13 | Settings like caution and how long to wait for others | #2396: chooses no new order because the join time is 61 hours |
| YOUR LORDS AND ARMIES | 11 | Strengths and what each lord is doing | #6257: adds up the relief heading to Nanduhirion |
| IN VIEW | 10 | Enemies' strength and arrival time | #5595: when Aragorn reaches Torech Emel |
| YOUR HOLDINGS | 7 | What a place can hold against | #2373: Westoft holds up to 7,719, so Zulgora falls back there |

This shaped the cards. Every report by kind keeps the lessons and plan, the orders nearby with their settings, the clock, the lords and enemies in reach, and the holdings nearby. Sections go only when both the numbers and this hand reading say the kind can do without them.

## The cards

One per kind we'd still wake for. "Report" is built on today's news report, because the whole report's sections are already sorted by what bears on the news. Every report keeps the news and its clock, the skipped news, the orders nearby with their settings, the agent's lessons and plan, and its notes on enemies in the news. Each card lists what's added to that and what's left to ask for. Reach is the share of names in orders within 45 map units of the news, about 15 hours for a lone lord. Replay figures show the original's cost and seconds first, then the report by kind.

### Village raided (wake now)

- **How often:** 45 wakes, 44 of them the Orcs'.
- **What it did:** Orders on 21, no action on 20, letter only on 4. Of 71 orders, 56 named the news, and 20 fought, all 20 won, with 4 lords taken. This is the best return of any kind.
- **What it asked:** 19 of 45 wakes asked: when the fight would start (15), the way (10), a look-up page (8), odds (7), could it catch them (4).
- **What it quoted:** READY-MADE FACTS 31%, lords 20%, holdings 16%, notes 11%.
- **Reach:** 85%.
- **Report gives:** lords and others in reach, holdings here, captured lords here. **Left to ask for:** allies, purse, history, the rest of the war. About 9,800 tokens, against 12,200 for the news report and about 28,000 for the whole report.
- **Replay:** 6 wakes. $2.70 to $1.28; 132 to 94 seconds; 12 questions against 9. Acted alike on 4. At #5900 it missed a strike on Izdenka, a raider still on the way: the whole report found it ("crush Izdenka raiding Hrak-dum") and the small one didn't. So chances to strike should also list raiders on their way.
- **Confidence:** Fair on what it needs. Low on whether it still finds strikes off the news.

### Fight or capture (wake now)

- **How often:** 42 wakes, 28 of them Dol Guldur's.
- **What it did:** Orders on 23, no action on 14, letter only on 5. Of 73 orders, 32 cash in loot and prisoners.
- **What it asked:** 22 of 42 wakes asked: the way (19), a look-up page (16), could it catch them (7), when the fight would start (7), odds (6).
- **What it quoted:** Lords 26%, READY-MADE FACTS, purse and holdings 17% each, notes 10%.
- **Reach:** 84%.
- **Report gives:** lords and others in reach, holdings here, captured lords, purse line, the rest of the war. **Left to ask for:** allies, history. About 8,100 tokens.
- **Replay:** 5 wakes. $2.17 to $1.59; 172 to 234 seconds; 32 questions against 10, mostly whole-war lists. Acted alike on 4. Keeping the rest of the war barely helped (24 questions against 27 on 4 wakes, no faster). A big fight changes the whole map, so it looks at the whole map.
- **Confidence:** Fair. This one saves money but costs time.

### Strong enemy: an army, or a lord of 700 or more (wake now)

- **How often:** 57 wakes, across both realms.
- **What it did:** Orders on 26, no action on 23, letter only on 8. Of 63 orders, about half move lords behind walls or change their settings: rest 15, garrison 10, adjust 8. Sightings like these rarely lead to a strike.
- **What it asked:** 29 of 57 wakes asked: the way (27), odds (14), a look-up page (14), when the fight would start (9).
- **What it quoted:** READY-MADE FACTS 44%, lords 39%, holdings 19%, notes 12%.
- **Reach:** 79%. A big army can matter from far off.
- **Report gives:** lords and others in reach, holdings here, captured lords here. **Left to ask for:** allies, purse, history, the rest of the war. About 9,000 tokens.
- **Replay:** 6 wakes. $2.78 to $1.71; 214 to 193 seconds; 32 questions against 15. Acted alike on 5. At #2041 the whole report changed three lords' settings, as the original did. The small one asked 11 questions and changed nothing.
- **Confidence:** Fair.

### Siege, relief, holding changed, hunger (wake now)

- **How often:** 23 wakes, usually with other news. Sieges and lost holdings also call a council already.
- **What it did:** Orders on 16, no action on 4, letter only on 3. Of 50 orders, 21 cash in.
- **What it asked:** 15 of 23 wakes asked: the way (13), odds (8), a look-up page (8), when the fight would start (7).
- **What it quoted:** READY-MADE FACTS 43%, lords 39%, notes 30%, history 30%, holdings 22%.
- **Reach:** 81%.
- **Report gives:** lords and others in reach, holdings here, captured lords, recent history here. **Left to ask for:** allies, purse, the rest of the war. About 11,500 tokens.
- **Replay:** 4 wakes, on a report without recent history. $1.65 to $0.66; 93 to 60 seconds; 4 questions against 6. Acted alike on 3. At #6125, a hunger warning, the whole report sent supply and the small one sent nothing.
- **Confidence:** Low. It's a rare kind and only 4 replays.

### Free lord: an order ended, a lord stuck, an army broke up (ended orders wait; stuck or broken wakes now)

- **How often:** 85 wakes. Fixing the follow-up bug removes some.
- **What it did:** Orders on 75. This is the kind that really needs an answer. 261 orders: defend 70, recruit at home 55, garrison 49, go to 21.
- **What it asked:** 46 of 85 wakes asked: the way (39), a look-up page (30), odds (14), when the fight would start (10).
- **What it quoted:** Lords and holdings 27% each, history 18%, notes 14%, purse 13%.
- **Reach:** 73%, and 87% within twice that. It places lords wherever they're safe, not only nearby.
- **Report gives:** lords in reach, holdings here, purse line, lords with no order, lords too poor to recruit. **Left to ask for:** others in reach, allies, history, the rest of the war. About 7,700 tokens.
- **Replay:** 7 wakes. $3.63 to about $1.56; 366 to 379 seconds. Acted alike on 4 of the 5 that answered. **2 hit the 120-second limit** (#6033 and #6331), both lords stuck at the Nanduhirion siege, asking 16 and 11 questions. Those wakes are about the whole northern war and belong to a council.
- **Confidence:** Fair for one lord. Poor when many lords are caught up in one big event.

### Letter from an ally (waits, unless marked)

- **How often:** 174 wakes, the biggest kind we'd keep.
- **What it did:** Letter only on 92, orders on 54 (220 orders), no action on 28.
- **What it asked:** 72 of 174 wakes asked: the way (70), a look-up page (39), when the fight would start (38), odds (18), could it catch them (13).
- **What it quoted:** Notes 22%, lords 21%, history 19%, allies 13%.
- **Reach:** 76%.
- **Report:** the whole report, as today. A letter is about the whole war, so there's little to trim. Fewer letter wakes is where the saving lies.
- **Replay:** 8 wakes. $3.32 to $2.88, only 13% less; 232 to 482 seconds, twice as slow; 44 questions against 12. Acted alike on 5. Keeping the rest of the war barely helped (22 questions against 25 on 4 wakes).
- **Confidence:** Fair. The time cost is large enough to show even in 8 wakes.

**Order reports and weak enemies get no card,** because they no longer wake the agent. They arrive as lines in WHAT CHANGED on the next call. A vote, a peace or a war keeps the realm report it has today.

## The replays

There were 73 replays, for about $24: 36 on a report by kind, 8 on a wider version for fights and letters, 20 on the whole report (10 wakes, twice each), and 9 more on the whole report as a control. Way-finding answered from each wake's recorded world, with no code changes.

**The first result is about the agent, not the report: it varies a lot by itself.**

| Compared | Pairs | Both acted, or both didn't | Same lords (overlap, 0 to 1) |
|---|---|---|---|
| The same whole report, run twice | 10 | 7 of 10 | 0.49 |
| Whole report today, against the original answer | 20 | 17 of 20 | 0.62 |
| Report by kind, against the original answer | 34 | 25 of 34 | 0.55 |

At #5838 one run gave 1 order and the other gave 14, from the identical report. So the report by kind differs from the original about as much as the agent differs from itself. That's reassuring, though these numbers can't rule out a small harm.

**Then the nine wakes where the report by kind changed whether it acted.** Each was run on the whole report under today's instructions. On 5 the whole report sided with the report by kind, so the difference wasn't the report. On 4 it sided with the original: #5900, #2041, #6125 and #2200. In all 4, the whole report acted and the report by kind did nothing. That's a pattern to watch: when a small report changes the decision, it tends to make the agent do less.

**Cost and time, by kind:**

| Kind | Wakes | Cost, original to kind | Less | Seconds, original to kind | Questions, original to kind | Acted alike |
|---|---|---|---|---|---|---|
| Raid | 6 | $2.70 to $1.28 | 53% | 132 to 94 | 9 to 12 | 4 |
| Fight | 5 | $2.17 to $1.59 | 27% | 172 to 234 | 10 to 32 | 4 |
| Strong enemy | 6 | $2.78 to $1.71 | 38% | 214 to 193 | 15 to 32 | 5 |
| Siege | 4 | $1.65 to $0.66 | 60% | 93 to 60 | 6 to 4 | 3 |
| Free lord | 7 | $3.63 to about $1.56 | 57% | 366 to 379 | 9 to 15 (5 that answered) | 4 of 5 |
| Letter | 8 | $3.32 to $2.88 | 13% | 232 to 482 | 12 to 44 | 5 |
| **All** | **36** | **$16.25 to about $9.68** | **40%** |  |  |  |

Free lord's cost includes an estimate for the two calls stopped at the limit, whose bills weren't logged. Their logs show about $0.64 together.

The upshot: raids, sieges and strong enemies do well on a small report. Fights and letters ask for what was left out, which costs time and saves little, and letters should keep the whole report.

The agent does ask for what it lacks, which supports the design. But asking costs a model step, about 9 seconds, and that's more than an hour of game time. Leaving letters out, reports by kind saved 47% on these wakes, against 26 to 35% for the news report in the twelve Nov 3 replays. On a whole run that's about $20 more, with the risk above of doing less. So the wake rule comes first, and reports by kind can wait. They matter more for the local model, where every token takes up graphics memory.

## 3. The new design

### How it fits together

In run A every line of news woke the agent, and every wake got the same kind of report. In the new design the game sorts each line first, with no model call, into one of three lanes:

- **Wake now:** raids, fights, sieges, strong enemies, stuck lords, marked letters.
- **Wait up to 6 hours:** unmarked letters and ended orders. They ride any earlier call.
- **Digest:** order reports and weak enemies passing. These never wake the agent and ride the next call.

The call then reads the news report, or the whole report when a letter is in it. The report holds the news and its clock, the skipped news in one digest, the lessons and plan, the orders in force nearby with their settings, the lords and enemies in reach, the holdings nearby, the lords with no order, the chances to strike, and what the agent can ask. Orders, letters and notes come back as before.

The council keeps the whole picture, with no gate. It picks up errands and everything in the digest.

**The council can't stay exactly as it is.** The study's first draft said it could, and that was wrong. The war room checks whether a council is due only when a wake arrives, by that wake's in-game hour (`Room.CouncilDue`), and it reads the big-event words only after a fast answer (`Room.AfterAnswer`). With fewer wakes, councils would slip, and a digest line such as "army has formed" wouldn't call one. So the game must wake the agent when a council is due, and digest lines must be checked for those words.

### What changes

| Change | What it does | Why | Status |
|---|---|---|---|
| **1. Fix the follow-up** | A supply or recruit order that ends starts its next order, as other orders do. | 0 of 92 follow-ups started. Removes some wakes and stops lords drifting. | **Done in 0.14.2** |
| **2. The wake rule** | The game sorts each line into three lanes. It is on by default, with a setting to turn it off. Every line logs its lane and the rule that sorted it. | About 44% fewer calls, and important news answered sooner. | To build |
| **3. Letter marks** | A letter says whether it must wake its reader. The instructions say to mark it only if the ally must change an order within about 6 hours, and not to reply just to acknowledge. | A third of timed letters are urgent, and half of replies open with thanks. | To build |
| **4. News report on, except letters** | Switch on the news report built on Nov 3 for every kind we wake for. Wakes with a letter keep the whole report. | Letters saved only 13% on a small report and took twice as long. The rest saved 27 to 60%. | To build |
| **5. Lords with no order** | One line in every report: name, strength, where, gold short or not. | 360 of 905 orders went to these lords, and there are only about 4 at a time. | To build |
| **6. Chances to strike** | A short list in every report: lone enemy lords under 700, raiding, fighting or on their way to raid, with 1.5 times their strength of ours within reach. | Keeps the strikes the agent finds by looking around. About 1.3 lines on average, before counting raiders on their way. | To build |
| **7. Too poor to recruit** | Show a lord's gold beside him when it's near the floor, or refuse the recruit order up front with the reason. | 69 recruit orders ended early for money. A lord's gold wasn't in the report for 59 of the 67 that could be checked. | To build |
| **8. Big events go to the council** | When many lords are caught up in one siege or battle, the wake calls a council. The war room can already do this on set words. | Both replays that hit the 120-second limit were this. The original #6033 took 115 seconds and cost $1.00. | To build |
| **9. Council clock** | The game wakes the agent when a council is due, and digest lines are checked for the council's words. | See above. | To build |
| **10. The agent's own wake rules** | The agent can edit the vanilla rules and write its own, in any answer. | See below. | To build, for the second test run |
| **11. Reports by kind, later** | A small table of sections by kind on top of the news report (the cards). | Up to 33% smaller than the news report, for about $20 more saved per run on Claude. Mostly useful for the local model. | Later |

**Left alone:** first looks, the question book, and one shared copy of the instructions on Claude. By-kind instructions only matter for the local model, where 25,000 tokens cost graphics memory on every wake (step 9 in the back-up).

### The follow-up fix

Version 0.14.2 (commit 3f9c8eb). All 92 follow-ups in run A reached a normal end, so all 92 would now start: 70 supply orders handed over their food, 18 recruit orders ran out of gold and 4 hit their wage cap, at between 14% and 82% of their ranks.

- A supply order goes on to its next order when the food is in or the place needs none. It doesn't when the place was besieged or lost first, or the lord had nothing to bring.
- A recruit order goes on at every end, including when the lord can't pay for more. Those lords now go to their post under strength instead of drifting, and the agent can still replace the order.

The smoke test covers the part that decides what starts and when. The part inside the game hasn't run in play yet, so the next test run is its first real trial.

### The two lists, on a real wake

Misty Orcs, Spring 10, 1088, wake #6301. The wake's news was a weak Rohan lord passing (Unthery, 163 men, strength 240, 27.6 from Linhamm), plus 16 backlog lines, none about Celeborn. The agent found him by reading the whole report and sent Morznak, who took him prisoner and lost 13 men.

From that wake's recorded data, the two sections would read like this. The wording is a sketch:

```
LORDS WITH NO ORDER (1 of 48)
- Morznak: army of 664 men in 4 parties, strength 1,177; 3 from Mazûg-salth,
  heading to defend Nanduhirion.

CHANCES TO STRIKE
- Celeborn (Arnor and Gondor): 131 men, strength 333, on the way to raid
  Mazûg-salth, there in about an hour. Yours near: Morznak 4 away (no order);
  Bargha 12 and Shagash 12 (going to garrison Nanduhirion).
- Haleth Halethring (Rohan): 159 men, strength 392, on the way to raid Grafton.
  Yours near: Tûba and Maukhash, 31 away (garrisoning Starkmoore).
- Cadda Tordaging (Rohan): 63 men, strength 129, on the way to raid Feldham.
  Yours near: the same two, 31 away.
- Fighting (Rohan): Hilda Bonecrusher 121, Waldæg Bregdaning 70, Leofwine 26.
  Yours near: the same two, about 22 away.
```

The list leaves out any enemy over 700 strength or in an army, and any enemy without 1.5 times its strength of ours within 40 map units. The wake rule wouldn't have dropped this wake, since its backlog held two battles and two lords taken.

**What the list needs before it's built.** At #6248 the rule above gives 16 lines, 15 of them Rohan lords fighting about the same distance from the same two lords. So those get grouped into one line, the list is capped at 5, and reach is measured in hours of march, not straight-line distance.

### Letter marks, on real letters

A mark is a yes or no the writer puts on its letter: wake my ally now. A marked letter wakes its reader at once. An unmarked one waits up to 6 in-game hours, or goes with any earlier call.

- **Should be marked (l103):** "A warning for Dramthak's road. The Baracharn host has come out... about 8,500 strength." The reader's lord may be marching into them.
- **Can wait (l95):** "Thank you for l94. Our lords are marching to Sirannon one by one..." Dol Guldur gave no orders on that wake and just wrote back.

### The agent's own wake rules

The study's first draft held this back. I decided otherwise, and want it tested: the agent can edit the rules and write new ones, as it already does for tools. The plan:

1. **The vanilla rule is an editable list from the start.** Each rule has a name, the news it matches, and a lane. Both test runs use the same machinery, with one setting that allows edits.
2. **Every news line carries its facts:** the enemy's realm and strength, what it's doing, the nearest holding, our strength nearby, the letter mark. Today a line is mostly text, so this is the biggest piece of new work.
3. **Deciding a lane.** The agent's rules are checked first, then the vanilla ones, and the first match decides. A line that matches nothing wakes the agent. If anything in the backlog is "wake now", the whole backlog goes with it.
4. **Writing rules.** The agent uses the language it already uses for its own tools, with the same checker, and a bad rule is refused with the reason. Any answer can change rules, fast or council. An answer could carry something like this (one of each kind; the third would remove the first):

```
"wake_rules": [
  {"name": "rohan_passing_linhamm", "lane": "digest", "days": 5,
   "if": "realm == 'rohan' and strength < 500 and near == 'Linhamm' and not raiding"},
  {"name": "weak_lord_passing", "set": {"strength": 400}},
  {"name": "rohan_passing_linhamm", "retire": true}
]
```

5. **Limits.**
   - **Starting settings:** 700 strength and the 6-hour wait, and the agent can edit both.
   - **Own rules:** at most 20, each expiring within 10 in-game days unless renewed, because a stale quiet rule is the main danger.
   - **Locked:** first look, the council clock, marked letters, a lord taken, one of its own holdings besieged or lost, and votes about to close.
   - **Everything else:** the agent can change any number or lane. The worst a bad rule can do is delay news until the next call or council, since digest news is never lost.
6. **Feedback at councils.** A short section lists each rule, the lines it held, and any held line whose same enemy then raided, besieged or took something within 12 hours. It goes in the report and not the instructions, so a rule change doesn't throw away the cached prompt. The instructions explain how rules work and give no advice on what to silence.
7. **Records.** Every rule change logs the answer it came from. Rules start empty in each test run, unlike tools, so the runs stay comparable.

### Order of work and testing

1. Fix the follow-up bug. **Done.**
2. Build the finished version, with the wake rule on by default and rule editing off. There is no log-only step first.
3. **Test run 1:** the vanilla rules, both sides live.
4. Add rule editing.
5. Replay about 10 recorded councils (about $6) to see what rules the agent writes, then run those rules over run A's news.
6. **Test run 2:** from the same save and for the same length, with the agent able to edit and write rules.

Reports by kind come after that, when the local model needs them. They can build on the same lanes and cards.

**What the runs compare:** calls, cost, how fast raids, battles and sieges get answered, lords taken and lost, holdings lost, money, held lines followed by harm, and how many rules get written and retired. These runs are the only test of whether the agent plays as well.

**Their limits:**
- Run 1 changes several things at once against run A. If play gets better or worse, the logs can split out the cost, but not which change caused it.
- With one run per setup, only big differences in play will show, because the agent acted alike on the same report only 7 times in 10. Calls and cost will be clear.

### What it should save

| Whole 60-day run, councils included | Fast answers | Councils | Total | Less |
|---|---|---|---|---|
| Today: the whole report | $295.59 | $87.74 | $383 |  |
| News report alone, no wake rule | about $210 | $87.74 | about $298 | 22% |
| Wake rule alone, whole report | about $175 | $87.74 | about $263 | 31% |
| **Wake rule and the news report, letters keep the whole report** | **about $145** | $87.74 | **about $233** | **39%** |
| Wake rule and reports by kind, letters keep the whole report | about $125 | $87.74 | about $213 | 44% |

These are estimates. Calls come from the walk through the run, scaled to the real 768. Cost per call starts from run A's kept wakes ($0.40 on average), and the smaller reports are then priced from replays, with the extra questions counted. Wakes with a letter keep the whole report in the two lower rows, which is why those rows save less than they might seem to. The $172.94 reckoned for the news report on Nov 3 came from sizes alone. The twelve replays measured less, so without the wake rule about $210 is the better figure.

**Time matters as much as money.** Each call holds the war room for about 3.3 hours of game time, and the agent can't take new news during it. Today 836 of 1,605 pokes never got a call of their own. For the local model, fewer calls is worth even more: it's far slower than Claude, so a call every three game hours may not keep up.

## 4. Back-up

### Results by step

| Step | Result |
|---|---|
| 1. One row per wake | 769 answers, 744 studied. 836 of 1,605 pokes had no call of their own. 114 throwaway tool checks, 106 of them the last call before answering; at #2051 the thinking ends "That extra step wasn't needed". Each cost a model step. News mode already removes that tool. |
| 2. Sort the news | 3,474 lines. Enemy in view 1,190, order reports 1,281 by label, letters 298, order ended 209. The two realms differ: nearly all raid wakes are the Orcs' (44 of 45) and so are weak-enemy wakes (212 of 230), while Dol Guldur has more order-report wakes (218 of 332). |
| 3. Mark what was used | First version failed: 70% of marks right, verdict held in 12 of 42. Second version: 85 of 90 right on names and numbers, which passes. Word-based marks failed for two sections, so they are left out of the tables, with the hand-read counts beside them. |
| 4. Sections | See the table. The planned "smallest set" was dropped, because it picked sections by price and not by where a fact came from. |
| 5. Distance | Share of order names within 45 map units of the news (about 15 hours for a lone lord): raid 85%, fight 84%, strong enemy 79%, siege 81%, letter 76%, free lord 73%, order report 62%, weak enemy 41%. The kinds we keep stay near the news. |
| 6. About the news? | 386 orders name the news, 209 the skipped news, 310 neither (34%; the rough trial said 40%). See why it acts anyway. |
| 7. No action | 339 of 744. See the count. The planned check, "did harm follow within 24 hours?", turned out too noisy to use: harm linked by name followed 41% of wakes with orders and 29 to 51% of wakes without. It also matters less than expected, because dropping a wake where the agent did nothing changes nothing. The real risk is in wakes that gave orders, so those were measured instead. |
| 8. Used against needed | 69 recruit orders ended for money (51 too little gold, 17 wage cap, 1 clan purse), all from fast answers, and the figure was mostly not in the report: a real gap. 30 orders refused: 13 told a lord to act on himself, 7 targeted a prisoner, 4 were already in an army, 4 named ground no party can stand on, 2 a holding that wasn't friendly. |
| 9. Instructions and memory | Order types used, of about 29: raid 7, fight 9, strong enemy 11, siege 12, free lord 19, letter 22. The news-mode instructions run about 19,000 tokens, and the list of orders takes about 7,600 of them. A by-kind list of orders could save several thousand tokens on a local model, and isn't worth it on Claude. Memory: "plan" appears in 52% of siege wakes and 37% of letter wakes, "lesson" in 22% of siege wakes, and the story almost never (0 to 2%). |
| 10. Older logs | 4,611 answers. Sightings: 68 of 4,059 struck, the same shape as run A. No use of diplomacy, enemy realms or the realm summary on a 900-wake sample, though those logs keep less of the thinking. **One difference:** older agents gave orders far more often (66% of lone-sighting wakes against 26%), under older rules. Letters don't appear there. The Kingdom's agent acted on 53% of its wakes, against 69 to 78% for the others. |
| 11. Replays | See the replays. |

### Ten wakes traced

Each shows the lane the rule would pick. Each reading comes straight from the logged thinking.

#### #6142 Misty Orcs: a raid, answered cleanly (Wake now; $0.45, 4 steps)

**News:** Grish-morn is being raided by Elendara (Kingdom of Arnor and Gondor).

**Thinking:** "Elendara is raiding Grish-morn alone with strength 281, holding Brughza captive — if Krithak, Hrakash, Ufza, and Shagash could reach her (they're 5-7 hours away at Nanduhirion) and defeat her, Brughza would be freed."

**Did:** Asked when the fight would start and whether they could catch her. Four lords engage her.

**Reading:** this is what a wake is for. The report by kind holds all four lords and Elendara. It lacks Brughza as a prisoner, because the first card for raids dropped YOUR CAPTURED LORDS. That section is about 80 tokens, so it should stay in every kind.

#### #2286 Dol Guldur: two order reports, nothing to do (Digest; $0.28, 1 step)

**News:** Zorgrul found few recruits at Dol Guldur... Dûrnia is sheltering in Dale from Girda and 11 more with it...

**Thinking:** "This poke covers Zorgrul's recruiting and Dûrnia sheltering in Dale, neither of which needs action... No new orders needed."

**Did:** Rewrote its plan. No orders.

**Reading:** a needless wake. The orders had already acted on their own.

#### #5595 Misty Orcs: a huge army seen far off (Wake now; $0.71, 2 steps)

**News:** Enemy army in view: Aragorn, 8,999 men, strength 22,895, 75.4 from Dush-salth; going to Torech Emel...

**Thinking:** "Aragorn's force has swelled to 8,999 men with 22,895 strength, far exceeding what Dol Guldur estimated at 10,800... so I should warn Dol Guldur of this miscalculation."

**Did:** Wrote to Dol Guldur. Nabil shadows Aragorn.

**Reading:** the sighting line alone justified this wake, and 75 map units away is no reason to ignore an army. The report by kind holds Aragorn and Nabil. Its letter to the ally shows why this line wakes the agent however far off it is.

#### #2373 Dol Guldur: an army near a starving castle (Wake now; $0.45, 4 steps)

**News:** Enemy army in view: Aragorn, 2,545 men, strength 7,947... Hunger at Gwígar: its food would last only 0 days of siege... plus a letter from the Orcs.

**Thinking:** "...Westoft... has strong lords inside (totaling roughly 2,000) and could hold up to 7,719, so adding Zulgora's 4,155 there would be defensible."

**Did:** Zulgora goes to Westoft, Dûrnia shadows Aragorn, and it answers the letter.

**Reading:** a mixed wake, and the "hold up to" figure drove the choice. The report by kind keeps it in HOLDINGS HERE. This is why that section stays for every kind we wake on.

#### #2212 Dol Guldur: lords captured in a great win (Wake now; $0.30, 1 step)

**News:** Malthorn... Asta... Durin (Kingdom of Arnor and Gondor) were taken prisoner...

**Thinking:** "Selling a batch with a lord in it nets zero at Caras Galadhon, so I need to think about the escape risk... Nanduhirion, a Misty Orcs town and ally, is only 8.5 by road versus the 4-day trek..."

**Did:** Dramthak takes Dáin to the allied dungeon at Nanduhirion.

**Reading:** the rule about selling comes from its lessons, with no number to match. So lessons stay in every report, even though the script rarely sees them used.

#### #5562 Misty Orcs: an army breaks up, 12 lords at once (Wake now; $0.50, 6 steps)

**News:** The army of Naglur broke up (CohesionDepleted).

**Thinking:** "Naglur's army scattered near Hollin Gate... Options are limited: re-gathering costs influence again and resets cohesion, while recruiting at Hollin Gate is uncertain given few volunteers and unknown gold."

**Did:** 12 orders: Naglur sells prisoners, the rest recruit at Hollin Gate.

**Reading:** a real need, and an urgent one, since 12 lords go free at once. Note "unknown gold": the agent itself says the report doesn't show it. That's the gap in change 7.

#### #2176 Dol Guldur: lords stuck outside a besieged town (Wake now; $0.48, 3 steps, 6 pokes skipped)

**News:** Mhargza has not moved for 7 hours while sheltering in Caras Laerolin, still 14.4 from Caras Laerolin... plus a letter from the Orcs.

**Thinking:** "Maybe this is a pathing issue tied to the besieged status of Caras Laerolin — lords might not be able to enter a besieged settlement normally."

**Did:** Six lords get a direct garrison order, plus a letter and a new lesson.

**Reading:** worth waking for. The report by kind holds all these lords. It lacks the fall time the agent also weighed, from SIEGES AND BATTLES. Today's news report leaves that out too, so the agent would have to ask.

#### #2200 Dol Guldur: a letter, and a watch report (Can wait; $0.43, 4 steps)

**News:** A letter from the Misty Orcs: "Your l77 reached us after the event. Our lords began reaching Dáin about 04:00..." Dûrnia, shadowing Alenon: marching to besiege Stranding...

**Thinking:** "I should set Dramthak's caution to 0 for the final stretch since the strength numbers (7176 vs Dáin's 5581 plus allies) favor him comfortably..."

**Did:** One change to Dramthak, two lords sent to Stranding, and a letter.

**Reading:** this one is a warning about the wait lane. The fight is already under way, so a 6-hour wait on this letter could matter. The writer's mark has to catch cases like it, and a stand-in like the one used here might not. The report by kind holds every figure the thinking used.

#### #1976 Dol Guldur: a relief is coming to a siege (Wake now; $0.33, 4 steps)

**News:** A relief is coming to Imnagath: 5 enemy lords and armies... strength 4,034 together, against your side's 3,527...

**Thinking:** "I'm weighing Narsha's siege force of 3,527 against relief arriving in waves at Imnagath — Imák's 1,085 reaching around 16 hours, Thalion's 929 near 18 hours..."

**Did:** Asked the odds. Changed Narsha's setting so he stays and beats the relief piecemeal.

**Reading:** a clean match between news and need. Every name and figure is in the report by kind, including the setting it changed.

#### #6033 Misty Orcs: stuck lords at the Nanduhirion siege (Wake now; $1.00, 5 steps, 115 seconds, 8 pokes skipped)

**News:** Izdenka on the way to raid Gor-mîr... Shelza has not moved for 6 hours... Boldog has not moved for 6 hours...

**Thinking:** "This is actually the opening Nár gives us: a storming force is locked in place and vulnerable, so I should coordinate a strike..."

**Did:** Wrote to the ally, changed its plan and wrote a lesson. Originally it asked 4 questions in 115 seconds. On the report by kind, the replay asked 16 and hit the 120-second limit.

**Reading:** the news says "stuck lords", but the real decision is the whole northern war. No small report fits that, and it's really a council's job. The war room can already call a council early on certain words, and "has not moved" near a storm could join that list.

### What this can't tell us

- **Whether the agent plays as well.** The walk through the run assumes it would answer the same way with less. 34 replays are a good sign at best, and only a live run can settle it.
- **Knock-on effects.** Fewer wakes means fewer notes, fewer letters and a different rhythm with the ally. A replay of one wake can't show that.
- **How much delay costs.** Hours of delay can be counted, but not what a lord would have met in those hours.
- **How far it generalises.** This is one campaign, with me playing against two agents. The "weak under 700" line comes from this run's Rohan crowd, and a different war might need a different line.
- **The local model.** The cards describe what Claude needs. A smaller model may need more spelled out.
- **The replays' limits.** They use today's instructions, not those of the time. Way-finding answers from the recorded world, with morale and tactics at set values, so battle odds are close but not exact. The agent can also see it's a replay: at #5900 it wrote that the message is "framed as a replay mode instruction", and at #6067 "this is a replay and I should answer quickly".
