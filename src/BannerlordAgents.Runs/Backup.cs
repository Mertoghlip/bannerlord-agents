using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using static BannerlordAgents.Runs.RunData;

namespace BannerlordAgents.Runs
{
    /// <summary>
    /// The backup (0.13.1, Oct 28: the data stays local for now, on the D: drive): a copy of everything the project
    /// records, on another drive. The bridge asks for one when a run ends, right after the run's bundle (Runs.exe bundle (run)
    /// --backup); Runs.exe backup [--to (folder)] makes one by hand. The place is "backup = (folder)" in settings.txt. Into it go:
    /// - records\: the records folder (%LOCALAPPDATA%\BannerlordAgents: every campaign's records, the hourly pictures, the logs,
    ///   the runs with their bundles, the settings);
    /// - agents\(folder)\: each agent's folder settings.txt names ("agents = ..."): its instructions, memory, channel and library.
    /// It never deletes: a file gone from its place stays in the copy. A file that only grew gets its new bytes added; one that
    /// shrank or was rewritten is copied whole, and the copy it replaces is kept under earlier\(time)\. A file another program holds
    /// shut is named and left for the next backup. backup.log in the copy has a line for each backup. It works at background
    /// priority, so the game keeps the disk. A place inside what it copies is refused, links followed (0.13.2).
    /// </summary>
    internal static class Backup
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int Window = 4096;
        private const long Margin = 1L << 30;   // room left free on the backup's drive

        private enum Act { Same, New, Grown, Rewritten }

        private sealed class Item
        {
            public string Source, Dest, Rel;   // Rel: the path inside the copy (records\..., agents\(folder)\...)
            public long Length, OldLength;
            public DateTime Written;
            public Act Act;
        }

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern bool SetThreadPriority(IntPtr thread, int priority);
        private const int BackgroundBegin = 0x00010000, BackgroundEnd = 0x00020000;

