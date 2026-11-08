using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace BannerlordAgents
{
    /// <summary>
    /// The human player's battles, taken on the game's thread (0.17.3; InPerson keeps them for the realms' agents). Each side as the battle begins with him
    /// in it (MapEventStarted), and again as he fights it, once the game has added the parties within its ring (the battle scene begins, Watchdog): men,
    /// strength (the game's estimate), lords, the realms in it, and its men by kind (foot, archers, horse, horse archers, healthy men only); at its end the
    /// losses and the winner. Handed to the helper, which decides which realms knew of it. Game's thread only.
    /// </summary>
    internal static class PlayerBattles
    {
        private sealed class Sides
        {
            public double Hours;
            public bool HisAttacker;
            public InPersonSide His, Other;
        }

        private static readonly Dictionary<MapEvent, Sides> Begun = new Dictionary<MapEvent, Sides>();

        public static void Reset() => Begun.Clear();

        private static bool Involves(MapEvent me) => me != null && MobileParty.MainParty != null && MobileParty.MainParty.MapEvent == me;

        public static void OnStarted(MapEvent me)
        {
            if (!Involves(me)) return;
            if (Begun.Count > 50) Begun.Clear();   // battles whose end never came (a load in between)
            Begun[me] = Take(me);
        }

        /// <summary>The battle scene with him began: the sides as he fights it, the ring's joiners in.</summary>
        public static void AsFought()
        {
            MapEvent me = MapEvent.PlayerMapEvent;
            if (Involves(me)) Begun[me] = Take(me);
        }

        public static void OnEnded(MapEvent me)
        {
            if (me == null) return;
            bool had = Begun.TryGetValue(me, out Sides s);
            Begun.Remove(me);
            if (!had && !Involves(me)) return;
            if (s == null) s = Take(me);
            MapEventSide his = s.HisAttacker ? me.AttackerSide : me.DefenderSide, other = s.HisAttacker ? me.DefenderSide : me.AttackerSide;
            s.His.Lost = his?.TroopCasualties ?? 0;
            s.Other.Lost = other?.TroopCasualties ?? 0;
            // the men as it began, when the game's own count is higher (the losers' parties are gone by now)
            s.His.Men = Math.Max(s.His.Men, his?.HealthyTroopCountAtMapEventStart ?? 0);
            s.Other.Men = Math.Max(s.Other.Men, other?.HealthyTroopCountAtMapEventStart ?? 0);
            bool won = me.HasWinner && (me.WinningSide == TaleWorlds.Core.BattleSideEnum.Attacker) == s.HisAttacker;
            var b = new InPersonBattle
            {
                Hours = s.Hours, Date = Text.When(s.Hours), Load = Bridge.History?.Label, Won = won, His = s.His, Other = s.Other,
                Kind = me.IsSiegeAssault ? "storm" : me.IsSallyOut ? "sally" : me.IsRaid ? "raid" : me.IsSiegeOutside ? "siege outside" : "field",
                Place = me.MapEventSettlement != null ? Names.Settlement(me.MapEventSettlement) : null
            };
            string player = MobileParty.MainParty?.StringId;
            Background.Post("the human player's battle", () => InPerson.Add(b, player));
        }

        private static Sides Take(MapEvent me)
        {
            bool att = me.AttackerSide != null && me.AttackerSide.Parties.Any(mp => mp.Party == PartyBase.MainParty);
            return new Sides { Hours = CampaignTime.Now.ToHours, HisAttacker = att, His = Count(att ? me.AttackerSide : me.DefenderSide), Other = Count(att ? me.DefenderSide : me.AttackerSide) };
        }

        private static InPersonSide Count(MapEventSide side)
        {
            var x = new InPersonSide();
            if (side == null) return x;
            x.Realm = side.MapFaction?.StringId;
            x.RealmName = side.MapFaction != null ? Names.Faction(side.MapFaction) : null;
            x.Leader = side.LeaderParty?.LeaderHero != null ? Names.Hero(side.LeaderParty.LeaderHero) : side.LeaderParty?.Name?.ToString();
            foreach (MapEventParty mp in side.Parties)
            {
                PartyBase p = mp.Party;
                if (p == null) continue;
                string realm = p.MapFaction?.StringId;
                if (realm != null && !x.Realms.Contains(realm)) x.Realms.Add(realm);
                x.Strength += p.EstimatedStrength;
                if (p.LeaderHero != null && p.LeaderHero.IsLord) x.Lords++;
                foreach (TroopRosterElement e in p.MemberRoster.GetTroopRoster())
                {
                    int healthy = e.Number - e.WoundedNumber;
                    if (e.Character == null || healthy <= 0) continue;
                    x.Men += healthy;
                    bool mounted = e.Character.IsMounted, ranged = e.Character.IsRanged;
                    if (mounted && ranged) x.HorseArchers += healthy;
                    else if (mounted) x.Cavalry += healthy;
                    else if (ranged) x.Ranged += healthy;
                    else x.Infantry += healthy;
                }
            }
            x.Strength = Math.Round(x.Strength);
            return x;
        }
    }
}
