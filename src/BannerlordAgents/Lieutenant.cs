using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;

namespace BannerlordAgents
{
    /// <summary>
    /// The player's lieutenant (0.10; Mert, Oct 22: the player types orders to "their liutenant, which then orders lords to do
    /// stuff on the map ... it would propose sending x, x, x lords there ... I would verify and approve the order"; Oct 23: "keep
    /// everything verbal", "Keep its answers short and concise whenever possible, make popups pause the game until the popup is
    /// closed", it commands every lord of the realm "including my own clan's parties", and (0.10.1) a window of its own, opened
    /// with Enter, with buttons to approve or decline a proposal). An agent folder of kind lieutenant, "realm = player":
    /// - on the campaign map, Enter opens its window (LieutenantWindow) instead of the game's chat box, which keeps T
    ///   (ChatHook); what the player sends goes into the lieutenant's channel as chat.json, with the waiting proposal and what the
    ///   player did with the last one (notes);
    /// - its war room (LieutenantRoom) answers in orders\chat-N.json: a few words, maybe a proposal, maybe the orders the player
    ///   has just agreed to in words, maybe a longer text (shown in the window);
    /// - it never acts unasked: since 0.13.8 (Mert, Oct 29: "I should only have to approve them in the chat since many times I
    ///   approve some but have comments on others") it answers only the player's words, and carries out the orders those words
    ///   asked for or agreed to ("carry_out"), all or some of the waiting proposal, or new ones he told it to give; what it would do
    ///   of its own idea, and what he wants changed, becomes the proposal and waits for his next words (LieutenantRules.Gate). The
    ///   Approve and Decline buttons are gone;
    /// - its orders go through the door every agent's go through (Orders.FromAgent), for the player's realm's lords, the player
    ///   and lords holding a Party AI Controls order excepted, and wait for the clock as every agent's do; what becomes of them
    ///   comes into the window.
    /// It is never poked. Answers are shown at once, even with the game paused (the frame's queue, Background.ToUi); orders
    /// are carried out on the game's tick (Background.ToMain). Game's thread, except ReadAnswer and Heard (worker).
    ///
    /// The conversation of a save (0.11.1; Mert, Oct 23: "the chat I have with my liutenant should be persistent within a save,
    /// even if each prompt is technically a different session ... I want to be able to reference previous things that were
    /// said in the chat throughout a save. The liutenant should also have access to the chat"): every line, the player's and
    /// the lieutenant's, the buttons, the proposals and what became of the orders, goes into the campaign's own
    /// lieutenant\chat.jsonl, labelled with the session and its in-game hour and wall clock. A load keeps the lines of its save's
    /// history (History.HoldsLine, the same rule as the chronicle's), shows them in the window and puts back a proposal still
    /// waiting; each message carries the latest of them (chat.json's history) and the whole conversation is conversation.txt
    /// in the channel, for the lieutenant to read. Nothing of it is in the save. Only the lieutenant's.
    /// </summary>
    internal static class Lieutenant
    {
        public const string Name = "Lieutenant";
        private const uint Voice = 0xFFE6C36Au, You = 0xFFC8C8C8u, Aside = 0xFF8FB3D9u, Refused = 0xFFD98F8Fu, Post = 0xFFB8D98Fu;

        private sealed class Proposal
        {
            public int Chat;
            public string Say, Words, List;
            public JArray Orders;
            /// <summary>Since 0.13 (docs/PLAN-0.13.md B5): tools it would write, diplomatic acts and letters to allies, waiting for the same yes.</summary>
            public JArray Tools, Diplomacy, Letters;
            public double Hours;
            public int Count => (Orders?.Count ?? 0) + (Tools?.Count ?? 0) + (Diplomacy?.Count ?? 0) + (Letters?.Count ?? 0);
        }

        private static Proposal _pending;
        private static int _next = -1;
        // what the player said since the lieutenant's last answer (it answers the newest, with these before it)
        private static readonly List<string> Unanswered = new List<string>();
        // what the player did with a proposal (the buttons), told with the next words
        private static readonly List<string> Notes = new List<string>();
        // letters from the realm's allies since the lieutenant's last answer (0.13), told with the next words
        private static readonly List<string> Mail = new List<string>();
        // the conversation of this save (0.11.1): its lines as kept, oldest first; where the words not yet answered begin
        private static readonly List<JObject> Thread = new List<JObject>();
        private static int _unansweredFrom = -1;
        private static string _chatFile;

