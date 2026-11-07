using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// The won battles' takings of each watched realm (0.9.4, TakingsPatch on the game's thread): kept in the campaign's
    /// takings.jsonl, read back at every load, so a realm's reckoning of what a battle brings in grows over the campaign. A
    /// realm counts only its own battles. Worker only.
    /// </summary>
    internal static class Takings
    {
        private static readonly object Gate = new object();
        private static readonly List<TakingsRow> Rows = new List<TakingsRow>();
        private static string _file;

        internal sealed class Sum
        {
            public int Battles, Fallen, Loot, Pieces, Prisoners, Lords, Ransom;
            /// <summary>Loot and prisoners for each enemy man fallen, and ransom for each prisoner taken.</summary>
            public double LootPerFallen => Fallen > 0 ? (double)Loot / Fallen : 0;
            public double PrisonersPerFallen => Fallen > 0 ? (double)Prisoners / Fallen : 0;
            public double RansomPerPrisoner => Prisoners > 0 ? (double)Ransom / Prisoners : 0;
        }

        /// <summary>The fewest won battles a realm needs before it reckons what one brings in.</summary>
        public const int Enough = 3;

        public static void Reset(string dataDir)
        {
            lock (Gate)
            {
                Rows.Clear();
                _file = Path.Combine(dataDir, "takings.jsonl");
                if (!File.Exists(_file)) return;
                var lines = new List<string>();
                try
                {
                    // shared: the record writer keeps the file open
                    using (var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                    {
                        string l;
                        while ((l = sr.ReadLine()) != null) lines.Add(l);
                    }
                }
                catch (IOException e) { Diag.Error("reading the battles' takings", e); }
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        JObject o = JObject.Parse(line);
                        Rows.Add(new TakingsRow
                        {
                            Hours = (double?)o["hours"] ?? 0, Date = (string)o["date"], Realm = (string)o["realm"], Kind = (string)o["kind"], Place = (string)o["place"], Load = (string)o["load"],
                            Men = (int?)o["men"] ?? 0, EnemyFallen = (int?)o["enemy_fallen"] ?? 0, Loot = (int?)o["loot"] ?? 0, Pieces = (int?)o["pieces"] ?? 0,
                            Prisoners = (int?)o["prisoners"] ?? 0, PrisonerLords = (int?)o["prisoner_lords"] ?? 0, Ransom = (int?)o["ransom"] ?? 0
                        });
                    }
                    catch (Exception e) when (e is Newtonsoft.Json.JsonException || e is FormatException || e is InvalidCastException || e is OverflowException) { Diag.Count("takings:unreadable lines"); }
                }
            }
        }

        public static void Add(TakingsRow r)
        {
            r.Load = Bridge.LoadId;
            lock (Gate)
            {
                Rows.Add(r);
                if (_file != null) Files.Append(_file, Json.Line(r));
            }
            Diag.Event("takings", "battle", new { realm = r.Realm, kind = r.Kind, lords = r.Lords, enemy_fallen = r.EnemyFallen, loot = r.Loot, prisoners = r.Prisoners, ransom = r.Ransom });
        }

        public static Sum For(string realm)
        {
            var s = new Sum();
            lock (Gate)
                foreach (TakingsRow r in Rows)
                {
                    if (r.Realm != realm) continue;
                    s.Battles++;
                    s.Fallen += r.EnemyFallen;
                    s.Loot += r.Loot;
                    s.Pieces += r.Pieces;
                    s.Prisoners += r.Prisoners;
                    s.Lords += r.PrisonerLords;
                    s.Ransom += r.Ransom;
                }
            return s;
        }

        /// <summary>The realm's reckoning in a line, or why there is none yet.</summary>
        public static string Text(string realm)
        {
            Sum s = For(realm);
            if (s.Battles < Enough || s.Fallen <= 0)
                return "your realm has won " + (s.Battles == 0 ? "no battle" : s.Battles == 1 ? "1 battle" : s.Battles + " battles") + " on record yet, too few to reckon what one brings in";
            return "by your realm's " + s.Battles + " won battles on record, each 100 enemy men fallen left about " + global::BannerlordAgents.Text.N((int)Math.Round(100 * s.LootPerFallen))
                   + " in loot and " + global::BannerlordAgents.Text.N((int)Math.Round(100 * s.PrisonersPerFallen)) + " prisoners, a prisoner worth about "
                   + global::BannerlordAgents.Text.N((int)Math.Round(s.RansomPerPrisoner)) + " in ransom";
        }
    }

    /// <summary>The realm's purse in words, for the report (YOUR PURSE), the council's briefing and the look-ups (0.9.4). Worker only.</summary>
    internal static class PurseView
    {
        /// <summary>At his wage cap: no room left for even a cheap recruit's wage (the game keeps 5 of the cap back).</summary>
        public static bool AtCap(PartyRow r) => r.WageCap >= 0 && r.WageCap - r.Wages - 5 < 2;

        public static string CapWords(PartyRow r) => r.WageCap < 0 ? "no cap" : "cap " + Text.N(r.WageCap) + (r.CapOurs ? " (yours)" : " (the game's)");

        /// <summary>How close to the floor a lord's gold shows beside him: this many times the floor (0.15).</summary>
        private const double NearFloor = 1.2;

        /// <summary>
        /// A lord's gold, when it is near the floor below which the game keeps him from recruiting (0.15; run A: 69 recruit orders ended for money, and a lord's gold was not in
        /// the report for 59 of the 67 that could be checked); null when he has room.
        /// </summary>
        public static string PoorText(PartyRow r)
        {
            if (r.Gold < 0 || r.RecruitFloor <= 0 || r.Gold >= r.RecruitFloor * NearFloor) return null;
            return r.Gold <= r.RecruitFloor
                ? "gold " + Text.N(r.Gold) + ", too little to recruit (the game keeps him from recruiting below " + Text.N(r.RecruitFloor) + ")"
                : "gold " + Text.N(r.Gold) + ", close to the " + Text.N(r.RecruitFloor) + " below which the game keeps him from recruiting";
        }

        /// <summary>How close to his cap a lord's wages show beside him: within this share of the cap (0.16).</summary>
        private const double NearCap = 0.15;

        /// <summary>
        /// A lord's wages beside him, when they are at their cap or near it (0.16; test A1: 47 of Dol Guldur's 64 recruit orders ended at the cap
        /// or the gold floor, and the cap showed only in YOUR PURSE's list, cut at 16 lords); null when he has room.
        /// </summary>
        public static string CapText(PartyRow r)
        {
            if (r.Gold < 0 || r.WageCap < 0) return null;
            string whose = r.CapOurs ? " (yours)" : " (the game's)";
            if (AtCap(r)) return "wages " + Text.N(r.Wages) + " a day of his " + Text.N(r.WageCap) + " cap" + whose + ": at his cap, so he cannot recruit";
            int room = r.WageCap - r.Wages - 5;
            if (room >= r.WageCap * NearCap) return null;
            return "wages " + Text.N(r.Wages) + " a day of his " + Text.N(r.WageCap) + " cap" + whose + ": room for " + Text.N(Math.Max(0, room)) + " a day of new men's wages";
        }

        /// <summary>What may keep a lord from recruiting, beside him in the report's lists (0.15 his gold, 0.16 his wages too); null when nothing does.</summary>
        public static string RecruitNote(PartyRow r)
        {
            string gold = PoorText(r), cap = CapText(r);
            return gold == null ? cap : cap == null ? gold : gold + "; " + cap;
        }

        /// <summary>One lord's purse, for his look-up and the council.</summary>
        public static string LordLine(PartyRow r)
        {
            if (r.Gold < 0) return null;
            var parts = new List<string> { "gold " + Text.N(r.Gold), "wages " + Text.N(r.Wages) + " a day, " + CapWords(r) + (AtCap(r) ? ": at his cap, so he cannot recruit" : "") };
            if (r.LootPieces > 0) parts.Add("loot worth about " + Text.N(r.Loot) + " (" + Text.N(r.LootPieces) + " pieces)");
            if (r.Prisoners + r.PrisonerLords > 0)
                parts.Add(Text.N(r.Prisoners) + " prisoners" + (r.PrisonerLords > 0 ? " and " + r.PrisonerLords + (r.PrisonerLords == 1 ? " lord" : " lords") : "") + ", about " + Text.N(r.Ransom) + " in ransom");
            return string.Join("; ", parts);
        }

        /// <summary>The report's YOUR PURSE: clans' gold, who is at his wage cap, the caps set, loot and prisoners carried, the battles' takings.</summary>
        public static List<string> Lines(Snapshot s, string fid, int most = 8)
        {
            var lines = new List<string>();
            List<PartyRow> lords = s.Parties.Where(r => r.Faction == fid && r.Kind == "lord" && r.Id != s.PlayerParty && r.Gold >= 0).ToList();
            List<ClanRow> clans = s.Clans.Where(c => c.Gold >= 0).OrderByDescending(c => c.Gold).ToList();
            if (clans.Count > 0)
                lines.Add("Clans' gold (their leaders' purses, which top their lords up), with the game's reckoning of its change a day: "
                          + string.Join("; ", clans.Select(c => c.Name + (c.Ruler ? " (yours)" : "") + " " + Text.N(c.Gold) + (c.Daily != int.MinValue ? " (" + (c.Daily >= 0 ? "+" : "") + Text.N(c.Daily) + ")" : ""))) + ".");
            List<PartyRow> capped = lords.Where(AtCap).OrderBy(r => r.SizeLimit > 0 ? (double)r.Men / r.SizeLimit : 1).ToList();
            lines.Add(capped.Count == 0 ? "No lord is at his wage cap."
                      : "At their wage cap, so they cannot recruit (" + capped.Count + "): " + string.Join("; ", capped.Take(most * 2).Select(r => Name(r) + " wages " + Text.N(r.Wages) + " of " + Text.N(r.WageCap)
                                                                                                                                        + (r.CapOurs ? " (yours)" : "") + Ranks(r)))
                        + (capped.Count > most * 2 ? "; and " + (capped.Count - most * 2) + " more" : "") + ".");
            List<PartyRow> ours = lords.Where(r => r.CapOurs).ToList();
            if (ours.Count > 0) lines.Add("Caps you set: " + string.Join("; ", ours.Select(r => Name(r) + " " + (r.WageCap < 0 ? "no cap" : Text.N(r.WageCap)) + " (wages " + Text.N(r.Wages) + ")")) + ".");
            List<PartyRow> loot = lords.Where(r => r.LootPieces > 0).OrderByDescending(r => r.Loot).ToList();
            if (loot.Count > 0)
                lines.Add("Carrying loot (" + loot.Count + " lords, about " + Text.N(loot.Sum(r => r.Loot)) + " in all): " + string.Join("; ", loot.Take(most).Select(r => Name(r) + " about " + Text.N(r.Loot)))
                          + (loot.Count > most ? "; and " + (loot.Count - most) + " more" : "") + ".");
            List<PartyRow> held = lords.Where(r => r.Prisoners + r.PrisonerLords > 0).OrderByDescending(r => r.Ransom).ToList();
            if (held.Count > 0)
                lines.Add("Holding prisoners (" + held.Count + " lords, " + Text.N(held.Sum(r => r.Prisoners)) + " men and " + held.Sum(r => r.PrisonerLords) + " lords, about " + Text.N(held.Sum(r => r.Ransom)) + " in ransom): "
                          + string.Join("; ", held.Take(most).Select(r => Name(r) + " " + Text.N(r.Prisoners + r.PrisonerLords) + ", about " + Text.N(r.Ransom))) + (held.Count > most ? "; and " + (held.Count - most) + " more" : "") + ".");
            lines.Add("Battles: " + Takings.Text(fid) + ".");
            return lines;
        }

        private static string Name(PartyRow r) => (r.Name ?? r.Id) + " [" + (r.Leader ?? r.Id) + "]";
        private static string Ranks(PartyRow r) => r.SizeLimit > 0 ? ", ranks " + Math.Round(100.0 * r.Men / r.SizeLimit) + "%" : "";
    }
}
