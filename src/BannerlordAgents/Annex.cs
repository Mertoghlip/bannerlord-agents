using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// One realm folded into another (0.19.6, Nov 16: a new test where Dol Guldur's strength is closer to mine, given all of Gundabad's lands and lords, its
    /// hired clans too). "agents.annex gundabad dolguldur" shows what it would move; with "go" it moves every clan of the first realm into the second, as the
    /// game's own actions move clans (ChangeKingdomAction): its armies broken up first (DisbandArmyAction); each hired clan taken into the new realm's pay at its
    /// own award (ApplyByJoinFactionAsMercenary); each of its own clans, the ruling clan last, joining with its lords, castles, towns, villages and gold
    /// (ApplyByJoinToKingdom keeps a clan's fiefs); then the emptied realm ended (DestroyKingdomAction, which ends only clans still in it, and none are), as the
    /// game itself ends a realm whose ruling clan has left (ClanVariablesCampaignBehavior). Its lords keep their parties and troops; the realm they join keeps
    /// its ruler, wars and allies. Never the player's realm, either way. Game's thread (a console command); save under a new name afterwards.
    /// </summary>
    internal static class Annex
    {
        public static string Run(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            args = args ?? new List<string>();
            bool go = args.Any(a => a.Equals("go", StringComparison.OrdinalIgnoreCase));
            List<string> ids = args.Where(a => !a.Equals("go", StringComparison.OrdinalIgnoreCase)).ToList();
            if (ids.Count < 2)
                return "Usage: agents.annex <realm to fold in> <realm it joins> [go], for example agents.annex gundabad dolguldur. Without go it only shows what it would move.";
            Kingdom from = Find(ids[0]), into = Find(ids[1]);
            if (from == null) return "No realm '" + ids[0] + "' (use its id, such as gundabad).";
            if (into == null) return "No realm '" + ids[1] + "' (use its id, such as dolguldur).";
            if (from == into) return "Those are the same realm.";
            if (from.IsEliminated) return Names.Faction(from) + " is already gone.";
            if (into.IsEliminated) return Names.Faction(into) + " is gone.";
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (from == mine || into == mine) return "This never folds the player's realm in or out.";

            List<Clan> own = from.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService).ToList();
            List<Clan> hired = from.Clans.Where(c => c != null && !c.IsEliminated && c.IsUnderMercenaryService).ToList();
            Clan ruling = from.RulingClan;
            var sb = new StringBuilder();
            sb.AppendLine(Names.Faction(from) + " (ruler " + (from.Leader?.Name?.ToString() ?? "?") + ") into " + Names.Faction(into) + " (ruler " + (into.Leader?.Name?.ToString() ?? "?") + "):");
            sb.AppendLine("  its own clans (" + own.Count + "):");
            foreach (Clan c in own.OrderByDescending(c => c == ruling).ThenByDescending(c => c.CurrentTotalStrength)) sb.AppendLine("  - " + ClanLine(c, c == ruling));
            sb.AppendLine("  its hired clans (" + hired.Count + ", into " + Names.Faction(into) + "'s pay):");
            foreach (Clan c in hired.OrderByDescending(c => c.CurrentTotalStrength)) sb.AppendLine("  - " + ClanLine(c, false));
            if (from.Armies.Count > 0) sb.AppendLine("  its armies, broken up first: " + string.Join(", ", from.Armies.Select(a => a.Name?.ToString() ?? a.LeaderParty?.Name?.ToString() ?? "an army")) + ".");
            sb.AppendLine("  " + Names.Faction(into) + " now: " + RealmLine(into) + "; " + Names.Faction(from) + ": " + RealmLine(from) + ".");

            if (!go)
                return "PREVIEW, nothing moved yet.\n" + sb + "Type agents.annex " + ids[0] + " " + ids[1] + " go to carry it out, then save under a new name.";

            var moved = new List<string>();
            var failed = new List<string>();
            foreach (Army a in from.Armies.ToList())
            {
                try { DisbandArmyAction.ApplyByUnknownReason(a); }
                catch (Exception e) { failed.Add("an army (" + e.Message + ")"); Diag.Swallowed("breaking up an army before folding a realm in", e); }
            }
            foreach (Clan c in hired)
            {
                try
                {
                    ChangeKingdomAction.ApplyByJoinFactionAsMercenary(c, into, default(CampaignTime), Math.Max(1, c.MercenaryAwardMultiplier), false);
                    if (c.Kingdom == into) moved.Add(c.StringId); else failed.Add(Names.Faction(c) + " (still with " + (c.Kingdom?.Name?.ToString() ?? "no realm") + ")");
                }
                catch (Exception e) { failed.Add(Names.Faction(c) + " (" + e.Message + ")"); Diag.Swallowed("moving a hired clan into another realm", e); }
            }
            foreach (Clan c in own.OrderBy(c => c == ruling ? 1 : 0))
            {
                try
                {
                    ChangeKingdomAction.ApplyByJoinToKingdom(c, into, default(CampaignTime), false);
                    if (c.Kingdom == into) moved.Add(c.StringId); else failed.Add(Names.Faction(c) + " (still with " + (c.Kingdom?.Name?.ToString() ?? "no realm") + ")");
                }
                catch (Exception e) { failed.Add(Names.Faction(c) + " (" + e.Message + ")"); Diag.Swallowed("moving a clan into another realm", e); }
            }
            string end;
            if (!from.IsEliminated && !from.Clans.Any(c => c != null && !c.IsEliminated))
            {
                try { DestroyKingdomAction.Apply(from); end = Names.Faction(from) + " is no more."; }
                catch (Exception e) { end = Names.Faction(from) + " is empty but could not be ended (" + e.Message + "); the game ends a realm whose ruling clan has left at its next daily reckoning."; Diag.Swallowed("ending an emptied realm", e); }
            }
            else end = Names.Faction(from) + " still has clans: " + string.Join(", ", from.Clans.Where(c => c != null && !c.IsEliminated).Select(c => Names.Faction(c))) + ".";
            int fiefsLeft = Settlement.All.Count(s => s.IsFortification && s.MapFaction == from);
            Diag.Event("annex", "carried out", new { from = from.StringId, into = into.StringId, moved, failed, fiefs_left = fiefsLeft, now = RealmLine(into) });
            Log.Line("annex: " + from.StringId + " into " + into.StringId + ": " + moved.Count + " clans moved, " + failed.Count + " failed; " + end);
            return (failed.Count == 0 ? "CARRIED OUT: " : "CARRIED OUT WITH PROBLEMS: ") + moved.Count + " clans of " + Names.Faction(from) + " now serve " + Names.Faction(into) + ". " + end
                   + (failed.Count > 0 ? " Not moved: " + string.Join("; ", failed) + "." : "") + (fiefsLeft > 0 ? " " + fiefsLeft + " castles or towns still show the old realm." : "")
                   + "\n  " + Names.Faction(into) + " now: " + RealmLine(into) + ".\nNow save under a new name.";
        }

        private static Kingdom Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            id = id.Trim();
            return Kingdom.All.FirstOrDefault(k => k.StringId.Equals(id, StringComparison.OrdinalIgnoreCase))
                   ?? Kingdom.All.FirstOrDefault(k => (k.Name?.ToString() ?? "").Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        private static string ClanLine(Clan c, bool ruling)
        {
            List<MobileParty> parties = c.WarPartyComponents.Select(w => w.MobileParty).Where(p => p != null && p.IsActive).ToList();
            float strength = parties.Sum(p => p.Party.EstimatedStrength);
            List<Settlement> fiefs = c.Settlements.Where(s => s.IsFortification).ToList();
            int villages = c.Settlements.Count(s => s.IsVillage);
            return Names.Faction(c) + (ruling ? " (the ruling clan)" : "") + ", led by " + (c.Leader?.Name?.ToString() ?? "?") + ": " + c.Heroes.Count(h => h.IsAlive && h.IsLord) + " lords, "
                   + parties.Count + " parties, strength " + Text.N((int)Math.Round(strength)) + ", gold " + Text.N(c.Leader?.Gold ?? 0)
                   + (fiefs.Count > 0 ? "; holds " + string.Join(", ", fiefs.Select(s => s.Name.ToString())) + (villages > 0 ? " and " + villages + " villages" : "") : "");
        }

        private static string RealmLine(Kingdom k)
        {
            List<MobileParty> parties = k.Clans.Where(c => c != null && !c.IsEliminated).SelectMany(c => c.WarPartyComponents).Select(w => w.MobileParty).Where(p => p != null && p.IsActive).ToList();
            int towns = Settlement.All.Count(s => s.IsTown && s.MapFaction == k), castles = Settlement.All.Count(s => s.IsCastle && s.MapFaction == k), villages = Settlement.All.Count(s => s.IsVillage && s.MapFaction == k);
            return parties.Count + " lord parties, strength " + Text.N((int)Math.Round(parties.Sum(p => p.Party.EstimatedStrength))) + ", " + towns + " towns, " + castles + " castles, " + villages + " villages";
        }
    }
}
