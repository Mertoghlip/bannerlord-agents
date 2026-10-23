# The lieutenant (since 0.10)

An agent that serves the player's own realm, through the game's chat box on the campaign map. It is the human-in-the-loop arm of the project: every proposal, and whether the player agreed, changed it or turned it down, is logged.

**Mert's words.**
- Oct 22: "lets build an agent that acts as the player's 'liutenant' ... have the player type orders into that chat to their liutenant, which then orders lords to do stuff on the map ... it would propose sending x, x, x lords there, with a combined force of x men, to defend it. I would verify and approve the order ... I could also ask it what lords are doing across the realm, any threats that were spotted in fronts I'm far away from, and also what lords I gave orders to are currently doing."
- Oct 23: "Lets keep everything verbal in the chatbox. My approval should also be verbal. Keep its answers short and concise whenever possible, make popups pause the game until the popup is closed"; it may command every lord of the realm, "including my own clan's parties"; "Lets keep it in the chat window for now, the companion chat will be a quality of life upgrade for later."

## Using it

- On the campaign map, press **Enter** (or **T**) to open the chat box at the bottom left. The line shows **(Lieutenant)**; the game's clock stops while you type (`lieutenant_pause` in the bridge's settings).
- Type what you want, up to 400 letters, and press **Enter**. Your line appears as "You: ...", and the lieutenant answers as "Lieutenant: ..." after 5 to 30 seconds (the clock runs again once the box closes).
- Ask anything: what lords are doing, what the realm can see on a far front, what your ordered lords are up to, who could reach a place first, how a battle would go.
- Ask for something to be done ("hold Goblin Town against the orcs with dwarves"): it proposes who goes, with how many men and when they get there, and asks. **Nothing moves until you say yes** in the chat ("yes", "do it", "only Dain and Thorin"). Say "no" to drop it.
- A long answer (when you ask for one) opens in a window that pauses the game until you close it.
- When one of its orders calls back or ends, it says so in the chat box.

## How it works

- **The folder** (outside the repository, made by `scripts\setup-agent.ps1 -Workspace <folder> -Kind lieutenant -Mode live`): `agent.txt` with `kind = lieutenant` and `realm = player` (the player's realm at each load), its instructions `warroom-system.md` (from `agent/lieutenant-system.template.md`), its war room's settings `warroom.txt` (effort medium, thinking cap 2000, timeout 120 s, no council), its channel `warroom\`, and its memory `memory\<campaign>\chat.jsonl` (the conversation). It is listed in the bridge's `agents`, after the realms' agents, and `scripts\agents.ps1 start` runs its war room with the others.
- **The chat box** (`Lieutenant.cs`, `ChatHook`): the game's single-player chat log is the multiplayer chat box with its typing line taken out (its `TextInputWidget` path names a widget the prefab no longer has) and its channels closed (the "all" and "team" channels need a server). The module brings the prefab back with its typing line (`module\GUI\Prefabs\SPChatLog.xml`: the game's own, plus the line), and five Harmony patches on `MPChatVM`: on the campaign map with a lieutenant, "all" and "team" become the open "party" channel, named "(Lieutenant)"; what is sent goes to the lieutenant instead of a server; messages may be 400 letters; the clock stops while typing. Missions and multiplayer are left as they are. The patches attach only when a lieutenant is in the agents' settings.
- **The bridge's side**: the player's realm is a seat like an agent's realm (its sight, its report every hour with a **YOUR RULER** line saying where the player's party is, its look-ups every hour, its view file for the war room's questions), but it is **never poked**. What the player sends goes into `chat.json` (the words, any words since the last answer, and the proposal waiting, if any) and a line in `chats.log`.
- **The war room** (`LieutenantRoom.cs`): for each new `chat.json`, one fresh call: its instructions and the order library as the system prompt; as the message the conversation so far (the last 14 exchanges), what became of its orders lately (`receipts.log`), the waiting proposal, the realm's report, and the player's words. It may read its look-ups and ask the same questions as the realms' agents (`ask_the_way`, `battle_odds`, `can_catch`, `roads_in`). The answer goes to `orders\chat-N.json`: `say`, `propose`, `carry_out` with `orders`, `drop`, `detail`; the war room adds `pending_chat`, the waiting proposal's number from `chat.json`.
- **Never unasked** (`LieutenantRules.Gate`): the bridge carries out orders only when the answer's `pending_chat` is the proposal still waiting, i.e. the player's words were the answer to it. Orders without that become the proposal and wait for a yes; a new proposal replaces the waiting one; `drop` forgets it.
- **Its orders** go through `Orders.FromAgent`, like every agent's, in its mode (live) and for every lord of the realm (`lords = all`): never the player's own party, and never a lord holding an order from the player's dialogue (Party AI Controls; release him there first). A refusal is said in the chat box at once; the order's call-backs and its end come into the chat box from the realm's hour (`Lieutenant.Heard`).
- **Logs**: `%LOCALAPPDATA%\BannerlordAgents\logs\warroom-lieutenant` (`calls.jsonl` with kind `chat`, each call's prompt, stream and answer), the channel's `chats.log` and `receipts.log`, and the session's trace (`lieutenant` events: said, answer file, answered with its gate).

## Not yet seen in a game

- The chat box itself: the typing line, its focus, Enter sending, the clock stopping and running again.
- The player's clan parties: Party AI Controls manages them; a lord without a dialogue order can be held, and `KeepHeldPatch` re-holds any lord its hourly look wakes, but whether its own handling of managed parties fights our moves has not been seen.
- The cost of the player's realm as a seat on the game's thread (its sight from every lord and holding, every hour).
- Measured in a scratch folder on Oct 23 with a real report: a question with a proposal of two recruit orders in 25 s, the verbal yes carried out in 6 s, a front question in 14 s.
