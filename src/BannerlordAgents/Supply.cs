using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// The supply order (0.13.19, Nov 1: I am seeing a lot of castles at my fronts with zero food due to raids on their villages, and I
    /// think agents and the lieutenant should be able to order lords to donate extra food to settlements to make sure they can outlast
    /// sieges; the choice: buy first, every realm's agents). A lord brings food into a castle or town of his realm or an ally's:
    /// - what it needs: enough to last the order's days of siege at what it eats a day under siege (its people's prosperity / 40 and its
    ///   garrison's men / 20, the game's food model, nothing coming in), at most what its stores hold (Town.FoodStocksUpperLimit), less what
    ///   it has;
    /// - what he can spare: the food he carries beyond keep_days of his own men's eating (MobileParty.FoodChange);
    /// - with too little to spare, he first rides to the market costing him the least detour (a town not besieged, of his realm, an ally or
    ///   a realm at peace, or an unraided village, with food for sale, within four town gaps) and buys there as the game's lords buy food
    ///   (PartyFoodBuyingModel.FindItemToBuy, SellItemsAction), as much as the place needs, keeping half his gold and within what he can
    ///   carry without slowing down (InventoryCapacity);
    /// - then into the holding, where the food he can spare goes into its stores, one food item for one day's portion, as the game turns
    ///   food sold to a market into stores (DefaultSettlementFoodModel, BonusToFoodStores), up to its most.
    /// It ends once he has handed it over, when the place is besieged or lost before he gets in (a besieged place is not supplied: the food
    /// would have to pass the besiegers), when it needs nothing, or when there is nothing he could bring. Game's thread only.
    /// </summary>
    internal static class Supply
    {
        private const float ReachInTownGaps = 4f;

        /// <summary>Per tool run: what he bought on the way, and whether he has been to market (once an order).</summary>
        private sealed class Errand
        {
            public int Bought, Spent;
            public string Market;
            public bool Shopped;
        }

        private static readonly Dictionary<int, Errand> Errands = new Dictionary<int, Errand>();

        public static void Reset() => Errands.Clear();

        private static Errand Of(Orders.HandOrder o)
        {
            if (!Errands.TryGetValue(o.ToolId, out Errand e)) Errands[o.ToolId] = e = new Errand();
            return e;
        }

        /// <summary>What a castle or town eats a day under siege: its people and its garrison, by the game's food model.</summary>
        public static float PerDay(Town town)
        {
            SettlementFoodModel food = Campaign.Current.Models.SettlementFoodModel;
            int garrison = town.GarrisonParty?.Party.NumberOfAllMembers ?? 0;
            return town.Prosperity / Math.Max(1, food.NumberOfProsperityToEatOneFood) + garrison / (float)Math.Max(1, food.NumberOfMenOnGarrisonToEatOneFood);
        }

        /// <summary>The food a holding still needs to last days of siege, within its stores.</summary>
        private static int Need(Town town, double days)
        {
            float want = Math.Min(town.FoodStocksUpperLimit(), (float)(days * PerDay(town)));
            return Math.Max(0, (int)Math.Ceiling(want - town.FoodStocks));
        }

        /// <summary>The food he carries beyond keep days of his own men's eating.</summary>
        private static int Spare(MobileParty p, double keep)
        {
            float eats = Math.Max(0f, -p.FoodChange);
            return Math.Max(0, p.TotalFoodAtInventory - (int)Math.Ceiling(keep * eats));
        }

        /// <summary>The first move (from ToolMove): to market first when he carries too little to spare and has not shopped yet.</summary>
        public static void Started(Orders.HandOrder o)
        {
            Settlement h = o.SupplyFor;
            MobileParty p = o.Party;
            if (h?.Town == null || p == null) return;
            Errand e = Of(o);
            int need = Need(h.Town, o.Setting("days", 10));
            if (need <= 0 || e.Shopped || Spare(p, o.Setting("keep_days", 3)) >= need) return;
            Settlement m = BestMarket(p, h);
            if (m != null) o.Place = m;
        }

        /// <summary>One hour of the errand. Returns the end reason (key: its short form), or null to carry on; moved: o.Place changed.</summary>
        public static string Step(Orders.HandOrder o, double now, out string key, out bool moved)
        {
            key = null;
            moved = false;
            MobileParty p = o.Party;
            Settlement h = o.SupplyFor;
            if (h?.Town == null) { key = "no holding"; return "the place to supply is no castle or town"; }
            Kingdom realm = o.Realm;
            if (realm != null && h.MapFaction != null && FactionManager.IsAtWarAgainstFaction(h.MapFaction, realm)) { key = "place fell"; return Names.Settlement(h) + " fell to " + Names.Faction(h.MapFaction) + " before he brought the food"; }
            if (h.IsUnderSiege) { key = "besieged"; return Names.Settlement(h) + " is under siege: he cannot bring food in past the besiegers" + Bought(o); }
            Town town = h.Town;
            double days = o.Setting("days", 10), keep = o.Setting("keep_days", 3);
            int need = Need(town, days);
            Errand e = Of(o);
            if (o.Place != null && o.Place != h)
            {
                // on the way to market: buy there, then on to the holding; a market that closed is chosen again
                if (Orders.At(p, o.Place))
                {
                    Buy(p, o.Place, need - Spare(p, keep), e);
                    o.Place = h;
                    moved = true;
                    return null;
                }
                if (!IsMarket(p, o.Place))
                {
                    Settlement m = e.Shopped ? null : BestMarket(p, h);
                    o.Place = m ?? h;
                    moved = true;
                }
                return null;
            }
            if (need <= 0 && e.Bought == 0) { key = "stocked"; return Names.Settlement(h) + " needs no food: it has " + Stocks(town); }
            if (!Orders.At(p, h))
            {
                if (Spare(p, keep) == 0 && !e.Shopped && BestMarket(p, h) == null) { key = "nothing to bring"; return "he has no food to spare and no market within reach to buy it"; }
                return null;
            }
            int given = Give(p, town, Math.Min(Spare(p, keep), need));
            if (given == 0) { key = "nothing to bring"; return "he reached " + Names.Settlement(h) + " with no food to spare" + Bought(o) + "; it has " + Stocks(town); }
            key = "supplied";
            Diag.Count("supply:food handed over", given);
            Diag.Event("order", "supplied", new { lord = p.LeaderHero?.StringId, place = h.StringId, given, bought = e.Bought, spent = e.Spent, market = e.Market, food = Math.Round(town.FoodStocks, 1), cap = town.FoodStocksUpperLimit() });
            Errands.Remove(o.ToolId);
            return "handed over " + given + " food at " + Names.Settlement(h) + (e.Bought > 0 ? " (" + e.Bought + " of it bought at " + e.Market + " for " + Text.N(e.Spent) + " gold)" : "") + "; it now has " + Stocks(town);
        }

        private static string Bought(Orders.HandOrder o)
            => Errands.TryGetValue(o.ToolId, out Errand e) && e.Bought > 0 ? " (he carries the " + e.Bought + " food he bought at " + e.Market + ")" : "";

        private static string Stocks(Town town)
        {
            float per = PerDay(town);
            return Text.N((int)Math.Floor(town.FoodStocks)) + " of " + Text.N(town.FoodStocksUpperLimit()) + (per > 0 ? ", about " + Text.N((int)Math.Floor(town.FoodStocks / per)) + " days under siege" : "");
        }

        /// <summary>A place he may buy food at now: a town not besieged and not an enemy's, or an unraided village not an enemy's, with food for sale.</summary>
        private static bool IsMarket(MobileParty p, Settlement s)
        {
            if (s == null || (!s.IsTown && !s.IsVillage) || s.ItemRoster == null || s.ItemRoster.TotalFood <= 0) return false;
            if (s.MapFaction != null && FactionManager.IsAtWarAgainstFaction(p.MapFaction, s.MapFaction)) return false;
            if (s.IsTown) return !s.IsUnderSiege;
            return s.Village != null && s.Village.VillageState == Village.VillageStates.Normal;
        }

        /// <summary>The market costing the least detour on the way to the holding, within reach; null when none, or when he could buy nothing (no gold, no room).</summary>
        private static Settlement BestMarket(MobileParty p, Settlement h)
        {
            if (p.PartyTradeGold < 100 || p.TotalWeightCarried >= p.InventoryCapacity) return null;
            Vec2 at = p.GetPosition2D, to = h.GatePosition.ToVec2();
            float reach = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default) * ReachInTownGaps;
            Settlement best = null;
            float bestCost = float.MaxValue;
            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s == h || !IsMarket(p, s)) continue;
                Vec2 g = s.GatePosition.ToVec2();
                float there = at.Distance(g);
                if (reach > 0 && there > reach) continue;
                float cost = there + g.Distance(to);
                if (cost < bestCost) { bestCost = cost; best = s; }
            }
            return best;
        }

        /// <summary>Buys up to want food items where he stands, as the game's lords buy food; keeps half his gold and his pace.</summary>
        private static void Buy(MobileParty p, Settlement s, int want, Errand e)
        {
            e.Shopped = true;
            e.Market = Names.Settlement(s);
            if (want <= 0) return;
            PartyFoodBuyingModel model = Campaign.Current.Models.PartyFoodBuyingModel;
            int budget = p.PartyTradeGold / 2;
            for (int i = 0; i < want; i++)
            {
                model.FindItemToBuy(p, s, out ItemRosterElement element, out float price);
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || !item.IsFood || price > budget || p.TotalWeightCarried + item.Weight > p.InventoryCapacity) break;
                int before = p.ItemRoster.TotalFood;
                SellItemsAction.Apply(s.Party, p.Party, element, 1);
                if (p.ItemRoster.TotalFood <= before) break;
                budget -= (int)Math.Ceiling(price);
                e.Spent += (int)Math.Ceiling(price);
                e.Bought++;
            }
            Diag.Count("supply:food bought", e.Bought);
        }

        /// <summary>Hands up to amount food items into the town's stores, up to its most; returns how many went in.</summary>
        private static int Give(MobileParty p, Town town, int amount)
        {
            int room = Math.Max(0, (int)Math.Floor(town.FoodStocksUpperLimit() - town.FoodStocks));
            int left = Math.Min(amount, room), given = 0;
            if (left <= 0) return 0;
            ItemRoster roster = p.ItemRoster;
            for (int i = roster.Count - 1; i >= 0 && left > 0; i--)
            {
                ItemRosterElement el = roster.GetElementCopyAtIndex(i);
                ItemObject item = el.EquipmentElement.Item;
                if (item == null || !item.IsFood || el.Amount <= 0) continue;
                int n = Math.Min(el.Amount, left);
                roster.AddToCounts(el.EquipmentElement, -n);
                left -= n;
                given += n;
            }
            town.FoodStocks = Math.Min(town.FoodStocksUpperLimit(), town.FoodStocks + given);
            return given;
        }
    }
}
