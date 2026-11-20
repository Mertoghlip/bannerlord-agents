using System;
using System.Collections.Generic;
using System.IO;
using BannerlordAgents;
using BannerlordAgents.Common;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.21.6 (Nov 20, runs 20261120-0212-A to -1102-A): with the game's own ways, a place with no way to it over land (the far west beyond the sea, Orthanc's
        /// closed ring) gave "never" for the lord and for every enemy, and "never" against "never" read as "he gets there first": 41 of 83 sheltering lords were sent
        /// over 100 map units, Naltûr and Throrgash from Imnagath to Orthanc Gate and Blue Craig, 400 and 565 away. The safety rule now takes only a refuge the lord
        /// has a way to within a day's march (World.RefugeHours), and a lord running in the open treats a point he has no way to as the end of that direction.
        /// </summary>
        private static void RefugeTests()
        {
            double D(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

            // running in the open with water (no way) to his west and one enemy to his east: not into the water
            Func<double, double, double> mine = (px, py) => px < -2 ? double.PositiveInfinity : D(0, 0, px, py) / 2;
            var foe = new List<Func<double, double, double>> { (px, py) => D(10, 0, px, py) / 2.5 };
            (double wx, double wy) = Runner.EscapePoint(0, 0, mine, foe, 16);
            Check(wx >= -2.01 && D(10, 0, wx, wy) > 10, "a lord running from an enemy to his east, with no way into the water to his west, runs along the shore, not into it ("
                  + wx.ToString("0.0") + ", " + wy.ToString("0.0") + ")");

            Snapshot Land(params SettlementRow[] forts)
            {
                var s = new Snapshot { Campaign = "smoketest", Hours = 50000, Date = "Summer 9, 1085", PlayerFaction = "player_k", PlayerParty = "main_party", Watched = "orcs" };
                s.Factions["orcs"] = new FactionRow { Id = "orcs", Name = "Misty Mountain Orcs", Ruler = "Azog", AtWar = { "player_k" } };
                s.Factions["player_k"] = new FactionRow { Id = "player_k", Name = "Kingdom of Arnor and Gondor", Ruler = "Aragorn", AtWar = { "orcs" } };
                s.FactionNames["orcs"] = "Misty Mountain Orcs";
                s.FactionNames["player_k"] = "Kingdom of Arnor and Gondor";
                s.HostileToWatched.Add("player_k");
                s.HostileToPlayer.Add("orcs");
                foreach (SettlementRow f in forts) Add(s, f);
                s.WatchedSees = new Dictionary<string, Sighting>();
                s.InspectedByWatched = new HashSet<string>();
                foreach (SettlementRow f in forts) s.InspectedByWatched.Add(f.Id);
                return s;
            }
            SettlementRow Fort(string id, string name, float x, float y) => new SettlementRow { Id = id, Name = name, Kind = "castle", Faction = "orcs", X = x, Y = y, Garrison = 3000, GarrisonStrength = 6000, Militia = 300, WallLevel = 3 };
            PartyRow Lord(string id, string faction, float x, float y, float strength) => new PartyRow { Id = id, Leader = "l_" + id, Name = id, Faction = faction, Kind = "lord", Men = (int)(strength / 2), Strength = strength, Speed = 5, FoodDays = 10, X = x, Y = y, Doing = "Hold", Spottable = true };
            SettlementRow Choose(Snapshot s, PartyRow me, PartyRow danger, out bool running)
            {
                Add(s, me);
                Add(s, danger);
                s.WatchedSees[danger.Id] = new Sighting { Party = danger.Id, By = me.Name, Distance = 10f };
                var w = new BannerlordAgents.Tools.World(s, "orcs", s.WatchedSees, new Dictionary<string, ViewParty>());
                return w.ChooseRefuge(me.Id, null, BannerlordAgents.Tools.World.HoldRadius, danger, out _, out running, danger.Strength, new HashSet<string> { danger.Id });
            }

            // a day's march (the straight line, before the game's own ways): a castle 200 away (about 48 hours) is no refuge; one 10 away is
            SettlementRow far = Choose(Land(Fort("castle_far", "Far Keep", 100, 100)), Lord("p_grub", "orcs", 300, 100, 150), Lord("p_en", "player_k", 312, 100, 2000), out bool ranFar);
            SettlementRow near = Choose(Land(Fort("castle_far", "Far Keep", 100, 100), Fort("castle_near", "Near Keep", 290, 100)), Lord("p_grub", "orcs", 300, 100, 150), Lord("p_en", "player_k", 312, 100, 2000), out _);
            Check(far == null && near?.Id == "castle_near", "the safety rule takes a refuge within a day's march only: a castle about 48 hours away is none (" + (far?.Id ?? "none")
                  + (ranFar ? ", running" : "") + "), one about 2 hours away is (" + (near?.Id ?? "none") + ")");

            // no way over land: a three-by-three mesh of 10-unit squares whose east column is another island, with the game's own ways switched on
            string dir = Path.Combine(Path.GetTempPath(), "BannerlordAgents-smoketest-refuge");
            try
            {
                Directory.CreateDirectory(dir);
                RefugeGrid().Save(Path.Combine(dir, "navmesh.bin"));
                File.WriteAllText(Path.Combine(dir, "navcopy-proof.json"), "{\"copy_version\":" + NavCopy.Version + ",\"rate\":1.0}");
                Ways.Load(dir);
                bool proven = Ways.Proven && Ways.Between(5, 5, 25, 5, out _) == null && Ways.Between(5, 5, 5, 15, out _) != null;
                // the only castle is across the water: none for either side, so no refuge (was: taken, "never" against "never")
                SettlementRow across = Choose(Land(Fort("castle_isle", "Isle Keep", 25, 5)), Lord("p_grub", "orcs", 5, 5, 150), Lord("p_en", "player_k", 15, 25, 2000), out bool ranAcross);
                // a castle on his own land is still taken
                SettlementRow home = Choose(Land(Fort("castle_isle", "Isle Keep", 25, 5), Fort("castle_home", "Home Keep", 5, 15)), Lord("p_grub", "orcs", 5, 5, 150), Lord("p_en", "player_k", 15, 25, 2000), out _);
                Check(proven && across == null && home?.Id == "castle_home", "with the game's own ways, a castle he has no way to over land is never his refuge (" + (across?.Id ?? "none") + (ranAcross ? ", running" : "")
                      + "); one on his own land is (" + (home?.Id ?? "none") + ")" + (proven ? "" : " (the made-up mesh did not load as proven)"));
            }
            finally
            {
                Ways.Load(null);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        /// <summary>A three-by-three walking mesh of 10-unit squares, all open, its east column (x 20 to 30) a separate island.</summary>
        private static NavMesh RefugeGrid()
        {
            var vx = new List<float>(); var vy = new List<float>(); var vf = new List<byte>();
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) { vx.Add(x * 10); vy.Add(y * 10); vf.Add((byte)(x == 0 || y == 0 || x == 3 || y == 3 ? 0x80 : 0)); }
            int V(int x, int y) => y * 4 + x;
            var ea = new List<int>(); var eb = new List<int>(); var edges = new Dictionary<long, int>();
            int E(int a, int b)
            {
                long key = Math.Min(a, b) * 1000L + Math.Max(a, b);
                if (edges.TryGetValue(key, out int k)) return k;
                edges[key] = ea.Count; ea.Add(a); eb.Add(b);
                return ea.Count - 1;
            }
            int n = 9;
            var m = new NavMesh
            {
                Crc = "test", FN = new byte[n], FEdge = new int[n * 4], FNbr = new int[n * 4], FCost = new float[n], FD8 = new float[n], FE4 = new float[n], FIsland = new int[n], FGroup = new int[n],
                FAbility = new int[n], FSpecialEdge = new int[n], FOneWay = new byte[n], FFlags = new byte[n], FLinkStart = new int[n + 1], FLinks = new int[0], RadiusOn = true, AbilityMask = 1,
                GroupRegion = new byte[8], PartyExcluded = new[] { 5 }
            };
            int F(int x, int y) => y * 3 + x;
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    int f = F(x, y);
                    m.FN[f] = 4;
                    int[] e = { E(V(x, y), V(x + 1, y)), E(V(x + 1, y), V(x + 1, y + 1)), E(V(x + 1, y + 1), V(x, y + 1)), E(V(x, y + 1), V(x, y)) };
                    int[] nb = { y > 0 ? F(x, y - 1) : -1, x < 2 ? F(x + 1, y) : -1, y < 2 ? F(x, y + 1) : -1, x > 0 ? F(x - 1, y) : -1 };
                    for (int k = 0; k < 4; k++) { m.FEdge[f * 4 + k] = e[k]; m.FNbr[f * 4 + k] = nb[k]; }
                    m.FAbility[f] = 1; m.FFlags[f] = 1; m.FSpecialEdge[f] = -1; m.FGroup[f] = 1;
                    m.FIsland[f] = x == 2 ? 1 : 0;
                }
            for (int f = 0; f <= n; f++) m.FLinkStart[f] = 0;
            m.VX = vx.ToArray(); m.VY = vy.ToArray(); m.VFlags = vf.ToArray(); m.EA = ea.ToArray(); m.EB = eb.ToArray(); m.EKind = new byte[ea.Count];
            for (int f = 0; f < n; f++) for (int k = 0; k < 4; k++) m.EKind[m.FEdge[f * 4 + k]] = (byte)(m.FNbr[f * 4 + k] >= 0 ? 2 : 1);
            return m;
        }
    }
}