        /// <summary>The lines of the conversation the lieutenant gets with each message; the rest are in conversation.txt.</summary>
        public const int HistoryLines = 60;

        /// <summary>Waiting for the lieutenant's answer.</summary>
        public static bool Thinking { get; private set; }

        /// <summary>The lieutenant of this load (null: none in the agents' settings, or its channel is off).</summary>
        public static RealmAgent Agent => Agents.All.FirstOrDefault(a => a.Config.IsLieutenant && a.On && a.Realm != null);

        /// <summary>A lieutenant serves the player's realm in this game.</summary>
        public static bool Active
        {
            get
            {
                if (!Bridge.Running) return false;
                RealmAgent a = Agent;
                return a != null && Bridge.SeatOf(a.Realm) != null;
            }
        }

        public static void Reset()
        {
            _pending = null;
            _next = -1;
            Thinking = false;
            Unanswered.Clear();
            Notes.Clear();
            Mail.Clear();
            Thread.Clear();
            _unansweredFrom = -1;
            _chatFile = null;
            LieutenantWindow.Reset();
        }

        /// <summary>
        /// At load, once the save's history is known (Bridge.Start): the conversation this save lived through, back in the window,
        /// and its proposal still waiting, if one was.
        /// </summary>
        public static void Restore(History history)
        {
            Thread.Clear();
            _chatFile = Path.Combine(Paths.Data(Bridge.CampaignId), "lieutenant", "chat.jsonl");
            if (history == null) return;
            Thread.AddRange(LabelledLines.Read(_chatFile, history));
            int shown = 0;
            foreach (JObject l in Thread)
            {
                KeyValuePair<string, uint>? line = Shown(l);
                if (line == null) continue;
                if (shown++ == 0) LieutenantWindow.Add("(Earlier in this save:)", Aside, false);
                LieutenantWindow.Add(line.Value.Key, line.Value.Value, false, (string)l["k"] == "you");
            }
            // a proposal is still waiting when nothing answered it before the save
            int last = Thread.FindLastIndex(l => (string)l["k"] == "proposal");
            if (last >= 0 && !Thread.Skip(last + 1).Any(l => Resolves((string)l["k"])))
            {
                JObject p = Thread[last];
                JObject x = p["x"] as JObject;
                Proposal back = Make((int?)p["n"] ?? -1, (string)p["say"], p["o"] as JArray ?? new JArray(), (double?)p["h"] ?? 0, x?["tools"] as JArray, x?["diplomacy"] as JArray, x?["letters"] as JArray);
                if (back.Count > 0)
                {
                    _pending = back;
                    LieutenantWindow.Add("(Your lieutenant's proposal from before still waits for your answer.)", Aside, false);
                }
            }
            if (Thread.Count > 0) Diag.Event("lieutenant", "conversation restored", new { lines = Thread.Count, shown, pending = _pending?.Chat, save = history.Save });
        }

        private static bool Resolves(string kind) => kind == "approved" || kind == "declined" || kind == "carried" || kind == "dropped" || kind == "proposal";

        /// <summary>One line of the conversation, kept for this save (and the window's own line, shown by the caller).</summary>
        private static void Keep(string kind, string text, int n = -1, JArray orders = null, string say = null, Proposal whole = null)
        {
            if (_chatFile == null || Campaign.Current == null) return;
            var l = new JObject
            {
                ["load"] = Bridge.History?.Label, ["h"] = CampaignTime.Now.ToHours, ["w"] = DateTime.Now.ToString("o", Text.Inv), ["k"] = kind, ["t"] = text ?? ""
            };
            if (n >= 0) l["n"] = n;
            if (orders != null) l["o"] = orders.DeepClone();
            if (say != null) l["say"] = say;
            // a proposal's tools, diplomatic acts and letters (0.13), so a load puts the whole proposal back
            if (whole != null && (whole.Tools?.Count > 0 || whole.Diplomacy?.Count > 0 || whole.Letters?.Count > 0))
                l["x"] = new JObject { ["tools"] = whole.Tools?.DeepClone(), ["diplomacy"] = whole.Diplomacy?.DeepClone(), ["letters"] = whole.Letters?.DeepClone() };
            Thread.Add(l);
            string json = l.ToString(Newtonsoft.Json.Formatting.None), file = _chatFile;
            Background.Post("lieutenant conversation", () => Files.Append(file, json));
        }

