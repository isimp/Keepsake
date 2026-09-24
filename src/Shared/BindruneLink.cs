using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace Keepsake
{
    /// <summary>A key Bindrune holds as yours, as its bindrune.keys file records it.</summary>
    public sealed class BindruneKey
    {
        public string Id;

        /// <summary>Your key and the profile's, in the form BepInEx writes a KeyboardShortcut.</summary>
        public string Yours;
        public string Profile;

        /// <summary>Whether your key is the one in use, rather than kept aside.</summary>
        public bool Active;
    }

    /// <summary>
    /// Bindrune keeps keybinds of its own through profile syncs. Where both are installed,
    /// keybinds are Bindrune's: Keepsake neither keeps, writes nor follows a KeyCode or
    /// KeyboardShortcut setting, and Bindrune takes over any keybind Keepsake kept before it was
    /// installed. So a keybind only ever has one keeper, and what it ends up as never depends on
    /// which of the two wrote last. Without Bindrune, Keepsake keeps keybinds like any setting and
    /// can take over the keys Bindrune held.
    /// </summary>
    public static class BindruneLink
    {
        public const string Guid = "isimp.Bindrune";

        private const string DllName = "Bindrune.dll";

        public static string KeysFile => Path.Combine(Paths.BepInExRootPath, "bindrune.keys");

        /// <summary>
        /// Whether Bindrune is installed and enabled, told from its files, for use before any plugin
        /// has loaded. A mod manager disables a mod by renaming its files, so only the DLL under
        /// its own name counts.
        /// </summary>
        public static bool InstalledOnDisk()
        {
            try
            {
                var root = Paths.PluginPath;
                if (!Directory.Exists(root)) return false;

                // Where a mod manager or a hand install puts it: the plugins folder or one folder
                // down. Walking the whole tree costs a tenth of a second on a large profile.
                if (File.Exists(Path.Combine(root, DllName))) return true;
                if (Directory.GetDirectories(root).Any(dir => File.Exists(Path.Combine(dir, DllName)))) return true;

                return Directory.GetFiles(root, DllName, SearchOption.AllDirectories)
                    .Any(f => string.Equals(Path.GetFileName(f), DllName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not look for Bindrune: {ex.Message}");
                return false;
            }
        }

        /// <summary>A setting type name, as BepInEx writes it into a cfg file, that holds a keybind.</summary>
        public static bool IsKeybindType(string typeName) => typeName == "KeyCode" || typeName == "KeyboardShortcut";

        /// <summary>
        /// The keys section of bindrune.keys: one key per line, tab separated, as bind id, your
        /// key, the profile's key and 1 when yours is in use. Lines are skipped, not guessed at,
        /// when they do not fit. Empty when the file is missing or unreadable.
        /// </summary>
        public static List<BindruneKey> ReadKeys()
        {
            string[] lines;
            try
            {
                if (!File.Exists(KeysFile)) return new List<BindruneKey>();
                lines = File.ReadAllLines(KeysFile);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not read {Path.GetFileName(KeysFile)}: {ex.Message}");
                return new List<BindruneKey>();
            }

            var keys = ParseKeys(lines);
            if (keys != null) return keys;

            if (!_warnedVersion)
            {
                _warnedVersion = true;
                PinFile.Log?.LogWarning($"Keepsake: {Path.GetFileName(KeysFile)} is not in a version this Keepsake knows " +
                                        $"({StateVersion}), so its keys are not offered. Updating Keepsake fixes this.");
            }

            return new List<BindruneKey>();
        }

        /// <summary>
        /// The version line Bindrune writes at the top of bindrune.keys, in the version whose
        /// layout this reads. Any other is left alone rather than guessed at. See tests/contract.
        /// </summary>
        public const string StateVersion = "# bindrune state v3";

        private static bool _warnedVersion;

        /// <summary>The keys in the lines of bindrune.keys, or null when its version line is not StateVersion.</summary>
        public static List<BindruneKey> ParseKeys(IEnumerable<string> lines)
        {
            var keys = new List<BindruneKey>();
            var versionSeen = false;

            var inKeys = false;
            foreach (var raw in lines)
            {
                if (!versionSeen)
                {
                    if (raw.Trim().Length == 0) continue;
                    if (raw.Trim() != StateVersion) return null;
                    versionSeen = true;
                    continue;
                }

                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    inKeys = line == "[keys]";
                    continue;
                }

                if (!inKeys || line.Length == 0 || line.StartsWith("#")) continue;

                var parts = raw.Split('\t');
                if (parts.Length < 2) continue;

                keys.Add(new BindruneKey
                {
                    Id = parts[0],
                    Yours = parts[1].Trim(),
                    Profile = parts.Length > 2 ? parts[2].Trim() : "none",
                    Active = parts.Length < 4 || parts[3].Trim() == "1",
                });
            }

            return keys;
        }
    }
}
