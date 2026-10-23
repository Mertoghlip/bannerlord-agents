using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// What a realm can see, by the game's own rules, extended from the player to any realm.
    ///
    /// The game decides what the player sees like this (PartyBase.CalculateVisibilityAndInspected): a party is seen
    /// when its distance divided by the player's sight range is at most its "spotting ratio". The sight range is 12
    /// by day and 6 by night, plus skills; the spotting ratio is lower in forest and for small parties. Parties inside
    /// a settlement are hidden unless a lord flies his banner there. An army's members are seen when its leader is.
    /// Besiegers and raiders are seen whenever the place they attack can be looked over (within 1.5 times the sight
    /// range).
    ///
    /// For a realm we apply the same rule from every one of its parties, and from its castles and towns, which look
    /// out with their garrison's sight (or the base 12/6 without one). A realm also always knows who besieges or raids
    /// its own holdings. Game's thread only: the sight range and the spotting ratio come from the game's own model.
    ///
    /// For the logs, it counts what it did each hour (eyes, holdings looked over, spotting ratios asked and reused)
    /// and every time the game's sight model failed and a stand-in value was used; and every few in-game hours it runs
    /// the sight check for the player's party by itself (Audit), our rule against the game's own view.
    /// </summary>
    internal static class Vision
    {
        private struct Eye
        {
            public float X, Y, Range;
            public string Name;
        }

        private const float Cell = 60f;   // the game's maximum sight range, so a 3x3 block of cells holds every eye in range
        private static readonly Dictionary<string, KeyValuePair<float, double>> Ratios = new Dictionary<string, KeyValuePair<float, double>>();
        private static VisionStats _stats = new VisionStats();

        public static void Reset() => Ratios.Clear();

        public static void FactionSees(Kingdom realm, List<MobileParty> live, Snapshot snap)
        {
            _stats = new VisionStats();
            float baseRange = Campaign.Current.IsNight ? 6f : 12f;
            var eyes = new List<Eye>(256);
            foreach (MobileParty p in live)
            {
                if (p.MapFaction != realm || p.IsGarrison) continue;
                eyes.Add(new Eye { X = p.Position.X, Y = p.Position.Y, Range = SightOf(p, baseRange), Name = Names.Party(p) });
            }
            foreach (Settlement s in realm.Settlements)
            {
                if (s == null || !s.IsFortification) continue;
                MobileParty g = s.Town?.GarrisonParty;
                eyes.Add(new Eye { X = s.Position.X, Y = s.Position.Y, Range = g != null ? SightOf(g, baseRange) : baseRange, Name = Names.Settlement(s) });
            }

            var grid = new Dictionary<long, List<int>>();
            for (int e = 0; e < eyes.Count; e++)
            {
                long key = Key(Cx(eyes[e].X), Cx(eyes[e].Y));
                if (!grid.TryGetValue(key, out List<int> list)) grid[key] = list = new List<int>();
                list.Add(e);
            }

            // Holdings the realm can look over: its own, and any within 1.5 times an eye's sight.
            var inspected = new HashSet<string>();
            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s.IsHideout) continue;
                if (s.MapFaction == realm || Nearest(eyes, grid, s.Position.X, s.Position.Y, out _) <= 1.5f) inspected.Add(s.StringId);
            }

            var sees = new Dictionary<string, Sighting>();
            double now = snap.Hours;
            for (int i = 0; i < live.Count; i++)
            {
                MobileParty p = live[i];
                PartyRow r = snap.Parties[i];
                if (p.MapFaction == realm || r.Attached) continue;

                Settlement besieged = p.BesiegedSettlement;
                if (besieged != null && inspected.Contains(besieged.StringId))
                {
                    sees[r.Id] = new Sighting { Party = r.Id, By = "the siege of " + Names.Settlement(besieged) };
                    continue;
                }
                MapEvent me = p.MapEvent;
                if (me != null && me.IsRaid && me.MapEventSettlement != null && inspected.Contains(me.MapEventSettlement.StringId))
                {
                    sees[r.Id] = new Sighting { Party = r.Id, By = "the raid on " + Names.Settlement(me.MapEventSettlement) };
                    continue;
                }
                if (!r.Spottable) continue;

                float best = Nearest(eyes, grid, p.Position.X, p.Position.Y, out int eye);
                if (eye < 0 || best >= 1f) continue;
                if (best <= Ratio(p, now))
                    sees[r.Id] = new Sighting { Party = r.Id, By = eyes[eye].Name, Distance = Text.R1(best * eyes[eye].Range) };
            }

            // An army's members are seen when its leader is.
            foreach (PartyRow r in snap.Parties)
            {
                if (!r.Attached || r.ArmyLeader == null || !sees.TryGetValue(r.ArmyLeader, out Sighting lead)) continue;
                sees[r.Id] = new Sighting { Party = r.Id, By = lead.By, Distance = lead.Distance };
            }

            snap.WatchedSees = sees;
            snap.InspectedByWatched = inspected;
            _stats.Eyes = eyes.Count;
            _stats.Inspected = inspected.Count;
            _stats.Seen = sees.Count;
            snap.Vision = _stats;
        }

        /// <summary>The smallest distance-over-sight-range from any eye; below 1 means inside some eye's sight.</summary>
        private static float Nearest(List<Eye> eyes, Dictionary<long, List<int>> grid, float x, float y, out int which)
        {
            which = -1;
            float best = float.MaxValue;
            int cx = Cx(x), cy = Cx(y);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!grid.TryGetValue(Key(cx + dx, cy + dy), out List<int> list)) continue;
                foreach (int e in list)
                {
                    float ddx = eyes[e].X - x, ddy = eyes[e].Y - y;
                    float n = (float)Math.Sqrt(ddx * ddx + ddy * ddy) / Math.Max(0.1f, eyes[e].Range);
                    if (n < best) { best = n; which = e; }
                }
            }
            return best;
        }

        private static float SightOf(MobileParty p, float fallback)
        {
            try { return p.SeeingRange; }
            catch (Exception e)
            {
                _stats.RangeFailed++;
                Diag.Swallowed("the game's sight range", e);
                return fallback;
            }
        }

        /// <summary>The game's spotting ratio for a party, kept for two in-game hours (it changes with terrain and size).</summary>
        private static float Ratio(MobileParty p, double now)
        {
            if (Ratios.TryGetValue(p.StringId, out KeyValuePair<float, double> kv) && now - kv.Value < 2.0)
            {
                _stats.RatioCached++;
                return kv.Key;
            }
            _stats.RatioAsked++;
            float ratio;
            try { ratio = Campaign.Current.Models.MapVisibilityModel.GetPartySpottingRatioForMainPartySeeingRange(p); }
            catch (Exception e)
            {
                _stats.RatioFailed++;
                Diag.Swallowed("the game's spotting ratio", e);
                ratio = 1f;
            }
            Ratios[p.StringId] = new KeyValuePair<float, double>(ratio, now);
            return ratio;
        }

        private static int Cx(float v) => (int)Math.Floor(v / Cell);
        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        /// <summary>
        /// The sight check, as numbers: our rule applied to the player's own party, against the game's own flags, for
        /// every party where the game itself refreshes them. Each disagreement keeps the numbers that explain it.
        /// The game looks from the player's party (from the port town it besieges or raids, with a third more sight) and
        /// refreshes the seen flag only within its largest sight range, plus 5, of there, and never for garrisons and
        /// militias (CampaignTickCacheDataStore.UpdateVisibilitiesAroundMainParty); elsewhere a flag is whatever it was when
        /// last refreshed. Until 0.10.4 the check also took those old flags, and an orc army seen days before, 247 away,
        /// counted as a disagreement every run (Oct 22: 86-91% agreement).
        /// </summary>
        public static SightAudit Audit()
        {
            MobileParty me = MobileParty.MainParty;
            if (me == null || Campaign.Current.TrueSight) return null;
            float range = me.SeeingRange;
            CampaignVec2 from = me.Position;
            if (me.SiegeEvent != null && me.SiegeEvent.BesiegedSettlement.HasPort) { from = me.SiegeEvent.BesiegedSettlement.Position; range *= 1.35f; }
            else if (me.MapEvent != null && me.MapEvent.IsRaid && me.MapEvent.MapEventSettlement.HasPort) { from = me.MapEvent.MapEventSettlement.Position; range *= 1.35f; }
            float reach = Campaign.Current.Models.MapVisibilityModel.MaximumSeeingRange() + 5f;
            var a = new SightAudit { Range = range, Night = Campaign.Current.IsNight };
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == null || p == me || !p.IsActive || p.IsGarrison || p.IsMilitia) continue;
                float d = p.Position.Distance(from);
                if (d > reach) continue;
                bool ours = OursForPlayer(p, from, range, 0, out float ratio);
                bool game = p.IsVisible;
                a.Total++;
                if (ours == game) { a.Agree++; continue; }
                if (a.Disagree.Count >= 20) continue;
                a.Disagree.Add(new SightCase
                {
                    Id = p.StringId, Name = Names.Party(p), Kind = p.IsLordParty ? "lord" : p.IsBandit ? "bandit" : p.IsCaravan ? "caravan" : p.IsVillager ? "villager" : "other",
                    Men = p.Party.NumberOfHealthyMembers, Distance = d, Ratio = ratio, Ours = ours, Game = game,
                    InSettlement = p.CurrentSettlement != null, ArmyMember = p.Army != null && p.Army.LeaderParty != p,
                    Besieging = p.BesiegedSettlement != null, Raiding = p.MapEvent != null && p.MapEvent.IsRaid
                });
            }
            return a;
        }

        /// <summary>For the console (agents.vision_check): the same check, in words.</summary>
        public static string CheckPlayer()
        {
            MobileParty me = MobileParty.MainParty;
            if (me == null) return "There is no player party.";
            if (Campaign.Current.TrueSight) return "True sight (a cheat) is on, so the game shows every party. Switch it off and check again.";
            SightAudit a = Audit();
            if (a == null) return "The check could not run.";
            string head = "Your sight range is " + Text.D(a.Range) + ". For the " + a.Total + " parties near you, our rule agrees with the game on " + a.Agree + ".";
            if (a.Disagree.Count == 0) return head;
            return head + "\n  " + string.Join("\n  ", a.Disagree.Take(8).Select(c => c.Name + " at " + Text.D(c.Distance) + " (spotting ratio " + c.Ratio.ToString("0.00", Text.Inv)
                                                                                     + "): our rule says " + (c.Ours ? "seen" : "hidden") + ", the game says " + (c.Game ? "seen" : "hidden")));
        }

        private static bool OursForPlayer(MobileParty p, CampaignVec2 from, float range, int depth, out float ratio)
        {
            ratio = -1f;
            if (!p.IsActive) return false;
            bool computed = p.CurrentSettlement == null || p.LeaderHero?.ClanBanner != null
                            || (p.MapEvent != null && p.MapEvent.IsSiegeAssault && p.Party.Side == TaleWorlds.Core.BattleSideEnum.Attacker);
            if (!computed) return false;
            if (p.Army != null && p.Army.LeaderParty != null && p.Army.LeaderParty != p && p.Army.LeaderParty.AttachedParties.Contains(p))
                return depth < 3 && OursForPlayer(p.Army.LeaderParty, from, range, depth + 1, out ratio);
            if (p.SiegeEvent != null && Inspected(p.SiegeEvent.BesiegedSettlement, from, range)) return true;
            if (p.MapEvent != null && p.MapEvent.IsRaid && Inspected(p.MapEvent.MapEventSettlement, from, range)) return true;
            float n = p.Position.Distance(from) / range;
            try { ratio = Campaign.Current.Models.MapVisibilityModel.GetPartySpottingRatioForMainPartySeeingRange(p); }
            catch (Exception e) { Diag.Swallowed("the game's spotting ratio (sight check)", e); ratio = 1f; }
            if (n >= 1f) return false;
            return n <= ratio;
        }

        private static bool Inspected(Settlement s, CampaignVec2 from, float range)
            => s != null && s.Position.Distance(from) / range <= 1.5f;
    }
}
