using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BannerlordAgents.Tools;

namespace BannerlordAgents
{
    /// <summary>
    /// The test run the game is in right now (0.13, docs/PLAN-0.13.md C3.1): its id and arm, for the tag every record carries,
    /// so a run's records are found by their own words, not by time spans. Set on the game's thread (Run), read by any thread.
    /// Null outside a run.
    /// </summary>
    internal static class RunTags
    {
        private static volatile string _id, _arm;

        public static string Id => _id;
        public static string Arm => _arm;

        public static void Set(string id, string arm)
        {
            _id = id;
            _arm = arm;
        }
    }

    /// <summary>The recorder's rules that need no game (the smoke test reads them; Recorder asks them with the game's facts).</summary>
    internal static class RecordRules
    {
        /// <summary>
        /// Is the record the player's (0.13, docs/PLAN-0.13.md C2)? His own character alive in his clan, and ruling his realm, or a
        /// lord of a realm no live agent of its own commands (or of none). Not a wanderer standing in for him (the C runs).
        /// </summary>
        public static bool Yours(bool alive, bool inClan, bool inKingdom, bool rules, bool isLord, bool liveAgent)
        {
            if (!alive || !inClan) return false;
            if (!inKingdom) return isLord;
            if (rules) return true;
            return isLord && !liveAgent;
        }

        /// <summary>
        /// Party AI Controls' own refill (0.13): a recruit order with no target it sets by itself for a lord below his share of ranks,
        /// with no word from the player just before (a dialogue order follows a conversation with that lord).
        /// </summary>
        public static bool PacRefill(string after, bool hasTarget, bool justTalked) =>
            !justTalked && !hasTarget && after != null && after.StartsWith("RecruitFromTemplate|", StringComparison.Ordinal);
    }

    /// <summary>Small helpers for the training record (0.13, Part C): a text's fingerprint, an hour's key, a packed copy.</summary>
    internal static class RecordFiles
    {
        /// <summary>The first 12 hex digits of a text's SHA-256: enough to tell two reports apart, short enough to keep on every line.</summary>
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
        }

        /// <summary>An in-game hour's key in file names: "h28473" for hour 28,473.</summary>
        public static string HourKey(double hours) => "h" + ((long)Math.Floor(hours + 1e-6)).ToString(Text.Inv);

