# The war room

The war room (`WarRoom.exe`, in `src/BannerlordAgents.WarRoom`) is the program beside the game where the agent decides. Since Oct 8 it replaces the agent chat. It makes one fresh, short Claude Code call per poke, on your subscription, with no API key (plan, decision 8).

## Why it is faster than the agent chat

| | Agent chat (Oct 8) | War room |
|---|---|---|
| Memory | one conversation that grew by about 25,000 tokens an answer (66,000 to 193,000) | a fresh call every time: instructions and library (cached), the journal and the poke |
| Steps per answer | 2 to 3: wake, read the files, think and write | 1: everything comes in the message, and the orders come back as the reply |
| Thinking | unlimited, at max effort: 2,000 to 14,600 tokens | capped: 3,000 tokens for an ordinary poke, 5,000 for an urgent one, at high effort |
| What it decides | the whole realm, every time | only what changed: the orders in force stand, and an empty answer is valid |
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
| `max_lessons` | 12 | lessons kept in the journal |
| `claude_path` | (empty) | the newest Claude Code in VS Code's extension folder, else claude on the path |

## One call

- **The system prompt** is the agent's instructions (`warroom-system.md`, from `agent/warroom-system.template.md`) plus the order library (`tools.txt`). It changes only when they do, so it stays cached between calls.
- **The message** is the journal, the number of the last poke answered, and the poke itself:
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
  - The plan replaces the journal's standing plan, the lessons are added, and other sections of the journal are kept.

## Guards

- **The answer ends the call,** not Claude Code's exit: Claude Code sometimes lingers after answering, so it is closed after 2 s.
- **A call that sends nothing for 45 s counts as stalled:** it is stopped and asked once more.
- **An answer that types tool calls as text, or runs past 16,000 characters, counts as a runaway,** and is stopped and asked once more. This was seen when the instructions promised look-ups to a call that had no tools; now the look-ups section is dropped whenever reading is off.
- **A poke from before the war room started** is answered only if it is under 2 minutes old.

## Logs (`%LOCALAPPDATA%\BannerlordAgents\logs\warroom`)

- **`calls.jsonl`:** one line per call:
  - which poke, and when it was;
  - urgent or not, model, effort, thinking cap;
  - total seconds, call seconds, when the answer began (`answer_began_ms`);
  - tokens in, out and thinking, and cache read and written;
  - the steps, the number of orders, any retry, any denied read.
- **`calls\<date>\<time>-poke-N\`:** the prompt, Claude Code's event stream (`raw.jsonl`) and the answer, for reading side by side.
- **`warroom-<date>.log`:** the window's lines.
- **`system-<hash>.md`:** each system prompt used.

## Replay

```
WarRoom replay --pokes <pokes_<realm>.jsonl> --numbers 6,10,54 [--effort high] [--cap 3000] [--model m] [--tag t]
```

Recorded pokes are answered again in a scratch folder:
- with the live instructions and library;
- with an empty journal and no look-ups;
- without writing any orders.

It prints the time, tokens and steps of each call. This is how settings and models are compared: the RQ3 numbers on answer time and usage.

## Not available

**Fast mode:** Claude Code reports it is off for this account ("extra usage" is disabled), so the war room does not use it.
