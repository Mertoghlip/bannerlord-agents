using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// Can they cut the way (0.13.22, Nov 1, after Dol Guldur's agent sent Zulgora's army for Westoft because "ask the way" said it
    /// would get there 8 hours before Aragorn, and Aragorn, marching straight at the army, caught it about 16 hours later, long before
    /// either could reach Westoft: "who gets there first" weighs a race to the same place, not a march across the road). For each lord or
    /// army of the other side named in also_from, the first point of the way it could reach before the one asked about passes there, each
    /// marching at once by its own shortest way at its own pace. A chase from behind shows too: an army that runs another down gets to the
    /// points ahead of it first. Facts only, like the rest of the tool.
    /// </summary>
    internal sealed partial class RouteTool
    {
        /// <summary>The most points of the way weighed for each army (evenly in time along the march, its end always among them).</summary>
        public const int CutPoints = 24;

        /// <summary>An army's fastest pace is taken as this times its own on open ground by day, so a point it could not reach even so is passed by without a search.</summary>
        private const double CutReach = 1.3;

        private List<string> CutOff(WalkMap m, List<Known> known, Known from, RouteResult r, List<string> also, double start, double now, GameCalendar cal)
        {
            var lines = new List<string>();
            if (r == null || !r.Ok || from.March == null) return lines;
            // where the one asked about is, hour by hour, along its way
            var marks = new List<(double at, float x, float y)>();
            BannerlordAgents.Common.GamePace.March(r.Points, (x, y) => m.GroundAt(x, y), from.March, start, out double arrive, out bool reached, at: (h, x, y) => marks.Add((start + h, x, y)));
            if (!reached) return lines;
            float[] end = r.Points[r.Points.Count - 1];
            if (marks.Count == 0 || marks[marks.Count - 1].at < arrive) marks.Add((arrive, end[0], end[1]));
            int every = Math.Max(1, (int)Math.Ceiling(marks.Count / (double)CutPoints));
            var points = marks.Where((p, i) => i % every == every - 1 || i == marks.Count - 1).ToList();
            foreach (string q in also)
            {
                Known k = Find(known, m, q);
                if (k == null || k.Place || k.Own == from.Own || k.March == null) continue;
                double kStart = double.IsNaN(k.At) ? now : k.At;
                double fastest = CutReach * PerHour(k.March);
                string seen = double.IsNaN(k.At) || k.At >= now - 0.5 ? "" : ", from where it was seen " + Words.N(now - k.At) + " hours ago";
                bool found = false;
                foreach ((double at, float x, float y) p in points)
                {
                    double room = p.at - kStart;
                    if (room <= 0 || WalkMap.Dist(k.X, k.Y, p.x, p.y) / Math.Max(0.05, fastest) > room) continue;
                    RouteResult rk;
                    lock (Gate) rk = _finder.Find(k.X, k.Y, p.x, p.y, analyse: false);
                    if (!rk.Ok) continue;
                    Marching.Hours(rk, m, k.March, kStart, out double ka);
                    if (ka > p.at) continue;
                    float along = Explain.Along(r, p.x, p.y, out _);
                    double ahead = p.at - ka;
                    string lead = ahead < 0.5 ? "about when " + from.Name + " would pass there"
                                  : Words.N(ahead) + (Math.Round(ahead) == 1 ? " hour" : " hours") + " before " + from.Name + " would pass there, about " + cal.When(p.at);
                    lines.Add("- " + k.Name + " [" + (k.Lord ?? k.Id) + "] could be on the way about " + Words.N(along) + " along, " + m.Describe(p.x, p.y) + " [" + Explain.SpotId(p.x, p.y) + "], by about "
                              + cal.When(ka) + seen + ": " + lead + ". From there on, " + from.Name + " could not keep to this way without meeting it.");
                    found = true;
                    break;
                }
                if (!found)
                    lines.Add("- " + k.Name + " [" + (k.Lord ?? k.Id) + "] could reach no point of the way before " + from.Name + " passes there" + seen + ".");
            }
            if (lines.Count > 0)
                lines.Insert(0, "CAN THEY CUT THE WAY? Each of the other side named, marching at once by its own shortest way to a point of " + from.Name + "'s way, against " + from.Name + " marching on it at once");
            return lines;
        }
    }
}
