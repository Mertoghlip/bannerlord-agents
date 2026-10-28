using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// One diplomatic act of an agent's answer (0.13, docs/PLAN-0.13.md B2), as written: what it is, with whom, and its terms. The acts are
    /// listed in DiploRules.Acts. Game-free.
    /// </summary>
    internal sealed class DiploAct
    {
        public string File, Realm, Act, With, Against, Clan, Policy, Vote, Option, Weight, Offer, Why;
        public int Index, Poke = -1;
        public int? Tribute, Days;
        public bool Overrule, Abolish;
        public JObject Raw;
        public DateTime Received = DateTime.Now;

        public static DiploAct From(JObject o, int poke, string file, int index)
        {
            string S(params string[] keys)
            {
                foreach (string k in keys)
                    if (o[k] != null && o[k].Type != JTokenType.Null) return ((string)o[k])?.Trim().Trim('[', ']');
                return null;
            }
            int? I(string k) => o[k] != null && (o[k].Type == JTokenType.Integer || o[k].Type == JTokenType.Float) ? (int?)Math.Round((double)o[k]) : null;
            var a = new DiploAct
            {
                File = file, Index = index, Poke = (int?)o["poke"] ?? poke, Raw = o,
                Act = (S("act", "do") ?? "").ToLowerInvariant().Replace(' ', '_').Replace('-', '_'),
                With = S("with", "realm", "kingdom", "to", "from", "ally"), Against = S("against"), Clan = S("clan"), Policy = S("policy"),
                Vote = S("vote", "vote_id"), Option = S("option", "choice"), Weight = S("weight")?.ToLowerInvariant(), Offer = S("offer", "offer_id"), Why = S("why"),
                Tribute = I("tribute"), Days = I("days"), Overrule = (bool?)o["overrule"] ?? false, Abolish = (bool?)o["abolish"] ?? false
            };
            // the policy's two ways written as acts of their own
            if (a.Act == "adopt_policy") a.Act = "propose_policy";
            else if (a.Act == "abolish_policy") { a.Act = "propose_policy"; a.Abolish = true; }
            return a;
        }

        public override string ToString() => Act + (With != null ? " " + With : "") + (Against != null ? " against " + Against : "") + (Clan != null ? " " + Clan : "")
                                             + (Policy != null ? " " + Policy : "") + (Vote != null ? " " + Vote : "") + (Option != null ? " " + Option : "") + (Offer != null ? " " + Offer : "");
    }

    /// <summary>A realm's diplomacy as its ruler's screens show it (0.13, B3), copied hourly on the game's thread for each realm an agent rules.</summary>
    internal sealed class DiploView
    {
        public string Realm, RealmName, RulerClan, RulerClanName;
        public float Influence;
        /// <summary>The War of the Ring in full (TAOM): the peace and wars it forbids cannot be made, whatever the votes.</summary>
        public bool RingWar;
        /// <summary>What each proposal costs the ruler's clan in influence, by act.</summary>
        public readonly Dictionary<string, int> Costs = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<DiploStance> Stances = new List<DiploStance>();
        public readonly List<DiploVote> Votes = new List<DiploVote>();
        public readonly List<DiploOffer> Offers = new List<DiploOffer>();
        /// <summary>The realm's own clans, and mercenary clans in no realm (for hire).</summary>
        public readonly List<DiploClan> Clans = new List<DiploClan>();
        public readonly List<DiploClan> ForHire = new List<DiploClan>();
        public readonly List<DiploPolicy> Policies = new List<DiploPolicy>();
        /// <summary>Supporting a vote: slightly, strongly, fully, in influence.</summary>
        public int Slight = 20, Strong = 60, Full = 150;
    }

    internal sealed class DiploStance
    {
        public string Id, Name, Stance = "peace";
        public double Since = double.NaN, TradeUntil = double.NaN;
        public int OurLosses, TheirLosses, OurSieges, TheirSieges, OurRaids, TheirRaids, Lords;
        /// <summary>Daily tribute: above 0 we pay them, below 0 they pay us; and its days left.</summary>
        public int Tribute, TributeDays;
        public float Strength;
        public bool Trade, NoPeace, NoWar, Permanent;
        /// <summary>At war with it because an ally called us in (CalledBy), or we called an ally in against it (Called).</summary>
        public string CalledBy, Called;
    }

    internal sealed class DiploVote
    {
        public string Id, Title, Kind, Proposer;
        public double Opened = double.NaN, Closes = double.NaN;
        public readonly List<string> Options = new List<string>();
        /// <summary>Each option's share of the clans' support so far (the game's own count).</summary>
        public readonly List<float> Support = new List<float>();
        /// <summary>The influence the ruler pays to decide each option against the most supported (0 for the most supported itself).</summary>
        public readonly List<int> Override = new List<int>();
        public string YourVote, YourDecision;
        /// <summary>Held by the game itself (your realm's own votes when an agent rules it), not by us.</summary>
        public bool ByGame;
    }

    internal sealed class DiploOffer
    {
        public string Id, Kind, From, FromName, Against, AgainstName;
        public int Tribute, Days;
        public double Closes = double.NaN;
    }

    internal sealed class DiploClan
    {
        public string Id, Name;
        public bool Mercenary, Ruling;
        public int Fiefs, Lords, Award;
        public float Strength;
        public double ContractUntil = double.NaN;
    }

    internal sealed class DiploPolicy
    {
        public string Id, Name;
        public bool Active;
    }

    /// <summary>
    /// The checks of a diplomatic act that need no game (0.13, B2): the act known, its realm and terms there, the stance it needs (at war for
    /// peace, at peace for war or an alliance, an ally for a call to war), the War of the Ring's rule, the influence, the vote or offer open,
    /// the clan in the realm or for hire. The game's own conditions are checked again on its thread when the act is carried out. Game-free.
    /// </summary>
    internal static class DiploRules
    {
        public static readonly string[] Acts =
        {
            "propose_war", "propose_peace", "propose_alliance", "call_to_war", "propose_trade", "propose_policy", "propose_expel", "hire_mercenary", "release_mercenary",
            "vote", "decide", "accept_peace", "decline_peace", "accept_alliance", "decline_alliance", "accept_call", "decline_call", "accept_trade", "decline_trade"
        };

        /// <summary>The kind of offer an answer to one is about ("accept_peace" answers a peace offer).</summary>
        public static string OfferKind(string act) => act != null && (act.StartsWith("accept_", StringComparison.Ordinal) || act.StartsWith("decline_", StringComparison.Ordinal))
            ? act.Substring(act.IndexOf('_') + 1) : null;

        public static DiploStance Realm(DiploView v, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return null;
            return v.Stances.FirstOrDefault(s => string.Equals(s.Id, q, StringComparison.OrdinalIgnoreCase))
                   ?? v.Stances.FirstOrDefault(s => Text.Plain(s.Name) == Text.Plain(q)) ?? v.Stances.FirstOrDefault(s => Text.Plain(s.Name).Contains(Text.Plain(q)));
        }

        private static DiploClan ClanOf(IEnumerable<DiploClan> list, string q) =>
            string.IsNullOrWhiteSpace(q) ? null : list.FirstOrDefault(c => string.Equals(c.Id, q, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault(c => Text.Plain(c.Name) == Text.Plain(q));

        /// <summary>The option of a vote an act names: by its number from 1, or its words.</summary>
        public static int OptionIndex(DiploVote vote, string q)
        {
            if (vote == null || string.IsNullOrWhiteSpace(q)) return -1;
            if (int.TryParse(q.Trim(), out int n) && n >= 1 && n <= vote.Options.Count) return n - 1;
            string p = Text.Plain(q);
            int i = vote.Options.FindIndex(o => Text.Plain(o) == p);
            return i >= 0 ? i : vote.Options.FindIndex(o => Text.Plain(o).StartsWith(p, StringComparison.Ordinal));
        }

        /// <summary>
        /// Why the act cannot be made now (null: it can, as far as the realm's screens show), and what it acts on (a realm's, clan's,
        /// policy's, vote's or offer's id).
        /// </summary>
        public static string Check(DiploAct a, DiploView v, out string target)
        {
            target = null;
            if (v == null) return "your realm's diplomacy is not known yet (the game has not copied an hour since the load)";
            if (!Acts.Contains(a.Act)) return "'" + a.Act + "' is not a diplomatic act; the acts: " + string.Join(", ", Acts);
            int Cost(string act) => v.Costs.TryGetValue(act, out int c) ? c : 0;
            string Influence(string act)
            {
                int c = Cost(act);
                return c > v.Influence ? "it costs your clan " + c + " influence and it has " + (int)Math.Floor(v.Influence) : null;
            }
            DiploStance other = null;
            switch (a.Act)
            {
                case "propose_war":
                case "propose_peace":
                case "propose_alliance":
                case "propose_trade":
                    other = Realm(v, a.With);
                    if (other == null) return "no realm called '" + a.With + "' is known to yours (give its id in [brackets] from DIPLOMACY)";
                    target = other.Id;
                    break;
            }
            switch (a.Act)
            {
                case "propose_war":
                    if (other.Stance == "war") return "you are already at war with " + other.Name;
                    if (other.Stance == "alliance") return other.Name + " is your ally: war on an ally cannot be declared";
                    if (other.NoWar) return "the War of the Ring forbids war between " + v.RealmName + " and " + other.Name;
                    return Influence("propose_war");
                case "propose_peace":
                    if (other.Stance != "war") return "you are not at war with " + other.Name;
                    if (other.NoPeace) return "the War of the Ring forbids peace with " + other.Name + " while it rages";
                    if (a.Days.HasValue && (a.Days < 0 || a.Days > 1000)) return "days must be between 0 and 1000";
                    return Influence("propose_peace");
                case "propose_alliance":
                    if (other.Stance == "war") return "you are at war with " + other.Name + ": make peace first";
                    if (other.Stance == "alliance") return other.Name + " is your ally already";
                    return Influence("propose_alliance");
                case "propose_trade":
                    if (other.Stance == "war") return "you are at war with " + other.Name;
                    if (other.Trade) return "you have a trade agreement with " + other.Name + " already";
                    return Influence("propose_trade");
                case "call_to_war":
                {
                    DiploStance ally = Realm(v, a.With), enemy = Realm(v, a.Against);
                    if (ally == null) return "no realm called '" + a.With + "' is known to yours (with: the ally you call)";
                    if (enemy == null) return "no realm called '" + a.Against + "' is known to yours (against: the enemy you call it to fight)";
                    target = ally.Id;
                    if (ally.Stance != "alliance") return ally.Name + " is not your ally";
                    if (enemy.Stance != "war") return "you are not at war with " + enemy.Name;
                    return Influence("call_to_war");
                }
                case "propose_policy":
                {
                    DiploPolicy p = v.Policies.FirstOrDefault(x => string.Equals(x.Id, a.Policy, StringComparison.OrdinalIgnoreCase)) ?? v.Policies.FirstOrDefault(x => Text.Plain(x.Name) == Text.Plain(a.Policy));
                    if (p == null) return "no policy called '" + a.Policy + "' (DIPLOMACY lists them)";
                    target = p.Id;
                    if (a.Abolish && !p.Active) return p.Name + " is not in force, so it cannot be abolished";
                    if (!a.Abolish && p.Active) return p.Name + " is in force already (\"abolish\": true to propose ending it)";
                    return Influence("propose_policy");
                }
                case "propose_expel":
                {
                    DiploClan c = ClanOf(v.Clans, a.Clan);
                    if (c == null) return "no clan of your realm is called '" + a.Clan + "'";
                    target = c.Id;
                    if (c.Ruling) return "your own clan cannot be expelled";
                    if (c.Mercenary) return c.Name + " serves for pay: let it go with release_mercenary";
                    return Influence("propose_expel");
                }
                case "hire_mercenary":
                {
                    DiploClan c = ClanOf(v.ForHire, a.Clan);
                    if (c == null) return ClanOf(v.Clans, a.Clan) != null ? a.Clan + " serves your realm already" : "no clan for hire is called '" + a.Clan + "' (DIPLOMACY lists those for hire)";
                    target = c.Id;
                    return null;
                }
                case "release_mercenary":
                {
                    DiploClan c = ClanOf(v.Clans, a.Clan);
                    if (c == null || !c.Mercenary) return "no clan serving your realm for pay is called '" + a.Clan + "'";
                    target = c.Id;
                    return null;
                }
                case "vote":
                case "decide":
                {
                    DiploVote vote = v.Votes.FirstOrDefault(x => string.Equals(x.Id, a.Vote, StringComparison.OrdinalIgnoreCase));
                    if (vote == null) return "no vote " + (a.Vote ?? "(vote: its id)") + " is open in your realm (OPEN VOTES lists them)";
                    target = vote.Id;
                    int i = OptionIndex(vote, a.Option);
                    if (i < 0) return "the vote " + vote.Id + " has no option '" + a.Option + "'; its options: " + string.Join(", ", vote.Options.Select((o, n) => (n + 1) + " " + o));
                    if (a.Act == "vote")
                    {
                        int w = a.Weight == "slight" || a.Weight == "slightly" ? v.Slight : a.Weight == "strong" || a.Weight == "strongly" ? v.Strong : v.Full;
                        if (a.Weight != null && !new[] { "slight", "slightly", "strong", "strongly", "full", "fully" }.Contains(a.Weight)) return "weight is slight, strong or full";
                        if (w > v.Influence) return "supporting it " + (a.Weight ?? "fully") + " costs " + w + " influence and your clan has " + (int)Math.Floor(v.Influence) + " (a lighter weight costs less)";
                        return null;
                    }
                    int over = i < vote.Override.Count ? vote.Override[i] : 0;
                    if (over > v.Influence) return "deciding " + vote.Options[i] + " against the clans costs " + over + " influence and your clan has " + (int)Math.Floor(v.Influence);
                    return null;
                }
                default:
                {
                    string kind = OfferKind(a.Act);
                    DiploOffer o = v.Offers.FirstOrDefault(x => a.Offer != null && string.Equals(x.Id, a.Offer, StringComparison.OrdinalIgnoreCase));
                    if (o == null)
                    {
                        DiploStance from = Realm(v, a.With);
                        o = v.Offers.FirstOrDefault(x => x.Kind == kind && from != null && x.From == from.Id);
                    }
                    if (o == null) return "no " + Noun(kind) + " offer " + (a.Offer ?? (a.With != null ? "from " + a.With : "")) + " is open to your realm (OPEN VOTES AND OFFERS lists them)";
                    if (o.Kind != kind) return "offer " + o.Id + " is " + KindWords(o.Kind) + ", not " + KindWords(kind);
                    target = o.Id;
                    return null;
                }
            }
        }

        /// <summary>An offer's kind as a noun: peace, alliance, call to war, trade agreement.</summary>
        public static string Noun(string kind) => kind == "call" ? "call to war" : kind == "trade" ? "trade agreement" : kind ?? "";

        public static string KindWords(string kind)
        {
            switch (kind)
            {
                case "peace": return "a peace";
                case "alliance": return "an alliance";
                case "call": return "a call to war";
                case "trade": return "a trade agreement";
                default: return "a";
            }
        }

        // ---------- the report's sections (B3) ----------

        /// <summary>DIPLOMACY: the ruler's influence and what each act costs, the War of the Ring, each realm's stance and the war's tally, the realm's clans, those for hire, the policies.</summary>
        public static List<string> Diplomacy(DiploView v, double now, Func<double, string> when)
        {
            var lines = new List<string>();
            if (v == null) return lines;
            string C(string act) => v.Costs.TryGetValue(act, out int c) ? Text.N(c) : "?";
            lines.Add("Your clan's influence: " + Text.N((int)Math.Floor(v.Influence)) + ". A proposal costs it, in influence: war " + C("propose_war") + ", peace " + C("propose_peace") + ", an alliance " + C("propose_alliance")
                      + ", a call to war " + C("call_to_war") + ", a trade agreement " + C("propose_trade") + ", a policy " + C("propose_policy") + ", expelling a clan " + C("propose_expel")
                      + ". Your realm's clans then vote on it as the game has them vote; you vote with them and, as ruler, decide (OPEN VOTES). Supporting a vote costs " + v.Slight + ", " + v.Strong + " or " + v.Full + ".");
            if (v.RingWar) lines.Add("The War of the Ring rages: peace with a realm marked \"no peace\" cannot be made, and war on one marked \"no war\" cannot be declared, whatever the votes; influence spent on them is lost.");
            foreach (DiploStance s in v.Stances.OrderBy(x => x.Stance == "war" ? 0 : x.Stance == "alliance" ? 1 : 2).ThenByDescending(x => x.Strength))
            {
                var sb = new StringBuilder(s.Name + " [" + s.Id + "]: ");
                string since = !double.IsNaN(s.Since) ? " since " + when(s.Since) + " (" + Text.N((int)Math.Max(0, Math.Round((now - s.Since) / 24))) + " days)" : "";
                if (s.Stance == "war")
                {
                    sb.Append("at war" + since + "; men lost yours " + Text.N(s.OurLosses) + ", theirs " + Text.N(s.TheirLosses) + "; holdings taken by you " + s.OurSieges + ", by them " + s.TheirSieges
                              + "; villages raided by you " + s.OurRaids + ", by them " + s.TheirRaids);
                    if (s.CalledBy != null) sb.Append("; you fight it at the call of your ally " + s.CalledBy);
                    if (s.Called != null) sb.Append("; your ally " + s.Called + " fights it at your call");
                    if (s.NoPeace) sb.Append("; no peace (War of the Ring)");
                }
                else if (s.Stance == "alliance") sb.Append("your ally" + since + (s.Permanent ? " (bound for good by the War of the Ring)" : ""));
                else sb.Append("at peace" + since + (s.NoWar ? "; no war (War of the Ring)" : ""));
                if (s.Tribute != 0 && s.TributeDays > 0) sb.Append("; tribute: " + (s.Tribute > 0 ? "you pay them " : "they pay you ") + Text.N(Math.Abs(s.Tribute)) + " a day for " + s.TributeDays + " more days");
                if (s.Trade) sb.Append("; trade agreement" + (!double.IsNaN(s.TradeUntil) ? " until " + when(s.TradeUntil) : ""));
                sb.Append("; " + s.Lords + " lords, strength " + Text.N((int)s.Strength) + ".");
                lines.Add(sb.ToString());
            }
            List<DiploClan> mercs = v.Clans.Where(c => c.Mercenary).ToList();
            List<DiploClan> vassals = v.Clans.Where(c => !c.Mercenary && !c.Ruling).ToList();
            if (vassals.Count > 0) lines.Add("Your realm's clans (expel one with propose_expel): " + string.Join("; ", vassals.Select(c => c.Name + " [" + c.Id + "], " + c.Lords + " lords, strength " + Text.N((int)c.Strength) + ", " + c.Fiefs + " fiefs")) + ".");
            if (mercs.Count > 0) lines.Add("Mercenary clans in your pay (let one go with release_mercenary): " + string.Join("; ", mercs.Select(c => c.Name + " [" + c.Id + "], strength " + Text.N((int)c.Strength)
                                                                                                               + (c.Award > 0 ? ", paid " + c.Award + " a day per influence point" : "") + (!double.IsNaN(c.ContractUntil) && c.ContractUntil > now ? ", bound until " + when(c.ContractUntil) : ""))) + ".");
            if (v.ForHire.Count > 0) lines.Add("Mercenary clans for hire, in no realm (hire_mercenary; each judges your offer by the game's own terms): " + string.Join("; ", v.ForHire.OrderByDescending(c => c.Strength).Take(12)
                                                    .Select(c => c.Name + " [" + c.Id + "], strength " + Text.N((int)c.Strength))) + (v.ForHire.Count > 12 ? "; and " + (v.ForHire.Count - 12) + " smaller" : "") + ".");
            List<DiploPolicy> on = v.Policies.Where(p => p.Active).ToList(), off = v.Policies.Where(p => !p.Active).ToList();
            if (v.Policies.Count > 0)
                lines.Add("Policies in force: " + (on.Count > 0 ? string.Join(", ", on.Select(p => p.Name + " [" + p.Id + "]")) : "none") + ". Others the game has (propose_policy): "
                          + string.Join(", ", off.Select(p => p.Name + " [" + p.Id + "]")) + ".");
            return lines;
        }

        /// <summary>OPEN VOTES AND OFFERS: each vote of the realm with its options and their support, your vote, when it closes and what deciding costs; each offer made to it.</summary>
        public static List<string> Open(DiploView v, Func<double, string> when)
        {
            var lines = new List<string>();
            if (v == null) return lines;
            foreach (DiploVote x in v.Votes)
            {
                var sb = new StringBuilder("[" + x.Id + "] " + x.Title + (x.Proposer != null ? ", proposed by " + x.Proposer : "") + (!double.IsNaN(x.Closes) ? "; closes " + when(x.Closes) : "") + ". ");
                sb.Append("Options and the clans' support so far: " + string.Join("; ", x.Options.Select((o, i) => (i + 1) + " " + o + " " + (i < x.Support.Count ? Math.Round(x.Support[i] * 100) + "%" : "?")
                                                                                     + (i < x.Override.Count && x.Override[i] > 0 ? " (to decide it against the clans: " + Text.N(x.Override[i]) + " influence)" : ""))) + ". ");
                sb.Append("Your vote: " + (x.YourVote ?? "none yet") + (x.YourDecision != null ? "; your decision: " + x.YourDecision : "") + ".");
                lines.Add(sb.ToString());
            }
            foreach (DiploOffer o in v.Offers)
            {
                string terms = o.Kind == "peace" ? (o.Tribute > 0 ? "they pay you " + Text.N(o.Tribute) + " a day for " + o.Days + " days" : o.Tribute < 0 ? "you pay them " + Text.N(-o.Tribute) + " a day for " + o.Days + " days" : "no tribute either way")
                               : o.Kind == "call" ? "to join their war on " + o.AgainstName + " [" + o.Against + "]" : "";
                lines.Add("[" + o.Id + "] " + Cap(KindWords(o.Kind)) + " offered by " + o.FromName + " [" + o.From + "]" + (terms.Length > 0 ? ": " + terms : "") + (!double.IsNaN(o.Closes) ? "; answer by " + when(o.Closes) : "")
                          + ". accept_" + o.Kind + " or decline_" + o.Kind + "; with no answer by then it is accepted, as the game does between the realms it runs.");
            }
            return lines;
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
