# Memory: how a realm remembers the war

Since version 0.5 (Oct 9), each realm keeps a continuous memory of the game. Every answer is still a fresh call, which keeps answers short ([WARROOM.md](WARROOM.md)), so the memory is written down and handed to the agent with each poke.

**Why not one long conversation?** The agent chat of Oct 8 kept everything in one conversation. It grew from 66,000 to 193,000 tokens in 27 minutes, because every report stayed in it.
- **It fills up.** Within an hour or two of play, Claude Code would squeeze it into a summary by itself, at a moment nobody chooses.
- **It is mostly old reports.** The history that matters is buried among them.
- **It cannot carry over.** The small model of November could not hold it.

The written memory is short, and the same size at every poke. The one-long-conversation memory is kept as a setting, so the two can be compared ([below](#comparing-the-two-memories)).

## The three parts

| Part | Who writes it | What it holds | Where |
|---|---|---|---|
| **The chronicle** | the bridge, as things happen | everything the realm learned, dated, one line each | the campaign's folder (`chronicle_<realm>.jsonl`), mirrored into the channel (`chronicle.txt`, `chronicle.jsonl`) |
| **The ruler's notes** | the agent, through its answers | its plan, what it knows about each enemy, its lessons | the agent's folder, `memory\<campaign>\notes.md` |
| **The story so far** | the chronicler (Opus, in quiet moments) | the whole war in a few hundred words | `memory\<campaign>\story.md` |

### The chronicle (`Chronicle`, `GameHistory`)
- **Every notice that reached the realm**, except the ones kept quiet:
  - wars and peace, alliances, calls to war;
  - sieges begun and lifted;
  - raids and lootings;
  - holdings won and lost;
  - battles, with who won;
  - lords captured, killed or set free;
  - armies formed and broken up;
  - its orders' call-backs and endings.
- **Every enemy army it saw come into view.** A lord passing by is not history.
- **Every order the agent gave,** carried out or refused, with the agent's reason. The checks of shadow mode are not history.
- **At the first load, what the game's own history log still holds about the realm,** marked "(from the game's records)":
  - its wars and peace, and those of the realms it is at war or allied with;
  - its holdings changing hands or besieged;
  - its lords captured, freed, beaten or killed;
  - its clans coming and going, its armies, and battles at its holdings.

  The game keeps most of these for only a week (a death for ever, a peace 40 days, a capture 84), so from then on the chronicle is the only record. These lines fill only the time before the chronicle's own lines begin, so nothing is told twice.
- **New notices for it (0.5):**
  - a siege lifted;
  - a lord killed (in battle, executed, of age);
  - a lord set free (ransomed, escaped, released);
  - an army of the realm's own formed.

  The first two poke. The last two only go into the chronicle (`Notice.RecordOnly`).
- **Fog:** every line comes from what the realm was told or saw, or from the game's public history, which a player can read in the encyclopedia.

The report's last section, **RECENT HISTORY, LAST TWO DAYS**, is the chronicle's last 25 lines within 48 hours. It replaces the old NEWS. `agents.chronicle [n]` prints the last lines in the game's console.

### The ruler's notes (`Memory`, in the war room)
- **Plan:** replaced by the answer's `plan`.
- **Enemies:** one line per name, from the answer's `enemies`: `{"Aragorn": "comes through the Redhorn Gate"}`. A new note replaces the old one on that name, and `""` removes it.
- **Lessons:** the answer's `lessons` are added. Past `max_lessons`, the chronicler merges them; any the agent adds meanwhile are kept after the merged ones.
- **The old journal:** the first time, the old single journal (`journal.md`) became the campaign's notes, with its plan and its lessons. Its list of lords' tasks did not carry over, since YOUR ORDERS IN FORCE says it at every poke. The journal is then renamed `journal-before-memory-<date>.md`, so it is never read twice. Those notes count from the first poke of the memory: loading a save from before it starts with empty notes, since the journal belonged to another run of the game (Oct 9: a Summer 9 save was given notes written at Summer 17).

### The story so far (the chronicler)
- **When:** once `story_every` (12) new chronicle lines have come, or the oldest of them is a day and a half older than the newest poke.
- **How:** the chronicler is a separate call: the story so far, the notes (for context only) and the new lines go in, and one new story so far (at most `story_words`, 450) comes out.
- **Never in the way:** it runs on its own thread, beside the answers. A poke that arrives meanwhile is answered at once, from the previous story.
- **Instructions:** `chronicler-system.md` in the agent's folder (template `agent/chronicler-system.template.md`). Facts only: dates of turning points, sieges and how they ended, lords lost, orders that worked or failed, threats still open. No advice.
- **Failure:** a failed attempt waits five minutes, and the story stays as it was.

## What each answer gets

The message of each call, before the poke:
1. **THE STORY SO FAR**, with the date it reaches.
2. **YOUR NOTES.**
3. **YOUR CHRONICLE SINCE THE STORY,** only when the chronicler is behind. These are the lines after the story that the report's RECENT HISTORY does not show, at most 80; any older ones are in `chronicle.txt`.

Then the poke itself, as before. The agent can also read the whole `chronicle.txt` when it needs an old detail (`lookups = on`).

## Reloads, restarts and campaigns

- **Closing the game or the war room** loses nothing: the chronicle, the notes and the story are files.
- **Loading any save** (since 0.9.1: earlier, later, or on another branch of play):
  - every chronicle line, and every copy of the notes and the story in `memory\<campaign>\snapshots.jsonl` (written at every change, with the in-game hour), is labelled with the game session that wrote it, and nothing is deleted;
  - at every save the bridge notes, in the campaign's own `saves.jsonl` (never in the save itself), the save's name, its in-game hour, the session that made it, and that session's own history;
  - a load finds its save's note by name and hour, so its history is exactly the sessions its world lived through, each up to the hour it was saved at. The chronicle shows only those lines, and the war room puts back the last copy of the notes and the story written within that history. `poke.json` carries the history;
  - a save with no note (made before 0.9.1, renamed, or made without this mod) gets the lines written before labels began, up to its date. That is the old rule, which kept those lines to one line of play;
  - so the realm never remembers what then did not happen, and switching between saves of different runs never mixes their histories.
- **Another campaign** gets its own memory, and its own chronicle in its own folder.
- **Measured runs** start from the same save, so each starts with the memory of that date: empty, apart from the game's own history.
- **Each realm** keeps its own memory, of only what it could know. When Mordor, Harad and Rhûn play, each will have its own channel.

## Settings (`warroom.txt`)

| Key | Default | What it does |
|---|---|---|
| `memory` | written | `written`: a fresh call each poke with the story, the notes and the chronicle. `conversation`: one long conversation, for comparison. |
| `story_every` | 12 | new chronicle lines before the story is rewritten |
| `story_words` | 450 | the story's length at most |
| `story_model`, `story_effort`, `story_thinking_cap` | the agent's model, medium, 2,000 | the chronicler's call |
| `max_lessons` | 12 in the live file, 15 by default | lessons kept before the chronicler merges them |
| `conversation_limit` | 180,000 | in the conversation memory: past this many tokens, the next poke starts a new conversation from the notes |

## Comparing the two memories

```
WarRoom replay --pokes <pokes_<realm>.jsonl> --memory written      --from 1 --to 125 [--model m] [--tag t]
WarRoom replay --pokes <pokes_<realm>.jsonl> --memory conversation --from 1 --to 125 [--model m] [--tag t]
```

- **The setup:** recorded pokes are answered in order, each in a scratch folder of its own, with the memory carried from answer to answer. Nothing reaches the game.
- **The pokes:** with `--from`/`--to`, only the ones the bridge would still send (not those made only of small fights). `--numbers` picks them by hand.
- **The chronicle** grows from the recorded pokes' reasons, minus what is now kept quiet, and from the replay's own orders. Nothing is carried out, so those orders have no outcomes.
- **The output:** each answer's time and size; the notes, the story and the answers are kept for reading side by side.

This is the experiment behind the choice: answer time, usage, and the quality of the decisions with each memory (RQ3).

**First check (Oct 9, small model, Haiku 4.5, on pokes #6, #10 and #54):**

| Memory | Tokens read per answer | Answer time | Story |
|---|---|---|---|
| Written | 11,800, 12,000, 13,500 | 8 to 16 s | rewritten once, 350 words, in 15 s |
| Conversation | 11,800, 21,100, 30,500 | 10 to 12 s | (none) |

- **Both work end to end:** the conversation is resumed each poke, the story is written, and the notes change with the answers.
- **Not yet run:** the full comparison with Opus 5.5, which uses about 120 calls of the subscription.

## Checks

- `WarRoom selftest` (also run by `scripts\deploy.ps1`; 38 checks since 0.6, the council of war included; no calls) covers:
  - the journal becoming the notes;
  - what an answer changes in the notes;
  - cutting back on a reload;
  - which chronicle lines a message carries;
  - both memories' messages and instructions;
  - when a story is due;
  - the replay's view of what is now quiet.
- The smoke test (118 checks since 0.6.4) covers the bridge's side:
  - the game's history first, marked;
  - notices, orders and refusals in the chronicle, and fights with bandits kept out;
  - a lord set free kept without a poke;
  - an enemy army sighted;
  - `poke.json`;
  - RECENT HISTORY from the chronicle;
  - the cut back on loading an earlier save, in both copies.

## Not yet seen in a game

- The game's own history read at a real load: how many lines, and how they read.
- The first story written from it.
- A reload of an earlier save cutting both memories back.
- Answer time with the memory added to each message.
