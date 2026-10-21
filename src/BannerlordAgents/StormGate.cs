using System;
using System.Collections.Concurrent;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace BannerlordAgents
{
    /// <summary>
    /// The agent's storm rule for one of its sieges (0.7.4; Mert, Oct 20: "give the AI a tool to dictate siege decisions,
    /// starting with the storm time ... gated on storm immediately, storm after x siege engines constructed etc."), set by
    /// besiege's storm settings and its relief bars, and read on the game's thread by the storm hook.
    /// </summary>
    internal sealed class StormRule
    {
        /// <summary>0 the game's own commander decides; 1 storm once the camp is ready, Engines are built and the besiegers are Ratio times the defenders; 2 do not storm.</summary>
        public int Mode;
        public int Engines;
        public float Ratio;
        /// <summary>A relief bar was met: storm as soon as the camp may (its preparations done).</summary>
        public bool Now;

        public bool Same(StormRule o) => o != null && o.Mode == Mode && o.Engines == Engines && Math.Abs(o.Ratio - Ratio) < 1e-4f && o.Now == Now;

        public override string ToString() => Now ? "storm as soon as the camp may (a relief bar was met)"
            : Mode == 1 ? "storm once ready" + (Engines > 0 ? ", " + Engines + (Engines == 1 ? " engine" : " engines") + " built" : "") + (Ratio > 0 ? ", the besiegers " + Ratio.ToString("0.#", Text.Inv) + " times the defenders" : "")
            : Mode == 2 ? "do not storm" : "the game's own commander decides";
    }

    /// <summary>
    /// The storm rules in force, by the besieging lord's party (the siege camp's leader). Written by the tools' worker each hour
    /// (StormRule objects are never changed once handed over), read on the game's thread inside the game's storm test
    /// (BesiegerCamp.StartingAssaultOnBesiegedSettlementIsLogical, which the camp leader's AI asks about three times an hour
    /// once the camp's preparations are done; GameHooks.StormPatch). A siege with no rule here, or rule 0, is left to the
    /// game's own test untouched. Nothing here lets a camp storm before its preparations are done: the game asks only then,
    /// as it holds the player to it too.
    /// </summary>
    internal static class StormGate
    {
        private static readonly ConcurrentDictionary<string, StormRule> Rules = new ConcurrentDictionary<string, StormRule>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, bool> Last = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Sets (or with null or rule 0, clears) a party's rule; a null party clears every rule.</summary>
        public static void Set(string party, StormRule r)
        {
            if (party == null) { Rules.Clear(); Last.Clear(); return; }
            if (r == null || (r.Mode == 0 && !r.Now)) { Rules.TryRemove(party, out _); Last.TryRemove(party, out _); return; }
            if (Rules.TryGetValue(party, out StormRule old) && old.Same(r)) return;
            Rules[party] = r;
            Diag.Event("storm", "rule", new { party, rule = r.ToString() });
        }

        public static StormRule For(string party) => party != null && Rules.TryGetValue(party, out StormRule r) ? r : null;
        public static int Count => Rules.Count;

        /// <summary>Game's thread, from the hook: null leaves the decision to the game's own test; else whether to storm now.</summary>
        public static bool? Decide(BesiegerCamp camp)
        {
            if (Rules.IsEmpty) return null;
            string leader = camp?.LeaderParty?.StringId;
            StormRule r = For(leader);
            if (r == null) return null;
            bool storm;
            string why;
            if (r.Now) { storm = true; why = "a relief bar was met"; }
            else if (r.Mode == 2) { storm = false; why = "your rule: do not storm"; }
            else if (r.Mode != 1) return null;
            else
            {
                Settlement s = camp.SiegeEvent?.BesiegedSettlement;
                int built = s != null ? Campaign.Current.Models.CombatSimulationModel.GetNumberOfEquipmentsBuilt(s) : 0;
                if (built < r.Engines) { storm = false; why = built + " of " + r.Engines + " engines built"; }
                else if (r.Ratio > 0 && s != null)
                {
                    // the strengths as the game's own test counts them (parties inside not shy of a fight, the militia, the garrison)
                    float d = 0, a = 0;
                    float half = MobileParty.MainParty?.CurrentSettlement == s ? 0.5f : 1f;
                    foreach (PartyBase x in s.GetInvolvedPartiesForEventType())
                        if (x.IsMobile && x.MobileParty.CurrentSettlement == s && (x.MobileParty.Aggressiveness > 0.01f || x.MobileParty.IsMilitia || x.MobileParty.IsGarrison))
                            d += half * x.CalculateCurrentStrength();
                    foreach (PartyBase x in camp.GetInvolvedPartiesForEventType()) a += x.CalculateCurrentStrength();
                    storm = d <= 0 || a / d >= r.Ratio;
                    why = "besiegers " + Math.Round(a) + " against defenders " + Math.Round(d) + " (the bar: " + r.Ratio.ToString("0.#", Text.Inv) + " times)";
                }
                else { storm = true; why = built + " engines built"; }
            }
            if (!Last.TryGetValue(leader, out bool was) || was != storm)
            {
                Last[leader] = storm;
                Diag.Event("storm", storm ? "storm" : "hold", new { party = leader, rule = r.ToString(), why });
                if (storm) Log.Line("storm rule: " + Names.Party(camp.LeaderParty) + " storms " + Names.Settlement(camp.SiegeEvent?.BesiegedSettlement) + " (" + r + "; " + why + ")");
            }
            return storm;
        }
    }
}
