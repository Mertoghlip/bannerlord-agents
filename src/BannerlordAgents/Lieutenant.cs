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
    /// - it never acts unasked: orders are carried out when the player presses Approve on the proposal waiting, or when the
    ///   player's words answered that proposal (the war room says which, from chat.json; LieutenantRules.Gate); any others
    ///   become the proposal and wait;
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
        private const uint Voice = 0xFFE6C36Au, You = 0xFFC8C8C8u, Aside = 0xFF8FB3D9u, Refused = 0xFFD98F8Fu;

        private sealed class Proposal
        {
            public int Chat;
            public string Say, Words, List;
            public JArray Orders;
            public double Hours;
        }

        private static Proposal _pending;
        private static int _next = -1;
        // what the player said since the lieutenant's last answer (it answers the newest, with these before it)
        private static readonly List<string> Unanswered = new List<string>();
        // what the player did with a proposal (the buttons), told with the next words
        private static readonly List<string> Notes = new List<string>();
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
                if (p["o"] is JArray orders && orders.Count > 0)
                {
                    _pending = Make((int?)p["n"] ?? -1, (string)p["say"], orders, (double?)p["h"] ?? 0);
                    LieutenantWindow.Add("(Your lieutenant's proposal from before still waits for your answer.)", Aside, false);
                }
            }
            if (Thread.Count > 0) Diag.Event("lieutenant", "conversation restored", new { lines = Thread.Count, shown, pending = _pending?.Chat, save = history.Save });
        }

        private static bool Resolves(string kind) => kind == "approved" || kind == "declined" || kind == "carried" || kind == "dropped" || kind == "proposal";

        /// <summary>One line of the conversation, kept for this save (and the window's own line, shown by the caller).</summary>
        private static void Keep(string kind, string text, int n = -1, JArray orders = null, string say = null)
        {
            if (_chatFile == null || Campaign.Current == null) return;
            var l = new JObject
            {
                ["load"] = Bridge.History?.Label, ["h"] = CampaignTime.Now.ToHours, ["w"] = DateTime.Now.ToString("o", Text.Inv), ["k"] = kind, ["t"] = text ?? ""
            };
            if (n >= 0) l["n"] = n;
            if (orders != null) l["o"] = orders.DeepClone();
            if (say != null) l["say"] = say;
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
                Number = n, Said = text, Earlier = Unanswered.Take(Unanswered.Count - 1).ToList(), Notes = Notes.ToList(), Hours = hours, Date = Text.When(hours),
                Campaign = Bridge.CampaignId, Realm = realm.StringId, RealmName = Names.Faction(realm), LoadId = Bridge.LoadId, Ruler = Names.Hero(Hero.MainHero),
                Pending = _pending == null ? null : new { _pending.Chat, _pending.Say, _pending.Orders, _pending.Hours },
                History = before.Skip(Math.Max(0, before.Count - HistoryLines)).ToList(), HistoryTotal = before.Count, Save = Bridge.History?.Save, PartyAi = pac,
                Run = RunTags.Id, Arm = RunTags.Arm
            };
            Notes.Clear();
            Thinking = true;
            LieutenantWindow.Refresh();
            Channel ch = ag.Channel;
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
            if (ag != null) { string realm = ag.Realm; Background.ToMain.Enqueue(() => Carry(p.Orders, p.Chat, realm)); }
            LieutenantWindow.Refresh();
            Diag.Event("lieutenant", "approved", new { chat = p.Chat, orders = p.Orders.Count });
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
            bool carry = (bool?)a["carry_out"] == true, drop = (bool?)a["drop"] == true;
            int answered = (int?)a["pending_chat"] ?? -1;
            Unanswered.Clear();
            _unansweredFrom = -1;
            Thinking = false;
            if (!string.IsNullOrWhiteSpace(say)) { LieutenantWindow.Add(Name + ": " + say.Trim(), Voice, true); Keep("lt", say.Trim(), n); }
            if (!string.IsNullOrWhiteSpace(detail)) { LieutenantWindow.Add(detail.Trim(), Voice, false); Keep("detail", detail.Trim(), n); }
            double now = Campaign.Current != null ? CampaignTime.Now.ToHours : 0;
            string gate = LieutenantRules.Gate(_pending?.Chat, answered, carry, orders?.Count ?? 0, propose?.Count ?? 0, drop);
            switch (gate)
            {
                case "carry":
                    Background.ToMain.Enqueue(() => Carry(orders, n, realmId));
                    Keep("carried", Make(n, say, orders, now).List, n);
                    _pending = null;
                    break;
                case "wait":
                    // orders the player has not agreed to: they wait for the yes, as a proposal
                    _pending = Make(n, say, orders, now);
                    LieutenantWindow.Add("(Your lieutenant waits for your yes before giving those orders.)", Aside, false);
                    Keep("aside", "(Your lieutenant waits for your yes before giving those orders.)", n);
                    Keep("proposal", _pending.List, n, orders, say);
                    Diag.Count("lieutenant:orders held for a yes");
                    break;
                case "propose":
                    _pending = Make(n, say, propose, now);
                    Keep("proposal", _pending.List, n, propose, say);
                    break;
                case "drop":
                    if (_pending != null) Keep("dropped", "", _pending.Chat);
                    _pending = null;
                    break;
            }
            LieutenantWindow.Refresh();
            Diag.Count("lieutenant:answers");
            Diag.Event("lieutenant", "answered", new { chat = n, gate, pending = _pending?.Chat, said = say });
        }

        private static Proposal Make(int chat, string say, JArray orders, double hours)
        {
            var lines = new List<string>();
            foreach (JObject o in orders.OfType<JObject>())
            {
                string lord = Str(o["lord"]), what = Str(o["do"]);
                Hero h = lord != null ? Hero.Find(lord) : null;
                if (string.Equals(what, "party_ai", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add("- " + (h != null ? Names.Hero(h) : lord) + ": " + PartyAiWords(o));
                    continue;
                }
                string at = Str(o["at"] ?? o["place"]), target = Str(o["target"]);
                Settlement s = at != null ? Settlement.Find(at) : null;
                Hero t = target != null ? Hero.Find(target) : null;
                lines.Add("- " + (h != null ? Names.Hero(h) : lord) + ": " + what + (s != null ? " " + Names.Settlement(s) : at != null ? " " + at : "")
                          + (t != null ? " " + Names.Hero(t) : target != null && s == null ? " " + target : ""));
            }
            string list = string.Join("\n", lines);
            return new Proposal { Chat = chat, Say = say, Orders = orders, Hours = hours, List = list, Words = "Waiting for your answer:\n" + list };
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

        /// <summary>The orders the player agreed to, through the door every agent's orders go through; a refusal is said in the window.</summary>
        private static void Carry(JArray orders, int chat, string realmId)
        {
            int i = 0;
            foreach (JObject o in orders.OfType<JObject>())
            {
                AgentChannel.AgentOrder order = AgentChannel.OrderFrom(o, null, -1, "chat-" + chat + ".json");
                order.Index = ++i;
                order.Realm = realmId;
                string lord = order.Lord;
                Orders.FromAgent(order, (status, reason) =>
                {
                    if (status == "refused") { string r = reason ?? "an order for " + lord + " was refused"; Background.ToUi.Enqueue(() => { LieutenantWindow.Add("(" + r + ")", Refused, true); Keep("refused", r, chat); }); }
                });
            }
            Diag.Count("lieutenant:orders carried out", i);
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
        public static bool IsRealm(string fid) => fid != null && Agents.All.Any(a => a.Config.IsLieutenant && a.Realm == fid);
    }

    /// <summary>The lieutenant's rules that need no game (the smoke test reads them).</summary>
    internal static class LieutenantRules
    {
        /// <summary>
        /// What the bridge does with an answer: carry out its orders only when the player's words answered the proposal still waiting
        /// (answered: the waiting proposal's chat number, as the war room took it from chat.json); orders without that wait for a yes
        /// ("wait"); a new proposal replaces the waiting one ("propose"); "drop" forgets it; anything else leaves it as it is ("none").
        /// </summary>
        public static string Gate(int? pending, int answered, bool carry, int orders, int proposed, bool drop)
        {
            if (carry && orders > 0) return pending.HasValue && pending.Value == answered ? "carry" : "wait";
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
