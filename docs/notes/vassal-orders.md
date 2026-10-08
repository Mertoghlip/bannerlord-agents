# Vassal orders without Party AI Controls (checked Oct 7, 2026)

**Question (Mert):** can orders to vassals go straight to the lord, through the same commands the agents and their tools use, instead of through another mod?

**Answer: yes.** Five of the six dialogue orders map onto the bridge's own orders today. The sixth, patrolling one's own lands, is a multi-step behavior, which is a tool (week 2).

## What happens today

1. The dialogue line "I have orders for you" (TFAOMFixes, `VassalOrders.cs`) stores an order on Party AI Controls' settings for that lord.
2. Party AI Controls 1.4.5 (decompiled from the installed `PartyAIUpdated.dll`, `PartyAIThinker`) then drives the lord every hour with the game's own commands:

| Dialogue order | What Party AI Controls calls | The bridge's order |
|---|---|---|
| Wait in one of our fiefs | stop-deciding switch on, then `SetPartyAiAction.GetActionForVisitingSettlement` | `garrison` |
| Defend one of our fiefs | stop-deciding switch on, then `GetActionForDefendingSettlement` (or visit) | `defend` |
| Patrol around one of our fiefs | adds weighted patrol choices to the lord's own hourly decision (`PartyThinkParams`), plus some defend and visit commands | `patrol` (one direct command instead) |
| Patrol your own lands | the same weighted choices, spread over the clan's fiefs | a tool, week 2 |
| Go and fill your ranks | visits a settlement with volunteers; its own recruiter buys | `recruit` (go to a town or village and wait; the game's own recruiting buys) |
| Act as you see fit | stop-deciding switch off | `free` / `agents.release` |

## What the game itself does (decompiled 1.4.8)

- **The stop-deciding switch** is honoured in exactly one place: the hourly decision step, `AiPartyThinkBehavior.PartyHourlyAiTick`, which skips the party. The game's own quests use the same switch to script bandits and caravans. Nothing restricts it to one realm or another, so it works the same on the player's vassals and on orc lords.
- **The move commands** (`SetPartyAiAction.*`, `MobileParty.SetMove*`) have no faction checks.
- **Recruiting:** `RecruitmentCampaignBehavior.HourlyTickParty` recruits for any lord's party sitting in an unraided village or in a town not under siege, every hour, whatever the switch says. So "go to a town and wait" is enough. Castles never give recruits.
- **Armies:** a lord holding an order can still be called into another lord's army. While the switch is on, he does not walk over, because walking over is part of the skipped decision step. Once he does join, the bridge ends his order, as Party AI Controls does (`OnPartyJoinedArmy`).
- **One switch, one owner:** if Party AI Controls and the bridge both held an order for the same lord, they would fight every hour. `agents.vassal` refuses a lord who still holds a dialogue order.

## What is built now (module 0.2.0)

`agents.vassal <lord> <verb> <target>` orders one of the player's own lords directly. It uses the same verbs and checks as the agents' orders, and the recorder records it as the player's decision (`vassal_order`, with `lord` and `verb` fields). It is ready to verify in the game.

## Still open

- Whether the dialogue moves onto this path: a dialogue line in Bannerlord Agents, with TFAOMFixes' line retired. TFAOMFixes' feature has no off switch today, only `tfaomfix.vassal_orders clear`, which releases everyone.
- A patrol-own-lands tool (week 2).
