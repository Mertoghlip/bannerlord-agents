using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// Each lord party's last battle since the load (0.19, the battle facts: the language's retreated): the hour it ended and whether its side retreated, taken
    /// at the game's MapEventEnded (MapEvent.FinalizeEvent raises it with RetreatingSide still set; a retreating side keeps its lords and, in
    /// DisorganizedStateCampaignBehavior, is the one side not slowed afterwards). Game thread only: a few lines a battle, one look-up per lord in the hourly
    /// copy (Capture.Row). In memory only, cleared at every load, so nothing goes into the save.
    /// </summary>
    internal static class BattleEnds
    {
        private struct End
        {
            public double Hours;
            public bool Retreated;
        }

        private static readonly Dictionary<string, End> Last = new Dictionary<string, End>(StringComparer.Ordinal);

        /// <summary>Entries older than this are dropped once the book grows past PruneAt (a lord's last battle a week ago says little).</summary>
        private const double KeepHours = 24 * 7;
        private const int PruneAt = 4000;

        public static void Reset() => Last.Clear();

        /// <summary>A battle ended (BridgeBehavior.OnBattleEnded): every lord party in it, with whether its own side was the one that retreated.</summary>
        public static void OnEnded(MapEvent me)
        {
            if (me?.AttackerSide == null || me.DefenderSide == null) return;
            double now = CampaignTime.Now.ToHours;
            BattleSideEnum retreating = me.RetreatingSide;
            Note(me.AttackerSide, retreating == BattleSideEnum.Attacker, now);
            Note(me.DefenderSide, retreating == BattleSideEnum.Defender, now);
            if (Last.Count > PruneAt) Prune(now);
        }

        private static void Note(MapEventSide side, bool retreated, double now)
        {
            foreach (MapEventParty mp in side.Parties)
            {
                var p = mp?.Party?.MobileParty;
                if (p == null || !p.IsLordParty) continue;
                Last[p.StringId] = new End { Hours = Math.Round(now, 2), Retreated = retreated };
            }
        }

        private static void Prune(double now)
        {
            var old = new List<string>();
            foreach (KeyValuePair<string, End> kv in Last) if (now - kv.Value.Hours > KeepHours) old.Add(kv.Key);
            foreach (string id in old) Last.Remove(id);
        }

        /// <summary>The party's last battle into its hourly row (Capture.Row, lords only).</summary>
        public static void Fill(PartyRow r)
        {
            if (r?.Id == null || !Last.TryGetValue(r.Id, out End e)) return;
            r.LastBattleEnd = e.Hours;
            r.LastRetreated = e.Retreated;
        }
    }
}
