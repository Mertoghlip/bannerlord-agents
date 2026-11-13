using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BannerlordAgents.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BannerlordAgents
{
    /// <summary>
    /// The orders in force carried across a reload of the same save (0.13.5, Oct 29: the test logs and the
    /// agents' records must survive the game being closed and reloaded into the same save, so the test continues when I have to step
    /// off the game and come back to it later). Orders live only in memory and every load clears them, so until now a run carried
    /// on from its save with every agent's lords left to the game's own AI until the agent ordered them again, and each tool's
    /// progress (a guard's refill, a siege's storm rules, a circuit's visits) was lost. Now at every save the orders in force are
    /// written down beside the save's history note, never in the save: each tool order with the very definition it runs, its
    /// settings, state and remembered values and its record so far, and each wage cap; one file per save name
    /// (data\(campaign)\orders\(save).json), overwritten when the save is, as the game overwrites the save. A load of that save at
    /// that hour finds it (the rule History uses for the save's note) and carries each order on: the lord held again and checked
    /// as at any order's start (alive, free, leading his party in the realm, obeying its agent), the realm's agent the same folder
    /// and live, the tool in the state it was in, its move given again. The agent's first look says what carried on and what
    /// could not. Game's thread, except the writing and the tools' side (worker).
    /// </summary>
    internal static class CarryOn
    {
        /// <summary>A save's file is its own only at the hour it was made (as History finds a save's note).</summary>
        private const double SameHour = 0.05;

        // worker: what each realm's first look since the load says of its orders carried on
        private static readonly Dictionary<string, string> Told = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string FileFor(string campaignDir, string save) => Path.Combine(campaignDir, "orders", Safe(save) + ".json");

        private static string Safe(string save)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char ch in save.Trim()) sb.Append(bad.Contains(ch) ? '_' : ch);
            return sb.Length > 0 ? sb.ToString() : "_";
        }

        // ---------- at a save ----------

        /// <summary>Game's thread, after a save was written (the orders taken back): the orders in force written down for it.</summary>
        public static void OnSaved(string save)
        {
            if (!Bridge.Running || string.IsNullOrEmpty(save)) return;
            double hours = CampaignTime.Now.ToHours;
            List<JObject> held = Orders.KeepForSave();
            List<JObject> caps = Wages.KeepForSave();
            string file = FileFor(Paths.Data(Bridge.CampaignId), save), load = Bridge.LoadId;
            // the lieutenant's orders agreed to and not yet given (0.19.2): not in force, so not in the save; the ruler is told now, the lieutenant at a load of it
            int waiting = Lieutenant.WaitingOrders, answers = Lieutenant.WaitingAnswers;
            if (waiting > 0)
            {
                try { Lieutenant.SavedWhileWaiting(save, waiting, answers); }
                catch (Exception e) { Diag.Error("telling the ruler his lieutenant's orders are not in the save", e); }
            }
            // the urgent lane, as each hour's tool steps are: the tools are written as they stood at the hour the save was made in
            Background.PostFirst("orders kept for the save", () => Write(file, save, hours, load, held, caps, waiting, answers));
        }

        private static void Write(string file, string save, double hours, string load, List<JObject> held, List<JObject> caps, int lieutenantWaiting = 0, int lieutenantAnswers = 0)
        {
            var orders = new JArray();
            foreach (JObject h in held)
            {
                int id = (int?)h["tool_id"] ?? 0;
                JObject tool = id > 0 ? Runner.Export(id) : null;
                if (tool == null) continue;   // its tool ended meanwhile, or never started
                orders.Add(new JObject { ["hand"] = h, ["tool"] = tool, ["record"] = Outcomes.Export(id) });
            }
            // the self-waking agents' watches (0.18), as they stood
            JObject watches = Watches.ExportAll();
            var o = new JObject
            {
                ["type"] = "orders_at_save", ["save"] = save, ["hours"] = hours, ["date"] = Text.When(hours), ["load"] = load,
                ["wall"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", Text.Inv), ["orders"] = orders, ["wages"] = new JArray(caps), ["watches"] = watches
            };
            if (lieutenantWaiting > 0) o["lieutenant_waiting"] = new JObject { ["orders"] = lieutenantWaiting, ["answers"] = lieutenantAnswers };
            Files.Overwrite(file, o.ToString(Formatting.None));
            Diag.Event("orders", "kept for the save", new { save, orders = orders.Count, wages = caps.Count, watches = watches.Properties().Sum(p => (p.Value as JArray)?.Count ?? 0), lieutenant_waiting = lieutenantWaiting, file });
        }

        // ---------- at a load ----------

        /// <summary>
        /// Game's thread, at a load, after the agents of this load are known and Brain.Reset is posted: the orders written down at this
        /// save carried on. The tools' side follows on the worker, after its reset.
        /// </summary>
        public static void AtLoad(string save, double loadedAt)
        {
            JObject o = Read(save, loadedAt, out string why);
            if (o == null)
            {
                if (why != null) Log.Line("orders: " + why);
                Background.Post("orders carried on", () => { Told.Clear(); Watches.SetPending(null); });
                return;
            }
            // the self-waking agents' watches (0.18): each realm's first hour takes its own up, checked again
            var watches = new Dictionary<string, JArray>(StringComparer.Ordinal);
            if (o["watches"] is JObject wo) foreach (JProperty p in wo.Properties()) if (p.Value is JArray wa) watches[p.Name] = wa;
            Background.Post("watches carried on", () => Watches.SetPending(watches));
            var tools = new List<KeyValuePair<JObject, JObject>>();
            var refused = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            void Refuse(string realm, string what) { if (realm == null) return; if (!refused.TryGetValue(realm, out List<string> l)) refused[realm] = l = new List<string>(); l.Add(what); }
            foreach (JObject e in (o["orders"] as JArray ?? new JArray()).OfType<JObject>())
            {
                JObject hand = e["hand"] as JObject, tool = e["tool"] as JObject;
                if (hand == null || tool == null) continue;
                string realm = (string)hand["realm"];
                string what = ((string)tool["order"]?["lord_name"] ?? (string)hand["lord"]) + "'s " + ((string)hand["tool"] ?? "order");
                AgentConfig serving = Bridge.Configs.FirstOrDefault(c => c.Realm == realm);
                string reason;
                if (serving == null || serving.Mode != "live") reason = "the realm's agent is not in command now";
                else if (!SameFolder(serving.ChannelDir, (string)tool["owner"])) reason = "another agent commands the realm now";
                else
                {
                    try { reason = Orders.Resume(hand, serving.Lords, (string)tool["order"]?["at"], (string)tool["order"]?["target"], (tool["order"]?["places"] as JArray)?.Select(x => (string)x)); }
                    catch (Exception x) { reason = "it could not be carried on (" + x.Message + ")"; Diag.Error("carrying on an order from the save", x); }
                }
                if (reason != null) { Refuse(realm, what + " (" + reason + ")"); Diag.Event("order", "not carried on", new { save, lord = (string)hand["lord"], tool = (string)hand["tool"], why = reason }); }
                else tools.Add(new KeyValuePair<JObject, JObject>(tool, e["record"] as JObject));
            }
            int caps = 0;
            foreach (JObject w in (o["wages"] as JArray ?? new JArray()).OfType<JObject>())
            {
                MobileParty p = Orders.PartyById((string)w["party"]);
                AgentConfig serving = p == null ? null : Bridge.Configs.FirstOrDefault(c => c.Realm == p.MapFaction?.StringId);
                string reason = serving == null || serving.Mode != "live" ? "no agent in command" : Wages.Resume(w);
                if (reason == null) caps++;
            }
            // the lieutenant's orders that had not started when the save was made (0.19.2): not in it, so said in the window and told to the lieutenant
            int lost = (int?)o["lieutenant_waiting"]?["orders"] ?? 0;
            if (lost > 0)
            {
                Log.Line("orders: " + lost + " of the lieutenant's orders had not started when " + save + " was made, so it does not hold them");
                try { Lieutenant.NotInSave(save, lost, (int?)o["lieutenant_waiting"]?["answers"] ?? 1); }
                catch (Exception e) { Diag.Error("telling of the lieutenant's orders the save does not hold", e); }
            }
            Log.Line("orders: " + tools.Count + " carried on from " + save + " (" + Text.When((double?)o["hours"] ?? loadedAt) + "), " + refused.Values.Sum(l => l.Count) + " not, " + caps + " wage caps");
            Diag.Event("orders", "carried on", new { save, orders = tools.Count, not_carried = refused.Values.Sum(l => l.Count), wage_caps = caps });
            Background.Post("orders carried on", () => Resume(tools, refused));
        }

        /// <summary>The save's file, when it is this save's at this hour; else null and why (null: no file, nothing to say).</summary>
        private static JObject Read(string save, double loadedAt, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(save) || double.IsNaN(loadedAt)) return null;
            string file = FileFor(Paths.Data(Bridge.CampaignId), save);
            try
            {
                if (!File.Exists(file)) return null;
                JObject o = JObject.Parse(File.ReadAllText(file));
                double h = (double?)o["hours"] ?? double.NaN;
                if ((string)o["save"] != save || double.IsNaN(h) || Math.Abs(h - loadedAt) > SameHour)
                {
                    why = "the orders written for " + save + " are from " + Text.When(h) + ", not this save's hour (" + Text.When(loadedAt) + "): none carried on";
                    return null;
                }
                return o;
            }
            catch (Exception e)
            {
                Diag.Error("reading the orders written for the save " + save, e);
                why = "the orders written for " + save + " could not be read: none carried on";
                return null;
            }
        }

        private static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception e) { Diag.Swallowed("comparing agent folders", e); return false; }
        }

        /// <summary>Worker, after its reset at the load: each tool taken up where it was; what each realm's first look will say.</summary>
        private static void Resume(List<KeyValuePair<JObject, JObject>> tools, Dictionary<string, List<string>> refused)
        {
            Told.Clear();
            var carried = new Dictionary<string, int>(StringComparer.Ordinal);
            var upgraded = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<JObject, JObject> kv in tools)
            {
                string realm = (string)kv.Key["realm"];
                int id = (int?)kv.Key["id"] ?? 0;
                string why;
                try { why = Runner.Resume(kv.Key, kv.Value); }
                catch (Exception e) { why = "it could not be carried on (" + e.Message + ")"; Diag.Error("taking up a tool from the save", e); }
                if (why == null)
                {
                    carried.TryGetValue(realm ?? "", out int n); carried[realm ?? ""] = n + 1;
                    if (Runner.LastResumeUpgraded) { upgraded.TryGetValue(realm ?? "", out int u); upgraded[realm ?? ""] = u + 1; }
                    continue;
                }
                string what = ((string)kv.Key["order"]?["lord_name"] ?? "a lord") + "'s " + ((string)kv.Key["def"]?["name"] ?? "order");
                if (realm != null) { if (!refused.TryGetValue(realm, out List<string> l)) refused[realm] = l = new List<string>(); l.Add(what + " (" + why + ")"); }
                Diag.Event("tool", "not carried on", new { id, why });
                if (id > 0) Runner.ReleaseSink(id, "its order could not be carried on from the save: " + why);
            }
            foreach (string realm in carried.Keys.Concat(refused.Keys).Distinct())
            {
                carried.TryGetValue(realm, out int n);
                refused.TryGetValue(realm, out List<string> no);
                string text = n > 0 ? " The game was loaded from a save: the " + (n == 1 ? "order" : n + " orders") + " you had in force then carry on as they stood (YOUR ORDERS IN FORCE lists them)." : " The game was loaded from a save.";
                // an intervention by hand at this load (0.17.4): the orders that took the library's newer version, in the order's own terms
                if (upgraded.TryGetValue(realm, out int up) && up > 0)
                    text += " " + (up == 1 ? "One of them" : up + " of them") + " (" + string.Join(", ", Runner.UpgradeAtLoad) + ") now follow" + (up == 1 ? "s" : "")
                            + " that order's newer version from this load on: each goes on from where it stood, a run to carry food to its holding no longer ends it, and it feeds a holding only up to what its stores hold when full.";
                if (no != null && no.Count > 0) text += " Not carried on: " + string.Join("; ", no) + ".";
                Told[realm] = text;
            }
        }

        /// <summary>Worker: what the realm's first look since the load says of its orders (empty when nothing was carried on).</summary>
        public static string NoteFor(string realm) => realm != null && Told.TryGetValue(realm, out string t) ? t : "";
    }

    /// <summary>
    /// The war rooms of this load's agents (0.13.5, for when I step off the game and come back to it later): a running war room
    /// holds a lock named after its folder (WarRoom LiveLock). At each load, an agent switched on whose war room is not running is
    /// started when settings.txt names the war room's program (war_room), with no window, else an alarm says so. Game's thread.
    /// </summary>
    internal static class WarRooms
    {
        /// <summary>The lock's name, as WarRoom.LiveLock makes it from the folder.</summary>
        internal static string LockName(string folder)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).TrimEnd('\\', '/').ToLowerInvariant()));
            var sb = new StringBuilder(@"Local\BannerlordAgents.WarRoom.");
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("X2", Text.Inv));
            return sb.ToString();
        }

        /// <summary>Is a war room running for this agent folder?</summary>
        public static bool Running(string folder)
        {
            if (!Mutex.TryOpenExisting(LockName(folder), out Mutex m)) return false;
            using (m)
            {
                try
                {
                    if (!m.WaitOne(0)) return true;
                    m.ReleaseMutex();
                    return false;
                }
                catch (AbandonedMutexException) { m.ReleaseMutex(); return false; }
            }
        }

        /// <summary>At a load: every agent switched on has its war room, started now when it was not running.</summary>
        public static void Check(IEnumerable<AgentConfig> configs)
        {
            foreach (AgentConfig c in configs)
            {
                if (!c.On || string.IsNullOrEmpty(c.Workspace)) continue;
                bool up;
                try { up = Running(c.Workspace); }
                catch (Exception e) { Diag.Swallowed("checking a war room's lock", e); continue; }
                if (up) continue;
                string exe = Settings.WarRoom;
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                {
                    Diag.Alarm("war_room_down", "The war room for " + c.Workspace + " is not running, so that agent will not answer. Start it with scripts\\agents.ps1 start"
                                                + (string.IsNullOrWhiteSpace(exe) ? " (or name its program as war_room in settings.txt, and the game starts it at a load)." : " (war_room in settings.txt names " + exe + ", which is missing)."));
                    continue;
                }
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(exe, "run --dir \"" + c.Workspace + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) };
                    System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);
                    Log.Line("war room for " + c.Workspace + " was not running: started it (process " + (p?.Id.ToString(Text.Inv) ?? "?") + ")");
                    Diag.Event("session", "war room started", new { folder = c.Workspace, exe, pid = p?.Id });
                }
                catch (Exception e) { Diag.Error("starting the war room for " + c.Workspace, e); }
            }
        }
    }
}
