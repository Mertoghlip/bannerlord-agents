using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// A besieged place eats the food of the lords inside it too (0.19.5, Nov 13: the food that parties have in their inventories should also be counted
    /// towards the settlement's food when a settlement is besieged and has parties inside of it). In the game (1.4.8) a castle's or town's store is its own:
    /// Town.DailyTick adds its day's change (DefaultSettlementFoodModel: its lands and villages, none under siege, less prosperity/40 and garrison/20) and the
    /// place starves at 0 (Settlement.IsStarving), when its garrison loses a tenth of its regulars a day (DefaultPartyHealingModel); the lords inside eat
    /// only what they carry, and a town's market, and never feed the place. Now, once a besieged place's day would leave its store below one, it draws the
    /// shortfall from the food of the lord parties inside it (its realm's and those not at war with it, the human player's own party too), one item at a time,
    /// each from the party whose food would last it longest, the cheapest item first: the game's own rule when an army's parties share food
    /// (FoodConsumptionBehavior). The lords keep eating their own food meanwhile, so the place's food and theirs run out together. Game's thread, once a
    /// place's day; the switch is settings.txt's siege_food.
    /// </summary>
    internal static class SiegeFood
    {
        /// <summary>The lord parties inside a place whose food it may draw on: active, not its garrison or militia, not at war with its owner, carrying food.</summary>
        internal static List<MobileParty> Givers(Settlement s)
        {
            var list = new List<MobileParty>();
            IFaction owner = s?.MapFaction;
            if (owner == null) return list;
            foreach (MobileParty p in s.Parties)
            {
                if (p == null || !p.IsActive || p.IsGarrison || p.IsMilitia || !(p.IsLordParty || p.IsMainParty)) continue;
                if (p.MapFaction == null || (p.MapFaction != owner && FactionManager.IsAtWarAgainstFaction(p.MapFaction, owner))) continue;
                if (p.ItemRoster.TotalFood <= 0) continue;
                list.Add(p);
            }
            return list;
        }

        /// <summary>The food the lords inside carry, and what they eat a day themselves (the siege clock's pooled reckoning).</summary>
        internal static void Inside(Settlement s, out float food, out float eats)
        {
            food = 0;
            eats = 0;
            foreach (MobileParty p in Givers(s))
            {
                food += p.ItemRoster.TotalFood;
                eats += Math.Max(0f, -p.FoodChange);
            }
        }

        /// <summary>Game's thread, before a place's day (Town.DailyTick): a besieged place about to run out takes what it lacks from the lords inside.</summary>
        internal static void BeforeDailyTick(Town t)
        {
            if (!Settings.SiegeFood || t == null) return;
            Settlement s = t.Settlement;
            if (s == null || !s.IsUnderSiege) return;
            float before = t.FoodStocks, after = before + t.FoodChange;
            if (after >= 1f) return;
            List<MobileParty> givers = Givers(s);
            if (givers.Count == 0) return;
            int need = (int)Math.Ceiling(1f - after);
            List<int> plan = SiegeFoodRules.Plan(need, givers.Select(p => (float)p.ItemRoster.TotalFood).ToList(), givers.Select(p => Math.Max(0f, -p.FoodChange)).ToList());
            int given = 0, mine = 0;
            var from = new List<string>();
            for (int i = 0; i < givers.Count; i++)
            {
                if (plan[i] <= 0) continue;
                int got = Take(givers[i], plan[i]);
                if (got <= 0) continue;
                given += got;
                from.Add((givers[i].Name?.ToString() ?? givers[i].StringId) + " " + got);
                if (givers[i].IsMainParty) mine += got;
            }
            if (given <= 0) return;
            t.FoodStocks = before + given;
            Diag.Count("siege food:drawn", given);
            Diag.Event("siege food", "drawn", new { settlement = s.StringId, name = s.Name?.ToString(), stores = Math.Round(before, 1), change = Math.Round(after - before, 1), need, given, from });
            if (mine > 0)
            {
                try { InformationManager.DisplayMessage(new InformationMessage(s.Name + "'s stores are spent: its garrison takes " + mine + " food from your party's.", Color.FromUint(0xFFE0B060))); }
                catch (Exception e) { Diag.Swallowed("the siege food message", e); }
            }
        }

        /// <summary>Takes n food items from a party, the cheapest first, as the game's army food sharing does; returns how many it took.</summary>
        private static int Take(MobileParty p, int n)
        {
            ItemRoster r = p.ItemRoster;
            int got = 0;
            while (got < n)
            {
                int best = -1, value = int.MaxValue;
                for (int i = 0; i < r.Count; i++)
                {
                    ItemObject it = r.GetItemAtIndex(i);
                    if (it != null && it.IsFood && r.GetElementNumber(i) > 0 && it.Value < value) { value = it.Value; best = i; }
                }
                if (best < 0) break;
                r.AddToCounts(r.GetItemAtIndex(best), -1);
                got++;
            }
            return got;
        }
    }

    /// <summary>Harmony prefix on Town.DailyTick (internal; GameHooks attaches it): the besieged place's draw on its lords' food, before its day is added.</summary>
    internal static class SiegeFoodPatch
    {
        private static void Prefix(Town __instance)
        {
            try { SiegeFood.BeforeDailyTick(__instance); }
            catch (Exception e) { Diag.Swallowed("the siege food draw", e); }
        }
    }

    /// <summary>The siege food rule's arithmetic (0.19.5), game-free: for the game's thread, the reports, the siege clock and the order language.</summary>
    internal static class SiegeFoodRules
    {
        /// <summary>Who gives what toward a shortfall of need: one item at a time from the party whose food would last it longest (the game's army sharing); never more than one has.</summary>
        public static List<int> Plan(int need, IList<float> food, IList<float> eats)
        {
            int n = food?.Count ?? 0;
            var take = new List<int>(new int[n]);
            if (n == 0 || need <= 0) return take;
            var left = food.Select(f => (double)Math.Floor(Math.Max(0f, f))).ToArray();
            for (int k = 0; k < need; k++)
            {
                int best = -1;
                double bestDays = -1;
                for (int i = 0; i < n; i++)
                {
                    if (left[i] < 1) continue;
                    double days = left[i] / Math.Max(0.1, eats != null && i < eats.Count ? eats[i] : 0);
                    if (days > bestDays) { bestDays = days; best = i; }
                }
                if (best < 0) break;
                take[best]++;
                left[best] -= 1;
            }
            return take;
        }

        /// <summary>
        /// Days a besieged place's food lasts with the lords inside: its store alone first (the lords eat their own meanwhile), then what the lords have left,
        /// shared by the place and the lords together. Lords who would eat all theirs before the store runs out add nothing. Infinity when the place eats nothing.
        /// </summary>
        public static double Days(double stores, double eats, double lordsFood, double lordsEat)
        {
            if (eats <= 0) return double.PositiveInfinity;
            double alone = Math.Max(0, stores) / eats;
            if (lordsFood <= 0) return alone;
            double left = lordsFood - Math.Max(0, lordsEat) * alone;
            return left <= 0 ? alone : alone + left / (eats + Math.Max(0, lordsEat));
        }

        /// <summary>The food the realm's own lords inside a place carry, and what they eat a day (only its own lords' stores are known to it).</summary>
        public static void OwnLordsInside(Snapshot s, string realm, string place, out float food, out float eats)
        {
            food = 0;
            eats = 0;
            if (s == null || realm == null || place == null) return;
            foreach (PartyRow r in s.Parties)
            {
                if (r.Kind != "lord" || r.Faction != realm || r.InSettlement != place || r.FoodItems <= 0) continue;
                food += r.FoodItems;
                eats += r.FoodEats;
            }
        }
    }
}
