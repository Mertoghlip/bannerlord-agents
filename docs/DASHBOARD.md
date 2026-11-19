# The dashboard

Version 0.7 (Oct 16). The aim is to make watching and diagnosing the agents much easier: a dashboard with a live map of the game. I want to be able to see exactly what each agent sees, have everything it sees depicted by icons with a legend, switch between agents, and have a log on the side depicting the moves of the agent I am following. When I click on a given icon on the map, I should be able to see where it is moving.

## Starting it

```
powershell -ExecutionPolicy Bypass -File scripts\dashboard.ps1
```

It opens http://localhost:7329/ in the browser. The page is served on this laptop only. The dashboard only reads files the bridge and the war room write, so it can be started and closed at any time, with the game running or not. The agent never sees it: its files are under `%LOCALAPPDATA%\BannerlordAgents`, which the agent's calls cannot read. `Dashboard.exe` runs at below-normal priority with efficiency mode, like the war room. Its log is in `logs\dashboard\`.

`Dashboard.exe selftest` checks the way-finder, the atlas, the map picture and the log on made-up data (27 checks in 0.7.3; `deploy.ps1` runs it). `Dashboard.exe atlas` works out the crossing atlas now and prints it. `Dashboard.exe route x1,y1 x2,y2` describes one way. `Dashboard.exe learncheck` measures our ways against the game's planned ways in the newest load; `Dashboard.exe etacheck [load]` (0.7.3) measures the game's marching rule (GamePace) against the arrivals the frames show, beside the old reckonings (docs/TOOLS.md, "The game's clocks"). Since 0.7.3 a party's "its way" card times the march by the same rule, from the party's speed sum in the frame (`march` in the request).

## What the page shows

- **The map.** The game's own walking map, one pixel per map unit, coloured by ground. The ground no party can cross is a little darker, and the land's relief is shaded from the north-west. North is up. It is copied once per map by the bridge (`MapExport`, [ROUTES.md](ROUTES.md)). Until then, the page draws the holdings on a plain ground.
- **Following.** The realm whose eyes the map shows: the agent's realm (now the Misty Mountain Orcs), and the player's own.
  - **What it sees** is built from the same view the realm's report was written from that hour, not a second copy:
    - its own lords;
    - what its report lists IN VIEW;
    - what it saw earlier, faded at the last place seen, with the age;
    - each enemy army's trail;
    - its holdings with their walls and what they hold against.
  - Two markers show what the report left out:
    - a dashed magenta ring for a party in sight that the list's cap cut, or one remembered but cut from SEEN EARLIER;
    - a thin ring for caravans and villagers in sight that the report only counts.

    The Oct 16 gap (Aragorn's army missing from SEEN EARLIER) would have shown at a glance.
  - **Everything** shows the truth: every party, coloured by its realm, ringed by its side towards the followed realm.
  - The layer "The truth under its picture" lays the true positions over what the realm sees, with a thin line from each faded sighting to where that party really is.
- **Icons and the legend.**
  - Holdings: town, castle, village, coloured by owner; a blue ring marks the followed realm's own; under siege; being raided; looted.
  - Parties: a lord's ring says whose side it is on. An army is drawn bigger by strength, with its men written beside it. Also bands, caravans and villagers.
  - Badges: fighting; holding the agent's order.
  - Ways: the game's own planned way, our prediction, a way you asked for.
  - Crossings: bridges, fords and passes, the critical ones ringed.
  - Lords inside a holding are small marks around it.
- **Clicking an icon** opens its card.
  - A party's card gives its men, strength, what it is doing, where it is, its pace, the order it holds, and its way:
    - **the game's own planned way**, the exact corners the game plans to walk (for the followed realm's own lords, and for everyone in Everything);
    - or **our prediction**: for an enemy, from where its realm saw it to the holding its report says it is heading for, at the pace its trail shows (else a typical pace, said so).
  - The way's length, time, arrival, ground, crossings (with what closing each would cost) and narrowest place are in plain words, the same words the agent's tool gives.
  - **Ask the way from here**, then click a holding or any spot: our way there.
  - A holding's card gives its owner, garrison, militia, walls, defenders, what it holds against, its state and the lords inside.
  - A crossing's card gives its width, how many ways between holdings use it, and what its closing would cost them.
