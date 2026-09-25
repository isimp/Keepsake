using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace Keepsake
{
    /// <summary>A file or folder under BepInEx/config, as the Files list shows it.</summary>
    public sealed class ConfigItem
    {
        /// <summary>Relative to BepInEx/config, with forward slashes.</summary>
        public string Path;

        public bool IsFolder;

        /// <summary>Bytes, and for a folder the files in it, counted through every folder below.</summary>
        public long Size;

        public int Files;

        /// <summary>How many of the files are images, for leaving them out of the list.</summary>
        public int Images;

        public DateTime Changed;

        public string Id => FileKeeper.IdOf(Path, IsFolder);

        public string Name => Path.Substring(Path.LastIndexOf('/') + 1) + (IsFolder ? "/" : "");

        /// <summary>The folder it sits in, or empty for the top of BepInEx/config.</summary>
        public string Parent => Path.Contains("/") ? Path.Substring(0, Path.LastIndexOf('/')) : "";
    }

    /// <summary>
    /// Kept files while the game runs: keeping and releasing, answering for files left waiting at
    /// launch, and a copy of every kept file that changed as the game closes. See KeptFiles.
    /// </summary>
    public static class FileKeeper
    {
        private static List<KeptPath> _kept;
        private static bool _unreadable;

        private static SessionState _session;
        private static bool _sessionUnreadable;

        public static string IdOf(string path, bool isFolder) => "file:" + path + (isFolder ? "/" : "");

        public static IReadOnlyList<KeptPath> Kept
        {
            get
            {
                Load();
                return _kept;
            }
        }

        /// <summary>Reads the list the first time it is needed. An unreadable one is never written over.</summary>
        private static void Load()
        {
            if (_kept != null) return;
            var read = KeptFiles.Read();
            _unreadable = read == null;
            _kept = read ?? new List<KeptPath>();
        }

        /// <summary>The kept entry that covers a path: the path itself or a kept folder around it.</summary>
        public static KeptPath KeptBy(string path) => Kept.FirstOrDefault(k => k.Covers(path));

        /// <summary>Keeps a file or folder and saves a copy of it at once. Returns why not, or null.</summary>
        public static string Keep(string path, bool isFolder)
        {
            path = KeptFiles.Normalise(path);
            if (path == null) return "that is not a path inside BepInEx/config";
            Load();
            if (_unreadable) return "keepsake.files could not be read, so nothing is added to it until it can";
            if (KeptBy(path) != null) return null;

            var entry = new KeptPath { Path = path, IsFolder = isFolder };

            // A folder takes in what was kept inside it; the copies are already where it keeps them.
            _kept.RemoveAll(k => entry.Covers(k.Path));
            _kept.Add(entry);
            if (!KeptFiles.Write(_kept)) return "keepsake.files could not be saved, see the log";

            KeptFiles.Save(new[] { entry });
            SpareCopy.Follow();
            return null;
        }

        /// <summary>Stops keeping a file or folder and moves its copies to the trash. The file itself stays as it is. Returns why not, or null.</summary>
        public static string Release(string path)
        {
            Load();
            if (_unreadable) return "keepsake.files could not be read, so nothing is taken out of it until it can";
            var entry = Kept.FirstOrDefault(k => string.Equals(k.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            if (!KeptFiles.SetAside(entry)) return "the copies could not be set aside in the trash, so it stays kept; see the log";

            _kept.Remove(entry);
            if (!KeptFiles.Write(_kept))
            {
                _kept.Add(entry);
                return "keepsake.files could not be saved, see the log";
            }
            KeptFiles.Forget(entry);

            var session = Session;
            if (session != null && session.Waiting.RemoveAll(w => entry.Covers(w.Path)) > 0) SessionFile.Write(session);
            SpareCopy.Follow();
            return null;
        }

        /// <summary>keepsake.session as the preloader left it this launch, or null when it cannot be read, and is then never written over.</summary>
        private static SessionState Session
        {
            get
            {
                if (_session != null || _sessionUnreadable) return _session;
                _session = SessionFile.Read();
                _sessionUnreadable = _session == null;
                return _session;
            }
        }

        /// <summary>A kept file left as it is at launch, waiting for you to choose between it and your copy, or null.</summary>
        public static WaitingFile WaitingFor(string path) => Session?.WaitingFor(path);

        /// <summary>How many kept files wait for an answer, leaving out those whose copy already goes back at the next launch.</summary>
        public static int WaitingCount => Session?.Waiting.Count(w => !w.PutBack && KeptBy(w.Path) != null) ?? 0;

        /// <summary>Your copy goes back in at the next launch, before any mod reads the file.</summary>
        public static string PutBack(string path)
        {
            var waiting = WaitingFor(path);
            if (waiting == null) return "that file is not waiting for an answer";

            waiting.PutBack = true;
            return Saved(SessionFile.Write(Session));
        }

        /// <summary>After an answer or a Put back: the session file saved, and the spare copy following it. Returns why not, or null.</summary>
        private static string Saved(bool written)
        {
            if (!written) return "keepsake.session could not be saved, see the log";
            SpareCopy.Follow();
            return null;
        }

        /// <summary>The file as it is now becomes yours: its copy is made from it, and it no longer waits.</summary>
        public static string KeepCurrent(string path)
        {
            var waiting = WaitingFor(path);
            if (waiting == null) return "that file is not waiting for an answer";
            if (!File.Exists(KeptFiles.Live(path))) return "the file is not there to keep";
            if (!Trash.Put(path, KeptFiles.Copy(path), TrashReason.Copy)) return "your copy could not be set aside, see the log";

            KeptFiles.CopyOver(KeptFiles.Live(path), KeptFiles.Copy(path));
            Session.Waiting.Remove(waiting);
            return Saved(SessionFile.Write(Session));
        }

        /// <summary>
        /// An earlier version of a file goes back in at the next launch, before any mod reads the
        /// file: it becomes the copy, and the file waits with your copy chosen. A file no longer
        /// kept is kept again for it. The copy it replaces is set aside first.
        /// </summary>
        public static string PutBackVersion(string path, TrashEntry version)
        {
            if (!File.Exists(version.File)) return "that version is no longer in the trash";
            Load();
            if (_unreadable) return "keepsake.files could not be read, so nothing is added to it until it can";
            if (Session == null) return "keepsake.session could not be read, see the log";

            if (KeptBy(path) == null)
            {
                _kept.Add(new KeptPath { Path = path });
                if (!KeptFiles.Write(_kept)) return "keepsake.files could not be saved, see the log";
            }

            // Read first: setting the copy aside may take the oldest version out of the trash, and
            // that may be this one.
            var bytes = File.ReadAllBytes(version.File);
            var written = File.GetLastWriteTimeUtc(version.File);

            var copy = KeptFiles.Copy(path);
            if (!Trash.Put(path, copy, TrashReason.Copy)) return "your copy could not be set aside, see the log";

            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            var temp = copy + PinFile.TempSuffix;
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(copy)) File.Replace(temp, copy, null);
            else File.Move(temp, copy);
            File.SetLastWriteTimeUtc(copy, written);

            var waiting = WaitingFor(path);
            if (waiting == null) Session.Waiting.Add(waiting = new WaitingFile { Path = path });
            waiting.PutBack = true;
            return Saved(SessionFile.Write(Session));
        }

        /// <summary>
        /// As the game closes: a copy of every kept file that changed, and when the game closed, which
        /// tells the next launch that a file written after it was not written by a mod. Called from
        /// each hook the game gives on the way out, so the last one to run stamps the latest time.
        /// </summary>
        /// <param name="by">Which hook called, recorded for the log.</param>
        public static void Close(string by, DateTime? at = null)
        {
            Load();
            if (_unreadable) return;
            var session = Session;

            // The file is only there while something is kept.
            if (Kept.Count == 0)
            {
                if (session != null && File.Exists(SessionFile.FilePath)) File.Delete(SessionFile.FilePath);
                return;
            }

            // Without the session file there is no telling which files wait for an answer, and a
            // copy made from one of those would lose the version you have not chosen against. The
            // next launch takes this game as one it did not see close, and judges each file by
            // when it was written instead.
            if (session == null)
            {
                Plugin.Log?.LogWarning("Keepsake: keepsake.session could not be read, so no copies of kept files were saved this time.");
                return;
            }

            var saved = KeptFiles.Save(Kept, p => session.WaitingFor(p) != null);
            if (saved > 0) Plugin.Log?.LogDebug($"Keepsake: saved a copy of {saved} kept file(s).");
            session.Closed = at ?? DateTime.UtcNow;
            session.ClosedBy = by;
            SessionFile.Write(session);
        }

        /// <summary>
        /// Every folder under BepInEx/config and every file in it that is not a mod's own settings,
        /// which are kept one setting at a time instead. Files Keepsake writes aside are left out,
        /// and so are logs, which change all the time and are nothing to keep.
        /// </summary>
        /// <param name="settingsFiles">The cfg files of loaded mods, relative to BepInEx/config.</param>
        public static List<ConfigItem> Browse(ICollection<string> settingsFiles)
        {
            var root = Paths.ConfigPath;
            var items = new List<ConfigItem>();
            if (!Directory.Exists(root)) return items;

            var folders = new Dictionary<string, ConfigItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var full in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var path = full.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (path.EndsWith(PinFile.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (path.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                if (!path.Contains("/") && (settingsFiles.Contains(path) ||
                                            string.Equals(path, "BepInEx.cfg", StringComparison.OrdinalIgnoreCase))) continue;

                FileInfo info;
                try
                {
                    info = new FileInfo(full);
                }
                catch (Exception)
                {
                    continue;
                }

                var image = IsImage(path);
                var item = new ConfigItem { Path = path, Size = info.Length, Files = 1, Images = image ? 1 : 0, Changed = info.LastWriteTime };
                items.Add(item);

                // Every folder above it counts it.
                for (var parent = item.Parent; parent.Length > 0; parent = parent.Contains("/") ? parent.Substring(0, parent.LastIndexOf('/')) : "")
                {
                    if (!folders.TryGetValue(parent, out var folder))
                    {
                        folder = new ConfigItem { Path = parent, IsFolder = true };
                        folders[parent] = folder;
                    }
                    folder.Size += item.Size;
                    folder.Files++;
                    if (image) folder.Images++;
                    if (item.Changed > folder.Changed) folder.Changed = item.Changed;
                }
            }

            items.AddRange(folders.Values);
            return items
                .OrderBy(i => i.IsFolder ? i.Path : i.Parent, NaturalOrder.IgnoreCase)
                .ThenBy(i => i.IsFolder ? 0 : 1)
                .ThenBy(i => i.Path, NaturalOrder.IgnoreCase)
                .ToList();
        }

        private static readonly HashSet<string> ImageEndings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".tga", ".dds", ".bmp", ".gif", ".webp", ".psd", ".exr", ".hdr",
        };

        /// <summary>Whether a file is an image, told by its ending: textures, which mods ship by the hundred.</summary>
        public static bool IsImage(string path) => ImageEndings.Contains(System.IO.Path.GetExtension(path));

        /// <summary>Forgets everything, as at launch. For the tests.</summary>
        internal static void Reset()
        {
            _kept = null;
            _unreadable = false;
            _session = null;
            _sessionUnreadable = false;
        }
    }
}