        /// <summary>Writes a text packed (gzip), whole, swapped in; false when it could not be written.</summary>
        public static bool WriteGz(string path, string text)
        {
            string tmp = path + ".new";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text ?? "");
                    gz.Write(bytes, 0, bytes.Length);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                Diag.Swallowed("writing the packed " + Path.GetFileName(path), e);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception e2) { Diag.Swallowed("removing " + Path.GetFileName(tmp), e2); }
                return false;
            }
        }

        /// <summary>Reads back a packed text (the smoke test, the bundle's check).</summary>
        public static string ReadGz(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var sr = new StreamReader(gz, Encoding.UTF8)) return sr.ReadToEnd();
        }
    }

    /// <summary>
    /// The episode index (0.13, docs/PLAN-0.13.md C3.2, C4): one line per poke, answer, receipt, order's outcome and realm's day, in
    /// the campaign's episodes.jsonl, so a training example can be put together from what was seen (the poke and its report,
    /// named by its number and fingerprint), what was decided (the answer, its call's folder in the war room's logs), what the
    /// game made of it (each order's receipt and the tool run it started) and what came of it (each order's outcome when it ends,
    /// the realm's numbers each day). Every line carries the run and its arm. Worker thread.
    /// </summary>
    internal static class Episodes
    {
        public static int Lines;

        private static string File => Path.Combine(Brain.Dir, "episodes.jsonl");

        private static void Write(object line)
        {
            Files.Append(File, Json.Line(line));
            Lines++;
            Diag.Count("record:episodes");
        }

        private static string Wall() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", Text.Inv);

        /// <summary>A poke the bridge sent: its number, reasons, and its report's fingerprint (the report itself is its line in pokes_(realm).jsonl).</summary>
        public static void Poke(string realm, int number, double hours, List<Trigger> triggers, string report, bool toAgent)
        {
            Write(new
            {
                Type = "poke", Realm = realm, Run = RunTags.Id, Arm = RunTags.Arm, Load = Bridge.LoadId, Wall = Wall(), Hours = hours, Poke = number,
                Triggers = triggers.Select(t => t.Kind).ToList(), Notices = triggers.Where(t => t.Notice.HasValue).Select(t => t.Notice.Value).ToList(),
                ReportHash = RecordFiles.Hash(report), ReportChars = report?.Length ?? 0, Pokes = "pokes_" + realm + ".jsonl", ToAgent = toAgent
            });
        }

        /// <summary>An answer file read: which poke it answers, from which call (the war room names its folder), and what is in it.</summary>
        public static void Answer(string realm, int poke, string file, AgentChannel.Answer ans, double hours)
        {
            Write(new
            {
                Type = "answer", Realm = realm, Run = RunTags.Id, Arm = RunTags.Arm, Load = Bridge.LoadId, Wall = Wall(), Hours = hours, Poke = poke, File = file,
                Kind = ans?.Kind ?? "answer", Call = ans?.Call, Orders = ans?.Orders ?? 0, Tools = ans?.Tools ?? 0, Diplomacy = ans?.Diplomacy ?? 0, Letters = ans?.Letters ?? 0
            });
        }

        /// <summary>What became of one order at the door: applied, checked or refused and why, and the tool run it started.</summary>
        public static void Receipt(string realm, AgentChannel.AgentOrder a, string status, string reason, double hours)
        {
            Write(new
            {
                Type = "receipt", Realm = realm, Run = RunTags.Id, Arm = RunTags.Arm, Wall = Wall(), Hours = hours, a.Poke, a.File, a.Index, a.Lord, a.Do, a.Target, a.At,
                Places = a.Places.Count > 0 ? a.Places.ToList() : null, a.Why, Status = status, Reason = reason, ToolRun = a.ToolRun > 0 ? a.ToolRun : (int?)null
            });
        }

        /// <summary>An order's outcome, at its end (Outcomes).</summary>
        public static void Outcome(object line) => Write(line);

        // ---------- the realm's day (C4) ----------

        private static readonly Dictionary<string, long> LastDay = new Dictionary<string, long>();

        public static void Reset() => LastDay.Clear();

        /// <summary>
        /// The first hour of each in-game day, for each agent's realm: its numbers as they stand (lords, men, strength, holdings,
        /// its lords held prisoner), the line the dataset builder weighs a day's answers by.
        /// </summary>
        public static void MaybeDay(Snapshot v)
        {
            if (v?.Watched == null) return;
            long day = (long)Math.Floor(v.Hours / 24 + 1e-9);
            if (LastDay.TryGetValue(v.Watched, out long last) && last == day) return;
            bool first = !LastDay.ContainsKey(v.Watched);
            LastDay[v.Watched] = day;
            if (first && v.Hours - day * 24 > 1.5) return;   // a load in the middle of a day: the day line comes at the next day's start
            List<PartyRow> lords = v.Parties.Where(r => r.Faction == v.Watched && r.Kind == "lord").ToList();
            List<SettlementRow> own = v.Settlements.Where(h => h.Faction == v.Watched).ToList();
            Write(new
            {
                Type = "day", Realm = v.Watched, Run = RunTags.Id, Arm = RunTags.Arm, Load = v.LoadId, Wall = Wall(), Hours = v.Hours, Date = v.Date, Day = day,
                Lords = lords.Count, Men = lords.Sum(r => r.Men), Wounded = lords.Sum(r => r.Wounded), Strength = (int)Math.Round(lords.Sum(r => r.Strength)),
                Gold = lords.Where(r => r.Gold >= 0).Sum(r => (long)r.Gold), Loot = lords.Sum(r => r.Loot),
                Towns = own.Count(h => h.Kind == "town"), Castles = own.Count(h => h.Kind == "castle"), Villages = own.Count(h => h.Kind == "village"),
                Captives = v.Captives?.Count ?? 0, Influence = (int)Math.Round(v.Clans?.Sum(c => c.Influence) ?? 0),
                Orders = Agents.Of(v.Watched)?.Tools.Count ?? 0
            });
        }
    }

    /// <summary>A lord's numbers at a moment, for an order's outcome.</summary>
    internal sealed class LordNumbers
    {
        public int Men, Wounded, Gold = -1, Loot, Prisoners, FoodDays = -1;
        public float Strength;

        public static LordNumbers Of(PartyRow r) => r == null ? null : new LordNumbers
        {
            Men = r.ArmyMen > 0 ? r.ArmyMen : r.Men, Wounded = r.Wounded, Gold = r.Gold, Loot = r.Loot, Prisoners = r.Prisoners, FoodDays = r.FoodDays,
            Strength = (float)Math.Round(World.StrengthOf(r))
        };
    }

    /// <summary>One battle's end as the game's thread saw it, for the orders' outcomes: each party in it with its side and losses.</summary>
    internal sealed class BattleEnd
    {
        public double Hours;
        public string Kind, Place, Winner;
        public readonly List<BattleParty> Parties = new List<BattleParty>();
    }

    internal sealed class BattleParty
    {
        public string Party, Faction, Side;
        public int Men, Lost;
    }

    /// <summary>
    /// Each tool run's record (0.13, docs/PLAN-0.13.md A7, C3.3, C3.4): who wrote the tool (ours, or the agent at which poke) and its
    /// version, the lord and its settings, its states with their hours, its call-backs, and over its run what changed for its lord
    /// (men, wounded, gold, loot, strength, food), battles fought and won, lords taken and lost, holdings gained and lost at its
    /// places. Each step goes to the campaign's toolbook.jsonl; at the end, the outcome goes there and to the episode index. The
    /// game's thread sends the battles, captures and holdings changing hands. Worker thread.
    /// </summary>
    internal static class Outcomes
    {
        private sealed class Track
        {
            public int Id;
            public string Realm, Tool, Version, Author, Lord, Party, At, Target;
            public List<string> Places = new List<string>(), Lords = new List<string>();
            public Dictionary<string, double> Settings;
            public int Poke;
            public double Started;
            public LordNumbers Start;
            public int Fought, Won, Lost, MenLost, EnemyFallen, LordsTaken, LordsLost, Gained, LostHoldings, CallBacks, Steps;
            public readonly List<object> States = new List<object>();
            public string State;
            public double StateSince;
        }

        private static readonly Dictionary<int, Track> Tracks = new Dictionary<int, Track>();
        public static int Ended;

        private static string Book => Path.Combine(Brain.Dir, "toolbook.jsonl");

        public static void Reset()
        {
            Tracks.Clear();
            Ended = 0;
        }

        private static void Write(object line)
        {
            Files.Append(Book, Json.Line(line));
            Diag.Count("record:toolbook");
        }

        /// <summary>A line of the toolbook from elsewhere (the agents' own tools: written, checked, kept, retired; 0.13 step 3).</summary>
        public static void Note(object line) => Write(line);

        private static string Wall() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", Text.Inv);

        public static void Started(ToolInstance t, PartyRow me, double hours)
        {
            var k = new Track
            {
                Id = t.Id, Realm = t.Owner?.Realm ?? t.Order.Realm, Tool = t.Def.Name, Version = t.Def.VersionText, Author = t.Def.Author, Lord = t.Order.Lord, Party = t.Order.Party,
                At = t.Order.At, Target = t.Order.Target, Poke = t.Order.Poke, Started = hours, Start = LordNumbers.Of(me), Settings = new Dictionary<string, double>(t.Settings)
            };
            k.Places.AddRange(t.Order.Places);
            k.Lords.AddRange(t.Order.Lords);
            Tracks[t.Id] = k;
            Write(new
            {
                Type = "run_start", Realm = k.Realm, Run = RunTags.Id, Arm = RunTags.Arm, Wall = Wall(), Hours = hours, ToolRun = t.Id, k.Tool, k.Version, k.Author, Source = t.Order.Source,
                k.Lord, k.Party, k.At, k.Target, Places = k.Places.Count > 0 ? k.Places : null, Lords = k.Lords.Count > 0 ? k.Lords : null, k.Settings, k.Poke, t.Order.Why, k.Start
            });
        }

        public static void Step(ToolInstance t, string from, string to, string why, double hours)
        {
            if (!Tracks.TryGetValue(t.Id, out Track k)) return;
            k.Steps++;
            if (k.State != null && !string.Equals(k.State, to, StringComparison.OrdinalIgnoreCase))
                k.States.Add(new { State = k.State, From = Math.Round(k.StateSince, 2), Hours = Math.Round(hours - k.StateSince, 2) });
            if (!string.Equals(k.State, to, StringComparison.OrdinalIgnoreCase)) { k.State = to; k.StateSince = hours; }
            Write(new { Type = "step", Realm = k.Realm, Run = RunTags.Id, Arm = RunTags.Arm, Hours = hours, ToolRun = t.Id, k.Tool, k.Version, From = from, To = to, Why = why, Move = t.HasPending ? t.Pending.ToString() : null });
        }

        public static void CallBack(ToolInstance t, string text, double hours)
        {
            if (!Tracks.TryGetValue(t.Id, out Track k)) return;
            k.CallBacks++;
            Write(new { Type = "callback", Realm = k.Realm, Run = RunTags.Id, Arm = RunTags.Arm, Hours = hours, ToolRun = t.Id, k.Tool, k.Version, Text = text });
        }

        /// <summary>A tool run ended (how: finished, failed, stopped, replaced, captured, ...): its outcome, kept whole.</summary>
        public static void End(int id, string how, string why, PartyRow me, double hours)
        {
            if (!Tracks.TryGetValue(id, out Track k)) return;
            Tracks.Remove(id);
            Ended++;
            if (k.State != null) k.States.Add(new { State = k.State, From = Math.Round(k.StateSince, 2), Hours = Math.Round(hours - k.StateSince, 2) });
            LordNumbers end = LordNumbers.Of(me);
            var outcome = new
            {
                Type = "outcome", Realm = k.Realm, Run = RunTags.Id, Arm = RunTags.Arm, Wall = Wall(), Hours = hours, ToolRun = k.Id, k.Tool, k.Version, k.Author, k.Lord, k.Party, k.At, k.Target,
                Places = k.Places.Count > 0 ? k.Places : null, k.Poke, How = how, Why = why, HoursHeld = Math.Round(hours - k.Started, 2), k.Start, End = end,
                Change = k.Start != null && end != null ? new
                {
                    Men = end.Men - k.Start.Men, Wounded = end.Wounded - k.Start.Wounded, Gold = end.Gold >= 0 && k.Start.Gold >= 0 ? end.Gold - k.Start.Gold : (int?)null,
                    Loot = end.Loot - k.Start.Loot, Strength = (float)Math.Round(end.Strength - k.Start.Strength), Prisoners = end.Prisoners - k.Start.Prisoners
                } : null,
                Battles = new { Fought = k.Fought, Won = k.Won, Lost = k.Lost, MenLost = k.MenLost, EnemyFallen = k.EnemyFallen },
                k.LordsTaken, k.LordsLost, HoldingsGained = k.Gained, HoldingsLost = k.LostHoldings, k.CallBacks, k.Steps, k.States
            };
            Write(new
            {
                Type = "run_end", Realm = k.Realm, Run = RunTags.Id, Arm = RunTags.Arm, Wall = Wall(), Hours = hours, ToolRun = k.Id, k.Tool, k.Version, How = how, Why = why, Outcome = outcome
            });
            Episodes.Outcome(outcome);
        }

        /// <summary>The tool runs still going (the record's check at a session's end).</summary>
        public static int Open => Tracks.Count;

        /// <summary>A battle's end (posted by the game's thread): each tool run whose lord fought in it counts it.</summary>
        public static void OnBattle(BattleEnd b)
        {
            if (Tracks.Count == 0 || b == null) return;
            foreach (Track k in Tracks.Values)
            {
                BattleParty mine = b.Parties.FirstOrDefault(p => p.Party == k.Party);
                if (mine == null) continue;
                k.Fought++;
                if (b.Winner == mine.Side) k.Won++;
                else if (b.Winner != null && b.Winner != "none") k.Lost++;
                k.MenLost += mine.Lost;
                k.EnemyFallen += b.Parties.Where(p => p.Side != mine.Side).Sum(p => p.Lost);
            }
        }

        /// <summary>A lord taken prisoner (posted by the game's thread): the tool run whose party took him counts a lord taken.</summary>
        public static void OnCapture(string hero, string heroFaction, string capturerParty)
        {
            foreach (Track k in Tracks.Values)
            {
                if (k.Party != null && k.Party == capturerParty) k.LordsTaken++;
                if (k.Lord == hero) k.LordsLost++;
            }
        }

        /// <summary>A holding changing hands (posted by the game's thread): the tool runs aimed at it, or at a place within 20 of it, count it.</summary>
        public static void OnHolding(string settlement, string from, string to)
        {
            if (Tracks.Count == 0) return;
            Snapshot s = Brain.Latest;
            SettlementRow h = s != null && s.SettlementById.TryGetValue(settlement, out SettlementRow r) ? r : null;
            foreach (Track k in Tracks.Values)
            {
                bool near = k.At == settlement || k.Target == settlement || k.Places.Contains(settlement);
                if (!near && h != null && s != null)
                    near = new[] { k.At }.Concat(k.Places).Any(p => p != null && s.SettlementById.TryGetValue(p, out SettlementRow o) && Views.Dist(o.X, o.Y, h.X, h.Y) <= 20f);
                if (!near) continue;
                if (to == k.Realm) k.Gained++;
                else if (from == k.Realm) k.LostHoldings++;
            }
        }
    }
}
