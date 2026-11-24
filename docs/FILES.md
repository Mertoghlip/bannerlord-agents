# The realm's files: context the agent reads instead of reports pushed to it

Since war room 0.20 and module 0.22.0 (Nov 20). A continuous agent whose `warroom.txt` says `context = files` no longer gets the war in its messages. It keeps a shelf of files about its own realm, its own thinking and the war's history, and reads the pages it needs. Each message says only what woke it. `context = report` (the default) keeps the reports, so both setups can be run side by side.

The Orcs and Dol Guldur run on files since Nov 20, 20:32, and the kingdom's agent since Nov 24 (war room 0.20.3; it had been on fresh calls and reports), so every realm agent of a test runs the same way. In a test nobody plays in person (`human_player = none`, the C arms), the instructions' lines about the human player change (`Room.NoHumanEdits`) and the shelf keeps no human player's record.

## Why

Until 0.20 each agent got two big things:
- **Standing instructions** of about 153,000 characters. About 65,000 of them were the order library (`tools.txt`) and how to write orders of its own.
- **A long message at every wake** that repeated most of the realm's picture: about 28,000 characters for a wake and 85,000 for a strategy review, in the median of Nov 11 to 20.

All of it stayed in the conversation. That is what filled conversations to their handover (about 650,000 tokens), and every step re-read it. The design is mine from Nov 20; this page is how it was built.

## The files

Each file is a folder of the channel's `files\` (`<agent>\warroom\files\`), and each tab a small page in it, so a page can be read without the rest.

