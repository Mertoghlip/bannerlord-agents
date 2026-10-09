using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// What the game's own history log (the encyclopedia's) still holds about a realm, read once at load to start its
    /// chronicle: wars and peace of the realm and of the realms it is at war or allied with, its holdings changing hands
    /// or besieged, its lords captured, freed, beaten or killed, its clans coming and going, its armies, and battles at
    /// its holdings. The game keeps most of these for a week (a death for ever, a peace 40 days, a capture 84), so this
    /// covers the last week or so before the load. Each line is the game's own sentence, without its links. Game's
    /// thread; only plain lines leave it.
    /// </summary>
    internal static class GameHistory
    {
        /// <summary>Entry kinds that never matter to a realm's war: births, marriages, quests, the player's own small affairs, ...</summary>
        private static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.Ordinal)
        {
            "CharacterBorn", "CharacterMarried", "CharacterInsulted", "Childbirth", "Pregnancy", "ChangeRomanticState", "IssueQuest", "IssueQuestStart",
            "PlayerMeetLord", "PlayerAttackAlley", "PlayerReputationChanges", "PlayerCharacterChanged", "PlayerBattleEnded", "PlayerRetired",
            "OverruleInfluence", "CommonAreaFight", "Journal", "KingdomDecisionAdded", "SiegeAftermath", "GatherArmy", "CharacterBecameFugitive",
            "SettlementClaimed", "DestroyMobileParty"
        };

        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

        public static List<ChronicleEntry> Collect(Kingdom realm, out int scanned, out int failed)
        {
            var list = new List<ChronicleEntry>();
            scanned = failed = 0;
            LogEntryHistory history = Campaign.Current?.LogEntryHistory;
            if (realm == null || history == null) return list;
            var interest = new HashSet<MBObjectBase> { realm };
            foreach (Clan c in realm.Clans) if (c != null) interest.Add(c);
            foreach (Hero h in realm.Heroes) if (h != null) interest.Add(h);
            foreach (Hero h in realm.DeadLords) if (h != null) interest.Add(h);
            foreach (Settlement s in realm.Settlements) if (s != null) interest.Add(s);
            var near = new HashSet<IFaction>(Kingdom.All.Where(k => k != null && k != realm && !k.IsEliminated && (realm.IsAtWarWith(k) || realm.AlliedKingdoms.Contains(k))));
            foreach (LogEntry e in history.GameActionLogs)
            {
                scanned++;
                try
                {
                    if (e == null || !About(e, realm, interest, near)) continue;
                    string text = Clean(e is IEncyclopediaLog el ? el.GetEncyclopediaText()?.ToString() : e.ToString());
                    if (string.IsNullOrEmpty(text)) continue;
                    list.Add(new ChronicleEntry { Hours = e.GameTime.ToHours, Kind = "game:" + Kind(e), Text = text, From = "game" });
                }
                catch (Exception ex)
                {
                    // an entry about something long gone can throw; the rest still count
                    if (++failed <= 3) Diag.Swallowed("reading the game's history log", ex);
                }
            }
            return list;
        }

        private static bool About(LogEntry e, Kingdom realm, HashSet<MBObjectBase> interest, HashSet<IFaction> near)
        {
            switch (e)
            {
                case DeclareWarLogEntry w: return Wars(w.Faction1, w.Faction2, realm, near);
                case MakePeaceLogEntry p: return Wars(p.Faction1, p.Faction2, realm, near);
                case ChangeSettlementOwnerLogEntry c: return c.PreviousClan?.Kingdom == realm || c.NewClan?.Kingdom == realm || (c.Settlement != null && interest.Contains(c.Settlement));
                case BesiegeSettlementLogEntry b: return b.BesiegerFaction == realm || b.OwnerClanBeforeBesiege?.Kingdom == realm || (b.Settlement != null && interest.Contains(b.Settlement));
                case TakePrisonerLogEntry t: return t.Prisoner?.MapFaction == realm || t.CapturerPartyMapFaction == realm;
                case EndCaptivityLogEntry r: return r.Prisoner?.MapFaction == realm || r.CapturerMapFaction == realm;
                case CharacterKilledLogEntry k: return k.VictimClan?.Kingdom == realm || k.Killer?.MapFaction == realm;
                case DefeatCharacterLogEntry d: return d.WinnerHero?.MapFaction == realm || d.LoserHero?.MapFaction == realm;
                case ClanChangeKingdomLogEntry cc: return cc.OldKingdom == realm || cc.NewKingdom == realm;
            }
            if (Skip.Contains(Kind(e)) || !(e is IEncyclopediaLog log)) return false;
            foreach (MBObjectBase o in interest)
                if (log.IsVisibleInEncyclopediaPageOf(o)) return true;
            return false;
        }

        /// <summary>A war or a peace of the realm's own, or of a realm it is at war or allied with.</summary>
        private static bool Wars(IFaction a, IFaction b, Kingdom realm, HashSet<IFaction> near)
            => a == realm || b == realm || (a != null && near.Contains(a)) || (b != null && near.Contains(b));

        private static string Kind(LogEntry e)
        {
            string n = e.GetType().Name;
            return n.EndsWith("LogEntry", StringComparison.Ordinal) ? n.Substring(0, n.Length - "LogEntry".Length) : n;
        }

        /// <summary>The game's sentence without its encyclopedia links and markup.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string s = Spaces.Replace(Tags.Replace(text, ""), " ").Trim();
            return s.Length == 0 ? null : s;
        }
    }
}
