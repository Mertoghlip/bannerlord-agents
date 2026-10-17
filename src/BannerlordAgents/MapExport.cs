using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace BannerlordAgents
{
    /// <summary>
    /// The game's walking map, copied once per map for the dashboard and the agent's "ask the way" tool (0.7, docs/ROUTES.md).
    /// Into %LOCALAPPDATA%\BannerlordAgents\map\(the walking map's checksum)\: ground.bin, the game's ground type at the centre of
    /// every map unit (row by row from the south-west corner); height.bin, the land's height every second unit, for shading;
    /// crossing_faces.json, the centre of every bridge and ford face; places.json, every holding and hideout with its gate;
    /// meta.json last, so a map without it is unfinished. Which ground can be walked comes from the game's own navigation
    /// model, and the speed model's name is kept, since a pack may change how ground slows a march.
    ///
    /// The copy is a few million questions to the map, so it runs in slices on the game's thread on the campaign map only
    /// (never in a battle): under a millisecond a frame while the clock runs, a few while it is stopped. The game asks the same
    /// questions from its own worker threads (MobileParty's ...Parallel methods), so they are safe beside its party work.
    /// Game's thread only; the files are written by the helper.
    /// </summary>
    internal static class MapExport
    {
        private enum Phase { Idle, Faces, Ground, Heights, Done, Failed }

        private static Phase _phase = Phase.Idle;
        private static IMapScene _scene;
        private static string _dir, _crc;
        private static float _x0, _y0, _cell;
        private static int _w, _h, _hw, _hh, _faces, _next;
        private static byte[] _ground;
        private static float[] _heights;
        private static List<object> _crossings;
        private static Dictionary<string, int> _faceCounts;
        private static double _gameMs;
        private static int _frames;
        private static DateTime _started, _lastProgress;
        private static readonly Stopwatch Clock = new Stopwatch();

        public const int HeightStep = 2;
        private const int MostCells = 6_000_000;

        /// <summary>For the console and the dashboard: where the copy stands.</summary>
        public static volatile string Progress = "not started";
        public static string Crc => _crc;
        public static string Folder => _dir;
        public static bool Ready => _phase == Phase.Done || (_dir != null && File.Exists(Path.Combine(_dir, "meta.json")));

        /// <summary>At each campaign start: the map's places are written, and the copy begins unless this map was copied before (or redo).</summary>
        public static void Start(bool redo = false)
        {
            _phase = Phase.Idle;
            _ground = null;
            _heights = null;
            if (!Settings.Dashboard) { Progress = "off (dashboard = off)"; return; }
            try
            {
                _scene = Campaign.Current?.MapSceneWrapper;
                if (_scene == null) { Progress = "no map scene"; return; }
                _crc = _scene.GetSceneNavigationMeshCrc().ToString("x8");
                _dir = Path.Combine(Paths.Root, "map", _crc);
                Directory.CreateDirectory(_dir);
                WritePlaces();
                if (!redo && File.Exists(Path.Combine(_dir, "meta.json")))
                {
                    Progress = "ready (copied before): " + _dir;
                    _phase = Phase.Done;
                    return;
                }
                _scene.GetMapBorders(out Vec2 min, out Vec2 max, out float _);
                float width = max.x - min.x, height = max.y - min.y;
                if (width <= 1 || height <= 1) { Fail("the map's borders are empty (" + min + " to " + max + ")"); return; }
                _cell = 1f;
                while ((width / _cell) * (height / _cell) > MostCells) _cell += 0.5f;
                _x0 = min.x;
                _y0 = min.y;
                _w = (int)Math.Ceiling(width / _cell);
                _h = (int)Math.Ceiling(height / _cell);
                _hw = (_w + HeightStep - 1) / HeightStep;
                _hh = (_h + HeightStep - 1) / HeightStep;
                _faces = _scene.GetNumberOfNavigationMeshFaces();
                _ground = new byte[_w * _h];
                _heights = new float[_hw * _hh];
                _crossings = new List<object>();
                _faceCounts = new Dictionary<string, int>();
                _next = 0;
                _gameMs = 0;
                _frames = 0;
                _started = DateTime.Now;
                _phase = Phase.Faces;
                Progress = "copying the walking map: " + _w + " x " + _h + " cells of " + _cell.ToString("0.0", Text.Inv) + " units, " + _faces + " faces";
                Log.Line("map: " + Progress + " (checksum " + _crc + ")");
                Diag.Event("map", "copy started", new { crc = _crc, width = _w, height = _h, cell = _cell, faces = _faces });
            }
            catch (Exception e) { Fail("starting the copy: " + e.Message, e); }
        }

        /// <summary>Every holding and hideout with where it stands and its gate (where parties march to).</summary>
        private static void WritePlaces()
        {
            var places = new List<object>();
            foreach (Settlement s in Settlement.All)
            {
                if (s == null) continue;
                string kind = s.IsTown ? "town" : s.IsCastle ? "castle" : s.IsVillage ? "village" : s.IsHideout ? "hideout" : "other";
                CampaignVec2 g = s.GatePosition;
                places.Add(new { Id = s.StringId, Name = Names.Settlement(s), Kind = kind, X = Text.R1(s.Position.X), Y = Text.R1(s.Position.Y), Gx = Text.R1(g.X), Gy = Text.R1(g.Y) });
            }
            string path = Path.Combine(_dir, "places.json");
            Background.Post("map places", () => Files.Overwrite(path, Json.Line(places)));
        }

        /// <summary>Every frame (OnApplicationTick): the next slice of the copy, on the campaign map only.</summary>
        public static void Frame()
        {
            if (_phase != Phase.Faces && _phase != Phase.Ground && _phase != Phase.Heights) return;
            if (!Bridge.Running || Campaign.Current == null || Mission.Current != null || !(GameStateManager.Current?.ActiveState is MapState)) return;
            double budget = Campaign.Current.TimeControlMode == CampaignTimeControlMode.Stop ? 4.0 : 0.8;
            long limit = (long)(budget * Stopwatch.Frequency / 1000.0);
            Clock.Restart();
            try
            {
                int k = 0;
                while (true)
                {
                    if ((++k & 31) == 0 && Clock.ElapsedTicks >= limit) break;
                    if (!Step()) break;
                }
            }
            catch (Exception e) { Fail("copying the walking map: " + e.Message, e); }
            _gameMs += Clock.Elapsed.TotalMilliseconds;
            _frames++;
            if ((DateTime.Now - _lastProgress).TotalSeconds >= 2 && _phase != Phase.Done && _phase != Phase.Failed)
            {
                _lastProgress = DateTime.Now;
                double share = _phase == Phase.Faces ? (double)_next / Math.Max(1, _faces) * 0.1
                             : _phase == Phase.Ground ? 0.1 + (double)_next / _ground.Length * 0.75
                             : 0.85 + (double)_next / _heights.Length * 0.15;
                Progress = "copying the walking map: " + Math.Round(share * 100) + "% (" + _phase.ToString().ToLowerInvariant() + ")";
                string text = Progress, dir = _dir;
                Background.Post("map progress", () => Files.Overwrite(Path.Combine(dir, "progress.json"), Json.Line(new { Progress = text, Share = Math.Round(share, 3), At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") })));
            }
        }

        /// <summary>One question to the map; false when the phase is over (or the copy is).</summary>
        private static bool Step()
        {
            switch (_phase)
            {
                case Phase.Faces:
                    if (_next >= _faces) { _phase = Phase.Ground; _next = 0; return true; }
                    {
                        PathFaceRecord f = _scene.GetFaceAtIndex(_next);
                        string t = f.FaceGroupIndex.ToString(Text.Inv);
                        _faceCounts[t] = (_faceCounts.TryGetValue(t, out int c) ? c : 0) + 1;
                        if (f.FaceGroupIndex == (int)TerrainType.Bridge || f.FaceGroupIndex == (int)TerrainType.Fording)
                        {
                            Vec2 at = _scene.GetNavigationMeshCenterPosition(_next);
                            _crossings.Add(new { X = Text.R1(at.x), Y = Text.R1(at.y), T = f.FaceGroupIndex });
                        }
                    }
                    _next++;
                    return true;
                case Phase.Ground:
                    if (_next >= _ground.Length) { _phase = Phase.Heights; _next = 0; return true; }
                    {
                        int cx = _next % _w, cy = _next / _w;
                        var at = new CampaignVec2(new Vec2(_x0 + (cx + 0.5f) * _cell, _y0 + (cy + 0.5f) * _cell), true);
                        PathFaceRecord f = _scene.GetFaceIndex(in at);
                        _ground[_next] = f.IsValid() && f.FaceGroupIndex > 0 && f.FaceGroupIndex < 256 ? (byte)f.FaceGroupIndex : (byte)0;
                    }
                    _next++;
                    return true;
                case Phase.Heights:
                    if (_next >= _heights.Length) { Finish(); return false; }
                    {
                        int cx = _next % _hw, cy = _next / _hw;
                        _scene.GetTerrainHeightAndNormal(new Vec2(_x0 + (cx * HeightStep + 1f) * _cell, _y0 + (cy * HeightStep + 1f) * _cell), out float height, out Vec3 _);
                        _heights[_next] = height;
                    }
                    _next++;
                    return true;
                default:
                    return false;
            }
        }

        private static void Finish()
        {
            _phase = Phase.Done;
            double seconds = (DateTime.Now - _started).TotalSeconds;
            var walkable = new List<int>();
            PartyNavigationModel nav = Campaign.Current.Models.PartyNavigationModel;
            foreach (TerrainType t in Enum.GetValues(typeof(TerrainType)))
            {
                try { if (nav.IsTerrainTypeValidForNavigationType(t, MobileParty.NavigationType.Default)) walkable.Add((int)t); }
                catch (Exception e) { Diag.Swallowed("asking the navigation model about " + t, e); }
            }
            float hmin = _heights.Length > 0 ? _heights.Min() : 0, hmax = _heights.Length > 0 ? _heights.Max() : 0;
            var meta = new
            {
                Version = 1, Crc = _crc, MinX = _x0, MinY = _y0, MaxX = _x0 + _w * _cell, MaxY = _y0 + _h * _cell, Cell = _cell, Width = _w, Height = _h, Faces = _faces,
                Walkable = walkable, SunRise = CampaignTime.SunRise, SunSet = CampaignTime.SunSet,
                SpeedModel = Campaign.Current.Models.PartySpeedCalculatingModel?.GetType().FullName, NavigationModel = nav?.GetType().FullName,
                HeightStep, HeightWidth = _hw, HeightHeight = _hh, HeightMin = hmin, HeightMax = hmax,
                Made = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), GameMs = Math.Round(_gameMs, 1), Seconds = Math.Round(seconds, 1), FaceCounts = _faceCounts
            };
            byte[] ground = _ground;
            float[] heights = _heights;
            List<object> crossings = _crossings;
            string dir = _dir;
            int frames = _frames, w = _w, h = _h;
            Progress = "ready: " + dir;
            Background.Post("map files", () =>
            {
                try
                {
                    WriteBytes(Path.Combine(dir, "ground.bin"), ground);
                    var raw = new byte[heights.Length * 4];
                    Buffer.BlockCopy(heights, 0, raw, 0, raw.Length);
                    WriteBytes(Path.Combine(dir, "height.bin"), raw);
                    Files.Overwrite(Path.Combine(dir, "crossing_faces.json"), Json.Line(crossings));
                    Files.Overwrite(Path.Combine(dir, "meta.json"), Json.Line(meta));
                    int walk = 0;
                    var walkSet = new HashSet<int>(walkable);
                    foreach (byte b in ground) if (walkSet.Contains(b)) walk++;
                    Log.Line("map: the walking map is copied (" + dir + "): " + w + " x " + h + " cells, " + Text.N(walk) + " walkable, " + crossings.Count + " bridge and ford faces; "
                             + seconds.ToString("0", Text.Inv) + " s, " + meta.GameMs.ToString("0", Text.Inv) + " ms of the game's thread over " + frames + " frames");
                    Diag.Event("map", "copied", new { dir, width = w, height = h, walkable = walk, crossings = crossings.Count, seconds = Math.Round(seconds, 1), game_ms = meta.GameMs, frames });
                }
                catch (Exception e) { Diag.Error("writing the walking map", e); }
            });
            _ground = null;
            _heights = null;
        }

        private static void WriteBytes(string path, byte[] data)
        {
            string tmp = path + ".part";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void Fail(string what, Exception e = null)
        {
            _phase = Phase.Failed;
            Progress = "failed: " + what;
            _ground = null;
            _heights = null;
            if (e != null) Diag.Error("the walking map", e);
            Diag.Alarm("map_copy_failed", "The walking map for the dashboard could not be copied: " + what + ". The dashboard shows the parties on a blank map, and the agent's \"ask the way\" tool has no map.");
        }
    }
}
