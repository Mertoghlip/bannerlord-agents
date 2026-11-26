using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// War room 0.22.0 (Nov 26, after the tooling audit of run 20261125-2341-C2): what every call of the agent's goes through.
    /// - The busy mark (item 14): while a call runs, %LOCALAPPDATA%\BannerlordAgents\autopilot\busy\(realm).json says so ({"pid", "since", "kind"}), so the autopilot
    ///   waits for the war room to be between calls before a memory restart (at restart 2 a review's answer came 12 s after the game had saved and quit, and was set aside).
    /// - The run-finished mark (item 36): while the game's (channel)\run-finished.json is there, no call of any kind is made (the run's end was followed by an opening
    ///   whose seven orders the game never read).
    /// - The run's tag (item 40): a call is tagged with the run of its poke, else the run the game has open (runs\current.txt), so the archive and the costs hold the
    ///   chronicler's calls and the run's first calls (156 calls and $34.56 were missing).
    /// </summary>
    internal sealed partial class Room
    {
        /// <summary>For the self-test: the folder that stands for %LOCALAPPDATA%\BannerlordAgents (null: the real one, or BANNERLORD_AGENTS_ROOT).</summary>
        internal static string RootOverride;

        private static string Root => RootOverride ?? RouteTool.DefaultRoot();

        /// <summary>The busy marks' folder (the autopilot's, docs/AUTOPILOT.md).</summary>
        internal static string BusyDir => Path.Combine(Root, "autopilot", "busy");

        private readonly object _busyGate = new object();
        private int _busyCount;
        private string _busyFile;
        private readonly List<string> _busyKinds = new List<string>();

        /// <summary>The kind of call, in the busy mark's words: wake, review, handover, lords or chronicle.</summary>
        internal static string BusyKind(string kind)
        {
            string k = (kind ?? "").ToLowerInvariant();
            if (k.Contains("lords")) return "lords";
            if (k.StartsWith("handover", StringComparison.Ordinal)) return "handover";
            if (k.StartsWith("review", StringComparison.Ordinal) || k.StartsWith("council", StringComparison.Ordinal)) return "review";
            if (k.StartsWith("chronicle", StringComparison.Ordinal) || k.StartsWith("story", StringComparison.Ordinal)) return "chronicle";
            return "wake";
        }

        /// <summary>The realm this war room answers for (its poke.json's), for the busy mark's name.</summary>
        private string BusyRealm()
        {
            string realm = ReadMeta(-1)?.Realm;
            return string.IsNullOrWhiteSpace(realm) ? "unknown" : realm.Trim();
        }

        /// <summary>A call begins: the busy mark is written (or rewritten with this call's kind while another still runs, the chronicler beside a wake).</summary>
        internal void BusyEnter(string kind)
        {
            lock (_busyGate)
            {
                _busyCount++;
                _busyKinds.Add(BusyKind(kind));
                WriteBusy();
            }
            // 0.22.1: a new call (the chronicler runs beside one, so not its) may wait for a fresh snapshot again
            if (BusyKind(kind) != "chronicle") _freshMissed = false;
        }

        /// <summary>A call ended (also on an error): the mark goes when no other call runs.</summary>
        internal void BusyExit(string kind)
        {
            lock (_busyGate)
            {
                _busyCount = Math.Max(0, _busyCount - 1);
                _busyKinds.Remove(BusyKind(kind));
                if (_busyCount > 0) { WriteBusy(); return; }
                try { if (_busyFile != null && File.Exists(_busyFile)) File.Delete(_busyFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void WriteBusy()
        {
            try
            {
                Directory.CreateDirectory(BusyDir);
                _busyFile = Path.Combine(BusyDir, Safe(BusyRealm()) + ".json");
                string kind = _busyKinds.Count > 0 ? _busyKinds[_busyKinds.Count - 1] : "wake";
                string text = new JsonObject { ["pid"] = Environment.ProcessId, ["since"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture), ["kind"] = kind }.ToJsonString();
                string tmp = _busyFile + ".part";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                File.Move(tmp, _busyFile, true);
            }
            catch (IOException e) { Say("The busy mark could not be written (" + e.Message + ")."); }
            catch (UnauthorizedAccessException e) { Say("The busy mark could not be written (" + e.Message + ")."); }
        }

        private static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ---------- the run-finished mark ----------

        /// <summary>The game's word that its test run has ended (contract b): while this file is in the channel, no call of any kind.</summary>
        internal const string RunFinishedFile = "run-finished.json";
        private bool _runFinishedSaid;

        /// <summary>The run has finished: no new call. Said once in the log each time the mark appears.</summary>
        internal bool RunFinished()
        {
            bool there;
            try { there = File.Exists(Path.Combine(_c.ChannelDir, RunFinishedFile)); }
            catch (IOException) { there = false; }
            if (there && !_runFinishedSaid) { Say("run finished: no more calls"); _runFinishedSaid = true; }
            else if (!there && _runFinishedSaid) { Say("The run-finished mark is gone: calls may be made again."); _runFinishedSaid = false; }
            return there;
        }

        /// <summary>
        /// Every call of the agent's and its chronicler's goes through here: none while the run is finished, and the busy mark held from just before it starts until it ends.
        /// </summary>
        private CallResult Guarded(string kind, Func<CallResult> call)
        {
            if (RunFinished()) return new CallResult { Error = "the run has finished: no more calls" };
            BusyEnter(kind);
            try { return call(); }
            finally { BusyExit(kind); }
        }

        // ---------- what the battle questions read beside the view ----------

        /// <summary>Where the realm's measured enemy edge is kept: the campaign's memory folder (war room 0.22.0, item 9); null before there is a memory.</summary>
        internal string EdgeFile() => _mem?.Dir == null ? null : Path.Combine(_mem.Dir, "enemy-edge.json");

        private static readonly System.Text.RegularExpressions.Regex WakeLag = new System.Text.RegularExpressions.Regex(@"Wake answers:\s*about (\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex OldLag = new System.Text.RegularExpressions.Regex(@"take effect about (\d+(?:\.\d+)?) in-game hours", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// The usual order delay, in in-game hours, as the game measures wake answers (its clock line: "Wake answers: about 8 hours", or the older "take effect about 8 in-game hours");
        /// NaN when the game has not said. The battle question adds it for a lord whose order does not yet take him to the fight (item 4).
        /// </summary>
        internal double UsualLag()
        {
            string line = _shelf?.ClockLine;
            if (string.IsNullOrEmpty(line)) line = Section(ReadShared(Path.Combine(_c.ChannelDir, "poke.txt")) ?? "", "THE CLOCK");
            System.Text.RegularExpressions.Match m = WakeLag.Match(line ?? "");
            if (!m.Success) m = OldLag.Match(line ?? "");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) ? h : double.NaN;
        }

        // ---------- the run's tag ----------

        /// <summary>
        /// The run a call belongs to: its poke's, else the run the game has open (runs\current.txt and its run.json, armed or running, of this campaign). The run's first
        /// look comes before the run begins (it begins at the first in-game hour after the load), so its poke carries no tag.
        /// </summary>
        internal string RunTag(PokeMeta meta, out string arm)
        {
            arm = meta?.Arm;
            if (!string.IsNullOrEmpty(meta?.Run)) return meta.Run;
            if (!string.IsNullOrEmpty(_mem?.Run)) { arm = _mem.Arm; return _mem.Run; }
            try
            {
                string runs = Path.Combine(Root, "runs"), current = Path.Combine(runs, "current.txt");
                if (!File.Exists(current)) return null;
                string id = (ReadShared(current) ?? "").Trim();
                if (id.Length == 0) return null;
                string runFile = Path.Combine(runs, id, "run.json");
                if (!(JsonNode.Parse(ReadShared(runFile) ?? "{}") is JsonObject run)) return null;
                string status = (string)run["status"] ?? "";
                string campaign = (string)run["campaign"];
                string want = meta?.Campaign ?? _mem?.Campaign;
                if (status != "running" && status != "armed") return null;
                if (!string.IsNullOrEmpty(want) && !string.IsNullOrEmpty(campaign) && campaign != want) return null;
                arm = (string)run["arm"] ?? arm;
                return (string)run["id"] ?? id;
            }
            catch (Exception e) when (e is IOException || e is System.Text.Json.JsonException || e is InvalidOperationException || e is UnauthorizedAccessException) { return null; }
        }
    }
}
