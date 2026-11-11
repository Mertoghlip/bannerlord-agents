# The war room

The war room (`WarRoom.exe`, in `src/BannerlordAgents.WarRoom`) is the program beside the game where the agent decides. Since Oct 8 it replaces the agent chat. It makes one fresh, short Claude Code call per poke, on my subscription, with no API key (plan, decision 8). Since Oct 9 (0.5) each call also carries the realm's written memory of the war: the story so far, the agent's notes and its chronicle ([MEMORY.md](MEMORY.md)).

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
| #6 | my army first seen, 48 from Western Moria | not answered | 28 s | 1,240 | 2,025 |
| #10 | my army heading for Western Moria | 93 s | 15 s | 851 | 1,507 |
| #54 | Rohan's host near Starkmoore, raids | 53 s | 11 s | 565 | 1,026 |
| #83 | Galenor raiding Celebloth | 21 s | 15 s | 646 | 962 |
| #124 | my army at the gates of Western Moria | 33 s | 24 s | 1,871 | 2,456 |

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
| `council_at_battle` | on | an extra council as my own battle begins, beside the regular ones ([below](#the-extra-council-as-my-battle-begins-since-0151)) |
| `cache_ttl` | 5m | how long the prompt cache keeps what each fresh call writes (0.17.7): 5m (writes at 1.25 times the input price) or 1h (twice); empty leaves Claude Code's default, one hour on the subscription. Passed as `CLAUDE_CODE_PROMPT_CACHE_TTL` (Claude Code 2.1.242 or later). Each call's own report is used for under a minute, and the standing prompt stays cached while calls come within five minutes of each other; the one long conversation (`memory = conversation`) keeps the default. Each line of `calls.jsonl` has `cache_write_1h` (the part of `cache_write` that went to the one-hour cache); `WarRoom toolcheck` prints both |
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

### The extra council as my battle begins (since 0.15.1)

My own battle stops the game's clock for minutes, so a council sitting then loses no game time. A regular council loses a median of 5 to 8 in-game hours while it thinks (run A: up to 45), because the clock runs on. So each realm agent gets one extra council when my battle begins.

- **When:** the game writes `spare.txt` in each realm agent's channel when a scene begins while I am in a battle on the map. A walk through a town opens a scene but no battle, and an auto-resolve opens no scene. Agents that are off and the lieutenant get no word.
- **The regular councils are left alone:** it does not move their times or count toward their gap. Its `council_in` can only bring the next one sooner. If it fails, no regular council waits for it.
- **When it does not sit:** a council is already sitting; a regular council is due, which sits instead; the last council of either kind read this same in-game hour's report, so a reload after a crash doesn't pay twice; the game's word is over two minutes old; or the game's briefing doesn't come in time, since an old briefing would defeat the point.
- **What the agent reads:** that it is an extra look over the realm, and that its regular councils keep their times. It isn't told why there is time for one, so it doesn't learn I am in a battle.
- **What it doesn't know:** how the battle ends. It reads the newest in-game hour's report. An order for a lord taken in that battle is refused like any other. Its orders are read at once and carried out when the clock runs again.
- **The record:** `calls.jsonl` has `"extra": true` on its line, and its call's folder ends in `-extra`.
- **A local model:** this needs the model running during the battle. A local model unloaded for battles couldn't sit these.

Why battles and nothing else, from the screen changes in run A's launch logs (one run, one player; time with the game window in front; 6 of the 16 battle visits ended in a crash, so those are cut short):

| Where | Visits | Average | Typical | Lasted over 75 s |
|---|---|---|---|---|
| Battles (10 separate ones) | 16 | 10.5 min | 9.6 min | all |
| Stopped on the map, for 30 s or more | 48 | 95 s | 74 s | 47% |
| Town and castle menus | 69 | 63 s | 11 s | 13% |
| Inventory | 22 | 27 s | 21 s | 4% |
| Party | 33 | 19 s | 16 s | none |
| Kingdom | 7 | 15 s | 15 s | none |
| Clan | 12 | 11 s | 11 s | none |

A council took a median of 72 s, and 250 s at the longest. The shortest battle, counting time with the window behind, was 293 s. 6 of run A's 142 councils happened to sit wholly inside a battle, and the 30 orders of the two I checked were all carried out once the clock ran again. Stops on the map can't be foretold: of those that had already lasted a minute, 31% lasted the next 75 s.

## News wakes (since 0.14)

A poke is a news wake. `agent.txt`'s `news` says what it reads (docs/PLAN-news.md has the plan, the decisions and the measures):

- `full`, the default: the whole report, as before.
- `shadow`: the whole report goes; the news report is built too, written to `reports\<realm>_latest_news.txt` and kept in `pokes_<realm>.jsonl` beside it, for comparison.
- `theatre`: the news report goes. Its news's focus and clock (`Theatre`, the bridge) say how far it reaches: everyone who can be there before the development is decided, at least 6 hours and at most 3 days of marching, a day where no clock runs. The rest of the realm is a line a region with each lord's name and id, and a question away. A first look and a quiet day keep the whole report; votes, offers, wars and peace get the realm's diplomacy, purse and sum.

In theatre mode a news wake's call differs in four ways. Councils are untouched: they read the whole realm, the full instructions and the look-up files as before.

- **Instructions:** each `## ` section of `warroom-news.md` takes the place of the same section of `warroom-system.md` (tool writing at a council only, the questions in place of the look-up files); each version keeps its own file by fingerprint, so both stay cached.
- **Message:** the plan and the lessons whole, the notes on the enemies and realms of this news only, the chronicle lines about it.
- **Tools:** no Read and no `check_tool`; `look_up`, `list` and `chronicle` (`BookTools.cs`) answer from the poke's question book (`book\poke-N.json`, written by the bridge before the poke, from the same hour and view, fog-checked like the report, the newest 40 kept), the realm's chronicle and the agent's notes. The bridge counts every id in the book as shown to the agent (`Channel.Shows`). The bridge no longer writes the look-up files for a news wake; it writes them for a council when the briefing is asked for.
- **Tools written in an answer** are dropped (`tools_dropped` in `calls.jsonl`), and the next answer and the next council are told.

`calls.jsonl` also gets `news_mode`, `recipe`, `message_chars`, `read_tool`, `book_asked` and `tools_offered`; `routes.jsonl` a line per book question.

## The wake rule (since 0.15)

Each agent's `agent.txt` says `wake = rule` (the default) or `all` (every line of news wakes it, as before). The study behind it is [reports/2026-11-06-news-wake-study.md](reports/2026-11-06-news-wake-study.md).

- **The lanes.** The game sorts each line of news before any call, by the list in `src/BannerlordAgents/Wake/wake-rules.json`, the first rule that matches: `now` wakes the agent, `wait` wakes it after 6 in-game hours, `digest` never does and rides the next call. A line no rule matches wakes it. The rules are data in the behavior language; agents can't change them yet.
- **What a wake holds.** The `now` and `wait` lines are its news, in WHY YOU ARE ASKED NOW. The digest goes in WHAT CHANGED. A first look takes everything as news.
- **One call at a time.** While a poke has no answer the game holds the news, at most 12 in-game hours, so the next report is fresh.
- **Letters.** A letter marked `"urgent": true` wakes its reader at once; others wait. A wake with a letter reads the whole report, not the news report.
- **The council.** The game writes `clock.txt` in the channel every reported hour, and the war room reckons a council's time from it, so a council sits on time with no poke. It reads the lines held back, which then no longer wake the agent. Three lords stuck in one wake's news, or a fast answer that runs out of time, also call one.
- **The record.** `wake_<realm>.jsonl` in the campaign's data folder: each line's kind, lane, rule and facts, each wake and why it came, and what each council took up. A poke's own record keeps each line's lane, and its digest apart.
- **The check.** `SmokeTest --wake-check <pokes_realm.jsonl> --run <id> --out <lines.jsonl> [--show 6301,6248]` runs a recorded run through the rule and the two short lists.

## Continuous agents (since 0.18)

On Nov 10 I froze the fresh-call design at git tag `fresh-calls-0.17.7`. Each fresh call wrote its whole report into the prompt cache, and with one wake every two minutes of play, that cost more of my plan than I could keep spending. A continuous agent instead keeps one conversation per realm. It sets its own wakes and holds its strategy reviews in that conversation. When the conversation has grown long, the agent writes its own handover at a quiet moment, and the next conversation starts from it. The fresh-call design still runs with the old settings, for comparison.

Switching an agent: `scripts\setup-agent.ps1 -Workspace <folder> -Continuous` sets `wake = self` and `news = theatre` in `agent.txt`, sets `memory = continuous` in `warroom.txt` (keeping the old file beside it), and installs `warroom-continuous.md` if the folder has none. `-Fresh` switches the agent back to fresh calls.

**The game's side: what wakes the agent** (`agent.txt`: `wake = self`; `Watches.cs`, `WakeRules.SortSelf`)
- **Watches.** An answer may carry a `"watch"` list. Each watch has a name, a lord (its `me`), names for the ids its conditions read, up to 8 conditions with the words each wakes the agent with, lords to follow, a check-in every N hours, and an end hour or end condition. The conditions use the behavior language and are read every in-game hour on the realm's own view, the way a tool's rules are (`Runner.WatchScope`), so they see only what the realm sees. Ids enter a condition only through the watch's names; one written straight in is refused.
- **What wakes it.** A condition wakes the agent when it turns true, not while it stays true; one already true when given waits until it has been false. A check-in comes due its hours after the last wake, since any wake starts every watch's check-in clock again. News of a followed lord's orders wakes it too. The locked rules still wake it, as do war, peace and offers to the realm (an offer is accepted by itself if left unanswered). So does a whole day with no wake. Every other line rides the next wake as quieter news.
- **In the report.** YOUR WATCHES comes right after WHY YOU ARE ASKED NOW in every wake, and in the review's briefing. It lists each watch and whether each of its conditions holds now. Each watch gets a receipt (kept, changed, ended or refused, with every problem), which the next wake's WHAT CHANGED carries.
- **Kept beside the save.** At a save the watches are written into the save's carry-on file (`CarryOn`). At its load, each realm's first hour takes them up and checks them again, and the first look says which carried on.

**The war room's side: one conversation** (`warroom.txt`: `memory = continuous`; `Room.Continuous.cs`)
- **The same call every time.** Every call of a conversation resumes the same Claude Code session, with the system prompt it began with, the same tools (the question book's always; the way-finding questions if the conversation began with them), one model (`continuous_model`, default `claude-opus-5-5[1m]`, the million-token window), one effort and one thinking cap. Changing any of these mid-conversation makes the next call write the whole conversation into the cache again. Each call asks for the one-hour cache, and switches off Claude Code's own compaction (`DISABLE_AUTO_COMPACT`).
- **The opening.** A new conversation's first message holds why it begins, the agent's handover (or the old story so far), its notes, its chronicle since the handover, and the whole realm now (the briefing, plus the situation report unless the wake is a first look). The agent answers it as a strategy review.
- **A wake's message** is the news report less what the conversation already holds. WHY YOU ARE ASKED NOW, YOUR WATCHES, THE CLOCK and WHAT CHANGED come whole. The chronicle's last two days are left out. THE REST OF THE WAR and YOUR PURSE come every 12 and 24 in-game hours. Any other section exactly as last shown becomes one line. A section's long rule paragraphs come once per conversation. Each wake ends with the day's count of wakes and the budget (`wake_budget`, told, not enforced). If a call never reached the conversation, what it had marked as shown is given back.
- **Strategy reviews** sit in the same conversation, at the hour the agent sets (`review_in`, 1 to 240 in-game hours from its answer, given at a review or at any wake), and at once after a big event in a wake's news (`council_after`; not a first look, whose report is already the whole realm). Since 0.18.2 there is no daily review when no hour is set and no gap between reviews; a review that sets no next hour leaves none set. The first answer of each conversation is a review. A review's message is the briefing plus the situation report, less the briefing's own sections (purse, the human player, captured lords), the chronicle, and any section exactly as last shown.
- **The handover.** It is written as the player's own battle begins (`spare.txt`; the clock stands still) once the conversation holds `compact_at_tokens` (default 500,000), or as the game closes (its program gone) once it holds `compact_at_close_tokens` (default 500,000) and the cache is still warm. It is also written at any wake past `compact_hard_tokens` (850,000). The agent writes it in its own voice, in the nine parts its instructions give: how we got here, where the war stands, the plan, every order in force (grouped by front, the trials marked ON TRIAL), watches, the human player, lessons and what is worth copying, allies, and what it was about to do. Since 0.18.1 the request carries the game's own list for the orders and watches parts (`HandoverFacts`): every order in force, the lords with none, and the watches. While the game runs, a fresh briefing is asked for first, so the situation report holds every order as of the newest hour. At the game's close, the list comes from the newer of the last situation report and the last wake's report, whose orders section names every other order in a line. The handover is kept as the memory's story with its snapshot and as `handover-*.md`, and the next wake begins a new conversation from it.
- **Reloads.** A load at the hour the conversation reached carries it on, and the wake says so. A load of an earlier save puts the memory back to that history, as the written memory does, and the next wake begins a new conversation from the handover written before that day, plus the chronicle since.
- **Answers from the newest hour (since 0.18.1 of the module, 0.18.3 of the war room).** Before every question the agent asks in a call (the question book's and the way-finding ones; checking a tool already asks the game), the war room puts a `book-request` in the orders folder. The game writes the question book (`book\now.json`) and the realm's view file afresh from the newest hour it has copied, then a stamp (`book\now.txt`) naming the request (`Brain.FreshBook`). The war room waits up to 8 s for that stamp and answers from what was written (`Room.FreshNow`, `RouteTool.Refresh`). So a question is never answered from an hour older than the game's last copy, whether it comes in a wake, a review or the opening, and a review's questions no longer fall back on the last wake's book. The game builds the book at most once an in-game hour (asked again within the hour, only the stamp is written), and questions asked together within 2 seconds share one fresh book. With the game closed or not answering, the book and view the call began with answer. The questions' own descriptions say their answers are of the newest hour, and the instructions tell the agent to ask several questions in one step.
- **Its own tools.** The system prompt keeps the order library as it was when the conversation began. A tool kept, changed or retired later is told in the next message instead.
- **State and logs.** `conversation.json` in the campaign's memory folder holds the session, the system prompt's file, the size, what has been shown, the wakes by day and the review hours. `calls.jsonl` logs a wake as `answer` and a review as `council` (the run reports count them as before), and a handover as `handover`, each marked `continuous`. A resumed session's `total_cost_usd` is the whole session's, so each line's `cost_usd_list` is what that call added. The size after each call is the last request's own usage (`context_tokens`, `conversation_tokens`), not the call's sum.
- **The lieutenant (since war room 0.18.4).** It keeps one conversation with the player too, with the same handover rules: [LIEUTENANT.md](LIEUTENANT.md#one-conversation-since-0184).
- **Checked.** `SmokeTest` has the watches and self-wakes (`Watch018.cs`), and `WarRoom selftest` has the continuous checks (`SelfTestContinuous.cs`). A real check on Nov 10 in a scratch folder: `claude-opus-5-5[1m]` runs on the subscription with a 1,000,000-token window. The first wake wrote its 37,543 tokens into the one-hour cache ($0.32 at list prices). The second wake read them back and wrote 7,951 new ones ($0.08), with a message of 14,458 characters against the full report's 25,637.

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

News replays (0.14): `SmokeTest --newspokes pokes_<realm>.jsonl <out dir> --numbers 2041,2373` builds recorded pokes again as news wakes from their recorded views (`<out dir>\pokes.jsonl`, `<out dir>\book\poke-N.json`; a siege's or battle's clock and the look-up pages only where the record holds them), and `WarRoom replay --dir <agent> --pokes <out dir>\pokes.jsonl --numbers ... --news --calls <the war room's calls folder> [--news-template agent\warroom-news.template.md]` answers them in a scratch folder with the news instructions, the question book and the memory each original call read, cut to the news, and prints each beside what the original call did. Its logs stay in the scratch folder. `SmokeTest --coverage pokes_<realm>.jsonl <channel>\orders\done --run <id> [--out file.md]` is the coverage check; its `--sizes file` writes each poke's sizes and theatre names. `replay --campaign <campaign>` offers the way-finding questions from that campaign's newest view file (for measuring a call's size only, since they answer from the wrong hour), and `--lookups` gives the replay the Read tool.

## Not available

**Fast mode:** Claude Code reports it is off for this account ("extra usage" is disabled), so the war room does not use it.
