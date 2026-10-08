# Record formats (install 1, with the logs of version 0.3)

Everything the bridge writes is JSON, one object per line (JSON Lines). Names are snake_case, and empty fields are left out. `hours` is in-game hours since the calendar began. `date` is the game's own date text, and `wall` is the real clock.

The formats for the war room (agents' orders, tool definitions) arrive in weeks 2 and 3.

## Where the files are

`%LOCALAPPDATA%\BannerlordAgents\`

| Path | One line or file per | Notes |
|---|---|---|
| `logs\bridge.log` | human-readable log | carries across launches; past 8 MB the old one becomes `bridge_prev.log`; alarms start with `!!` |
| `logs\sessions\<date-time>\` | game launch | the detailed logs (below, and [docs/LOGGING.md](../docs/LOGGING.md)); the newest 30 launches are kept, 2 GB at most in all |
| `settings.txt` | settings | `watch`, `bridge`, `record`, `perf`, `notify`, `agent`, `agent_lords`, `agent_dir`, `detail` (full or everyday), `witness`, `ai_reasons`, `screen_alarms`, `answer_alarm_minutes`, `freeze_probe`; the console commands change these |
| `perf\perf_<yyyyMMdd>.csv` | second of campaign-map play | see the columns below |
| `data\<campaign id>\pokes_<realm id>.jsonl` | poke of the watched realm | carries the full view and the report text |
| `data\<campaign id>\reports\<realm id>_latest.txt` | the latest poke's report | overwritten each poke |
| `data\<campaign id>\reports\<realm id>_now.txt` | the report from `agents.view` | overwritten each time |
| `data\<campaign id>\notices.jsonl` | ruler notice | for the watched realm and the player's realm |
| `data\<campaign id>\orders.jsonl` | hand-order event | issued, arrived, reissued, ended |
| `data\<campaign id>\record\decisions.jsonl` | decision of the player's | with what the player could see |
| `data\<campaign id>\record\events.jsonl` | event around the player | entered or left a settlement, attacked, realm votes, vassal orders ended |
| `data\<campaign id>\record\clicks.jsonl` | change of the player's course | raw, before corrections are merged |
| `data\<campaign id>\record\pokes_you.jsonl` | poke the player would have had | same rules as the agents' pokes, using the game's own view of the player |
| `data\<campaign id>\handbacks.jsonl` | save made while lords held our orders | `save`, `saved`, `lords` (hero ids handed back), `clean` (all checked free before the game wrote); every load checks these lords |
| `data\<campaign id>\ids_shown.txt` | id shown to the agent | every id in brackets the agent has been shown in this campaign; an order naming any other id raises an alarm |

The campaign id is the game's own id for the campaign, so separate campaigns never mix.

## Poke

```json
{"type":"poke","faction":"mistymountainorcs","faction_name":"Misty Mountain Orcs","hours":1001.0,"date":"...",
 "triggers":[{"hours":1001.0,"kind":"enemy_in_view","text":"Enemy army in view: ...","party":"..."}],
 "view":{...},"report":"REPORT FOR MISTY MOUNTAIN ORCS\n..."}
```

A realm is poked at most once per in-game hour. Everything that happened in that hour is merged into one poke.

Poke numbers carry on across loads: the first poke of a session takes the number after the highest one in the channel's `pokes.log` and in the campaign's poke file, so a number never means two different pokes. A trigger that came from a notice carries `notice`, the notice's own `id`.

Trigger kinds:

| Group | Kinds |
|---|---|
| Seen | `first_look`, `enemy_in_view` (a lord or army, first sighting or after 12 hours out of sight), `quiet_day` |
| Notices | `war`, `peace`, `alliance`, `alliance_ended`, `call_to_war`, `call_to_war_ended`, `besieged`, `siege_started`, `raided`, `looted`, `holding_changed`, `army_dispersed`, `party_destroyed`, `battle`, `captured`, `proposal`, `decided`, `peace_offer` |

## View

| Field | Meaning |
|---|---|
| `faction`, `faction_name`, `ruler`, `hours`, `date`, `night` | who and when |
| `at_war`, `allies` | realm names |
| `forces` | the realm's own lords' parties and armies, as *view parties* (members of an army are folded into its leader) |
| `holdings` | the realm's own settlements, as *view holdings*, each with the nearest enemy in view |
| `seen` | lords and enemies in view, nearest to the realm's holdings first, at most 40 (`seen_not_listed` counts the rest) |
| `others_in_view` | caravans, villagers and other small neutral parties in view (counted, not listed) |
| `last_seen` | parties seen earlier and now out of sight, up to 72 hours back |
| `frontier` | enemy castles and towns within 60 map units of the realm's holdings; the garrison is known only when in sight (`garrison` is -1 otherwise) |
| `news` | notices from the last two days, as text |

A **view party** has `id`, `name`, `faction`, `faction_name`, `relation` (own, enemy, ally, neutral), `kind` (lord, bandit, ...), `men`, `army_men` and `army_parties` (when it leads an army), `strength` (the game's own estimate), `x`, `y`, `near` and `near_distance` (the nearest settlement), `nearest_holding` and `holding_distance` (the realm's nearest holding), `doing`, `seen_by`, and `seen_hours`.

A **view holding** has `id`, `name`, `kind` (town, castle, village), `faction`, `faction_name`, `garrison`, `militia`, `x`, `y`, `status` (under siege by ..., being raided, looted), and `threat`, `threat_men`, `threat_distance`.

## Decision (the player's)

```json
{"type":"decision","kind":"go_to","text":"You set out for Fornost (castle, yours).","hours":...,"date":"...","wall":"...",
 "faction":"...","you":{...},"target":{...},"seen_by_you":[...],"realm":{"forces":[...],"holdings":[...]},"recent_pokes":[...]}
```

| Field | Meaning |
|---|---|
| `kind` | `go_to`, `go_to_point`, `chase`, `follow`, `patrol`, `march_to_besiege`, `march_to_raid`, `march_to_defend`, `assault`, `attack`, `besiege`, `raid`, `form_army`, `join_army`, `proposal`, `vassal_order`, `vassal_release` |
| `you` | the player's party: `id`, `name`, `men`, `army_men`, `strength`, `x`, `y` |
| `target` | `kind` (settlement, party, point), `id`, `name`, `faction`, `men`, `x`, `y` |
| `seen_by_you` | everything the game showed the player at that moment, nearest first, up to 60. Each entry has `id`, `name`, `faction`, `kind`, `doing`, `men`, `army_men`, `strength`, `x`, `y`, `distance` and `hostile`. |
| `realm` | the player's realm at the last in-game hour: its lords (`forces`) and its castles and towns (`holdings`) |
| `recent_pokes` | the triggers of the player's own pokes in the last 24 in-game hours |

A movement counts as a decision once it has held for a quarter of an in-game hour. Quick corrections are merged into it; a movement decision also carries `settle_hours` (in-game hours from the first change of course to the decision) and `corrections` (how many quick corrections were merged).

## Event, click, order, notice

| Record | Fields |
|---|---|
| Event | `type`: "event", `kind` (entered, left, attacked, joined_your_army, realm_decided, vassal_order_ended), `text`, `hours`, `date` |
| Click | `type`: "click", `hours`, `wall`, `behavior` (the game's own name), `settlement`, `party`, `x`, `y` |
| Order | `type`: "order", `event` (issued, arrived, reissued, ended), `id` (numbered per session), `source`, `poke`, `hours`, `lord`, `lord_name`, `verb`, `place`, `target`, `reissued`, `text` |
| Notice | `id`, `hours`, `kind` (as the trigger kinds above), `text`, `factions` (the realms told), `party` |

## Agent channel

The channel lives in the folder set by `agent_dir` in `settings.txt`, now `C:\Users\karab\Projects\orc-war-council\channel`. It is used when `agent` is `shadow` or `live`.

| File | Written by | Contents |
|---|---|---|
| `pokes.log` | bridge | one line per poke of the watched realm: `#12 \| Summer 3, 1085, 14:00 \| <first trigger> (and N more)` |
| `poke.txt` | bridge | `POKE #12, <date>`, how to answer, the mode, then the full report. It is written before the poke's line appears in `pokes.log`. |
| `report.txt` | bridge | `SITUATION AT <date>` and the mode, then the report without triggers. Refreshed every in-game hour. |
| `orders\*.json` | agent | one answer per file, in the form shown below. Each file is read once and moved to `orders\done\`. |
| `receipts.log` | bridge | one line per order (APPLIED, CHECKED in shadow mode, or REFUSED, with the reason and the answer time), and later lines for what became of agent orders (arrived, reissued, ended). Also: RECEIVED, at once, for a file read while the game's clock is stopped (its orders are answered when the clock runs again); SET ASIDE for a file written before the game session began (moved unread to `orders\stale\`); REFUSED for every order in a file answering a poke from before the last load; UNREADABLE for a file that is not valid JSON after five tries. |

An answer file:

```json
{"poke": 12, "orders": [{"lord": "lord_MM1_2", "do": "garrison", "target": "castle_MM1", "why": "..."}], "note": "..."}
```

- `lord` and `target` take the ids shown in [brackets] in the report, or names.
- `do` is one of `goto`, `garrison`, `patrol`, `defend`, `engage`, `besiege`, `raid`, `recruit`, `hold` or `free`.
- A bare list of orders, or a single order object, is accepted too.

Every receipt is also kept in the campaign's `orders.jsonl` as a record with `type` "agent_receipt". It has `poke`, `file`, `index`, `lord`, `do`, `target`, `why`, `status`, `reason`, `hours`, `answer_seconds`, `answer_game_hours`, `waited_seconds` (when the order waited at least 2 s for the game's clock) and `mode`.

The orders folder is watched by Windows, so a new answer file is read within about half a second; a slow check every 10 seconds is the safety net. `poke.txt`, `report.txt` and the reports are written beside the old file and swapped in whole, so a reader never sees half a file. Orders the player gives through `agents.vassal` become decision records of kind `vassal_order`, with `lord` and `verb` fields.

## The detailed logs (version 0.3)

One folder per game launch, `logs\sessions\<yyyyMMdd-HHmmss>\`. What each file is for, in plain words, is in [docs/LOGGING.md](../docs/LOGGING.md).

**`trace.jsonl`**: one record per line, `{"t": local time with offset, "h": in-game hours, "k": kind, "w": what, "d": {details}}`. Kinds:

| Kind | `w` and `d` |
|---|---|
| `session` | loaded, modules, watchdog started, campaign started, campaign ended, end |
| `self_check` | "N of M" with every check and its problem; the helper's half |
| `state`, `speed`, `focus` | the game's screen (campaign map, in a menu, battle or scene, loading, ...), its speed (stopped, play, fast), the game window in front or in the background |
| `command` | a console command typed, with the first line of its answer |
| `hour`, `vision`, `snapshot` | each hourly copy: counts, the worker's delay after the hour, the parts' times; the realm's sight; impossible values |
| `sight_check` | our sight rule against the game's for the player's party, with every disagreement's numbers |
| `fog` | a leak found in a report |
| `notice`, `poke` | every notice with its `id`; every poke with its triggers, the notices it carried, its size by section, and its delay |
| `answer` | each answer file (written, read, seconds after its poke, newer pokes already out, duplicate), each receipt, files set aside or unreadable, ids never shown |
| `order` | issued (with the words, and the lord's state before and after), checked (shadow), hour, arrived, reissued (with who turned him away), ended, after release, refused at the door, lord lookup |
| `lord` | a change in what a lord of the watched realm (or a lord we hold) is doing |
| `ai` | the game's AI weighing its options: kept plan (the best other option), changed plan or called an army (the options and their scores, the bar) |
| `witness` | the movement watcher: who changed the plan of a lord holding our order, with the call trail |
| `save`, `load`, `sweep` | each save's hand-back; the switch sweep at load and the check of earlier hand-backs; the daily sweep |
| `recorder`, `decision`, `pac` | the recorder paused or resumed; each decision recorded; changes in Party AI Controls' list |
| `freeze`, `probe`, `blackbox`, `hooks` | the game's thread stopping and moving again; the freeze probe started; a black box written; the game hooks attached |
| `alarm`, `error` | every alarm (`text`, `n`, `serious`) and every error (`type`, `message`, `n`, `handled`) |

**`health.csv`**, one row a minute: `time`, `game_hours`, `state`, `speed`, `clock_running`, `window`, `frames`, `campaign_ticks`, `our_ms_on_game_thread`, `top_hooks_ms`, `slowest_call_ms`, `slowest_call`, `helper_jobs_total`, `helper_queued`, `helper_slowest_job_ms`, `helper_cpu_ms`, `helper_on_efficiency_cores`, `orders_held`, `pokes`, `answers`, `waiting_for_answer_s`, `alarms_total`, `errors_total`, `gc0`, `gc1`, `gc2` (collections that minute), `private_mb`, `threads`, `handles`, `open_files`, `names_cached`, `trace_mb`.

**`lords.log`**: one line per event, `<game date>  <lord> [<id>]: <what>`. The game's AI weighing options: `weighed N options and kept his plan: ... (best other: ... 0.120)`, or `changed plan: A -> B` followed by an `options:` line (up to eight, with scores) and a `best ... against a bar of ...` line.

**`freeze-*.txt`**: the freeze probe's report: a verdict (BUSY in a loop, BUSY moving around, or WAITING, with the share of a core), ten samples of where the main thread was, its call trail (module+offset, Windows functions by name, C# methods by name), the busiest threads, and the C# stacks.

## Performance log columns

| Column | Meaning |
|---|---|
| `time`, `seconds` | the wall clock, and the length of the sample |
| `ticks` | campaign ticks in the sample |
| `fps_engine`, `fps_main`, `fps_render` | the engine's own frame-rate counters |
| `our_ms` | time the bridge spent on the game's thread in that second |
| `bridge` | on or off |
| `speed` | the game's speed setting |
| `game_hours` | in-game hours at the sample |
| `main_thread_on_performance_cores` | the share of ticks the game's thread ran on a performance core |
| `performance_cores_avg` / `_max`, `efficiency_cores_avg` / `_max` | how busy each kind of core was |
| `core_loads` | every core's load, separated by semicolons |
