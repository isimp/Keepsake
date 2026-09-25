using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;

namespace Keepsake
{
    /// <summary>Why a version of a kept file was set aside.</summary>
    public enum TrashReason
    {
        /// <summary>A copy of yours, replaced by a newer copy.</summary>
        Copy,

        /// <summary>The file in BepInEx/config, replaced by your copy.</summary>
        Replaced,

        /// <summary>Your copy when you released the file.</summary>
        Released,
    }

    /// <summary>An earlier version of a kept file, set aside in the trash.</summary>
    public sealed class TrashEntry
    {
        /// <summary>The kept file's path, relative to BepInEx/config, with forward slashes.</summary>
        public string Path;

        /// <summary>The version itself, in the trash.</summary>
        public string File;

        /// <summary>When it was set aside, in UTC.</summary>
        public DateTime At;

        /// <summary>Tells two versions set aside in the same millisecond apart.</summary>
        public int Count;

        public TrashReason Reason;
    }

    /// <summary>
    /// BepInEx/keepsake-trash: what Keepsake replaces or removes is set aside here first, so no
    /// decision it makes on its own, and no Release, is final. Kept files go to files/, under
    /// their path in BepInEx/config, each version named after the file, when it was set aside and
    /// why. Keepsake's own lists go to lists/, a copy at each launch they changed since the last.
    /// The last Versions of each are kept, older ones removed as a new one comes in.
    ///
    /// Every name ends in .kept, so no profile sync picks it up whatever the file was.
    /// </summary>
    public static class Trash
    {
        /// <summary>How many versions of each file are kept.</summary>
        public const int Versions = 5;

        public static string Root => System.IO.Path.Combine(Paths.BepInExRootPath, "keepsake-trash");

        private static string FilesRoot => System.IO.Path.Combine(Root, "files");

        private static string ListsRoot => System.IO.Path.Combine(Root, "lists");

        private const string StampFormat = "yyyyMMdd-HHmmss-fff";

        /// <summary>The name, the time with a count when two share it, and the reason, as in Seasonality.bin.20260925-143000-123.copy.kept.</summary>
        private static readonly Regex VersionName =
            new Regex(@"^(?<name>.+)\.(?<at>\d{8}-\d{6}-\d{3})(?:-(?<count>\d+))?\.(?<reason>copy|replaced|released|launch)\.kept$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string ReasonText(TrashReason reason) => reason.ToString().ToLowerInvariant();

        /// <summary>
        /// Sets a copy of a file aside as an earlier version of a kept path, before it is replaced
        /// or removed. A file that is not there has nothing to set aside. Returns false when it
        /// could not be set aside, and the caller then leaves the file as it is.
        /// </summary>
        /// <param name="path">The kept file's path, relative to BepInEx/config.</param>
        /// <param name="file">The file to set aside: the one in BepInEx/config, or its copy.</param>
        public static bool Put(string path, string file, TrashReason reason)
        {
            try
            {
                if (!System.IO.File.Exists(file)) return true;

                var folder = System.IO.Path.Combine(FilesRoot, System.IO.Path.GetDirectoryName(path.Replace('/', System.IO.Path.DirectorySeparatorChar)) ?? "");
                var name = System.IO.Path.GetFileName(path);

                // Already set aside as it is, such as your copy put aside by Put back and then the
                // same file replaced by the launch after: once is enough, and keeps the slot free.
                var newest = In(folder, name).FirstOrDefault();
                if (newest != null && SameBytes(newest.File, file)) return true;

                SetAside(file, folder, name, ReasonText(reason));
                return true;
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not set aside the version of {path} it was about to replace, so it is left as it is: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Keeps a copy of each of Keepsake's own lists that changed since its last copy, before
        /// the launch changes anything. Called by the preloader.
        /// </summary>
        /// <param name="lists">The full paths of the lists.</param>
        public static void KeepLists(IEnumerable<string> lists)
        {
            foreach (var list in lists)
            {
                try
                {
                    if (!System.IO.File.Exists(list)) continue;

                    var name = System.IO.Path.GetFileName(list);
                    var latest = In(ListsRoot, name).FirstOrDefault();
                    if (latest != null && SameBytes(latest.File, list)) continue;

                    SetAside(list, ListsRoot, name, "launch");
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not keep a copy of {System.IO.Path.GetFileName(list)}: {ex.Message}");
                }
            }
        }

        /// <summary>The earlier versions of a kept file, newest first.</summary>
        /// <param name="path">The kept file's path, relative to BepInEx/config.</param>
        public static List<TrashEntry> Of(string path)
        {
            try
            {
                var folder = System.IO.Path.Combine(FilesRoot, System.IO.Path.GetDirectoryName(path.Replace('/', System.IO.Path.DirectorySeparatorChar)) ?? "");
                var entries = In(folder, System.IO.Path.GetFileName(path));
                foreach (var entry in entries) entry.Path = path;
                return entries;
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not look through the earlier versions of {path}: {ex.Message}");
                return new List<TrashEntry>();
            }
        }

        /// <summary>Copies the file into the folder under a new version's name, then removes the oldest versions past Versions.</summary>
        private static void SetAside(string file, string folder, string name, string reason)
        {
            Directory.CreateDirectory(folder);

            var at = DateTime.UtcNow;
            var stamp = at.ToString(StampFormat, CultureInfo.InvariantCulture);
            var target = System.IO.Path.Combine(folder, $"{name}.{stamp}.{reason}.kept");
            for (var count = 1; System.IO.File.Exists(target); count++)
                target = System.IO.Path.Combine(folder, $"{name}.{stamp}-{count}.{reason}.kept");

            // Written aside and moved in, so a version is never half there under its own name.
            var temp = target + PinFile.TempSuffix;
            System.IO.File.Copy(file, temp, true);
            System.IO.File.SetLastWriteTimeUtc(temp, System.IO.File.GetLastWriteTimeUtc(file));
            System.IO.File.Move(temp, target);

            foreach (var old in In(folder, name).Skip(Versions))
                System.IO.File.Delete(old.File);

            // What a game that stopped mid-copy left behind.
            foreach (var leftover in Directory.GetFiles(folder, name + ".*" + PinFile.TempSuffix))
                if (!string.Equals(leftover, temp, StringComparison.OrdinalIgnoreCase)) System.IO.File.Delete(leftover);
        }

        /// <summary>The versions of one file in a folder of the trash, newest first.</summary>
        private static List<TrashEntry> In(string folder, string name)
        {
            if (!Directory.Exists(folder)) return new List<TrashEntry>();

            var entries = new List<TrashEntry>();
            foreach (var file in Directory.GetFiles(folder, name + ".*.kept"))
            {
                var match = VersionName.Match(System.IO.Path.GetFileName(file));
                if (!match.Success || !string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!DateTime.TryParseExact(match.Groups["at"].Value, StampFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)) continue;

                entries.Add(new TrashEntry
                {
                    File = file,
                    At = at,
                    Count = match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture) : 0,
                    Reason = ReasonOf(match.Groups["reason"].Value),
                });
            }

            return entries.OrderByDescending(e => e.At).ThenByDescending(e => e.Count).ToList();
        }

        private static TrashReason ReasonOf(string text) =>
            string.Equals(text, "replaced", StringComparison.OrdinalIgnoreCase) ? TrashReason.Replaced :
            string.Equals(text, "released", StringComparison.OrdinalIgnoreCase) ? TrashReason.Released :
            TrashReason.Copy;

        private static bool SameBytes(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return fa.Length == fb.Length && System.IO.File.ReadAllBytes(a).SequenceEqual(System.IO.File.ReadAllBytes(b));
        }
    }
}
