namespace BannerlordAgents.Routes
{
    /// <summary>
    /// How much each kind of ground slows a march: the game's own numbers (DefaultPartySpeedCalculatingModel, 1.4.8) until the
    /// dashboard has measured enough marches to say what this game really does (speed_factors.json beside the map).
    /// </summary>
    public sealed class SpeedFactors
    {
        public Dictionary<string, float> ByGround { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, int> Samples { get; set; } = new Dictionary<string, int>();
        public float Night { get; set; } = 0.75f;
        public int NightSamples { get; set; }
        public string Source { get; set; } = "the game's own rules (1.4.8)";
        public string Updated { get; set; }

        public static SpeedFactors Default()
        {
            var f = new SpeedFactors();
            f.ByGround[Ground.Forest.ToString()] = 0.7f;
            f.ByGround[Ground.Fording.ToString()] = 0.7f;
            f.ByGround[Ground.Bridge.ToString()] = 0.7f;
            f.ByGround[Ground.Desert.ToString()] = 0.9f;
            f.ByGround[Ground.Dune.ToString()] = 0.9f;
            return f;
        }

        public float Of(byte ground) => ByGround.TryGetValue(ground.ToString(), out float v) && v > 0.05f ? v : 1f;

        /// <summary>The measured factors where enough marches were seen (at least 30), the game's own numbers elsewhere.</summary>
        public static SpeedFactors Load(string mapDir)
        {
            SpeedFactors d = Default();
            SpeedFactors m = mapDir == null ? null : JsonText.Read<SpeedFactors>(Path.Combine(mapDir, "speed_factors.json"));
            if (m == null) return d;
            int used = 0;
            foreach (KeyValuePair<string, float> kv in m.ByGround)
                if (m.Samples.TryGetValue(kv.Key, out int n) && n >= 30 && kv.Value > 0.05f && kv.Value < 2f) { d.ByGround[kv.Key] = kv.Value; d.Samples[kv.Key] = n; used++; }
            if (m.NightSamples >= 30 && m.Night > 0.05f && m.Night <= 1.2f) { d.Night = m.Night; d.NightSamples = m.NightSamples; used++; }
            if (used > 0) { d.Source = "measured in this game's marches where seen often enough, else the game's own rules"; d.Updated = m.Updated; }
            return d;
        }
    }

    public static class Marching
    {
        public const int HoursInDay = 24;

        public static bool IsNight(double hours, int sunRise, int sunSet)
        {
            int h = (int)Math.Floor(((hours % HoursInDay) + HoursInDay) % HoursInDay);
            return !(h >= sunRise && h < sunSet);
        }

        /// <summary>
        /// Hours to march a way at a party's pace, by the game's own rules (GamePace: its speed sum with each stretch's ground and
        /// the night added in, map units an hour = speed / 1.2), starting at an in-game hour. Since 0.7.3; before, the pace was
        /// taken as map units an hour and the ground and night multiplied in, some 20% too quick.
        /// </summary>
        public static double Hours(RouteResult r, WalkMap m, BannerlordAgents.Common.Pace p, double startHours, out double arrive)
        {
            arrive = startHours;
            if (r == null || !r.Ok || p == null || p.Base <= 0.05f) return double.NaN;
            return BannerlordAgents.Common.GamePace.March(r.Points, m != null ? (Func<float, float, int>)((x, y) => m.GroundAt(x, y)) : null, p, startHours, out arrive, out _);
        }

        /// <summary>How marching works, in plain words: the first answer of the "ask the way" tool in a call carries it, and so does marching.txt.</summary>
        public static string Rules(IDictionary<string, string> peopleBonuses = null) => BannerlordAgents.Common.GamePace.Rules(peopleBonuses);

        /// <summary>
        /// Each people's march bonuses in words, from the bridge's table (culture id: bonuses by GamePace kind) and names:
        /// "Misty Mountain Orcs" -> "+10% on snow".
        /// </summary>
        public static Dictionary<string, string> PeopleBonuses(IDictionary<string, float[]> table, IDictionary<string, string> names)
        {
            var words = new Dictionary<string, string>();
            if (table == null) return words;
            string[] kind = { null, "on open plains", "in forest", "in swamps", "on steppe", "in desert", "on snow", "by night" };
            foreach (KeyValuePair<string, float[]> kv in table.OrderBy(k => k.Key))
            {
                var parts = new List<string>();
                for (int i = 1; i < kv.Value.Length && i < kind.Length; i++)
                    if (Math.Abs(kv.Value[i]) > 0.001f) parts.Add((kv.Value[i] > 0 ? "+" : "") + Math.Round(kv.Value[i] * 100) + "% " + kind[i]);
                if (parts.Count == 0) continue;
                string name = names != null && names.TryGetValue(kv.Key, out string n) && !string.IsNullOrWhiteSpace(n) ? n : kv.Key;
                words[name] = string.Join(", ", parts);
            }
            return words;
        }
    }
}
