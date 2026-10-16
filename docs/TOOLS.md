# Tools: what the agent's lords can do between its answers

Since version 0.4 (Oct 8), every order the agent gives is a **move** or a **tool**:
- a move is a single step, such as go to a holding or attack a lord;
- a tool is a small set of rules that a lord follows on its own between the agent's answers. Defend, for example, patrols around a holding, shelters from armies too strong for the lord, strikes weaker ones and breaks weak sieges.

A tool calls the agent back, with a poke, only when its plan may need to change. This is the plan's behavior language (RQ1, RQ2): our tools come first, and the agent's own tools come in week 4.

## How an order travels

1. **The agent answers a poke** with one JSON object (the war room writes it into `orders\poke-N.json`; [WARROOM.md](WARROOM.md)):
   ```json
   {"poke": 12, "orders": [
     {"lord": "lord_MM2_3", "do": "defend", "at": "town_MM2", "radius": 25, "why": "Aragorn marches on Western Moria"},
     {"lord": "lord_MM10_5", "do": "gather", "at": "castle_MM5", "lords": ["lord_MM8_1", "lord_MM14_3"],
      "then": {"do": "relieve", "at": "town_MM2"}}]}
   ```
   - `at` is a holding and `target` a lord, both as ids or names. `lords` is a list.
   - Settings are numbers, given at the top level or in `"settings"`.
   - `then` is the next order, started when this one finishes.
