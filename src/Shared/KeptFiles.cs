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

    /// <summary>
    /// Files a mod keeps its state in, such as a timer per world, kept the way settings are.
    /// While the game runs, the plugin copies each kept file into BepInEx/keepsake-files when it
    /// changes, and at launch the preloader puts a copy back wherever a profile sync replaced or
    /// removed the file, before any mod reads it.
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
        /// rather than forgotten. Returns how many were copied.
        /// </summary>
        public static int Save(IEnumerable<KeptPath> kept)
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
        /// Puts the copy back of every kept file that is missing from BepInEx/config or differs
        /// from its copy. Files the kept folders hold that have no copy, such as ones a sync
        /// brought, are left as they are. Returns how many were put back.
        /// </summary>
        public static int Restore(IEnumerable<KeptPath> kept)
        {
            var restored = 0;
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
                    try
                    {
                        if (!Differ(Copy(path), Live(path))) continue;
                        CopyOver(Copy(path), Live(path));
                        restored++;
                        PinFile.Log?.LogInfo($"Keepsake: put back your copy of {path}.");
                    }
                    catch (Exception ex)
                    {
                        PinFile.Log?.LogWarning($"Keepsake: could not put back {path}: {ex.Message}");
                    }
                }
            }
            return restored;
        }

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
