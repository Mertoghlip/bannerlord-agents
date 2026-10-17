using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BannerlordAgents.Routes;

namespace BannerlordAgents.Dashboard
{
    /// <summary>
    /// The dashboard's web server, on http://localhost:port/ only: the page (embedded, or from --web), and the readings it asks for.
    /// GET /api/status, /api/frames, /api/frame (the bridge's gzipped frame as it is), /api/map/meta, /api/map/picture, /api/atlas,
    /// /api/route (our way between two points), /api/log (the agent's log for a load), /api/checks; POST /api/way (a way given by
    /// its corners, described). Nothing here writes where the game or the agent reads.
    /// </summary>
    internal sealed class Server
    {
        private readonly Store _store;
        private readonly Checks _checks;
        private readonly LogReader _log;
        private readonly int _port;
        private readonly string _webDir;
        private HttpListener _http;

        public Server(Store store, Checks checks, int port, string webDir)
        {
            _store = store;
            _checks = checks;
            _log = new LogReader(store);
            _port = port;
            _webDir = webDir;
        }

        public bool Start(out string why)
        {
            why = null;
            _http = new HttpListener();
            _http.Prefixes.Add("http://localhost:" + _port + "/");
            try { _http.Start(); }
            catch (HttpListenerException e)
            {
                why = "The dashboard could not listen on http://localhost:" + _port + "/ (" + e.Message + "). Another program may hold the port: try --port 7330.";
                return false;
            }
            return true;
        }

        public void Serve(CancellationToken stop)
        {
            stop.Register(() => { try { _http.Stop(); } catch (ObjectDisposedException) { } });
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = _http.GetContext(); }
                catch (HttpListenerException) { break; }
                catch (InvalidOperationException) { break; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
            Log.Say("Stopped.");
        }

        private void Handle(HttpListenerContext ctx)
        {
            HttpListenerRequest q = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            try
            {
                string path = q.Url?.AbsolutePath ?? "/";
                res.Headers["Cache-Control"] = "no-store";
                if (path == "/" || path == "/index.html") { File(res, "index.html"); return; }
                if (!path.StartsWith("/api/", StringComparison.Ordinal)) { File(res, path.TrimStart('/')); return; }
                switch (path)
                {
                    case "/api/status": Json(res, Status()); return;
                    case "/api/frames": Json(res, Frames(q.QueryString["campaign"], q.QueryString["load"])); return;
                    case "/api/frame": Frame(res, q.QueryString["campaign"], q.QueryString["load"], q.QueryString["h"]); return;
                    case "/api/map/meta": MapMeta(res, q.QueryString["crc"]); return;
                    case "/api/map/picture": MapPicture(res, q.QueryString["crc"]); return;
                    case "/api/atlas": AtlasJson(res, q.QueryString["crc"]); return;
                    case "/api/route": Route(res, q); return;
                    case "/api/way": Way(res, q); return;
                    case "/api/log": LogJson(res, q.QueryString["campaign"], q.QueryString["load"], q.QueryString["realm"]); return;
                    case "/api/checks": Json(res, _checks.Summary()); return;
                    default: Error(res, 404, "no such reading: " + path); return;
                }
            }
            catch (Exception e)
            {
                Log.Say("Request " + q.Url?.PathAndQuery + " failed: " + e.Message);
                try { Error(res, 500, e.Message); } catch { }
            }
            finally { try { res.Close(); } catch { } }
        }

        // ---------- readings ----------

        private object Status()
        {
            JsonObject cur = _store.Current();
            string campaign = (string)cur?["campaign"], load = (string)cur?["load_id"], crc = (string)cur?["map"];
            string mapDir = crc != null ? Path.Combine(_store.Root, "map", crc) : null;
            JsonNode progress = null;
            try { if (mapDir != null) progress = Store.ReadJson(Path.Combine(mapDir, "progress.json")); } catch (Exception e) when (e is IOException || e is JsonException) { }
            WalkMap m = _store.Map(crc);
            DateTime written = cur != null && DateTime.TryParse((string)cur["written"], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime w) ? w : DateTime.MinValue;
            return new
            {
                version = typeof(Server).Assembly.GetName().Version.ToString(3),
                current = cur,
                seconds_since_frame = written == DateTime.MinValue ? (double?)null : Math.Round((DateTime.Now - written).TotalSeconds, 1),
                campaigns = _store.Campaigns(),
                loads = campaign == null ? null : _store.Loads(campaign).Select(l => new { load_id = l.LoadId, first = l.Hours.FirstOrDefault(), last = l.Hours.LastOrDefault(), frames = l.Hours.Count, wall = l.Wall, realm = l.Realm }).ToList(),
                map = new { crc = m?.Meta.Crc ?? crc, ready = m != null, progress = progress?["progress"]?.ToString() ?? (m != null ? "ready" : "not copied yet: load a campaign with the dashboard on") },
                atlas = _checks.AtlasProgress
            };
        }

