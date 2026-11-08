using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BannerlordAgents.Tools
{
    /// <summary>
    /// Where the enemies a realm sees are, for the recruit order's tour (0.16, idea D, from the lieutenant's safe recruiting loop of test A1, which passed
    /// by stops that were threatened): the worker publishes, each hour a realm has a lord on that order, every hostile lord or army (its leader, with the
    /// army's strength) and band of bandits in the realm's view, as points with their strength; the game's thread reads them when the tour picks its next
    /// stop (RecruitTour.FindNext). Only what the realm sees, so the tour learns nothing the realm could not. Game-free; safe from any thread.
    /// </summary>
    internal static class Threats
    {
        internal struct Point
        {
            public float X, Y;
            public double Strength;
        }

        private static readonly ConcurrentDictionary<string, Point[]> ByRealm = new ConcurrentDictionary<string, Point[]>(StringComparer.Ordinal);

        /// <summary>How near a stop the enemies count, in map units (the safe recruiting loop's own was 20 about a stop; a stop is reached at its gate, so a little less).</summary>
        public const float Near = 15f;

        public static void Reset() => ByRealm.Clear();

        /// <summary>The hour's points for the realm whose view this is (worker).</summary>
        public static void Publish(World w)
        {
            if (w?.S?.Parties == null || w.Realm == null) return;
            var list = new List<Point>();
            foreach (PartyRow r in w.S.Parties)
            {
                if (r.Attached || (r.Kind != "lord" && r.Kind != "bandit")) continue;
                if (r.Kind == "lord" && !w.HostileFaction(r.Faction)) continue;
                if (!w.InView(r.Id)) continue;
                list.Add(new Point { X = r.X, Y = r.Y, Strength = World.StrengthOf(r) });
            }
            ByRealm[w.Realm] = list.ToArray();
        }

        /// <summary>The strength of the enemies the realm saw this hour within Near of a point (0 when it has published none).</summary>
        public static double Around(string realm, float x, float y)
        {
            if (realm == null || !ByRealm.TryGetValue(realm, out Point[] pts)) return 0;
            double sum = 0;
            float r2 = Near * Near;
            foreach (Point p in pts)
                if ((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) <= r2) sum += p.Strength;
            return sum;
        }
    }
}
