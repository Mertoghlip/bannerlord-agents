# Diplomacy: what the agents can do with other realms (since 0.13)

An agent that rules a realm makes its diplomacy by the game's own rules and costs, and the pack's (the War of the Ring). It can propose war, peace with a tribute either way, an alliance, a call to war, a trade agreement, a policy or a clan's expulsion; hire and let go mercenary clans; vote in its realm's votes and decide them as ruler; answer the offers other realms make it; and write letters and joint plans to its allies. Everything is recorded. The plan is [PLAN-0.13.md](PLAN-0.13.md) Part B; the agents' instructions have the sections "Diplomacy" and "Allies".

## What the game does, and why we hold votes (1.4.8, read in its code)

- **A realm the game runs settles each vote the moment it is proposed** (`Kingdom.AddDecision` starts the election at once; `KingdomElection.StartElection`). Each clan's support is counted by the game's own scores (`DetermineSupportOption`), and the ruler may override the clans at random when his own view differs a lot (`GetAiChoice`), paying the game's price (`HandleInfluenceCosts`, `ClanPoliticsModel.GetInfluenceRequiredToOverrideKingdomDecision`). Only the player's own realm keeps its votes open (48 hours) for the player.
- **Peace, an alliance, a call to war and a trade agreement between two such realms** are made by the proposer's vote alone (`MakePeaceAction.ApplyByKingdomDecision`, `StartAlliance`, `StartCallToWarAgreement`, `MakeTradeAgreement`); only the player's realm is ever asked, through the offer windows (`OnShowDecision` returns false and shows `PeaceOfferMapNotification` and the others).
- **The pack (TAOM 2.0.26)** blocks peace between realms of hostile tiers while the War of the Ring rages (a prefix on `MakePeaceAction.ApplyInternal`), some wars (`DeclareWarAction.ApplyInternal`) and the end of alliances bound for good. TFAOMFixes' ring wars copy Gondor's wars into the player's realm. These rules stay (Mert, Oct 28).

## The acts (`Diplomacy.cs`, an answer's `diplomacy`)

| Act | What it names | What happens |
|---|---|---|
| `propose_war` | `with` | the game's DeclareWarDecision in the realm |
| `propose_peace` | `with`, `tribute` (a day: above 0 we pay, below 0 they pay), `days` (100 by default with a tribute) | MakePeaceKingdomDecision |
| `propose_alliance` | `with` | StartAllianceDecision |
| `call_to_war` | `with` (an ally), `against` | ProposeCallToWarAgreementDecision |
| `propose_trade` | `with` | TradeAgreementDecision |
| `propose_policy` | `policy`, `abolish` | KingdomPolicyDecision (adopt, or end one in force) |
| `propose_expel` | `clan` | ExpelClanFromKingdomDecision |
| `hire_mercenary` | `clan` | `ChangeKingdomAction.ApplyByJoinFactionAsMercenary` with the game's award, only if the clan values the game's offer above 0 (`MercenaryJoinKingdomBarterable`) |
| `release_mercenary` | `clan` | `ApplyByLeaveKingdomAsMercenary`, once its contract allows (`ShouldStayInKingdomUntil`) |
| `vote` | `vote`, `option`, `weight` (slight, strong, full) | the ruler's clan's support in a held vote |
| `decide` | `vote`, `option` | settles a held vote at once, as ruler |
| `accept_peace`, `decline_peace` (and `_alliance`, `_call`, `_trade`) | `offer` or `with` | answers a held offer |

- **A proposal** is the game's own vote in the realm, the ruler's clan its proposer at the game's influence price (`GetInfluenceCost`), settled at once: the ruler's clan supports it fully, the other clans vote as the game has them vote, and the outcome they support most stands. `"overrule": true` decides it the agent's way even against them, at the game's price for overruling. The game's own conditions are checked first (`IsAllowed`, `CanMakeDecision`).
- **Checked twice:** on the helper, against the realm's screens as last copied (`DiploRules.Check`, which needs no game: the act known, the stance it needs, the War of the Ring, the influence, the vote or offer open, the clan in the realm or for hire); refused there with the reason, or checked only in shadow mode. Then on the game's thread against a fresh copy and the game's own conditions, and carried out.
- **Receipts:** each act gets a line in `receipts.log` ("diplomacy 1 (propose_peace dale): APPLIED. proposed (100 influence): Make Peace With Dale; the clans voted at once and decided: Yes."), the orders' record, the episode index and the diplomacy book.

## The agent's say in its realm's votes

