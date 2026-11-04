using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// Money for the agents (0.9.4, Oct 23: the caps should be the agent's call; it should be able to estimate the
    /// value of loot and prisoner ransom money from a battle and decide accordingly, and the sell order must sell loot and
    /// just loot, instead of important food and other things). The game's own rules (1.4.8, read in its code):
    /// - every lord pays his men's wages each day from his own purse; his clan's leader tops him back up to 5,000 while the
    ///   leader has 4,000 or more, and a lord above 10,000 hands a tenth of the excess to the clan (DefaultClanFinanceModel);
    ///   unpaid wages cost morale, and men desert;
    /// - a party at its wage cap cannot recruit; each day the game sets every computer war party's cap from its clan
    ///   leader's gold (ClanVariablesCampaignBehavior.MakeClanFinancialEvaluation): the agent may now set it instead (Wages);
    /// - a computer lord sells everything but food and good riding horses on entering any friendly town (pack animals too),
    ///   and hands his prisoners over for ransom on entering any friendly castle or town. For a lord holding one of our orders
    ///   the first is replaced by ours, which sells loot only (IsLoot); the second is the game's, and measured.
    /// What each won battle brings in is recorded (TakingsPatch), so the agent can weigh a battle's worth by its own realm's
    /// past battles. Game's thread only, except where said.
    /// </summary>
    internal static class Purse
    {
        /// <summary>
        /// Loot: what a lord can sell without losing anything he needs: weapons, armour, shields, ammunition, harness and plunder
        /// (goods that are not food). Never food, horses (riding, war or pack: his speed and his carrying), herds, books or banners.
        /// </summary>
        public static bool IsLoot(ItemObject item)
        {
            if (item == null || item.IsFood) return false;
            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Invalid:
                case ItemObject.ItemTypeEnum.Horse:
                case ItemObject.ItemTypeEnum.Animal:
                case ItemObject.ItemTypeEnum.Book:
                case ItemObject.ItemTypeEnum.Banner:
                    return false;
            }
            return true;
        }

        // ---------- what a lord carries ----------

        private sealed class Carried
        {
            public int Items = -1, Prison = -1;
            public int Loot, Pieces, Men, Lords, Ransom;
        }

        // by party, worked out again only when its item roster or its prisoners change (their version numbers)
        private static readonly Dictionary<MobileParty, Carried> Cache = new Dictionary<MobileParty, Carried>();

        /// <summary>The worth of his loot at its base value (towns pay about that, less as they fill), and how many pieces.</summary>
        public static int LootValue(MobileParty p, out int pieces)
        {
            pieces = 0;
            int value = 0;
            ItemRoster items = p.ItemRoster;
            for (int i = 0; i < items.Count; i++)
            {
                ItemRosterElement e = items[i];
                if (e.Amount <= 0 || !IsLoot(e.EquipmentElement.Item)) continue;
                pieces += e.Amount;
                value += e.EquipmentElement.ItemValue * e.Amount;
            }
            return value;
        }

        /// <summary>His prisoners: soldiers, lords, and their ransom by the game's own reckoning with him as the seller.</summary>
        public static void Prisoners(MobileParty p, out int men, out int lords, out int ransom)
        {
            men = lords = ransom = 0;
            Hero seller = p.LeaderHero;
            foreach (TroopRosterElement e in p.PrisonRoster.GetTroopRoster())
            {
                if (e.Character == null || e.Number <= 0 || e.Character.IsPlayerCharacter) continue;
                if (e.Character.IsHero) lords++;
                else men += e.Number;
                ransom += Campaign.Current.Models.RansomValueCalculationModel.PrisonerRansomValue(e.Character, seller) * (e.Character.IsHero ? 1 : e.Number);
            }
        }

        /// <summary>The hourly copy of one of a watched realm's own lords: his purse, wages and cap, loot, prisoners, and his last sale.</summary>
        public static void Fill(PartyRow r, MobileParty p)
        {
            r.Gold = p.PartyTradeGold;
            r.Wages = p.TotalWage;
            r.WageCap = p.HasLimitedWage() ? p.PaymentLimit : -1;
            r.CapOurs = Wages.Has(p);
            if (!Cache.TryGetValue(p, out Carried c)) Cache[p] = c = new Carried();
            if (c.Items != p.ItemRoster.VersionNo)
            {
                c.Items = p.ItemRoster.VersionNo;
                c.Loot = LootValue(p, out c.Pieces);
            }
            if (c.Prison != p.PrisonRoster.VersionNo)
            {
                c.Prison = p.PrisonRoster.VersionNo;
                Prisoners(p, out c.Men, out c.Lords, out c.Ransom);
            }
            r.Loot = c.Loot;
            r.LootPieces = c.Pieces;
            r.Prisoners = c.Men;
            r.PrisonerLords = c.Lords;
            r.Ransom = c.Ransom;
            if (LastSale.TryGetValue(p, out Sale s))
            {
                r.SaleHours = s.Hours;
                r.SaleText = s.Text();
                r.SaleGold = s.LootGold + s.RansomGold;
            }
            RecruitTour.Fill(p, r);
        }

        // ---------- selling ----------

        internal sealed class Sale
        {
            public double Hours;
            public string Place;
            public int LootGold, Pieces, Kept, RansomGold, Sold;

            public string Text()
            {
                var parts = new List<string>();
                if (Pieces > 0) parts.Add("sold " + global::BannerlordAgents.Text.N(Pieces) + " pieces of loot for " + global::BannerlordAgents.Text.N(LootGold)
                                          + (Kept > 0 ? " (" + global::BannerlordAgents.Text.N(Kept) + " more the town could not pay for)" : ""));
                if (Sold > 0) parts.Add("handed over " + global::BannerlordAgents.Text.N(Sold) + " prisoners for " + global::BannerlordAgents.Text.N(RansomGold) + " in ransom");
                return parts.Count > 0 ? string.Join(", and ", parts) + " at " + Place : "had nothing to sell at " + Place;
            }
        }

        private static readonly Dictionary<MobileParty, Sale> LastSale = new Dictionary<MobileParty, Sale>();

        private static Sale SaleAt(MobileParty p, Settlement s)
        {
            double now = CampaignTime.Now.ToHours;
            // one sale record per visit: the loot (ours) and the prisoners (the game's) of the same hour at the same place go together
            if (!LastSale.TryGetValue(p, out Sale sale) || sale.Place != Names.Settlement(s) || now - sale.Hours > 1)
                LastSale[p] = sale = new Sale { Hours = now, Place = Names.Settlement(s) };
            return sale;
        }

        /// <summary>
        /// Sells his loot, and only his loot, to a friendly town: the game's own sale (SellItemsAction) at the town's price, as far
        /// as the town's purse goes, as the game does for every computer lord, but never food, horses, pack animals, herds or banners.
        /// </summary>
        public static int SellLoot(MobileParty p, Settlement s)
        {
            if (p == null || s == null || !s.IsTown || p.ItemRoster == null || FactionManager.IsAtWarAgainstFaction(p.MapFaction, s.MapFaction)) return 0;
            int got = 0, pieces = 0, kept = 0;
            ItemRoster items = p.ItemRoster;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                ItemRosterElement e = items[i];
                if (e.Amount <= 0 || !IsLoot(e.EquipmentElement.Item)) continue;
                int price = s.Town.GetItemPrice(e.EquipmentElement, p, true);
                if (price <= 0) { kept += e.Amount; continue; }
                int purse = s.SettlementComponent.Gold;
                int n = price * e.Amount <= purse ? e.Amount : purse / price;
                if (n > 0)
                {
                    SellItemsAction.Apply(p.Party, s.Party, e, n, s);
                    got += price * n;
                    pieces += n;
                }
                kept += e.Amount - n;
            }
            if (pieces > 0 || kept > 0)
            {
                Sale sale = SaleAt(p, s);
                sale.LootGold += got;
                sale.Pieces += pieces;
                sale.Kept = kept;
                Diag.Count("purse:loot sold", pieces);
                Diag.Event("purse", "loot sold", new { lord = p.LeaderHero?.StringId, place = s.StringId, pieces, gold = got, kept });
            }
            return got;
        }

        /// <summary>The game handed his prisoners over on entering (PartiesSellPrisonerCampaignBehavior): what it brought, for the sell order's word.</summary>
        /// <summary>
        /// Prisoners handed over at a holding: sold, or (0.11.3) with lords among them given to the dungeon of one's own realm's
        /// holding, a gift the game pays nothing for, the soldiers with them included, while the realm gains influence for the lords.
        /// </summary>
        public static void NoteRansom(MobileParty p, Settlement s, int gold, int sold, int lords = 0)
        {
            if (sold <= 0) return;
            Sale sale = SaleAt(p, s);
            sale.RansomGold += Math.Max(0, gold);
            sale.Sold += sold;
            Diag.Count("purse:prisoners sold", sold);
            bool gift = lords > 0 && gold <= 0 && s.MapFaction == p.MapFaction;
            if (gift) Diag.Count("purse:prisoners given to a dungeon", sold);
            Diag.Event("purse", gift ? "prisoners given to a dungeon" : "prisoners sold", new { lord = p.LeaderHero?.StringId, place = s.StringId, sold, gold, lords });
        }

        /// <summary>
        /// A sell order given to a lord already inside the place (the game sells only on entering): his loot in a town, and his
        /// prisoners in a castle or town, chosen as the game chooses them (soldiers, and lords at war with the place's realm).
        /// </summary>
        public static void CashIn(MobileParty p, Settlement s)
        {
            if (p == null || s == null || FactionManager.IsAtWarAgainstFaction(p.MapFaction, s.MapFaction)) return;
            SellLoot(p, s);
            if (!s.IsFortification || p.PrisonRoster == null) return;
            TroopRoster sell = TroopRoster.CreateDummyTroopRoster();
            foreach (TroopRosterElement e in p.PrisonRoster.GetTroopRoster())
                if (e.Character != null && (!e.Character.IsHero || (!e.Character.IsPlayerCharacter && e.Character.HeroObject.MapFaction.IsAtWarWith(s.MapFaction))))
                    sell.Add(e);
            if (sell.TotalManCount <= 0) return;
            // in one hand-over, as the game's own lords make it on entering (with an enemy lord in it, to one's own realm's holding,
            // the lord goes to the dungeon as a gift that earns the realm influence, and the game pays for none of it: 0.11.3 looked
            // into run C1's 27 hand-overs paid 0 and keeps the game's rule)
            int gold = p.PartyTradeGold, before = p.PrisonRoster.TotalManCount, lordsBefore = p.PrisonRoster.TotalHeroes;
            SellPrisonersAction.ApplyForSelectedPrisoners(p.Party, s.Party, sell);
            NoteRansom(p, s, p.PartyTradeGold - gold, before - p.PrisonRoster.TotalManCount, lordsBefore - p.PrisonRoster.TotalHeroes);
        }

        // ---------- the clans' purses ----------

        // a clan's gold change a day, by the game's own reckoning (the clan screen's number), worked out at its daily tick
        private static readonly Dictionary<Clan, int> Daily = new Dictionary<Clan, int>();

        /// <summary>The game's daily tick for a clan (after the game's own, which runs first): its day's forecast, and our wage caps put back.</summary>
        public static void OnDailyClan(Clan clan)
        {
            if (clan == null || clan.IsEliminated || !Bridge.IsSeat(clan.Kingdom)) return;
            Daily[clan] = (int)Math.Round(Campaign.Current.Models.ClanFinanceModel.CalculateClanGoldChange(clan, false, false, false).ResultNumber);
            Wages.OnDailyClan(clan);
        }

        /// <summary>Its forecast, worked out now if this session has none yet.</summary>
        public static int DailyOf(Clan clan)
        {
            if (Daily.TryGetValue(clan, out int d)) return d;
            d = (int)Math.Round(Campaign.Current.Models.ClanFinanceModel.CalculateClanGoldChange(clan, false, false, false).ResultNumber);
            Daily[clan] = d;
            return d;
        }

        /// <summary>A campaign was loaded: nothing of the last one's parties is kept.</summary>
        public static void Reset()
        {
            Cache.Clear();
            LastSale.Clear();
            Daily.Clear();
            Wages.Reset();
        }
    }

    /// <summary>
    /// The agent's wage caps (the wages order, 0.9.4): the most a lord's party may pay in wages a day, which decides whether he
    /// may recruit. The game sets every computer clan's caps once a day, so ours are put back right after (OnDailyClan) and
    /// checked every hour. A party's cap is saved with the game, so around every save the game's own cap is handed back and
    /// ours taken again after ("nothing in the save"). Cleared at every load, like every order. Game's thread only.
    /// </summary>
    internal static class Wages
    {
        private sealed class Cap
        {
            public int Value, Game;
        }

        private static readonly Dictionary<MobileParty, Cap> Caps = new Dictionary<MobileParty, Cap>();

        /// <summary>The game's "no cap" (the clan screen's unlimited).</summary>
        public static int None => Campaign.Current.Models.PartyWageModel.MaxWagePaymentLimit;

        public static bool Has(MobileParty p) => p != null && Caps.ContainsKey(p);
        public static int Count => Caps.Count;
        public static void Reset() => Caps.Clear();

        /// <summary>The caps in force, for the save's carry-on file (CarryOn, 0.13.5): each party and the cap its agent set.</summary>
        public static List<JObject> KeepForSave()
            => Caps.Where(kv => kv.Key != null && kv.Key.IsActive).Select(kv => new JObject { ["party"] = kv.Key.StringId, ["lord"] = kv.Key.LeaderHero?.StringId, ["cap"] = kv.Value.Value }).ToList();

        /// <summary>A cap carried on from the save just loaded: the save's own cap kept to hand back at the next save, the agent's put on. Null when carried on, else why not.</summary>
        public static string Resume(JObject row)
        {
            MobileParty p = Orders.PartyById((string)row["party"]);
            int cap = (int?)row["cap"] ?? -1;
            if (p == null || !p.IsActive || p.LeaderHero == null) return "the party of " + ((string)row["lord"] ?? "a lord") + " is gone";
            if (!Bridge.IsSeat(p.MapFaction) || cap < 0) return Names.Hero(p.LeaderHero) + "'s wage cap no longer applies";
            Caps[p] = new Cap { Game = p.PaymentLimit, Value = Math.Min(cap, None) };
            p.SetWagePaymentLimit(Caps[p].Value);
            Diag.Event("wages", "cap carried on", new { lord = p.LeaderHero.StringId, cap = Caps[p].Value, game = Caps[p].Game });
            return null;
        }

        /// <summary>Sets his cap (null: the game's own again), and says what changed.</summary>
        public static string Set(MobileParty p, int? cap)
        {
            string who = Names.Hero(p.LeaderHero);
            if (cap == null)
            {
                if (!Caps.TryGetValue(p, out Cap old)) return who + "'s wage cap is the game's own already (" + Words(p.HasLimitedWage() ? p.PaymentLimit : -1) + ")";
                Caps.Remove(p);
                p.SetWagePaymentLimit(old.Game);
                Diag.Event("wages", "cap handed back", new { lord = p.LeaderHero?.StringId, cap = old.Game });
                return who + "'s wage cap is the game's own again: " + Words(p.HasLimitedWage() ? p.PaymentLimit : -1) + "; his wages are " + Text.N(p.TotalWage) + " a day";
            }
            int was = p.HasLimitedWage() ? p.PaymentLimit : -1;
            if (!Caps.TryGetValue(p, out Cap c)) Caps[p] = c = new Cap { Game = p.PaymentLimit };
            c.Value = Math.Max(0, Math.Min(cap.Value, None));
            p.SetWagePaymentLimit(c.Value);
            Diag.Count("wages:caps set");
            Diag.Event("wages", "cap set", new { lord = p.LeaderHero?.StringId, cap = c.Value, was, game = c.Game, wages = p.TotalWage });
            return who + " may now pay " + Words(c.Value >= None ? -1 : c.Value) + " in wages (it was " + Words(was) + "); his wages are " + Text.N(p.TotalWage) + " a day";
        }

        private static string Words(int cap) => cap < 0 ? "with no cap" : "up to " + Text.N(cap) + " a day";

        /// <summary>Every hour: parties gone or no longer of a watched realm are dropped; a cap the game changed is put back.</summary>
        public static void Hourly()
        {
            if (Caps.Count == 0) return;
            foreach (KeyValuePair<MobileParty, Cap> kv in Caps.ToList())
            {
                MobileParty p = kv.Key;
                if (p == null || !p.IsActive || p.LeaderHero == null || !Bridge.IsSeat(p.MapFaction))
                {
                    if (p != null && p.IsActive) p.SetWagePaymentLimit(kv.Value.Game);
                    Caps.Remove(p);
                    continue;
                }
                Restore(p, kv.Value);
            }
        }

        /// <summary>The game has just set this clan's caps for the day: ours go back on.</summary>
        public static void OnDailyClan(Clan clan)
        {
            foreach (KeyValuePair<MobileParty, Cap> kv in Caps)
                if (kv.Key != null && kv.Key.IsActive && kv.Key.ActualClan == clan) Restore(kv.Key, kv.Value);
        }

        private static void Restore(MobileParty p, Cap c)
        {
            if (p.PaymentLimit == c.Value) return;
            c.Game = p.PaymentLimit;   // what the game set meanwhile: handed back at saves
            p.SetWagePaymentLimit(c.Value);
            Diag.Count("wages:caps put back");
        }

        public static void HandBackForSave()
        {
            foreach (KeyValuePair<MobileParty, Cap> kv in Caps)
            {
                try { if (kv.Key != null && kv.Key.IsActive) kv.Key.SetWagePaymentLimit(kv.Value.Game); }
                catch (Exception e) { Diag.Error("handing a wage cap back before a save", e); }
            }
        }

        public static void TakeBackAfterSave()
        {
            foreach (KeyValuePair<MobileParty, Cap> kv in Caps)
            {
                try { if (kv.Key != null && kv.Key.IsActive) kv.Key.SetWagePaymentLimit(kv.Value.Value); }
                catch (Exception e) { Diag.Error("taking a wage cap back after a save", e); }
            }
        }
    }

    /// <summary>
    /// For a lord holding one of our orders, the game's sale on entering a town (PartiesSellLootCampaignBehavior.OnSettlementEntered:
    /// everything but food and good riding horses, pack animals included) is replaced by ours, which sells loot only. Every
    /// other party keeps the game's.
    /// </summary>
    internal static class LootSalePatch
    {
        private static bool Prefix(MobileParty mobileParty, Settlement settlement)
        {
            try
            {
                if (mobileParty == null || settlement == null || !mobileParty.IsLordParty || mobileParty.IsMainParty || !Orders.Holds(mobileParty)) return true;
                if (Campaign.Current.GameStarted && !mobileParty.IsDisbanding && settlement.IsTown) Purse.SellLoot(mobileParty, settlement);
                return false;
            }
            catch (Exception e)
            {
                Diag.Swallowed("selling loot only", e);
                return true;
            }
        }
    }

    /// <summary>The game's prisoner sale on entering a castle or town (PartiesSellPrisonerCampaignBehavior), measured for lords holding our orders.</summary>
    internal static class PrisonerSalePatch
    {
        internal sealed class Before
        {
            public int Gold, Prisoners, Lords;
        }

        private static void Prefix(MobileParty mobileParty, out Before __state)
        {
            __state = null;
            try
            {
                if (mobileParty == null || !mobileParty.IsLordParty || mobileParty.IsMainParty || !Orders.Holds(mobileParty)) return;
                __state = new Before { Gold = mobileParty.PartyTradeGold, Prisoners = mobileParty.PrisonRoster.TotalManCount, Lords = mobileParty.PrisonRoster.TotalHeroes };
            }
            catch (Exception e) { Diag.Swallowed("measuring a prisoner sale (before)", e); }
        }

        private static void Postfix(MobileParty mobileParty, Settlement settlement, Before __state)
        {
            if (__state == null) return;
            try { Purse.NoteRansom(mobileParty, settlement, mobileParty.PartyTradeGold - __state.Gold, __state.Prisoners - mobileParty.PrisonRoster.TotalManCount, __state.Lords - mobileParty.PrisonRoster.TotalHeroes); }
            catch (Exception e) { Diag.Swallowed("measuring a prisoner sale", e); }
        }
    }

    /// <summary>
    /// What a won battle brings in (0.9.4): around the game's handing out of a battle's results (MapEvent.CalculateAndCommitMapEventResults:
    /// the fallen's gear, the losers' goods, their prisoners, the captured), each winning lord of a watched realm's loot and prisoners
    /// before and after, with the enemy's fallen shared out by men. Recorded for the realm's own reckoning of a battle's worth (Takings).
    /// </summary>
    internal static class TakingsPatch
    {
        internal sealed class Before
        {
            public MobileParty Party;
            public string Realm;
            public int Men, Loot, Pieces, Prisoners, Lords, Ransom;
        }

        private static void Prefix(MapEvent __instance, out List<Before> __state)
        {
            __state = null;
            try
            {
                if (!Bridge.Running || __instance == null || Bridge.Seats.Count == 0) return;
                if (__instance.BattleState != BattleState.AttackerVictory && __instance.BattleState != BattleState.DefenderVictory) return;
                foreach (MapEventParty mp in __instance.GetMapEventSide(__instance.WinningSide).Parties)
                {
                    MobileParty p = mp?.Party?.MobileParty;
                    if (p == null || !p.IsLordParty || p.LeaderHero == null || !Bridge.IsSeat(p.MapFaction)) continue;
                    var b = new Before { Party = p, Realm = p.MapFaction.StringId, Men = p.Party.NumberOfHealthyMembers };
                    b.Loot = Purse.LootValue(p, out b.Pieces);
                    Purse.Prisoners(p, out b.Prisoners, out b.Lords, out b.Ransom);
                    (__state ?? (__state = new List<Before>())).Add(b);
                }
            }
            catch (Exception e)
            {
                Diag.Swallowed("a battle's takings (before)", e);
                __state = null;
            }
        }

        private static void Postfix(MapEvent __instance, List<Before> __state)
        {
            if (__state == null || __state.Count == 0) return;
            try
            {
                MapEventSide lost = __instance.GetMapEventSide(__instance.DefeatedSide);
                int fallen = lost.TroopCasualties;
                int winners = 0;
                foreach (MapEventParty mp in __instance.GetMapEventSide(__instance.WinningSide).Parties) winners += mp?.Party != null ? mp.Party.NumberOfHealthyMembers : 0;
                double hours = CampaignTime.Now.ToHours;
                string kind = __instance.IsSiegeAssault ? "storm" : __instance.IsRaid ? "raid" : __instance.IsSallyOut ? "sally" : "field";
                foreach (IGrouping<string, Before> realm in __state.GroupBy(b => b.Realm))
                {
                    var row = new TakingsRow { Hours = hours, Date = Text.When(hours), Realm = realm.Key, Kind = kind, Place = __instance.MapEventSettlement?.StringId };
                    int men = 0;
                    foreach (Before b in realm)
                    {
                        MobileParty p = b.Party;
                        int loot = Purse.LootValue(p, out int pieces);
                        Purse.Prisoners(p, out int prisoners, out int lords, out int ransom);
                        row.Loot += Math.Max(0, loot - b.Loot);
                        row.Pieces += Math.Max(0, pieces - b.Pieces);
                        row.Prisoners += Math.Max(0, prisoners - b.Prisoners);
                        row.PrisonerLords += Math.Max(0, lords - b.Lords);
                        row.Ransom += Math.Max(0, ransom - b.Ransom);
                        row.Lords.Add(p.LeaderHero.StringId);
                        men += b.Men;
                    }
                    row.Men = men;
                    row.EnemyFallen = winners > 0 ? (int)Math.Round((double)fallen * men / winners) : fallen;
                    Diag.Count("takings:battles");
                    Background.Post("battle takings", () => Takings.Add(row));
                }
            }
            catch (Exception e) { Diag.Swallowed("a battle's takings (after)", e); }
        }
    }
}
