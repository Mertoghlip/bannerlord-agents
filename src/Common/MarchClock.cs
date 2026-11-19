using System;
using System.Collections.Generic;

namespace BannerlordAgents.Common
{
    /// <summary>What a march time rests on (PLAN-ways.md): the party seen now, seen within a day (its pace +10%), seen one to three days ago (that pace or speed 2.8, whichever is faster), seen too long ago to count, or no pace known.</summary>
    public enum PaceBasis { Exact, Margin, Floor, Forgotten, Unknown }

    /// <summary>
    /// A party as the clock knows it (Nov 18, PLAN-ways.md): where it stands or was last seen, when, its pace then (the game's own speed sum),
    /// for an army its leader's own pace as well, and for a party in a battle the hour the battle should end.
    /// </summary>
    public sealed class ClockParty
    {
        public string Id;
        public float X, Y;
        /// <summary>The in-game hour it was seen (now for one in view).</summary>
        public double SeenHours;
        public bool InView;
        /// <summary>Its pace when seen (null: not known).</summary>
        public Pace Pace;
        /// <summary>An army's leader's own pace (the army's base scaled by its men, MarchClock.AlonePace); null for a party not leading an army.</summary>
        public Pace AlonePace;
        /// <summary>In a battle: the in-game hour the battle should end, when its march can begin; NaN otherwise.</summary>
        public double FreeAt = double.NaN;
    }

    /// <summary>When a party could get somewhere, and what that rests on.</summary>
    public sealed class MarchTime
    {
        /// <summary>In-game hours from now until it could be there (0 if it could be there already); NaN with no pace or when forgotten.</summary>
        public double Hours = double.NaN;
        /// <summary>The in-game hour it could be there.</summary>
        public double At = double.NaN;
        public bool Reached;
        public PaceBasis Basis;
        /// <summary>Hours since it was seen (0 in view).</summary>
        public double SeenAgo;
        /// <summary>Its speed on open ground by day when seen (0 unknown), and the one it is counted at there.</summary>
        public float SeenSpeed, CountedSpeed;
        /// <summary>The way's length in map units.</summary>
        public double Length;
    }

    /// <summary>
    /// The one march clock (Nov 18, PLAN-ways.md, after the storm of Imnagath): every estimate of when a party could get somewhere, inside the game and in
    /// the war room. In view: its exact pace, stretch by stretch for the ground and the hour (GamePace.Speed). Out of sight up to a day: its last-seen pace
    /// 10% faster, from where it was seen, the clock running since; one to three days: on each stretch the faster of that and speed 2.8 (as measured, the
    /// average of lord parties as they marched, on any ground); longer: not counted. An army out of sight: the faster of its own pace and its leader's.
    /// A party in a battle starts when the battle should end.
    /// </summary>
    public static class MarchClock
    {
        /// <summary>The margin on a pace seen earlier: 10% faster.</summary>
        public const float Margin = 1.1f;
        /// <summary>After this many hours out of sight the minimum speed counts too; after ForgetHours the party is not counted.</summary>
        public const double MarginHours = 24, ForgetHours = 72;
        /// <summary>The minimum for a party out of sight over a day: the game's speed 2.8, the mean speed of its mid- to high-strength lord parties as measured in this campaign (World.ReliefFloorSpeed).</summary>
        public const float FloorSpeed = 2.8f;
        /// <summary>The speed a party of no known pace is counted at (World.DefaultSpeed).</summary>
        public const float UnknownSpeed = 4f;

        public static PaceBasis BasisAt(ClockParty s, double now)
        {
            if (s == null) return PaceBasis.Unknown;
            if (s.InView) return s.Pace != null && s.Pace.Base > 0 ? PaceBasis.Exact : PaceBasis.Unknown;
            double ago = now - s.SeenHours;
            if (ago > ForgetHours) return PaceBasis.Forgotten;
            if (s.Pace == null || s.Pace.Base <= 0) return PaceBasis.Unknown;
            return ago > MarginHours ? PaceBasis.Floor : PaceBasis.Margin;
        }

        /// <summary>
        /// An army's leader's own pace: the army's pace with its base scaled from the army's men to his own (the game's base is 4 x (200 / (200 + men))^0.4
        /// over the whole army; its factors are mostly shares, kept as they are). Null when there is nothing to scale.
        /// </summary>
        public static Pace AlonePace(Pace army, int armyMen, int ownMen)
        {
            if (army == null || army.Base <= 0 || ownMen <= 0 || armyMen <= ownMen) return null;
            return new Pace
            {
                Base = (float)(army.Base * Math.Pow((200.0 + armyMen) / (200.0 + ownMen), 0.4)), Factors = army.Factors, Other = army.Other, Feats = army.Feats,
                Perks = army.Perks, FootShare = army.FootShare, PerkValues = army.PerkValues, Flat = army.Flat
            };
        }

        /// <summary>Map units an hour this party is counted at on one ground at one hour, by its basis.</summary>
        public static float UnitsPerHour(ClockParty s, PaceBasis basis, int ground, bool night)
        {
            if (basis == PaceBasis.Unknown) return GamePace.PerHour(Math.Max(GamePace.MinimumSpeed, UnknownSpeed * (1f + (night ? GamePace.NightFactor : 0f))));
            float Own(Pace p) => p == null ? 0f : p.Flat > 0 ? p.Flat : GamePace.PerHour(GamePace.Speed(p, ground, night));
            float u = Own(s.Pace);
            if (basis == PaceBasis.Exact) return u;
            if (s.AlonePace != null) u = Math.Max(u, Own(s.AlonePace));
            u *= Margin;
            if (basis == PaceBasis.Floor) u = Math.Max(u, GamePace.DayAverageOfSpeed(FloorSpeed));
            return u;
        }