        /// <summary>How a kept line looks in the window (null: kept for the record only).</summary>
        private static KeyValuePair<string, uint>? Shown(JObject l)
        {
            string t = (string)l["t"] ?? "";
            switch ((string)l["k"])
            {
                case "you": return new KeyValuePair<string, uint>("You: " + t, You);
                case "lt": return new KeyValuePair<string, uint>(Name + ": " + t, Voice);
                case "detail": return new KeyValuePair<string, uint>(t, Voice);
                case "approved": return new KeyValuePair<string, uint>(t.Length == 0 ? "You approved it." : "You approved:\n" + t, You);
                case "declined": return new KeyValuePair<string, uint>("You declined it.", You);
                case "aside": return new KeyValuePair<string, uint>(t, Aside);
                case "refused": return new KeyValuePair<string, uint>("(" + t + ")", Refused);
                case "heard": return new KeyValuePair<string, uint>(Name + ": " + t, Aside);
                case "letter": return new KeyValuePair<string, uint>(t, Post);
                default: return null;
            }
        }

        /// <summary>A kept line for the lieutenant (null: not for it). Long texts are cut in the message, whole in conversation.txt.</summary>
        internal static string ForLieutenant(JObject l, bool whole)
        {
            string t = ((string)l["t"] ?? "").Trim();
            string date = l["h"] != null ? Text.When((double)l["h"]) : "";
            switch ((string)l["k"])
            {
                case "you": return date + " · Ruler: " + t;
                case "lt": return "  You: " + t;
                case "detail": return "  (your longer text) " + (whole || t.Length <= 500 ? t : t.Substring(0, 500) + " [cut; the rest is in conversation.txt]");
                case "proposal": return "  (you proposed: " + t.Replace("\n", "; ") + ")";
                case "approved": return "  (the ruler approved your proposal with the button" + (t.Length > 0 ? ": " + t.Replace("\n", "; ") : "") + ")";
                case "declined": return "  (the ruler declined your proposal with the button)";
                case "carried": return "  (you gave these orders on the ruler's word: " + t.Replace("\n", "; ") + ")";
                case "dropped": return "  (your proposal was dropped)";
                case "refused": return "  (an order was refused: " + t + ")";
                case "heard": return "  (what became of an order: " + t + ")";
                case "letter": return date + " · " + t;
                default: return null;
            }
        }

        // ---------- the player speaks ----------

        /// <summary>What the player sent in the lieutenant's window: shown, and written for the lieutenant.</summary>
        public static void OnPlayerSays(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            LieutenantWindow.Add("You: " + text, You, false, mine: true);
            RealmAgent ag = Agent;
            Kingdom realm = ag != null ? Bridge.SeatOf(ag.Realm) : null;
            if (ag == null || realm == null)
            {
                LieutenantWindow.Add("(No lieutenant serves your realm in this game.)", Aside, true);
                return;
            }
            if (_next < 0) _next = Math.Max(1, ag.Channel.LastChatNumber() + 1);
            int n = _next++;
            if (Unanswered.Count == 0) _unansweredFrom = Thread.Count;
            Unanswered.Add(text);
            Keep("you", text, n);
            Recorder.OnToLieutenant(n, text);
            double hours = CampaignTime.Now.ToHours;
            // the conversation of this save before the words not yet answered (0.11.1): its latest lines go with the message, all of it into conversation.txt
            List<string> before = Thread.Take(_unansweredFrom < 0 ? Thread.Count : _unansweredFrom).Select(l => ForLieutenant(l, false)).Where(x => x != null).ToList();
            string whole = string.Join("\n", Thread.Select(l => ForLieutenant(l, true)).Where(x => x != null));
            string pac = null;
            try { pac = PacLink.Overview(realm); }
            catch (Exception e) { Diag.Error("reading Party AI Controls for the lieutenant", e); }
            var chat = new
            {
                Number = n, Said = text, Earlier = Unanswered.Take(Unanswered.Count - 1).ToList(), Notes = Notes.ToList(), Letters = Mail.ToList(), Hours = hours, Date = Text.When(hours),
                Campaign = Bridge.CampaignId, Realm = realm.StringId, RealmName = Names.Faction(realm), LoadId = Bridge.LoadId, Ruler = Names.Hero(Hero.MainHero),
                Pending = _pending == null ? null : new { _pending.Chat, _pending.Say, _pending.Orders, _pending.Tools, _pending.Diplomacy, _pending.Letters, _pending.Hours },
                History = before.Skip(Math.Max(0, before.Count - HistoryLines)).ToList(), HistoryTotal = before.Count, Save = Bridge.History?.Save, PartyAi = pac,
                Run = RunTags.Id, Arm = RunTags.Arm
            };
            Notes.Clear();
            Mail.Clear();
            Thinking = true;
            LieutenantWindow.Refresh();
            Channel ch = ag.Channel;
            // no hour copied since this load (the clock has not run since the save was loaded): copy one now and write the realm's report
            // first, so the lieutenant never answers from the report of the session or save before (0.13.9; Oct 29, chat 11 read the
            // report written before the load, which did not yet list Stein's army's lords)
            if (ag.Latest == null && Bridge.Running)
            {
                try
                {
                    Snapshot now = Capture.Take(Bridge.Seats, true);
                    string seat = realm.StringId;
                    Background.Post("lieutenant report on the spot", () => Brain.LieutenantReportNow(now, seat));
                }
                catch (Exception e) { Diag.Error("copying the hour for the lieutenant's report", e); }
            }
            Background.Post("lieutenant chat", () => ch.WriteChat(n, chat, text, whole));
            Diag.Count("lieutenant:said");
            Diag.Event("lieutenant", "said", new { chat = n, text, pending = _pending?.Chat });
        }