- **Held votes:** in a realm a live agent rules, a vote its other clans propose is held where the game would announce its result (a Harmony prefix on `KingdomDecision.OnShowDecision` and its four overrides, `ShowPatch`), with the clans' support already counted, for up to 12 in-game hours (`Diplomacy.VoteHours`). The agent is poked ("A vote is open in your realm ... [v3]"), again three hours before it closes if it has not voted, and when it is settled.
- **The ruler's choice** (a prefix on the private `KingdomElection.GetAiChoice`, `ChoicePatch`; 100 bytes and more of code, so never folded into its callers, and its size is logged at start): in an agent's realm, its vote is counted as its clan's support (lowered to what its influence pays for), and its decision taken if it gave one, else the outcome the clans support most. Never the game's own ruler's whim. With no vote its clan abstains.
- **Settling:** a held vote is resumed on the very election the game began (its outcomes and supporters kept), through the game's own `GetAiChoice` and `ApplyChosenOutcome` (by reflection), so its influence costs, its relations and its events are the game's.
- **Your kingdom** while an agent rules it (C runs): the game holds its votes itself; they are listed and settled the same way, at the same 12 hours.
- **Not in the save:** a held vote lives only in the bridge. A save taken while one is held does not have it; loaded again, the proposal is gone (its proposer's influence was spent, as at any proposal).

## Offers to an agent's realm

- An offer another realm chose to make (peace, an alliance, a call to war, a trade agreement whose vote said yes) to a realm a live agent rules is held for up to 12 hours (`Diplomacy.OfferHours`; the game's own window for the player lasts a day) and poked: "Dale offers peace [o2]: they pay you 40 a day for 100 days. Answer by ...". Accepted, the game applies it as it would have (`ApplyChosenOutcome`); declined, it is dropped, as the player's decline drops it; with no answer it is accepted, as the game does between the realms it runs.
- A peace the War of the Ring would block anyway is not offered: the game's own way follows, and the pack stops it.
- A "no" to your kingdom while an agent rules it no longer opens the player's window.
- A peace, alliance or call-to-war offer calls a council (the realm councils' `council_after`, since Oct 28).

## What the agent reads (`DiploRules.Diplomacy`, `DiploRules.Open`)

Copied hourly on the game's thread for each realm an agent serves (`Diplomacy.Capture`, `Snapshot.Diplomacy`):
- **OPEN VOTES AND OFFERS:** each vote's id, title, proposer, when it closes, its options with the clans' support so far and what deciding each against them costs, the agent's vote and decision; each offer's id, terms and when it lapses.
- **DIPLOMACY:** the ruler clan's influence and what each act costs; the War of the Ring when it rages; every realm's stance (war since when, men lost each way, holdings taken, villages raided, a call to war either way, "no peace"; peace since when, "no war"; an alliance, bound for good or not), tribute, trade agreements, lords and strength; the realm's clans (to expel), mercenaries in its pay (their award and contract) and those for hire; the policies in force and the others.
- **ALLIES AND JOINT PLANS** (below).

## Allies: letters and joint plans (`Allies.cs`, an answer's `letters`)

- **Who:** an agent writes to an allied realm (an alliance) whose ruler reads letters: its own agent switched on, or, for your realm, the lieutenant. A letter to a realm not allied, or one whose ruler is the game's own, is refused with that reason.
- **A letter** is a few lines (at most 2,000 characters), delivered as a notice in the ally's next poke ("A letter from the ruler of Misty Mountain Orcs [orcs] (l1): ..."), into your lieutenant's window for your realm. It binds no one and carries no orders; an ally knows only what it is told.
- **Joint plans:** a letter may carry `plan` (`what`, `where`, `when`, `parts` by realm), which gets an id (jp1); later letters change its state with `plan_id` and `state`: `agreed` or `declined` only by the realm it was proposed to while proposed; `under way`, `done`, `dropped` by either. Plans still open show in both realms' reports.
- **Kept** in the campaign's `allies.jsonl`, each line labelled with its session (`LabelledLines`), so a load keeps the letters and plans of its save's history.

## Your lieutenant (A and B)

Its proposals carry diplomatic acts and letters beside its orders, under its yes rule (on your words or your Approve). For your realm a proposal is added to your kingdom's votes and waits for your decision in the kingdom screen; the game's own windows bring you the offers made to your realm. Your allies' letters come into its window ([LIEUTENANT.md](LIEUTENANT.md)).

## The record

- **`diplomacy.jsonl`** (the campaign's folder, `DiploBook`): every act and its receipt (`act`), each vote held (`vote_held`), voted (`voted`), decided (`decided`), concluded (`concluded`, the game's event) or cancelled; each offer held, accepted, declined or lapsed; each proposal (`proposed`, with its cost); mercenaries hired and released; every letter and every change of a plan. Every line has its realm, run, arm and in-game hour.
- **`episodes.jsonl`:** one line per act (`diplomacy`) and per letter (`letter`), with its answer's poke and file.
- **`allies.jsonl`:** the letters and plans as kept.

## Checks

- Smoke test: an act read as written; every act's checks against a made-up realm's screens; the two report sections; an answer's acts refused, checked in shadow mode and handed to the game's thread when live; the book and the index; the allied channel end to end (Allies013).
- In the game only (the shake-down): holding and settling votes, offers and their answers, the ruler's choice, mercenaries, the War of the Ring's answers. The self-check looks for the vote's private parts by name at start (`Diplomacy.PartsProblem`).
