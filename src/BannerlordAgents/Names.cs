using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// Display names, cached. Turning a game name into text runs the game's localisation (a regular-expression pass
    /// per call), which is too slow to do for thousands of parties every hour, so each name is made once and kept.
    /// Game's thread only.
    /// </summary>
    internal static class Names
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);

        public static void Clear() => Cache.Clear();

        public static string Hero(Hero h)
        {
            if (h == null) return "";
            string key = "h:" + h.StringId;
            if (Cache.TryGetValue(key, out string v)) return v;
            try { v = h.Name?.ToString() ?? h.StringId; } catch { v = h.StringId; }
            Cache[key] = v;
            return v;
        }

        public static string Settlement(Settlement s)
        {
            if (s == null) return "";
            string key = "s:" + s.StringId;
            if (Cache.TryGetValue(key, out string v)) return v;
            try { v = s.Name?.ToString() ?? s.StringId; } catch { v = s.StringId; }
            Cache[key] = v;
            return v;
        }

        public static string Faction(IFaction f)
        {
            if (f == null) return "";
            string key = "f:" + f.StringId;
            if (Cache.TryGetValue(key, out string v)) return v;
            try { v = f.Name?.ToString() ?? f.StringId; } catch { v = f.StringId; }
            Cache[key] = v;
            return v;
        }

        /// <summary>A lord's party goes by its lord's name; anything else by its own.</summary>
        public static string Party(MobileParty p)
        {
            if (p == null) return "";
            if (p.LeaderHero != null) return Hero(p.LeaderHero);
            string key = "p:" + p.StringId;
            if (Cache.TryGetValue(key, out string v)) return v;
            try { v = p.Name?.ToString() ?? p.StringId; } catch { v = p.StringId; }
            Cache[key] = v;
            return v;
        }

        /// <summary>Fills the cache while the campaign loads, so the first hour of play pays nothing.</summary>
        public static int WarmUp()
        {
            int before = Cache.Count;
            foreach (Settlement s in TaleWorlds.CampaignSystem.Settlements.Settlement.All) Settlement(s);
            foreach (Kingdom k in Kingdom.All) { Faction(k); Hero(k.Leader); }
            foreach (MobileParty p in MobileParty.AllLordParties) Party(p);
            return Cache.Count - before;
        }
    }
}
