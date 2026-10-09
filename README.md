# Bannerlord Agents

Course project for CS 329M, Machine Programming (Stanford, autumn 2026).

Language-model agents command three allied realms, Mordor, Harad and Rhûn, in a *Mount & Blade II: Bannerlord* campaign running the TAOM Lord of the Rings pack. Each agent sees only what its realm's own parties and holdings could see. It is woken up the way a player would be: an enemy army comes into view, a holding is besieged, or a peace offer arrives. It answers with strategy and diplomacy, and small deterministic programs, the tools, carry out the moves between decisions: defend a holding, besiege a city, gather an army. The agents can also write tools of their own. A recorder logs how Mert plays on the campaign map, in the same format. That data is used to train the agents and to steer them.

The study asks how much to leave to the model and how much to code. It also asks which model an everyday gamer could actually run: a frontier model (Opus 5.5 or Sonnet 5) on a personal subscription, a small model trained from it that runs on the player's own graphics card, or a mix of the two.

**Status:** proposal submitted Oct 6 · checkpoint Tue Oct 27 · final report and talk Tue Dec 1. The build plan and the to-do list are in [docs/PLAN.md](docs/PLAN.md).

## What's here

| Folder | What it holds |
|---|---|
| `src/BannerlordAgents/` | The bridge, the game module that runs inside the game. It is its own module, separate from TFAOMFixes. Its `Tools/` folder holds the tool engine and the order library ([docs/TOOLS.md](docs/TOOLS.md)). |
| `src/BannerlordAgents.WarRoom/` | The war room, the program beside the game where the agent decides: one short Claude Code call per poke ([docs/WARROOM.md](docs/WARROOM.md)). |
| `tests/SmokeTest/` | Runs the bridge's background half, the tools included, on a small made-up world outside the game. Mistakes surface there before they cost an in-game test. |
| `protocol/` | Every file the module writes, and the fields in each record. |
| `agent/` | Templates for the agent's own folder, which lives outside this repository so the agent cannot read the code. `scripts/setup-agent.ps1` installs them. |
| `tools/FreezeProbe/` | A small program outside the game that says where the game is stuck when it freezes. The module starts it by itself; it is installed in the module's `probe` folder. |
| `scripts/` | `deploy.ps1` builds everything, runs the smoke test, and installs the module into the game; it refuses while the game is open or if a check fails. `probe.ps1` runs the freeze probe by hand. `setup-agent.ps1` prepares the agent's folder, and `warroom.ps1` starts the war room. |
| `docs/` | The build plan, the first tests phase by phase, the tools, the war room, the logs ([LOGGING.md](docs/LOGGING.md)), every change with its notes ([CHANGELOG.md](docs/CHANGELOG.md)), the hours log, the session reports, and the proposal. |

More arrives as the plan reaches it:
- `src/BannerlordAgents.Core/`: the shared core, when the tool engine is needed outside the game too.
- `training/` and `analysis/`: Python, in November.

## Setting up on a computer

1. Install Git and the .NET SDK, version 8 or newer.
2. Install Bannerlord **1.4.8** and **TAOM 2.0.26**. TAOM needs exactly that game version.
3. Clone the repository: `git clone https://github.com/Mertoghlip/bannerlord-agents.git`
4. If the game is not in Steam's default folder, copy `GamePath.props.example` to `GamePath.props` and set your game folder in it.
5. Build with `dotnet build -c Release` in the repository folder. The ready-to-install module lands in `build/Modules/BannerlordAgents`.
6. With the game closed, install it: `powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1`
7. In the launcher, tick **Bannerlord Agents** only in the experiment module list (decision 2 in the plan).
8. For an agent: `powershell -ExecutionPolicy Bypass -File scripts\setup-agent.ps1 -Mode shadow` once, then `scripts\warroom.ps1` beside the game. It needs Claude Code signed in to your subscription ([docs/WARROOM.md](docs/WARROOM.md)).

Nothing from the game itself is stored here. The build reads the game's own files from your install.

## Where the module writes

Everything goes under `%LOCALAPPDATA%\BannerlordAgents`, never the Documents folder, which OneDrive syncs. That covers its log, its settings, the performance log, one folder per game launch for the detailed logs, and one folder per campaign for the reports, pokes and recordings. [protocol/README.md](protocol/README.md) lists every file. In the game's console (Alt + ~), `agents.status` reports what the module is doing, `agents.health` lists any alarms, and `agents.help` lists the commands.
