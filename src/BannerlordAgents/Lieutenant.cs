using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Diamond;
using TaleWorlds.MountAndBlade.ViewModelCollection.Multiplayer;

namespace BannerlordAgents
{
    /// <summary>
    /// The player's lieutenant (0.10; Mert, Oct 22: the player types orders into the chat "to their liutenant, which then orders
    /// lords to do stuff on the map ... it would propose sending x, x, x lords there ... I would verify and approve the order";
    /// Oct 23: "keep everything verbal in the chatbox. My approval should also be verbal. Keep its answers short and concise
    /// whenever possible, make popups pause the game until the popup is closed", and it may command every lord of the realm,
    /// "including my own clan's parties"). An agent folder of kind lieutenant, "realm = player", in the agents' settings:
    /// - the game's own chat box (the message log at the bottom left: the campaign map lets the player type in it, but in
    ///   single-player it has no channel to send to) gets a channel of the lieutenant's on the campaign map (ChatHook). What the
    ///   player sends goes into the lieutenant's channel as chat.json; the clock stops while the player types (lieutenant_pause);
    /// - its war room (LieutenantRoom) answers in orders\chat-N.json: a few words for the chat box, maybe a proposal of orders,
    ///   maybe the orders the player has just agreed to, maybe a longer text for a window that pauses the game;
    /// - it never acts unasked: orders are carried out only when the player's words answered a proposal still waiting (the war
    ///   room says which, from chat.json; LieutenantRules.Gate); any others become the proposal and wait for the player's yes;
    /// - its orders go through the door every agent's go through (Orders.FromAgent), for the player's realm's lords, the player
    ///   and lords holding a Party AI Controls order excepted; what becomes of them comes into the chat box.
    /// It is never poked. Game's thread, except ReadAnswer and Heard (worker).
    /// </summary>
    internal static class Lieutenant
    {
        public const string Name = "Lieutenant";

        private sealed class Proposal
        {
            public int Chat;
            public string Say;
            public JArray Orders;
            public double Hours;
        }

        private static Proposal _pending;
        private static int _next = -1;
        // what the player said since the lieutenant's last answer (it answers the newest, with these before it)
        private static readonly List<string> Unanswered = new List<string>();

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
            Unanswered.Clear();
        }

        // ---------- the player speaks ----------

        /// <summary>What the player sent in the chat box on the campaign map: shown, and written for the lieutenant.</summary>
        public static void OnPlayerSays(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            Line("You: " + text, 0xFFC8C8C8u);
            RealmAgent ag = Agent;
            Kingdom realm = ag != null ? Bridge.SeatOf(ag.Realm) : null;
            if (ag == null || realm == null)
            {
                Line(Name + ": (no lieutenant serves your realm in this game)", 0xFF8FB3D9u);
                return;
            }
            if (_next < 0) _next = Math.Max(1, ag.Channel.LastChatNumber() + 1);
            int n = _next++;
            Unanswered.Add(text);
            double hours = CampaignTime.Now.ToHours;
            var chat = new
            {
                Number = n, Said = text, Earlier = Unanswered.Take(Unanswered.Count - 1).ToList(), Hours = hours, Date = Text.When(hours),
                Campaign = Bridge.CampaignId, Realm = realm.StringId, RealmName = Names.Faction(realm), LoadId = Bridge.LoadId, Ruler = Names.Hero(Hero.MainHero),
                Pending = _pending == null ? null : new { _pending.Chat, _pending.Say, _pending.Orders, _pending.Hours }
            };
            Channel ch = ag.Channel;
            Background.Post("lieutenant chat", () => ch.WriteChat(n, chat, text));
            Diag.Count("lieutenant:said");
            Diag.Event("lieutenant", "said", new { chat = n, text, pending = _pending?.Chat });
        }

        // ---------- the lieutenant answers ----------

