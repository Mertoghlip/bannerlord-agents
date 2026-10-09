# Second live session: Opus commands the orcs, with tools and memory (Oct 9)

**When:** Thursday Oct 9, 11:42 to 11:51 (the game's clock ran from about 11:47 to 11:49). Version 0.5.0, the war room in live mode for every orc lord, Opus 5.5 at high effort.

**Save:** Mert loaded the save of Summer 9, 1087, 09:00, the same starting point as the first session (Oct 8). Mert then marched an army on Western Moria.

## What happened

**The load:**
- The game's own history held 4,533 entries; 318 were about the orcs. They were read in 56 ms on the game's thread and started the orcs' chronicle.
- The freeze probe ran once, on the loading screen (31 s, the game reading its XML), as designed. None of our code was running.
- The self-checks passed: 9 of 9 on the game's side, and all on the helper's.

**The answers:**

| Poke | In-game hour (Summer 9) | Why | Answer time | Orders |
|---|---|---|---|---|
| #1 | 09:00 | first look | 23.5 s | 3 |
| #2 | 13:00 | enemy lord near Grafton | 16.1 s | 4 |
| #3 | 15:00 | Arnor-Gondor lords heading for Eastern Moria | 15.5 s | 2 |
| #6 | 18:00 | more lords gathering at Eastern Moria | 9.9 s | 1 |
| #7 | 19:00 | Mert's army in view, 5,528 men, 48 from Western Moria | 29.2 s (4 look-ups) | 1 |
| #9 | 22:00 | Elendara taken prisoner by Radbug; Ulga's army | 8.4 s | 1 |

- **Answer time:** a median of about 16 s, against 53 s for the agent chat on Oct 8. Pokes #4, #5 and #8 came during calls; the next poke's WHAT CHANGED carried them.
- **Orders:** 10 carried out, 2 refused, 4 waiting for the clock when the game was paused and then closed.

**The answer to Mert's march:**
- The agent judged the army far too strong (strength about 14,700 against its own armies of about 600 men each).
- It ordered Ulga's new army out of Western Moria to Methedras, beside Vrakdush's army, to relieve the town later instead of being trapped and captured inside it.
- That reasoning came from its memory: from the chronicle's record of the spring's mass captures, it noted "Do not shut lords inside a town that an overwhelming army is coming for."
- Both orders arrived while the game was paused, so they were never carried out before the game closed.

**Elsewhere:**
- Morzash held Starkmoore and struck the Rohan raider Éomund.
- Naglur held Hiltbolt.
- Othrod sheltered in Gramburg from a stronger Rohan lord (the safety rule, seen in a real game for the first time).

## The memory

- **The old notes:** the old journal became the campaign's notes at the first poke. They described Summer 17 of the Oct 8 run, a future that had not happened in this one. The agent noticed it by itself, rewrote its plan for the present, and noted "Trust the current poke over carried-over notes."
- **The story so far:** written at the first quiet moment, 550 words in 14 s, from 346 chronicle lines. It covers:
  - the alliances since 1084;
  - the lords captured on both sides (18, then 27 orc lords taken by the Kingdom of Arnor and Gondor in spring);
  - Hollin Gate falling to Aragorn on Summer 9;
  - Rohan's raids;
  - the day's orders, and the threats still open.
- **The notes** kept a line on each enemy: Aragorn, the Rohan host at Wealdham, and two Arnor-Gondor lords near Moria.

## What went wrong, and the fixes (version 0.5.1, installed 11:55)

| Seen | Cause | Fix |
|---|---|---|
| "Vrakdush's army broke up before it formed", and the same for Ulga, although both armies had formed | The army order checked the map snapshot from the start of that hour, before the army existed. Its next order (defend) then started at once. | The army is judged from the next hour on. If an army truly breaks up, its next order no longer starts (a new "fail" ending), and its lord is set free. |
| An army refused whole because one of the four lords called was in a battle | Every called lord had to be free | The army forms with the lords who can come; the receipt names those left out and why |
| The chronicle named the game's own meeting place for the agent's armies (Celebdil for Methedras) | The game announces a new army before the order sets its meeting place | The order's place is used |
| Poke numbers began again at #1 | The campaign's poke file holds a whole report per line, longer than the end of the file that was read | The highest number is read from the whole file once, then kept beside it; the next poke is #126 |
| On reloading the Summer 9 save, the old Summer 17 notes would come back | The notes taken over from the old journal were kept as valid at any date | They hold from the first poke of this memory; a save from before it starts afresh |

**Not fixed yet:**
- **The game's thread:** 39 ms at the first hourly copy after the load, and 28 ms applying the first orders, which a player can feel. The hourly copy then took about 5 to 7 ms; the aim is under 1 ms (open since Oct 8).
- **A long pause:** orders that arrive while the game is paused wait for the clock. A paused game leaves the agent's answers waiting with it, by design.

## Checks

- The smoke test grew to 81 checks; the war room's self-test to 28.
- 327 game references, 0 missing.
- The installed DLL has sha256 `384C41BEBCA7AAA9…`.
