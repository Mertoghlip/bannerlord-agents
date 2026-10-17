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
        /// Hours to march a way at a party's pace (its speed on open ground by day, map units an hour), slowed by the ground
        /// under each stretch and by night, starting at an in-game hour.
        /// </summary>
        public static double Hours(RouteResult r, WalkMap m, float pace, double startHours, SpeedFactors f, out double arrive)
        {
            arrive = startHours;
            if (r == null || !r.Ok || pace <= 0.05f) return double.NaN;
            int rise = m.Meta.SunRise, set = m.Meta.SunSet;
            double t = startHours;
            const float step = 0.5f;
            for (int i = 1; i < r.Points.Count; i++)
            {
                float[] a = r.Points[i - 1], b = r.Points[i];
                float dx = b[0] - a[0], dy = b[1] - a[1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) continue;
                for (float d = 0; d < len; d += step)
                {
                    float piece = Math.Min(step, len - d);
                    byte g = m.GroundAt(a[0] + dx / len * d, a[1] + dy / len * d);
                    double speed = pace * f.Of(g) * (IsNight(t, rise, set) ? f.Night : 1f);
                    t += piece / Math.Max(0.05, speed);
                }
            }
            arrive = t;
            return t - startHours;
        }

        /// <summary>How marching works, in plain words: the first answer of the "ask the way" tool in a call carries it, and so does marching.txt.</summary>
        public static string Rules(WalkMap m, SpeedFactors f)
        {
            int rise = m?.Meta.SunRise ?? 2, set = m?.Meta.SunSet ?? 22;
            string Pct(byte g) => Math.Round((1 - f.Of(g)) * 100).ToString("0") + "%";
            return "HOW MARCHING WORKS\n"
                + "- A party plans its whole way when it sets off and takes the shortest way over walkable ground: open land, forest, steppe, desert, snow, swamp, dunes and beach. "
                + "Mountains, cliffs, canyons, lakes, rivers and the sea cannot be crossed; a river only at a bridge or a ford. It plans again whenever its destination moves "
                + "(chasing a party) or it gets a new one.\n"
                + "- It takes the shortest way, not the fastest. Ground slows it as it goes: forest by " + Pct(Ground.Forest) + ", fords and bridges by " + Pct(Ground.Fording)
                + ", desert and dunes by " + Pct(Ground.Desert) + ", and night (from " + set.ToString("00") + ":00 to " + rise.ToString("00") + ":00) by "
                + Math.Round((1 - f.Night) * 100).ToString("0") + "%. Snowfall slows it by 10% more. So a short way through forest can take longer than a longer way round.\n"
                + "- A party's pace on open ground by day depends on its size, its men on foot and on horse, its wounded, its herds and its cargo; an army's pace counts all its parties together.\n"
                + "- Where only one bridge, ford or pass joins two lands, every way between them goes through it.\n"
                + "(Slowing: " + f.Source + ".)";
        }
    }
}