        /// <summary>Worker: one answer file (orders\chat-N.json), handed to the game's thread.</summary>
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
            Background.ToMain.Enqueue(() => OnAnswer(a, realm));
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
            if (!string.IsNullOrWhiteSpace(say)) Line(Name + ": " + say.Trim(), 0xFFE6C36Au);
            double now = CampaignTime.Now.ToHours;
            string gate = LieutenantRules.Gate(_pending?.Chat, answered, carry, orders?.Count ?? 0, propose?.Count ?? 0, drop);
            switch (gate)
            {
                case "carry":
                    Carry(orders, n, realmId);
                    _pending = null;
                    break;
                case "wait":
                    // orders the player has not agreed to: they wait for the yes, as a proposal
                    _pending = new Proposal { Chat = n, Say = say, Orders = orders, Hours = now };
                    Line("(" + Name + " waits for your yes before giving those orders.)", 0xFF8FB3D9u);
                    Diag.Count("lieutenant:orders held for a yes");
                    break;
                case "propose":
                    _pending = new Proposal { Chat = n, Say = say, Orders = propose, Hours = now };
                    break;
                case "drop":
                    _pending = null;
                    break;
            }
            if (!string.IsNullOrWhiteSpace(detail)) Window(detail.Trim());
            Diag.Count("lieutenant:answers");
            Diag.Event("lieutenant", "answered", new { chat = n, gate, pending = _pending?.Chat, said = say });
        }

        /// <summary>The orders the player agreed to, through the door every agent's orders go through; a refusal is said in the chat box.</summary>
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
                    if (status == "refused") Line("(" + Name + ": " + (reason ?? "an order for " + lord + " was refused") + ")", 0xFFD98F8Fu);
                });
            }
            Diag.Count("lieutenant:orders carried out", i);
        }

        // ---------- what becomes of its orders ----------

        /// <summary>Worker: the call-backs of the lieutenant's orders and their endings, from its realm's hour, into the chat box.</summary>
        public static void Heard(List<Trigger> triggers)
        {
            List<string> said = triggers.Where(t => t.Kind == "tool" || t.Kind == "order_ended").Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(6).ToList();
            foreach (string t in said)
            {
                string text = Name + ": " + t;
                Background.ToMain.Enqueue(() => Line(text, 0xFF8FB3D9u));
            }
        }

        /// <summary>Is this the lieutenant's realm (its orders' endings go to the chat box, not the corner)?</summary>
        public static bool IsRealm(string fid) => fid != null && Agents.All.Any(a => a.Config.IsLieutenant && a.Realm == fid);

        // ---------- the chat box and the window ----------

        private static void Line(string text, uint argb)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(argb))); }
            catch (Exception e) { Diag.Swallowed("a line in the chat box", e); }
        }

        /// <summary>A longer answer: a window the game stands still behind until it is closed (Mert: "make popups pause the game").</summary>
        private static void Window(string text)
        {
            try { InformationManager.ShowInquiry(new InquiryData(Name, text, true, false, "Close", null, null, null), true); }
            catch (Exception e) { Diag.Swallowed("the lieutenant's window", e); }
        }
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
    /// The chat box on the campaign map, for the lieutenant (0.10). The game's chat box (MPChatVM, under GauntletChatLogView) closes
    /// in single-player because its "all" and "team" channels need a multiplayer server: on the campaign map, with a lieutenant,
    /// they become its channel (the "party" channel, which the box keeps open), named for it, and what is sent goes to the
    /// lieutenant instead of a server. Messages may be longer there (400 letters, not 100), and the clock stops while the player
    /// types. Missions (battles) and multiplayer are left as they are. Harmony, from GameHooks.
    /// </summary>
    internal static class ChatHook
    {
        private static bool OnMap => Campaign.Current != null && Mission.Current == null && !GameNetwork.IsMultiplayer;
        private static bool Ours => OnMap && Lieutenant.Active;
        private static CampaignTimeControlMode? _clockBefore;

        internal static void ChannelPrefix(ref ChatChannelType value)
        {
            try { if ((value == ChatChannelType.All || value == ChatChannelType.Team) && Ours) value = ChatChannelType.Party; }
            catch (Exception e) { Diag.Swallowed("the lieutenant's chat channel", e); }
        }

        internal static void NamePostfix(MPChatVM __instance)
        {
            try { if (__instance.ActiveChannelType == ChatChannelType.Party && Ours) __instance.ActiveChannelNameText = "(" + Lieutenant.Name + ")"; }
            catch (Exception e) { Diag.Swallowed("the lieutenant's chat channel name", e); }
        }

        internal static bool SendPrefix(string message)
        {
            try
            {
                if (!Ours) return true;
                Lieutenant.OnPlayerSays(message);
                return false;
            }
            catch (Exception e)
            {
                Diag.Error("the lieutenant's chat", e);
                return false;
            }
        }

        internal static void StartPostfix(MPChatVM __instance)
        {
            try
            {
                if (!Ours) return;
                if (__instance.MaxMessageLength < 400) __instance.MaxMessageLength = 400;
                Campaign c = Campaign.Current;
                if (Settings.LieutenantPause && c.TimeControlMode != CampaignTimeControlMode.Stop)
                {
                    _clockBefore = c.TimeControlMode;
                    c.TimeControlMode = CampaignTimeControlMode.Stop;
                }
            }
            catch (Exception e) { Diag.Swallowed("stopping the clock for the chat box", e); }
        }

        internal static void StopPostfix()
        {
            try
            {
                if (_clockBefore == null) return;
                Campaign c = Campaign.Current;
                // the clock runs again as it ran, unless the player has set it otherwise meanwhile
                if (c != null && c.TimeControlMode == CampaignTimeControlMode.Stop) c.TimeControlMode = _clockBefore.Value;
                _clockBefore = null;
            }
            catch (Exception e) { Diag.Swallowed("starting the clock after the chat box", e); }
        }
    }
}
