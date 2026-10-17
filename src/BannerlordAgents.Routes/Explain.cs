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
        public static string GroundText(RouteResult r, SpeedFactors f)
        {
            float total = r.OnGround.Values.Sum();
            if (total <= 0) return "";
            var parts = r.OnGround.Where(kv => kv.Value / total >= 0.02f || Ground.IsCrossing(kv.Key)).OrderByDescending(kv => kv.Value)
                .Select(kv => Ground.Name(kv.Key) + " " + Words.Pct(kv.Value / total) + (f != null && f.Of(kv.Key) < 0.99f ? " (slower)" : ""));
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

        /// <summary>
        /// The answer about one way: its length against the straight line, the time at the party's pace (null pace: no time),
        /// the ground, the crossings and what closing each would cost, and the narrowest place.
        /// </summary>
        public static List<string> Way(WalkMap m, Atlas atlas, RouteResult r, float pace, string paceFrom, double startHours, SpeedFactors f, Func<double, string> when, out double hours, out double arrive)
        {
            hours = arrive = double.NaN;
            var lines = new List<string>();
            if (r == null || !r.Ok)
            {
                lines.Add("No way: " + (r?.Why ?? "unknown") + ".");
                return lines;
            }
            string time = "";
            if (pace > 0.05f)
            {
                hours = Marching.Hours(r, m, pace, startHours, f, out arrive);
                time = "; about " + Words.N(hours) + " hours at " + Words.D1(pace) + " an hour on open ground by day (" + paceFrom + "), so about " + when(arrive);
            }
            lines.Add("By the shortest way: " + Words.N(r.Length) + " map units (" + Words.N(r.Straight) + " as the crow flies)" + time + ".");
            string ground = GroundText(r, f);
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
                    parts.Add(x.Name + " (" + at + (x.Kind == "pass" ? ", " + Words.D1(x.Width) + " across" : "") + "; " + what + ")");
                }
                lines.Add("Crossings on the way, in order: " + string.Join("; ", parts) + (xs.Count > 6 ? "; and " + (xs.Count - 6) + " more" : "") + ".");
            }
            if (r.NarrowWidth < Atlas.PassWidth && !xs.Any(x => WalkMap.Dist(x.X, x.Y, r.NarrowX, r.NarrowY) < Math.Max(6f, x.Width + 3f)))
                lines.Add("Narrowest place: " + Words.D1(r.NarrowWidth) + " across, " + m.Describe(r.NarrowX, r.NarrowY) + ".");
            return lines;
        }
    }
}
