# Record formats (install 1, with the logs of version 0.3, the tools of 0.4, the memory of 0.5, the council of 0.6 and the dashboard of 0.7)

Everything the bridge writes is JSON, one object per line (JSON Lines). Names are snake_case, and empty fields are left out. `hours` is in-game hours since the calendar began. `date` is the game's own date text, and `wall` is the real clock.

The agents' answers are under [Agent channel](#agent-channel) below. Tool definitions are in [docs/TOOLS.md](../docs/TOOLS.md), and the war room's own logs in [docs/WARROOM.md](../docs/WARROOM.md).

## Where the files are

`%LOCALAPPDATA%\BannerlordAgents\`

| Path | One line or file per | Notes |
|---|---|---|
| `logs\bridge.log` | human-readable log | carries across launches; past 8 MB the old one becomes `bridge_prev.log`; alarms start with `!!` |
| `logs\sessions\<date-time>\` | game launch | the detailed logs (below, and [docs/LOGGING.md](../docs/LOGGING.md)); the newest 30 launches are kept, 2 GB at most in all |
| `logs\warroom\` | war room call | `calls.jsonl` (one line per call: the poke, model, effort, thinking cap, seconds, tokens, orders, retries and denied reads, and `ways_asked`, the questions to the "ask the way" tool), `routes.jsonl` (0.7, one line per question to that tool: `at`, `campaign`, `realm`, `poke`, `call` (answer or council), `hours`, `from` and `from_id`, `to` and `to_id`, `also`, `lines` (the answer) or `error`, `ms`; [docs/ROUTES.md](../docs/ROUTES.md)), `calls\<date>\<time>-poke-N\` (the prompt, Claude Code's event stream and the answer), `warroom-<date>.log`, `system-<hash>.md` ([docs/WARROOM.md](../docs/WARROOM.md)) |
| `settings.txt` | settings | `watch`, `bridge`, `record`, `perf`, `notify`, `agent`, `agent_lords`, `agent_dir`, `sighting_distance` (30: enemy lords further than this from all the realm's holdings poke only when heading for one or chasing its parties), `detail` (full or everyday), `witness`, `ai_reasons`, `screen_alarms`, `answer_alarm_minutes`, `freeze_probe`, `dashboard` (0.7: the walking map, the planned ways and the hourly frames), `keep_loads` (12: loads of a campaign whose frames are kept); the console commands change these |
| `perf\perf_<yyyyMMdd>.csv` | second of campaign-map play | see the columns below |
| `data\<campaign id>\pokes_<realm id>.jsonl` | poke of the watched realm | carries the full view and the report text |
| `data\<campaign id>\reports\<realm id>_latest.txt` | the latest poke's report | overwritten each poke |
| `data\<campaign id>\reports\<realm id>_now.txt` | the report from `agents.view` | overwritten each time |
| `data\<campaign id>\notices.jsonl` | ruler notice | for the watched realm and the player's realm; `quiet` says why one was kept to the logs instead of poking (see below); `record_only`, one that goes only into the chronicle |
| `data\<campaign id>\chronicle_<realm id>.jsonl` | line of the realm's chronicle (0.5) | `hours`, `date`, `kind`, `text`, `place`, `party`, `from` ("game" for a line from the game's own history log). Cut back to the save's date when an earlier save is loaded ([docs/MEMORY.md](../docs/MEMORY.md)). |
| `data\<campaign id>\orders.jsonl` | order event | issued, arrived, reissued, ended; for hand, agent and vassal orders, tools included |
| `data\<campaign id>\record\decisions.jsonl` | decision of the player's | with what the player could see |
| `data\<campaign id>\record\events.jsonl` | event around the player | entered or left a settlement, attacked, realm votes, vassal orders ended |
| `data\<campaign id>\record\clicks.jsonl` | change of the player's course | raw, before corrections are merged |
| `data\<campaign id>\record\pokes_you.jsonl` | poke the player would have had | same rules as the agents' pokes, using the game's own view of the player |
| `data\<campaign id>\handbacks.jsonl` | save made while lords held our orders | `save`, `saved`, `lords` (hero ids handed back), `clean` (all checked free before the game wrote); every load checks these lords |
| `data\<campaign id>\ids_shown.txt` | id shown to the agent | every id in brackets the agent has been shown in this campaign; an order naming any other id raises an alarm |
| `data\<campaign id>\recruitment.jsonl` | day of the watched realm (0.6) | `type` "recruitment", `hours`, `date`, `realm`, `summary` (lords, average fill, below the party-gold bar, below the clan-gold bar, ranks full, starving, no wage room, recruiters, recruited, volunteers waiting, within the ruler's reach, places with volunteers, places), `hours_counted`, `hours_where_recruiting` (lord id: hours spent where it could recruit), `lords` (each: `lord`, `name`, `clan`, `leads_clan`, `men`, `limit`, `gold`, `gold_bar`, `clan_gold`, `clan_bar`, `wage`, `wage_limit`, `wage_room`, `starving`, `in_army`, `held`, `inside`, `may_recruit`, `recruited`, `recruited_at`), `places` (each town and village: `id`, `name`, `kind`, `culture`, `state`, `waiting`, `ruler_could_take`). Logs only ([docs/LOGGING.md](../docs/LOGGING.md#recruitment-since-06-oct-9-logs-only-never-shown-to-the-agent)). |

| `data\<campaign id>\loads.jsonl` | load of the campaign (0.7) | `load_id`, `loaded_at` (the save's in-game hour), `realm`, `wall`, and `offsets`: the byte length of `pokes_<realm>.jsonl`, `orders.jsonl`, `notices.jsonl` and the war room's `calls.jsonl` and `routes.jsonl` when the load began, so one load's lines can be read alone (the dashboard's log) |
| `live\current.json` | the newest hour (0.7) | `campaign`, `load_id` (the folder of its frames), `hours`, `date`, `dir`, `frame`, `map` (the walking map's checksum), `realms` (the realms in the frames), `agent`, `agent_on`, `written`, and `calendar` (`hours_in_day`, `days_in_season`, `seasons_in_year`, `season_names`) |
| `live\<campaign id>\<load id>\frames\h<hour>.json.gz` | in-game hour (0.7) | the dashboard's frame, gzipped; see [Frame](#frame-07) below. Only the newest `keep_loads` loads keep theirs |
| `live\<campaign id>\view_<realm id>.json` | the agent's realm, each hour (0.7) | its view alone, for the "ask the way" tool: `hours`, `map`, `own` (its lords: `id`, `lord`, `name`, `x`, `y`, `men`, `army_men`, `strength`, `pace` (base speed), `final_speed`, `doing`, `in_settlement`, `route`, `tx`, `ty`), and the view's own `seen`, `last_seen`, `trails` and `holdings`. Never anything the realm did not see |
| `map\<checksum>\` | walking map (0.7) | `meta.json`, `ground.bin`, `height.bin`, `crossing_faces.json`, `places.json` from the bridge; `atlas.json`, `speed_factors.json`, `learned.bin` (cells opened by the game's own planned ways: int32 cell indices) and `picture-v1.png` added beside them by the dashboard or the war room ([docs/ROUTES.md](../docs/ROUTES.md)) |
| `logs\dashboard\` | the dashboard (0.7) | `dashboard-<date>.log`, and `accuracy.jsonl`: one line per lord checked, the game's planned length (`game`) and, for each guess (`shortest`, `forest counted twice`, `fords and bridges counted twice`), our `length`, `gap_pct`, `apart` (mean map units between the two ways) and `ms` |

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
| Seen | `first_look`, `enemy_in_view` (an army, or a lord close to the realm: within `sighting_distance` of one of its holdings, heading for, besieging or raiding one, or chasing one of its parties; on first coming close, or again after 12 hours without), `quiet_day` |
| Notices | `war`, `peace`, `alliance`, `alliance_ended`, `call_to_war`, `call_to_war_ended`, `besieged`, `siege_started`, `siege_ended` (lifted; 0.5), `raided`, `looted`, `holding_changed`, `army_dispersed`, `party_destroyed`, `battle`, `captured`, `lord_killed` (0.5), `proposal`, `decided`, `peace_offer`, `tool`, `order_arrived`, `order_ended` |

Since 0.3.1 (after the first session's 125 pokes, [docs/reports/2026-10-08-pokes.md](../docs/reports/2026-10-08-pokes.md)), some notices are **kept quiet**: written to `notices.jsonl` and the trace with `quiet` set, but never a trigger, never in the report's news, and (since 0.5) never in the chronicle.

| `quiet` | What |
|---|---|
| `bandits`, `a caravan`, `villagers`, `no lord on one side` | a battle away from any town, castle or village without lords on both sides. A battle still pokes when it is an assault, a sally or a raid, or has lords on both sides. |
| `raid already announced` | the same raider on the same village within 24 hours (the game says "being raided" twice per raid) |
| `the capture says it` | a lord's party wiped out as its lord was taken prisoner; the `captured` notice says it |

Enemy lords further off are still listed in the report; the trace logs each one the old rule would have poked for as a `sighting` event, `far off`.

## View

| Field | Meaning |
|---|---|
| `faction`, `faction_name`, `ruler`, `hours`, `date`, `night` | who and when |
| `at_war`, `allies` | realm names |
| `forces` | the realm's own lords' parties and armies, as *view parties* (members of an army are folded into its leader) |
| `captives` | the watched realm's lords held prisoner (0.6): `hero`, `name`, `holding` with `holding_name`, `holding_kind`, `holding_faction` (where they are held), or `party_lord`, `party_name`, `party_faction` (the party holding them, when not in a holding), and `since` (in-game hour) |
| `sum` | the watched realm in a few numbers (0.6): `lords`, `men` and `strength` (an army counted with its leader), `armies`, `army_lords` (parties in them), `ordered` (holding the agent's orders), `forts`, `garrison_men`, `militia`, `besieged`, `villages`, `looted`, `raided`, `captives` |
| `holdings` | the realm's own settlements, as *view holdings*, each with the nearest enemy in view |
| `seen` | lords and enemies in view: enemy armies, enemy lords, bandits, then allies and neutrals, each nearest to the realm's holdings first; at most 40, but every enemy army (0.6.3; `seen_not_listed` counts the rest) |
| `others_in_view` | caravans, villagers and other small neutral parties in view (counted, not listed) |
| `last_seen` | parties seen earlier and now out of sight, up to 72 hours back; at most 25, cut in the same order as `seen` (bandits and neutrals first), never an enemy army (0.7) |
| `trails` | each enemy army's trail (0.6.4), as the realm saw it: `id`, `lord`, `name`, `faction_name`, `army_men`, `army_parties`, `strength`, `in_view`, `last_seen_hours`, and `points` oldest first, each with `hours`, `x`, `y`, `near`, `near_distance`, `nearest_holding`, `holding_distance`, `doing`, and `after_gap` (hours out of sight just before it). A point is added when the army's course changes, after 10 map units, every 6 hours, or when it comes back into sight; points older than 48 hours go, 12 at most; only trails of 2 points or more are listed |
| `frontier` | enemy castles and towns within 60 map units of the realm's holdings; the garrison is known only when in sight (`garrison` is -1 otherwise) |
| `news` | notices from the last two days, as text |

A **view party** has `id`, `name`, `faction`, `faction_name`, `relation` (own, enemy, ally, neutral), `kind` (lord, bandit, ...), `men`, `army_men` and `army_parties` (when it leads an army), `strength` (the game's own estimate), `x`, `y`, `near` and `near_distance` (the nearest settlement), `nearest_holding` and `holding_distance` (the realm's nearest holding), `doing`, `seen_by`, `seen_hours`, `toward` (0.7: the holding its `doing` names, when it names one), and (0.7.1) `near_road` and `holding_road` (the two distances by road, as the game reckons a march; NaN when not known) and `entrance` (the holding whose gate the game counts its road distances from). The nearest holding is the nearest by road where known. A trail point has `near_road` and `holding_road` too, and a view holding `threat_road`.

## Frame (0.7)

One in-game hour for the dashboard ([docs/DASHBOARD.md](../docs/DASHBOARD.md)): written by the bridge's worker (`LiveMap`), gzipped JSON, snake_case. Empty, zero and false fields are left out, so a reader must take a missing field as 0 or false.

| Field | Meaning |
|---|---|
| `v`, `campaign`, `load_id`, `hours`, `date`, `night`, `player`, `player_party`, `watched`, `map` | which frame, and the walking map it stands on |
| `factions` | every faction seen: `id`, `name`, `color` (#rrggbb), `kingdom`, `at_war` and `allies` (kingdom ids), `ruler` |
| `settlements` | every holding as the hourly copy has it (the settlement row: `garrison`, `militia`, `besieged_by`, `besieger`, `village_state`, and the watched realm's and allies' `garrison_strength`, `militia_strength`, `wall_level`) |
| `parties` | every party as the hourly copy has it (the party row), with (0.7) `tx`, `ty` (where it walks to), `final_speed` (lords: speed with ground and night), `route` (lords and caravans on the march: the corners still ahead of the game's planned way, x, y pairs, at most 48), `straight` (walking straight there, no planned way), and (0.7.1) `entrance` (the holding whose gate the game counts its road distances from); a settlement row has its gate, `gx`, `gy` |
| `realms` | by realm id, each followed realm (the agent's, and the player's from the game's own sight flags): `name`, `agent`, `player`, `view` (the very view its report was written from this hour), `in_sight` (party ids), `cut` (enemies and lords in sight that the report's list left out), `counted_only` (small parties in sight the report only counts), `remembered` (view parties remembered but left out of SEEN EARLIER), `orders` (the agent's realm: party id to its order in force, as the report words it) |

A **view holding** has `id`, `name`, `kind` (town, castle, village), `faction`, `faction_name`, `garrison`, `militia`, `x`, `y`, `status` (under siege by ..., being raided, looted), and `threat`, `threat_men`, `threat_distance`. The realm's own castles and towns also have (0.6.3) `wall_level`, `defence` (garrison, militia and the lords inside, the realm's and its allies'), `lords_inside` (that part of it) and `holds_up_to` (the besiegers' strength it holds against: defence times what the walls are worth, `World.WallFactor`).

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
| Order | `type`: "order", `event` (issued, arrived, reissued, ended), `id` (numbered per session), `source`, `poke`, `hours`, `lord`, `lord_name`, `verb` (the current move), `tool` (for the agent's and the console's orders), `place`, `target`, `reissued`, `text` |
| Notice | `id`, `hours`, `kind` (as the trigger kinds above, plus `released` and `army_formed`, which only go into the chronicle), `text`, `factions` (the realms told), `party`, `place` (the holding it is about), `quiet`, `record_only` |

## Agent channel

The channel lives in the folder set by `agent_dir` in `settings.txt`, now `C:\Users\karab\Projects\orc-war-council\warroom` (the war room's; until Oct 8 the agent chat's `...\channel`). It is used when `agent` is `shadow` or `live`.

| File | Written by | Contents |
|---|---|---|
| `pokes.log` | bridge | one line per poke of the watched realm: `#12 \| Summer 3, 1085, 14:00 \| <first trigger> (and N more)` |
| `poke.txt` | bridge | `POKE #12, <date>`, how to answer, the mode, then the report with the poke's sections (below). It is written before the poke's line appears in `pokes.log`. |
| `report.txt` | bridge | `SITUATION AT <date>` and the mode, then the report without triggers, with the orders in force. Refreshed every in-game hour. |
| `council.txt` | bridge | the briefing for a council of war (0.6), refreshed every in-game hour: `COUNCIL BRIEFING FOR <realm>, <date>`, then YOUR PURSE (the ruler's clan's gold and influence, every other clan's influence), EVERY LORD (men of the size limit, army, strength, food, speed, what it is doing, the order it holds or "free", its clan and the clan's influence), MUSTER AT EACH OF YOUR CASTLES AND TOWNS (running totals within one, two and three days) and YOUR CAPTURED LORDS ([docs/TOOLS.md](../docs/TOOLS.md)). The war room reads it with `report.txt` for a council. |
| `tools.txt` | bridge | the order library the agent reads: every move, tool and army order with what it takes, its settings and ranges, and when it calls back. Written at every campaign load (and by `scripts\setup-agent.ps1`). |
| `lookups\<id>.txt` | bridge | one file per holding of the realm (with the full muster for a castle or town, since 0.6), lord of the realm, captured lord of the realm (0.6) and enemy it knows of, and `captured.txt` (0.6); README.txt says what is in each. Rewritten before each poke. |
| `chronicle.txt`, `chronicle.jsonl` | bridge | the realm's chronicle (0.5): to read, one dated line each, oldest first; and for the war room, `hours`, `date`, `kind`, `text` per line. Rewritten whole at each load, then a line added as each comes ([docs/MEMORY.md](../docs/MEMORY.md)). |
| `poke.json` | bridge | the latest poke for the war room (0.5), written before `poke.txt`: `number`, `hours`, `date`, `campaign`, `realm`, `realm_name`, `load_id` (new at every load), `loaded_at` and `loaded_at_date` (the save's in-game hour), `chronicle_lines` |
| `orders\*.json` | agent (war room) | one answer per file, in the form shown below; a council's answer is `poke-N-council-<time>.json` with `"kind": "council"` (0.6). Each file is read once and moved to `orders\done\`. |
| `receipts.log` | bridge | one line per order (APPLIED, CHECKED in shadow mode, or REFUSED, with the reason and the answer time), and later lines for what became of agent orders (ended, and `tool:` lines for every call-back). Also: RECEIVED, at once, for a file read while the game's clock is stopped (its orders are answered when the clock runs again); SET ASIDE for a file written before the game session began (moved unread to `orders\stale\`); REFUSED for every order in a file answering a poke from before the last load; UNREADABLE for a file that is not valid JSON after five tries. |

The poke's sections, after WHY YOU ARE ASKED NOW (since 0.4):
- **WHAT CHANGED SINCE YOUR LAST ANSWER (to poke #K):** the reasons of every poke after #K, then "What became of your orders since then" (the receipts written since poke #K was).
- **YOUR ORDERS IN FORCE:** one line per held lord: the order with its ids, what it is doing now, since when, settings that differ from the defaults, and its next order (then).
- **READY-MADE FACTS:** for up to four things the poke is about, the realm's lords soonest to it (strength, rough hours, free or ordered) and enemies near it; for a besieged holding of the realm, the strength inside and when it sallies out (0.6); the muster at the first holding (0.6); then the count of free lords.
- **YOUR REALM IN SUM** (0.6), before YOUR LORDS AND ARMIES: the field forces, the castles and towns, the villages and the lords held prisoner, in three lines; and **YOUR CAPTURED LORDS** (0.6), after YOUR HOLDINGS, grouped by where they are held.
- In YOUR LORDS AND ARMIES, a held lord's line ends with "(your order: <tool>)".
- **RECENT HISTORY, LAST TWO DAYS (from your chronicle)** (since 0.5): the report's last section, the chronicle's last 25 lines within 48 hours, in place of NEWS. The war room adds the rest of the realm's memory before the poke ([docs/MEMORY.md](../docs/MEMORY.md)).

An answer file:

```json
{"poke": 12, "orders": [
  {"lord": "lord_MM1_2", "do": "defend", "at": "castle_MM1", "radius": 25, "why": "..."},
  {"lord": "lord_MM1_5", "do": "gather", "at": "castle_MM1", "lords": ["lord_MM1_6", "lord_MM1_7"], "then": {"do": "relieve", "at": "town_MM2"}},
  {"lord": "lord_MM1_9", "do": "adjust", "settings": {"caution": 0}}],
 "note": "..."}
```

- `lord`, `at` (a holding), `target` (a lord) and `lords` (a list) take the ids shown in [brackets], or names. `target` and `at` stand in for each other where a tool takes only one.
- `do` is any order of the library in `tools.txt` (docs/TOOLS.md), or `free`, `release`, `add`, `disband`, `adjust`.
- A council's answer may also carry `"council_in"`: in-game hours until the next council (4 to 72); the war room reads it, the bridge ignores it.
- Settings are numbers: in `"settings": {...}`, or as any other number at the order's top level.
- `then` is an order without a lord (it is the same lord's), checked against the game when it starts.
- `"orders": []` is a valid answer: nothing changes.
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
| `notice`, `poke` | every notice with its `id` (and `quiet` when kept from the agent); every poke with its triggers, the notices it carried, its size by section, and its delay |
| `sighting` | an enemy lord newly seen far off, not poked for (`far off`: distance, nearest holding, what the lord was doing); a lord seen with a new party |
| `answer` | each answer file (written, read, seconds after its poke, newer pokes already out, duplicate), each receipt, files set aside or unreadable, ids never shown |
| `order` | issued (with the words, and the lord's state before and after), checked (shadow), hour, arrived, moved (a tool's new move, and whether the game took it), reissued (with who turned the lord away), replaced, ended, after release, refused at the door, lord lookup |
| `tool` | library written (with any problems the checker found), started (tool, lord, places, lords, settings, next order), step (from, to, the rule that fired, the move), no target, sheltered and resumed (the safety rule), adjusted, then, replaced, ended (why, steps, hours), cleared |
| `army` | formed (leader, place, lords called, lords sent to join (0.6), influence cost now and the far lords' cost now, the clan's influence before, army type); joined (0.6: the lord, the leader, the cost, the influence before) |
| `recruitment` | day (0.6): the summary, how many lords sat where they could recruit, the hours counted |
| `chronicle` | game history read (entries scanned, lines about the realm, unread, ms; at load), loaded (lines, cut, seeded from the game, the cutoff hour) |
| `lord` | a change in what a lord of the watched realm (or a lord we hold) is doing |
| `ai` | the game's AI weighing its options: kept plan (the best other option), changed plan or called an army (the options and their scores, the bar) |
| `witness` | the movement watcher: who changed the plan of a lord holding our order, with the call trail |
| `save`, `load`, `sweep` | each save's hand-back; the switch sweep at load and the check of earlier hand-backs; the daily sweep |
| `recorder`, `decision`, `pac` | the recorder paused or resumed; each decision recorded; changes in Party AI Controls' list |
| `freeze`, `probe`, `blackbox`, `hooks` | the game's thread stopping and moving again; the freeze probe started; a black box written; the game hooks attached |
| `alarm`, `error` | every alarm (`text`, `n`, `serious`) and every error (`type`, `message`, `n`, `handled`) |

**`health.csv`**, one row a minute: `time`, `game_hours`, `state`, `speed`, `clock_running`, `window`, `frames`, `campaign_ticks`, `our_ms_on_game_thread`, `top_hooks_ms`, `slowest_call_ms`, `slowest_call`, `helper_jobs_total`, `helper_queued`, `helper_slowest_job_ms`, `helper_cpu_ms`, `helper_on_efficiency_cores`, `orders_held`, `pokes`, `answers`, `waiting_for_answer_s`, `alarms_total`, `errors_total`, `gc0`, `gc1`, `gc2` (collections that minute), `private_mb`, `threads`, `handles`, `open_files`, `names_cached`, `trace_mb`.

**`lords.log`**: one line per event, `<game date>  <lord> [<id>]: <what>`. The game's AI weighing options: `weighed N options and kept the plan: ... (best other: ... 0.120)`, or `changed plan: A -> B` followed by an `options:` line (up to eight, with scores) and a `best ... against a bar of ...` line.

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
