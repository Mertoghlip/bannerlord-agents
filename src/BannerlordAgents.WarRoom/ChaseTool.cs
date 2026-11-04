using System.Globalization;
using System.Text.Json.Nodes;
using BannerlordAgents.Common;
using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The chase question (0.9.3, Oct 23: a party or army hunting an enemy faster than itself, while it has other work,
    /// wastes itself; the agent should be able to ask an enemy's speed before it gives a hunt order):
    /// - can_catch: one of the realm's lords or armies against one enemy its report shows: each side's pace by the game's rules
    ///   (the enemy's as the map shows it, or as it was when last seen), the way between them, how long to reach it if it holds
    ///   still and to run it down if it flees, and, when it is marching somewhere, who gets there first.
    /// From the realm's own view file only, like the other questions: it says nothing the realm could not know.
    /// </summary>
    internal sealed partial class RouteTool
    {
        public const string ChaseName = "can_catch";

        public static JsonObject ChaseDefinition() => new JsonObject
        {
            ["name"] = ChaseName,
            ["description"] = "Whether one of your lords or armies could catch an enemy lord or army your report shows: each side's pace by the game's rules, the way between them, "
                              + "how long to reach the enemy if it holds still and to run it down if it flees, and, when it is marching somewhere, who would get there first.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["hunter"] = new JsonObject { ["type"] = "string", ["description"] = "The id in [brackets] of one of your lords or armies." },
                    ["quarry"] = new JsonObject { ["type"] = "string", ["description"] = "The id in [brackets] of an enemy lord or army your report shows." }
                },
                ["required"] = new JsonArray("hunter", "quarry")
            }
        };

        public string Chase(JsonObject args)
        {
            JsonObject view = ReadView();
            if (view == null) return "Your realm's view is not ready yet; ask again in a moment.";
            WalkMap m = MapFor(_root, (string)view["map"]);
            if (m == null) return "The map is not ready yet, so the chase cannot be worked out.";
            double now = Num(view["hours"]);
            List<Known> known = Gather(view, m);
            string hunterQ = Str(args["hunter"]), quarryQ = Str(args["quarry"]);
            Known h = Find(known, m, hunterQ), q = Find(known, m, quarryQ);
            if (h == null || h.Place || !h.Own) return "Name one of your own lords or armies in hunter, by its id in [brackets] (" + Show(hunterQ) + " is not one).";
            if (q == null || q.Place || q.Own) return "Name an enemy lord or army your report shows in quarry, by its id in [brackets] (" + Show(quarryQ) + " is not one).";
            if (h.March == null || q.March == null) return "The pace of " + (h.March == null ? h.Name : q.Name) + " is not known, so the chase cannot be worked out.";
            GameCalendar cal = Calendar();
            double vh = PerHour(h.March), vq = PerHour(q.March);
            bool fresh = double.IsNaN(q.At) || q.At >= now - 0.5;
            var lines = new List<string>
            {
                "CAN " + Head(h) + " CATCH " + Head(q) + "?",
                "- " + h.Name + ": " + Explain.PaceWords(h.March) + ", " + h.PaceFrom + ".",
                "- " + q.Name + ": " + Explain.PaceWords(q.March) + ", " + q.PaceFrom + (fresh ? ", in view now." : ", last seen " + Words.N(now - q.At) + " hours ago.")
            };
            RouteResult r;
            lock (Gate) r = _finder.Find(h.X, h.Y, q.X, q.Y);
            double reach = r.Ok ? Marching.Hours(r, m, h.March, now, out _) : double.NaN;
            if (!r.Ok) lines.Add("- No way from one to the other is known (" + r.Why + ").");
            else lines.Add("- Between them: " + Words.N(r.Length) + " by the way the game would march, about " + Words.N(reach) + " hours of " + h.Name + "'s march to where " + q.Name
                           + (fresh ? " is now." : " was seen."));
            double pct = vq > 0 ? 100 * (vh - vq) / vq : 100;
            if (vh > vq * 1.02)
            {
                string runs = double.IsNaN(reach) ? "" : "; if it runs from " + h.Name + " over the same ground, caught in about " + Words.N(reach * vh / (vh - vq)) + " hours";
                lines.Add("- " + h.Name + " is the faster, by about " + Words.N(pct) + "% (" + Words.D1(vh - vq) + " map units an hour on open ground by day): if " + q.Name + " holds still, it is reached"
                          + (double.IsNaN(reach) ? "" : " in about " + Words.N(reach) + " hours") + runs + ". Ground and night slow both.");
            }
            else if (vh >= vq * 0.98)
                lines.Add("- They are about as fast: if " + q.Name + " runs, the gap stays as it is, and it is caught only when it stops (to besiege, raid, fight, or go into a holding) or turns toward " + h.Name + ".");
            else
                lines.Add("- " + q.Name + " is the faster, by about " + Words.N(vh > 0 ? 100 * (vq - vh) / vh : 100) + "%: if it runs, it gains about " + Words.D1(vq - vh) + " map units an hour on open ground and is never "
                          + "caught; " + h.Name + " reaches it only when it stops (to besiege, raid, fight, or go into a holding) or comes toward " + h.Name + ".");
            // where it is going: who gets there first
            if (q.Toward != null)
            {
                Known dest = known.FirstOrDefault(k => k.Place && string.Equals(k.Id, q.Toward, StringComparison.OrdinalIgnoreCase));
                if (dest != null)
                {
                    RouteResult rq, rh;
                    lock (Gate) { rq = _finder.Find(q.X, q.Y, dest.X, dest.Y); rh = _finder.Find(h.X, h.Y, dest.X, dest.Y); }
                    Marching.Hours(rq, m, q.March, double.IsNaN(q.At) ? now : q.At, out double aq);
                    Marching.Hours(rh, m, h.March, now, out double ah);
                    if (rq.Ok && rh.Ok)
                        lines.Add("- It is marching to " + dest.Name + " [" + dest.Id + "]: it gets there about " + cal.When(aq) + ", " + h.Name + " about " + cal.When(ah)
                                  + (ah < aq ? ", first: " + h.Name + " can be there to meet it." : ": it gets there first."));
                }
            }
            Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = ChaseName, hunter = h.Id, quarry = q.Id, lines });
            return string.Join("\n", lines);
        }

        /// <summary>Map units an hour on open ground by day; a pace known only by a trail, as the trail showed it.</summary>
        private static double PerHour(BannerlordAgents.Common.Pace p) => p.Flat > 0 ? p.Flat : GamePace.PerHour(p.OpenSpeed);
    }
}
