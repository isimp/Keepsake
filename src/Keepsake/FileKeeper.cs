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
    /// Kept files while the game runs: keeping and releasing, and saving a copy of every kept file
    /// that changed. See KeptFiles.
    /// </summary>
    public static class FileKeeper
    {
        /// <summary>How often kept files are checked for changes while the game runs, in seconds.</summary>
        public const float SaveEvery = 10f;

        private static List<KeptPath> _kept;
        private static bool _unreadable;
        private static float _saveAt;

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
            return null;
        }

        /// <summary>Stops keeping a file or folder and removes its copies. The file itself stays as it is.</summary>
        public static void Release(string path)
        {
            Load();
            if (_unreadable) return;
            var entry = Kept.FirstOrDefault(k => string.Equals(k.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return;

            _kept.Remove(entry);
            if (KeptFiles.Write(_kept)) KeptFiles.Forget(entry);
        }

        /// <summary>Called every frame with the time; saves copies of kept files that changed, every SaveEvery seconds.</summary>
        public static void Tick(float now)
        {
            if (now < _saveAt) return;
            _saveAt = now + SaveEvery;
            Flush();
        }

        /// <summary>Saves copies of kept files that changed, such as when the game closes.</summary>
        public static void Flush()
        {
            if (Kept.Count == 0) return;
            var saved = KeptFiles.Save(Kept);
            if (saved > 0) Plugin.Log.LogDebug($"Keepsake: saved a copy of {saved} kept file(s).");
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
                .OrderBy(i => i.IsFolder ? i.Path : i.Parent, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.IsFolder ? 0 : 1)
                .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
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
            _saveAt = 0f;
        }
    }
}