        /// <summary>The Approve button: the proposal waiting is carried out as it stands, and the lieutenant is told with the next words.</summary>
        public static void Approve()
        {
            Proposal p = _pending;
            if (p == null) return;
            _pending = null;
            RealmAgent ag = Agent;
            // what was approved stays in the conversation, as the proposal's box empties
            LieutenantWindow.Add(string.IsNullOrEmpty(p.List) ? "You approved it." : "You approved:\n" + p.List, You, false, mine: true);
            Keep("approved", p.List, p.Chat);
            Recorder.OnLieutenantAnswered(p.Chat, true, p.List);
            Notes.Add("The ruler approved your proposal of message #" + p.Chat + " with the button, and its orders were given.");
            if (ag != null) { string realm = ag.Realm; Background.ToMain.Enqueue(() => Carry(p, p.Chat, realm)); }
            LieutenantWindow.Refresh();
            Diag.Event("lieutenant", "approved", new { chat = p.Chat, orders = p.Orders?.Count, tools = p.Tools?.Count, diplomacy = p.Diplomacy?.Count, letters = p.Letters?.Count });
        }

        /// <summary>The Decline button: the proposal waiting is dropped, and the lieutenant is told with the next words.</summary>
        public static void Decline()
        {
            Proposal p = _pending;
            if (p == null) return;
            _pending = null;
            LieutenantWindow.Add("You declined it.", You, false, mine: true);
            Keep("declined", "", p.Chat);
            Recorder.OnLieutenantAnswered(p.Chat, false, null);
            Notes.Add("The ruler declined your proposal of message #" + p.Chat + " with the button; nothing was given.");
            LieutenantWindow.Refresh();
            Diag.Event("lieutenant", "declined", new { chat = p.Chat });
        }

        /// <summary>The proposal waiting, in words for the window (empty: none).</summary>
        public static string ProposalWords() => _pending == null ? "" : _pending.Words;

        // ---------- the lieutenant answers ----------

