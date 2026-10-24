using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BannerlordAgents.Common;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// The game's clocks, run ahead (0.7.3; Mert, Oct 17: "We should be adjusting our prediction of the marching speed of a
    /// force based on the game's exact equations, so the ai has a good idea on where its own/enemies' armies will reach their
    /// waypoint. We should do the same about siege and battle timers so it can plan ahead."). Game-free, worker thread, from
    /// the hourly copy only:
    /// - marching: each party's speed sum (GamePace) along the way the game planned for it, the ground under each stretch from
    ///   the walking map, the night as it comes;
    /// - sieges: the camp's preparations and engines at the rates the model in force builds them, the game's storm test rolled
    ///   about 3 times an hour (BesiegerCamp.StartingAssaultOnBesiegedSettlementIsLogical, MobilePartyAi), the four-day fall
    ///   of the bar, the place's food and the garrison's hunger;
    /// - battles: the game's rounds (one an hour in a storm, two in any other battle), its strikes per round and the chance
    ///   each knocks a man down, its rule for a lost round (morale -3) and a rout, with the expected numbers.
    /// The numbers and their sources are in docs/TOOLS.md ("The game's clocks").
    /// </summary>
    internal static class Forecast
    {
        // ---------- marching ----------

        /// <summary>The army's leader stands for a party marching in it.</summary>
        public static PartyRow Lead(Snapshot s, PartyRow r) => r != null && r.Attached && r.ArmyLeader != null && s.PartyById.TryGetValue(r.ArmyLeader, out PartyRow l) ? l : r;

        /// <summary>A people's march bonuses by ground kind and night (TAOM's feats as the game holds them), or null.</summary>
        public static float[] Feats(Snapshot s, string culture) => culture != null && s?.CultureSpeeds != null && s.CultureSpeeds.TryGetValue(culture, out float[] f) ? f : null;

        /// <summary>
        /// A party's march as the game reckons it: its speed sum's base and factors from the game's cache, its people's bonuses,
        /// its scout's perks, and what the named rules leave unexplained in its speed now (measured only while it marches in
        /// the open: in a holding or a battle the game's speed is not kept up). Null for a party with no speed copied.
        /// </summary>
        public static Pace PaceOf(Snapshot s, PartyRow r)
        {
            PartyRow lead = Lead(s, r);
            if (lead == null) return null;
            if (lead.SpeedBase <= 0)
                return lead.Speed > 0.05f ? new Pace { Base = lead.Speed, PerkValues = s?.PerkValues } : null;
            var p = new Pace
            {
                Base = lead.SpeedBase, Factors = lead.SpeedFactors, Perks = lead.ScoutPerks, FootShare = lead.FootShare,
                PerkValues = s?.PerkValues, Feats = Feats(s, lead.Culture)
            };
            if (Marching(lead) && lead.FinalSpeed > 0)
            {
                float rest = GamePace.Residual(p, lead.FinalSpeed, lead.Terrain, s != null && s.Night, lead.Snowing);
                if (!float.IsNaN(rest) && Math.Abs(rest) <= 0.6f) p.Other = (float)Math.Round(rest, 3);
            }
            return p;
        }

        /// <summary>
        /// The hourly check of the marching rule: for every lord marching in the open, what the named rules leave unexplained in
        /// its speed now (GamePace.Residual), as samples for the session summary ("march:unexplained"), and how many lords it
        /// explains to within 0.02 of their own speed sum. On Oct 16's frames the typical lord carried about +0.19 the rules do
        /// not name (likely the scout's Uncanny Insight, which grows with scouting skill above 200), constant for each party, so
        /// it is measured and carried forward.
        /// </summary>
        public static void Calibrate(Snapshot s)
        {
            int marching = 0, explained = 0;
            foreach (PartyRow r in s.Parties)
            {
                if (r.Kind != "lord" || r.SpeedBase <= 0 || !Marching(r) || r.FinalSpeed <= 0) continue;
                var p = new Pace { Base = r.SpeedBase, Factors = r.SpeedFactors, Perks = r.ScoutPerks, FootShare = r.FootShare, PerkValues = s.PerkValues, Feats = Feats(s, r.Culture) };
                float rest = GamePace.Residual(p, r.FinalSpeed, r.Terrain, s.Night, r.Snowing);
                if (float.IsNaN(rest)) continue;
                marching++;
                if (Math.Abs(rest) <= 0.02f) explained++;
                Diag.Sample("march:unexplained", rest);
            }
            if (marching > 0) Diag.Count("march:lords checked", marching);
            if (explained > 0) Diag.Count("march:explained within 0.02", explained);
        }

        /// <summary>Walking on the map now: not in a holding, a battle or a siege camp.</summary>
        public static bool Marching(PartyRow r) => r != null && r.InSettlement == null && !r.InBattle && r.Besieging == null && !r.Attached;

        /// <summary>The way a party is walking: where it stands, the corners the game planned for it, and where it walks to.</summary>
        public static List<float[]> WayOf(PartyRow r)
        {
            var way = new List<float[]> { new[] { r.X, r.Y } };
            if (r.Route != null)
                for (int i = 0; i + 1 < r.Route.Length; i += 2) way.Add(new[] { r.Route[i], r.Route[i + 1] });
            float[] last = way[way.Count - 1];
            if ((r.Tx != 0 || r.Ty != 0) && Math.Abs(last[0] - r.Tx) + Math.Abs(last[1] - r.Ty) > 0.3f) way.Add(new[] { r.Tx, r.Ty });
            return way;
        }

        /// <summary>When a party gets where it is walking.</summary>
        internal sealed class Arrival
        {
            public double Hours, At, Length;
            public bool Reached;
            /// <summary>The holding it heads for, if one (else it walks to a point: a camp, a party it follows).</summary>
            public string To;
            /// <summary>Along the game's planned way (true), or by road at its pace (ToHolding, when the walk ends elsewhere).</summary>
            public bool Way = true;
        }

        /// <summary>
        /// When the party reaches where it is walking, along the game's own planned way at the game's own speed for each stretch's
        /// ground and hour. Null when it is not marching, has no destination, or no pace. A holding counts as reached at its gate
        /// (the game: within 0.05 for a town, 1 for a castle or village).
        /// </summary>
        public static Arrival ArrivalOf(Snapshot s, PartyRow r, double maxHours = 24 * 20)
        {
            PartyRow lead = Lead(s, r);
            if (!Marching(lead) || (lead.Tx == 0 && lead.Ty == 0)) return null;
            float dx = lead.Tx - lead.X, dy = lead.Ty - lead.Y;
            if (dx * dx + dy * dy < 0.5f) return null;
            Pace p = PaceOf(s, lead);
            if (p == null) return null;
            List<float[]> way = WayOf(lead);
            MapGround g = MapGround.For(s?.MapCrc);
            double hours = GamePace.March(way, g != null ? (Func<float, float, int>)g.TerrainAt : null, p, s?.Hours ?? 0, out double at, out bool reached, maxHours);
            if (double.IsNaN(hours)) return null;
            return new Arrival { Hours = hours, At = at, Reached = reached, Length = GamePace.Length(way), To = Views.Toward(lead) };
        }

        /// <summary>Does the party's walk end at the holding (its gate, or within 5 of it: the gate, a siege camp)?</summary>
        public static bool EndsAt(PartyRow lead, SettlementRow h)
        {
            if (lead == null || h == null || (lead.Tx == 0 && lead.Ty == 0)) return false;
            bool Near(float x, float y) => (lead.Tx - x) * (lead.Tx - x) + (lead.Ty - y) * (lead.Ty - y) <= 25f;
            return Near(h.X, h.Y) || ((h.Gx != 0 || h.Gy != 0) && Near(h.Gx, h.Gy));
        }

        /// <summary>
        /// When a party gets to a holding (0.7.4 after Hollin Gate, where two lords walking to a waypoint 8.6 ahead were counted
        /// as there in 3 hours, 96.5 away by road): along the game's planned way when that way ends at the holding; else, a
        /// party walking elsewhere first or standing still, by road (the game's table, else as the crow flies) at its pace over
        /// a day. Way says which. Null with no pace.
        /// </summary>
        public static Arrival ToHolding(Snapshot s, PartyRow r, SettlementRow h, double maxHours = 24 * 20)
        {
            PartyRow lead = Lead(s, r);
            if (lead == null || h == null) return null;
            if (lead.InSettlement == h.Id) return new Arrival { Hours = 0, At = s.Hours, Reached = true, To = h.Id, Way = true };
            if (Marching(lead) && EndsAt(lead, h))
            {
                Arrival a = ArrivalOf(s, r, maxHours);
                if (a != null) { a.Way = true; a.To = h.Id; return a; }
            }
            float per = DayPerHour(s, lead);
            if (per <= 0.05f) return null;
            float road = Roads.To(s, lead, h);
            double d = !float.IsNaN(road) ? road : Math.Sqrt((h.X - lead.X) * (h.X - lead.X) + (h.Y - lead.Y) * (h.Y - lead.Y));
            double hours = d / per;
            return new Arrival { Hours = hours, At = s.Hours + hours, Reached = hours <= maxHours, Length = d, To = h.Id, Way = false };
        }

        /// <summary>"there in about 14 hours (Summer 19, 14:00), 38 to go on its way", or "more than 20 days away".</summary>
        public static string ArrivalText(Arrival a)
        {
            if (a == null) return null;
            if (!a.Reached) return "more than " + Math.Round(a.Hours / 24) + " days away at its pace";
            string left = a.Length > 0.5 ? ", " + Text.D((float)a.Length) + (a.Way ? " to go on its way" : " to go by road") : "";
            return "there in about " + HoursText(a.Hours) + " (" + Text.When(a.At) + ")" + (a.Way ? "" : " by road at its pace") + left;
        }

        public static string HoursText(double h) => h < 1.5 ? "an hour" : h < 72 ? Math.Round(h) + " hours" : Math.Round(h / 24, 1).ToString("0.#", Text.Inv) + " days";

        /// <summary>Map units an hour over a day on open ground, night counted: for a march known only by its length (the road table).</summary>
        public static float DayPerHour(Snapshot s, PartyRow r)
        {
            Pace p = PaceOf(s, r);
            return p != null ? GamePace.DayAverage(p) : 0f;
        }

        // ---------- sieges ----------

        public const double RollsPerHour = 3.08;   // the camp leader's AI checks every 0.30 to 0.35 hours (MobilePartyAi, AiCheckInterval 0.25)
        private const double SiegeHorizon = 24 * 10, Step = 0.1;

        /// <summary>The engines each siege strategy builds, in the order the game builds them (DefaultSiegeStrategyActionModel).</summary>
        public static readonly Dictionary<string, string[]> BuildOrder = new Dictionary<string, string[]>
        {
            ["siege_strategy_prepare_assault"] = new[] { "ram", "siege_tower_level1", "siege_tower_level1", "ballista", "ballista", "onager", "onager" },
            ["siege_strategy_breach_walls"] = new[] { "ram", "siege_tower_level1", "onager", "onager", "onager", "onager" },
            ["siege_strategy_wear_out_defenders"] = new[] { "ram", "siege_tower_level1", "trebuchet", "trebuchet", "trebuchet", "trebuchet" }
        };

        internal sealed class SiegeClock
        {
            public double Now, PrepDoneAt = double.NaN, FirstChanceAt = double.NaN, MedianAt = double.NaN, FoodOutAt = double.NaN, GarrisonGoneAt = double.NaN, CohesionOutAt = double.NaN;
            public double Within24, Within48, Within72, Within240;
            public double RatioNow;
            public List<KeyValuePair<string, double>> Engines = new List<KeyValuePair<string, double>>();
            public bool NeverByStrength;
            /// <summary>The agent's storm rule the clock followed (null: the game's own commander), and the hour it would storm by that rule (NaN: not within 10 days).</summary>
            public StormRule Rule;
            public double RuleStormAt = double.NaN;
        }

        private static bool Counts(string type) => type == "ram" || type == "improved_ram" || type == "siege_tower_level1" || type == "ballista" || type == "onager" || type == "trebuchet"
                                                    || type == "fire_ballista" || type == "fire_onager" || type == "fire_trebuchet";

        /// <summary>The walls' worth by the game's formula (GetSettlementAdvantage) for these built engines, before perks.</summary>
        public static double Advantage(int wallLevel, bool wallsDown, IEnumerable<string> built)
        {
            bool ram = false, improved = false;
            int towers = 0, ranged = 0, fire = 0;
            foreach (string t in built)
            {
                if (t == "ram" || t == "improved_ram") { ram = true; if (t == "improved_ram") improved = true; }
                else if (t == "siege_tower_level1") towers++;
                else if (t == "trebuchet" || t == "onager" || t == "ballista") ranged++;
                else if (t == "fire_onager" || t == "fire_ballista") fire++;
            }
            double walls = (4.0 + (Math.Max(1, wallLevel) - 1)) * (wallsDown ? 0.25 : 1.0);
            double engines = 1.0 + (ram || towers > 0 ? 0.25 : 0) + (improved ? 0.24 : ram ? 0.16 : 0) + (towers > 1 ? 0.24 : towers == 1 ? 0.16 : 0) + ranged * 0.08 + fire * 0.12;
            return (1.0 + walls) / engines;
        }

        /// <summary>
        /// The siege's clock, run ahead from this hour: the camp builds its preparations, then its strategy's engines one at a
        /// time at the rates the model in force gives; once ready, the camp's leader rolls the game's storm test about 3 times
        /// an hour (the chance grows with the besiegers' edge over the defenders times the walls' worth to a power that starts
        /// falling after 96 hours, and is near nothing while an engine is half built); the garrison's healthy men fall by a tenth
        /// of its men each day once the food is gone (militia do not eat; nobody surrenders). The besiegers' strength is held
        /// as it is now, and no relief is counted.
        /// </summary>
        public static SiegeClock Siege(SiegeRow g, double now, StormRule rule = null)
        {
            var c = new SiegeClock { Now = now, Rule = rule != null && (rule.Mode != 0 || rule.Now) ? rule : null };
            if (g == null) return c;
            string[] order = g.Strategy != null && BuildOrder.TryGetValue(g.Strategy, out string[] o) ? o : BuildOrder["siege_strategy_prepare_assault"];
            var built = g.Engines.Where(e => e.Progress >= 1f).Select(e => e.Type).ToList();
            var building = g.Engines.Where(e => e.Progress < 1f && !e.Reserve).ToList();
            // the order still to come: the strategy's list less what the camp holds already
            var have = g.Engines.Select(e => e.Type).ToList();
            var todo = new List<string>();
            foreach (string t in order) { if (have.Contains(t)) have.Remove(t); else todo.Add(t); }
            // the perks' share of the walls' worth: the game's number now over the formula's for the same engines
            double formula = Advantage(g.WallLevel, g.WallsDown, built);
            double perk = formula > 0 && g.Advantage > 0 ? g.Advantage / formula : 1.0;
            double prep = g.Prep, defenders = g.Defenders, attackers = g.Attackers;
            if (g.Food > 0 && g.FoodChange < 0) c.FoodOutAt = now + 24.0 * g.Food / -g.FoodChange;
            else if (g.Food <= 0) c.FoodOutAt = now;
            int regulars = g.GarrisonRegulars;
            if (!double.IsNaN(c.FoodOutAt) && regulars > 0) c.GarrisonGoneAt = c.FoodOutAt + 24.0 * 10 * Math.Max(0, regulars - g.GarrisonWounded) / Math.Max(1, regulars);
            if (g.Cohesion >= 0 && g.CohesionPerDay < 0) c.CohesionOutAt = now + 24.0 * Math.Max(0, g.Cohesion - 0.1f) / -g.CohesionPerDay;
            double current = building.Count > 0 ? building[0].Progress : 0;
            string currentType = building.Count > 0 ? building[0].Type : null;
            double survive = 1.0;
            c.NeverByStrength = true;
            for (double t = now; t < now + SiegeHorizon; t += Step)
            {
                // building
                if (prep < 1)
                {
                    prep += g.PrepRate * Step;
                    if (prep >= 1) { c.PrepDoneAt = t + Step; prep = 1; }
                }
                else
                {
                    if (currentType == null && todo.Count > 0) { currentType = todo[0]; todo.RemoveAt(0); current = 0; }
                    if (currentType != null)
                    {
                        float rate = g.Rates.TryGetValue(currentType, out float r) ? r : 0f;
                        current += rate * Step;
                        if (current >= 1) { built.Add(currentType); c.Engines.Add(new KeyValuePair<string, double>(currentType, t + Step)); currentType = null; current = 0; }
                    }
                }
                if (prep < 1) continue;
                // the storm test
                double hoursOfSiege = t - g.StartHours;
                bool ram = built.Contains("ram") || built.Contains("improved_ram"), tower = built.Contains("siege_tower_level1");
                double e = (0.8 - (hoursOfSiege > 96 ? (hoursOfSiege - 96) * 0.02 : 0)) * (ram ? 1 : 1.25) * (tower ? 1 : 1.25);
                double adv = Advantage(g.WallLevel, g.WallsDown, built) * perk;
                double d = defenders;
                if (!double.IsNaN(c.GarrisonGoneAt) && t > c.FoodOutAt && g.GarrisonPower > 0)
                {
                    // the garrison's part of the defence falls as its men lie wounded with hunger, down to none by GarrisonGoneAt
                    double frac = Math.Min(1.0, (t - c.FoodOutAt) / Math.Max(1, c.GarrisonGoneAt - c.FoodOutAt));
                    d = Math.Max(0, defenders - g.GarrisonPower * frac);
                }
                int b = built.Count(Counts);
                double p;
                if (c.Rule != null)
                {
                    // the agent's rule in place of the game's roll (StormGate): storm at the first check it is met, or never
                    bool met = c.Rule.Now || (c.Rule.Mode == 1 && b >= c.Rule.Engines && (c.Rule.Ratio <= 0 || d <= 0 || attackers / d >= c.Rule.Ratio));
                    p = met ? 1 : 0;
                    if (met && double.IsNaN(c.RuleStormAt)) c.RuleStormAt = t;
                    if (d > 0 && t <= now + Step) c.RatioNow = attackers / (d * Math.Pow(adv, e));
                }
                else if (d <= 0) p = 1;
                else
                {
                    double ratio = attackers / (d * Math.Pow(adv, e));
                    if (t <= now + Step) c.RatioNow = ratio;
                    if (ratio <= 1) p = 0;
                    else
                    {
                        c.NeverByStrength = false;
                        double pmax = currentType != null ? current : 0;
                        double q = pmax + 0.25 * (5 - b);
                        p = 0.1 * ratio * Math.Min(3, b) / 3.0 * (1 - 0.85 * q * q);
                    }
                }
                if (p <= 0) continue;
                if (double.IsNaN(c.FirstChanceAt)) c.FirstChanceAt = t;
                survive = p >= 1 ? 0 : survive * Math.Pow(1 - Math.Min(1, p), RollsPerHour * Step);
                double stormed = 1 - survive;
                if (t - now <= 24) c.Within24 = stormed;
                if (t - now <= 48) c.Within48 = stormed;
                if (t - now <= 72) c.Within72 = stormed;
                c.Within240 = stormed;
                if (double.IsNaN(c.MedianAt) && stormed >= 0.5) c.MedianAt = t;
            }
            return c;
        }

        /// <summary>The siege's clock in words, for the report: the camp's building, when a storm could come (by the agent's own rule when it set one), a storm begun now, the food.</summary>
        public static string SiegeText(Snapshot s, SiegeRow g, SiegeClock c, bool ours, string stormNow = null, bool known = true)
        {
            var sb = new StringBuilder();
            sb.Append(g.Prep >= 1 ? "preparations done" : double.IsNaN(c.PrepDoneAt) ? "preparations not done within 10 days" : "preparations done in about " + HoursText(c.PrepDoneAt - c.Now) + " (" + Text.When(c.PrepDoneAt) + ")");
            var built = g.Engines.Where(e => e.Progress >= 1f).GroupBy(e => EngineName(e.Type)).Select(x => x.Count() + " " + x.Key + (x.Count() > 1 ? "s" : "")).ToList();
            if (built.Count > 0) sb.Append("; built: " + string.Join(", ", built));
            if (c.Engines.Count > 0) sb.Append("; next: " + string.Join(", ", c.Engines.Take(3).Select(e => EngineName(e.Key) + " in about " + HoursText(e.Value - c.Now))));
            sb.Append(". ");
            if (!known)
            {
                // only the camp is in sight: the walls' defenders, the food and so the storm's timing are not (0.11.3)
                sb.Append("The besiegers' strength is " + Text.N((int)Math.Round(g.Attackers)) + "; who defends the walls, and their food, cannot be seen from here, so neither can when a storm comes.");
                if (!double.IsNaN(c.CohesionOutAt)) sb.Append(" The besieging army holds together about " + HoursText(c.CohesionOutAt - c.Now) + " more.");
                return sb.ToString();
            }
            if (c.Rule != null)
                sb.Append("Your storm rule: " + c.Rule + "; by it the camp storms " + (double.IsNaN(c.RuleStormAt) ? "not within 10 days" : c.RuleStormAt <= c.Now + 0.2 ? "now" : "about " + Text.When(c.RuleStormAt)));
            else if (c.Within240 <= 0)
                sb.Append(c.NeverByStrength ? "At this strength the besiegers would not storm within 10 days" : "No storm is likely within 10 days");
            else
            {
                sb.Append("A storm could come from " + (c.FirstChanceAt <= c.Now + 0.2 ? "now" : "about " + Text.When(c.FirstChanceAt)) + "; chance it comes within a day " + Pct(c.Within24)
                          + ", two days " + Pct(c.Within48) + ", three " + Pct(c.Within72));
                if (!double.IsNaN(c.MedianAt)) sb.Append("; by " + Text.When(c.MedianAt) + " more likely than not");
            }
            sb.Append(" (besiegers " + Text.N((int)Math.Round(g.Attackers)) + " against defenders " + Text.N((int)Math.Round(g.Defenders)) + " times the walls' worth " + g.Advantage.ToString("0.0", Text.Inv) + "). ");
            if (stormNow != null) sb.Append(stormNow).Append(' ');
            if (!double.IsNaN(c.FoodOutAt))
                sb.Append(c.FoodOutAt <= c.Now + 0.5 ? "The place has no food: a tenth of its garrison falls wounded each day" : "Food lasts about " + HoursText(c.FoodOutAt - c.Now) + " (" + Text.When(c.FoodOutAt) + "), then a tenth of the garrison falls wounded each day");
            else sb.Append("Food holds");
            if (!double.IsNaN(c.CohesionOutAt)) sb.Append("; the besieging army holds together about " + HoursText(c.CohesionOutAt - c.Now) + " more");
            return sb.Append('.').ToString();
        }

        /// <summary>
        /// A storm begun now, run ahead by the game's battle rules (GameBattle: one round an hour, the attackers' blows growing only
        /// with the defenders' number, the walls' worth now), with the lords seen marching to it joining as they arrive: who
        /// would win, how, in how long, and with how many left. The besiegers' or the defenders' morale, when the realm cannot
        /// see it, is run low, middling and high.
        /// </summary>
        public static string StormNow(Snapshot s, SiegeRow g, bool attackersOurs, IList<Tuple<bool, int, double>> joiners)
        {
            if (g == null || g.AttackerMen <= 0) return null;
            bool town = s.SettlementById.TryGetValue(g.Settlement, out SettlementRow h) && h.Kind == "town";
            float tactics = attackersOurs && g.Besieger != null && s.PartyById.TryGetValue(g.Besieger, out PartyRow lead) ? lead.Tactics * 0.001f : 0f;
            var att = new BattleSide { Men = g.AttackerMen, Power = g.Attackers / Math.Max(1, g.AttackerMen), Advantage = 0.9f + tactics, Morale = g.AttackerMorale };
            var def = new BattleSide { Men = g.DefenderMen, Power = g.DefenderMen > 0 ? g.Defenders / g.DefenderMen : 1f, Advantage = 1f, Morale = 50, Walls = true };
            var js = (joiners ?? new List<Tuple<bool, int, double>>()).Select(j => new BattleJoiner { Attackers = j.Item1, Men = j.Item2, At = Math.Max(0, j.Item3 - s.Hours) }).ToList();
            if (g.DefenderMen <= 0) return "A storm begun now would take it at once: nobody holds the walls.";
            BattleOutcome[] runs = GameBattle.ByUnseenMorale("siege", town, g.Advantage, att, def, !attackersOurs, js);
            BattleOutcome o = runs[1];
            string coming = js.Count == 0 ? "" : ", counting the " + js.Count + (js.Count == 1 ? " lord" : " lords") + " seen marching to it (" + Text.N((int)js.Sum(j => j.Men)) + " men) as they arrive";
            if (o.Winner == null) return "A storm begun now would not end within 5 days" + coming + ".";
            string how = o.Winner == "attackers"
                ? "the besiegers would take it with about " + Text.N(o.AttackersLeft) + " of their " + Text.N(g.AttackerMen) + " men left"
                : "the defenders would hold: the besiegers " + (o.How == "rout" ? "break" : "fall") + ", " + Text.N(o.DefendersLeft) + " defenders still standing";
            return "A storm begun now would last " + GameBattle.HoursRange(runs) + ": " + how + coming + " (the attackers losing about " + Math.Round(o.AttackerLossPerHour)
                   + " men an hour at first, the defenders about " + Math.Round(o.DefenderLossPerHour) + ").";
        }

        private static string Pct(double p) => p >= 0.995 ? "almost certain" : p < 0.005 ? "almost none" : Math.Round(p * 100) + "%";

        public static string EngineName(string type)
        {
            switch (type)
            {
                case "ram": case "improved_ram": return "ram";
                case "siege_tower_level1": case "siege_tower_level2": return "siege tower";
                case "ballista": case "fire_ballista": return "ballista";
                case "onager": case "fire_onager": return "onager";
                case "trebuchet": case "fire_trebuchet": return "trebuchet";
                case "catapult": case "fire_catapult": return "catapult";
                default: return type ?? "engine";
            }
        }

        // ---------- battles ----------

        internal sealed class BattleClock
        {
            public double Now, EndsAt = double.NaN;
            /// <summary>"attackers" or "defenders": the side left standing; how: "all down" or "rout".</summary>
            public string Winner, How;
            public double AttackerLossPerHour, DefenderLossPerHour;
            public int AttackersLeft, DefendersLeft;
            public bool Storm;
            /// <summary>Run ahead at the rates measured in the battle so far (Measured), over this many hours of it.</summary>
            public bool Measured;
            public double MeasuredHours;
        }

        /// <summary>
        /// A battle under way run ahead at the rates measured in it so far (0.7.4, after Hollin Gate: by the rules' expectation the
        /// orcs would lose 16 men an hour and break in 3.4 days; over the first 22 hours they lost about 7 an hour, kept their
        /// morale and wore the defenders from 197 down to 65). The battle is run by the rules from its start for the hours it has
        /// lasted; each side's blows are scaled by what it actually knocked down against what the rules expected (0.2 to 5 times);
        /// the rest is run ahead with those blows. The other side's morale, which the realm cannot see, is taken as middling in
        /// both runs, so the measure carries it. Null before two rounds or with too few men fallen to measure.
        /// </summary>
        public static BattleClock Measured(BattleRow b, double now, bool? ownAttackers, IEnumerable<Tuple<bool, int, double>> joiners = null, double horizon = 24 * 5)
        {
            if (b?.Attackers == null || b.Defenders == null) return null;
            double ran = now - b.StartHours;
            int perHour = b.Kind == "siege" ? 1 : 2;
            if (ran * perHour < 2 || b.Attackers.StartHealthy <= 0 || b.Defenders.StartHealthy <= 0 || b.Attackers.Healthy <= 0 || b.Defenders.Healthy <= 0) return null;
            float? mA = ownAttackers == true ? (float?)null : 50f, mD = ownAttackers == false ? (float?)null : 50f;
            BattleSide a0 = SideOf(b.Attackers, mA, false), d0 = SideOf(b.Defenders, mD, b.Defenders.SettlementLed);
            a0.Men = b.Attackers.StartHealthy;
            d0.Men = b.Defenders.StartHealthy;
            BattleOutcome past = GameBattle.Run(b.Kind, b.Town, b.SettlementAdvantage, a0, d0, null, ran);
            double expA = b.Attackers.StartHealthy - past.AttackersLeft, expD = b.Defenders.StartHealthy - past.DefendersLeft;
            double obsA = b.Attackers.Casualties, obsD = b.Defenders.Casualties;
            if (expA < 2 || expD < 2 || obsA + obsD < 5) return null;
            double Clamp(double k) => Math.Max(0.2, Math.Min(5, k));
            BattleSide a = SideOf(b.Attackers, mA, false), d = SideOf(b.Defenders, mD, b.Defenders.SettlementLed);
            a.Hit = Clamp(obsD / expD);   // the attackers' blows are what knock the defenders down
            d.Hit = Clamp(obsA / expA);
            BattleOutcome o = GameBattle.Run(b.Kind, b.Town, b.SettlementAdvantage, a, d, Joiners(joiners, now), horizon);
            var c = new BattleClock { Now = now, Storm = b.Kind == "siege", Measured = true, MeasuredHours = ran };
            Fill(c, o, now);
            return c;
        }

        /// <summary>
        /// One battle run ahead with the game's own rules (Common.GameBattle: its rounds, strikes, hit chance, lost rounds,
        /// routs and retreats). Joiners: (side, men, the hour they arrive), with their side's mean power.
        /// </summary>
        public static BattleClock Battle(BattleRow b, double now, IEnumerable<Tuple<bool, int, double>> joiners = null, float? attackerMorale = null, float? defenderMorale = null, double horizon = 24 * 5)
        {
            var c = new BattleClock { Now = now, Storm = b.Kind == "siege" };
            BattleOutcome o = GameBattle.Run(b.Kind, b.Town, b.SettlementAdvantage, SideOf(b.Attackers, attackerMorale, false), SideOf(b.Defenders, defenderMorale, b.Defenders.SettlementLed),
                                             Joiners(joiners, now), horizon);
            Fill(c, o, now);
            return c;
        }

        /// <summary>The same with the other side's unseen morale low, middling and high (its middling run, and the three's hours in words).</summary>
        public static BattleClock BattleByMorale(BattleRow b, double now, bool attackersUnseen, IEnumerable<Tuple<bool, int, double>> joiners, out string hours, double horizon = 24 * 5)
        {
            BattleOutcome[] runs = GameBattle.ByUnseenMorale(b.Kind, b.Town, b.SettlementAdvantage, SideOf(b.Attackers, null, false), SideOf(b.Defenders, null, b.Defenders.SettlementLed),
                                                              attackersUnseen, Joiners(joiners, now), horizon);
            var c = new BattleClock { Now = now, Storm = b.Kind == "siege" };
            Fill(c, runs[1], now);
            hours = GameBattle.HoursRange(runs);
            return c;
        }

        private static BattleSide SideOf(BattleSideRow r, float? morale, bool walls) =>
            new BattleSide { Men = r.Healthy, Power = r.Power, Advantage = r.Advantage, Morale = morale ?? r.Morale, Walls = walls };

        private static List<BattleJoiner> Joiners(IEnumerable<Tuple<bool, int, double>> joiners, double now) =>
            (joiners ?? Enumerable.Empty<Tuple<bool, int, double>>()).Select(j => new BattleJoiner { Attackers = j.Item1, Men = j.Item2, At = Math.Max(0, j.Item3 - now) }).ToList();

        private static void Fill(BattleClock c, BattleOutcome o, double now)
        {
            c.EndsAt = double.IsNaN(o.Hours) ? double.NaN : now + o.Hours;
            c.Winner = o.Winner;
            c.How = o.How;
            c.AttackerLossPerHour = o.AttackerLossPerHour;
            c.DefenderLossPerHour = o.DefenderLossPerHour;
            c.AttackersLeft = o.AttackersLeft;
            c.DefendersLeft = o.DefendersLeft;
        }

        /// <summary>The game's strikes per round for each side (GameBattle).</summary>
        public static void StrikesPerRound(bool storm, bool town, float advantage, double attackers, double defenders, out double byAttackers, out double byDefenders)
            => GameBattle.StrikesPerRound(storm, town, advantage, attackers, defenders, out byAttackers, out byDefenders);

        /// <summary>
        /// A field battle between some of the realm's lords and an enemy, by the game's battle rules (GameBattle), from what the realm
        /// knows: its own parties' men, strength (their mean troop power), morale and leader's Tactics; the enemy's men and strength
        /// as seen (or last seen), its morale run low, middling and high, its leader's skill not known (no advantage). The lords
        /// are counted as one side joined at the start.
        /// </summary>
        public static BattleOutcome[] FieldOdds(IList<PartyRow> ours, double enemyMen, double enemyStrength, out BattleSide mine)
        {
            mine = null;
            if (ours == null || ours.Count == 0 || enemyMen <= 0) return null;
            double men = ours.Sum(r => (double)(r.ArmyMen > 0 ? r.ArmyMen : r.Men)), strength = ours.Sum(r => (double)(r.ArmyStrength > 0 ? r.ArmyStrength : r.Strength));
            if (men <= 0) return null;
            double morale = ours.Sum(r => (r.Morale >= 0 ? r.Morale : 50) * (r.ArmyMen > 0 ? r.ArmyMen : r.Men)) / men;
            PartyRow lead = ours.OrderByDescending(r => r.ArmyMen > 0 ? r.ArmyMen : r.Men).First();
            mine = new BattleSide { Men = men, Power = (float)(strength / men), Advantage = 1f + 0.001f * lead.Tactics, Morale = (float)morale };
            var theirs = new BattleSide { Men = enemyMen, Power = (float)(enemyStrength / enemyMen), Advantage = 1f, Morale = 50 };
            return GameBattle.ByUnseenMorale("field", false, 1f, mine, theirs, false);
        }

        /// <summary>"In the field, by the game's battle rules: Bolg alone (139 men) would lose to it in about 3 hours (...); Bolg, Othrod and Ufza together ..."</summary>
        public static string FieldOddsText(Tools.World w, IList<PartyRow> soonest, PartyRow enemy)
        {
            if (soonest == null || soonest.Count == 0 || enemy == null) return null;
            int men = w.KnownMen(enemy.Id) ?? 0;
            double strength = w.KnownStrength(enemy.Id) ?? 0;
            if (men <= 0 || strength <= 0) return null;
            string One(IList<PartyRow> side)
            {
                BattleOutcome[] runs = FieldOdds(side, men, strength, out BattleSide mine);
                if (runs == null) return null;
                BattleOutcome o = runs[1];
                List<string> names = side.Select(r => w.Name(r.Id)).ToList();
                string who = names.Count == 1 ? names[0] + " alone" : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1] + " together";
                string head = who + " (" + Text.N((int)mine.Men) + " men)";
                if (o.Winner == null) return head + " would still be fighting after 5 days";
                bool win = o.Winner == "attackers";
                string how = o.How == "rout" ? (win ? ", its morale breaking" : ", yours breaking") : o.How == "retreat" ? (win ? ", it retreating" : ", yours retreating") : "";
                return head + (win ? " would beat it" : " would lose to it") + how + " in " + GameBattle.HoursRange(runs) + ", " + Text.N(win ? o.AttackersLeft : o.DefendersLeft)
                       + (win ? " of yours" : " of its men") + " still standing";
            }
            var parts = new List<string>();
            string first = One(soonest.Take(1).ToList());
            if (first != null) parts.Add(first);
            if (soonest.Count > 1) { string all = One(soonest); if (all != null) parts.Add(all); }
            return parts.Count == 0 ? null : "If they fought in the field, by the game's battle rules (two rounds an hour; nobody leaves a battle once in it): " + string.Join("; ", parts) + ".";
        }

        // a village's health at the last two hours it was looked at, to measure a raid's pace (worker only)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, double[]> RaidSeen = new System.Collections.Concurrent.ConcurrentDictionary<string, double[]>(StringComparer.Ordinal);

        /// <summary>
        /// A raid whose defenders are down (0.7.4: the battle clock had it "won in about an hour" every hour). The game loots the
        /// village when its health runs out (RaidEventComponent: each hour the raiders take (the square root of their number + 5)
        /// / 900 of it, DefaultRaidModel, TAOM adding some peoples' and lords' gifts). The pace is measured from the health's fall
        /// between the last two hours seen when it can be, else the base rule. Returns the hours until it is looted.
        /// </summary>
        public static double RaidHoursLeft(SettlementRow v, double now, double raiders, out double perHour, out bool measured)
        {
            perHour = (Math.Sqrt(Math.Max(0, raiders)) + 5) / 900.0;
            measured = false;
            if (v == null) return double.NaN;
            double[] h = RaidSeen.GetOrAdd(v.Id, _ => new[] { double.NaN, 0, double.NaN, 0 });   // previous hour, its health, last hour, its health
            lock (h)
            {
                if (double.IsNaN(h[2]) || now < h[2] - 0.01) { h[0] = double.NaN; h[2] = now; h[3] = v.HitPoints; }
                else if (now - h[2] >= 0.5) { h[0] = h[2]; h[1] = h[3]; h[2] = now; h[3] = v.HitPoints; }
                double gap = h[2] - h[0];
                if (!double.IsNaN(h[0]) && gap >= 0.5 && gap <= 6 && h[1] > h[3] + 1e-4) { perHour = (h[1] - h[3]) / gap; measured = true; }
            }
            return v.HitPoints / Math.Max(1e-4, perHour);
        }

        public static string KindText(string kind)
        {
            switch (kind)
            {
                case "siege": return "a storm of the walls";
                case "sally": return "a sally from the walls";
                case "siege_outside": return "a battle at the siege camp";
                case "raid": return "a raid";
                case "field": return "a battle in the field";
                default: return "a fight";
            }
        }
    }

    /// <summary>
    /// The walking map's ground, for the worker: the game's TerrainType at the centre of every map unit (MapExport's ground.bin),
    /// read once per map. Null until the map has been copied; the smoke test sets one of its own.
    /// </summary>
    internal sealed class MapGround
    {
        private readonly byte[] _cells;
        private readonly float _x0, _y0, _cell;
        private readonly int _w, _h;
        public readonly string Crc;

        private static MapGround _current;
        private static readonly object Gate = new object();
        /// <summary>Set by a test to stand in for the game's map.</summary>
        public static MapGround Override;

        public MapGround(string crc, byte[] cells, float x0, float y0, float cell, int w, int h)
        {
            Crc = crc; _cells = cells; _x0 = x0; _y0 = y0; _cell = cell <= 0 ? 1 : cell; _w = w; _h = h;
        }

        public int TerrainAt(float x, float y)
        {
            int cx = (int)Math.Floor((x - _x0) / _cell), cy = (int)Math.Floor((y - _y0) / _cell);
            if (cx < 0 || cy < 0 || cx >= _w || cy >= _h) return GamePace.Plain;
            byte g = _cells[cy * _w + cx];
            return g == 0 ? GamePace.Plain : g;
        }

        public static MapGround For(string crc)
        {
            if (Override != null) return Override;
            if (string.IsNullOrEmpty(crc)) return null;
            MapGround m = _current;
            if (m != null && m.Crc == crc) return m;
            lock (Gate)
            {
                if (_current != null && _current.Crc == crc) return _current;
                try
                {
                    string dir = Path.Combine(Paths.Root, "map", crc);
                    string meta = Path.Combine(dir, "meta.json"), ground = Path.Combine(dir, "ground.bin");
                    if (!File.Exists(meta) || !File.Exists(ground)) return null;
                    JObject j = JObject.Parse(File.ReadAllText(meta));
                    int w = (int)j["width"], h = (int)j["height"];
                    byte[] cells = File.ReadAllBytes(ground);
                    if (cells.Length != w * h) return null;
                    _current = new MapGround(crc, cells, (float)j["min_x"], (float)j["min_y"], (float?)j["cell"] ?? 1f, w, h);
                    return _current;
                }
                catch (Exception e) { Diag.Swallowed("reading the walking map's ground for march times", e); return null; }
            }
        }
    }
}