        private object Frames(string campaign, string load) => new { campaign, load, hours = _store.FrameHours(campaign, load) };

        private void Frame(HttpListenerResponse res, string campaign, string load, string h)
        {
            JsonObject cur = _store.Current();
            campaign ??= (string)cur?["campaign"];
            load ??= (string)cur?["load_id"];
            if (campaign == null || load == null) { Error(res, 404, "no frames yet: load a campaign with the dashboard on"); return; }
            List<long> hours = _store.FrameHours(campaign, load);
            if (hours.Count == 0) { Error(res, 404, "this load has no frames"); return; }
            long hour = h == null || h == "latest" ? hours[hours.Count - 1]
                      : long.TryParse(h, out long x) ? hours.OrderBy(v => Math.Abs(v - x)).First() : hours[hours.Count - 1];
            string path = _store.FramePath(campaign, load, hour);
            byte[] data;
            try { data = ReadAll(path); }
            catch (IOException) { Error(res, 503, "the frame is being written; ask again"); return; }
            res.ContentType = "application/json; charset=utf-8";
            res.Headers["Content-Encoding"] = "gzip";
            res.Headers["X-Frame-Hour"] = hour.ToString(CultureInfo.InvariantCulture);
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
        }

        private void MapMeta(HttpListenerResponse res, string crc)
        {
            WalkMap m = _store.Map(crc);
            if (m == null) { Error(res, 404, "the walking map is not copied yet"); return; }
            Json(res, new { meta = m.Meta, places = m.Places, picture = "/api/map/picture?crc=" + m.Meta.Crc });
        }

        private void MapPicture(HttpListenerResponse res, string crc)
        {
            WalkMap m = _store.Map(crc);
            if (m == null) { Error(res, 404, "the walking map is not copied yet"); return; }
            string path;
            lock (typeof(Picture)) path = Picture.Ensure(m);
            byte[] data = ReadAll(path);
            res.ContentType = "image/png";
            res.Headers["Cache-Control"] = "max-age=86400";
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
        }

        private void AtlasJson(HttpListenerResponse res, string crc)
        {
            WalkMap m = _store.Map(crc);
            Atlas a = m == null ? null : _store.Atlas(m);
            if (a == null) { Json(res, new { ready = false, progress = _checks.AtlasProgress }); return; }
            Json(res, new
            {
                ready = true, a.Made, a.Seconds, a.Places, a.WaysFound,
                crossings = a.Crossings.Select(c => new { c.Id, c.Kind, c.X, c.Y, c.Width, c.Name, c.Ways, c.Detour, c.CutsOff, c.Critical, closing = Explain.Closing(c), c.Examples })
            });
        }

        private void Route(HttpListenerResponse res, HttpListenerRequest q)
        {
            WalkMap m = _store.Map(q.QueryString["crc"]);
            if (m == null) { Error(res, 404, "the walking map is not copied yet"); return; }
            float fx = F(q.QueryString["fx"]), fy = F(q.QueryString["fy"]), tx = F(q.QueryString["tx"]), ty = F(q.QueryString["ty"]);
            Costs costs = Costs.ByName(q.QueryString["costs"]);
            RouteResult r = _store.WithFinder(m, f => f.Find(fx, fy, tx, ty, costs));
            Json(res, Describe(m, r, F(q.QueryString["pace"]), D(q.QueryString["hours"]), q.QueryString["pace_from"] ?? "its pace"));
        }