        /// <summary>Worker: one answer file (orders\chat-N.json), shown at once on the frame's queue (even with the game paused).</summary>
        public static bool ReadAnswer(string path, string realm, out string error)
        {
            error = null;
            JObject a;
            try
            {
                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8)) text = sr.ReadToEnd();
                a = JObject.Parse(text);
            }
            catch (Exception e) when (e is IOException || e is Newtonsoft.Json.JsonException || e is UnauthorizedAccessException)
            {
                error = e.Message;
                return false;
            }
            Diag.Event("lieutenant", "answer file", new { file = Path.GetFileName(path), chat = (int?)a["chat"], carry_out = (bool?)a["carry_out"], orders = (a["orders"] as JArray)?.Count, proposed = (a["propose"] as JArray)?.Count });
            Background.ToUi.Enqueue(() => OnAnswer(a, realm));
            return true;
        }

        private static void OnAnswer(JObject a, string realmId)
        {
            int n = (int?)a["chat"] ?? -1;
            string say = (string)a["say"], detail = (string)a["detail"];
            JArray orders = a["orders"] as JArray, propose = a["propose"] as JArray;
            // since 0.13 (B5): tools it would write, diplomatic acts and letters to allies, under the same yes rule as its orders
            JArray tools = a["tools"] as JArray, diplomacy = a["diplomacy"] as JArray, letters = a["letters"] as JArray;
            int extras = (tools?.Count ?? 0) + (diplomacy?.Count ?? 0) + (letters?.Count ?? 0);
            bool carry = (bool?)a["carry_out"] == true, drop = (bool?)a["drop"] == true;
            int answered = (int?)a["pending_chat"] ?? -1;
            Unanswered.Clear();
            _unansweredFrom = -1;
            Thinking = false;
            if (!string.IsNullOrWhiteSpace(say)) { LieutenantWindow.Add(Name + ": " + say.Trim(), Voice, true); Keep("lt", say.Trim(), n); }
            if (!string.IsNullOrWhiteSpace(detail)) { LieutenantWindow.Add(detail.Trim(), Voice, false); Keep("detail", detail.Trim(), n); }
            double now = Campaign.Current != null ? CampaignTime.Now.ToHours : 0;
            string gate = LieutenantRules.Gate(carry, (orders?.Count ?? 0) + (carry ? extras : 0), (propose?.Count ?? 0) + (carry ? 0 : extras), drop);
            // the orders the player's words asked for or agreed to: given now (0.13.8: in words only, no buttons)
            if (gate == "carry" || gate == "carry+propose")
            {
                Proposal agreed = Make(n, say, orders ?? new JArray(), now, tools, diplomacy, letters);
                Background.ToMain.Enqueue(() => Carry(agreed, n, realmId));
                Keep("carried", agreed.List, n);
                Recorder.OnLieutenantAgreed(n, agreed.List);
                _pending = null;
            }
            // what waits for the player's next words: a proposal of its own, or what he wanted changed, changed (beside the orders given)
            if (gate == "propose" || gate == "carry+propose")
            {
                _pending = gate == "propose" ? Make(n, say, propose ?? new JArray(), now, tools, diplomacy, letters) : Make(n, say, propose ?? new JArray(), now);
                Keep("proposal", _pending.List, n, _pending.Orders, say, _pending);
            }
            if (gate == "drop")
            {
                if (_pending != null) Keep("dropped", "", _pending.Chat);
                _pending = null;
            }
            LieutenantWindow.Refresh();
            Diag.Count("lieutenant:answers");
            Diag.Event("lieutenant", "answered", new { chat = n, gate, pending = _pending?.Chat, said = say });
        }

        private static Proposal Make(int chat, string say, JArray orders, double hours, JArray tools = null, JArray diplomacy = null, JArray letters = null)
        {
            var lines = new List<string>();
            // a tool it writes is shown with what it does, its settings and the checker's result before you approve it (0.13, B5)
            foreach (JObject t in (tools ?? new JArray()).OfType<JObject>())
            {
                if ((bool?)t["retire"] == true) { lines.Add("- Retire your tool " + Str(t["name"]) + "."); continue; }
                JObject def = t["definition"] as JObject ?? t;
                var json = (JObject)def.DeepClone();
                json["name"] = Str(t["name"]) ?? Str(def["name"]);
                if (json["about"] == null && t["about"] != null) json["about"] = t["about"];
                Tools.ToolDef d = Tools.ToolDef.FromJson(json.ToString(), Str(json["name"]) ?? "tool", Agent?.Realm ?? "player");
                lines.Add("- " + ((bool?)t["keep"] == true ? "Keep" : "Use once") + " a tool of your own: " + d.Describe().TrimStart('-', ' ')
                          + (d.Valid ? " The checker: it passes." : " The checker: " + d.Problems.Count + " problem" + (d.Problems.Count == 1 ? "" : "s") + ", so it would be refused (" + string.Join("; ", d.Problems.Take(3)) + ")."));
            }
            foreach (JObject o in (diplomacy ?? new JArray()).OfType<JObject>()) lines.Add("- Diplomacy: " + DiploAct.From(o, -1, "chat-" + chat + ".json", 0) + (Str(o["why"]) != null ? " (" + Str(o["why"]) + ")" : "") + ".");
            foreach (JObject o in (letters ?? new JArray()).OfType<JObject>())
            {
                string text = Str(o["text"]) ?? "";
                lines.Add("- A letter to " + (Str(o["to"]) ?? "?") + ": \"" + (text.Length > 300 ? text.Substring(0, 300) + "..." : text) + "\"" + (o["plan"] is JObject p ? " (with a joint plan: " + Str(p["what"]) + (Str(p["where"]) != null ? " at " + Str(p["where"]) : "") + ")" : o["plan_id"] != null ? " (plan " + Str(o["plan_id"]) + ": " + Str(o["state"]) + ")" : "") + ".");
            }
            foreach (JObject o in orders.OfType<JObject>())
            {
                string lord = Str(o["lord"]), what = Str(o["do"]);
                Hero h = lord != null ? Hero.Find(lord) : null;
                if (string.Equals(what, "party_ai", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add("- " + (h != null ? Names.Hero(h) : lord) + ": " + PartyAiWords(o));
                    continue;
                }
                // the ruler's own army (0.13.9)
                if (string.Equals(what, "my_army", StringComparison.OrdinalIgnoreCase))
                {
                    var called = (o["lords"] as JArray ?? new JArray()).Select(x => Str(x)).Where(x => x != null).Select(x => Hero.Find(x) is Hero c ? Names.Hero(c) : x).ToList();
                    Settlement gather = Str(o["at"]) is string g ? Settlement.Find(g) : null;
                    lines.Add("- Your army" + (gather != null ? " (gathering at " + Names.Settlement(gather) + ")" : "") + ": call " + (called.Count > 0 ? string.Join(", ", called) : "no one"));
                    continue;
                }
                string at = Str(o["at"] ?? o["place"]), target = Str(o["target"]);
                Settlement s = at != null ? Settlement.Find(at) : null;
                Hero t = target != null ? Hero.Find(target) : null;
                lines.Add("- " + (h != null ? Names.Hero(h) : lord) + ": " + what + (s != null ? " " + Names.Settlement(s) : at != null ? " " + at : "")
                          + (t != null ? " " + Names.Hero(t) : target != null && s == null ? " " + target : ""));
            }
            string list = string.Join("\n", lines);
            return new Proposal { Chat = chat, Say = say, Orders = orders, Tools = tools, Diplomacy = diplomacy, Letters = letters, Hours = hours, List = list, Words = "Waiting for your answer:\n" + list };
        }

        private static string Str(JToken t) => t == null || t.Type == JTokenType.Null || t is JContainer ? null : ((string)t)?.Trim().Trim('[', ']');

        /// <summary>A party_ai order in words, for the proposal's box (names where the ids are known).</summary>
        private static string PartyAiWords(JObject o)
        {
            string Place(JToken t)
            {
                string id = Str(t);
                if (id == null) return "";
                Settlement s = Settlement.Find(id);
                Hero h = s == null ? Hero.Find(id) : null;
                return " " + (s != null ? Names.Settlement(s) : h != null ? Names.Hero(h) : id);
            }
            string One(JToken t) => t is JObject j ? (Str(j["do"]) ?? "?").Replace('_', ' ') + Place(j["at"] ?? j["target"]) : (Str(t) ?? "?");
            var parts = new List<string>();
            if (o["order"] != null) parts.Add("order " + One(o["order"]));
            if (o["queue"] is JArray q) parts.Add(q.Count == 0 ? "queue emptied" : "queue " + string.Join(", then ", q.Select(One)));
            if (o["fallback"] != null) parts.Add("backup order " + One(o["fallback"]));
            if (o["new_template"] is JObject nt) parts.Add("new template " + Str(nt["name"]));
            else if (o["template"] != null) parts.Add("template " + Str(o["template"]));
            if (o["composition"] is JObject c) parts.Add("troop mix " + string.Join(", ", c.Properties().Select(p => p.Name.Replace('_', ' ') + " " + p.Value + "%")));
            if (o["options"] is JObject op) parts.Add(string.Join(", ", op.Properties().Select(p => p.Name.Replace('_', ' ') + " " + (p.Value.Type == JTokenType.Boolean ? ((bool)p.Value ? "yes" : "no") : p.Value.ToString()))));
            if (o["managed"] != null && o["managed"].Type == JTokenType.Boolean && !(bool)o["managed"]) parts.Add("handed back to the game's own AI");
            return "in Party AI Controls, " + (parts.Count > 0 ? string.Join("; ", parts) : "nothing");
        }

        /// <summary>
        /// What the player agreed to (game's thread): its orders through the door every agent's go through; its tools into your realm's library,
        /// its diplomatic acts through the agents' own diplomacy, its letters to your allies' agents (0.13). Each refusal is said in the window.
        /// </summary>
        private static void Carry(Proposal p, int chat, string realmId)
        {
            string file = "chat-" + chat + ".json";
            void Say(string text, uint colour) => Background.ToUi.Enqueue(() => { LieutenantWindow.Add(text, colour, true); Keep(colour == Refused ? "refused" : "heard", text.Trim('(', ')'), chat); });
            int i = 0;
            foreach (JObject o in (p.Orders ?? new JArray()).OfType<JObject>())
            {
                AgentChannel.AgentOrder order = AgentChannel.OrderFrom(o, null, -1, file);
                order.Index = ++i;
                order.Realm = realmId;
                string lord = order.Lord;
                Orders.FromAgent(order, (status, reason) =>
                {
                    if (status == "refused") Say("(" + (reason ?? "an order for " + lord + " was refused") + ")", Refused);
                });
            }
            Diag.Count("lieutenant:orders carried out", i);
            int d = 0;
            foreach (JObject o in (p.Diplomacy ?? new JArray()).OfType<JObject>())
            {
                DiploAct act = DiploAct.From(o, -1, file, ++d);
                act.Realm = realmId;
                Diplomacy.FromAgent(act, (status, reason) => Say((status == "refused" ? "(Refused: " : "(") + act + ": " + reason + ")", status == "refused" ? Refused : Aside));
            }
            // tools and letters are the helper's: the realm's library and the allied channel live there
            JArray tools = p.Tools, letters = p.Letters;
            if ((tools?.Count ?? 0) + (letters?.Count ?? 0) == 0) return;
            Background.Post("lieutenant's tools and letters", () =>
            {
                RealmAgent ag = Agents.Of(realmId);
                int t = 0;
                foreach (JToken tool in tools ?? new JArray())
                {
                    t++;
                    Tools.OwnLibrary lib = Tools.OwnLibrary.For(realmId);
                    Tools.OwnLibrary.Taken r = lib != null ? lib.Take(tool as JObject, -1, "lieutenant", file, Diag.Hours, false)
                                                           : new Tools.OwnLibrary.Taken { Name = (string)(tool as JObject)?["name"], Status = "refused", Reason = "your realm has no library" };
                    ag?.Channel.ToolReceipt(-1, file, t, r, Diag.Hours);
                    if (r.Status == "kept" || r.Status == "changed" || r.Status == "retired") AgentChannel.WriteLibrary(realmId);
                    Say("(" + (r.Name ?? "a tool") + ": " + r.Status.Replace('_', ' ') + ". " + r.Reason + ")", r.Status == "refused" ? Refused : Aside);
                }
                int l = 0;
                foreach (JToken letter in letters ?? new JArray())
                {
                    l++;
                    string reason = Allies.Send(ag?.Latest, realmId, letter as JObject, "lieutenant", -1, out string status);
                    ag?.Channel.LetterReceipt(-1, file, l, (string)(letter as JObject)?["to"], status, reason, Diag.Hours);
                    Say("(" + (status == "sent" ? "Sent: " : "Not sent: ") + reason + ")", status == "sent" ? Aside : Refused);
                }
            });
        }

        /// <summary>A letter from an ally's agent to your realm (0.13, B5): into the window, and told to the lieutenant with your next words.</summary>
        public static void Letter(Allies.Letter letter, Allies.Plan plan)
        {
            string text = "A letter from the ruler of " + letter.FromName + " (" + letter.Id + "): \"" + letter.Text + "\""
                          + (plan != null ? " " + Allies.PlanText(null, plan) : "") + (plan?.State == "proposed" && plan.To == letter.To ? " Tell your lieutenant whether to agree." : "");
            LieutenantWindow.Add(text, Post, true);
            Keep("letter", text);
            Mail.Add(text);
            Diag.Count("lieutenant:letters");
        }

        // ---------- what becomes of its orders ----------

        /// <summary>Worker: the call-backs of the lieutenant's orders and their endings, from its realm's hour, into the window.</summary>
        public static void Heard(List<Trigger> triggers)
        {
            List<string> said = triggers.Where(t => t.Kind == "tool" || t.Kind == "order_ended").Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(6).ToList();
            foreach (string t in said)
            {
                string text = Name + ": " + t, heard = t;
                Background.ToUi.Enqueue(() => { LieutenantWindow.Add(text, Aside, true); Keep("heard", heard); });
            }
        }

        /// <summary>Is this the lieutenant's realm (its orders' endings go to its window, not the corner)?</summary>
        /// <summary>
        /// Does the lieutenant serve this realm? From the bridge's agents of this load (Bridge.Configs, set on the game's thread as the load begins),
        /// and the helper's (Agents, filled only once its reset has run): since 0.13.12 the first too, so at a load, before that reset, the lieutenant's
        /// carried-on orders are known as its own (Oct 29, 21:12: Theorin's and Himrion's recruiting rounds were refused at the load as if another
        /// agent's, for their Party AI Controls orders, which the lieutenant's replace).
        /// </summary>
        public static bool IsRealm(string fid) => fid != null && (Bridge.Configs.Any(c => c.IsLieutenant && c.Realm == fid) || Agents.All.Any(a => a.Config.IsLieutenant && a.Realm == fid));
    }

    /// <summary>The lieutenant's rules that need no game (the smoke test reads them).</summary>
    internal static class LieutenantRules
    {
        /// <summary>
        /// What the bridge does with an answer (since 0.13.8; Mert, Oct 29: approve in the chat only, some orders yes and others with
        /// comments): every answer is to the player's words, so its "carry_out" orders, the ones those words asked for or agreed to,
        /// are given ("carry"); its proposal waits for the next words, beside them or alone ("carry+propose", "propose"); "drop"
        /// forgets the proposal waiting; anything else leaves it as it is ("none"). (Until 0.13.7 orders were given only when the
        /// words answered a proposal still waiting, else they waited for a button: a ruler's "give those orders now" after a declined
        /// proposal was held, Oct 29.)
        /// </summary>
        public static string Gate(bool carry, int orders, int proposed, bool drop)
        {
            bool give = carry && orders > 0;
            if (give) return proposed > 0 ? "carry+propose" : "carry";
            if (proposed > 0) return "propose";
            return drop ? "drop" : "none";
        }
    }

    /// <summary>
    /// Enter on the campaign map, for the lieutenant (0.10.2; Mert, Oct 23: "enter should open the liutenant window, t should
    /// open the chat window"). The game's chat box looks at its keys every frame (GauntletChatLogView.HandleInput, through the
    /// map's own keys); with a lieutenant on the campaign map and the chat box shut, Enter there opens the lieutenant's window
    /// instead, and T, and Enter while the chat box is open, stay the chat box's. While the window is open the chat box does not
    /// look at its keys, so it stays shut beneath. Missions and multiplayer are left as they are. Harmony, from GameHooks.
    /// (0.10.1 hooked the chat box's start of typing, MPChatVM.StartTyping: two lines, which the game folds into its callers,
    /// so the hook was never reached.)
    /// </summary>
    internal static class ChatHook
    {
        private const int ChatEnter = 8; // the chat keys' Enter (ChatLogHotKeyCategory.FinalizeChat)

        internal static bool InputPrefix(TaleWorlds.MountAndBlade.ViewModelCollection.Multiplayer.MPChatVM ____dataSource)
        {
            try
            {
                if (Campaign.Current == null || Mission.Current != null || GameNetwork.IsMultiplayer || !Lieutenant.Active) return true;
                if (LieutenantWindow.IsOpen) return false;
                if (____dataSource == null || ____dataSource.IsTypingText || ____dataSource.IsInspectingMessages) return true;
                TaleWorlds.ScreenSystem.ScreenBase top = TaleWorlds.ScreenSystem.ScreenManager.TopScreen;
                if (top == null || top.GetType().Name != "MapScreen") return true;
                TaleWorlds.InputSystem.InputContext keys = top.FindLayer<TaleWorlds.Engine.Screens.SceneLayer>()?.Input;
                if (keys == null) return true;
                // the chat box registers its keys on the map's the same way before it reads them
                TaleWorlds.InputSystem.GameKeyContext chatKeys = TaleWorlds.InputSystem.HotKeyManager.GetCategory("ChatLogHotKeyCategory");
                if (chatKeys == null) return true;
                if (!keys.IsCategoryRegistered(chatKeys)) keys.RegisterHotKeyCategory(chatKeys);
                if (!keys.IsGameKeyReleased(ChatEnter) && !keys.IsHotKeyReleased("FinalizeChatAlternative")) return true;
                LieutenantWindow.AskOpen();
                return false;
            }
            catch (Exception e)
            {
                Diag.Swallowed("the lieutenant's key", e);
                return true;
            }
        }
    }
}
