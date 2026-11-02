using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// The late answer (0.13.22; Mert, Nov 1: Dol Guldur's agent gave up Gwígar for "no food" in an answer that took effect about 20 in-game
    /// hours after the report it answered, 13 hours after food had been handed in): when an answer's orders take effect hours after that
    /// report, the agent's next poke says so, and what changed since that report at the places and lords its orders named, so it can
    /// change course at once. Each poke keeps a few facts of what its report showed (Notes, copied: the realm's memory changes its own
    /// sightings in place); the next poke weighs them against its own report. From the reports only, so it says nothing the realm could not
    /// know. Worker thread (the channel's pokes, answers and receipts all run there).
    /// </summary>
    internal static class LateAnswer
    {
        /// <summary>An answer whose orders took effect this many in-game hours or more after its poke is late (the agents' median was 6 to 8, Nov 1).</summary>
        public const double Hours = 6;

        /// <summary>The most places and lords one note names.</summary>
        public const int MostShown = 12;

        /// <summary>How many of the newest pokes a channel keeps for this; an answer to an older one is not weighed.</summary>
        public const int PokesKept = 30;

        /// <summary>
        /// One channel's record (Channel): each poke's notes as its report showed them, what its answer's orders named, when the last of them
        /// took effect, and whether a later poke has weighed it. Tell weighs each answered poke once, the newest two shown, each only when
        /// something its orders named has changed since its report.
        /// </summary>
        internal sealed class Book
        {
            private sealed class Entry
            {
                public double Hours, TookEffect = double.NaN;
                public Dictionary<string, Note> Notes;
                public readonly HashSet<string> Named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                public bool Told;
            }

            private readonly Dictionary<int, Entry> _pokes = new Dictionary<int, Entry>();

            public void Clear() => _pokes.Clear();

            /// <summary>A poke went out, its report built from v.</summary>
            public void Poked(int poke, double hours, View v)
            {
                _pokes[poke] = new Entry { Hours = hours, Notes = Notes(v) };
                foreach (int old in _pokes.Keys.Where(k => k <= poke - PokesKept).ToList()) _pokes.Remove(old);
            }

            /// <summary>An answer to a poke, with the orders it gives (those not refused at once).</summary>
            public void Answered(int poke, IEnumerable<AgentChannel.AgentOrder> orders)
            {
                if (!_pokes.TryGetValue(poke, out Entry e)) return;
                foreach (AgentChannel.AgentOrder o in orders)
                    foreach (string s in Named(o))
                        if (!string.IsNullOrWhiteSpace(s)) e.Named.Add(s.Trim().Trim('[', ']').Trim());
            }

            /// <summary>One of a poke's orders took effect at that in-game hour.</summary>
            public void Applied(int poke, double hours)
            {
                if (_pokes.TryGetValue(poke, out Entry e) && (double.IsNaN(e.TookEffect) || hours > e.TookEffect)) e.TookEffect = hours;
            }

            /// <summary>The notes for the late answers not told yet, for the poke current with its report built from now; null for none.</summary>
            public string Tell(View now, int current, string realm)
            {
                Dictionary<string, Note> notesNow = null;
                var parts = new List<string>();
                foreach (KeyValuePair<int, Entry> kv in _pokes.Where(k => k.Key < current).OrderByDescending(k => k.Key))
                {
                    Entry e = kv.Value;
                    if (e.Told || e.Named.Count == 0 || double.IsNaN(e.TookEffect)) continue;
                    e.Told = true;
                    if (e.TookEffect - e.Hours < Hours || parts.Count >= 2) continue;
                    if (notesNow == null) notesNow = Notes(now);
                    string t = For(kv.Key, e.Hours, e.TookEffect, e.Notes, notesNow, e.Named, now.Hours);
                    if (t == null) continue;
                    parts.Add(t);
                    Diag.Count("agent:late answer told");
                    Diag.Event("answer", "late", new { realm, poke = kv.Key, hours_late = Math.Round(e.TookEffect - e.Hours, 1), told_in = current });
                }
                return parts.Count > 0 ? string.Join("\n", parts) : null;
            }
        }

        /// <summary>What a report showed of one lord, army, captured lord or holding.</summary>
        internal sealed class Note
        {
            public string Id, Lord, Name, Kind;   // own, enemy, captive, holding
            public int Men = -1, Garrison = -1, ThreatMen;
            public string Doing, Near, Faction, Status, FoodComing, Threat, HeldAt;
            public bool InView;
            public double SeenHours = double.NaN;
            public float Food = -1, FoodCap, FoodPerDay;
        }

        /// <summary>What a report showed, by id, the lords' own ids and plain names too (an order may name a lord by his hero, his party or his name).</summary>
        public static Dictionary<string, Note> Notes(View v)
        {
            var d = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
            if (v == null) return d;
            void Add(Note n, params string[] keys)
            {
                foreach (string k in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
                    if (!d.ContainsKey(k)) d[k] = n;
            }
            foreach (ViewParty p in v.Forces)
            {
                Add(Party(p, "own", true), p.Id, p.Lord, Text.Plain(p.Name));
                if (p.Members != null) foreach (ViewParty x in p.Members) Add(Party(x, "own", true), x.Id, x.Lord, Text.Plain(x.Name));
            }
            foreach (ViewParty p in v.Seen) if (p.Relation == "enemy") Add(Party(p, "enemy", true), p.Id, p.Lord);
            foreach (ViewParty p in v.LastSeen) if (p.Relation == "enemy") Add(Party(p, "enemy", false), p.Id, p.Lord);
            foreach (ViewCaptive c in v.Captives)
                Add(new Note { Id = c.Hero, Name = c.Name, Kind = "captive", HeldAt = c.HoldingName ?? c.PartyName }, c.Hero, Text.Plain(c.Name));
            foreach (ViewHolding h in v.Holdings.Concat(v.Frontier))
                Add(new Note
                {
                    Id = h.Id, Name = h.Name, Kind = "holding", Faction = h.FactionName, Status = h.Status, Garrison = h.Garrison, Food = h.Food, FoodCap = h.FoodCap, FoodPerDay = h.FoodPerDay,
                    FoodComing = h.FoodComing, Threat = h.Threat, ThreatMen = h.ThreatMen
                }, h.Id, Text.Plain(h.Name));
            return d;
        }

        private static Note Party(ViewParty p, string kind, bool inView) => new Note
        {
            Id = p.Id, Lord = p.Lord, Name = p.Name, Kind = kind, Men = p.ArmyMen > 0 ? p.ArmyMen : p.Men, Doing = p.Doing, Near = p.Near, InView = inView, SeenHours = p.SeenHours, Faction = p.FactionName
        };

        /// <summary>Every lord, army, holding and spot an order names, its next orders' too.</summary>
        public static IEnumerable<string> Named(AgentChannel.AgentOrder o)
        {
            foreach (string s in new[] { o.Lord, o.Target, o.At }.Concat(o.Lords).Concat(o.Places)) yield return s;
            for (Newtonsoft.Json.Linq.JObject t = o.Then; t != null; t = t["then"] as Newtonsoft.Json.Linq.JObject)
            {
                foreach (string k in new[] { "lord", "target", "at" }) yield return (string)t[k];
                foreach (string k in new[] { "lords", "places" })
                    if (t[k] is Newtonsoft.Json.Linq.JArray a) foreach (Newtonsoft.Json.Linq.JToken x in a) yield return (string)x;
            }
        }

        /// <summary>
        /// The note for one late answer, or null when nothing its orders named changed: when it took effect, and for each place and lord,
        /// what its report showed against what the newest shows.
        /// </summary>
        public static string For(int poke, double reportHours, double tookEffect, Dictionary<string, Note> then, Dictionary<string, Note> now, IEnumerable<string> named, double nowHours)
        {
            var lines = new List<string>();
            var done = new HashSet<Note>();
            foreach (string raw in named)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string id = raw.Trim().Trim('[', ']').Trim();
                Note a;
                if (!then.TryGetValue(id, out a) && !then.TryGetValue(Text.Plain(id), out a)) continue;
                if (!done.Add(a)) continue;
                Note b;
                if (!now.TryGetValue(a.Id ?? id, out b) && (a.Lord == null || !now.TryGetValue(a.Lord, out b)) && !now.TryGetValue(id, out b)) b = null;
                List<string> changes = Changes(a, b, nowHours);
                if (changes.Count == 0) continue;
                lines.Add("- " + a.Name + " [" + a.Id + "]: " + string.Join("; ", changes) + ".");
                if (lines.Count >= MostShown) break;
            }
            if (lines.Count == 0) return null;
            return "Your answer to poke #" + poke + " took effect late: it answered the report of " + Text.When(reportHours) + ", and its orders took effect "
                   + Text.When(tookEffect) + ", " + Text.N((int)Math.Round(tookEffect - reportHours)) + " in-game hours later. Since that report, at the places and lords those orders named:\n"
                   + string.Join("\n", lines);
        }

        /// <summary>What changed for one place or lord, in words ("food 0 of 450 then, 219 of 450 now (about 9 days of siege)").</summary>
        public static List<string> Changes(Note a, Note b, double nowHours)
        {
            var c = new List<string>();
            if (a.Kind == "holding")
            {
                if (b == null) return c;   // beyond what the newest report shows (an enemy holding off its list): nothing to weigh
                if (a.Faction != b.Faction) c.Add("held by " + (a.Faction ?? "?") + " then, by " + (b.Faction ?? "?") + " now");
                if (a.Status != b.Status) c.Add((a.Status ?? "not under siege or raided") + " then, " + (b.Status ?? "no longer") + " now");
                if (a.Garrison >= 0 && b.Garrison >= 0 && Big(a.Garrison, b.Garrison, 20)) c.Add("garrison " + Text.N(a.Garrison) + " then, " + Text.N(b.Garrison) + " now");
                if (a.Food >= 0 && b.Food >= 0 && Math.Abs(b.Food - a.Food) >= Math.Max(1f, Math.Max(b.FoodPerDay, 0.05f * Math.Max(a.FoodCap, b.FoodCap))))
                    c.Add("food " + Food(a) + " then, " + Food(b) + " now");
                if (a.FoodComing != b.FoodComing && (a.FoodComing == null) != (b.FoodComing == null))
                    c.Add(b.FoodComing != null ? "food coming now: " + b.FoodComing : "no food coming now (then: " + a.FoodComing + ")");
                if (a.Threat != b.Threat)
                    c.Add("the nearest enemy in view " + (a.Threat != null ? a.Threat + " (" + Text.N(a.ThreatMen) + " men)" : "none") + " then, "
                          + (b.Threat != null ? b.Threat + " (" + Text.N(b.ThreatMen) + " men)" : "none") + " now");
                return c;
            }
            if (a.Kind == "own")
            {
                // where he goes and what he does are his orders' doing: only what happened to him counts
                if (b == null) { c.Add("no longer in your report"); return c; }
                if (b.Kind == "captive") { c.Add("taken prisoner" + (b.HeldAt != null ? ", held by " + b.HeldAt : "")); return c; }
                if (a.Men >= 0 && b.Men >= 0 && Big(a.Men, b.Men, 50)) c.Add(Text.N(a.Men) + " men then, " + Text.N(b.Men) + " now");
                return c;
            }
            if (a.Kind == "enemy")
            {
                if (b == null) { c.Add("no longer in your report"); return c; }
                if (a.InView && !b.InView) c.Add("in view then, out of view now (last seen " + Text.When(b.SeenHours) + ")");
                else if (!a.InView && b.InView) c.Add("out of view then, in view now");
                else if (!a.InView && !b.InView && b.SeenHours > a.SeenHours + 0.5) c.Add("seen again since, last " + Text.When(b.SeenHours));
                if (a.Men >= 0 && b.Men >= 0 && Big(a.Men, b.Men, 50)) c.Add(Text.N(a.Men) + " men then, " + Text.N(b.Men) + " now");
                if (a.Doing != b.Doing && b.Doing != null) c.Add((a.Doing ?? "?") + " then, " + b.Doing + " now");
                if (a.Near != b.Near && b.Near != null) c.Add("near " + (a.Near ?? "?") + " then, near " + b.Near + " now");
                return c;
            }
            if (a.Kind == "captive" && b != null && b.Kind == "own") c.Add("a prisoner then, free now");
            return c;
        }

        private static bool Big(int then, int now, int least) => Math.Abs(now - then) >= Math.Max(least, 0.1 * Math.Max(then, now));

        private static string Food(Note h)
        {
            string s = Text.N((int)Math.Round(h.Food)) + " of " + Text.N((int)Math.Round(h.FoodCap));
            return h.FoodPerDay > 0 ? s + " (about " + Text.N((int)Math.Floor(h.Food / h.FoodPerDay)) + " days of siege)" : s;
        }
    }
}