        private void Way(HttpListenerResponse res, HttpListenerRequest q)
        {
            JsonObject body;
            using (var sr = new StreamReader(q.InputStream, Encoding.UTF8)) body = JsonNode.Parse(sr.ReadToEnd()) as JsonObject;
            WalkMap m = _store.Map((string)body?["crc"]);
            if (m == null || body?["points"] is not JsonArray arr) { Error(res, 404, "no walking map, or no points"); return; }
            var pts = arr.OfType<JsonArray>().Where(p => p.Count >= 2).Select(p => new[] { (float)(double)p[0], (float)(double)p[1] }).ToList();
            RouteResult r = _store.WithFinder(m, f => f.FromPoints(pts));
            float pace = body["pace"] is JsonValue pv && pv.TryGetValue(out double pd) ? (float)pd : 0;
            double hours = body["hours"] is JsonValue hv && hv.TryGetValue(out double hd) ? hd : double.NaN;
            Json(res, Describe(m, r, pace, hours, (string)body["pace_from"] ?? "its pace"));
        }

        private object Describe(WalkMap m, RouteResult r, float pace, double hours, string paceFrom)
        {
            Atlas atlas = _store.Atlas(m);
            SpeedFactors sf = _store.Speeds(m);
            GameCalendar cal = GameCalendar.From(_store.Current()?["calendar"]);
            List<string> lines = Explain.Way(m, atlas, r, pace, paceFrom, double.IsNaN(hours) ? 0 : hours, sf, cal.When, out double h, out double arrive);
            float total = r.OnGround.Values.Sum();
            return new
            {
                ok = r.Ok, why = r.Why, points = r.Points, length = Math.Round(r.Length, 1), straight = Math.Round(r.Straight, 1),
                hours = double.IsNaN(h) ? (double?)null : Math.Round(h, 1), arrive = double.IsNaN(arrive) || double.IsNaN(hours) ? (double?)null : arrive,
                arrive_text = double.IsNaN(arrive) || double.IsNaN(hours) ? null : cal.When(arrive),
                ground = total <= 0 ? null : r.OnGround.OrderByDescending(kv => kv.Value).Select(kv => new { name = Ground.Name(kv.Key), share = Math.Round(kv.Value / total, 3) }).ToList(),
                crossings = Explain.Crossings(m, atlas, r).Select(x => new { kind = x.Kind, name = x.Name, x = x.X, y = x.Y, at = Math.Round(x.At, 1), width = Math.Round(x.Width, 1), critical = x.Atlas?.Critical ?? false, closing = Explain.Closing(x.Atlas) }).ToList(),
                narrow = float.IsInfinity(r.NarrowWidth) ? null : new { width = Math.Round(r.NarrowWidth, 1), x = r.NarrowX, y = r.NarrowY, where = m.Describe(r.NarrowX, r.NarrowY) },
                lines, ms = Math.Round(r.Ms), speeds = sf.Source
            };
        }

        private void LogJson(HttpListenerResponse res, string campaign, string load, string realm)
        {
            JsonObject cur = _store.Current();
            campaign ??= (string)cur?["campaign"];
            load ??= (string)cur?["load_id"];
            LoadInfo li = campaign == null ? null : _store.Load(campaign, load);
            realm ??= li?.Realm ?? (string)cur?["agent"];
            List<LogEntry> entries = campaign == null ? new List<LogEntry>() : _log.Read(campaign, li, realm);
            Json(res, new { campaign, load, realm, entries });
        }

        // ---------- plumbing ----------

        private static readonly JsonSerializerOptions Out = new JsonSerializerOptions(JsonText.Options) { IncludeFields = true };

        private static void Json(HttpListenerResponse res, object o)
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(o, Out);
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
        }

        private static void Error(HttpListenerResponse res, int code, string message)
        {
            res.StatusCode = code;
            Json(res, new { error = message });
        }

        private void File(HttpListenerResponse res, string name)
        {
            if (name.Contains("..")) { Error(res, 404, "no such file"); return; }
            byte[] data = null;
            if (_webDir != null)
            {
                string p = Path.Combine(_webDir, name);
                if (System.IO.File.Exists(p)) data = System.IO.File.ReadAllBytes(p);
            }
            if (data == null)
            {
                using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + name);
                if (s != null) { using var ms = new MemoryStream(); s.CopyTo(ms); data = ms.ToArray(); }
            }
            if (data == null) { Error(res, 404, "no such file: " + name); return; }
            res.ContentType = name.EndsWith(".js") ? "text/javascript; charset=utf-8" : name.EndsWith(".css") ? "text/css; charset=utf-8"
                            : name.EndsWith(".svg") ? "image/svg+xml" : name.EndsWith(".html") ? "text/html; charset=utf-8" : "application/octet-stream";
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
        }

        private static byte[] ReadAll(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }

        private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        private static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }
}
