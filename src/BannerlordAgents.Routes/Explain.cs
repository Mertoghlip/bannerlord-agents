namespace BannerlordAgents.Routes
{
    /// <summary>A crossing the way passes, with where along it, and what the atlas knows of it (null when it is not in the atlas).</summary>
    public sealed class WayCrossing
    {
        public string Kind, Name;
        public float X, Y, At, Width;
        public AtlasCrossing Atlas;
    }

    /// <summary>A way in plain words, for the agent's "ask the way" tool and the dashboard's cards.</summary>
    public static class Explain
    {
        /// <summary>The crossings on a way: its bridges and fords, and the atlas's passes it goes through, in the order it meets them.</summary>
        public static List<WayCrossing> Crossings(WalkMap m, Atlas atlas, RouteResult r)
        {
            var list = new List<WayCrossing>();
            if (r == null || !r.Ok) return list;
            foreach (RouteCrossing c in r.Crossings)
            {
                AtlasCrossing a = atlas?.Near(c.X, c.Y, 5f);
                string kind = c.Ground == Ground.Bridge ? "bridge" : "ford";
                list.Add(new WayCrossing { Kind = kind, X = c.X, Y = c.Y, At = c.At, Width = c.Length, Atlas = a, Name = a?.Name ?? "a " + kind + " " + m.Describe(c.X, c.Y) });
            }
            if (atlas != null)
                foreach (AtlasCrossing a in atlas.Crossings.Where(a => a.Kind == "pass"))
                {
                    float at = Along(r, a.X, a.Y, out float off);
                    if (off > Math.Max(3f, a.Width / 2 + 1.5f)) continue;
                    if (list.Any(x => x.Atlas == a)) continue;
                    list.Add(new WayCrossing { Kind = "pass", X = a.X, Y = a.Y, At = at, Width = a.Width, Atlas = a, Name = a.Name });
                }
            return list.OrderBy(x => x.At).ToList();
        }

        /// <summary>How far along a way the point nearest (x, y) comes, and how far off the way (x, y) lies.</summary>
        public static float Along(RouteResult r, float x, float y, out float off)
        {
            off = float.MaxValue;
            float best = 0, along = 0;
            for (int i = 1; i < r.Points.Count; i++)
            {
                float[] a = r.Points[i - 1], b = r.Points[i];
                float dx = b[0] - a[0], dy = b[1] - a[1], len2 = dx * dx + dy * dy, len = (float)Math.Sqrt(len2);
                float t = len2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / len2));
                float px = a[0] + dx * t, py = a[1] + dy * t, d = WalkMap.Dist(px, py, x, y);
                if (d < off) { off = d; best = along + len * t; }
                along += len;
            }
            return best;
        }

        /// <summary>The ground a way crosses, largest share first ("plain 62%, forest 30%, ford 1%").</summary>
        public static string GroundText(RouteResult r)
        {
            float total = r.OnGround.Values.Sum();
            if (total <= 0) return "";
            var parts = r.OnGround.Where(kv => kv.Value / total >= 0.02f || Ground.IsCrossing(kv.Key)).OrderByDescending(kv => kv.Value)
                .Select(kv => Ground.Name(kv.Key) + " " + Words.Pct(kv.Value / total) + (BannerlordAgents.Common.GamePace.GroundFactor(kv.Key, 0, 1f, null) < -0.001f ? " (slower)" : ""));
            return string.Join(", ", parts);
        }

        /// <summary>What closing a crossing does to the ways between holdings, from the atlas: "the only way between A and B" or "A to B grows from 80 to 210".</summary>
        public static string Closing(AtlasCrossing a)
        {
            if (a == null || a.Examples.Count == 0) return null;
            AtlasExample e = a.Examples[0];
            if (float.IsNaN(e.After)) return "if it were closed there would be no other way between " + e.From + " and " + e.To;
            return "if it were closed the way from " + e.From + " to " + e.To + " would grow from " + Words.N(e.Before) + " to " + Words.N(e.After);
        }

        /// <summary>"at its speed 1.7 (1.4 map units an hour on open ground by day)", or for a pace known only as units an hour, "at 1.3 an hour, all in".</summary>
        public static string PaceWords(BannerlordAgents.Common.Pace p)
        {
            if (p.Flat > 0) return "at " + Words.D1(p.Flat) + " map units an hour, all in";
            float open = p.OpenSpeed;
            return "at its speed " + Words.D1(open) + " (" + Words.D1(BannerlordAgents.Common.GamePace.PerHour(open)) + " map units an hour on open ground by day; forest, fords, bridges and night slower, as they come)";
        }

        /// <summary>
        /// The answer about one way: its length against the straight line, the time at the party's pace (null pace: no time),
        /// the ground, the crossings and what closing each would cost, and the narrowest place.
        /// </summary>
        public static List<string> Way(WalkMap m, Atlas atlas, RouteResult r, BannerlordAgents.Common.Pace pace, string paceFrom, double startHours, Func<double, string> when, out double hours, out double arrive)
        {
            hours = arrive = double.NaN;
            var lines = new List<string>();
            if (r == null || !r.Ok)
            {
                lines.Add("No way: " + (r?.Why ?? "unknown") + ".");
                return lines;
            }
            string time = "";
            if (pace != null && pace.Base > 0.05f)
            {
                hours = Marching.Hours(r, m, pace, startHours, out arrive);
                time = "; about " + Words.N(hours) + " hours " + PaceWords(pace) + " (" + paceFrom + "), so about " + when(arrive);
            }
            lines.Add("By the shortest way: " + Words.N(r.Length) + " map units (" + Words.N(r.Straight) + " as the crow flies)" + time + ".");
            string ground = GroundText(r);
            if (ground.Length > 0) lines.Add("Ground on the way: " + ground + ".");
            List<WayCrossing> xs = Crossings(m, atlas, r);
            if (xs.Count == 0) lines.Add("No bridge, ford or narrow pass on the way.");
            else
            {
                var parts = new List<string>();
                foreach (WayCrossing x in xs.Take(6))
                {
                    string at = Words.N(x.At) + " along";
                    string what = x.Atlas != null ? Closing(x.Atlas) : atlas == null ? "what closing it would cost is not worked out yet" : "other ways round lie close by";
                    parts.Add(x.Name + " [" + SpotId(x.X, x.Y) + "] (" + at + (x.Kind == "pass" ? ", " + Words.D1(x.Width) + " across" : "") + "; " + what + ")");
                }
                lines.Add("Crossings on the way, in order: " + string.Join("; ", parts) + (xs.Count > 6 ? "; and " + (xs.Count - 6) + " more" : "") + ".");
            }
            if (r.NarrowWidth < Atlas.PassWidth && !xs.Any(x => WalkMap.Dist(x.X, x.Y, r.NarrowX, r.NarrowY) < Math.Max(6f, x.Width + 3f)))
                lines.Add("Narrowest place: " + Words.D1(r.NarrowWidth) + " across, " + m.Describe(r.NarrowX, r.NarrowY) + " [" + SpotId(r.NarrowX, r.NarrowY) + "].");
            List<float[]> spots = SpotsAlong(r, 6);
            if (spots.Count > 0)
                lines.Add("Spots on the way, by how far along (a watch can be posted on any of them): "
                          + string.Join("; ", spots.Select(p => Words.N(p[2]) + " along, " + m.Describe(p[0], p[1]) + " [" + SpotId(p[0], p[1]) + "]")) + ".");
            return lines;
        }

        /// <summary>A spot's id, as orders name it: "p673_994" (map units, east then north).</summary>
        public static string SpotId(float x, float y) => "p" + Math.Round(x).ToString("0") + "_" + Math.Round(y).ToString("0");

        /// <summary>Points spread evenly along a way (x, y, how far along), at most most of them, none closer than 15 apart or to its ends.</summary>
        public static List<float[]> SpotsAlong(RouteResult r, int most)
        {
            var list = new List<float[]>();
            if (r == null || !r.Ok || r.Length < 30) return list;
            int n = Math.Min(most, (int)(r.Length / 15f) - 1);
            for (int k = 1; k <= n; k++)
            {
                float want = r.Length * k / (n + 1), along = 0;
                for (int i = 1; i < r.Points.Count; i++)
                {
                    float[] a = r.Points[i - 1], b = r.Points[i];
                    float len = WalkMap.Dist(a[0], a[1], b[0], b[1]);
                    if (along + len >= want && len > 0)
                    {
                        float t = (want - along) / len;
                        list.Add(new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, want });
                        break;
                    }
                    along += len;
                }
            }
            return list;
        }
    }
}
