using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// Module 0.24.5 (Nov 24/25): the realms' lists of enemies put right after a load (docs/ENGINE-NOTES.md, "AI storms break at a reload").
    /// The game keeps each realm's enemies in a cached list (IFaction.FactionsAtWarWith), rebuilt when the game loads
    /// (CampaignFactionManagerBehaviour.OnGameLoaded) and at each war, peace or alliance call. Its war planner (AiMilitaryBehavior) looks for siege and
    /// raid targets only in that list. With TAOM a war can come from its diplomacy model (TaomDiplomacyModel.IsAtConstantWar, the War of the Ring), and
    /// at the moment the game rebuilds the lists that answer is not yet ready for every pair: measured Nov 24 (AiProbe, AUTOPILOT STORM REPRO), Dol
    /// Guldur's list came out of the load without the Kingdom of Arnor and Gondor (38 enemy castles and towns before the save, 18 after), so the planner
    /// offered its 26-lord army storming Torech Emel no military option, its leader chose a visit, and the game ended the storm and the siege. The list
    /// stayed short until the next war or peace touching Dol Guldur rebuilt it (1.5 to 2 days).
    /// Here, once the load has finished (the map state's OnGameLoadFinished, after every mod's session start), every kingdom's list is compared with
    /// the game's own test (IsAtWarWith) and all lists are rebuilt by the game's own method, as its load does; each in-game day the kingdoms' lists are
    /// checked again and a stale one rebuilt. What was wrong is logged (kind warlists). Game thread.
    /// </summary>
    internal static class WarLists
    {
        /// <summary>At the end of a load: what was stale among the kingdoms, then every kingdom's and clan's list rebuilt by the game's own method.</summary>
        internal static void AtLoad()
        {
            try
            {
                var wrong = Stale(out int kingdoms);
                foreach (Kingdom k in Kingdom.All) k.UpdateFactionsAtWarWith();
                foreach (Clan c in Clan.All) c.UpdateFactionsAtWarWith();
                Diag.Event("warlists", "at load", new { kingdoms, stale = wrong.Select(w => w.Value).ToList() });
                if (wrong.Count > 0) Log.Line("enemy lists: " + wrong.Count + " realms' lists of enemies were stale after the load and are rebuilt: " + string.Join("; ", wrong.Select(w => w.Value)));
            }
            catch (Exception e) { Diag.Swallowed("the realms' lists of enemies (at load)", e); }
        }

        /// <summary>Each in-game day: a kingdom whose list differs from the game's own test is rebuilt.</summary>
        internal static void Daily()
        {
            try
            {
                var wrong = Stale(out int kingdoms);
                if (wrong.Count == 0) return;
                foreach (IFaction f in wrong.Keys) f.UpdateFactionsAtWarWith();
                Diag.Event("warlists", "daily", new { kingdoms, stale = wrong.Select(w => w.Value).ToList() });
            }
            catch (Exception e) { Diag.Swallowed("the realms' lists of enemies (daily)", e); }
        }

        /// <summary>The kingdoms whose list of enemy kingdoms differs from the game's own test, each with what it lacks (+) and holds wrongly (-).</summary>
        private static Dictionary<IFaction, string> Stale(out int kingdoms)
        {
            var wrong = new Dictionary<IFaction, string>();
            var live = Kingdom.All.Where(k => !k.IsEliminated).ToList();
            kingdoms = live.Count;
            foreach (Kingdom k in live)
            {
                var have = new HashSet<IFaction>(k.FactionsAtWarWith);
                var plus = new List<string>();
                var minus = new List<string>();
                foreach (Kingdom o in live)
                {
                    if (o == k) continue;
                    bool war = k.IsAtWarWith(o);
                    if (war && !have.Contains(o)) plus.Add(o.StringId);
                    else if (!war && have.Contains(o)) minus.Add(o.StringId);
                }
                if (plus.Count > 0 || minus.Count > 0)
                    wrong[k] = k.StringId + ": " + string.Join(" ", plus.Select(x => "+" + x).Concat(minus.Select(x => "-" + x)));
            }
            return wrong;
        }
    }
}
