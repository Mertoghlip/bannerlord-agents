using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// WarRoom.exe [run] [--dir agent-folder]        answers every poke as it comes (the live loop)
    /// WarRoom.exe once [--dir agent-folder]          answers the newest poke now, and stops
    /// WarRoom.exe story [--dir agent-folder]         has the chronicler rewrite the story so far now, from the channel's chronicle
    /// WarRoom.exe council [--dir agent-folder]       a council of war sits now (the game must be running), and stops
    /// WarRoom.exe selftest                           checks the realm's memory in a scratch folder, without calling Claude
    /// WarRoom.exe replay --pokes file.jsonl --numbers 6,10,54 [--effort e] [--cap tokens] [--model m] [--tag t]
    ///                                                 answers recorded pokes again, each on its own, without memory, to time them
    /// WarRoom.exe replay --pokes file.jsonl --memory written|conversation [--numbers ...|--from a --to b] [--model m] [--tag t]
    ///                                                 answers recorded pokes in order with the realm's memory carried along,
    ///                                                 to compare the two memories (docs/MEMORY.md)
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Native.Self();
            string mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "run";
            if (mode == "selftest") return SelfTest.Run();   // its own scratch folder: the live agent folder is never read or written
            string dir = Arg(args, "--dir");
            Config c = Config.Load(dir);
            switch (mode)
            {
                case "run":
                    {
                        var room = new Room(c);
                        using var cts = new CancellationTokenSource();
                        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
                        return room.Run(cts.Token);
                    }
                case "once":
                    {
                        var room = new Room(c);
                        string poke = Path.Combine(c.ChannelDir, "poke.txt");
                        if (!File.Exists(poke)) { Console.WriteLine("No poke in " + c.ChannelDir); return 1; }
                        string text = File.ReadAllText(poke);
                        var m = Regex.Match(text, @"^POKE #(\d+), (.*)$", RegexOptions.Multiline);
                        if (!m.Success) { Console.WriteLine("poke.txt has no number"); return 1; }
                        return room.Answer(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), text).Ok ? 0 : 1;
                    }
                case "story": return Story(c);
                case "council": return new Room(c).CouncilNow("asked for by hand");
                case "replay": return Arg(args, "--memory") != null ? ReplayInOrder(c, args) : Replay(c, args);
                default:
                    Console.WriteLine("Usage: WarRoom [run|once|story|council|replay] [--dir agent-folder]; replay --pokes file.jsonl --numbers 6,10 [--memory written|conversation] [--effort e] [--model m] [--tag t]");
                    return 1;
            }
        }

        /// <summary>The story so far, rewritten now from every chronicle line it does not cover yet (for a first story, or a test).</summary>
        private static int Story(Config c)
        {
            var room = new Room(c);
            string meta = Room.ReadShared(Path.Combine(c.ChannelDir, "poke.json"));
            string campaign = null;
            try { if (meta != null && JsonNode.Parse(meta) is JsonObject o) campaign = (string)o["campaign"]; }
            catch (JsonException) { }
            if (campaign == null) { Console.WriteLine("No poke.json in " + c.ChannelDir + " yet: load the campaign first."); return 1; }
            Memory m = Memory.Open(c, campaign, room.Say);
            m.View(out _, out double storyHours, out _, out _, out _);
            List<ChronicleLine> fresh = Memory.ReadChronicle(c.ChannelDir).Where(l => double.IsNaN(storyHours) || l.Hours > storyHours + 1e-6).ToList();
            if (fresh.Count == 0) { Console.WriteLine("The story so far already covers the whole chronicle."); return 0; }
            return room.WriteStory(m, fresh.Take(400).ToList()) ? 0 : 1;
        }

        /// <summary>
        /// Recorded pokes, answered again in a scratch folder (no journal, no orders written), to measure answer time and
        /// tokens at a given model and effort. The answers are kept in the war room's call logs for reading side by side.
        /// </summary>
        private static int Replay(Config live, string[] args)
        {
            string file = Arg(args, "--pokes");
            string numbers = Arg(args, "--numbers");
            if (file == null || numbers == null || !File.Exists(file)) { Console.WriteLine("replay needs --pokes <file.jsonl> and --numbers 6,10,54"); return 1; }
            var want = new HashSet<int>(numbers.Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)));
            string scratch = Path.Combine(Path.GetTempPath(), "warroom-replay");
            Config c = Scratch(live, scratch, args, "written");
            var room = new Room(c);
            int? cap = Arg(args, "--cap") != null ? int.Parse(Arg(args, "--cap"), CultureInfo.InvariantCulture) : (int?)null;
            string tag = Arg(args, "--tag") ?? ("replay-" + c.Model + "-" + c.Effort + (cap.HasValue ? "-cap" + cap : ""));
            var rows = new List<string>();
            foreach (Recorded p in ReadPokes(file).Where(p => want.Contains(p.Number)))
            {
                CallResult res = room.Answer(p.Number, p.Date, PokeText(p), false, null, tag, cap);
                rows.Add("#" + p.Number + "\t" + c.Model + "\t" + c.Effort + "\t" + res.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s\t" + res.OutTokens + " out\t" + res.Thinking + " thinking\t"
                         + res.Turns + " steps\t" + (res.Ok ? "ok" : "failed: " + res.Error));
            }
            Console.WriteLine();
            Console.WriteLine("poke\tmodel\teffort\ttime\twritten\tthinking\tsteps\tresult");
            foreach (string row in rows) Console.WriteLine(row);
            return 0;
        }

        /// <summary>
        /// Recorded pokes answered in order, the realm's memory carried from each answer to the next, in a scratch folder of
        /// its own (nothing reaches the game). The chronicle grows from the recorded pokes' reasons, the fights the bridge now
        /// keeps quiet left out, and from the replay's own orders (nothing is carried out, so they have no outcomes). With the
        /// written memory, the chronicler rewrites the story whenever it is due, between pokes; with the conversation, every
        /// poke goes into one conversation. Prints each call's time and size, and where the answers are, to compare.
        /// </summary>
        private static int ReplayInOrder(Config live, string[] args)
        {
            string file = Arg(args, "--pokes");
            string memory = Arg(args, "--memory");
            if (file == null || !File.Exists(file) || (memory != "written" && memory != "conversation"))
            {
                Console.WriteLine("replay in order needs --pokes <file.jsonl> and --memory written|conversation, with --numbers 1,6,10 or --from 1 --to 40");
                return 1;
            }
            List<Recorded> all = ReadPokes(file);
            HashSet<int> want;
            if (Arg(args, "--numbers") != null) want = new HashSet<int>(Arg(args, "--numbers").Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)));
            else
            {
                int from = int.Parse(Arg(args, "--from") ?? "1", CultureInfo.InvariantCulture), to = int.Parse(Arg(args, "--to") ?? "100000", CultureInfo.InvariantCulture);
                // the pokes the bridge would still send: not the ones made only of small fights (version 0.3.1)
                want = new HashSet<int>(all.Where(p => p.Number >= from && p.Number <= to && p.Triggers.Any(t => !Quiet(t))).Select(p => p.Number));
            }
            string tag = Arg(args, "--tag") ?? ("order-" + memory + "-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture));
            string scratch = Path.Combine(Path.GetTempPath(), "warroom-replay-" + tag);
            Config c = Scratch(live, scratch, args, memory);
            var room = new Room(c);
            string channel = c.ChannelDir;
            var chronicle = new List<string>();
            var rows = new List<string>();
            var times = new List<double>();
            long tokensIn = 0, tokensOut = 0;
            int stories = 0;
            Console.WriteLine("Replaying " + want.Count + " pokes in order, memory " + memory + ", in " + scratch);
            foreach (Recorded p in all)
            {
                foreach (Trigger t in p.Triggers.Where(t => !Quiet(t)))
                    chronicle.Add(Line(t.Hours, t.Kind, t.Text));
                if (!want.Contains(p.Number)) continue;
                File.WriteAllText(Path.Combine(channel, "chronicle.jsonl"), string.Concat(chronicle.Select(l => l + "\n")), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(channel, "poke.json"), new JsonObject
                {
                    ["number"] = p.Number, ["hours"] = p.Hours, ["date"] = When(p.Hours), ["campaign"] = "replay", ["realm"] = p.Realm, ["realm_name"] = p.RealmName, ["load_id"] = "replay"
                }.ToJsonString(), new UTF8Encoding(false));
                CallResult r = room.Answer(p.Number, When(p.Hours), PokeText(p), true, null, tag);
                times.Add(r.Seconds);
                tokensIn += r.InTokens + r.CacheRead + r.CacheWrite;
                tokensOut += r.OutTokens;
                // its orders become chronicle lines, at the poke's hour
                foreach (string o in Orders(Path.Combine(channel, "orders", "poke-" + p.Number + ".json"))) chronicle.Add(Line(p.Hours, "order", o));
                File.WriteAllText(Path.Combine(channel, "chronicle.jsonl"), string.Concat(chronicle.Select(l => l + "\n")), new UTF8Encoding(false));
                if (memory == "written")
                {
                    List<ChronicleLine> due = room.DueStory(p.Hours);
                    if (due != null && room.WriteStory(room.Mem, due, default, tag)) stories++;
                }
                rows.Add("#" + p.Number + "\t" + r.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s\t" + (r.InTokens + r.CacheRead + r.CacheWrite).ToString("N0", CultureInfo.InvariantCulture)
                         + " read\t" + r.OutTokens.ToString("N0", CultureInfo.InvariantCulture) + " written\t" + r.Thinking.ToString("N0", CultureInfo.InvariantCulture) + " thinking\t" + (r.Ok ? "ok" : "failed: " + r.Error));
            }
            Console.WriteLine();
            Console.WriteLine("poke\ttime\tread\twritten\tthinking\tresult");
            foreach (string row in rows) Console.WriteLine(row);
            times.Sort();
            Console.WriteLine();
            Console.WriteLine("memory " + memory + ": " + times.Count + " answers, median " + (times.Count > 0 ? times[times.Count / 2].ToString("0.0", CultureInfo.InvariantCulture) : "-") + " s; "
                              + tokensIn.ToString("N0", CultureInfo.InvariantCulture) + " tokens read, " + tokensOut.ToString("N0", CultureInfo.InvariantCulture) + " written"
                              + (memory == "written" ? "; the story rewritten " + stories + " times" : "") + ".");
            Console.WriteLine("The notes, the story and the answers: " + scratch + " and the war room's call logs (tag " + tag + ").");
            return 0;
        }

        /// <summary>A scratch agent folder: the live instructions, chronicler and order library, no journal, no look-ups.</summary>
        private static Config Scratch(Config live, string scratch, string[] args, string memory)
        {
            try { Directory.Delete(scratch, true); } catch (DirectoryNotFoundException) { }
            Directory.CreateDirectory(Path.Combine(scratch, live.Channel, "orders"));
            if (File.Exists(live.SystemTemplate)) File.Copy(live.SystemTemplate, Path.Combine(scratch, "warroom-system.md"), true);
            if (File.Exists(live.ChroniclerTemplate)) File.Copy(live.ChroniclerTemplate, Path.Combine(scratch, "chronicler-system.md"), true);
            string tools = Path.Combine(live.ChannelDir, "tools.txt");
            if (File.Exists(tools)) File.Copy(tools, Path.Combine(scratch, live.Channel, "tools.txt"), true);
            var c = new Config
            {
                AgentDir = scratch, Channel = live.Channel, Model = Arg(args, "--model") ?? live.Model, Effort = Arg(args, "--effort") ?? live.Effort,
                UrgentEffort = Arg(args, "--effort") ?? live.UrgentEffort, Urgent = live.Urgent, TimeoutSeconds = live.TimeoutSeconds, ClaudePath = live.ClaudePath, Lookups = false,
                ThinkingCap = live.ThinkingCap, UrgentThinkingCap = live.UrgentThinkingCap, MaxLessons = live.MaxLessons, MemoryMode = memory,
                StoryEvery = live.StoryEvery, StoryWords = live.StoryWords, StoryModel = Arg(args, "--model") ?? live.StoryModel, StoryEffort = live.StoryEffort,
                StoryThinkingCap = live.StoryThinkingCap, ConversationLimit = live.ConversationLimit
            };
            c.Save();
            return c;
        }

        internal sealed class Trigger
        {
            public double Hours;
            public string Kind = "", Text = "";
        }

        private sealed class Recorded
        {
            public int Number;
            public double Hours;
            public string Date = "", Realm = "", RealmName = "", Report = "";
            public List<Trigger> Triggers = new List<Trigger>();
        }

        private static List<Recorded> ReadPokes(string file)
        {
            var list = new List<Recorded>();
            foreach (string line in File.ReadLines(file))
            {
                if (line.Length < 20) continue;
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement r = doc.RootElement;
                if (!r.TryGetProperty("number", out JsonElement num) || !r.TryGetProperty("report", out JsonElement rep)) continue;
                var p = new Recorded
                {
                    Number = num.GetInt32(), Hours = r.TryGetProperty("hours", out JsonElement h) ? h.GetDouble() : 0, Date = r.TryGetProperty("date", out JsonElement d) ? d.GetString() : "",
                    Realm = r.TryGetProperty("faction", out JsonElement f) ? f.GetString() : "", RealmName = r.TryGetProperty("faction_name", out JsonElement fn) ? fn.GetString() : "", Report = rep.GetString()
                };
                if (r.TryGetProperty("triggers", out JsonElement ts) && ts.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement t in ts.EnumerateArray())
                        p.Triggers.Add(new Trigger
                        {
                            Hours = t.TryGetProperty("hours", out JsonElement th) ? th.GetDouble() : p.Hours, Kind = t.TryGetProperty("kind", out JsonElement k) ? k.GetString() : "",
                            Text = t.TryGetProperty("text", out JsonElement tx) ? tx.GetString() : ""
                        });
                list.Add(p);
            }
            return list.OrderBy(p => p.Number).ToList();
        }

        private static string PokeText(Recorded p)
            => "POKE #" + p.Number + ", " + When(p.Hours) + "\nAnswer with one JSON object that says \"poke\": " + p.Number + ".\nYour orders right now: REPLAY. Nothing is carried out.\n\n" + p.Report;

        private static readonly Regex BattleSides = new Regex(@"^Battle(?: at [^:]+)?: (.+?) \((.+?)\) against (.+?) \((.+?)\);", RegexOptions.Compiled);

        /// <summary>
        /// What the bridge now keeps out of pokes and the chronicle, judged from a recorded reason's words: a quiet day, the
        /// first look, a lord sighted (only armies are history), and a fight without lords on both sides away from any
        /// holding: caravans, villagers, bandits (version 0.3.1).
        /// </summary>
        internal static bool Quiet(Trigger t)
        {
            if (t.Kind == "quiet_day" || t.Kind == "first_look") return true;
            if (t.Kind == "enemy_in_view") return !t.Text.StartsWith("Enemy army", StringComparison.Ordinal);
            if (t.Kind != "battle" || t.Text.StartsWith("Assault", StringComparison.Ordinal) || t.Text.StartsWith("Fight over a raid", StringComparison.Ordinal)
                || t.Text.StartsWith("Battle at ", StringComparison.Ordinal)) return false;
            Match m = BattleSides.Match(t.Text);
            if (!m.Success) return false;
            return !Lordly(m.Groups[1].Value, m.Groups[2].Value) || !Lordly(m.Groups[3].Value, m.Groups[4].Value);
        }

        private static bool Lordly(string name, string realm)
            => name != realm && !name.StartsWith("Caravan of ", StringComparison.Ordinal) && !name.StartsWith("Villagers of ", StringComparison.Ordinal)
               && name.IndexOf("Patrol", StringComparison.Ordinal) < 0 && name.IndexOf("Soldiers", StringComparison.Ordinal) < 0;

        private static string Line(double hours, string kind, string text)
            => new JsonObject { ["hours"] = hours, ["date"] = When(hours), ["kind"] = kind, ["text"] = text }.ToJsonString();

        /// <summary>A replay's own orders, as the chronicle would keep them.</summary>
        private static IEnumerable<string> Orders(string path)
        {
            if (!File.Exists(path)) yield break;
            JsonObject o;
            try { o = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
            catch (JsonException) { yield break; }
            if (o?["orders"] is not JsonArray list) yield break;
            foreach (JsonNode n in list)
            {
                if (n is not JsonObject a) continue;
                string where = (string)(a["at"] as JsonValue) ?? (string)(a["target"] as JsonValue) ?? "";
                string why = (string)(a["why"] as JsonValue);
                yield return "You ordered: " + (string)a["lord"] + " " + (string)a["do"] + (where.Length > 0 ? " " + where : "") + "." + (string.IsNullOrWhiteSpace(why) ? "" : " Why: " + why.Trim().TrimEnd('.') + ".");
            }
        }

        /// <summary>The game's date from in-game hours: 24 hours a day, 21 days a season, 4 seasons (TAOM keeps the game's calendar).</summary>
        public static string When(double hours)
        {
            long h = (long)Math.Floor(hours), days = h / 24, year = days / 84;
            int dayOfYear = (int)(days % 84), hour = (int)(h % 24);
            string[] seasons = { "Spring", "Summer", "Autumn", "Winter" };
            return seasons[dayOfYear / 21] + " " + (dayOfYear % 21 + 1) + ", " + year + ", " + hour.ToString("00", CultureInfo.InvariantCulture) + ":00";
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