- **The side log.** The followed agent's moves in this load of the campaign, newest first:
  - **Woken:** what each poke said.
  - **Answer** and **Council:** the orders with their reasons, and the plan.
  - **Carried out** and **Refused:** what became of each order, and why.
  - **Ended:** an order's end.
  - **Reported back:** a tool's call-back.
  - **Asked the way:** each question to the tool.

  Names are links: clicking one selects that lord or place on the map. The kinds can be filtered, and "Only the selected" keeps the lines naming what is selected. Those lines are highlighted anyway.
- **Rewind.**
  - The slider runs over every hour of this load; play steps through them, at the speed chosen.
  - The source list picks the live session, an earlier load of the campaign (the newest `keep_loads`, 12, plus every load of a test run), or a whole test run (below).
  - "Live" follows the game again. When rewound, the log stops at that hour.
- **Checks** (top right):
  - whether the walking map and the crossing atlas are ready;
  - the accuracy check, our way-finder against the game's own planned ways ([ROUTES.md](ROUTES.md));
  - the measured speed on each ground.
- **Links.** `#mode=truth&realm=<id>&select=<party, lord or holding id>&hour=<hour>&layers=allways,truth` opens the page on that view, for example `http://localhost:7329/#select=lord_MM1_1`.

## Recordings: whole runs played back (since 0.10, Nov 20)

For demos. A test run (docs/RUNS.md) is played from its first hour to its last, across every load of the game it took, on the same map.

- **Opening one.** The source list's "Test runs, played back whole" lists every run that began and has hours on disk. A link opens one directly:
  `http://localhost:7329/#run=20261117-1614-A&at=0&speed=6&present=1&play=1` (`at`: the hour of the run to start from, counted from its first; `speed`: in-game hours a second; `present` and `play`: 1 to start in presentation mode and playing).
- **The run's line.** Each in-game hour once, from the load whose stretch it is. A load of an earlier save of the run drops what the load before played past that save, since it was never saved; a load of a later save leaves the one before whole. A load whose frames are gone (from before module 0.21.3 kept them) cuts nothing.
- **Playing.** Play and pause (space), an hour back or on (`,` and `.`), and 1 to 48 in-game hours a second. Parties move smoothly between the hours. The timeline marks where each load began (grey), each agent's decisions (in the realm's colour), the major news (red) and the minor news (faint); hovering says what each is, and clicking jumps there. While playing the map draws each hour's light copy. When paused it shows the hour's full frame, so cards, ways and every layer work as for a live hour. Clicking a lord while playing pauses on that hour.
- **Captions.** When the playback passes an agent's answer or strategy review that gave orders, it appears for a few seconds: the realm, its plan, and up to six orders in words with their reasons. The lords it ordered pulse on the map in the realm's colour.
- **Pop-ups.** The game's news along the run for its realms, where it happened:
  - **Major, ringed in red with a label:** battles between realms (not with bands, caravans or villagers), lords taken prisoner or killed, sieges laid and lifted, holdings changing hands, wars and peace.
  - **Minor, a fainter ring:** raids and lootings.
  - **Listed as notes in the corner:** the major news, armies formed, alliances and the agents' letters.
