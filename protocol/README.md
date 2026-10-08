# Record formats (install 1)

Everything the bridge writes is JSON, one object per line (JSON Lines). Names are snake_case, and empty fields are left out. `hours` is in-game hours since the calendar began. `date` is the game's own date text, and `wall` is the real clock.

The formats for the war room (agents' orders, tool definitions) arrive in weeks 2 and 3.

## Where the files are

`%LOCALAPPDATA%\BannerlordAgents\`

| Path | One line or file per | Notes |
|---|---|---|
| `logs\bridge.log` | human-readable log | carries across launches; past 8 MB the old one becomes `bridge_prev.log` |
| `settings.txt` | settings | `watch`, `bridge`, `record`, `perf`, `notify`; the console commands change these |
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

The campaign id is the game's own id for the campaign, so separate campaigns never mix.

## Poke

```json
{"type":"poke","faction":"mistymountainorcs","faction_name":"Misty Mountain Orcs","hours":1001.0,"date":"...",
 "triggers":[{"hours":1001.0,"kind":"enemy_in_view","text":"Enemy army in view: ...","party":"..."}],
 "view":{...},"report":"REPORT FOR MISTY MOUNTAIN ORCS\n..."}
```

A realm is poked at most once per in-game hour. Everything that happened in that hour is merged into one poke.

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

A movement counts as a decision once it has held for a quarter of an in-game hour. Quick corrections are merged into it.

## Event, click, order, notice

| Record | Fields |
|---|---|
| Event | `type`: "event", `kind` (entered, left, attacked, joined_your_army, realm_decided, vassal_order_ended), `text`, `hours`, `date` |
| Click | `type`: "click", `hours`, `wall`, `behavior` (the game's own name), `settlement`, `party`, `x`, `y` |
| Order | `type`: "order", `event` (issued, arrived, reissued, ended), `hours`, `lord`, `lord_name`, `verb`, `place`, `target`, `reissued`, `text` |
| Notice | `hours`, `kind` (as the trigger kinds above), `text`, `factions` (the realms told), `party` |

## Agent channel

The channel lives in the folder set by `agent_dir` in `settings.txt`, now `C:\Users\karab\Projects\orc-war-council\channel`. It is used when `agent` is `shadow` or `live`.

| File | Written by | Contents |
|---|---|---|
| `pokes.log` | bridge | one line per poke of the watched realm: `#12 \| Summer 3, 1085, 14:00 \| <first trigger> (and N more)` |
| `poke.txt` | bridge | `POKE #12, <date>`, how to answer, the mode, then the full report. It is written before the poke's line appears in `pokes.log`. |
| `report.txt` | bridge | `SITUATION AT <date>` and the mode, then the report without triggers. Refreshed every in-game hour. |
| `orders\*.json` | agent | one answer per file, in the form shown below. Each file is read once and moved to `orders\done\`. |
| `receipts.log` | bridge | one line per order (APPLIED, CHECKED in shadow mode, or REFUSED, with the reason and the answer time), and later lines for what became of agent orders (arrived, reissued, ended) |

An answer file:

```json
{"poke": 12, "orders": [{"lord": "lord_MM1_2", "do": "garrison", "target": "castle_MM1", "why": "..."}], "note": "..."}
```

- `lord` and `target` take the ids shown in [brackets] in the report, or names.
- `do` is one of `goto`, `garrison`, `patrol`, `defend`, `engage`, `besiege`, `raid`, `recruit`, `hold` or `free`.
- A bare list of orders, or a single order object, is accepted too.

Every receipt is also kept in the campaign's `orders.jsonl` as a record with `type` "agent_receipt". It has `poke`, `file`, `index`, `lord`, `do`, `target`, `why`, `status`, `reason`, `hours`, `answer_seconds`, `answer_game_hours` and `mode`. Orders the player gives through `agents.vassal` become decision records of kind `vassal_order`, with `lord` and `verb` fields.

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
