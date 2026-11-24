using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// Nov 24 diagnostic (docs/ENGINE-NOTES.md, "AI storms break at a reload"): a game-AI army storming or besieging a place when the game saved gives
    /// up its siege at its first decision after the load: the game's war planner (AiMilitaryBehavior) offers its leader no siege, raid or defence, the
    /// leader picks a visit, and AiPartyThinkBehavior ends the storm and the siege. This probe rides the decision hook (ThinkPatch): for every army leader
    /// storming or besieging when it decides, it records the planner's gates for its own target (the place suitable, the food score, the way there and its
    /// distance, the distance score, the target score), where the party stands on the map, its members' food, and how many of the enemy's castles and
    /// towns pass each gate. A full line when the decision kept no military option or ended the siege, and at each party's first decision of the session;
    /// a short one otherwise. Game thread (the decision's own postfix); text and numbers only to Diag.
    /// </summary>
    internal static class AiProbe
    {
        internal struct Before
        {
            public bool On, Storm, Camp;
            public Settlement At;
            public AiBehavior B;
        }

        private static readonly HashSet<string> _seen = new HashSet<string>();
        private static MethodInfo _food, _suitable, _besiegeScore;
        private static object _mil;
        private static bool _patchesLogged;
        private static int _lines;

        /// <summary>The decision's prefix: an army leader storming or besieging, as it stood before it decided.</summary>
        internal static Before Capture(MobileParty p)
        {
            try
            {
                if (p == null || p.Army == null || p.Army.LeaderParty != p || p.LeaderHero == null) return default(Before);
                MapEvent ev = p.MapEvent;
                bool storm = ev != null && ev.IsSiegeAssault && ev.AttackerSide?.LeaderParty == p.Party;
                bool camp = p.BesiegerCamp != null;
                if (!storm && !camp) return default(Before);
                return new Before { On = true, Storm = storm, Camp = camp, At = p.BesiegedSettlement ?? ev?.MapEventSettlement, B = p.DefaultBehavior };
            }
            catch (Exception e) { Diag.Swallowed("the AI probe (before)", e); return default(Before); }
        }

        /// <summary>The decision's postfix: the planner's gates for that leader, when it decided this hour.</summary>
        internal static void After(MobileParty p, Before b)
        {
            if (!b.On) return;
            try
            {
                if (p.Ai.HourCounter != 1) return;   // it decides only every 1 to 6 hours; this hour it did not
                if (!_patchesLogged) { _patchesLogged = true; LogPatches(); }
                if (++_lines > 4000) return;   // a session's cap
                PartyThinkParams tp = p.ThinkParamsCache;
                int total = 0, mil = 0;
                string best = "";
                float bestScore = -1;
                if (tp != null)
                    foreach ((AIBehaviorData d, float score) in tp.AIBehaviorScores)
                    {
                        total++;
                        if (d.AiBehavior == AiBehavior.BesiegeSettlement || d.AiBehavior == AiBehavior.RaidSettlement || d.AiBehavior == AiBehavior.DefendSettlement || d.AiBehavior == AiBehavior.AssaultSettlement) mil++;
                        if (score > bestScore) { bestScore = score; best = d.AiBehavior + " " + ((d.Party as Settlement)?.StringId ?? (d.Party as MobileParty)?.StringId ?? "point"); }
                    }
                bool ended = (b.Storm && (p.MapEvent == null || !p.MapEvent.IsSiegeAssault)) || (b.Camp && p.BesiegerCamp == null);
                bool first = _seen.Add(p.StringId);
                double now = CampaignTime.Now.ToHours;
                if (!(mil == 0 || ended || first))
                {
                    Diag.Event("aiprobe", "short", new { party = p.StringId, realm = p.MapFaction?.StringId, at = b.At?.StringId, storm = b.Storm, hours = now, options = total, military = mil, best, best_score = bestScore });
                    return;
                }
                Settlement s = b.At;
                Army army = p.Army;
                // the members' food, as the planner's food score reads it (GetNumDaysForFoodToLast)
                var food = new List<string>();
                foreach (MobileParty m in new[] { p }.Concat(p.AttachedParties).Take(40))
                    food.Add(m.StringId + ":" + m.ItemRoster.TotalFood + "/" + m.FoodChange.ToString("0.###") + "=" + m.GetNumDaysForFoodToLast());
                Resolve();
                float foodBesiege = Food(tp, Army.ArmyTypes.Besieger), foodDefend = Food(tp, Army.ArmyTypes.Defender);
                // where the party stands
                CampaignVec2 pos = p.Position;
                var face = p.CurrentNavigationFace;
                string entrance = null;
                try { entrance = Campaign.Current.Models.MapDistanceModel.GetClosestEntranceToFace(face, MobileParty.NavigationType.Default).Item1?.StringId; } catch (Exception e) { entrance = "error " + e.GetType().Name; }
                // its own target through each gate
                object own = null;
                if (s != null)
                {
                    bool suit = Suitable(s, p, Army.ArmyTypes.Besieger);
                    BesiegeScore(s, p, out MobileParty.NavigationType navB, out float distScore);
                    AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(p, s, false, out MobileParty.NavigationType nav, out float dist, out bool fromPort);
                    float d0 = Campaign.Current.Models.MapDistanceModel.GetDistance(p, s, false, MobileParty.NavigationType.Default, out float landRatio);
                    float strength = p.GetTotalLandStrengthWithFollowers();
                    float target = Campaign.Current.Models.TargetScoreCalculatingModel.GetTargetScoreForFaction(s, Army.ArmyTypes.Besieger, p, strength);
                    own = new
                    {
                        id = s.StringId, owner = s.MapFaction?.StringId, suitable = suit, besiege_nav = navB.ToString(), distance_score = distScore, nav = nav.ToString(), dist, from_port = fromPort,
                        straight = d0, land_ratio = landRatio, gate_face = s.GatePosition.Face.FaceIndex, target_score = target, strength,
                        siege = s.SiegeEvent != null, camp_faction = s.SiegeEvent?.BesiegerCamp?.MapFaction?.StringId, settlement_event = s.Party?.MapEvent != null
                    };
                }
                // the enemy's castles and towns through the gates
                int forts = 0, suitable = 0, noWay = 0, noScore = 0;
                foreach (IFaction f in p.MapFaction.FactionsAtWarWith)
                {
                    if (f.Leader == null || !f.IsMapFaction) continue;
                    foreach (Settlement es in f.Settlements)
                    {
                        if (!es.IsFortification) continue;
                        forts++;
                        if (!Suitable(es, p, Army.ArmyTypes.Besieger)) continue;
                        suitable++;
                        AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(p, es, false, out MobileParty.NavigationType n2, out _, out _);
                        if (n2 == MobileParty.NavigationType.None) noWay++;
                        BesiegeScore(es, p, out _, out float ds2);
                        if (!(ds2 > 0f)) noScore++;
                    }
                }
                Diag.Event("aiprobe", ended ? "ended its siege" : mil == 0 ? "no military option" : "first decision", new
                {
                    party = p.StringId, lord = p.LeaderHero?.StringId, realm = p.MapFaction?.StringId, at = s?.StringId, storm = b.Storm, camp = b.Camp, before = b.B.ToString(), after = p.DefaultBehavior.ToString(),
                    hours = now, options = total, military = mil, best, best_score = bestScore, objective_value = tp?.CurrentObjectiveValue, do_not_change = tp?.DoNotChangeBehavior,
                    cohesion = army?.Cohesion, members = army?.Parties.Count, objective = p.Objective.ToString(), disbanding = p.IsDisbanding,
                    food_besiege = foodBesiege, food_defend = foodDefend, food,
                    at_sea = p.IsCurrentlyAtSea, transition = p.IsTransitionInProgress, land = p.HasLandNavigationCapability, naval = p.HasNavalNavigationCapability, capability = p.NavigationCapability.ToString(),
                    x = pos.X, y = pos.Y, on_land = pos.IsOnLand, face = face.FaceIndex, face_valid = face.IsValid(), entrance, in_settlement = p.CurrentSettlement?.StringId,
                    own, forts, suitable, no_way = noWay, no_distance_score = noScore,
                    list_lacks = Kingdom.All.Where(k => !k.IsEliminated && k != p.MapFaction && p.MapFaction.IsAtWarWith(k) && !p.MapFaction.FactionsAtWarWith.Contains(k)).Select(k => k.StringId).ToList(),
                    planner = _mil?.GetType().FullName, distance_model = Campaign.Current.Models.MapDistanceModel.GetType().FullName,
                    target_model = Campaign.Current.Models.TargetScoreCalculatingModel.GetType().FullName, food_model = Campaign.Current.Models.MobilePartyFoodConsumptionModel.GetType().FullName
                });
            }
            catch (Exception e) { Diag.Swallowed("the AI probe (after)", e); }
        }

        private static void Resolve()
        {
            if (_mil != null) return;
            _mil = Campaign.Current.GetCampaignBehavior<AiMilitaryBehavior>();
            Type t = typeof(AiMilitaryBehavior);
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _food = t.GetMethod("GetFoodScoreForActionType", any);
            _suitable = t.GetMethod("CheckIfSettlementIsSuitableForMilitaryAction", any);
            _besiegeScore = t.GetMethod("CalculateDistanceScoreForBesieging", any);
        }

        private static float Food(PartyThinkParams tp, Army.ArmyTypes type)
        {
            if (_mil == null || _food == null || tp == null) return float.NaN;
            return (float)_food.Invoke(_mil, new object[] { tp, type });
        }

        private static bool Suitable(Settlement s, MobileParty p, Army.ArmyTypes type)
        {
            if (_mil == null || _suitable == null) return true;
            return (bool)_suitable.Invoke(_mil, new object[] { s, p, type, false });
        }

        private static void BesiegeScore(Settlement s, MobileParty p, out MobileParty.NavigationType nav, out float score)
        {
            nav = MobileParty.NavigationType.None;
            score = float.NaN;
            if (_mil == null || _besiegeScore == null || s.Town == null) return;
            var args = new object[] { s, p, null, null, null, null };
            _besiegeScore.Invoke(_mil, args);
            nav = (MobileParty.NavigationType)args[2];
            score = (float)args[3];
        }

        /// <summary>Once a session: which mods patch the planner's steps and the models it asks.</summary>
        private static void LogPatches()
        {
            try
            {
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                var found = new Dictionary<string, string>();
                void Look(Type t, string name)
                {
                    foreach (MethodInfo m in t.GetMethods(any).Where(m => m.Name == name))
                    {
                        HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(m);
                        found[t.Name + "." + name] = info == null ? "none" : string.Join(", ", info.Owners.Distinct());
                    }
                }
                Look(typeof(AiMilitaryBehavior), "AiHourlyTick");
                Look(typeof(AiMilitaryBehavior), "FindBestTargetAndItsValueForFaction");
                Look(typeof(AiMilitaryBehavior), "CalculateMilitaryBehaviorForFactionSettlements");
                Look(typeof(AiMilitaryBehavior), "CalculateMilitaryBehaviorForSettlement");
                Look(typeof(AiMilitaryBehavior), "CheckIfSettlementIsSuitableForMilitaryAction");
                Look(typeof(AiMilitaryBehavior), "GetFoodScoreForActionType");
                Look(typeof(AiMilitaryBehavior), "CalculateDistanceScoreForBesieging");
                Look(typeof(AiPartyThinkBehavior), "PartyHourlyAiTick");
                Look(typeof(AiHelper), "GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty");
                Look(typeof(PartyThinkParams), "AddBehaviorScore");
                Look(typeof(PartyThinkParams), "Reset");
                Look(typeof(PartyThinkParams), "Initialization");
                Look(Campaign.Current.Models.MapDistanceModel.GetType(), "GetDistance");
                Look(Campaign.Current.Models.TargetScoreCalculatingModel.GetType(), "GetTargetScoreForFaction");
                Diag.Event("aiprobe", "patches", new { methods = found });
            }
            catch (Exception e) { Diag.Swallowed("the AI probe (patches)", e); }
        }

        /// <summary>The console's list of storms and sieges under way (agents.storms): who besieges what, whether a storm is being fought, and its leader.</summary>
        internal static string Storms()
        {
            var lines = new List<string>();
            foreach (Settlement s in Settlement.All)
            {
                if (s.SiegeEvent == null) continue;
                MobileParty lead = s.SiegeEvent.BesiegerCamp?.LeaderParty;
                MapEvent ev = s.Party?.MapEvent;
                bool storm = ev != null && ev.IsSiegeAssault;
                lines.Add((storm ? "STORM " : "siege ") + s.StringId + " (" + s.MapFaction?.StringId + ") by " + (lead?.StringId ?? "?") + " (" + lead?.MapFaction?.StringId + ")"
                          + (lead?.Army != null ? ", army of " + lead.Army.Parties.Count : "") + (storm ? ", storm leader " + (ev.AttackerSide?.LeaderParty?.MobileParty?.StringId ?? "?") : ""));
            }
            return lines.Count == 0 ? "no sieges" : string.Join("\n", lines);
        }
    }
}
