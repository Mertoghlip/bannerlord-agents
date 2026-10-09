using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// WarRoom.exe [run] [--dir agent-folder]        answers every poke as it comes (the live loop)
    /// WarRoom.exe once [--dir agent-folder]          answers the newest poke now, and stops
    /// WarRoom.exe replay --pokes file.jsonl --numbers 6,10,54 [--effort e] [--cap tokens] [--model m] [--tag t]
    ///                                                 answers recorded pokes again, without writing orders, to time them
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Native.Self();
            string mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "run";
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
                        var m = System.Text.RegularExpressions.Regex.Match(text, @"^POKE #(\d+), (.*)$", System.Text.RegularExpressions.RegexOptions.Multiline);
                        if (!m.Success) { Console.WriteLine("poke.txt has no number"); return 1; }
                        return room.Answer(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), text).Ok ? 0 : 1;
                    }
                case "replay": return Replay(c, args);
                default:
                    Console.WriteLine("Usage: WarRoom [run|once|replay] [--dir agent-folder]; replay --pokes file.jsonl --numbers 6,10 [--effort e] [--model m] [--tag t]");
                    return 1;
            }
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
            Directory.CreateDirectory(Path.Combine(scratch, live.Channel));
            // the live agent's instructions and order library, an empty journal, no look-ups
            if (File.Exists(live.SystemTemplate)) File.Copy(live.SystemTemplate, Path.Combine(scratch, "warroom-system.md"), true);
            string tools = Path.Combine(live.ChannelDir, "tools.txt");
            if (File.Exists(tools)) File.Copy(tools, Path.Combine(scratch, live.Channel, "tools.txt"), true);
            File.Delete(Path.Combine(scratch, "journal.md"));
            var c = new Config
            {
                AgentDir = scratch, Channel = live.Channel, Model = Arg(args, "--model") ?? live.Model, Effort = Arg(args, "--effort") ?? live.Effort,
                UrgentEffort = Arg(args, "--effort") ?? live.UrgentEffort, Urgent = live.Urgent, TimeoutSeconds = live.TimeoutSeconds, ClaudePath = live.ClaudePath, Lookups = false,
                ThinkingCap = live.ThinkingCap, UrgentThinkingCap = live.UrgentThinkingCap
            };
            var room = new Room(c);
            int? cap = Arg(args, "--cap") != null ? int.Parse(Arg(args, "--cap"), CultureInfo.InvariantCulture) : (int?)null;
            string tag = Arg(args, "--tag") ?? ("replay-" + c.Model + "-" + c.Effort + (cap.HasValue ? "-cap" + cap : ""));
            var rows = new List<string>();
            foreach (string line in File.ReadLines(file))
            {
                if (line.Length < 20) continue;
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement r = doc.RootElement;
                if (!r.TryGetProperty("number", out JsonElement num) || !want.Contains(num.GetInt32())) continue;
                int n = num.GetInt32();
                string date = r.TryGetProperty("date", out JsonElement d) ? d.GetString() : "";
                string report = r.GetProperty("report").GetString();
                string text = "POKE #" + n + ", " + date + "\nAnswer with one JSON object that says \"poke\": " + n + ".\nYour orders right now: REPLAY. Nothing is carried out.\n\n" + report;
                CallResult res = room.Answer(n, date, text, false, null, tag, cap);
                rows.Add("#" + n + "\t" + c.Model + "\t" + c.Effort + "\t" + res.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s\t" + res.OutTokens + " out\t" + res.Thinking + " thinking\t"
                         + res.Turns + " steps\t" + (res.Ok ? "ok" : "failed: " + res.Error));
            }
            Console.WriteLine();
            Console.WriteLine("poke\tmodel\teffort\ttime\twritten\tthinking\tsteps\tresult");
            foreach (string row in rows) Console.WriteLine(row);
            return 0;
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
