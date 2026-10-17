using System;

namespace BannerlordAgents
{
    /// <summary>
    /// Travel distance as the game itself reckons it (0.7.1; Mert, Oct 16: "distances should be given both as the crow flies
    /// and the actual travel distance"). The game keeps the real way between every two holdings' gates (its navigation cache,
    /// copied into RoadBook); from a party it counts through the holding whose gate serves the ground the party stands on
    /// (DefaultMapDistanceModel.GetDistance(MobileParty, Settlement)): straight to the party's entrance, that entrance's road to
    /// the destination, less the straight line between the two gates. Never less than the straight line. Game-free, worker.
    /// </summary>
    internal static class Roads
    {
        /// <summary>From a point (and the entrance of the ground it stands on, or the holding it is inside) to a holding's gate; NaN when the game's table lacks the pair.</summary>
        public static float To(Snapshot s, float x, float y, string entrance, string inside, SettlementRow h)
        {
            if (h == null || s?.Roads == null) return float.NaN;
            if (inside != null && inside == h.Id) return 0f;
            float gx = GateX(h), gy = GateY(h), crow = Dist(x, y, gx, gy);
            string e = inside ?? entrance;
            if (e == null) return float.NaN;
            if (e == h.Id) return crow;
            if (!s.SettlementById.TryGetValue(e, out SettlementRow es) || !s.Roads.TryGet(e, h.Id, out float d) || float.IsNaN(d)) return float.NaN;
            return Math.Max(crow, crow - Dist(GateX(es), GateY(es), gx, gy) + d);
        }

        public static float To(Snapshot s, PartyRow r, SettlementRow h) => r == null ? float.NaN : To(s, r.X, r.Y, r.Entrance, r.InSettlement, h);

        /// <summary>Between two parties, each counted through its own entrance (the game's way from a party to a point); NaN when not known.</summary>
        public static float Between(Snapshot s, float ax, float ay, string ea, float bx, float by, string eb)
        {
            if (s?.Roads == null || ea == null || eb == null) return float.NaN;
            float crow = Dist(ax, ay, bx, by);
            if (ea == eb) return crow;
            if (!s.SettlementById.TryGetValue(ea, out SettlementRow ha) || !s.SettlementById.TryGetValue(eb, out SettlementRow hb)) return float.NaN;
            if (!s.Roads.TryGet(ea, eb, out float d) || float.IsNaN(d)) return float.NaN;
            return Math.Max(crow, crow - Dist(GateX(ha), GateY(ha), GateX(hb), GateY(hb)) + d);
        }

        public static float Between(Snapshot s, PartyRow a, PartyRow b)
            => a == null || b == null ? float.NaN : Between(s, a.X, a.Y, a.InSettlement ?? a.Entrance, b.X, b.Y, b.InSettlement ?? b.Entrance);

        /// <summary>The game's table was copied for this hour (Brain sets it); without one (a test world) distances are told as the crow flies only.</summary>
        public static bool Known;

        /// <summary>"63 (260 by road)": a distance as the crow flies, and by road; "(by road not known)" when the game's table lacks the pair.</summary>
        public static string Both(float crow, float road)
            => Text.D(crow) + (!Known ? "" : float.IsNaN(road) ? " (by road not known)" : " (" + Text.D(road) + " by road)");

        private static float GateX(SettlementRow h) => h.Gx != 0 || h.Gy != 0 ? h.Gx : h.X;
        private static float GateY(SettlementRow h) => h.Gx != 0 || h.Gy != 0 ? h.Gy : h.Y;
        private static float Dist(float x1, float y1, float x2, float y2) => (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
    }
}
