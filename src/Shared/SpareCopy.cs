using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace Keepsake
{
    /// <summary>
    /// A spare copy of everything Keepsake keeps in the profile, outside the profile folder, for
    /// the one kind of update that takes the whole folder: Update existing profile in Thunderstore
    /// Mod Manager and r2modman builds the new profile beside the old one, deletes the old folder
    /// and renames the new one to its name. When a launch finds none of Keepsake's lists in the
    /// profile and a spare copy under the profile's name, the preloader brings it back before
    /// anything else.
    ///
    /// Only for a profile of those two managers, told by the mods.yml they keep in every profile
    /// folder, in a folder named profiles. The copy goes one level up, beside the managers' own
    /// cache and exports folders, in Keepsake/ and the profile's folder name. Not inside profiles,
    /// where the managers list every folder as a profile. Gale never replaces a profile folder,
    /// and a profile anywhere else gets no spare copy.
    ///
    /// Nothing is written outside the profile until you agree to it: the panel asks once such a
    /// profile keeps something, and the answer is kept in BepInEx/keepsake.spare, not in the
    /// plugin's cfg file, which a sync hands out and an export carries, so no pack answers for the
    /// people who follow it. The answer is part of the spare copy and comes back with it.
    ///
    /// The copy is brought up to date at the end of each launch and as the game closes, and only
    /// from a profile that has Keepsake's lists, so a profile that lost them never writes over it.
    /// Keepsake removes a spare copy only when you turn it off; a profile deleted in the manager
    /// leaves its spare copy behind.
    /// </summary>
    public static class SpareCopy
    {
        /// <summary>
        /// Set on the game's AppDomain by the preloader when it brought the spare copy back, for
        /// the plugin to say so once your character appears.
        /// </summary>
        private const string RestoredKey = "Keepsake.SpareCopyRestored";

        /// <summary>
        /// Whether this launch brought the spare copy back. Kept on the game's AppDomain, which the
        /// preloader and the plugin share while each carries its own copy of this class.
        /// </summary>
        public static bool RestoredThisLaunch
        {
            get => AppDomain.CurrentDomain.GetData(RestoredKey) is bool restored && restored;
            set => AppDomain.CurrentDomain.SetData(RestoredKey, value);
        }

        /// <summary>Keepsake's lists: a profile with none of them has lost what it kept, or never kept anything.</summary>
        private static readonly string[] Lists = { "keepsake.pins", "keepsake.changes", "keepsake.files" };

        /// <summary>Everything the copy holds, relative to BepInEx, in the order it is written: the folders first, the lists last.</summary>
        private static readonly string[] Folders = { "keepsake-files", "keepsake-trash" };

        private static readonly string[] Files = { "keepsake.spare", "keepsake.session", "keepsake.changes", "keepsake.files", "keepsake.pins" };

        /// <summary>BepInEx/keepsake.spare: whether you want a spare copy, once asked.</summary>
        public static string AnswerPath => Path.Combine(Paths.BepInExRootPath, "keepsake.spare");

        private const string AnswerVersion = "# keepsake spare v1";

        /// <summary>Whether you want a spare copy: true or false once asked, null before, or when the file cannot be read.</summary>
        public static bool? Wanted
        {
            get
            {
                try
                {
                    if (!File.Exists(AnswerPath)) return null;
                    var lines = File.ReadAllLines(AnswerPath);
                    if (lines.Length == 0 || lines[0].Trim() != AnswerVersion) return null;

                    foreach (var parts in lines.Skip(1).Where(l => !l.StartsWith("#")).Select(l => l.Split('\t')))
                        if (parts.Length >= 2 && parts[0].Trim() == "copy")
                            return parts[1].Trim() == "yes" ? true : parts[1].Trim() == "no" ? (bool?)false : null;
                    return null;
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not read {Path.GetFileName(AnswerPath)}: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>Whether a spare copy can be kept for this profile at all: one of a manager that replaces profile folders.</summary>
        public static bool Offered => Folder != null;

        /// <summary>Whether to ask about a spare copy now: a profile that could have one, that keeps something, and no answer yet.</summary>
        public static bool Asks => Offered && HasLists(Paths.BepInExRootPath) && Wanted == null;

        /// <summary>
        /// Records your answer. Yes makes the spare copy at once; no removes one made before, and
        /// the Keepsake folder beside profiles once it holds nothing. Returns why not, or null.
        /// </summary>
        public static string Answer(bool keep)
        {
            try
            {
                PinFile.ReplaceText(AnswerPath, string.Join(Environment.NewLine, new[]
                {
                    AnswerVersion,
                    "# Whether Keepsake keeps a spare copy of what it keeps in this profile outside the",
                    "# profile, for Update existing profile in Thunderstore Mod Manager and r2modman.",
                    "copy\t" + (keep ? "yes" : "no"),
                }) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogError($"Keepsake: could not save {Path.GetFileName(AnswerPath)}: {ex.Message}");
                return "your answer could not be saved, see the log";
            }

            var spare = Folder;
            if (spare == null) return null;

            try
            {
                if (keep)
                {
                    Update();
                    return null;
                }

                if (Directory.Exists(spare)) Directory.Delete(spare, true);
                var root = Path.GetDirectoryName(spare);
                if (root != null && Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root);
                return null;
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not {(keep ? "make" : "remove")} the spare copy in {spare}: {ex.Message}");
                return keep ? "the spare copy could not be made, see the log" : "the spare copy could not be removed, see the log";
            }
        }

        /// <summary>The spare copy's folder for this profile, or null when the profile is not one of a manager that replaces profile folders.</summary>
        public static string Folder
        {
            get
            {
                try
                {
                    var profile = Directory.GetParent(Paths.BepInExRootPath);
                    if (profile == null || !File.Exists(Path.Combine(profile.FullName, "mods.yml"))) return null;

                    var profiles = profile.Parent;
                    if (profiles == null || !string.Equals(profiles.Name, "profiles", StringComparison.OrdinalIgnoreCase)) return null;

                    var game = profiles.Parent;
                    return game == null ? null : Path.Combine(Path.Combine(game.FullName, "Keepsake"), profile.Name);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// There while the spare copy is brought back, so a restore cut short is done again at the
        /// next launch, and the half restored profile is never copied over the spare copy.
        /// </summary>
        private static string Marker => Path.Combine(Paths.BepInExRootPath, "keepsake.restoring");

        /// <summary>Whether any of Keepsake's lists is in the folder.</summary>
        private static bool HasLists(string root) => Lists.Any(n => File.Exists(Path.Combine(root, n)));

        /// <summary>
        /// Brings the spare copy back into a profile that has none of Keepsake's lists. Returns how
        /// many files came back, 0 when there was nothing to do. Called by the preloader before it
        /// reads anything.
        /// </summary>
        public static int Restore()
        {
            var spare = Folder;
            if (spare == null || !Directory.Exists(spare) || !HasLists(spare)) return 0;

            var profile = Paths.BepInExRootPath;
            var again = File.Exists(Marker);
            if (HasLists(profile) && !again) return 0;

            PinFile.Log?.LogInfo(again
                ? $"Keepsake: bringing back the spare copy in {spare} was cut short at the last launch, so it is done again."
                : $"Keepsake: this profile holds nothing Keepsake kept, and a spare copy of it is in {spare}. " +
                  "The profile folder was replaced, as Update existing profile does, so the spare copy comes back.");

            File.WriteAllText(Marker, spare);
            var restored = Mirror(spare, profile, "bringing back", out var failed);
            if (failed == 0) File.Delete(Marker);
            return restored;
        }

        /// <summary>
        /// Brings the spare copy up to date with the profile, once you said yes. Nothing is written
        /// for a profile with none of Keepsake's lists, so one that lost them never writes over the
        /// copy, nor while a restore is unfinished. Returns how many files were written or removed.
        /// </summary>
        public static int Update()
        {
            var spare = Folder;
            if (spare == null || Wanted != true) return 0;

            var profile = Paths.BepInExRootPath;
            if (!HasLists(profile) || File.Exists(Marker)) return 0;
            return Mirror(profile, spare, "saving", out _);
        }

        /// <summary>
        /// Makes what Keepsake keeps in one folder match the other: each file that differs is
        /// copied, and each the source no longer has is removed. Files are told apart by length and
        /// time of the last write, as kept files are. See KeptFiles.Differ.
        /// </summary>
        /// <param name="doing">What the log calls it when a file fails.</param>
        /// <param name="failed">How many files could not be copied or removed.</param>
        private static int Mirror(string from, string to, string doing, out int failed)
        {
            var changed = 0;
            failed = 0;
            var paths = new List<string>();

            foreach (var folder in Folders)
            {
                paths.AddRange(FilesIn(from, folder));
                paths.AddRange(FilesIn(to, folder));
            }
            paths.AddRange(Files);

            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var source = Path.Combine(from, path);
                var target = Path.Combine(to, path);
                try
                {
                    if (File.Exists(source))
                    {
                        if (!KeptFiles.Differ(source, target)) continue;
                        KeptFiles.CopyOver(source, target);
                        changed++;
                    }
                    else if (File.Exists(target))
                    {
                        File.Delete(target);
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: {doing} the spare copy of {path} failed: {ex.Message}");
                    failed++;
                }
            }

            foreach (var folder in Folders) RemoveEmptyFolders(Path.Combine(to, folder));
            return changed;
        }

        /// <summary>The files in one of Keepsake's folders, relative to the folder above it, leaving out files written aside.</summary>
        private static IEnumerable<string> FilesIn(string root, string folder)
        {
            var full = Path.Combine(root, folder);
            if (!Directory.Exists(full)) return Enumerable.Empty<string>();

            return Directory.GetFiles(full, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(PinFile.TempSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(root.Length).TrimStart('\\', '/'))
                .ToList();
        }

        private static void RemoveEmptyFolders(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (var below in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                    if (Directory.GetFileSystemEntries(below).Length == 0) Directory.Delete(below);
            }
            catch (Exception)
            {
                // Empty folders are only untidy.
            }
        }
    }
}