        private static readonly int[] Grounds = { GamePace.Plain, GamePace.Steppe, GamePace.Desert, GamePace.Dune, GamePace.Snow, GamePace.Forest, GamePace.Swamp, GamePace.Fording };

        /// <summary>The most map units an hour the party is counted at on any ground, by day or night (its people may march faster at night): a bound for questions that need no march.</summary>
        public static float FastestUnits(ClockParty s, PaceBasis basis)
        {
            float best = 0f;
            foreach (int g in Grounds)
            {
                best = Math.Max(best, UnitsPerHour(s, basis, g, false));
                best = Math.Max(best, UnitsPerHour(s, basis, g, true));
            }
            return best;
        }

        /// <summary>The straight way from where the party is (or was seen) to a point: the first version of the way, the ground read along it.</summary>
        public static List<float[]> Straight(ClockParty s, float x, float y) => new List<float[]> { new[] { s.X, s.Y }, new[] { x, y } };

        /// <summary>
        /// A march on the straight line stretched to the game's road length when that is longer (its table of ways between holdings): the ground as read along the
        /// line, the hours in proportion. Until the copy of the game's way-finder this keeps long marches between holdings from looking short (docs/PLAN-ways.md).
        /// </summary>
        public static MarchTime ByRoad(ClockParty s, MarchTime m, double road, double now)
        {
            if (m == null || !m.Reached || double.IsNaN(road) || m.Length <= 0.5 || road <= m.Length) return m;
            double start = s.InView ? now : s.SeenHours;
            if (!double.IsNaN(s.FreeAt) && s.FreeAt > start) start = s.FreeAt;
            double k = road / m.Length;
            var r = new MarchTime { Reached = true, Basis = m.Basis, SeenAgo = m.SeenAgo, SeenSpeed = m.SeenSpeed, CountedSpeed = m.CountedSpeed, Length = road };
            r.At = start + (m.At - start) * k;
            r.Hours = Math.Max(0, r.At - now);
            return r;
        }

        /// <summary>
        /// When the party could reach the end of a way (its first point where it stands or was seen): marched stretch by stretch (a quarter unit at a time)
        /// at its counted pace for the ground under it and the hour, from when it was seen (or from the end of its battle). Hours are from now; one that
        /// could be there already gives 0. A forgotten party, or one with no way, gives NaN.
        /// </summary>
        public static MarchTime Reach(ClockParty s, IList<float[]> way, Func<float, float, int> groundAt, double now, double maxHours = 24 * 30)
        {
            var r = new MarchTime { Basis = BasisAt(s, now) };
            if (s == null) return r;
            r.SeenAgo = s.InView ? 0 : Math.Max(0, now - s.SeenHours);
            r.SeenSpeed = s.Pace != null && s.Pace.Base > 0 ? s.Pace.OpenSpeed : 0f;
            r.CountedSpeed = UnitsPerHour(s, r.Basis, GamePace.Plain, false) / GamePace.UnitsPerSpeedHour;
            if (r.Basis == PaceBasis.Forgotten || way == null || way.Count == 0) return r;
            double start = s.InView ? now : s.SeenHours;
            if (!double.IsNaN(s.FreeAt) && s.FreeAt > start) start = s.FreeAt;
            double t = start;
            double length = 0;
            double limit = now + maxHours;
            for (int i = 1; i < way.Count; i++)
            {
                float[] a = way[i - 1], b = way[i];
                float dx = b[0] - a[0], dy = b[1] - a[1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) continue;
                length += len;
                for (float d = 0; d < len; d += 0.25f)
                {
                    float piece = Math.Min(0.25f, len - d);
                    float x = a[0] + dx / len * (d + piece * 0.5f), y = a[1] + dy / len * (d + piece * 0.5f);
                    int g = groundAt != null ? groundAt(x, y) : GamePace.Plain;
                    float units = UnitsPerHour(s, r.Basis, g, GamePace.IsNight(t));
                    t += piece / Math.Max(0.05f, units);
                    if (t > limit) { r.Length = length; r.At = t; r.Hours = maxHours; return r; }
                }
            }
            r.Length = length;
            r.At = t;
            r.Hours = Math.Max(0, t - now);
            r.Reached = true;
            return r;
        }

        /// <summary>
        /// What the time rests on, in words for the agent: "seen now, speed 3.0 on open ground by day"; "last seen 5 hours ago at speed 3.0, counted at 3.3";
        /// "last seen 30 hours ago at speed 1.4, counted at no slower than 2.8"; "last seen 4 days ago at speed 1.4, no longer counted".
        /// </summary>
        public static string Basis(MarchTime m)
        {
            if (m == null) return "";
            string Sp(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string ago = m.SeenAgo < 1.5 ? "an hour" : m.SeenAgo < 72 ? Math.Round(m.SeenAgo) + " hours" : Math.Round(m.SeenAgo / 24, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " days";
            switch (m.Basis)
            {
                case PaceBasis.Exact: return "seen now, speed " + Sp(m.SeenSpeed) + " on open ground by day";
                case PaceBasis.Margin: return "last seen " + ago + " ago at speed " + Sp(m.SeenSpeed) + ", counted at " + Sp(m.CountedSpeed);
                case PaceBasis.Floor: return "last seen " + ago + " ago at speed " + Sp(m.SeenSpeed) + ", counted at no slower than " + Sp(FloorSpeed);
                case PaceBasis.Forgotten: return "last seen " + ago + " ago" + (m.SeenSpeed > 0 ? " at speed " + Sp(m.SeenSpeed) : "") + ", no longer counted";
                default: return "its speed not known, counted at " + Sp(UnknownSpeed);
            }
        }
    }
}
