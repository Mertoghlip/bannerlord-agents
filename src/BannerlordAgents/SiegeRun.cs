using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// A siege run ahead with its engines and walls (0.20.0): the camp's preparations, then its engines one at a time as the drill or the game's
    /// own strategy chooses them (SiegeDrill.Attacker, the very choice the game's hook makes), the defenders' engines likewise, and the
    /// bombardment between them by the game's rules (1.4.8, SiegeEvent.BombardTick and BesiegerCamp.GetAttackTarget): every working throwing
    /// engine shoots once each reload; a besieger's shot goes at the defenders' engine nearest its spot or at the wall stretch nearest it, at
    /// random weighted by their priorities (an engine 4 less the spots between them, a stretch 3 less the distance to it; ballistas never aim
    /// at walls); a defender's shot only at the besiegers' engine nearest its spot; a hit takes the shooter's damage off the engine's hit points
    /// or off the stretch (its share of one stretch's hit points); walls are never mended during a siege. Expected values, a tenth of an hour at
    /// a time. The storm test is the game's (see Forecast.Siege), with the walls' worth falling to a quarter once both stretches are down.
    /// </summary>
    internal static class SiegeRun
    {
        private const double Horizon = 24 * 10, Step = 0.1;

        private sealed class Eng
        {
            public string Type;
            public double Progress, Hp, Redeploy = 1;
            public bool Built => Progress >= 1 - 1e-9;
            public bool Active => Built && Redeploy >= 1 - 1e-9;
        }

        private sealed class Side
        {
            public Eng[] Melee;
            public readonly Eng[] Ranged = new Eng[4];
            public readonly List<Eng> Reserve = new List<Eng>();
            public double Prep = 1, PrepRate;
            public Dictionary<string, float> Rates;
            public string Key;   // "a:" or "d:"
            public int ActiveRanged() { int n = 0; foreach (Eng e in Ranged) if (e != null && e.Active) n++; return n; }
            public int ActiveOf(string type) { int n = 0; foreach (Eng e in Ranged) if (e != null && e.Active && e.Type == type) n++; return n; }
        }

        /// <summary>The defenders' strategies' engines (DefaultSiegeStrategyActionModel), in the order the lists name them.</summary>
        private static readonly Dictionary<string, string[]> DefenderLists = new Dictionary<string, string[]>
        {
            ["siege_strategy_prepare_against_assault"] = new[] { "fire_catapult", "catapult", "catapult", "fire_ballista" },
            ["siege_strategy_counter_bombardment"] = new[] { "fire_catapult", "fire_catapult", "catapult", "catapult" }
        };

        /// <summary>The game's numbers when the copy has none (siegeengines.xml, 1.4.8): damage to a wall, to an engine, hit chance on a wall, on an engine (a besieger's less 0.05 a wall level), hit points, minutes between shots.</summary>
        public static float[] DefaultStats(string key, int wallLevel)
        {
            float less = 0.05f * Math.Max(1, wallLevel);
            switch (key)
            {
                case "a:trebuchet": return new[] { 1360f, 1360f, 0.58f, 0.58f - less, 3200f, 144f };
                case "a:onager": return new[] { 640f, 640f, 0.62f, 0.62f - less, 1500f, 103f };
                case "a:fire_onager": return new[] { 768f, 768f, 0.62f, 0.62f - less, 1500f, 103f };
                case "a:ballista": return new[] { 160f, 160f, 0.52f, 0.52f - less, 500f, 72f };
                case "a:fire_ballista": return new[] { 192f, 192f, 0.52f, 0.52f - less, 500f, 72f };
                case "d:catapult": return new[] { 440f, 440f, 0.28f, 0.28f, 1200f, 103f };
                case "d:fire_catapult": return new[] { 528f, 528f, 0.28f, 0.28f, 1200f, 103f };
                case "d:ballista": return new[] { 160f, 160f, 0.52f, 0.52f, 500f, 72f };
                case "d:fire_ballista": return new[] { 192f, 192f, 0.52f, 0.52f, 500f, 72f };
                default: return null;
            }
        }

        /// <summary>The walls' hit points, both stretches together, by wall level (DefaultWallHitPointCalculationModel).</summary>
        public static float WallTotal(int wallLevel) => wallLevel <= 1 ? 30000f : wallLevel == 2 ? 50000f : 67000f;

        private static bool AntiPersonnel(string type) => type == "ballista" || type == "fire_ballista";

        public static Forecast.SiegeClock Run(SiegeRow g, double now, StormRule rule)
        {
            var c = new Forecast.SiegeClock { Now = now, Rule = rule != null && (rule.Mode != 0 || rule.Now) ? rule : null };
            if (g == null) return c;
            int wl = Math.Max(1, g.WallLevel);
            float[] Stat(string key) => g.Stats != null && g.Stats.TryGetValue(key, out float[] st) && st != null && st.Length >= 6 ? st : DefaultStats(key, wl);
            double sectionHp = g.WallSectionHp > 0 ? g.WallSectionHp : WallTotal(wl) / 2.0;
            var walls = new List<double>();
            if (g.WallSections != null && g.WallSections.Count > 0) walls.AddRange(g.WallSections.Select(w => (double)w));
            else { walls.Add(g.WallsDown ? 0 : g.WallShare); walls.Add(g.WallsDown ? 0 : g.WallShare); }
            bool Down() => walls.All(w => w <= 1e-6);

            var att = new Side { Melee = new Eng[3], Prep = g.Prep, PrepRate = g.PrepRate, Rates = g.Rates ?? new Dictionary<string, float>(), Key = "a:" };
            var def = new Side { Melee = new Eng[0], Prep = 1, Rates = g.DefRates ?? new Dictionary<string, float>(), Key = "d:" };
            Load(att, g.Engines, Stat);
            Load(def, g.DefEngines, Stat);

            string mode = g.Drill ?? "game";
            DrillRule drill = new DrillRule { Mode = mode == "rams" ? 2 : 1, Trebuchets = Math.Max(1, Math.Min(4, g.DrillTrebuchets)), Fire = g.DrillFire };
            bool fireCase = mode == "drill" && g.DrillFire && g.PlayerInside;
            var view = new SiegeDrill.SideView();
            Fill(view, att, att.Prep >= 1);
            bool rolledOut = g.RolledOut || ((mode == "drill") && SiegeDrill.LooksRolledOut(view, walls.Any(w => w < 0.999)));
            if (rolledOut && (mode == "drill")) c.RolledOutAt = now;
            c.Mode = mode;
            c.FireCase = fireCase;

            // the perks' share of the walls' worth: the game's number now over the formula's for the same engines
            List<string> builtNow = Built(att);
            double formula = Forecast.Advantage(g.WallLevel, Down(), builtNow);
            double perk = formula > 0 && g.Advantage > 0 ? g.Advantage / formula : 1.0;
            double defenders = g.Defenders, attackers = g.Attackers;
            // the place's store, then the food of the lords inside, which it draws on once its own runs out (0.19.5, SiegeFood)
            if (g.FoodChange < 0 && g.Food + g.LordsFood > 0) c.FoodOutAt = now + 24.0 * SiegeFoodRules.Days(g.Food, -g.FoodChange, g.LordsFood, g.LordsEat);
            else if (g.Food <= 0 && g.LordsFood <= 0) c.FoodOutAt = now;
            int regulars = g.GarrisonRegulars;
            if (!double.IsNaN(c.FoodOutAt) && regulars > 0) c.GarrisonGoneAt = c.FoodOutAt + 24.0 * 10 * Math.Max(0, regulars - g.GarrisonWounded) / Math.Max(1, regulars);
            if (g.Cohesion >= 0 && g.CohesionPerDay < 0) c.CohesionOutAt = now + 24.0 * Math.Max(0, g.Cohesion - 0.1f) / -g.CohesionPerDay;
            if (Down()) { c.WallsDownAt = now; c.AdvDown = g.Advantage; }
            double survive = 1.0;
            c.NeverByStrength = true;
            var hitAtt = new double[4];
            var hitDef = new double[4];
            var hitWall = new double[walls.Count];

            for (double t = now; t < now + Horizon; t += Step)
            {
                // building: the camp's preparations first; then, on each side, the first engine in its spot not yet finished; engines back from the reserve take 2 hours
                if (att.Prep < 1)
                {
                    att.Prep += att.PrepRate * Step;
                    if (att.Prep >= 1) { att.Prep = 1; c.PrepDoneAt = t + Step; }
                }
                else Construct(att, t, c);
                Construct(def, t, null);
                // the next steps, by the drill (or the game's own lists)
                for (int k = 0; k < 6; k++)
                {
                    Fill(view, att, att.Prep >= 1);
                    SiegeDrill.Act a;
                    if (mode == "drill" || mode == "rams")
                    {
                        a = SiegeDrill.Attacker(view, drill, g.PlayerInside, rolledOut, def.ActiveRanged(), out bool rollNow);
                        if (rollNow && !rolledOut) { rolledOut = true; if (double.IsNaN(c.RolledOutAt)) c.RolledOutAt = t; }
                    }
                    else if (mode == "player") a = SiegeDrill.Act.Hold;
                    else a = ByList(view, Forecast.BuildOrder.TryGetValue(g.Strategy ?? "", out string[] o) ? o : Forecast.BuildOrder["siege_strategy_prepare_assault"]);
                    if (a.Kind == SiegeDrill.Kind.Hold) break;
                    Apply(att, a, Stat);
                }
                for (int k = 0; k < 6; k++)
                {
                    Fill(view, def, true);
                    SiegeDrill.Act a;
                    if (g.PlayerCamp && mode != "off") a = SiegeDrill.Defender(view);
                    else if (g.PlayerInside) a = SiegeDrill.Act.Hold;   // the player's own choices: what stands now
                    else a = DefenderLists.TryGetValue(g.DefStrategy ?? "", out string[] dl) ? ByList(view, dl) : SiegeDrill.Act.Hold;
                    if (a.Kind == SiegeDrill.Kind.Hold) break;
                    Apply(def, a, Stat);
                }
                // the bombardment, both sides at once
                Array.Clear(hitAtt, 0, 4);
                Array.Clear(hitDef, 0, 4);
                Array.Clear(hitWall, 0, hitWall.Length);
                for (int i = 0; i < 4; i++)
                {
                    Eng e = att.Ranged[i];
                    if (e == null || !e.Active) continue;
                    float[] st = Stat("a:" + e.Type);
                    if (st == null || st[5] <= 0) continue;
                    double shots = 60.0 / st[5] * Step;
                    int j = Nearest(def, i, out double pe);
                    int w = -1;
                    double pw = 0;
                    if (!AntiPersonnel(e.Type))
                        for (int x = 0; x < walls.Count; x++)
                        {
                            if (walls[x] <= 1e-6) continue;
                            double pr = 3 - Math.Abs(i - (0.5 + 2 * x));
                            if (w < 0 || pr > pw) { w = x; pw = pr; }
                        }
                    if (j < 0 && w < 0) continue;
                    double toEngine = j < 0 ? 0 : w < 0 ? 1 : pe / (pe + pw);
                    if (j >= 0) hitDef[j] += shots * toEngine * st[3] * st[1];
                    if (w >= 0) hitWall[w] += shots * (1 - toEngine) * st[2] * st[0] / sectionHp;
                }
                for (int i = 0; i < 4; i++)
                {
                    Eng e = def.Ranged[i];
                    if (e == null || !e.Active) continue;
                    float[] st = Stat("d:" + e.Type);
                    if (st == null || st[5] <= 0) continue;
                    int j = Nearest(att, i, out _);
                    if (j >= 0) hitAtt[j] += 60.0 / st[5] * Step * st[3] * st[1];
                }
                for (int i = 0; i < 4; i++)
                {
                    if (att.Ranged[i] != null && hitAtt[i] > 0 && (att.Ranged[i].Hp -= hitAtt[i]) <= 0) att.Ranged[i] = null;
                    if (def.Ranged[i] != null && hitDef[i] > 0 && (def.Ranged[i].Hp -= hitDef[i]) <= 0) def.Ranged[i] = null;
                }
                for (int x = 0; x < walls.Count; x++) walls[x] = Math.Max(0, walls[x] - hitWall[x]);
                bool down = Down();
                List<string> built = Built(att);
                double adv = Forecast.Advantage(g.WallLevel, down, built) * perk;
                if (down && double.IsNaN(c.WallsDownAt)) { c.WallsDownAt = t + Step; c.AdvDown = adv; }
                if (rolledOut && double.IsNaN(c.SilencedAt) && def.ActiveRanged() == 0 && att.ActiveRanged() > 0) c.SilencedAt = t + Step;
                if (fireCase && double.IsNaN(c.SwapDoneAt) && att.ActiveOf(SiegeDrill.FireOnager) >= SiegeDrill.FireNeeded) c.SwapDoneAt = t + Step;
                if (att.Prep < 1) continue;
                // the storm test (BesiegerCamp.StartingAssaultOnBesiegedSettlementIsLogical)
                double hoursOfSiege = t - g.StartHours;
                bool ram = built.Contains("ram") || built.Contains("improved_ram"), tower = built.Contains("siege_tower_level1");
                double e2 = (0.8 - (hoursOfSiege > 96 ? (hoursOfSiege - 96) * 0.02 : 0)) * (ram ? 1 : 1.25) * (tower ? 1 : 1.25);
                double d = defenders;
                if (!double.IsNaN(c.GarrisonGoneAt) && t > c.FoodOutAt && g.GarrisonPower > 0)
                {
                    // the garrison's part of the defence falls as its men lie wounded with hunger, down to none by GarrisonGoneAt
                    double frac = Math.Min(1.0, (t - c.FoodOutAt) / Math.Max(1, c.GarrisonGoneAt - c.FoodOutAt));
                    d = Math.Max(0, defenders - g.GarrisonPower * frac);
                }
                int b = built.Count(Forecast.CountsAsBuilt);
                double pmax = 0;
                foreach (Eng x in att.Melee.Concat(att.Ranged)) if (x != null && !x.Built && x.Redeploy >= 1) { pmax = x.Progress; break; }
                double p;
                if (c.Rule != null)
                {
                    // the agent's rule in place of the game's roll (StormGate): storm at the first check it is met, or never
                    bool met = c.Rule.Now
                               || (c.Rule.Mode == 1 && b >= c.Rule.Engines && (c.Rule.Ratio <= 0 || d <= 0 || attackers / d >= c.Rule.Ratio))
                               || (c.Rule.Mode == 3 && down && (!fireCase || (def.ActiveRanged() == 0 && att.ActiveOf(SiegeDrill.FireOnager) >= SiegeDrill.FireNeeded)));
                    p = met ? 1 : 0;
                    if (met && double.IsNaN(c.RuleStormAt)) c.RuleStormAt = t;
                    if (d > 0 && t <= now + Step) c.RatioNow = attackers / (d * Math.Pow(adv, e2));
                }
                else if (d <= 0) p = 1;
                else
                {
                    double ratio = attackers / (d * Math.Pow(adv, e2));
                    if (t <= now + Step) c.RatioNow = ratio;
                    if (ratio <= 1) p = 0;
                    else
                    {
                        c.NeverByStrength = false;
                        double q = pmax + 0.25 * (5 - b);
                        p = 0.1 * ratio * Math.Min(3, b) / 3.0 * (1 - 0.85 * q * q);
                    }
                }
                if (p <= 0) continue;
                if (double.IsNaN(c.FirstChanceAt)) c.FirstChanceAt = t;
                survive = p >= 1 ? 0 : survive * Math.Pow(1 - Math.Min(1, p), Forecast.RollsPerHour * Step);
                double stormed = 1 - survive;
                if (t - now <= 24) c.Within24 = stormed;
                if (t - now <= 48) c.Within48 = stormed;
                if (t - now <= 72) c.Within72 = stormed;
                c.Within240 = stormed;
                if (double.IsNaN(c.MedianAt) && stormed >= 0.5) c.MedianAt = t;
            }
            return c;
        }

        private static void Load(Side side, List<EngineRow> rows, Func<string, float[]> stat)
        {
            if (rows == null) return;
            foreach (EngineRow r in rows)
            {
                if (r?.Type == null) continue;
                var e = new Eng { Type = r.Type, Progress = r.Progress, Hp = r.Hitpoints > 0 ? r.Hitpoints : (stat(side.Key + r.Type)?[4] ?? 1000), Redeploy = r.Reserve ? 0 : r.Redeploy };
                if (r.Reserve) { side.Reserve.Add(e); continue; }
                bool ranged = r.Slot >= 0 ? r.Ranged : SiegeDrill.IsRanged(r.Type);
                Eng[] slots = ranged ? side.Ranged : side.Melee;
                int at = r.Slot >= 0 && r.Slot < slots.Length && slots[r.Slot] == null ? r.Slot : Array.IndexOf(slots, null);
                if (at >= 0) slots[at] = e; else side.Reserve.Add(e);
            }
        }

        private static void Fill(SiegeDrill.SideView v, Side side, bool prepDone)
        {
            v.Clear(side.Melee.Length);
            v.PrepDone = prepDone;
            for (int i = 0; i < side.Melee.Length; i++) v.Melee[i] = SlotOf(side.Melee[i]);
            for (int i = 0; i < 4; i++) v.Ranged[i] = SlotOf(side.Ranged[i]);
            foreach (Eng e in side.Reserve) { v.Reserve.Add(e.Type); v.ReserveBuilt.Add(e.Built); }
        }

        private static SiegeDrill.Slot SlotOf(Eng e) => e == null ? default(SiegeDrill.Slot) : new SiegeDrill.Slot { Type = e.Type, Built = e.Built, Redeploying = e.Redeploy < 1 - 1e-9 };

        private static void Construct(Side side, double t, Forecast.SiegeClock c)
        {
            Eng current = null;
            foreach (Eng e in side.Melee.Concat(side.Ranged)) if (e != null && !e.Built && e.Redeploy >= 1 - 1e-9) { current = e; break; }
            if (current != null)
            {
                float rate = side.Rates.TryGetValue(current.Type, out float r) ? r : current.Type == SiegeDrill.FireOnager && side.Rates.TryGetValue("onager", out float ro) ? ro : 0f;
                current.Progress += rate * Step;
                if (current.Progress >= 1) { current.Progress = 1; c?.Engines.Add(new KeyValuePair<string, double>(current.Type, t + Step)); }
            }
            foreach (Eng e in side.Melee.Concat(side.Ranged))
                if (e != null && e.Built && e.Redeploy < 1) e.Redeploy = Math.Min(1, e.Redeploy + 0.5 * Step);
        }

        private static void Apply(Side side, SiegeDrill.Act a, Func<string, float[]> stat)
        {
            Eng[] slots = a.Ranged ? side.Ranged : side.Melee;
            if (a.Slot < 0 || a.Slot >= slots.Length) return;
            switch (a.Kind)
            {
                case SiegeDrill.Kind.Build:
                    if (slots[a.Slot] != null) { slots[a.Slot].Redeploy = 0; side.Reserve.Add(slots[a.Slot]); }
                    slots[a.Slot] = new Eng { Type = a.Type, Progress = 0, Hp = stat(side.Key + a.Type)?[4] ?? 5000 };
                    break;
                case SiegeDrill.Kind.Deploy:
                    if (a.Reserve < 0 || a.Reserve >= side.Reserve.Count) return;
                    Eng e = side.Reserve[a.Reserve];
                    side.Reserve.RemoveAt(a.Reserve);
                    if (slots[a.Slot] != null) { slots[a.Slot].Redeploy = 0; side.Reserve.Add(slots[a.Slot]); }
                    slots[a.Slot] = e;
                    break;
                case SiegeDrill.Kind.ToReserve:
                    if (slots[a.Slot] == null) return;
                    slots[a.Slot].Redeploy = 0;
                    side.Reserve.Add(slots[a.Slot]);
                    slots[a.Slot] = null;
                    break;
                case SiegeDrill.Kind.Scrap:
                    slots[a.Slot] = null;
                    break;
            }
        }

        /// <summary>The game's own lists, simplified: a needed engine from the reserve first, else the first missing one built in a free spot, one at a time.</summary>
        private static SiegeDrill.Act ByList(SiegeDrill.SideView v, string[] list)
        {
            foreach (SiegeDrill.Slot s in v.Melee) if (!s.Empty && !s.Built) return SiegeDrill.Act.Hold;
            foreach (SiegeDrill.Slot s in v.Ranged) if (!s.Empty && !s.Built) return SiegeDrill.Act.Hold;
            if (!v.PrepDone) return SiegeDrill.Act.Hold;
            var have = new List<string>();
            foreach (SiegeDrill.Slot s in v.Melee) if (!s.Empty) have.Add(s.Type);
            foreach (SiegeDrill.Slot s in v.Ranged) if (!s.Empty) have.Add(s.Type);
            foreach (string t in list)
            {
                if (have.Remove(t)) continue;
                bool ranged = SiegeDrill.IsRanged(t);
                SiegeDrill.Slot[] slots = ranged ? v.Ranged : v.Melee;
                int free = -1;
                for (int i = 0; i < slots.Length; i++) if (slots[i].Empty) { free = i; break; }
                if (free < 0) continue;
                for (int i = 0; i < v.Reserve.Count; i++)
                    if (v.Reserve[i] == t && v.ReserveBuilt[i]) return new SiegeDrill.Act { Kind = SiegeDrill.Kind.Deploy, Type = t, Ranged = ranged, Slot = free, Reserve = i };
                return new SiegeDrill.Act { Kind = SiegeDrill.Kind.Build, Type = t, Ranged = ranged, Slot = free, Reserve = -1 };
            }
            return SiegeDrill.Act.Hold;
        }

        /// <summary>The other side's working throwing engine nearest a spot (the game's priority: 4 less the spots between them), with that priority; -1 when none.</summary>
        private static int Nearest(Side other, int slot, out double priority)
        {
            int best = -1;
            priority = 0;
            for (int j = 0; j < 4; j++)
            {
                Eng e = other.Ranged[j];
                if (e == null || !e.Active || e.Hp <= 0) continue;
                double p = 4 - Math.Abs(slot - j);
                if (best < 0 || p > priority) { best = j; priority = p; }
            }
            return best;
        }

        private static List<string> Built(Side side)
        {
            var list = new List<string>();
            foreach (Eng e in side.Melee.Concat(side.Ranged)) if (e != null && e.Built) list.Add(e.Type);
            foreach (Eng e in side.Reserve) if (e.Built) list.Add(e.Type);
            return list;
        }
    }
}
