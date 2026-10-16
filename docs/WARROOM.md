# The war room

The war room (`WarRoom.exe`, in `src/BannerlordAgents.WarRoom`) is the program beside the game where the agent decides. Since Oct 8 it replaces the agent chat. It makes one fresh, short Claude Code call per poke, on your subscription, with no API key (plan, decision 8). Since Oct 9 (0.5) each call also carries the realm's written memory of the war: the story so far, the agent's notes and its chronicle ([MEMORY.md](MEMORY.md)).

## Why it is faster than the agent chat

| | Agent chat (Oct 8) | War room |
|---|---|---|
| Memory | one conversation that grew by about 25,000 tokens an answer (66,000 to 193,000) | a fresh call every time: instructions and library (cached), then the story so far, the notes and the poke ([MEMORY.md](MEMORY.md)) |
| Steps per answer | 2 to 3: wake, read the files, think and write | 1: everything comes in the message, and the orders come back as the reply |
| Thinking | unlimited, at max effort: 2,000 to 14,600 tokens | capped: 3,000 tokens for an ordinary poke, 5,000 for an urgent one, at high effort |
| What it decides | the whole realm, every time | at a poke, only what changed: the orders in force stand, and an empty answer is valid; the whole realm at a council of war, beside the answers (since 0.6) |
| Waiting between pokes | woke every 30 minutes to restart its watcher, even with the game closed | does nothing until a poke comes |

**Measured on the first session's recorded pokes** (`WarRoom replay`, Opus 5.5, high effort, thinking capped at 3,000):

| Poke | About | Agent chat, Oct 8 | War room | Thinking | Written |
|---|---|---|---|---|---|
| #6 | your army first seen, 48 from Western Moria | not answered | 28 s | 1,240 | 2,025 |
| #10 | your army heading for Western Moria | 93 s | 15 s | 851 | 1,507 |
| #54 | Rohan's host near Starkmoore, raids | 53 s | 11 s | 565 | 1,026 |
| #83 | Galenor raiding Celebloth | 21 s | 15 s | 646 | 962 |
| #124 | your army at the gates of Western Moria | 33 s | 24 s | 1,871 | 2,456 |

- **Median:** 15 seconds, against 53.
- **Old reports:** these pokes are in the old format, without the orders in force and the ready-made facts.
- **Its answers used the new tools.** At #6 it put four lords inside Western Moria, manned three empty castles and gathered a relief army of nine lords at Celebdil. At #124 it gathered twelve lords to relieve Western Moria, set to strike at 1.1 times the besiegers' strength.

