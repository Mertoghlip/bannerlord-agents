# Ways, crossings and the "ask the way" tool

Version 0.7 (Oct 16). Mert: "the pathfinding for the game is also an important factor to consider. Sometimes there are only singular crossing points, mountain ranges blocking paths etc. that can lead to interesting strategies like keeping an army close to a critical area perpetually to defend it and stuff. This means clicking on an icon should show how the army would 'pathfind' its way to the place it is trying to go to, and the agent should also know how pathfinding works (though this knowledge should only be exposed to it after it specifically asks for it for a specific army to not crowd the logs) so it can strategize accordingly."

**Test pollution.** The agent gets facts and mechanics only: how marching works, the way, its crossings and what closing each would cost. It never gets advice such as "hold the ford". No doctrine principle on crossings was added; Mert agreed to leave one out at first, to see whether the agent reaches the idea on its own.

## How the game finds its way (1.4.8, read in its code)

- **Planning.** `MobileParty.ComputePath` asks the engine for a path between two faces of the campaign map's walking mesh (`MapScene.GetPathBetweenAIFaces`). The parameters: agent radius 0.3; the ground the party may not walk on, excluded by face group (`PartyNavigationModel.GetInvalidTerrainTypesForNavigationType`); and an extra cost multiplier of 2 that the engine applies to some faces, which the managed code does not say.
- **The path.** `NavigationPath`: up to 128 corners, `Size` of them. The party walks corner by corner (`DoAiPathMode`: `NextTargetPosition = Path[PathBegin]`, then `PathBegin++`) while `_pathMode` is on. Without a path it walks straight at its target (`NextTargetPosition = finalTargetPosition`). It plans again when its target moves or changes (`IsComputePathCacheDirty`).
- **Walkable ground** (`DefaultPartyNavigationModel`): plain, desert, snow, forest, steppe, swamp, dunes, bridge, ford and beach. Everything else is closed to a land party: mountain, lake, water, river, canyon, farmland (`RuralArea`), cliff, sea, and the restriction types. So rivers are crossed only at bridges and fords, which the map labels as their own ground types. TaleWorlds' default model has no naval travel (`HasNavalNavigationCapability` false). The export records the walkable list from the running model, in case a pack changes it.
- **Speed** (`DefaultPartySpeedCalculatingModel.CalculateFinalSpeed`, on the party's base speed):
  - forest −30% (Forest Kin helps footmen);
  - water, river, bridge and ford −30%;
  - desert and dunes −10%;
  - snowfall −10%;
  - night −25%. Night is any hour outside `CampaignTime.SunRise` to `SunSet`, 2 to 22 in the default time model.
  - The base speed counts the whole army together: men on foot and on horse, wounded, herds, cargo.
  - The path is the shortest, not the fastest: speed does not enter it.

## The walking map (`MapExport`, the bridge, once per map)

- **Where it goes:** `%LOCALAPPDATA%\BannerlordAgents\map\<the walking mesh's checksum>\`, named by `GetSceneNavigationMeshCrc`. A changed map gets a new folder.
- **The files:**
  - `ground.bin`: the ground type (`TerrainType`, 0 for none) at the centre of every map unit, row by row from the south-west corner (`GetFaceIndex`).
  - `height.bin`: the land's height every second unit, float32 (`GetTerrainHeightAndNormal`), for shading.
  - `crossing_faces.json`: the centre of every bridge and ford face of the mesh (`GetFaceAtIndex`, `GetNavigationMeshCenterPosition`). A crossing narrower than a cell is stamped walkable from these.
  - `places.json`: every holding and hideout, with its gate (parties march to the gate).
  - `meta.json`, written last: the borders, the cell size, the walkable ground from the live navigation model, the sun's hours, the speed and navigation models' type names, the face counts by ground, and how long the copy took. A map without it is unfinished.
- **How it runs:** in slices on the game's thread, on the campaign map only: 0.8 ms a frame while the clock runs, 4 ms while it is stopped. The game itself asks these questions from its worker threads (`MobileParty`'s `...Parallel` methods), so they are safe beside its party work.
- **Copying anew:** `agents.map` shows the progress; `agents.map again` copies the map again.

## The way-finder (`src/BannerlordAgents.Routes`, outside the game)

- **`Finder`:**
  - A* over the cells, eight ways, never cutting a closed corner, with an octile estimate.
  - The staircase it finds is pulled into straight legs wherever the ground between two turns is open, as the game's path is a string of straight legs.
  - `Find` takes a guess about costs (`Costs`) and cells to close (the atlas's detour test).
  - `FromPoints` describes a given way, such as the game's own plan.
  - Each way is analysed: the ground it crosses; its bridges and fords in order, with how far along; and its narrowest place, the walkable width across, measured every 2 units where under 30.
- **`Marching`:**
  - The hours a way takes at a pace: the base speed, slowed by the ground under each stretch and by night, hour by hour from a start.
  - The plain-words rules ("HOW MARCHING WORKS"), from the measured slowing when there is enough, else the game's own numbers.
- **`Atlas`** (`atlas.json` beside the map, worked out once per map in the background by whichever of the dashboard and the war room finds it missing first, under a lock per map; until then the tool says what closing a crossing costs is not worked out yet):
  1. The ways between each town or castle and its 4 nearest neighbours within 180 units.
  2. Every cell of those ways that is a bridge or ford, or where walkable ground narrows to under 12 units across.
  3. Neighbouring such cells grouped into crossings.
  4. Each crossing closed in turn (the whole bridge or ford, or the pass's cross-section), and up to 6 of the ways through it found again.
  5. A crossing is kept when closing it cuts a way off or adds 10 units or more. It is critical when it cuts a way off, or adds 30 units and 40% or more. It is named by its nearest holding ("the ford 8 east of Tharbad"), with its worst examples.
- **`Explain`:** a way in plain words, shared by the dashboard's cards and the agent's tool.

## The agent's tool (`RouteTool`, the war room)

- **How it reaches the agent.** The tool is a real tool in each fast answer and each council: an in-process MCP server ("sdk" type) that the war room serves over the call's own stream.
  - The call starts with `--input-format stream-json`, `--mcp-config {"mcpServers":{"routes":{"type":"sdk","name":"routes"}}}`, `--strict-mcp-config` and `--allowedTools mcp__routes__ask_the_way`.
  - The war room answers Claude Code's `control_request` / `mcp_message` lines (`Claude.Control`).
  - Claude Code's `--safe-mode` switches off every MCP server Claude Code would start itself, but not one its host serves. Tried Oct 16: with a stand-alone server the tool was missing; served by the host it was there and worked.
  - Without the tool (switched off with `route_tool = off` in `warroom.txt`, no map copied yet, or no view file), the instructions' "## Asking the way" section is removed: never promise a tool the call does not have.
- **What it knows.** Only `live\<campaign>\view_<realm>.json`, written each hour by the bridge:
  - the realm's own lords, with their pace and planned ways;
  - the enemies its report shows, IN VIEW and SEEN EARLIER, where it saw them, with the holding each was heading for;
  - the trails;
  - its holdings.

  Plus the holdings everyone knows, and the walking map. An id the realm never saw is refused ("Your realm knows no lord, army or holding called ..."). An enemy starts from where it was last seen. Its pace is the one its trail shows (map units over hours, errands included), else the usual pace of the realm's own lords, said so.
- **What it answers** (`ask_the_way`, with `from`, `to` and optional `also_from`):
  - the way's length against the straight line;
  - the hours at that army's pace, and the arrival date;
  - the ground;
  - the crossings in order, with what closing each would cost (from the atlas);
  - the narrowest place;
  - for an enemy seen earlier, that this is if it marched on at once;
  - with `also_from`, who would get there first.
  - The first answer in a call adds HOW MARCHING WORKS. `marching.txt` in the channel holds it for the agent to read.
- **The log:** every question and answer goes to `logs\warroom\routes.jsonl` (the poke, the call, from, to, the answer's lines, ms). The war room's `calls.jsonl` gives each call's `ways_asked`. The dashboard's log shows each question.
- **Checks:** `WarRoom selftest` checks it on the made-up map: the protocol, a lord over the bridge, an enemy with its trail's pace and who first, an unseen party refused, the log, the instructions with and without the tool, `marching.txt`. `WarRoom toolcheck` makes one real, short call with a small model through the whole path. Oct 16: passed, 1 question, 2.8 s.

## The accuracy check (the dashboard)

- **Comparing ways.** For up to 5 marching lords in each new frame, our way from the lord to the end of the game's planned way, against the game's own. Two numbers: the length's gap in percent, and how far apart they run (the mean distance, in map units, from points along ours to the game's).
- **Three guesses** about the engine's unknown extra cost: everything equal; forest counted twice; fords and bridges counted twice. The Checks panel shows each guess's median gap. Each sample is in `logs\dashboard\accuracy.jsonl`.
- **The target** is within 5%. If one of the dearer guesses matches better, `Costs.Equal` should change.
- **The speeds.** Each marching lord's speed now against its pace on open ground, by the ground under it, by day, plus open ground at night. They are normalised by open ground by day and written to `speed_factors.json` beside the map. Grounds seen 30 times or more replace the game's own numbers in the times and the rules.

## Not yet seen in a game

- The map copy on the real map, and how many crossings the atlas finds there.
- The accuracy check's numbers, and which guess wins.
- The agent asking the way.
