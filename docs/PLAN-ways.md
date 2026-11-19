# One march clock and the game's own ways (Nov 18)

The job: every estimate of how fast a party moves and when it arrives (the rules inside the game, the reports, the war room's tools) goes through one march clock and one way-finder, and the way-finder becomes an exact copy of the game's own. No test runs until the whole job is done.

Why: in run 20261117-2146-A the storm guard counted my army at speed 2.8 while it marched about a map unit an hour through forest, Dol Guldur switched the guard off, and its storm of Imnagath ran into my arrival (module 0.20.5 fixed the guard's pace and added the storm's clock). An audit then found that most rules inside the game use a party's exact pace but on open ground over a straight line, that nothing adds a margin for an enemy out of sight, that the rules forget an enemy 6 hours after losing sight, and that our way-finder is close to the game's but not exact (on 69,495 comparisons with the ways the game planned: half within 1%, 9 in 10 within 5%, 1 in 100 off by 14% or more).

## Decisions

- **In view:** a party's exact pace from what is seen (the game's own speed sum), stretch by stretch for the ground and the hour. A party in a holding, a siege or a battle, whose stored speed the game stops updating, gets a fresh one from the game's own formula (`PartySpeedModel.CalculateBaseSpeed`, which may be called for any active party).
- **Out of sight up to 24 hours:** its last-seen pace +10%, stretch by stretch, from where it was seen, the clock running since the sighting.
- **Out of sight 24 hours to 3 days:** on each stretch the faster of its last-seen pace +10% and speed 2.8 (as measured, on any ground: it is the average of lord parties as they actually marched); the agent is told both.
- **Out of sight over 3 days:** not counted by the rules; still listed for the agent with where, when and at what speed.
- **Never seen:** not counted, as before.
- **An army out of sight:** the faster of the army's pace and its leader's own (the army's base scaled by its men: ((200 + army's men) / (200 + his men))^0.4; for my army at Imnagath 1.44 gives 2.02, the measured 2.02).
- **A party in a battle:** its march starts at the battle's expected end.
- **News without sight** (a battle or siege named where a lord is): a sighting there and then, keeping his last pace; taken prisoner removes him.
- **The agent's judgement:** an order setting to count a given force at its own last-seen pace instead of the minimum (default no).
- **Ways:** our own way-finder, made an exact copy of the game's (`MobileParty.ComputePath` → `GetPathBetweenAIFaces`: agent radius 0.3, the party's closed ground by face group, an extra cost multiplier of 2 on some faces, land and sea switch costs). It searches as far as it is asked: the walking mesh has 19,396 faces, so a search over the whole map takes milliseconds, on a side thread. A party already marching to the very place asked about keeps the way the game planned for it.
- **Until the copy passes its proof** the rules use a straight line with the ground read along it, marked "rough"; today's way-finder stays in the war room's tools, in reserve.
- **Left as they are:** the copy of the game's attack-or-flee choice (it mirrors the game's own numbers); the borders' "near your lands", a fixed measure at speed 2.8.

## Steps

