using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace Keepsake
{
    /// <summary>One setting kept at your own value, and the value the profile has for it.</summary>
    public sealed class Pin
    {
        /// <summary>The cfg file, relative to BepInEx/config and with forward slashes.</summary>
        public string File;
        public string Section;
        public string Key;

        /// <summary>Your value, written the way the cfg file stores it.</summary>
        public string Value;

        /// <summary>
        /// The profile's value, written the same way. Null until one has been seen, which is the
        /// case for a line added to the pins file by hand.
        /// </summary>
        public string Profile;

        public string Id => PinFile.IdOf(File, Section, Key);
    }

    /// <summary>
    /// The pins file, BepInEx/keepsake.pins.
    ///
    /// A profile sync uploads everything under BepInEx/config and deletes what the owner does not
    /// have, and it also picks up cfg, txt, json, yml, yaml and ini files anywhere in the profile.
    /// So the file sits outside config and has an extension of its own, and a sync never sees it.
    ///
    /// Plain text, one pin per line, tab separated: cfg file, section, setting, your value, the
    /// profile's value. BepInEx escapes tabs and line breaks in the values it writes, so a tab
    /// never occurs inside one.
    /// </summary>
    public static class PinFile
    {
        /// <summary>
        /// The first line of the file. Bindrune reads this file too and checks it, so a change to
        /// the format means a new version here and in Bindrune. See tests/contract.
        /// </summary>
        public const string Version = "# keepsake pins v1";

        private const string VersionPrefix = "# keepsake pins v";

        private static readonly string[] Header =
        {
            Version,
            "# Settings Keepsake keeps at your own value. Tab separated: the cfg file in",
            "# BepInEx/config, the section, the setting, your value, then the profile's value.",
            "# The profile's value may be left off; it is filled in on the next launch.",
        };

        public static ManualLogSource Log;

        public static string FilePath => Path.Combine(Paths.BepInExRootPath, "keepsake.pins");

        public static string IdOf(string file, string section, string key) =>
            file.ToLowerInvariant() + "\t" + section + "\t" + key;

        /// <summary>Whether a value can be written into the pins file and read back unchanged.</summary>
        public static bool Storable(string value) =>
            value != null && value.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0 && value == value.Trim();

        /// <summary>
        /// Reads every pin. A missing file is no pins. A file that cannot be read returns null,
        /// so a caller never writes an empty list over pins it merely failed to see.
        /// </summary>
        public static List<Pin> Read()
        {
            string[] lines;
            try
            {
                if (!System.IO.File.Exists(FilePath)) return new List<Pin>();
                lines = System.IO.File.ReadAllLines(FilePath);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"Keepsake: could not read {Path.GetFileName(FilePath)}: {ex.Message}");
                return null;
            }

            var pins = Parse(lines);
            if (pins == null)
                Log?.LogWarning($"Keepsake: {Path.GetFileName(FilePath)} was written by a newer Keepsake, so it is left as it is.");

            return pins;
        }

        /// <summary>
        /// The pins in the lines of a pins file. Null for a file of another version, which this
        /// one cannot read correctly and must never write over. Lines that do not fit are skipped.
        /// </summary>
        public static List<Pin> Parse(IEnumerable<string> lines)
        {
            var pins = new List<Pin>();
            var seen = new HashSet<string>();

            foreach (var raw in lines)
            {
                if (raw.StartsWith(VersionPrefix) && raw.Trim() != Version) return null;
                if (raw.Length == 0 || raw.StartsWith("#")) continue;

                var parts = raw.Split('\t');
                if (parts.Length < 4)
                {
                    Log?.LogWarning($"Keepsake: skipped a line in keepsake.pins that has fewer than four parts: {raw}");
                    continue;
                }

                // A hand edit could point a line anywhere on the disk, and the preloader writes
                // into the file it names.
                var file = KeptFiles.Normalise(parts[0]);
                if (file == null)
                {
                    Log?.LogWarning($"Keepsake: skipped a line in keepsake.pins whose file is not inside BepInEx/config: {raw}");
                    continue;
                }

                var pin = new Pin
                {
                    File = file,
                    Section = parts[1].Trim(),
                    Key = parts[2].Trim(),
                    Value = parts[3].Trim(),
                    Profile = parts.Length > 4 ? parts[4].Trim() : null,
                };

                // The last line for a setting wins, the way it does in a cfg file.
                if (!seen.Add(pin.Id)) pins.RemoveAll(p => p.Id == pin.Id);
                pins.Add(pin);
            }

            return pins;
        }

        /// <summary>
        /// Writes every pin, sorted so the file reads by mod. Written aside and swapped in, so a
        /// crash mid-write cannot leave half a file.
        /// </summary>
        public static bool Write(IEnumerable<Pin> pins)
        {
            try
            {
                ReplaceText(FilePath, string.Join(Environment.NewLine, Format(pins)) + Environment.NewLine);
                return true;
            }
            catch (Exception ex)
            {
                Log?.LogError($"Keepsake: could not save {Path.GetFileName(FilePath)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>The lines of a pins file holding these pins: the header, then one line each, sorted by mod.</summary>
        public static string[] Format(IEnumerable<Pin> pins)
        {
            var lines = new List<string>(Header);
            lines.AddRange(pins
                .OrderBy(p => p.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Section, StringComparer.Ordinal)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => string.Join("\t", p.Profile == null
                    ? new[] { p.File, p.Section, p.Key, p.Value }
                    : new[] { p.File, p.Section, p.Key, p.Value, p.Profile })));
            return lines.ToArray();
        }

        /// <summary>Added to a file's name while it is written aside.</summary>
        public const string TempSuffix = ".keepsake.tmp";

        /// <summary>
        /// Writes a text file aside and swaps it in, so a crash mid-write leaves the old file
        /// rather than half of the new one. UTF-8 without a byte order mark, as BepInEx writes.
        /// </summary>
        /// <param name="byteOrderMark">Whether the file starts with a UTF-8 byte order mark, as one that had it keeps it.</param>
        public static void ReplaceText(string path, string text, bool byteOrderMark = false)
        {
            var temp = path + TempSuffix;
            System.IO.File.WriteAllText(temp, text, new UTF8Encoding(byteOrderMark));

            if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
            else System.IO.File.Move(temp, path);
        }

        /// <summary>
        /// When the file was last written and how long it is, so a copy held in memory can tell
        /// that the file was edited since. Null when it cannot be told.
        /// </summary>
        public static string Stamp()
        {
            try
            {
                var file = new FileInfo(FilePath);
                return file.Exists ? file.LastWriteTimeUtc.Ticks + ":" + file.Length : "missing";
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A cfg file's path relative to BepInEx/config, or null for one outside it.</summary>
        public static string Relative(string cfgPath)
        {
            try
            {
                var root = Path.GetFullPath(Paths.ConfigPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(cfgPath);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                return NormaliseFile(full.Substring(root.Length));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string Absolute(string file) =>
            Path.Combine(Paths.ConfigPath, file.Replace('/', Path.DirectorySeparatorChar));

        private static string NormaliseFile(string file) => file.Trim().Replace('\\', '/');
    }
}
