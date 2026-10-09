# The agent chat (retired Oct 8)

> **Replaced by the war room** on Oct 8 ([WARROOM.md](WARROOM.md)): one fresh, short call per poke, from the same folder and through the same channel, with a median answer of 15 s instead of 53 s. The bridge now writes to `orc-war-council\warroom`, so the chat below would find nothing new in `channel\`. This page records how the first live session (Oct 8) was run.

For the first tests with Opus 5.5 (phase 5 on Oct 8), the orc agent was a separate Claude Code chat. It has:
- its own folder outside this repository, so it cannot read the code;
- its own instructions;
- no memory of the chat that builds the mod.

It sees only what the orcs see, and acts only through orders that the game checks. That makes it an independent test of the whole loop, from the report an agent reads to the orders the game accepts. The war room program takes over the same connection in week 3.

## Where things are

| What | Where |
|---|---|
| The agent's folder | `C:\Users\karab\Projects\orc-war-council` |
| Its instructions | `CLAUDE.md` there. The template is [agent/CLAUDE.template.md](../agent/CLAUDE.template.md); its goal is at the top and can be edited. |
| Its settings | `.claude\settings.json` there: Opus 5.5, auto memory off, no web, no subagents, edits allowed only in its own folder. |
| Its journal | `journal.md` there, its memory between pokes. |
| The connection | `channel\` there: `pokes.log`, `poke.txt`, `report.txt`, `orders\`, `receipts.log`. The fields are in [protocol/README.md](../protocol/README.md). |

`scripts/setup-agent.ps1` creates or refreshes the folder from the templates and points the game at `channel\`. It never overwrites an existing journal.

## Starting it

1. Load your campaign with Bannerlord Agents ticked. `agents.agent` in the console should say SHADOW, with the folder above.
2. In VS Code, open a new window (File > New Window), open the folder `C:\Users\karab\Projects\orc-war-council`, and start a Claude Code chat there. Check that it shows Opus 5.5.
3. Send it the seed prompt below.
4. The first time it starts its watch on `pokes.log`, Claude Code may ask permission once. Allow it, and choose to always allow that command.

### Seed prompt

```
You are the war council of the Misty Mountain Orcs in a live game of Mount & Blade II: Bannerlord. Everything you need is in CLAUDE.md in this folder: your goal, how the game wakes you, how to give orders, and the boundaries you keep. Read it now and follow its Starting steps: read journal.md, start the monitor on channel/pokes.log, read the latest report, write your opening plan in journal.md, and answer the newest poke if there is one. Then keep answering pokes as they arrive, quickly, because the game does not wait for you. Stay inside this folder.
```

## Running the phases

| Phase | Console | What happens |
|---|---|---|
| 5 · Opus watches | `agents.agent shadow` (already set) | Every order is checked against the game and answered in `receipts.log`, but nothing is carried out |
| 6 · Opus commands one lord | `agents.agent live Throkmaw` (any one lord, by name or id) | That lord obeys; orders for the others are refused |
| 7 · the whole realm (after the tools) | `agents.agent live` | Every orc lord obeys |
| Stop | `agents.agent shadow` or `agents.agent off`, then `agents.release all` | Orders stop being carried out; `release` hands every lord back to the game |

Each receipt line gives the time from the poke to the agent's answer, in seconds and in in-game hours. The game's log keeps the same as data (`orders.jsonl`, records of type `agent_receipt`).

## Watching it

- **The agent chat:** what it is thinking.
- **`channel\receipts.log`:** what the game made of each order.
- **In the game:** the cyan messages when an order is given, arrives or ends.

## Starting fresh

To start a run fresh, close the agent chat, empty `journal.md` (or delete it and run `scripts/setup-agent.ps1` again), and open a new chat with the seed prompt.
