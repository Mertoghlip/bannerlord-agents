using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordAgents.Common
{
    /// <summary>
    /// Which lines of a realm's chronicle its report's RECENT HISTORY shows (0.13.16), one rule for the bridge (Chronicle.Recent) and the
    /// war room (Room.Gap, which leaves out of its "chronicle since the story" the lines the report already shows), linked into both.
    /// Before, the newest 25 lines of the last two days: on Nov 1 the lieutenant could not say who broke Dáin's siege of Nanduhirion,
    /// because a day of lords sheltering and finding few recruits had pushed the battle and the twelve captures out, the cut falling inside
    /// the very hour of them. Now the news of the last two days (battles, captures, sieges, raids, holdings, lords killed or freed, armies,
    /// wars and the realm's votes: every line but the orders' own) comes first, up to MaxNews lines; the orders' lines (their call-backs,
    /// endings and refusals, and the orders given) fill what is left of Lines. Lines go by whole in-game hours, newest first: an hour is
    /// shown whole or not at all, except that the newest hour of news is always shown.
    /// </summary>
    public static class RecentHistory
    {
        public const double Hours = 48;
        public const int Lines = 25;
        public const int MaxNews = 40;

        /// <summary>An order's own line (given, called back, ended, refused), the first to go when the history is cut.</summary>
        public static bool Routine(string kind) => kind == "tool" || kind == "order" || kind == "order_ended" || kind == "refused";

        /// <summary>The lines shown, as indices into the chronicle's lines (hours and kinds, oldest first), oldest first.</summary>
        public static List<int> Pick(IList<double> hours, IList<string> kinds, double now, double within = Hours, int lines = Lines, int maxNews = MaxNews)
        {
            var window = new List<int>();
            for (int i = 0; i < hours.Count; i++)
                if (hours[i] >= now - within && hours[i] <= now + 1) window.Add(i);
            var keep = new HashSet<int>();
            Take(window.Where(i => !Routine(kinds[i])).ToList(), hours, maxNews, keep, true);
            int room = lines - keep.Count;
            if (room > 0) Take(window.Where(i => Routine(kinds[i])).ToList(), hours, room, keep, false);
            return window.Where(keep.Contains).ToList();
        }

        /// <summary>Whole in-game hours, newest first, while they fit in max (the newest always, when first is set).</summary>
        private static void Take(List<int> lines, IList<double> hours, int max, HashSet<int> keep, bool first)
        {
            int taken = 0;
            foreach (IGrouping<double, int> hour in lines.GroupBy(i => Math.Floor(hours[i] + 1e-6)).OrderByDescending(g => g.Key))
            {
                int n = hour.Count();
                if (taken + n > max && !(first && taken == 0)) break;
                foreach (int i in hour) keep.Add(i);
                taken += n;
            }
        }
    }
}