Phase 1, the march clock:
- [x] The game's speed formula can be called for a party in a holding or a battle (1.4.8 source).
- [x] The clock (Common/MarchClock.cs): pace rules, the margin and the minimum by stretch, the start delay, the basis in words; behind one door for ways (World.MarchTo, WayFor).
- [x] Fresh speeds for parties in view not marching (Capture.Pace, SpeedFresh; the unexplained part carried on, Forecast.LastOther); sightings kept 3 days (the memory already kept 72 hours; the rules now look that far) with the army's and the leader's paces and the battle's end (Runner.HoursLeftAlone).
- [ ] Rules switched one at a time, each with tests: [x] the storm guard and its clock; [x] the siege relief bars and warnings; [x] joining times and relief rides (HoursTo, Joiners); [x] could_reach; [x] the danger rule (World.DangerWithin, Runner.DangerHours), refuges and the escape point; [x] the report's arrival times (Forecast.ToHolding), joining times, the seen-earlier list (where each could be by now, worked out at every report) and the theatre; [x] our own lords' marches (look-ups, hours_to, the food trip, closing in on the human player). (Left as they were: a wake's "could reach your nearest holding" for an enemy with no known road, Theatre.DecidedAt's last branch; the siege and battle clocks' joiners, already along the game's planned ways.) (Kept as it was: the threat in an area counts enemies seen there in the last 6 hours where they were seen, since it asks who is there, not who could come.)
- [x] The agent told the basis (the storm's clock, the relief warnings, the seen-earlier list, the war room's answers); instructions and descriptions updated (the march clock in How battles are joined; principle 2; my lieutenant's besiege line; could_reach, storm_relief, join_hours, danger_hours, danger_radius, besiege); the order setting unseen_floor (every order; 0 trusts the last-seen pace +10% after a day).
- [x] The war room's tools on the same out-of-sight rule (ask_the_way, its race, the cut-off check, can_catch; Marching.Clock); one seen over three days ago still reckoned for the agent's question, at the minimum, saying the orders no longer count it.
- [ ] Recorded games replayed, forecast against arrival, before and after.

Phase 2, the exact copy:
- [ ] The engine's way-finding code read (disassembly): step costs, which faces cost double, the 0.3 radius, the straightening, the 128 corners, ties, land and sea.
- [ ] The walking mesh copied out of the engine exactly, once per map: faces' corners, neighbours, ground, flags.
- [ ] The copy built (Common, for the game, the war room and the dashboard).
- [ ] The proof: every marching lord's planned way each hour, thousands of pairs asked of the game's own finder while paused, the hard cases; at least 999 in 1,000 identical within 0.01 units, every difference explained.
- [ ] Swapped in behind the door; today's finder retired.

## Phase 2 notes: the game's own way-finder in TaleWorlds.Native.dll (1.4.8)

- The managed call parties use (`MapScene.GetPathBetweenAIFaces` with region switch costs, `MobileParty.ComputePath`) is the engine binding `IScene.get_path_between_ai_face_indices_with_region_switch_cost`, number 941 in `ManagedCallbacks.ScriptingInterfaceObjects.EngineInterfaceGeneratedEnum` (TaleWorlds.Engine.AutoGenerated.dll, decompiled with ilspycmd under DOTNET_ROLL_FORWARD=Major); `get_path_distance_between_ai_faces` is 944, `get_path_between_ai_face_indices` (no region costs) 940.
- The engine registers each binding with its code: at +0x3957e3, `lea r8,[rip+0x140756]; mov edx,0x3AD; call +0x3986e0`, so binding 941's code is at **+0x4D5F40** (the wrapper).
- The wrapper (+0x4D5F40): the scene's face table at **scene+0x3218** (an array of face pointers, indexed by face index: start and end faces); with excluded ground, a set is built (+0x41F2F0) and registered with the scene's walking mesh object at **scene+0x2DA0** (+0x415C30, returns a handle); with an agent radius above 0 it calls **+0x4E8080** (navmesh, start face, start and end points, radius, the path out, the exclusion handle, the extra cost multiplier, the two region switch costs); with radius 0 another routine.
- Tools: C:\BannerlordArchive	ools (Disasm, ImmScan, Callers, XRefs, Hunt, NativeStrings).
- **+0x4E8080** (radius above 0): calls the search **+0x4F81B0**, then walks the found path (32-byte entries in a small-buffer vector: inline when byte [v+0x1024] is set, count [v+0x1020]; else [v+0x1000]..[v+0x1008]) reading each entry's face (+8) and its next face (+0x10): face flag byte +0x139 bit 4, a pointer at +0x118, a byte +0xA8 (more than 3: the face's corner count?), four pointers at +0xB0 (edges or neighbours), floats +0xD8 and +0xE4. Likely the straightening, with special faces (flag 4) handled apart. To read next.
- **The search, +0x4F81B0** (scene's mesh object, start face, end face, start point, end point, radius, path out, ...): fails unless both faces share the island at face **+0x128**; asks **+0x414120** of the start face and, if that says no, tries a direct line (**+0x50F230**), which, when clear, gives a one-leg path. Otherwise A*: a search slot per thread (**+0x41E990** gives it; generation stamps at mesh **+0x5F8 + slot*4**, bumped by **+0x4178C0**); each face keeps its visit stamp at **face+0x40 + slot*4** and its parent at **face + slot*8**; the open list is a binary min-heap of 24-byte entries {face (8), g (4), f (4, compared), point (8)}, seeded with the start face, g 0, f the straight distance between the two points. A per-group byte table at mesh **+0x5A8**, indexed by the face's group at **face+0x130**, marks closed ground (from the exclusion handle). The expansion (neighbours, the crossing point, the radius test, the extra cost, the region costs) is next.

