using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace Keepsake
{
    /// <summary>A kept setting whose profile's value changed while you kept your own.</summary>
    public sealed class ProfileChange
    {
        public string File;
        public string Section;
        public string Key;

        /// <summary>The profile's value before, written the way the cfg file stores it.</summary>
        public string From;

        /// <summary>The profile's value now.</summary>
        public string To;

        public string Id => PinFile.IdOf(File, Section, Key);

        public static ProfileChange Of(Pin pin, string from, string to) =>
            new ProfileChange { File = pin.File, Section = pin.Section, Key = pin.Key, From = from, To = to };
    }

    /// <summary>A kept setting whose profile changes are recorded without asking you about them.</summary>
    public sealed class QuietSetting
    {
        public string File;
        public string Section;
        public string Key;

        public string Id => PinFile.IdOf(File, Section, Key);

        public static QuietSetting Of(Pin pin) => new QuietSetting { File = pin.File, Section = pin.Section, Key = pin.Key };
    }

    /// <summary>What keepsake.changes holds: the changes waiting for an answer, and the settings not asked about.</summary>
    public sealed class ChangeState
    {
        public readonly List<ProfileChange> Waiting = new List<ProfileChange>();
        public readonly List<QuietSetting> Quiet = new List<QuietSetting>();

        public bool IsQuiet(string id) => Quiet.Any(q => q.Id == id);
    }

    /// <summary>
    /// The profile changes waiting for an answer, in BepInEx/keepsake.changes. The preloader adds
    /// what it finds at launch, and the panel takes a change out once you answer it, so one found
    /// in a session where the panel stayed shut is still there the next time. Settings you made
    /// quiet, such as a volume or a window size the profile's owner moves all the time, are listed
    /// there too, and their changes are recorded without being added.
    ///
    /// Kept apart from the pins file, whose format Bindrune reads too, and like it outside config
    /// with an extension no profile sync picks up. Plain text, tab separated, in two sections:
    /// [changes] with cfg file, section, setting, the profile's value before and its value now,
    /// and [quiet] with cfg file, section and setting.
    /// </summary>
    public static class ProfileChanges
    {
        public const string Version = "# keepsake changes v1";

        private const string VersionPrefix = "# keepsake changes v";

        private static readonly string[] Header =
        {
            Version,
            "# Profile changes to kept settings. [changes] waits for an answer in the panel: the cfg",
            "# file in BepInEx/config, the section, the setting, the profile's value before, then its",
            "# value now. [quiet] lists settings whose profile changes are recorded without asking:",
            "# the cfg file, the section and the setting. Tab separated.",
        };

        public static string FilePath => Path.Combine(Paths.BepInExRootPath, "keepsake.changes");

        /// <summary>
        /// What the file holds. A missing file holds nothing. Null for a file that cannot be read
        /// or is of another version, which is then never written over.
        /// </summary>
        public static ChangeState Read()
        {
            string[] lines;
            try
            {
                if (!System.IO.File.Exists(FilePath)) return new ChangeState();
                lines = System.IO.File.ReadAllLines(FilePath);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not read {Path.GetFileName(FilePath)}: {ex.Message}");
                return null;
            }

            var state = Parse(lines);
            if (state == null)
                PinFile.Log?.LogWarning($"Keepsake: {Path.GetFileName(FilePath)} was written by a newer Keepsake, so it is left as it is.");
            return state;
        }

        /// <summary>What the lines of the file hold, or null for a file of another version.</summary>
        public static ChangeState Parse(IEnumerable<string> lines)
        {
            var state = new ChangeState();
            string section = null;

            foreach (var raw in lines)
            {
                if (raw.StartsWith(VersionPrefix) && raw.Trim() != Version) return null;
                if (raw.Length == 0 || raw.StartsWith("#")) continue;

                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line;
                    continue;
                }

                var parts = raw.Split('\t').Select(p => p.Trim()).ToArray();
                var file = parts[0].Replace('\\', '/');

                if (section == "[changes]" && parts.Length >= 5)
                {
                    var change = new ProfileChange { File = file, Section = parts[1], Key = parts[2], From = parts[3], To = parts[4] };
                    state.Waiting.RemoveAll(c => c.Id == change.Id);
                    state.Waiting.Add(change);
                }
                else if (section == "[quiet]" && parts.Length >= 3)
                {
                    var quiet = new QuietSetting { File = file, Section = parts[1], Key = parts[2] };
                    if (!state.IsQuiet(quiet.Id)) state.Quiet.Add(quiet);
                }
            }

            return state;
        }

        public static string[] Format(ChangeState state)
        {
            var lines = new List<string>(Header) { "", "[changes]" };
            lines.AddRange(state.Waiting
                .OrderBy(c => c.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Section, StringComparer.Ordinal)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => string.Join("\t", new[] { c.File, c.Section, c.Key, c.From, c.To })));

            lines.Add("");
            lines.Add("[quiet]");
            lines.AddRange(state.Quiet
                .OrderBy(q => q.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(q => q.Section, StringComparer.Ordinal)
                .ThenBy(q => q.Key, StringComparer.Ordinal)
                .Select(q => string.Join("\t", new[] { q.File, q.Section, q.Key })));
            return lines.ToArray();
        }

        /// <summary>Writes the file, or removes it when it would hold nothing.</summary>
        public static bool Write(ChangeState state)
        {
            try
            {
                if (state.Waiting.Count == 0 && state.Quiet.Count == 0)
                {
                    if (System.IO.File.Exists(FilePath)) System.IO.File.Delete(FilePath);
                    return true;
                }

                PinFile.ReplaceText(FilePath, string.Join(Environment.NewLine, Format(state)) + Environment.NewLine);
                return true;
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogError($"Keepsake: could not save {Path.GetFileName(FilePath)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds newly found changes to those waiting, leaving out quiet settings. A setting already
        /// waiting keeps the value the profile had before its first change and takes the newest
        /// one, and drops out when the profile went back to where it was. Returns whether anything
        /// differs.
        /// </summary>
        public static bool Merge(ChangeState state, IEnumerable<ProfileChange> found)
        {
            var changed = false;

            foreach (var change in found)
            {
                if (state.IsQuiet(change.Id)) continue;

                var known = state.Waiting.FirstOrDefault(c => c.Id == change.Id);
                if (known == null)
                {
                    state.Waiting.Add(change);
                    changed = true;
                    continue;
                }

                if (change.To == known.From) state.Waiting.Remove(known);
                else known.To = change.To;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// Forgets the settings no longer kept, their waiting changes and their quiet mark alike,
        /// so a setting kept again later is asked about again. Returns whether any went.
        /// </summary>
        public static bool KeepOnly(ChangeState state, ICollection<string> keptIds)
        {
            var gone = state.Waiting.RemoveAll(c => !keptIds.Contains(c.Id));
            gone += state.Quiet.RemoveAll(q => !keptIds.Contains(q.Id));
            return gone > 0;
        }
    }
}
