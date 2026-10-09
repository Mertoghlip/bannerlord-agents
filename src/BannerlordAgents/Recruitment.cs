using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// Why the watched realm's parties do or do not refill: a measurement for the logs only (docs/LOGGING.md), never shown
    /// to the agent. The game's own gates (RecruitmentCampaignBehavior, 1.4.8) decide whether a lord the game runs may
    /// recruit, each hour it spends in a town or an unraided village:
    /// - the party's gold above HeroHelper.StartRecruitingMoneyLimit (50 + 20 a man, up to 150 men);
    /// - for a lord who does not lead the clan, the clan's gold above StartRecruitingMoneyLimitForClanLeader (50 + the
    ///   clan leader's party wages + 40 a man in that party);
    /// - room in the party (all members below its size limit), not starving, and room in its wage budget per recruit;
    /// - volunteers waiting with the place's notables, within the index the notable's relation allows (VolunteerModel).
    /// Once a day (game's thread) each lord and each holding of the realm is measured against those gates, with the men
    /// the lord recruited that day (OnTroopRecruitedEvent) and the hours it spent where it could recruit (worker, from the
    /// hourly copy). One line per day in recruitment.jsonl in the campaign's folder, and a sentence in the log.
    /// </summary>
    internal static class Recruitment
    {
        private sealed class Taken
        {
            public int Men;
            public readonly Dictionary<string, int> At = new Dictionary<string, int>();
        }

        // game's thread: the day's recruits by the watched realm's lords
        private static Dictionary<string, Taken> _today = new Dictionary<string, Taken>();
        // worker: the day's hours each lord spent where it could recruit, and the hours counted
        private static readonly Dictionary<string, int> HoursAt = new Dictionary<string, int>();
        private static int _hoursCounted;
        /// <summary>The last day's sentence, for the console (any thread may read it).</summary>
        public static volatile string LastSummary = "No day measured yet.";

        public static void Reset()
        {
            _today = new Dictionary<string, Taken>();
            LastSummary = "No day measured yet.";
            Background.Post("recruitment reset", () => { HoursAt.Clear(); _hoursCounted = 0; });
        }

        /// <summary>Game's thread, every recruit in the world: kept only for the watched realm's lords (a lookup, no allocation for anyone else).</summary>
        public static void OnTroopRecruited(Hero recruiter, Settlement where, Hero source, CharacterObject troop, int amount)
        {
            Kingdom watched = Bridge.Watched;
            if (watched == null || recruiter == null || amount <= 0 || recruiter.MapFaction != watched) return;
            if (!_today.TryGetValue(recruiter.StringId, out Taken t)) _today[recruiter.StringId] = t = new Taken();
            t.Men += amount;
            string at = where?.StringId ?? "elsewhere";
            t.At.TryGetValue(at, out int n);
            t.At[at] = n + amount;
        }

        /// <summary>Worker, every in-game hour: which of the realm's lords sat where the game lets a lord recruit.</summary>
        public static void OnHour(Snapshot s)
        {
            _hoursCounted++;
            foreach (PartyRow r in s.Parties)
            {
                if (r.Faction != s.Watched || r.Kind != "lord" || r.InSettlement == null || r.InBattle) continue;
                if (!s.SettlementById.TryGetValue(r.InSettlement, out SettlementRow h)) continue;
                bool can = (h.Kind == "town" && h.BesiegedBy == null) || (h.Kind == "village" && (h.VillageState == null || h.VillageState == "Normal"));
                if (!can) continue;
                HoursAt.TryGetValue(r.Leader ?? r.Id, out int n);
                HoursAt[r.Leader ?? r.Id] = n + 1;
            }
        }

        /// <summary>Game's thread, once a day (and on agents.recruits): measures the realm against the gates and hands the day to the worker.</summary>
        public static string Daily(bool fromConsole = false)
        {
            Kingdom k = Bridge.Watched;
            if (k == null) return "No realm is under watch.";
            double hours = CampaignTime.Now.ToHours;
            HashSet<MobileParty> held = Orders.HeldParties();
            Dictionary<string, Taken> taken = _today;
            if (!fromConsole) _today = new Dictionary<string, Taken>();
            var lords = new List<object>();
            int count = 0, belowGold = 0, belowClan = 0, full = 0, starving = 0, noWage = 0, recruiters = 0, recruited = 0;
            double fill = 0;
            foreach (MobileParty p in MobileParty.AllLordParties)
            {
                if (p == null || !p.IsActive || p.MapFaction != k || p.LeaderHero == null) continue;
                Hero h = p.LeaderHero;
                count++;
                bool leadsClan = h.Clan != null && h.Clan.Leader == h;
                float goldBar = HeroHelper.StartRecruitingMoneyLimit(h);
                float clanBar = leadsClan ? 0 : HeroHelper.StartRecruitingMoneyLimitForClanLeader(h);
                int gold = p.PartyTradeGold, clanGold = h.Clan?.Gold ?? 0;
                int all = p.Party.NumberOfAllMembers, limit = p.Party.PartySizeLimit;
                int wageRoom = p.GetAvailableWageBudget();
                bool okGold = gold > goldBar, okClan = leadsClan || clanGold > clanBar, room = (all + 0.5f) / Math.Max(1, limit) <= 1f, starve = p.Party.IsStarving;
                if (!okGold) belowGold++;
                if (!okClan) belowClan++;
                if (!room) full++;
                if (starve) starving++;
                if (wageRoom <= 0) noWage++;
                fill += limit > 0 ? (double)all / limit : 1;
                taken.TryGetValue(h.StringId, out Taken t);
                if (t != null && t.Men > 0) { recruiters++; recruited += t.Men; }
                lords.Add(new
                {
                    lord = h.StringId, name = Names.Hero(h), clan = h.Clan?.StringId, leads_clan = leadsClan, men = all, limit, gold, gold_bar = (int)goldBar,
                    clan_gold = clanGold, clan_bar = leadsClan ? (int?)null : (int)clanBar, wage = p.TotalWage, wage_limit = p.PaymentLimit, wage_room = wageRoom,
                    starving = starve, in_army = p.Army != null, held = held.Contains(p), inside = p.CurrentSettlement?.StringId,
                    may_recruit = okGold && okClan && room && !starve, recruited = t?.Men ?? 0, recruited_at = t?.At
                });
            }
            var places = new List<object>();
            int waiting = 0, rulerCould = 0, placesWithVolunteers = 0;
            Hero ruler = k.Leader;
            foreach (Settlement s in k.Settlements)
            {
                if (s == null || (!s.IsTown && !s.IsVillage)) continue;
                int here = 0, could = 0;
                try
                {
                    foreach (Hero n in s.Notables)
                    {
                        if (n == null || !n.IsAlive || n.VolunteerTypes == null) continue;
                        int max = ruler != null ? Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(ruler, n) : 0;
                        for (int i = 0; i < n.VolunteerTypes.Length; i++)
                        {
                            if (n.VolunteerTypes[i] == null) continue;
                            here++;
                            if (i < max) could++;
                        }
                    }
                }
                catch (Exception e) { Diag.Swallowed("counting the volunteers at " + s.StringId, e); }
                waiting += here;
                rulerCould += could;
                if (here > 0) placesWithVolunteers++;
                places.Add(new
                {
                    id = s.StringId, name = Names.Settlement(s), kind = s.IsTown ? "town" : "village", culture = s.Culture?.StringId,
                    state = s.IsVillage ? s.Village.VillageState.ToString() : s.IsUnderSiege ? "besieged" : "normal", waiting = here, ruler_could_take = could
                });
            }
            var summary = new
            {
                lords = count, average_fill = count > 0 ? Math.Round(fill / count, 2) : 0, below_party_gold_bar = belowGold, below_clan_gold_bar = belowClan, ranks_full = full,
                starving, no_wage_room = noWage, recruiters, recruited, volunteers_waiting = waiting, ruler_could_take = rulerCould, places_with_volunteers = placesWithVolunteers,
                places = places.Count
            };
            string date = Text.When(hours);
            string realm = k.StringId;
            string line = "recruitment, " + date + (fromConsole ? " (so far today)" : "") + ": " + count + " lords at " + Math.Round(100 * (count > 0 ? fill / count : 0)) + "% of their size limits on average; "
                          + belowGold + " below the party-gold bar, " + belowClan + " below the clan-gold bar, " + noWage + " without wage room, " + full + " full, " + starving + " starving; "
                          + recruited + " men recruited by " + recruiters + " lords; " + waiting + " volunteers waiting in " + placesWithVolunteers + " of the realm's " + places.Count
                          + " towns and villages (" + rulerCould + " within what the ruler's relations allow)";
            if (!fromConsole)
            {
                Background.Post("recruitment day", () =>
                {
                    var hoursAt = new Dictionary<string, int>(HoursAt);
                    int counted = _hoursCounted;
                    HoursAt.Clear();
                    _hoursCounted = 0;
                    string full2 =line + "; " + hoursAt.Count + " of " + lords.Count + " lords spent at least one of the last " + counted + " hours where they could recruit";
                    Brain.WriteData("recruitment.jsonl", new { Type = "recruitment", Hours = hours, Date = date, Realm = realm, Summary = summary, HoursCounted = counted, HoursWhereRecruiting = hoursAt, Lords = lords, Places = places });
                    Diag.Event("recruitment", "day", new { date, summary, lords_in_recruiting_places = hoursAt.Count, hours_counted = counted });
                    Log.Line(full2);
                    LastSummary = full2;
                });
            }
            else LastSummary = line;
            return line;
        }
    }
}