| File | Written by | What it holds |
|---|---|---|
| `lords` | the game, each build | index, by-front, a page per army (its leader and the lords in it or joining it, who have no page of their own) and per lord not in an army (position, heading, strength, ranks, food, gold, his order and who gave it, the hours to each holding, the enemies near him), no-order, held-by-the-enemy, order-results (the game's receipts of each answer and the quieter news since) |
| `settlements` | the game | index, by-front, a page per castle and town: garrison, militia, food, siege and its clock, the lords whose orders are tied to it with their hours there, the lords who can get there within a day, **the enemy strength seen around it within 3 days' march and within a day's march** (two numbers, each flagged), **every castle and town within 3 days' march at speed 2 by a way over land** (or the closest when none is) with its realm, its villages, and the game's look-up with the muster |
| `realm` | the game | in-sum (with the clock and the order delay), money, captives both ways, allies-and-letters (allies, joint plans, diplomacy, votes and offers), enemy-realms |
| `orders` | the game | index, a page per order (ours and the realm's own), common-settings, writing-your-own (the manual that was in the instructions) |
| `plan` | the agent | its own pages; the game keeps `watches` and `plan-check` (names in the plan that no longer fit, orders and watches it does not name) |
| `notes` | the agent | its own pages (lessons, worth copying); `handover` is its last handover |
| `intelligence` | the agent | a page per enemy it tracks (its read, how sure, what would prove it wrong); the game keeps `sightings` (★ the tracked ones), `enemy-holdings`, `human-player-record`, `chances-to-strike` |
| `chronicle` | the chronicler | the-war-so-far, turning-points, a page per in-game day, our-orders-worked-and-failed; the game keeps `record` (the last day) and `record-earlier` (the two before) as things happen |

The places around a holding are worked out once per map: the ways between places never change. They are kept in `files\.rings.json`, and the first build of a load takes about 0.6 s. The enemy around each place is counted from what the realm sees in view now and saw within the last day, by the length of the way over land at speed 2. Both use the way-finder copy the war room already serves for "ask the way".

**Since war room 0.20.6 (Nov 24):** a siege the realm lays at an enemy place, and any fight a lord of ours is in, goes on his page (`Siege:` or `Fight:`) with its clock: walls, storm and the food left, counting the food the lords inside carry. The siege also goes under that place on intelligence/enemy-holdings. Before, a siege of an enemy place reached no page at all, so the agent besieging Hollin Gate wrote "their food is unknown". A besieged holding of the realm's own gives its days with its lords' food (the store alone before a siege).

## Changes, flagged

Three layers:
1. **`shelf.md`**: the hour, the order delay, wakes today, the next review, and for each file what is new to the agent, the weightiest first.
2. **Each folder's `changes.md`**: the last three in-game days of changes, newest first, NEW until seen.
3. **The top of each page**: its own changes of the last day, marked ▲.

**What counts as a change:**
- an order begun, ended or changed;
- a lord's state (sheltering, falling back, besieging, fighting, raiding, taken);
- strength or a garrison moved by more than a tenth;
- ranks across half;
- food down to 3 days or to 1;
- a siege begun or lifted;
- the enemy around a place appearing, doubling, halving or going;
- a holding won or lost;
- a lord gone from the field or held;
- a watch set, its condition turning, or firing;
- an enemy army or a lord above strength 300, or a tracked one, coming into view (smaller ones in one line);
- an enemy holding's garrison;
- votes and allies;
- the human player's record;
- the plan's check;
- new lines in the record;
- the agent's own and the chronicler's pages.

A lord moving along is not a change: his page always says where he is.

**"New to you"** clears when the agent reads the page. The war room sees every read in the call's stream: a Read of the page's path, `open`, or the folder's changes page, which clears the folder.

**Loads:** a build after a load starts the changes afresh, since that world is new. The game's pages are built from the loaded world; the agent's pages come back as its save had them.

## Reading and writing

- **Reading:** any page by its whole path with Claude Code's Read tool (offered to a files agent's calls, inside its channel folder only), or the questions:
  - `open`: pages whole, brought up to the newest hour first;
  - `find`: lines of every page that hold some words;
  - `history`: the old versions of its pages.

  `look_up`, `chronicle`, `past` and the way-finding questions stay; `list` goes, since the pages are the lists.
- **Writing:** the answer's `"pages": [{"page": "plan/<name>", "text": "...", "why": "..."}]`, for plan, notes and intelligence only; `"text": ""` removes a page.
  - Each replaced or removed version goes into `memory\<campaign>\history\<file>\<page>\` with when and why.
  - The pages live in `memory\<campaign>\pages.json` and in every memory snapshot, so a load restores them with the save.
  - The old `plan`, `enemies` and `lessons` become pages too (the-plan, intelligence/<name>, notes/lessons). The first time, the agent's notes are turned into pages that way.
  - The game's pages are refused, and so is any page longer than 12,000 characters (it is cut, and the shelf says so).

## What the agent is sent

| Moment | The message |
|---|---|
| Wake | `WAKE #N (answer with one JSON object that says "poke": N)` and its WHY lines, word for word |
| Strategy review | `STRATEGY REVIEW (answer with one JSON object that says "poke": N): <why>.` |
| New conversation | `A NEW CONVERSATION BEGINS: <why>. Answer this first message as a strategy review.` |
| Reload | the reload's note, as before |
| Handover | bring the plan, notes and intelligence pages up to date, then a short handover of only what the files do not hold, as one JSON object `{"pages": [...], "handover": "HANDOVER ..."}` |
| Instructions changed | as before |

Before each call the war room builds the files from the hour's question book:
- a wake's own `book\poke-N.json`;
- a review's fresh `book\now.json` (`FreshNow`), with the briefing asked for as before;
- the newest hour again before an `open`.

The build takes 20 to 40 ms after the first. A conversation begun on the reports is not carried on in files mode, nor the other way: the next message begins a new conversation (`Room.SwitchContext`).

## The instructions

`agent/warroom-files.template.md` (installed as `warroom-files.md`) lays its sections over the continuous instructions:
- Rules;
- How you are asked;
- Strategy reviews;
- Your memory;
- Your files, a new section, with where each part of the old report now lives;
- How you answer;
- Look-ups;
- Your handover;
- Writing your own tools.

The doctrine, principles and rules of play are unchanged. In place of the order library, the instructions end with a pointer to the Orders file (`Room.FilesOrdersPointer`). The instructions went from 153,035 to 90,976 characters for the orcs.

There is no longer a limit on the realm's own orders. The 30 of 0.13 was lifted in module 0.22.0. The speed rule still stops an order that slows the game.

## The chronicler

In quiet moments, once a whole in-game day has passed that it has not written, the chronicler (`Room.WriteChronicle`, a separate call) writes:
- that day's page, at most three days a call;
- the war so far;
- the turning points;
- what worked and failed of the realm's orders.

Its input is the realm's record and its own last pages, with the plan for context only. The first time it writes only the last two days. Its instructions are `chronicler-files.md` in the agent's folder, or the built-in ones. Since war room 0.20.1 an answer cut short of its last closing bracket is mended and kept (`Room.ChroniclerPages`; Nov 20 Dol Guldur's was thrown away once and written again five minutes later).

**Since war room 0.20.1 (Nov 21):**
- **The view file must be of this load** and no more than two in-game hours older than the book (`Room.ViewFor`). The game writes it at the end of each hour's work, after that hour's wakes, so the first wake after a load found the previous session's, and the next build flagged false changes ("Hiltbolt: food 6 → 1 days"). A stale one is asked of the game afresh; still stale, the build goes without it, the shelf says the holdings' food and nearby enemies are missing, and the next build with a view starts its facts afresh instead of flagging them.
- **Wakes today** count the wakes of this in-game day up to the hour, by each wake's hour (`Talk.WakeHours`); a load drops those after the loaded hour. They had been counted by day, so a new conversation was told "39 wakes today, keep to about 12", counting my earlier loads of the same day.

## Measures for the test

Each call's line in `calls.jsonl` gains:
- `context`;
- `shelf_ms` (the build) and `shelf_changes`;
- `pages_read`, `read_steps` (steps that asked anything) and `opens`;
- `pages` (which ones);
- `page_writes`.

`message_chars` is the message alone. Questions on the files go into `routes.jsonl` beside the others, and the chronicler's calls into `calls.jsonl` as `chronicle`.

## Checks

- `WarRoom selftest` (246 checks; `SelfTestFiles.cs`, 14 new), on a made-up book built twice:
  - pages for every lord, army, holding and order, the army's lords only on its page;
  - the changes flagged on the shelf, the changes page and the page;
  - a Read in the stream and `open` clearing them;
  - `find` and `open` by id;
  - the plan check naming a captured lord;
  - a wake's message;
  - pages written, refused, kept in history, removed and restored by a load.
- `WarRoom shelf --dir <agent> [--out dir] [--again] [--from N]` builds the files once from the newest poke into a scratch folder, and prints each folder's size, the files instructions' size and a wake's message. With `--from N` it builds from wake N's book first, to see what the newest flags. On the orcs' and Dol Guldur's books of Nov 20, the first build took 650 and 540 ms and the later ones 17 to 35 ms. From wake 6843 to 6849 it flagged 76 changes.
