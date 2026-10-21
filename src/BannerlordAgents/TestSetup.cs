using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// The fairer, longer test save (0.7.4; Mert, Oct 20: "a state where I own all the land from Rivendell to Mount Gram to Mount
    /// Gundabad, and the Misty Mountain Orcs take back the rest of the land that I own in the mountains. The new fiefs they get
    /// should be distributed reasonably amongst them. Further, all of the prisoner Misty Mountain commanders should be returned
    /// to their realm."; the line chosen: only the old orc fortresses, their garrisons swapped to orc troops).
    /// "agents.rebalance" shows what it would do; "agents.rebalance apply" does it, on whatever save is loaded; save afterwards.
    /// - Every lord of the orcs held prisoner anywhere is released (EndCaptivityAction.ApplyByPeace: the game's own routines bring
    ///   each back with a party in time).
    /// - Each fortress (with its villages, which follow it) passes from the player's realm to an orc clan
    ///   (ChangeOwnerOfSettlementAction.ApplyByDefault, as a treaty does): a siege of it is ended first; the parties inside at war
    ///   with the orcs are moved out; towns first, each to the clan with the fewest castles and towns, then the higher tier, then
    ///   the greater renown, no clan given two while another has fewer (mercenary bands cannot hold land); its garrison becomes
    ///   orc troops, man for man, tier for tier (by kind: foot, archers, horse, where the orcs have them); orc soldiers in its
    ///   prison join the garrison.
    /// - It will not run while a battle is being fought at one of the places or their villages, or with the player's party inside one.
    /// </summary>
    internal static class TestSetup
    {
        public const string OrcRealm = "mistymountainorcs";
        /// <summary>Sirannon, Nanduhirion, Caradhras, Hollin Gate, Eastern Moria, Western Moria (added Oct 20, late): the old orc fortresses south of Rivendell.</summary>
        public static readonly string[] OrcFortresses = { "castle_MM7", "town_MM3", "castle_MM1", "castle_MM6", "town_MM1", "town_MM2" };

        public static string Rebalance(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            bool apply = args != null && args.Count > 0 && args[0].Equals("apply", StringComparison.OrdinalIgnoreCase);
            List<string> ids = (args ?? new List<string>()).Where(a => !a.Equals("apply", StringComparison.OrdinalIgnoreCase)).ToList();
            if (ids.Count == 0) ids = OrcFortresses.ToList();
            Kingdom orcs = Kingdom.All.FirstOrDefault(k => k.StringId == OrcRealm);
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (orcs == null || orcs.IsEliminated) return "The Misty Mountain Orcs are not in this campaign.";
            if (mine == null) return "Your clan is in no kingdom.";

            var sb = new StringBuilder(apply ? "REBALANCE, CARRIED OUT\n" : "REBALANCE, WHAT IT WOULD DO (\"agents.rebalance apply\" does it; save afterwards)\n");
            var problems = new List<string>();

            // the places
            var places = new List<Settlement>();
            foreach (string id in ids)
            {
                Settlement s = Settlement.Find(id);
                if (s == null || !s.IsFortification) { problems.Add(id + " is no castle or town"); continue; }
                if (s.MapFaction != mine) { sb.AppendLine("- " + s.Name + " is not your realm's (" + s.MapFaction?.Name + "); left as it is."); continue; }
                foreach (Settlement x in new[] { s }.Concat(s.BoundVillages.Select(v => v.Settlement)))
                    if (x.Party?.MapEvent != null) problems.Add("a battle is being fought at " + x.Name + "; wait until it ends");
                if (MobileParty.MainParty.CurrentSettlement == s) problems.Add("your party is inside " + s.Name + "; leave it first");
                places.Add(s);
            }

            // the prisoners
            List<Hero> captives = Hero.AllAliveHeroes.Where(h => h.IsPrisoner && h.MapFaction == orcs && h.Clan != null).ToList();
            sb.AppendLine("Orc lords held prisoner, to be released: " + captives.Count + ".");
            foreach (var g in captives.GroupBy(h => h.PartyBelongedToAsPrisoner?.Name?.ToString() ?? "unknown").OrderByDescending(g => g.Count()))
                sb.AppendLine("  held by " + g.Key + ": " + string.Join(", ", g.Select(h => h.Name.ToString())));

            // who gets which place: towns first, the clan with the fewest castles and towns, then tier, then renown
            List<Clan> clans = orcs.Clans.Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && c.Leader != null && c.Leader.IsAlive).ToList();
            if (clans.Count == 0) problems.Add("the orcs have no clan that can hold land");
            var given = new Dictionary<Clan, int>();
            var plan = new List<KeyValuePair<Settlement, Clan>>();
            foreach (Settlement s in places.OrderByDescending(p => p.IsTown).ThenBy(p => p.Name.ToString()))
            {
                Clan best = clans.OrderBy(c => c.Fiefs.Count + (given.TryGetValue(c, out int n) ? n : 0)).ThenByDescending(c => c.Tier).ThenByDescending(c => c.Renown).FirstOrDefault();
                if (best == null) break;
                given[best] = (given.TryGetValue(best, out int had) ? had : 0) + 1;
                plan.Add(new KeyValuePair<Settlement, Clan>(s, best));
            }
            CultureObject culture = orcs.Culture;
            Dictionary<string, List<CharacterObject>> tree = Tree(culture);
            foreach (var kv in plan)
            {
                Settlement s = kv.Key;
                MobileParty garrison = s.Town?.GarrisonParty;
                int men = garrison?.MemberRoster.TotalManCount ?? 0;
                int inside = s.Parties.Count(p => Leaves(p, orcs));
                int guests = Guests(s, orcs).Count;
                sb.AppendLine("- " + s.Name + " (" + (s.IsTown ? "town" : "castle") + ", " + s.BoundVillages.Count + " villages) to " + kv.Value.Name + " (" + kv.Value.Leader.Name
                              + "; it holds " + kv.Value.Fiefs.Count + " now): garrison of " + men + " to orc troops" + (inside > 0 ? "; " + inside + " parties inside to be moved out" : "") + (guests > 0 ? "; " + guests + " of your realm's heroes staying there without a party moved to your nearest castle or town" : "")
                              + (s.IsUnderSiege ? "; its siege ended" : "") + ".");
            }
            if (problems.Count > 0) return sb.Append("Cannot run now: ").Append(string.Join("; ", problems)).Append('.').ToString();
            if (!apply) return sb.ToString();

            // carry it out: the prisoners first (some are held in these very places)
            int freed = 0;
            foreach (Hero h in captives)
            {
                try { EndCaptivityAction.ApplyByPeace(h); freed++; }
                catch (Exception e) { Diag.Swallowed("releasing " + h.Name, e); }
            }
            int swapped = 0;
            int moved = 0;
            var targets = new HashSet<string>(plan.Select(kv => kv.Key.StringId), StringComparer.Ordinal);
            foreach (var kv in plan)
            {
                Settlement s = kv.Key;
                try
                {
                    s.SiegeEvent?.FinalizeSiegeEvent();
                    foreach (MobileParty p in s.Parties.Where(p => Leaves(p, orcs)).ToList()) LeaveSettlementAction.ApplyForParty(p);
                    moved += MoveGuests(s, orcs, targets);
                    ChangeOwnerOfSettlementAction.ApplyByDefault(kv.Value.Leader, s);
                    swapped += Garrison(s, tree, culture);
                    Log.Line("rebalance: " + s.Name + " to " + kv.Value.Name);
                }
                catch (Exception e) { Diag.Error("handing over " + s.Name, e); sb.AppendLine("  " + s.Name + " failed: " + e.Message); }
            }
            Diag.Event("command", "rebalance", new { freed, places = plan.Select(kv => kv.Key.StringId + ">" + kv.Value.StringId).ToList(), garrison_men = swapped, heroes_moved = moved });
            return sb.Append("Done: ").Append(freed).Append(" lords released, ").Append(plan.Count).Append(" places handed over, ").Append(swapped).Append(" garrison men now orc troops, ").Append(moved).Append(" of your heroes moved out first. Save the game now.").ToString();
        }

        /// <summary>
        /// Heroes staying in the place without a party whom the game takes prisoner when it changes owner (Oct 20, the first run:
        /// 11 of the player's realm's heroes "taken prisoner by Nanduhirion", Caradhras and Sirannon): the same test as the game's
        /// (PrisonerCaptureCampaignBehavior.HandleSettlementHeroes on OnSettlementOwnerChanged: not the player, a wanderer or a
        /// notable, not a prisoner or dead, and at war with the place's new owner).
        /// </summary>
        private static List<Hero> Guests(Settlement s, Kingdom orcs) =>
            s.HeroesWithoutParty.Where(h => h != null && h != Hero.MainHero && !h.IsWanderer && !h.IsNotable && h.HeroState != Hero.CharacterStates.Prisoner
                                            && h.HeroState != Hero.CharacterStates.Dead && h.MapFaction != null && h.MapFaction.IsAtWarWith(orcs)).ToList();

        /// <summary>Moves those heroes, before the handover, to the nearest castle or town of their own realm not under siege and not being handed over.</summary>
        private static int MoveGuests(Settlement s, Kingdom orcs, HashSet<string> handedOver)
        {
            int moved = 0;
            foreach (Hero h in Guests(s, orcs))
            {
                Settlement to = Settlement.All.Where(x => x.IsFortification && x.MapFaction == h.MapFaction && !x.IsUnderSiege && !handedOver.Contains(x.StringId))
                                              .OrderBy(x => x.Position.ToVec2().Distance(s.Position.ToVec2())).FirstOrDefault();
                if (to == null) { Log.Line("rebalance: " + h.Name + " has no castle or town of their realm to go to, and stays in " + s.Name); continue; }
                try
                {
                    if (h.GovernorOf != null) ChangeGovernorAction.RemoveGovernorOf(h);
                    TeleportHeroAction.ApplyImmediateTeleportToSettlement(h, to);
                    moved++;
                    Log.Line("rebalance: " + h.Name + " moved from " + s.Name + " to " + to.Name);
                }
                catch (Exception e) { Diag.Swallowed("moving " + h.Name + " out of " + s.Name, e); }
            }
            return moved;
        }

        /// <summary>A party inside that must leave when the place becomes the orcs': at war with them, and not the place's own.</summary>
        private static bool Leaves(MobileParty p, Kingdom orcs) =>
            p != null && !p.IsGarrison && !p.IsMilitia && !p.IsVillager && p != MobileParty.MainParty && p.MapFaction != null && p.MapFaction.IsAtWarWith(orcs);

        /// <summary>The culture's troops by "tier:kind" (kind: foot, archers, horse), every troop reachable from its basic and elite troops.</summary>
        private static Dictionary<string, List<CharacterObject>> Tree(CultureObject c)
        {
            var map = new Dictionary<string, List<CharacterObject>>(StringComparer.Ordinal);
            var seen = new HashSet<CharacterObject>();
            var queue = new Queue<CharacterObject>(new[] { c?.BasicTroop, c?.EliteBasicTroop }.Where(t => t != null));
            while (queue.Count > 0)
            {
                CharacterObject t = queue.Dequeue();
                if (!seen.Add(t)) continue;
                string key = t.Tier + ":" + Kind(t);
                if (!map.TryGetValue(key, out List<CharacterObject> l)) map[key] = l = new List<CharacterObject>();
                l.Add(t);
                foreach (CharacterObject u in t.UpgradeTargets ?? new CharacterObject[0]) if (u != null) queue.Enqueue(u);
            }
            return map;
        }

        private static string Kind(CharacterObject t) => t.IsMounted ? "horse" : t.IsRanged ? "archers" : "foot";

        /// <summary>The orc troop for a garrison troop: the same tier and kind, else the same tier, else the nearest tier below, else above.</summary>
        private static CharacterObject Match(CharacterObject t, Dictionary<string, List<CharacterObject>> tree, int turn)
        {
            if (tree.Count == 0) return null;
            if (tree.TryGetValue(t.Tier + ":" + Kind(t), out List<CharacterObject> same)) return same[turn % same.Count];
            for (int d = 0; d <= 7; d++)
                foreach (int tier in new[] { t.Tier - d, t.Tier + d })
                {
                    List<CharacterObject> any = tree.Where(kv => kv.Key.StartsWith(tier + ":", StringComparison.Ordinal)).SelectMany(kv => kv.Value).ToList();
                    if (any.Count > 0) return any[turn % any.Count];
                }
            return null;
        }

        /// <summary>Swaps a place's garrison to the orcs' troops man for man, and frees the orc soldiers in its prison into it. Returns the men swapped.</summary>
        private static int Garrison(Settlement s, Dictionary<string, List<CharacterObject>> tree, CultureObject culture)
        {
            MobileParty g = s.Town?.GarrisonParty;
            if (g == null) return 0;
            TroopRoster roster = g.MemberRoster;
            int swapped = 0, turn = 0;
            foreach (TroopRosterElement el in roster.GetTroopRoster().ToList())
            {
                CharacterObject t = el.Character;
                if (t == null || t.IsHero || t.Culture == culture) continue;
                CharacterObject to = Match(t, tree, turn++);
                if (to == null) continue;
                roster.AddToCounts(t, -el.Number, false, -el.WoundedNumber);
                roster.AddToCounts(to, el.Number, false, el.WoundedNumber);
                swapped += el.Number;
            }
            TroopRoster prison = s.Party.PrisonRoster;
            foreach (TroopRosterElement el in prison.GetTroopRoster().ToList())
            {
                if (el.Character == null || el.Character.IsHero || el.Character.Culture != culture) continue;
                prison.AddToCounts(el.Character, -el.Number, false, -el.WoundedNumber);
                roster.AddToCounts(el.Character, el.Number, false, el.WoundedNumber);
            }
            return swapped;
        }
    }
}