**The thinking cap is the main dial:**
- the answer time follows the thinking almost exactly, at about 110 tokens a second;
- the effort setting alone did not bound it: a small model at "low" still thought 8,800 tokens, 85 s;
- the cap (Claude Code's MAX_THINKING_TOKENS) did: the same call took 10 s.

## Running it

```
powershell -ExecutionPolicy Bypass -File scripts\warroom.ps1
```

- **What it does:** it runs in that window until Ctrl+C. It answers the newest poke whenever one is newer than its last answer, and waits otherwise.
- **Pokes that arrive during a call** are not lost: the next poke's WHAT CHANGED SINCE YOUR LAST ANSWER covers them.
- **One-time setup:** `scripts\setup-agent.ps1 -Mode live` (or `shadow`). It makes the agent's folder, installs the instructions and the order library, and points the bridge at the channel.
- **The old agent chat:** close it. It watched the old channel folder, which the bridge no longer writes.

## Settings (`warroom.txt` in the agent's folder)

| Key | Default | What it does |
|---|---|---|
| `model` | claude-opus-5-5 | the model |
| `effort`, `urgent_effort` | high, high | Claude Code's effort level for an ordinary poke and an urgent one |
| `thinking_cap`, `urgent_thinking_cap` | 3000, 5000 | the most it may think, in tokens (0: no cap) |
| `urgent` | is besieged, laid siege, passed from, declared war, Enemy army in view, was taken prisoner, army has formed, fell, striking the besiegers | words in a poke's reasons that make it urgent |
| `timeout_seconds` | 120 | the longest a call may take |
| `lookups` | on | let the agent read its look-up files during a call |
| `max_lessons` | 12 | lessons kept in the notes; past it, the chronicler merges them |
| `memory`, `story_every`, `story_words`, `story_model`, `story_effort`, `story_thinking_cap`, `conversation_limit` | written, 12, 450, (the agent's), medium, 2000, 180000 | the realm's memory and its chronicler ([MEMORY.md](MEMORY.md#settings-warroomtxt)) |
| `council` | on | the council of war ([below](#the-council-of-war)); written memory only |
| `council_every_hours`, `council_gap_hours` | 24, 6 | a council sits at least this often, and never sooner than this after the last (in-game hours; the first look excepted) |
| `council_after` | First look since the campaign was loaded, passed from, declared war on, made peace, is besieged by, army has formed | words in a poke's reasons (or in what changed since the last answer) that call a council |
| `council_effort`, `council_thinking_cap`, `council_timeout_seconds` | high, 12000, 420 | the council's call: room to think about the whole realm |
| `claude_path` | (empty) | the newest Claude Code in VS Code's extension folder, else claude on the path |

## One call

- **The system prompt** is the agent's instructions (`warroom-system.md`, from `agent/warroom-system.template.md`) plus the order library (`tools.txt`). It changes only when they do, so it stays cached between calls.
- **The message** is the realm's memory (the story so far, the notes, and any chronicle lines the story does not cover yet; [MEMORY.md](MEMORY.md)), the number of the last poke answered, and the poke itself:
  - why it is asked;
  - what changed since the last answer;
  - the orders in force;
  - ready-made facts;
  - the full report.
- **The call:**
  - `claude -p` with stream-json, the model and effort, and the system prompt file;
  - the only tool is `--tools Read`;
  - safe mode, so no project instructions, skills or plugins load;
  - prompts are refused rather than asked, and no session is saved.
  - It runs in the channel folder, so the agent can read only the channel. Anything outside is refused.
  - The war room and Claude Code run at below-normal priority with efficiency mode, on the efficiency cores.
- **The reply** must be one JSON object: `poke`, `orders`, an optional `plan` and `lessons`.
  - When there is more than one object, the last that reads wins. If none reads, the war room retries once at low effort, showing the agent its own answer.
  - The orders go to `orders\poke-N.json`, swapped in whole.
  - The plan, the notes on enemies and the lessons go into the notes ([MEMORY.md](MEMORY.md)).
- **The chronicler** rewrites the story so far in quiet moments, in a call of its own beside the answers (`chronicler-system.md`).

## The council of war

Since 0.6 (Oct 9). A poke's answer is fast and local: it decides what that poke needs and leaves the rest standing, so the lords nobody orders stay with their own judgment (the game's AI) and the realm is never set on one course as a whole (second live session: 3 of 49 lords held orders). The council is the other half: a slower call, now and then, that sees the whole realm and may reorder any lord.

- **When it sits** (`Room.CouncilDue`):
  - right after the first look of a game session (`First look since the campaign was loaded`), gap or not;
  - after a big event: any of `council_after`'s words in the reasons of a poke just answered, or in what changed since the last answer;
  - when its time has come: `council_every_hours` after the last council, or sooner if the last council asked for it (`council_in`, 4 to 72 hours);
  - once in each run of the war room, even with no event;
  - never sooner than `council_gap_hours` after the last one (except after the first look), never two at once, and only while the game is running (the bridge's briefing less than ten minutes old). A council that fails waits five minutes.
- **What it sees:** the same written memory as a poke (the story so far, the notes, the chronicle not in the story yet), then:
  - COUNCIL OF WAR, with why it sits now;
  - the bridge's **briefing** (`council.txt`, rewritten every in-game hour, [TOOLS.md](TOOLS.md#what-the-agent-reads-besides-the-tools)): the purse, every lord in detail, the muster at each castle and town, the captured lords;
  - the hourly **situation report** (`report.txt`): the realm in sum, lords and armies, holdings, captured lords, what is in view and was seen earlier, enemy holdings near, the orders in force, the recent history.
  - Its instructions are the same system prompt as every poke (so it stays cached), whose section "The council of war" says what a council is for.
- **Beside the answers, never in their way:** it runs on its own thread, like the chronicler. Pokes that come meanwhile are answered at once. A fast answer that orders a lord while the council sits saw a newer world, so **its order stands**: the council's order for that lord is dropped, and that lord is taken out of the council's gather and add lists (`Room.DropOrdered`). The next poke's message says which of the council's orders did not go out.
- **Its answer** is the same JSON object as a poke's, with any number of orders, and an optional `"council_in"`: in how many in-game hours the next council should sit. The orders go to `orders\poke-N-council-<time>.json` (N: the newest poke), which the bridge reads like any answer; the plan, enemies and lessons go into the notes.
- **It has no plan of ours** ([plan, decision 15](PLAN.md#decisions-for-the-build)): the instructions teach doctrine, not this war's moves ([DOCTRINE.md](DOCTRINE.md)).
- **By hand:** `WarRoom council` sits one now (the game must be running).

## Guards

- **The answer ends the call,** not Claude Code's exit: Claude Code sometimes lingers after answering, so it is closed after 2 s.
- **A call that sends nothing for 45 s counts as stalled:** it is stopped and asked once more.
- **An answer that types tool calls as text, or runs past 16,000 characters, counts as a runaway,** and is stopped and asked once more. This was seen when the instructions promised look-ups to a call that had no tools; now the look-ups section is dropped whenever reading is off.
- **A poke from before the war room started** is answered only if it is under 2 minutes old.
- **One war room per agent folder** (since 0.6.3): `WarRoom run` holds a lock for the folder while it runs (a named mutex, `LiveLock`). A second `run` stops at once, and `once` and `council` refuse while it runs. On Oct 16 a `once` started beside a running war room answered the first poke too, and the game carried out both answers' orders: two of the agent's army calls were refused and its siege host was split.

## Logs (`%LOCALAPPDATA%\BannerlordAgents\logs\warroom`)

- **`calls.jsonl`:** one line per call, `kind` "answer", "council" or "story" (a council's line also has why it sat, the briefing's and situation's sizes, the orders dropped because a faster answer ordered those lords, and `council_in`):
  - which poke, and when it was;
  - urgent or not, model, effort, thinking cap;
  - total seconds, call seconds, when the answer began (`answer_began_ms`);
  - tokens in, out and thinking, and cache read and written; `context_tokens`, all it read;
  - the memory: its kind, the story's words, the chronicle lines carried (`gap_lines`), the notes' size and what the answer changed in them (`notes_changed`), the conversation (in that memory);
  - the steps, the number of orders, any retry, any denied read.
  - For the chronicler: the lines it was given, the story's words, any lessons merged.
- **`calls\<date>\<time>-poke-N\`**, **`<time>-council-N\`** and **`<time>-story\`:** the prompt, Claude Code's event stream (`raw.jsonl`) and the answer (or the story), for reading side by side.
- **`warroom-<date>.log`:** the window's lines, memory included ("Memory: the story so far was rewritten ...", "Memory: an earlier save was loaded ...").
- **`system-<hash>.md`**, **`chronicler-<hash>.md`:** each system prompt used.

## Other commands

- `WarRoom story`: the chronicler rewrites the story so far now, from every chronicle line it does not cover yet.
- `WarRoom council`: a council of war sits now (the game must be running, and no `WarRoom run` for the folder: that one holds its own councils).
- `WarRoom selftest`: the memory and the council checked in a scratch folder, without any call (38 checks; `scripts\deploy.ps1` runs it before installing).

## Replay

```
WarRoom replay --pokes <pokes_<realm>.jsonl> --numbers 6,10,54 [--effort high] [--cap 3000] [--model m] [--tag t]
```

Recorded pokes are answered again in a scratch folder:
- with the live instructions and library;
- each on its own, with empty notes and no look-ups;
- without writing any orders.

It prints the time, tokens and steps of each call. This is how settings and models are compared: the RQ3 numbers on answer time and usage.

With `--memory written` or `--memory conversation`, the pokes are answered in order instead, with the memory carried along, to compare the two memories ([MEMORY.md](MEMORY.md#comparing-the-two-memories)).

## Not available

**Fast mode:** Claude Code reports it is off for this account ("extra usage" is disabled), so the war room does not use it.
