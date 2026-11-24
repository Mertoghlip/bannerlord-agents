# The facts-only instructions: the base model on its own

Since Nov 24 (war room 0.20.5). A second set of instructions for the realm agents that tells them how the game works and how to give orders, and nothing about how to fight. The test it is for asks how the base model adapts to the game on its own, set against the runs on the doctrine (docs/DOCTRINE.md). The proposal I decided from: https://claude.ai/artifact/K5CnbyRNvRNci4apTKeQv7.

## The rule

One test sorts every line of the doctrine instructions: would it still be true if the agent did the opposite?

- **Kept:** what the game does, read in its code or measured in play, and the interface (how messages arrive, how to answer, the orders, the questions, the files, the watches).
- **Rewritten to facts:** lines that mixed a fact with advice. "Your orders land hours late" stays; "so never steer lords wake by wake" goes.
- **Removed:** the persona, all 33 principles, "Where your attention goes", "Command through orders", "Following your own orders", "Learning from what happens", and the strategy review's seven steps.

## What is in it

`agent/facts-only/warroom-system.template.md` holds the whole set, in the order a files agent reads it: the opening lines, Your goal (word for word), What the game does (new: the game facts that were inside the principles, and the besieger's sally, which the doctrine set never stated), Strategy reviews (when one sits and where the realm's picture is), How you are asked, Your memory (with "your notes carry over between conversations; what you keep in them is up to you"), How you answer, Writing your own tools (the manual, its examples kept, its opening framing gone), Checking a tool, Diplomacy, Allies, Look-ups, Asking the way, Recruiting, Money, The human player (his measured facts only: battles on the spot, his worth about a quarter more than his strength, his record), How battles are joined (word for word), Time, Rules, Watches (the offensive example gone), Your handover, Your files. The continuous layer adds nothing and the files layer only points the tools manual at the Orders file, as for every files agent. It is generated from the doctrine templates by `scripts/make-facts-only.py`, which copies kept sections word for word and asserts every edited sentence is still there, so a template change that breaks it fails loudly. As a files agent reads it, about 39,000 characters plus the order library's pointer, against about 91,000.

## Outside the instructions

- **The "chances to strike" page** picks out lone enemy lords by the doctrine's rule. `chances_page = off` in warroom.txt keeps it off the shelf (`Shelf.NoChancesPage`).
- **The relieve order** lost its one line of advice ("Best with an army, for example gather, then relieve."), for every agent.
- **Kept as they are:** the orders and their default settings (the agent's way of acting on the game), each holding's border numbers (facts), the wake budget (a cost limit).

## Switching an agent

`scripts\setup-agent.ps1 -Workspace <folder> -Instructions facts` puts the three files in over the folder's own (each old one kept beside it as `.bak-<time>-before-facts`, the realm's title and opening line carried over) and sets `chances_page = off`; `-Instructions doctrine` puts the doctrine set back and the page on. Restart the war room afterwards: it reads warroom.txt only when it starts. A conversation that carries on is told YOUR INSTRUCTIONS CHANGED, so start a test's switch from a new run.

## Checks

`WarRoom selftest`: the facts-only set, laid out as a files agent reads it, has no persona, principle, priority or advice words, has the new facts and the neutral notes line, keeps the tools manual for the Orders page, stays under 50,000 characters, and with `human_player = none` says nobody plays in person and drops the human player's record; the shelf with `chances_page = off` keeps no chances page.