2. **The game checks each order** against the game as it stands (`Orders.Issue`, on the game's thread):
   - the lord leads a party, and is not marching in someone else's army;
   - the place or target is the kind the tool takes (an enemy castle, one of your holdings, ...);
   - the settings exist and are kept to their ranges;
   - for an army, the game's own rules and influence price (below).

   In shadow mode it stops here and receipts "checked".
3. **The lord is held:** the game's AI stops deciding for that lord. The tool goes to the **runner** on the helper thread (`Tools.Runner`).
4. **Every in-game hour, right after the hourly copy,** the runner checks each tool's rules against the realm's own view (`Tools.World`): what the realm sees, and where it last saw an enemy. It never looks at the game itself, so a tool knows exactly what the agent could know.
   - When a rule fires, the tool changes state and the lord gets a new move, made on the game's thread (`Orders.ToolMove`).
   - A rule may call the agent back. That becomes a notice, so it goes out with the same hour's poke.
5. **On the game's thread** the move is re-issued whenever the game turns the lord away. The hold ends when the lord dies, is captured, loses the party or leaves the realm, and the agent hears why.
   - **Party AI Controls** (since 0.6.3): each hour it looks at every lord party (`PartyAIThinker.OnHourlyTickParty`), and for a lord it does not manage that stands still (`Hold`) with the game's AI switched off, it switches the AI back on: it takes a held lord for a stuck one. The game's own hourly decision then changed the lord's plan within the hour, and the order was re-issued (Oct 16: 40 times in two and a half game days, mostly army leaders waiting for their lords). A hook right after that look (`GameHooks`, `KeepHeldPatch`) holds such a lord again when one of our orders holds it.

## Two rules every order carries

- **Safety.** The game switches off a held lord's quick reactions, so that lord does not flee by itself. (One reaction survives, the game's own exception: a held lord may still attack, or pull back from, an enemy fighting at or besieging the very holding it was sent to; `MobilePartyAi.GetBehaviors`.)
  - When an enemy lord, army or band of bandits (since 0.6.1) `caution` times stronger (default 1.5) comes within `danger_radius` (default 8), the lord shelters, or runs (since 0.6.2, Mert: shelter where the defence would hold, else run):
    - **Where it may go:** the castles and towns of the realm and its allies not under siege that it reaches before the enemy (its hours there at its own speed, no more than the enemy's at the enemy's), nearest first; the order's own holding first when it has a `stay_inside` setting.
    - **Would it hold?** For each, the game's own test for whether besiegers should assault (`BesiegerCamp.StartingAssaultOnBesiegedSettlementIsLogical`): they assault only when stronger than the defenders times the settlement's advantage to the power 0.8. Taken with a ram and two towers built, the walls are worth 2.4 times the defenders for level-1 walls, 2.8 for level 2, 3.2 for level 3. The defenders are the garrison, the militia (militia hold the walls, though they never sally), the realm's lords inside, the realm's lords holding a defend or garrison order for it within 20, and this lord. Against them: the danger, and every force threatening that place (the threat near it, below). An ally's garrison counts only for a place in sight.
    - **Shelter:** inside the first that would hold, even the castle the enemy is marching on.
    - **Run:** if none would, toward the nearest it reaches first that takes it farther from the enemy, never the place the enemy besieges or marches on, stopping outside; with none, straight away from the enemy, twice the danger radius. It runs again from where it is each hour, and takes a refuge that would hold as soon as one is in reach.
    - **Already inside** a castle or town that would not hold (and not besieged, so it can still leave): it leaves and does the same. Inside a village, which gives no protection, it is as if outside.
    - `stay_inside` 1 (garrison's default) keeps the lord in its own holding whatever comes.
  - While an order strikes a siege camp (relieve, defend's relief), the other lords camped in that siege are part of the fight it chose and do not count (0.6.1).
  - The order resumes once the danger has passed (at least two hours, and nothing that strong within 1.5 times the radius).
  - The agent is told, and `caution` 0 switches this off. Since 0.6.1 defend carries it too, beside its own sheltering.
- **Food.** With under two days of food, the lord goes to the nearest friendly town or village to buy more, then resumes.

## The library

The agent reads it in every call as `tools.txt` (written from the same code; `agents.tools` prints it in the game).

| Group | Orders |
|---|---|
| Moves | goto, patrol, engage, follow, leave, recruit, hold, free |
| Tools | defend, garrison, besiege, raid, hunt, screen, rest |
| Armies | gather, relieve, join, add, release, disband |
| Changing orders | a new order replaces the old one; adjust (settings only); no orders at all is a valid answer; then |

What each one does, takes, sets and calls back on is in `tools.txt`. The definitions are the JSON files in `src/BannerlordAgents/Tools/Library`, built into the DLL.

### Armies, by the game's own rules
- **gather** forms the army at once with the game's own call (`Kingdom.CreateArmy`), at the meeting place the agent names. The called lords march there and join as they arrive. The tool judges the army from the next hour on, since the hour's map copy was taken before the army existed.
  - **Lords too far away to be called yet** (since 0.6) are not left out: each gets a **join** order and marches toward the army until the game would let it be called (below). The army then forms with whoever is in range, even the leader alone, and gather waits for the marching lords too (`army_joining`) before it calls back that the army has formed.
  - **Lords who cannot come at all** (in a battle, inside a besieged holding, besieging, already in an army) are left out and named in the receipt; the order is refused only when no lord can come (since 0.5.1).
  - The receipt says who comes now, who marches first (with men, and the influence they would cost if called now; less as they come nearer, since the game prices distance), and who cannot come.
- **join** (since 0.6, `target`: the army's leader, or a lord in that army): the lord follows the army's leader and joins the army as soon as the game's calling rules allow (`CannotCall`, the same as gather's), paying the leader's clan's influence then, like add. Checked on the game's thread when the move is made and every in-game hour (`Orders.TryJoin`).
  - It keeps marching while the lord is only too far, or busy in a battle.
  - It fails, sets the lord free and calls the agent back when the army has broken up, its leader is gone, the lord cannot be called for another reason, or the leader's clan cannot pay. An army missing in the first 3 hours is waited for, since it may be forming in the same answer.
  - A lord who joins goes into the chronicle without a poke (as does a held lord called into an army); gather's own call-back tells the agent when the army is complete.
- **add** sends lords too far away to be called the same way: a join order each.
- **Influence:** the leader's clan pays the game's influence price for each lord (`CalculatePartyInfluenceCost`). The order is refused if the clan cannot afford the lords called at once; the receipt warns when it may not afford the marching ones too.
- **Who cannot be called** (the same as the game's AI):
  - a lord already in an army, in a battle, besieging, or inside a besieged holding;
  - the realm's ruler (except as the leader);
  - a lord farther away than the game allows (`MaximumDistanceToCallToArmy`: eight times the average distance between neighbouring towns, along the way): these march to join instead.
- **Who cannot lead:** a mercenary clan's lord.
- **Called lords we were holding** are let go, so the game can march them to the army.

### Patrol and defend (since 0.6.1)
- **The game's own patrol** (`SetMovePatrolAroundSettlement`, read in 1.4.8): the lord walks to random points in a ring between half and all of a patrol radius from the holding's gate, and the radius is half a day's march at the lord's own speed for a castle or town, a quarter of a day for a village (`DefaultMobilePartyAIModel.GetPatrolRadius`). At the orcs' median speed, 3.4 map units an hour, that is 20 to 41 units out from a castle, 10 to 20 from a village.
- **A patrol kept within a radius:** a move made by a tool with a `radius` setting (defend, screen), or the plain patrol given one, is our own patrol instead: from point to point, each a reachable spot between a quarter and three quarters of the radius from the gate (`Orders.NextPatrolPoint`), the next one picked the hour the lord reaches the last. The plain patrol's radius is 0 by default, which leaves it to the game.
- **defend**, step by step:
  - **patrolling** within its radius; each hour, the first that holds of: the holding lost (it stops); ranks below `floor` while it is not needed there (it goes to refill; below); a siege it can break (it strikes the besiegers, with the defenders inside when they would sally out); any other siege (it shelters); the threat near the holding at least `shelter_at` times its strength (it shelters); a weak enemy lord near the holding (it strikes the weakest);
  - **sheltering** (0.6.2): inside the holding when its garrison, militia and the realm's lords there, with this lord, would hold it against the threat near it (the game's assault test above), even under siege, to reinforce its walls; otherwise in the nearest castle or town of the realm or an ally that would hold against what threatens it, or else the nearest other one. `stay_inside` 1 shelters inside the holding whatever the odds. It strikes a siege from there when it becomes breakable, and patrols again after at least 3 hours once there is no siege and the threat is below 80% of the sheltering line;
  - **refilling** (since 0.6.3, Mert: lords weak alone may be needed, and massed they are strong; recruit when not needed urgently): `ranks` is the lord's own party against its size limit, and the orcs' lords stood at a quarter of it on average (Oct 16), so the old rule sent nearly every defender, armies too, off to recruit the hour it got the order. Now it goes only when it is not needed where it is:
    - not in an army (an army's leader never leaves to refill by itself: the agent massed it for a reason);
    - the holding not besieged, and no enemy lord or army within twice the radius of it (`enemy_threat_near`: bandits do not count);
    - not within two days of leaving a place that gave few recruits.

    It refills in the nearest town or village of the realm or an ally that can give recruits (castles give none), and is back on the first of: its ranks over the floor (after at least 3 hours); an enemy lord or army near the holding, or a siege of it (it breaks off, and the patrol's rules decide what next); 12 hours at the place without 5 points of ranks gained (it leaves, and waits two days before trying again); two days in all. `floor` 0 never leaves to refill;
  - **striking a weak lord**: given up when the target is gone or out of sight, too far from the holding, too strong, when another siege starts, or when the threat near the holding reaches the sheltering line;
  - **breaking a siege**: until it is broken, the besiegers grow too strong, or other enemy forces come near beside them.
- **The threat near a place** (`threat_near`, since 0.6.1): the strength of the hostile lords, armies and bandits in view within the radius, and of the hostile lords and armies seen there in the last 6 hours, at the place they were last seen, unless known gone. Defend and raid weigh it when they decide to shelter or pull back; striking still needs an enemy in view. `enemy_threat_near` (0.6.3) is the same without bandits: the forces that make a lord needed where it is.

### Breaking a siege: when the defenders inside join (since 0.6)
Read in the 1.4.8 code (`SallyOutsCampaignBehavior`, `StartBattleAction`, `MapEvent.Initialize`, `Town.GetDefenderParties`):
- A relief force that attacks the besiegers starts a "siege outside" battle. All the besieging lords fight it together.
- **The garrison and the lords sheltering inside** (the realm's and its allies') sally out to join that battle only when they, plus the friendly forces near the siege camp (the relief among them), are **more than 1.5 times** the enemy forces near the camp. The game checks this when the battle starts and again every hour while it lasts. (Without a relief, the bar for a sally on its own is 2 times, checked every 4 hours.)
- **Militia never sally out.**
- So **relieve** and **defend** now weigh `relief_strength(at)`: the relief's own strength, plus the garrison's and the inside lords' (`inside_strength(at)`) when that rule holds, against the besiegers'. A lord sheltering inside is not counted twice. READY-MADE FACTS give the strength inside a besieged holding of the realm and the rule.
- **Ordering the army:** orders go to the leader, and the leader's moves and tools move the whole army. A party marching in an army cannot be ordered.
- **The game still breaks armies up:**
  - idle (holding, or waiting at a friendly place) for about two days;
  - cohesion run out;
  - more than half of its parties starving;
  - no war left.
- **Saves:** armies are ordinary game armies, so they are saved like any other. Only the hold on the leader is handed back around every save.

## The language

A tool is a JSON file:
- `settings`: each written [default, min, max, "what it is"];
- `init`: values remembered from the start;
- `start`: the first state;
- `states`: each has:
  - `about`: what the orders in force say it is doing;
  - `do`: the move made when the state is entered;
  - `when`: ordered rules (`if`, `go`, optional `poke` and `set`). The first rule that holds fires.
- A state with `end` finishes the tool: `free` hands the lord back to the game's AI, `hold` keeps the lord standing, and `fail` says the tool could not be done: the lord is set free and its next order does not start (since 0.5.1). Otherwise a `then` order starts instead, if there is one.

The defend tool, shortened:

```json
"patrol":  { "about": "patrolling around {at}", "do": "patrol at",
  "when": [ { "if": "besieged(at)", "go": "shelter", "poke": "A strong force besieges {at}..." },
            { "if": "enemy_strength_near(at, radius) >= strength * shelter_at", "go": "shelter", "poke": "..." },
            { "if": "weakest_enemy_near(at, radius) != none and strength_of(weakest_enemy_near(at, radius)) <= strength * strike_below",
              "go": "strike", "set": { "target": "weakest_enemy_near(at, radius)" } } ] },
"shelter": { "about": "sheltering in {refuge}", "do": "enter refuge",
  "when": [ { "if": "hours_in_state >= 3 and not besieged(at) and enemy_strength_near(at, radius) < strength * shelter_at * 0.8", "go": "patrol" } ] }
```

- **Moves:** goto, enter, near (close to a holding's gate), toward (where a party was last seen), leave, patrol, defend, engage, follow, join (follow an army's leader and join once it can be called), besiege, raid, recruit and hold.
- **Expressions:** numbers, true, false and none; + - * /; == != < <= > >=; and, or, not.
  - A comparison with something unknown (none) is false, so no rule fires on a missing fact.
  - `{...}` in a call-back is filled in, with ids shown as names.
- **Names:**
  - the order's own: `me`, `at`, `target`, `lords`, and the settings;
  - about the lord: `strength` and `men` (the whole army's when the lord leads one), `ranks`, `wounded`, `food_days`;
  - about time: `hours_in_state`, `hours_running`;
  - about armies and places: `in_army`, `leads_army`, `army_lords`, `army_coming`, `army_complete`, `army_joining` (lords holding a join order for this lord's army), `in_settlement`, `besieging`.
- **Functions** (their names say what they do):
  - places and distance: `distance`, `hours_to`, `inside`, `within`, `fort`, `nearest_fort`, `nearest_supply`, `next_village`;
  - holdings: `besieged`, `siege_strength`, `inside_strength`, `relief_strength`, `besieger`, `raided`, `looted`, `ours`, `hostile`, `friendly`, `other_fort` (the nearest castle or town of the realm or an ally, not under siege, other than the one given);
  - enemies near a place: `enemies_near`, `enemy_strength_near`, `strongest_enemy_near`, `weakest_enemy_near`, `nearest_enemy`, `army_near` (enemy lords and armies in view), and `threat_near` (also bandits, and armies seen there in the last 6 hours), `enemy_threat_near` (the same without bandits);
  - a party: `strength_of`, `men_of`, `in_view`, `seen_ago`, `gone`;
  - the order's lords: `lords_arrived`, `lords_count`;
  - general: `min`, `max`, `pick`, `round`.
- **The checker** reads every tool before it runs: moves, functions, how many values each takes, names, states, ranges. Its problems are an alarm. The same checker will read the agent's own tools.

## What the agent reads besides the tools

- **WHAT CHANGED SINCE YOUR LAST ANSWER:** the reasons of every poke that came after the one it answered, and what became of its orders. The tools' call-backs are among them.
- **YOUR ORDERS IN FORCE:** each held lord's order, what it is doing right now (sheltering, buying food, waiting for an army), its settings that differ from the defaults, and its next order.
- **READY-MADE FACTS:** for up to four things the poke is about (a holding, an enemy), the realm's lords who can get there soonest, with strength, rough hours and whether they are free; and the enemies near it. Since 0.6 also:
  - **the muster** at the first holding the poke is about: how many lords, men and strength could be there within one, two and three days, as running totals, and how many of the first day's a leader there could call into an army at once;
  - for a besieged holding of the realm, the strength inside and the sally rule above.
- **The report** (since 0.6) opens with **YOUR REALM IN SUM** (lords leading parties, their men and strength, armies, how many hold the agent's orders and how many are led by the game's own AI; castles and towns with garrisons, militia and sieges; villages looted or raided; lords held prisoner) and lists **YOUR CAPTURED LORDS**, grouped by where each is held (a holding, or the party holding them) and since when. They come from the game's own prisoner records, which the encyclopedia shows a player too, so the fog check allows their ids.
  - **YOUR HOLDINGS** (since 0.6.3): each castle and town with its wall level, its defenders' strength (garrison, militia and the lords inside) and the besiegers' strength it holds against, by the same assault test as the shelter rule (walls worth 2.4 / 2.8 / 3.2), with one line saying how the test works: above that, besiegers may storm at any hour, the sooner the stronger; below, they wait, but after four days of siege the bar falls hour by hour, to the defenders' bare strength by about the sixth day (`BesiegerCamp`, 1.4.8). Before, the agent saw garrison men only, and could not match a defence to an attack (Oct 16, Western Moria against Aragorn's 14,695).
  - **IN VIEW** lists at most 40 parties, cut in this order since 0.6.3: allies and neutrals first, then bandits, then enemy lords; an enemy army is never cut. Before, it was cut by distance from the nearest holding, and on Oct 16 the 15 to 24 bands of bandits near the villages kept Aragorn's army of 5,532 men off the list for days, though the realm saw it. A sighting and the last-seen list now give an army's strength too.
- **Look-ups** (`lookups\<id>.txt`), rewritten at each poke. The war room lets the agent read them:
  - one per holding: lords by march time, enemies near, enemy castles near; for a castle or town, the full muster (every lord by how soon they could be there, with running totals, * for those a leader there could call at once);
  - one per lord: men, strength, ranks, food, hours to each holding, enemies near;
  - one per captured lord: where and since when, and the lords who can reach that place soonest; `captured.txt` lists them all by place;
  - one per enemy the realm knows of: where it is or was last seen, its strength, the lords who can reach it soonest.

  Travel hours are rough: straight-line distance over the lord's speed. The calling range is checked the same way, so it is a little generous: the game measures along the way.
- **The council's briefing** (`council.txt`, since 0.6, every in-game hour): the whole realm at once for the war room's council of war ([WARROOM.md](WARROOM.md#the-council-of-war)): the purse (the ruler's clan's gold and influence, every other clan's influence), every lord with ranks, food, speed, what it is doing, whose judgment it follows and its clan's influence, the muster at each castle and town, and the captured lords.
- **Its memory** (since 0.5, [MEMORY.md](MEMORY.md)): the story so far, its own notes, and its chronicle, of which the report's last section shows the last two days. A tool's call-backs and endings go into the chronicle, so the story remembers which orders worked.

## Testing by hand

- `agents.tools` lists the library and says whether every tool passed the checker. `agents.tools defend` shows one tool.
- `agents.order <lord> <order> <place or lord> [setting=value] [lords=a,b]` gives the same orders by hand, always carried out. Examples:
  - `agents.order Throkmaw defend Caradhras radius=25`
  - `agents.order Krumbash gather Methedras lords=Naglur,Vughza`
  - `agents.order Gashnar adjust caution=0`
- `agents.orders` lists every held lord with its tool and its current move. `agents.release <lord>` or `agents.release all` hands lords back.
- In the logs (docs/LOGGING.md): `tool` events for every start, step, call-back, shelter and end; `army` events; `order` events for every move made.

## Limits we know

- **Rules are checked once an in-game hour,** as the game's own AI thinks. At fast speed that is about a second.
- **When a tool attacks a lord, the game's move heads for that lord's true position,** as the game's own AI does. A lord out of view is searched for where the realm last saw it (toward), so the tools themselves never use what the realm cannot see.
- **Strength is the game's own estimate,** for a party, an army, a garrison or a siege camp. The game's sally check weighs a slightly different number (each party's strength for its side and the ground, times its aggressiveness) and counts every friendly and enemy party near the camp, so `relief_strength` is close to the game's judgment, not identical.
