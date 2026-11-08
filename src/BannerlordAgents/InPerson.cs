using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BannerlordAgents
{
    /// <summary>One side of a battle the human player fought, as it was fought (0.17.3).</summary>
    internal sealed class InPersonSide
    {
        public string Realm, RealmName, Leader;
        public List<string> Realms = new List<string>();
        public int Men, Lost, Lords, Infantry, Ranged, Cavalry, HorseArchers;
        public double Strength;
    }

    /// <summary>A battle the human player fought in person (0.17.3): when and where, what kind, his side and the other, who won, and the realms that knew of it.</summary>
    internal sealed class InPersonBattle
    {
        public double Hours;
        public string Date, Kind, Place, Load;
        public bool Won;
        public InPersonSide His = new InPersonSide(), Other = new InPersonSide();
        public List<string> Known = new List<string>();
    }

    /// <summary>
    /// The human player's battles, for the realms' agents to study (0.17.3, Nov 8: they should study battles involving the player as well as the player's
    /// moves on the campaign map and take notes on what seems to damage them most, what level of strength they need to win a battle involving a player,
    /// what army compositions, and how they behave, so they can also strategise accordingly). Each battle he fought in person (PlayerBattles, on the game's
    /// thread) goes into the campaign's in_person.jsonl, labelled with its session as the chronicle is, and a load keeps those of its save's history. A
    /// realm knows of one when its lords fought in it or it had him in view within two hours of it, so its record holds only what it saw. The report says
    /// who he is and how his battles are fought; the council's briefing and the look-up file the_human_player give the record. Worker only.
    /// </summary>
    internal static class InPerson
    {
        private static readonly object Gate = new object();
        private static readonly List<InPersonBattle> All = new List<InPersonBattle>();
        private static string _file;

        public const string LookupName = "the_human_player";

        public static void Reset(string dataDir, History history)
        {
            lock (Gate)
            {
                All.Clear();
                _file = dataDir != null ? Path.Combine(dataDir, "in_person.jsonl") : null;
                if (_file == null || !File.Exists(_file)) return;
                try
                {
                    using (var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.Trim().Length == 0) continue;
                            try
                            {
                                InPersonBattle b = Json.Read<InPersonBattle>(line);
                                if (b != null && (history == null || history.Holds(b.Load, b.Hours))) All.Add(b);
                            }
                            catch (Exception e) when (e is Newtonsoft.Json.JsonException || e is FormatException || e is InvalidCastException) { Diag.Count("in person:unreadable lines"); }
                        }
                    }
                }
                catch (IOException e) { Diag.Error("reading the human player's battles", e); }
            }
        }

        /// <summary>A battle he fought (the game's thread hands it over): which watched realms knew of it, then kept and written down.</summary>
        public static void Add(InPersonBattle b, string playerParty)
        {
            foreach (RealmAgent a in Agents.All)
            {
                if (a.Realm == null || b.Known.Contains(a.Realm)) continue;
                bool fought = b.His.Realms.Contains(a.Realm) || b.Other.Realms.Contains(a.Realm);
                bool saw = playerParty != null && Brain.Get(a.Realm).LastSeen.TryGetValue(playerParty, out ViewParty v) && b.Hours - v.SeenHours <= 2;
                if (fought || saw) b.Known.Add(a.Realm);
            }
            lock (Gate)
            {
                All.Add(b);
                if (_file != null) Files.Append(_file, Json.Line(b));
            }
            Diag.Event("in person", "battle", new { b.Kind, b.Place, b.Won, his = b.His.Men, other = b.Other.Men, known = b.Known });
        }

        /// <summary>The battles a realm knows of, oldest first.</summary>
        public static List<InPersonBattle> For(string realm)
        {
            lock (Gate) return All.Where(b => b.Known.Contains(realm)).OrderBy(b => b.Hours).ToList();
        }

        /// <summary>For the smoke test: a battle kept as given (its realms known already).</summary>
        internal static void AddForTest(InPersonBattle b) { lock (Gate) All.Add(b); }

        /// <summary>How the human player's battles are fought, in words, for an enemy realm or for his own.</summary>
        public static string Rule(bool own) =>
            (own ? "Your battles are fought on the spot: of your lords, only those in your army or within " : "His battles are fought on the spot: when one begins, only the lords and bands within ")
            + Text.N(3) + " of it" + (own ? " when it begins" : " and the lords in the armies of its leaders") + " take part (3.75 while " + (own ? "you are" : "he is") + " in a siege), and the clock stands still until it ends, so nobody else arrives; "
            + "a storm " + (own ? "you lead or defend" : "he leads or defends") + " is fought by the siege camp and those inside alone. Battles without " + (own ? "you" : "him") + " last hours and take in lords as they arrive.";

        /// <summary>The report's few lines (YOUR ENEMY, THE HUMAN PLAYER, or the same for his own realm): who he is, the rule, his record in one line.</summary>
        public static List<string> Lines(Snapshot s, string fid)
        {
            var lines = new List<string>();
            PartyRow p = s?.PlayerParty != null && s.PartyById.TryGetValue(s.PlayerParty, out PartyRow r) ? r : null;
            if (p == null) return lines;
            string who = (p.Name ?? "the player") + " [" + p.Id + "]";
            bool own = p.Faction == fid;
            if (own)
            {
                lines.Add("You, " + who + ", are the human player. " + Rule(true));
                return lines;
            }
            lines.Add(who + ", of " + Views.FactionName(s, p.Faction) + ", is commanded in person by a human player. " + Rule(false)
                      + " Lords of yours sent at him close in and wait about 6 from him, then strike together. A human commander wins battles far above the odds the battle forecasts give: judge him by his record.");
            lines.Add(Summary(For(fid), p.Name ?? "He") + " Every one, and what your realm saw him do: " + "lookups/" + LookupName + ".txt" + ".");
            return lines;
        }

        /// <summary>His record in a line: battles, won, the lowest odds he won at, the losses traded, by kind.</summary>
        public static string Summary(List<InPersonBattle> bs, string name)
        {
            if (bs.Count == 0) return "Your realm has seen no battle " + name + " fought in person yet.";
            int won = bs.Count(b => b.Won);
            InPersonBattle low = bs.Where(b => b.Won && b.His.Strength > 0 && b.Other.Strength > 0).OrderBy(b => b.His.Strength / b.Other.Strength).FirstOrDefault();
            int hisLost = bs.Sum(b => b.His.Lost), otherLost = bs.Sum(b => b.Other.Lost);
            string kinds = string.Join(", ", bs.GroupBy(b => b.Kind).Select(g => g.Count() + " " + Kind(g.Key, g.Count()) + " (" + g.Count(b => b.Won) + " won)"));
            return "His record as your realm saw it: " + bs.Count + (bs.Count == 1 ? " battle" : " battles") + " fought in person, " + won + " won (" + kinds + ")"
                   + (low != null ? "; the lowest odds he won at: his side's strength " + Math.Round(low.His.Strength / low.Other.Strength, 2).ToString(Text.Inv) + " of the other's (" + low.Date + ", " + (low.Place ?? low.Kind) + ")" : "")
                   + "; men lost: his side " + Text.N(hisLost) + ", the other " + Text.N(otherLost) + (hisLost > 0 ? " (" + Math.Round((double)otherLost / hisLost, 1).ToString(Text.Inv) + " for each of his)" : "") + ".";
        }

        private static string Kind(string k, int n) => k == "field" ? (n == 1 ? "field battle" : "field battles") : k == "storm" ? (n == 1 ? "storm" : "storms") : k == "siege outside" ? (n == 1 ? "battle by a siege" : "battles by a siege") : k == "sally" ? (n == 1 ? "sally" : "sallies") : k;

        /// <summary>One battle in a line: date, kind, place, both sides (men, strength, lords, troops), who won, the losses.</summary>
        public static string Line(InPersonBattle b, string name)
        {
            string Side(InPersonSide x) => Text.N(x.Men) + " men, strength " + Text.N((int)x.Strength) + (x.Lords > 0 ? ", " + x.Lords + (x.Lords == 1 ? " lord" : " lords") : "") + Mix(x);
            return b.Date + ", " + Kind(b.Kind, 1) + (b.Place != null ? " at " + b.Place : "") + ": " + name + "'s side " + Side(b.His) + "; against " + (b.Other.RealmName ?? b.Other.Realm ?? "?")
                   + (b.Other.Leader != null ? " under " + b.Other.Leader : "") + ", " + Side(b.Other) + ". " + (b.Won ? name + " won" : name + " lost") + "; lost " + Text.N(b.His.Lost) + " men against " + Text.N(b.Other.Lost) + ".";
        }

        private static string Mix(InPersonSide x)
        {
            int all = x.Infantry + x.Ranged + x.Cavalry + x.HorseArchers;
            if (all <= 0) return "";
            string P(int n) => Math.Round(100.0 * n / all).ToString(Text.Inv) + "%";
            return " (foot " + P(x.Infantry) + ", archers " + P(x.Ranged) + ", horse " + P(x.Cavalry) + ", horse archers " + P(x.HorseArchers) + ")";
        }

        /// <summary>The council's section: who he is, the rule, his record, his last battles.</summary>
        public static string Section(Snapshot s, string fid, int last = 8)
        {
            PartyRow p = s?.PlayerParty != null && s.PartyById.TryGetValue(s.PlayerParty, out PartyRow r) ? r : null;
            if (p == null) return null;
            var sb = new System.Text.StringBuilder();
            bool own = p.Faction == fid;
            sb.AppendLine(own ? "YOUR OWN BATTLES, THE HUMAN PLAYER'S" : "YOUR ENEMY, THE HUMAN PLAYER");
            foreach (string l in Lines(s, fid)) sb.AppendLine("- " + l);
            List<InPersonBattle> bs = For(fid);
            foreach (InPersonBattle b in bs.Skip(Math.Max(0, bs.Count - last))) sb.AppendLine("- " + Line(b, p.Name ?? "He"));
            return sb.ToString().TrimEnd();
        }

        /// <summary>The look-up file: the rule, every battle the realm knows of, and what the realm saw him do (its chronicle's lines about him).</summary>
        public static string Lookup(Snapshot s, string fid, List<string> chronicle)
        {
            string section = Section(s, fid, int.MaxValue);
            if (section == null) return null;
            var sb = new System.Text.StringBuilder(section);
            sb.AppendLine().AppendLine().AppendLine("WHAT YOUR REALM SAW HIM DO, FROM YOUR CHRONICLE (newest last)");
            if (chronicle == null || chronicle.Count == 0) sb.AppendLine("- Nothing yet.");
            else foreach (string l in chronicle) sb.AppendLine("- " + l);
            return sb.ToString().TrimEnd();
        }
    }
}
