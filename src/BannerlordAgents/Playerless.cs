using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BannerlordAgents
{
    /// <summary>
    /// The switch for the runs an agent plays without you (0.11.2, Oct 23: the switch should be done by a written command,
    /// including giving them gold and food, with a good wanderer found to switch into and the command prepared;
    /// before: switch the player character to a wanderer in a random town, give that character a lot of money and food so it
    /// doesn't die, and simply let the game run). agents.run playerless shows the wanderer it would take and what it would do;
    /// agents.run playerless go [name] does it, the game paused:
    /// - the wanderer: a free one (no clan, nobody's companion, no party, not a prisoner, not a child) in a town that is not your
    ///   kingdom's nor a rival's, at peace with your kingdom and not besieged: of those, the town farthest from every castle, town
    ///   and lord of anyone at war with your kingdom or with the town's owner;
    /// - every lord of your kingdom holding a Party AI Controls order is released, as "Act as you see fit" does (PacLink.Release),
    ///   so your kingdom's agent may command him;
    /// - the wanderer joins your clan as a companion, as hiring one does: the switch mod switches only within your clan, and the
    ///   game cannot make a party for a hero of no clan (LordPartyComponent reads his clan's party template);
    /// - you become him through the switch mod's own switch (PlayerSelectionSwitch.SwitchActions.SwitchPlayer, by name);
    /// - your old character's party, which that switch holds where it stands (the game's AI off, a Party AI Controls order to stay),
    ///   is let go to the game's AI, so your kingdom's agent can command it too;
    /// - Party AI Controls' auto-recruit is switched off for every lord it has settings for, and in its defaults (0.12.1: it held
    ///   Aragorn on a refill from day 14 to the end of run 20261025-1524-C2, where the agent could not command him);
    /// - the wanderer gets gold and grain, and goes into the town; you then choose to wait there and set the clock running.
    /// Nothing guards against pop-ups for now (if something does end up blocking a run, the response comes after). Game's thread.
    /// </summary>
    internal static class Playerless
    {
        public const int Gold = 100000, Grain = 100;

        private sealed class Pick
        {
            public Hero Hero;
            public Settlement Town;
            public float Quiet;
            public string Nearest;
        }

        public const string Usage = "Usage: agents.run playerless [go] [wanderer's name]: without go it shows whom it would take and what it would do";

        public static string Command(List<string> args)
        {
            if (Campaign.Current == null || !Bridge.Running) return "Load a campaign first.";
            List<string> rest = args.Skip(1).ToList();
            bool go = rest.Count > 0 && string.Equals(rest[0], "go", StringComparison.OrdinalIgnoreCase);
            if (go) rest.RemoveAt(0);
            string name = string.Join(" ", rest).Trim();
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (mine == null) return "Your clan is in no kingdom, so there is no kingdom for an agent to rule.";
            if (mine.Leader != Hero.MainHero) return "You do not rule " + Names.Faction(mine) + " now (" + Names.Hero(mine.Leader) + " does): the switch is done already, or the save is not the run's.";
            MethodInfo switchPlayer = HarmonyLib.AccessTools.Method("PlayerSelectionSwitch.SwitchActions:SwitchPlayer");
            if (switchPlayer == null) return "The character-switch mod (PlayerSelectionSwitch) is not loaded, so there is no switch to use.";

            List<Pick> picks = Candidates(mine);
            if (picks.Count == 0) return "No free wanderer is in a quiet town at peace with " + Names.Faction(mine) + " right now.";
            Pick pick = picks[0];
            if (name.Length > 0)
            {
                string q = Text.Plain(name);
                pick = picks.FirstOrDefault(p => Text.Plain(Names.Hero(p.Hero)) == q || string.Equals(p.Hero.StringId, name, StringComparison.OrdinalIgnoreCase))
                       ?? picks.FirstOrDefault(p => Text.Plain(Names.Hero(p.Hero)).Contains(q));
                if (pick == null) return "No free wanderer in a quiet town matches '" + name + "'. The quietest: " + Listing(picks.Take(5));
            }
            List<Hero> held = Held(mine);
            if (!go)
                return "The switch for a run without you would take " + Describe(pick) + ".\n"
                       + "Next quietest: " + Listing(picks.Skip(1).Take(4)) + ".\n"
                       + "It would: release " + (held.Count == 0 ? "no lord (none holds a Party AI Controls order)" : held.Count + " lords' Party AI Controls orders (" + string.Join(", ", held.Take(8).Select(Names.Hero)) + (held.Count > 8 ? ", ..." : "") + ")")
                       + "; take " + Names.Hero(pick.Hero) + " into your clan as a companion and make you him (the switch mod's own switch); let "
                       + Names.Hero(Hero.MainHero) + "'s party go to the game's AI for your kingdom's agent to command; switch off Party AI Controls' auto-recruit for every lord; give "
                       + Text.N(Gold) + " denars and " + Grain + " grain; and take him into " + Names.Settlement(pick.Town) + ".\n"
                       + "Type agents.run playerless go" + (name.Length > 0 ? " " + name : "") + " to do it, with the game paused.";

            if (Mission.Current != null) return "Leave the battle or scene first.";
            Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            Hero old = Hero.MainHero;
            MobileParty oldParty = MobileParty.MainParty;
            var done = new List<string>();

            // 1. the lords holding your Party AI Controls orders, released for the agent
            var released = new List<string>();
            foreach (Hero h in held)
                if (PacLink.Release(h)) released.Add(Names.Hero(h));
            if (released.Count > 0) done.Add("released " + released.Count + " lords from their Party AI Controls orders (" + string.Join(", ", released) + ")");

            // 2. the wanderer joins your clan, as hiring a companion does; 3. the switch mod's own switch
            Hero w = pick.Hero;
            try
            {
                AddCompanionAction.Apply(Clan.PlayerClan, w);
                switchPlayer.Invoke(null, new object[] { w });
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                Diag.Error("the playerless switch", inner);
                return "The switch failed (" + inner.Message + "). " + string.Join("; ", done) + (done.Count > 0 ? "." : "");
            }
            if (Hero.MainHero != w) return "The switch mod did not make you " + Names.Hero(w) + " (you are " + Names.Hero(Hero.MainHero) + ").";
            done.Add("you are " + Names.Hero(w) + ", a companion of your clan now");

            // 4. your old character's party, held by the switch mod where it stands, goes to the game's AI
            if (PacLink.Release(old) || (oldParty != null && oldParty.IsActive))
            {
                if (oldParty != null && oldParty.IsActive) oldParty.Ai.SetDoNotMakeNewDecisions(false);
                done.Add(Names.Hero(old) + "'s party is led by the game's AI now, for your kingdom's agent to command");
            }

            // 4b. Party AI Controls' own refill off for every lord, so only the agent sends them to recruit (0.12.1)
            int refillOff = PacLink.AutoRecruitOff(out bool defaultsOff);
            if (refillOff > 0 || defaultsOff)
                done.Add("switched off Party AI Controls' auto-recruit for " + refillOff + (refillOff == 1 ? " lord" : " lords") + (defaultsOff ? " and for new parties" : ""));

            // 5. gold and food; 6. into the town
            GiveGoldAction.ApplyBetweenCharacters(null, w, Gold, true);
            MobileParty.MainParty.ItemRoster.AddToCounts(DefaultItems.Grain, Grain);
            done.Add("gave him " + Text.N(Gold) + " denars and " + Grain + " grain");
            string where;
            try
            {
                if (MobileParty.MainParty.CurrentSettlement == null && PlayerEncounter.Current == null) EncounterManager.StartSettlementEncounter(MobileParty.MainParty, pick.Town);
                where = MobileParty.MainParty.CurrentSettlement == pick.Town ? "he is in " + Names.Settlement(pick.Town) : "he stands at " + Names.Settlement(pick.Town) + "'s gate (click the town to go in)";
            }
            catch (Exception e)
            {
                Diag.Swallowed("taking the wanderer into the town", e);
                where = "he stands at " + Names.Settlement(pick.Town) + "'s gate (click the town to go in)";
            }
            done.Add(where);

            string text = string.Join("; ", done) + ".";
            Run.SetUp("playerless switch: " + text);
            Log.Line("playerless: " + text);
            Diag.Event("run", "playerless switch", new { wanderer = w.StringId, town = pick.Town.StringId, quiet = Math.Round(pick.Quiet, 1), nearest = pick.Nearest, old = old.StringId, released, gold = Gold, grain = Grain });
            return text.Substring(0, 1).ToUpperInvariant() + text.Substring(1)
                   + "\nNow: in the town menu choose to wait there, type agents.run check (it should say C1 or C2 with your kingdom commanded by its agent, or C0 with it left to the game's AI), then set the clock running.";
        }

        /// <summary>Free wanderers in towns at peace with your kingdom and outside the run's realms, the quietest first.</summary>
        private static List<Pick> Candidates(Kingdom mine)
        {
            var runRealms = new HashSet<string>(StringComparer.Ordinal) { mine.StringId };
            foreach (string r in Run.Current?.Rivals ?? Settings.RunRivals.ToList()) runRealms.Add(r);
            List<Settlement> forts = Settlement.All.Where(s => s != null && s.IsFortification && s.MapFaction != null).ToList();
            List<MobileParty> lords = MobileParty.AllLordParties.Where(p => p != null && p.IsActive && p.MapFaction != null).ToList();
            var picks = new List<Pick>();
            foreach (Hero h in Hero.AllAliveHeroes)
            {
                if (h == null || !h.IsWanderer || h.Clan != null || h.CompanionOf != null || h.PartyBelongedTo != null || h.IsPrisoner || h.IsChild || h.IsDisabled) continue;
                Settlement town = h.CurrentSettlement;
                if (town == null || !town.IsTown || town.IsUnderSiege) continue;
                IFaction owner = town.MapFaction;
                if (owner == null || runRealms.Contains(owner.StringId) || FactionManager.IsAtWarAgainstFaction(owner, mine)) continue;
                bool Hostile(IFaction f) => f != owner && f != null && (FactionManager.IsAtWarAgainstFaction(f, owner) || FactionManager.IsAtWarAgainstFaction(f, mine));
                float best = float.MaxValue;
                string nearest = null;
                var at = town.Position.ToVec2();
                foreach (Settlement s in forts)
                {
                    if (!Hostile(s.MapFaction)) continue;
                    float d = s.Position.ToVec2().Distance(at);
                    if (d < best) { best = d; nearest = Names.Settlement(s) + " (" + Names.Faction(s.MapFaction) + ")"; }
                }
                foreach (MobileParty p in lords)
                {
                    if (!Hostile(p.MapFaction)) continue;
                    float d = p.Position.ToVec2().Distance(at);
                    if (d < best) { best = d; nearest = Names.Party(p) + " (" + Names.Faction(p.MapFaction) + ")"; }
                }
                picks.Add(new Pick { Hero = h, Town = town, Quiet = best, Nearest = nearest });
            }
            return picks.OrderByDescending(p => p.Quiet).ThenBy(p => Names.Hero(p.Hero)).ToList();
        }

        /// <summary>Your kingdom's lords with a Party AI Controls order (or queue), your own character among them.</summary>
        private static List<Hero> Held(Kingdom mine)
            => MobileParty.AllLordParties.Where(p => p != null && p.IsActive && p.LeaderHero != null && p.MapFaction == mine && PacLink.HasOrders(p.LeaderHero))
                                         .Select(p => p.LeaderHero).Distinct().ToList();

        private static string Describe(Pick p)
            => Names.Hero(p.Hero) + " [" + p.Hero.StringId + "], in " + Names.Settlement(p.Town) + " (" + Names.Faction(p.Town.MapFaction) + "), "
               + (p.Nearest == null ? "with no war anywhere near" : Math.Round(p.Quiet).ToString("0", Text.Inv) + " from the nearest war, " + p.Nearest);

        private static string Listing(IEnumerable<Pick> picks)
        {
            List<string> l = picks.Select(p => Names.Hero(p.Hero) + " in " + Names.Settlement(p.Town) + " (" + (p.Nearest == null ? "no war near" : Math.Round(p.Quiet).ToString("0", Text.Inv) + " from a war") + ")").ToList();
            return l.Count > 0 ? string.Join("; ", l) : "none";
        }
    }
}
