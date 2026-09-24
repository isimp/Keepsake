using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace Keepsake
{
    /// <summary>A file or folder under BepInEx/config kept through profile syncs.</summary>
    public sealed class KeptPath
    {
        /// <summary>Relative to BepInEx/config, with forward slashes and no slash at the end.</summary>
        public string Path;

        public bool IsFolder;

        /// <summary>Whether this entry covers a file or folder at the given relative path.</summary>
        public bool Covers(string path) =>
            string.Equals(Path, path, StringComparison.OrdinalIgnoreCase) ||
            IsFolder && path.StartsWith(Path + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What one launch did with the kept files.</summary>
    public sealed class SettleResult
    {
        /// <summary>Whether the last game was one Keepsake saw close.</summary>
        public bool Clean;

        /// <summary>Files your copy went back into: replaced or removed by a sync, or put back as you chose.</summary>
        public int PutBack;

        /// <summary>Copies brought up to date from files a mod wrote that Keepsake had not copied yet.</summary>
        public int Updated;

        /// <summary>Files left as they are, waiting for you to choose between them and your copy.</summary>
        public int Waiting;
    }

    /// <summary>
    /// Files a mod keeps its state in, such as a timer per world, kept the way settings are.
    /// As the game closes, the plugin copies each kept file into BepInEx/keepsake-files, and at
    /// launch the preloader puts a copy back wherever a profile sync replaced or removed the file,
    /// before any mod reads it. See Settle.
    ///
    /// A sync uploads everything under config and every cfg, txt, json, yml, yaml and ini file
    /// anywhere in the profile. So the copies sit outside config and each carries a .kept ending,
    /// and the list of kept paths, BepInEx/keepsake.files, has an extension of its own.
    /// </summary>
    public static class KeptFiles
    {
        public const string Version = "# keepsake files v1";

        private const string VersionPrefix = "# keepsake files v";

        /// <summary>Added to every copy, so no sync picks it up whatever the file was.</summary>
        public const string CopySuffix = ".kept";

        private static readonly string[] Header =
        {
            Version,
            "# Files and folders in BepInEx/config that Keepsake keeps through profile syncs, one",
            "# per line, relative to BepInEx/config. A folder ends with a slash. Copies are kept in",
            "# BepInEx/keepsake-files.",
        };

        public static string ListPath => System.IO.Path.Combine(Paths.BepInExRootPath, "keepsake.files");

        public static string StoreRoot => System.IO.Path.Combine(Paths.BepInExRootPath, "keepsake-files");

        public static string Live(string path) =>
            System.IO.Path.Combine(Paths.ConfigPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public static string Copy(string path) =>
            System.IO.Path.Combine(StoreRoot, path.Replace('/', System.IO.Path.DirectorySeparatorChar)) + CopySuffix;

        // ---------- the list ----------

        /// <summary>
        /// A path as the list stores it, or null for one that is empty or would leave
        /// BepInEx/config, which a hand edit could otherwise point anywhere on the disk.
        /// </summary>
        public static string Normalise(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var parts = path.Trim().Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Any(p => p == "." || p == ".." || p.Contains(":"))) return null;
            if (path.Trim().StartsWith("/") || path.Trim().StartsWith("\\")) return null;
            return string.Join("/", parts);
        }

        /// <summary>
        /// Every kept path. A missing list is none. Null for one that cannot be read or is of
        /// another version, which is then never written over.
        /// </summary>
        public static List<KeptPath> Read()
        {
            string[] lines;
            try
            {
                if (!File.Exists(ListPath)) return new List<KeptPath>();
                lines = File.ReadAllLines(ListPath);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not read {System.IO.Path.GetFileName(ListPath)}: {ex.Message}");
                return null;
            }

            var kept = Parse(lines);
            if (kept == null)
                PinFile.Log?.LogWarning($"Keepsake: {System.IO.Path.GetFileName(ListPath)} was written by a newer Keepsake, so it is left as it is.");
            return kept;
        }

        /// <summary>The kept paths in the lines of the list, or null for a list of another version.</summary>
        public static List<KeptPath> Parse(IEnumerable<string> lines)
        {
            var kept = new List<KeptPath>();
            foreach (var raw in lines)
            {
                if (raw.StartsWith(VersionPrefix) && raw.Trim() != Version) return null;
                if (raw.Trim().Length == 0 || raw.StartsWith("#")) continue;

                var line = raw.Trim();
                var path = Normalise(line);
                if (path == null)
                {
                    PinFile.Log?.LogWarning($"Keepsake: skipped {line} in keepsake.files, which is not a path inside BepInEx/config.");
                    continue;
                }

                if (kept.Any(k => string.Equals(k.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                kept.Add(new KeptPath { Path = path, IsFolder = line.EndsWith("/") || line.EndsWith("\\") });
            }
            return kept;
        }

        public static string[] Format(IEnumerable<KeptPath> kept)
        {
            var lines = new List<string>(Header);
            lines.AddRange(kept
                .OrderBy(k => k.Path, StringComparer.OrdinalIgnoreCase)
                .Select(k => k.IsFolder ? k.Path + "/" : k.Path));
            return lines.ToArray();
        }

        /// <summary>Writes the list, or removes it when nothing is kept.</summary>
        public static bool Write(IEnumerable<KeptPath> kept)
        {
            var list = kept.ToList();
            try
            {
                if (list.Count == 0)
                {
                    if (File.Exists(ListPath)) File.Delete(ListPath);
                    return true;
                }

                PinFile.ReplaceText(ListPath, string.Join(Environment.NewLine, Format(list)) + Environment.NewLine);
                return true;
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogError($"Keepsake: could not save {System.IO.Path.GetFileName(ListPath)}: {ex.Message}");
                return false;
            }
        }

        // ---------- the files ----------

        /// <summary>The files a kept path covers as they are in BepInEx/config, by relative path.</summary>
        public static List<string> LiveFiles(KeptPath kept)
        {
            var live = Live(kept.Path);
            if (!kept.IsFolder) return File.Exists(live) ? new List<string> { kept.Path } : new List<string>();
            if (!Directory.Exists(live)) return new List<string>();

            return Directory.GetFiles(live, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(PinFile.TempSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(f => kept.Path + "/" + f.Substring(live.Length).TrimStart('\\', '/').Replace('\\', '/'))
                .ToList();
        }

        /// <summary>The files a kept path has copies of, by relative path.</summary>
        public static List<string> CopiedFiles(KeptPath kept)
        {
            if (!kept.IsFolder) return File.Exists(Copy(kept.Path)) ? new List<string> { kept.Path } : new List<string>();

            var folder = System.IO.Path.Combine(StoreRoot, kept.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder)) return new List<string>();

            return Directory.GetFiles(folder, "*" + CopySuffix, SearchOption.AllDirectories)
                .Where(f => f.EndsWith(CopySuffix, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(0, f.Length - CopySuffix.Length))
                .Select(f => kept.Path + "/" + f.Substring(folder.Length).TrimStart('\\', '/').Replace('\\', '/'))
                .ToList();
        }

        /// <summary>
        /// Whether two files differ, told by length and time of the last write. Every copy is
        /// given the time of the file it was made from, and a file put back the time of its copy,
        /// so a file nobody touched since matches its copy, and one a sync replaced does not.
        /// </summary>
        public static bool Differ(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (!fa.Exists || !fb.Exists) return fa.Exists != fb.Exists;
            return fa.Length != fb.Length || fa.LastWriteTimeUtc != fb.LastWriteTimeUtc;
        }

        /// <summary>
        /// Copies a file over another, written aside and swapped in so an interrupted copy leaves
        /// the old one, and gives it the time of the one it was copied from.
        /// </summary>
        public static void CopyOver(string from, string to)
        {
            var folder = System.IO.Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var temp = to + PinFile.TempSuffix;
            File.Copy(from, temp, true);
            if (File.Exists(to)) File.Replace(temp, to, null);
            else File.Move(temp, to);

            File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
        }

        /// <summary>
        /// Makes a copy of every file the kept paths cover that changed since its last copy.
        /// A file gone from BepInEx/config keeps its copy, so a file a sync took away is put back
        /// rather than forgotten. Files waiting for an answer keep the copy they have, since it is
        /// one of the two to choose from. Returns how many were copied.
        /// </summary>
        public static int Save(IEnumerable<KeptPath> kept, Func<string, bool> waiting = null)
        {
            var saved = 0;
            foreach (var entry in kept)
            {
                List<string> files;
                try
                {
                    files = LiveFiles(entry);
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not look through {entry.Path}: {ex.Message}");
                    continue;
                }

                foreach (var path in files)
                {
                    try
                    {
                        if (waiting != null && waiting(path)) continue;
                        if (!Differ(Live(path), Copy(path))) continue;
                        CopyOver(Live(path), Copy(path));
                        saved++;
                    }
                    catch (Exception ex)
                    {
                        PinFile.Log?.LogWarning($"Keepsake: could not save a copy of {path}: {ex.Message}");
                    }
                }
            }
            return saved;
        }

        /// <summary>
        /// How long a game's log may go on after Keepsake saw it close, with the game still Keepsake's.
        /// Other mods log as they shut down after it. A game without Keepsake that started and
        /// closed in that time never got as far as a world, so wrote no mod's state either.
        /// </summary>
        public static readonly TimeSpan LogGrace = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Whether the last game of this profile was one Keepsake saw close. Not after a crash, a
        /// game in which the plugin did not load, or games played without Keepsake since; the log
        /// tells those, since every game of the profile writes it.
        /// </summary>
        public static bool ClosedCleanly(SessionState state, DateTime? logEnd) =>
            state.Closed != null &&
            (state.Started == null || state.Closed >= state.Started) &&
            (logEnd == null || logEnd <= state.Closed + LogGrace);

        /// <summary>
        /// The launch's look at every kept file that has a copy, before any mod reads one.
        ///
        /// A profile sync or a mod manager changes files while the game is closed, a mod while it
        /// runs. After a game Keepsake saw close, every kept file was copied on the way out, so a
        /// file that differs from its copy changed since, and your copy goes back, whatever time
        /// the file carries: mod managers that extract from a zip give files the zip's time, which
        /// is often long past. Only a file written after that close and before the game's log
        /// ended is the game's own, from a mod saving on the way out, and its copy is brought up
        /// to date from it.
        ///
        /// After any other game, a crash or one without Keepsake, the file's time is all there is
        /// to go on, against the end of the game's log: written before it, the file is yours and
        /// the copy is brought up to date; written after, it is left as it is, waiting for you to
        /// choose, since a mod writing late in a crash and a sync after it look the same.
        ///
        /// A file that is missing is put back either way. Files the kept folders hold that have no
        /// copy, such as ones a sync brought, are left as they are.
        /// </summary>
        /// <param name="logEnd">When the last game's BepInEx log was last written. See SessionFile.LogEnd.</param>
        public static SettleResult Settle(IEnumerable<KeptPath> kept, SessionState state, DateTime? logEnd)
        {
            var result = new SettleResult { Clean = ClosedCleanly(state, logEnd) };

            // The window in which a write is the game's own: after a clean close, from the close to
            // the end of the log, when mods may still be saving; otherwise up to the log's end.
            var from = result.Clean ? state.Closed : null;
            var end = result.Clean ? Later(state.Closed, logEnd) : logEnd;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in kept)
            {
                List<string> copies;
                try
                {
                    copies = CopiedFiles(entry);
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not look through the copies of {entry.Path}: {ex.Message}");
                    continue;
                }

                foreach (var path in copies)
                {
                    seen.Add(path);
                    try
                    {
                        SettleOne(path, state, from, end, result);
                    }
                    catch (Exception ex)
                    {
                        PinFile.Log?.LogWarning($"Keepsake: could not settle {path}: {ex.Message}");
                    }
                }
            }

            // Files no longer kept, or whose copy is gone, have nothing left to choose between.
            state.Waiting.RemoveAll(w => !seen.Contains(w.Path));
            return result;
        }

        /// <param name="from">After a clean close, the close: writes from then to end are the game's. Otherwise null, and every write up to end is.</param>
        private static void SettleOne(string path, SessionState state, DateTime? from, DateTime? end, SettleResult result)
        {
            var live = Live(path);
            var copy = Copy(path);

            var waiting = state.WaitingFor(path);
            if (waiting != null)
            {
                if (waiting.PutBack)
                {
                    CopyOver(copy, live);
                    state.Waiting.Remove(waiting);
                    result.PutBack++;
                    PinFile.Log?.LogInfo($"Keepsake: put back your copy of {path}, as you chose.");
                }
                else if (!Differ(copy, live)) state.Waiting.Remove(waiting);
                else result.Waiting++;
                return;
            }

            if (!File.Exists(live))
            {
                CopyOver(copy, live);
                result.PutBack++;
                PinFile.Log?.LogInfo($"Keepsake: put back your copy of {path}, which was missing.");
                return;
            }

            if (!Differ(copy, live)) return;

            var written = File.GetLastWriteTimeUtc(live);
            if (end != null && written <= end && (from == null || written > from))
            {
                CopyOver(live, copy);
                result.Updated++;
                PinFile.Log?.LogInfo($"Keepsake: {path} was written while the game ran, so your copy now matches it.");
            }
            else if (result.Clean)
            {
                CopyOver(copy, live);
                result.PutBack++;
                PinFile.Log?.LogInfo($"Keepsake: put back your copy of {path}, which changed after the game closed" +
                                     (written <= from ? ", though it carries an older time, as mod managers give files they extract." : "."));
            }
            else
            {
                state.Waiting.Add(new WaitingFile { Path = path });
                result.Waiting++;
                PinFile.Log?.LogInfo($"Keepsake: left {path} as it is. It changed after a game Keepsake did not see close, " +
                                     "so it waits for you to choose between it and your copy in the panel.");
            }
        }

        private static DateTime? Later(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : a > b ? a : b;

        /// <summary>Removes the copies of a path no longer kept.</summary>
        public static void Forget(KeptPath kept)
        {
            try
            {
                if (!kept.IsFolder)
                {
                    if (File.Exists(Copy(kept.Path))) File.Delete(Copy(kept.Path));
                    return;
                }

                var folder = System.IO.Path.Combine(StoreRoot, kept.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not remove the copies of {kept.Path}: {ex.Message}");
            }
        }
    }
}