- **Presentation mode** (the Present button or P; Esc leaves). The header, the side log, the legend and the card go. The map fills the screen, with the run and the date in large type, a scoreboard of each realm of the run (its towns and castles, its lords, and the strength of its lords in the field), the captions and the pop-ups. The timeline hides while the mouse rests.
- **The side log** is the followed realm's moves along the whole run, up to the hour shown.
- **Names** that reach the frames double-encoded (UTF-8 read as Latin-1, as "GrishÃ»l") are shown as the game means them ("Grishûl").
- **Keeping the hours.** Since module 0.21.3 the bridge never removes the frames of a load that is part of a test run (about 0.45 MB an hour, some 650 MB for a run of 60 days); `keep_loads` still applies to every other load.
- **The light copies.** Opening a run makes a light copy of each of its hours from the frame, in the background, three at a time (the frames are about 2.7 MB of JSON each, too heavy to play several a second). The copy holds only what the map draws while playing: the parties outside garrisons and militias, with their positions, men, strength, army, fight, holding and errand; the holdings; each realm's sightings, cuts and orders; the sieges and battles. It is about 90 KB an hour (some 130 MB for 60 days), kept in `dashboard\slim\v2\<campaign>\<load>\` under the bridge's folder, and made again only when its layout changes (`Recording.SlimVersion`). Playback can start before they are all made: the chip beside the date says how far it has come.

## How it works

- **The bridge, every in-game hour, on its worker thread** (`LiveMap`, after the hour's views):
  - It writes one frame: `live\<campaign>\<load>\frames\h<hour>.json.gz`. A frame holds every party and holding as the hourly copy has it. For each realm followed (the agent's, and the player's from the game's own sight flags), it holds the view its report was built from, what is in sight, what the report cut or only counts, what is remembered but cut, and the agent's orders in force.
  - It updates `live\current.json`, which says where the newest frame is, and gives the calendar.
  - About 1.5 KB a frame in the smoke test. A real frame is expected around 100 to 200 KB, so a long session costs tens of MB. Older loads are deleted past `keep_loads`.
- **The bridge's hourly copy (the game's thread)** now also copies, for each lord and caravan on the march, the corners still ahead of the way the game planned (`MobileParty.Path` from `PathBegin`, while `_pathMode` is on, at most 48). For every party it copies where it walks to (`MoveTargetPoint`), and for lords their speed with ground and night counted (`_lastCalculatedSpeed`). Plain copies only. The two private fields are found by name once (`Capture.WayFieldsProblem`; self-check "planned ways").
- **Loads apart.** A campaign reloaded from the same save repeats its hours. At each load, `data\<campaign>\loads.jsonl` notes where each record file stood (pokes, orders, notices, the war room's calls and routes). The log reads one load's lines alone.
- **The dashboard program** (`src/BannerlordAgents.Dashboard`):
  - `Server`: the page and the readings, on `HttpListener` at localhost.
  - `Store`: reads the frames, the map, the atlas, the loads.
  - `LogReader`: the side log, each file read on from where it was left.
  - `Picture`: the map picture, made once (`picture-v1.png` beside the map).
  - `Checks`: works out the atlas once per map when it is missing, measures the accuracy and the speeds.
  - `Recording` (0.10): the runs and their kept lines (`runs\<id>\run.json`), the light copies, the run's news and the agents' decisions along it. Readings `/api/runs`, `/api/run`, `/api/runprep`, `/api/runlog` and `/api/slim`.
  - The page: `web/` (no outside libraries; canvas): `app.js` the map, cards and log; `replay.js` (0.10) the timeline, the sources, playing a run, captions, pop-ups and presentation mode.
- **Settings** (`settings.txt` of the bridge): `dashboard = on` (the map copy, the planned ways and the frames) and `keep_loads = 12` (the loads of test runs are always kept, since module 0.21.3).

## What it costs the game

- The hourly copy reads a few more fields per party and copies the planned corners of marching lords and caravans. This is measured in the hourly copy's parts (`parts_ms` in the trace).
- The one-off map copy runs in slices on the campaign map, never in a battle: under a millisecond a frame while the clock runs, 4 ms while it is stopped (`MapExport`).
- Everything else runs on the worker or outside the game.

## Not yet seen in a game

- The map copy's time on the real map, and the frames' real size and writing time.
- Whether our map's north matches the game's: map y is taken to grow to the north. The first copy will show it (Middle-earth's shape).
