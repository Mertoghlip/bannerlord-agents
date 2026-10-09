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

## Two rules every order carries

- **Safety.** The game switches off a held lord's quick reactions, so that lord does not flee by itself.
  - When an enemy `caution` times stronger (default 1.5) comes within `danger_radius` (default 8), the lord shelters in the nearest friendly castle or town.
  - The order resumes once the danger has passed.
  - The agent is told, and `caution` 0 switches this off. Defend has its own sheltering instead.
- **Food.** With under two days of food, the lord goes to the nearest friendly town or village to buy more, then resumes.

## The library

The agent reads it in every call as `tools.txt` (written from the same code; `agents.tools` prints it in the game).

| Group | Orders |
|---|---|
| Moves | goto, patrol, engage, follow, leave, recruit, hold, free |
| Tools | defend, garrison, besiege, raid, hunt, screen, rest |
| Armies | gather, relieve, add, release, disband |
| Changing orders | a new order replaces the old one; adjust (settings only); no orders at all is a valid answer; then |

What each one does, takes, sets and calls back on is in `tools.txt`. The definitions are the JSON files in `src/BannerlordAgents/Tools/Library`, built into the DLL.

### Armies, by the game's own rules
- **gather** forms the army at once with the game's own call (`Kingdom.CreateArmy`), at the meeting place the agent names. The called lords march there and join as they arrive. Lords who cannot come (in a battle, too far, already in an army) are left out and named in the receipt; the order is refused only when none can come (since 0.5.1). The tool judges the army from the next hour on, since the hour's map copy was taken before the army existed.
- **Influence:** the leader's clan pays the game's influence price for each lord (`CalculatePartyInfluenceCost`). The order is refused if the clan cannot afford it.
- **Who cannot be called** (the same as the game's AI):
  - a lord already in an army, in a battle, besieging, or inside a besieged holding;
  - the realm's ruler (except as the leader);
  - a lord farther away than the game allows.
- **Who cannot lead:** a mercenary clan's lord.
- **Called lords we were holding** are let go, so the game can march them to the army.
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

- **Moves:** goto, enter, near (close to a holding's gate), toward (where a party was last seen), leave, patrol, defend, engage, follow, besiege, raid, recruit and hold.
- **Expressions:** numbers, true, false and none; + - * /; == != < <= > >=; and, or, not.
  - A comparison with something unknown (none) is false, so no rule fires on a missing fact.
  - `{...}` in a call-back is filled in, with ids shown as names.
- **Names:**
  - the order's own: `me`, `at`, `target`, `lords`, and the settings;
  - about the lord: `strength` and `men` (the whole army's when the lord leads one), `ranks`, `wounded`, `food_days`;
  - about time: `hours_in_state`, `hours_running`;
  - about armies and places: `in_army`, `leads_army`, `army_lords`, `army_coming`, `army_complete`, `in_settlement`, `besieging`.
- **Functions** (their names say what they do):
  - places and distance: `distance`, `hours_to`, `inside`, `within`, `fort`, `nearest_fort`, `nearest_supply`, `next_village`;
  - holdings: `besieged`, `siege_strength`, `besieger`, `raided`, `looted`, `ours`, `hostile`, `friendly`;
  - enemies near a place: `enemies_near`, `enemy_strength_near`, `strongest_enemy_near`, `weakest_enemy_near`, `nearest_enemy`, `army_near`;
  - a party: `strength_of`, `men_of`, `in_view`, `seen_ago`, `gone`;
  - the order's lords: `lords_arrived`, `lords_count`;
  - general: `min`, `max`, `pick`, `round`.
- **The checker** reads every tool before it runs: moves, functions, how many values each takes, names, states, ranges. Its problems are an alarm. The same checker will read the agent's own tools.

## What the agent reads besides the tools

- **WHAT CHANGED SINCE YOUR LAST ANSWER:** the reasons of every poke that came after the one it answered, and what became of its orders. The tools' call-backs are among them.
- **YOUR ORDERS IN FORCE:** each held lord's order, what it is doing right now (sheltering, buying food, waiting for an army), its settings that differ from the defaults, and its next order.
- **READY-MADE FACTS:** for up to four things the poke is about (a holding, an enemy), the realm's lords who can get there soonest, with strength, rough hours and whether they are free; and the enemies near it.
- **Look-ups** (`lookups\<id>.txt`), rewritten at each poke. The war room lets the agent read them:
  - one per holding: lords by march time, enemies near, enemy castles near;
  - one per lord: men, strength, ranks, food, hours to each holding, enemies near;
  - one per enemy the realm knows of: where it is or was last seen, its strength, the lords who can reach it soonest.

  Travel hours are rough: straight-line distance over the lord's speed.
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
- **Strength is the game's own estimate,** for a party, an army or a siege camp.
- **The garrison's strength is not added** when a tool weighs a siege it could break.
