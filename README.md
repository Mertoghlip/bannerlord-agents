# Bannerlord Agents

Course project for CS 329M, Machine Programming (Stanford, autumn 2026).

Language-model agents command three allied realms, Mordor, Harad and Rhûn, in a *Mount & Blade II: Bannerlord* campaign running the TAOM Lord of the Rings pack. Each agent sees only what its realm's own parties and holdings could see. It is woken up the way a player would be: an enemy army comes into view, a holding is besieged, or a peace offer arrives. It answers with strategy and diplomacy, and small deterministic programs, the tools, carry out the moves between decisions: defend a holding, besiege a city, gather an army. The agents can also write tools of their own.

The study asks how much to leave to the model and how much to code. It also asks which model an everyday gamer could actually run: a frontier model (Opus 5.5 or Sonnet 5) on a personal subscription, a small model trained from it that runs on the player's own graphics card, or a mix of the two.

**Status:** proposal submitted Oct 6 · checkpoint Tue Oct 27 · final report and talk Tue Dec 1. The build plan and the to-do list are in [docs/PLAN.md](docs/PLAN.md).

## What's here

| Folder | What it holds |
|---|---|
| `src/BannerlordAgents/` | The bridge, the game module that runs inside the game. It is its own module, separate from TFAOMFixes. |
| `scripts/` | `deploy.ps1` builds the module and installs it into the game. It refuses while the game is open. |
| `docs/` | The build plan, the hours log, and the proposal. |

More arrives as the plan reaches it: `protocol/` (message formats, this week), `src/BannerlordAgents.Core/` (the shared core and the tool engine, week 2), `src/BannerlordAgents.WarRoom/` (the program beside the game where the agents decide, week 3), and `training/` and `analysis/` (Python, November).

## Setting up on a computer

1. Install Git and the .NET SDK, version 8 or newer.
2. Install Bannerlord **1.4.8** and **TAOM 2.0.26**. TAOM needs exactly that game version.
3. Clone the repository: `git clone https://github.com/Mertoghlip/bannerlord-agents.git`
4. If the game is not in Steam's default folder, copy `GamePath.props.example` to `GamePath.props` and set your game folder in it.
5. Build with `dotnet build -c Release` in the repository folder. The ready-to-install module lands in `build/Modules/BannerlordAgents`.
6. With the game closed, install it: `powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1`
7. In the launcher, tick **Bannerlord Agents** only in the experiment module list (decision 2 in the plan).

Nothing from the game itself is stored here. The build reads the game's own files from your install.

## Where the module writes

Its log is `%LOCALAPPDATA%\BannerlordAgents\logs\bridge.log`, never the Documents folder, which OneDrive syncs. In the game's console (Alt + ~), `agents.status` reports what the module is doing.
