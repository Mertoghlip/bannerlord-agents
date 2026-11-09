using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents.Tools;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BannerlordAgents
{
    /// <summary>
    /// An intervention by hand at the next load (0.17.4, Nov 8: test A1's second start found the standard defend ending at its food step in
    /// every realm; I asked for the fixed defend to be given again for all realms, early in the test). A file intervention-at-next-load.txt in
    /// the campaign's data folder, written at my request, is read once, at a load:
    /// - "for save: NAME" (first line, optional): it waits until that save is loaded;
    /// - "upgrade: defend[, tool, ...]": every carried run of those tools of ours takes the library's version now (Runner.UpgradeAtLoad),
    ///   keeping its state, settings and memory; each realm's first look says so in the order's own terms (CarryOn);
    /// - "order: words": an order given by hand, as agents.order takes it, a second after the first frame back on the map, and only to a lord
    ///   who holds no order then (one given meanwhile stands);
    /// - lines starting with # are notes.
    /// The file is then kept as intervention.done-(time).txt, and the run gets a mark (Run.Intervened). Nothing else is ever done by hand.
    /// Game's thread.
    /// </summary>
    internal static class Intervention
    {
        public const string FileName = "intervention-at-next-load.txt";
        /// <summary>Frames on the map before the orders by hand (about a second, so the orders carried on from the save are in force).</summary>
        private const int WaitFrames = 60;
        private static readonly List<string> Words = new List<string>();
        private static readonly List<string> Upgrades = new List<string>();
        private static bool _waiting;
        private static int _frames;

        public static bool Waiting => _waiting;

        /// <summary>At a load, before the save's orders are carried on (BridgeBehavior): reads the file, sets the upgrades, keeps the orders for the map.</summary>
        public static void AtLoad(string dataDir, string save)
        {
            Runner.UpgradeAtLoad.Clear();
            Words.Clear();
            Upgrades.Clear();
            _waiting = false;
            _frames = 0;
            try
            {
                string file = Path.Combine(dataDir, FileName);
                if (!File.Exists(file)) return;
                List<string> lines = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
                if (lines.Count > 0 && lines[0].StartsWith("for save:", StringComparison.OrdinalIgnoreCase))
                {
                    string want = lines[0].Substring("for save:".Length).Trim();
                    if (!string.Equals(want, save, StringComparison.OrdinalIgnoreCase))
                    {
                        Diag.Event("intervention", "waits for its save", new { want, loaded = save });
                        return;
                    }
                    lines.RemoveAt(0);
                }
                foreach (string l in lines)
                {
                    if (l.StartsWith("upgrade:", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string n in l.Substring("upgrade:".Length).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                        {
                            if (Library.Get(n) == null) { Diag.Event("intervention", "no such order of ours", new { name = n }); continue; }
                            Runner.UpgradeAtLoad.Add(n);
                            Upgrades.Add(n);
                        }
                    }
                    else if (l.StartsWith("order:", StringComparison.OrdinalIgnoreCase)) Words.Add(l.Substring("order:".Length).Trim());
                    else Diag.Event("intervention", "line not understood", new { line = l });
                }
                File.Move(file, Path.Combine(dataDir, "intervention.done-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", Text.Inv) + ".txt"));
                Diag.Event("intervention", "read at load", new { save, upgrades = Upgrades, orders = Words });
                Log.Line("intervention by hand at this load: " + (Upgrades.Count > 0 ? "carried runs of " + string.Join(", ", Upgrades) + " take the library's version; " : "")
                         + Words.Count + " order(s) by hand to come");
                _waiting = true;
            }
            catch (Exception e) { Diag.Error("reading the intervention left for this load", e); }
        }

        /// <summary>Each frame on the map after a load while waiting (LieutenantWindow.Frame): the orders by hand, then the run's mark.</summary>
        public static void AfterLoad()
        {
            if (!_waiting || ++_frames < WaitFrames) return;
            _waiting = false;
            var done = new List<string>();
            foreach (string words in Words)
            {
                List<string> args = words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                string result;
                try
                {
                    MobileParty p = args.Count > 0 ? Hero.Find(args[0])?.PartyBelongedTo : null;
                    if (p != null && Orders.Holds(p)) result = "passed over: the lord holds an order now";
                    else result = Orders.Give(args);
                }
                catch (Exception e) { result = "failed: " + e.Message; Diag.Error("an order by hand at the load", e); }
                Diag.Event("intervention", "order by hand", new { words, result });
                Log.Line("intervention: " + words + " -> " + result);
                done.Add(words + " (" + FirstLine(result) + ")");
            }
            string text = "By hand at the load" + (Upgrades.Count > 0 ? ": carried " + string.Join(", ", Upgrades) + " orders took the newer version" : "")
                          + (done.Count > 0 ? (Upgrades.Count > 0 ? "; " : ": ") + done.Count + " order(s) given: " + string.Join("; ", done) : "");
            try { Run.Intervened(text); }
            catch (Exception e) { Diag.Error("marking the run's intervention", e); }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }
    }
}
