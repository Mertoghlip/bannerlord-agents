using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// The copy the bridge takes on the game's own thread, once per in-game hour. Only plain numbers and names
    /// leave this class; everything heavier happens on the worker (Brain).
    /// </summary>
    internal static class Capture
    {
        private static readonly string[] Behaviours = Enum.GetNames(typeof(AiBehavior));

        public static Snapshot Take(Kingdom watched, bool withVision)
        {
            long t0 = Perf.Now();
            Campaign c = Campaign.Current;
            IFaction player = Clan.PlayerClan?.MapFaction;
            var snap = new Snapshot
            {
                Campaign = c.UniqueGameId ?? "unknown",
                Hours = CampaignTime.Now.ToHours,
                Night = c.IsNight,
                Date = DateText(),
                PlayerFaction = player?.StringId,
                PlayerParty = MobileParty.MainParty?.StringId,
                Watched = watched?.StringId,
                Wall = DateTime.UtcNow.Ticks
            };
            var factions = new Dictionary<string, IFaction>();

            foreach (Kingdom k in Kingdom.All)
            {
                if (k == null || k.IsEliminated) continue;
                var row = new FactionRow { Id = k.StringId, Name = Names.Faction(k), Ruler = Names.Hero(k.Leader) };
                foreach (Kingdom o in Kingdom.All)
                    if (o != null && o != k && !o.IsEliminated && k.IsAtWarWith(o)) row.AtWar.Add(o.StringId);
                foreach (Kingdom a in k.AlliedKingdoms)
                    if (a != null) row.Allies.Add(a.StringId);
                snap.Factions[k.StringId] = row;
                factions[k.StringId] = k;
            }
            long t1 = Perf.Now();

            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s.IsHideout) continue;
                IFaction f = s.MapFaction;
                var r = new SettlementRow
                {
                    Id = s.StringId,
                    Name = Names.Settlement(s),
                    Kind = s.IsTown ? "town" : s.IsCastle ? "castle" : s.IsVillage ? "village" : "other",
                    Faction = f?.StringId,
                    X = Text.R1(s.Position.X),
                    Y = Text.R1(s.Position.Y),
                    Militia = (int)s.Militia
                };
                if (s.SiegeEvent != null)
                {
                    var camp = s.SiegeEvent.BesiegerCamp;
                    r.Besieger = camp?.LeaderParty?.StringId;
                    r.BesiegedBy = camp?.MapFaction?.StringId;
                }
                if (s.Town != null) r.Garrison = s.Town.GarrisonParty?.Party.NumberOfHealthyMembers ?? 0;
                if (s.Village != null)
                {
                    r.VillageState = s.Village.VillageState.ToString();
                    r.Bound = s.Village.Bound?.StringId;
                }
                snap.Settlements.Add(r);
                snap.SettlementById[r.Id] = r;
                if (f != null && !factions.ContainsKey(f.StringId)) factions[f.StringId] = f;
            }
            long t2 = Perf.Now();

            var live = new List<MobileParty>(4096);
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == null || !p.IsActive) continue;
                PartyRow r = Row(p);
                live.Add(p);
                snap.Parties.Add(r);
                snap.PartyById[r.Id] = r;
                IFaction f = p.MapFaction;
                if (f != null && !factions.ContainsKey(f.StringId)) factions[f.StringId] = f;
            }
            long t3 = Perf.Now();

            foreach (KeyValuePair<string, IFaction> kv in factions)
            {
                snap.FactionNames[kv.Key] = Names.Faction(kv.Value);
                if (watched != null && kv.Value != watched && FactionManager.IsAtWarAgainstFaction(kv.Value, watched)) snap.HostileToWatched.Add(kv.Key);
                if (player != null && kv.Value != player && FactionManager.IsAtWarAgainstFaction(kv.Value, player)) snap.HostileToPlayer.Add(kv.Key);
            }

            long t4 = Perf.Now();
            if (withVision && watched != null) Vision.FactionSees(watched, live, snap);
            long t5 = Perf.Now();

            // Names only where someone may read them: lords, the two realms' own parties, and anything seen.
            for (int i = 0; i < live.Count; i++)
            {
                PartyRow r = snap.Parties[i];
                if (r.Kind == "lord" || r.SeenByPlayer || r.Faction == snap.Watched || r.Faction == snap.PlayerFaction
                    || (snap.WatchedSees != null && snap.WatchedSees.ContainsKey(r.Id)))
                    r.Name = Names.Party(live[i]);
            }
            long t6 = Perf.Now();
            snap.PartsMs = new[] { Ms(t0, t1), Ms(t1, t2), Ms(t2, t3), Ms(t3, t4), Ms(t4, t5), Ms(t5, t6) };
            return snap;
        }

        private static double Ms(long a, long b) => (b - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public static PartyRow Row(MobileParty p)
        {
            var r = new PartyRow { Id = p.StringId, X = Text.R1(p.Position.X), Y = Text.R1(p.Position.Y) };
            r.Faction = p.MapFaction?.StringId;
            PartyBase party = p.Party;
            r.Men = party.NumberOfHealthyMembers;
            r.Wounded = party.NumberOfWoundedTotalMembers;
            r.Kind = p.IsLordParty ? "lord" : p.IsCaravan ? "caravan" : p.IsVillager ? "villager" : p.IsBandit ? "bandit"
                   : p.IsGarrison ? "garrison" : p.IsMilitia ? "militia" : "other";
            // the game recomputes a party's strength whenever its roster changes, so only ask where a report needs it
            if (p.IsLordParty || p.IsBandit) r.Strength = (float)Math.Round(party.EstimatedStrength);
            Hero leader = p.LeaderHero;
            r.Leader = leader?.StringId;
            Settlement inside = p.CurrentSettlement;
            r.InSettlement = inside?.StringId;
            Army army = p.Army;
            if (army != null)
            {
                r.ArmyLeader = army.LeaderParty?.StringId;
                if (army.LeaderParty == p) { r.ArmyMen = army.TotalHealthyMembers; r.ArmyParties = army.Parties.Count; }
                else r.Attached = p.AttachedTo != null;
            }
            int b = (int)p.DefaultBehavior;
            r.Doing = b >= 0 && b < Behaviours.Length ? Behaviours[b] : "?";
            r.TargetSettlement = p.TargetSettlement?.StringId;
            r.TargetParty = p.TargetParty?.StringId;
            r.Besieging = p.BesiegedSettlement?.StringId;
            MapEvent me = p.MapEvent;
            r.InBattle = me != null;
            r.SeenByPlayer = p.IsVisible && !p.IsMainParty;
            r.Spottable = inside == null || leader?.ClanBanner != null
                          || (me != null && me.IsSiegeAssault && party.Side == BattleSideEnum.Attacker);
            return r;
        }

        public static string DateText()
        {
            try { return CampaignTime.Now.ToString(); }
            catch (Exception e) { Diag.Swallowed("the game's date text", e); return ""; }
        }
    }
}