        public static int Run(string to)
        {
            string target = !string.IsNullOrWhiteSpace(to) ? to.Trim() : Setting("backup");
            if (string.IsNullOrWhiteSpace(target)) { Console.WriteLine("Backup: no place is set (\"backup = <folder>\" in " + SettingsFile + ")."); return 2; }
            target = Path.GetFullPath(target);
            List<(string Name, string Dir)> sources = Sources();
            foreach (var (_, dir) in sources)
                if (Inside(target, dir) || Inside(dir, target)) { Console.WriteLine("Backup: " + target + " is inside what it copies (" + dir + "), or holds it; choose another place."); return 2; }

            // what each file needs, from the sizes and times alone
            var items = new List<Item>();
            foreach (var (name, dir) in sources)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Files(dir))
                {
                    FileInfo fi;
                    try { fi = new FileInfo(f); if (!fi.Exists) continue; } catch (Exception) { continue; }
                    string rel = Path.Combine(name, Path.GetRelativePath(dir, f));
                    var it = new Item { Source = f, Rel = rel, Dest = Path.Combine(target, rel), Length = fi.Length, Written = fi.LastWriteTimeUtc };
                    var di = new FileInfo(it.Dest);
                    if (!di.Exists) it.Act = Act.New;
                    else
                    {
                        it.OldLength = di.Length;
                        it.Act = di.Length == it.Length && Math.Abs((di.LastWriteTimeUtc - it.Written).TotalSeconds) < 2 ? Act.Same
                               : it.Length > di.Length ? Act.Grown : Act.Rewritten;
                    }
                    items.Add(it);
                }
            }
            long need = items.Sum(i => i.Act == Act.Grown ? i.Length - i.OldLength : i.Act == Act.Same ? 0 : i.Length);
            Directory.CreateDirectory(target);
            try
            {
                long free = new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace;
                if (need + Margin > free) { Console.WriteLine("Backup: not enough room in " + target + ": it needs " + Text_.Mb(need) + " and keeps 1 GB free, and the drive has " + Text_.Mb(free) + "."); return 2; }
            }
            catch (Exception) { /* a network place: its room is not known, so it is not checked */ }

            FileStream lockFile;
            try { lockFile = new FileStream(Path.Combine(target, "backup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) { Console.WriteLine("Backup: another backup into " + target + " is running; this one is left."); return 3; }

            var count = new Dictionary<Act, int> { [Act.Same] = 0, [Act.New] = 0, [Act.Grown] = 0, [Act.Rewritten] = 0 };
            var inUse = new List<string>();
            var failed = new List<string>();
            long written = 0;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv);
            string earlier = Path.Combine(target, "earlier", stamp);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool background = false;
            try { background = SetThreadPriority(GetCurrentThread(), BackgroundBegin); } catch (Exception) { /* not on Windows: normal priority */ }
            using (lockFile)
            {
                foreach (Item it in items)
                {
                    if (it.Act == Act.Same) { count[Act.Same]++; continue; }
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(it.Dest));
                        if (it.Act == Act.Grown && !StillItsStart(it)) it.Act = Act.Rewritten;
                        written += it.Act == Act.Grown ? Append(it) : Whole(it, it.Act == Act.Rewritten ? earlier : null);
                        File.SetLastWriteTimeUtc(it.Dest, it.Written);
                        count[it.Act]++;
                    }
                    catch (IOException e) when ((e.HResult & 0xFFFF) == 32 || (e.HResult & 0xFFFF) == 33) { inUse.Add(it.Rel); }
                    catch (Exception e) { failed.Add(it.Rel + ": " + e.Message); }
                }
                if (background) SetThreadPriority(GetCurrentThread(), BackgroundEnd);

                string summary = items.Count.ToString("#,0", Inv) + " files from " + Text.N(sources.Count) + " folders; " + count[Act.New].ToString("#,0", Inv) + " new, " + count[Act.Grown].ToString("#,0", Inv) + " grown, "
                    + count[Act.Rewritten].ToString("#,0", Inv) + " rewritten" + (count[Act.Rewritten] > 0 ? " (the copies they replaced kept in earlier\\" + stamp + ")" : "") + ", " + count[Act.Same].ToString("#,0", Inv) + " unchanged; "
                    + Text_.Mb(written) + " written in " + Text.S(clock.Elapsed.TotalSeconds) + (inUse.Count > 0 ? "; " + inUse.Count + " held by another program, left for the next backup" : "") + (failed.Count > 0 ? "; " + failed.Count + " FAILED" : "");
                Console.WriteLine("Backup into " + target + ": " + summary + ".");
                foreach (string f in inUse.Take(10)) Console.WriteLine("  in use: " + f);
                if (inUse.Count > 10) Console.WriteLine("  ... and " + (inUse.Count - 10) + " more in use");
                foreach (string f in failed.Take(10)) Console.WriteLine("  ! " + f);
                try
                {
                    File.AppendAllText(Path.Combine(target, "backup.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + " | " + summary + Environment.NewLine
                        + string.Concat(inUse.Take(10).Select(f => "    in use: " + f + Environment.NewLine)) + string.Concat(failed.Take(10).Select(f => "    failed: " + f + Environment.NewLine)), new UTF8Encoding(false));
                    string readme = Path.Combine(target, "README.txt");
                    if (!File.Exists(readme)) File.WriteAllText(readme, ReadMe, new UTF8Encoding(false));
                }
                catch (Exception e) { Console.WriteLine("  ! backup.log could not be written: " + e.Message); }
            }
            return inUse.Count == 0 && failed.Count == 0 ? 0 : 1;
        }

        private const string ReadMe =
            "The backup of the Bannerlord Agents project, made by Runs.exe backup (docs/LOGGING.md, \"The backup\").\r\n\r\n"
            + "records\\          the records folder (%LOCALAPPDATA%\\BannerlordAgents) as it was at the last backup\r\n"
            + "agents\\(folder)\\  each agent's folder: its instructions, memory, channel and library of tools\r\n"
            + "earlier\\(time)\\   the older copy of every file a backup found shrunk or rewritten\r\n"
            + "backup.log        a line for each backup: when, how many files, what it wrote\r\n\r\n"
            + "A backup never deletes anything here: a file gone from its place stays.\r\n";

        /// <summary>What is copied: the records folder, then each agent's folder in settings.txt, each under its own name.</summary>
        private static List<(string Name, string Dir)> Sources()
        {
            string root = Path.GetFullPath(Root);
            var list = new List<(string, string)> { ("records", root) };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in (Setting("agents") ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                string dir;
                try { dir = Path.GetFullPath(f); } catch (Exception) { continue; }
                if (!Directory.Exists(dir) || Inside(dir, root)) continue;   // gone, or in the records folder already
                string name = Path.GetFileName(dir.TrimEnd('\\', '/')), unique = name;
                for (int i = 2; !used.Add(unique); i++) unique = name + "-" + i;
                list.Add((Path.Combine("agents", unique), dir));
            }
            return list;
        }

        /// <summary>Every file under a folder, hidden ones too, without following links, and without a copy's own .part files.</summary>
        private static IEnumerable<string> Files(string dir) =>
            Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase));

        private static bool Inside(string path, string dir)
        {
            string p = Real(path).TrimEnd('\\', '/') + "\\", d = Real(dir).TrimEnd('\\', '/') + "\\";
            return p.StartsWith(d, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Where a path really is, each link on the way followed (0.13.2): the records folder can be a link to another drive
        /// (mine is one to D:\BannerlordAgents since Oct 28), and a place inside where it really is must count as inside it.</summary>
        private static string Real(string path)
        {
            string full = Path.GetFullPath(path), at = Path.GetPathRoot(full) ?? "";
            foreach (string part in full.Substring(at.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                at = Path.Combine(at, part);
                if (!Directory.Exists(at)) continue;   // not made yet: nothing further on can be a link
                try { if (new DirectoryInfo(at).ResolveLinkTarget(true) is FileSystemInfo link) at = link.FullName; }
                catch (Exception) { /* not readable: taken as written */ }
            }
            return at;
        }

        private static string SettingsFile => Path.Combine(Root, "settings.txt");

        /// <summary>A key of settings.txt, read as the bridge reads it ("key = value", # a comment); null when it is not there.</summary>
        private static string Setting(string key)
        {
            if (!File.Exists(SettingsFile)) return null;
            foreach (string raw in ReadShared(SettingsFile).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) return line.Substring(eq + 1).Trim();
            }
            return null;
        }

        /// <summary>Whether the copy is still the file's start: its first and its last 4 KB match the file at the same places.</summary>
        private static bool StillItsStart(Item it)
        {
            using var src = new FileStream(it.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = new FileStream(it.Dest, FileMode.Open, FileAccess.Read, FileShare.Read);
            long n = dst.Length;
            if (src.Length < n) return false;
            int w = (int)Math.Min(Window, n);
            return Same(src, dst, 0, w) && Same(src, dst, n - w, w);
        }

        private static bool Same(Stream a, Stream b, long at, int count)
        {
            var x = new byte[count];
            var y = new byte[count];
            a.Seek(at, SeekOrigin.Begin);
            b.Seek(at, SeekOrigin.Begin);
            a.ReadExactly(x);
            b.ReadExactly(y);
            return x.AsSpan().SequenceEqual(y);
        }

        /// <summary>The bytes the file gained since the copy was made, added to the copy's end.</summary>
        private static long Append(Item it)
        {
            using var src = new FileStream(it.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            using var dst = new FileStream(it.Dest, FileMode.Open, FileAccess.Write, FileShare.None, 1 << 20);
            long from = dst.Length;
            src.Seek(from, SeekOrigin.Begin);
            dst.Seek(from, SeekOrigin.Begin);
            src.CopyTo(dst, 1 << 20);
            return dst.Length - from;
        }

        /// <summary>The whole file, written beside its copy as .part and swapped in at the end; the copy it replaces is kept in earlier\ first when one is given.</summary>
        private static long Whole(Item it, string earlier)
        {
            string part = it.Dest + ".part";
            using (var src = new FileStream(it.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
            using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                src.CopyTo(dst, 1 << 20);
            if (earlier != null && File.Exists(it.Dest))
            {
                string old = Path.Combine(earlier, it.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(old));
                File.Move(it.Dest, old, true);
            }
            File.Move(part, it.Dest, true);
            return new FileInfo(it.Dest).Length;
        }
    }
}
