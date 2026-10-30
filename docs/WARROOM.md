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
- **Setting up and starting:** `scripts\setup-agent.ps1` makes an agent's folder ([below](#agents-are-folders-since-09)). `scripts\agents.ps1 start` starts every agent's war room and the dashboard.
- **Started by the game when missing (since 0.13.5):** a running war room holds a lock named after its folder; at each load the bridge looks for it for every agent switched on, and starts any that is not running, with no window, when `war_room` in settings.txt names `WarRoom.exe` (`WarRooms.Check`); with no `war_room` it raises the alarm `war_room_down` instead. So after the laptop restarts, loading the save is enough. The dashboard is not started this way.
- **The old agent chat:** close it. It watched the old channel folder, which the bridge no longer writes.

### Agents are folders (since 0.9)

An agent is a folder outside this repository. Adding one means making a folder with a system prompt and listing it, with no code changes. Every agent is made and treated the same way.

| In the folder | What it is |
|---|---|
| `agent.txt` | who the agent is: `realm` (kingdom id), `mode` (off, shadow or live), `lords` (who obeys in live mode: all, or ids and names) and `channel` (its channel folder, `warroom`) |
| `warroom-system.md` | its instructions (the system prompt), followed in each call by the order library from its channel |
| `chronicler-system.md` | the instructions of its chronicler, who writes the story so far |
| `warroom.txt` | its war room's settings ([below](#settings-warroomtxt-in-the-agents-folder)), including where its logs go (`log_dir`) |
| `warroom\` | its channel: pokes, reports, the council briefing, look-ups, receipts, its chronicle, `tools.txt`, and `orders\` for its answers |
| `memory\<campaign>\` | its memory: notes, the story so far ([MEMORY.md](MEMORY.md)) |

- **The bridge:** `agents = <folder>; <folder>` in the bridge's `settings.txt` lists the agents. At every load, each folder's `agent.txt` makes one agent in the game. The agent holds its channel, chronicle, memory of sightings, orders in force and latest view together. Each is poked about what its own realm sees, and an answer in its folder can order only its own realm's lords. A folder that cannot be used (no `agent.txt`, no such realm, a realm listed twice) is named in the log and the self-check. Without `agents`, the settings of 0.8 and before (`watch`, `agent`, `agent_lords`, `agent_dir`, `agent_dir.<realm>`) describe the agents.
- **The war rooms:** one per agent, each its own program running from its own folder, with its own Claude calls, memory and logs. They share nothing but the order library's code.
- **Making one:** `scripts\setup-agent.ps1 -Workspace <folder> -Realm <id> -Mode live`. It writes `agent.txt`, installs the instructions from the templates only where the folder has none (`-Force` puts fresh copies in), gives a new agent a `warroom.txt` copied from another's with its own `log_dir`, and adds the folder to `agents`. For a new realm, `-RealmTitle` and `-RealmLine` rewrite the two lines of a fresh copy that name the realm: the title and the opening line. The goal names no region, so it needs no rewriting.

```
powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1 -Workspace C:\Users\karab\Projects\dolguldur-war-council -Realm dolguldur -Mode live -RealmTitle "Dol Guldur" -RealmLine "Dol Guldur, the fortress-realm of the Necromancer in southern Mirkwood ruled by Khamûl, the Easterling"
powershell -ExecutionPolicy Bypass -File scripts\agents.ps1 start
```

- **Running them:** `scripts\agents.ps1 start | stop | restart | status` starts a war room for every listed folder, and the dashboard, each detached from the window. `stop` ends them all, and must come before a build. `status` shows each agent's realm, whether its war room runs, and its last line.
- **In the game:** `agents.agent` lists the agents. `agents.agent <realm> off|shadow|live [lords]` changes one agent's mode at once and writes its `agent.txt`. `agents.view [realm]` and `agents.chronicle [n] [realm]` show any agent's realm. Agents are made at a load: add or remove one with the game closed.
- **The lieutenant (since 0.10, [LIEUTENANT.md](LIEUTENANT.md)):** a folder whose `agent.txt` says `kind = lieutenant` (and `realm = player`) gets the lieutenant's war room (`LieutenantRoom`): it answers the player's words from the campaign map's chat box (`chat.json`) instead of pokes, in `orders\chat-N.json`. `WarRoom once --dir <folder>` answers the newest `chat.json` now.

## Settings (`warroom.txt` in the agent's folder)

| Key | Default | What it does |
|---|---|---|
| `model` | claude-opus-5-5 | the model |
| `effort`, `urgent_effort` | high, high | Claude Code's effort level for an ordinary poke and an urgent one |
| `thinking_cap`, `urgent_thinking_cap` | 3000, 5000 | the most it may think, in tokens (0: no cap) |
| `urgent` | is besieged, laid siege, passed from, declared war, Enemy army in view, was taken prisoner, army has formed, fell, striking the besiegers | words in a poke's reasons that make it urgent |
| `timeout_seconds` | 240 | the longest a call may take; since 0.13.5, 0 is no limit (a call that goes silent for 45 s is still stopped, and one writing far past any answer: 16,000 characters, 48,000 once it writes tools or, since 0.13.5, the lieutenant's proposal). The lieutenant has 0 since Oct 29 |
| `lookups` | on | let the agent read its look-up files during a call |
| `route_tool` | on | offer the agent the "ask the way" tool in its answers and councils (0.7), and since 0.7.4 the "battle odds" and "roads in" questions beside it (since 0.9.3 "can catch" too, [TOOLS.md](TOOLS.md#the-chase-question-before-a-hunt-since-093)), once the walking map is copied ([ROUTES.md](ROUTES.md), [TOOLS.md](TOOLS.md#siege-control-the-storm-rule-the-relief-bars-and-battles-run-ahead-before-they-begin-since-074)) |
| `max_lessons` | 12 | lessons kept in the notes; past it, the chronicler merges them |
| `memory`, `story_every`, `story_words`, `story_model`, `story_effort`, `story_thinking_cap`, `conversation_limit` | written, 12, 450, (the agent's), medium, 2000, 180000 | the realm's memory and its chronicler ([MEMORY.md](MEMORY.md#settings-warroomtxt)) |
| `council` | on | the council of war ([below](#the-council-of-war)); written memory only |
| `council_every_hours`, `council_gap_hours` | 24, 6 | a council sits at least this often, and never sooner than this after the last (in-game hours; the first look excepted) |
| `council_after` | First look since the campaign was loaded, passed from, declared war on, made peace, is besieged by, army has formed, offers peace, offers an alliance, offers a call to war (the last three since 0.13) | words in a poke's reasons (or in what changed since the last answer) that call a council |
| `thinking_display` | summarized | how each call asks Claude Code for the model's thinking (0.13): summarized keeps its summary (`thinking.txt`), omitted leaves it out |
| `council_effort`, `council_thinking_cap`, `council_timeout_seconds` | high, 12000, 420 | the council's call: room to think about the whole realm |
| `claude_path` | (empty) | the newest Claude Code in VS Code's extension folder, else claude on the path |
| `log_dir` | (empty) | where its logs go; empty is `%LOCALAPPDATA%\BannerlordAgents\logs\warroom`. A second agent's war room keeps its own (since 0.8) |

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
  - the only built-in tool is `--tools Read`; since 0.7, once the walking map is copied, the war room also serves the "ask the way" tool itself (`RouteTool`, an in-process "sdk" MCP server over the call's own stream: `--input-format stream-json`, `--mcp-config`, `--strict-mcp-config`, `--allowedTools mcp__routes__ask_the_way`), which safe mode leaves on; without it, the instructions' "## Asking the way" section is left out ([ROUTES.md](ROUTES.md#the-agents-tool-routetool-the-war-room));
  - safe mode, so no project instructions, skills or plugins load;
  - prompts are refused rather than asked, and no session is saved.
  - It runs in the channel folder, so the agent can read only the channel. Anything outside is refused.
  - The war room and Claude Code run at below-normal priority with efficiency mode, on the efficiency cores.
- **The reply** must be one JSON object: `poke`, `orders`, an optional `plan` and `lessons`; since 0.13 also `tools` (the agent's own, [TOOLS.md](TOOLS.md#the-agents-own-tools-since-013)), `diplomacy` and `letters` ([DIPLOMACY.md](DIPLOMACY.md)). Since 0.13 the questions served beside "ask the way" include `check_tool`, and the instructions' "## Checking a tool" is left out of a call without them.
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
  - **Looked for after every answer as well as in a quiet moment** (since 0.11.5). Until then it was looked for only when no poke was waiting, and a busy realm's pokes never stop: in run C2 (Oct 24) the kingdom's war room, with a poke every few seconds and answers of 10 to 30 s, was idle 44 times in 80 minutes, so about 20 councils sat in 60 days, some 95 to 208 in-game hours after the last, and the one asked for 12 hours after the victory at Lasgalor sat 8.7 days later.
- **What it sees:** the same written memory as a poke (the story so far, the notes, the chronicle not in the story yet), then:
  - COUNCIL OF WAR, with why it sits now;
  - the bridge's **briefing** (`council.txt`, [TOOLS.md](TOOLS.md#what-the-agent-reads-besides-the-tools)): the purse, every lord in detail, the muster at each castle and town, the captured lords;
  - the **situation report** (`report.txt`): the realm in sum, lords and armies, holdings, captured lords, what is in view and was seen earlier, enemy holdings near, the orders in force, the recent history.
  - Since 0.12.0 both are written when the council sits, from the game's newest hour: the war room puts `orders\council-request-<time>.json` in the channel, the bridge's helper writes `report.txt` then `council.txt` at once on its urgent lane and takes the request away, and the war room waits up to 20 s for `council.txt` to be newer than its request (`Room.RequestBriefing`). If none comes, the last one written is used, or the council waits when there is none. Before 0.12.0 the bridge rewrote both every in-game hour for every agent, though only a council read them: a large part of what made its helper fall behind the game in run 20261025-1417-C2 (docs/LOGGING.md, "The helper on time"). Whether the game is running is now told by `poke.json` written within ten minutes.
  - Its instructions are the same system prompt as every poke (so it stays cached), whose section "The council of war" says what a council is for.
- **Beside the answers, never in their way:** it runs on its own thread, like the chronicler. Pokes that come meanwhile are answered at once. A fast answer that orders a lord while the council sits saw a newer world, so **its order stands**: the council's order for that lord is dropped, and that lord is taken out of the council's gather and add lists (`Room.DropOrdered`). The next poke's message says which of the council's orders did not go out.
- **Its answer** is the same JSON object as a poke's, with any number of orders, and an optional `"council_in"`: in how many in-game hours the next council should sit. The orders go to `orders\poke-N-council-<time>.json` (N: the newest poke), which the bridge reads like any answer; the plan, enemies and lessons go into the notes.
- **It has no plan of ours** ([plan, decision 15](PLAN.md#decisions-for-the-build)): the instructions teach doctrine, not this war's moves ([DOCTRINE.md](DOCTRINE.md)).
- **By hand:** `WarRoom council` sits one now (the game must be running).

## Guards

- **The answer ends the call,** not Claude Code's exit: Claude Code sometimes lingers after answering, so it is closed after 2 s.
- **A call that sends nothing for 45 s counts as stalled:** it is stopped and asked once more.
- **An answer that types tool calls as text, or runs past 16,000 characters, counts as a runaway,** and is stopped and asked once more. This was seen when the instructions promised look-ups to a call that had no tools; now the look-ups section is dropped whenever reading is off. Since 0.13 an answer that has begun its `"tools"` may run to 48,000 characters (a tool's definition is long).
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
- **`routes.jsonl`** (0.7): every question to the "ask the way" tool (and since 0.7.4 to "battle odds" and "roads in", with `tool`), with its poke and the answer; `replays\routes.jsonl` for replays. Each call's line in `calls.jsonl` gives `ways_asked` and `way_tool` (whether it was offered). Since 0.13 every `check_tool` too: the definition, whether it passed, its problems, the lord tried and the answer.
- **Since 0.13 (the training record):** each line of `calls.jsonl` also has the run and its arm, the realm, the instructions' fingerprint (`system_hash`) and the order library's (`library_hash`: which tools the call had, the realm's own among them), the thinking's length (`thinking_chars`) and counts of the answer's tools, diplomatic acts and letters; each call's folder keeps the model's thinking as summarized (`thinking.txt`; `thinking_display` in `warroom.txt`, summarized by default); the orders file names its call's folder (`call`) and carries the answer's `tools`, `diplomacy` and `letters`.

## Other commands

- `WarRoom story`: the chronicler rewrites the story so far now, from every chronicle line it does not cover yet.
- `WarRoom council`: a council of war sits now (the game must be running, and no `WarRoom run` for the folder: that one holds its own councils).
- `WarRoom selftest`: the memory, the council and the "ask the way", "battle odds" and "roads in" tools checked in a scratch folder, without any call (50 checks in 0.7.4; `scripts\deploy.ps1` runs it before installing).
- `WarRoom toolcheck --write [--model m]` (0.13): one real, short call (a small model) given the instructions' "Writing your own tools" and "Checking a tool", asked to write a two-state tool with one mistake in its first draft, check it with `check_tool`, fix it and answer with it; it passes when its last check passed and the tool it answers with passes the checker, and says whether a draft was refused and fixed (on Oct 28: refused, fixed, passed, in 8.8 s with claude-haiku-5-5).
- `WarRoom toolcheck [--model m] [--big]`: one real, short call (a small model by default) on the self-test's made-up map, asked to use the "ask the way" tool once, then a plain call: the whole path the agent's tool takes. `--big` sends a message as long as a real poke: on Oct 16 that froze both sides (the message filled the pipe while Claude Code was busy with the tool's handshake, and the tool's answer queued behind it), so standard input now has a writer thread of its own and the message goes only after the handshake. Passed after the fix in about 4 s.

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
