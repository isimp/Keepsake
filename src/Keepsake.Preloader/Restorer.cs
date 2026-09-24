using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Keepsake
{
    /// <summary>What one pass of the preloader did.</summary>
    public sealed class RestoreResult
    {
        public int Restored;
        public int Missing;
        public int ToBindrune;

        /// <summary>Whether a profile's value was learned or changed, so the pins file needs writing.</summary>
        public bool Learned;

        /// <summary>Kept settings whose profile's value is not the one recorded last time.</summary>
        public readonly List<ProfileChange> Changes = new List<ProfileChange>();
    }

    /// <summary>
    /// The preloader's work, apart from the moment it runs in: writing each kept value into its
    /// cfg file and recording the value it replaces as the profile's.
    /// </summary>
    public static class Restorer
    {
        /// <param name="bindruneInstalled">
        /// Whether Bindrune is installed, which keeps keybinds out of this. Asked only once a kept
        /// setting turns out to be a keybind, since finding out means looking through the plugins.
        /// </param>
        public static RestoreResult Apply(List<Pin> pins, Func<bool> bindruneInstalled)
        {
            var result = new RestoreResult();
            bool? bindrune = null;

            foreach (var group in pins.GroupBy(p => p.File, StringComparer.OrdinalIgnoreCase))
            {
                var path = PinFile.Absolute(group.Key);
                if (!File.Exists(path))
                {
                    // The mod writes the file when it first binds its settings, and the plugin
                    // puts the value in then.
                    result.Missing += group.Count();
                    continue;
                }

                CfgText cfg;
                try
                {
                    cfg = CfgText.Load(path);
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not read {group.Key}: {ex.Message}");
                    result.Missing += group.Count();
                    continue;
                }

                foreach (var pin in group)
                {
                    if (!cfg.TryGet(pin.Section, pin.Key, out var current))
                    {
                        result.Missing++;
                        continue;
                    }

                    if (BindruneLink.IsKeybindType(cfg.TypeOf(pin.Section, pin.Key)) && (bindrune ??= bindruneInstalled()))
                    {
                        result.ToBindrune++;
                        continue;
                    }

                    // Your value in the file tells nothing about the profile: it is there from the
                    // last session, or the profile moved to it, and the two look the same.
                    if (current == pin.Value)
                    {
                        if (pin.Profile == null)
                        {
                            pin.Profile = current;
                            result.Learned = true;
                        }
                        continue;
                    }

                    // Anything other than your value here came from the profile.
                    var before = pin.Profile;
                    var changed = before != null && before != current;
                    if (changed) result.Changes.Add(ProfileChange.Of(pin, before, current));
                    if (before != current) result.Learned = true;
                    pin.Profile = current;

                    cfg.Set(pin.Section, pin.Key, pin.Value);
                    result.Restored++;
                    PinFile.Log?.LogInfo($"Keepsake: kept your value for {pin.File} [{pin.Section}] {pin.Key}: {pin.Value} " +
                                         (changed ? $"(the profile changed it from {before} to {current})." : $"(the profile has {current})."));
                }

                if (!cfg.Changed) continue;

                try
                {
                    cfg.Save(path);
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not write {group.Key}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Adds the changes found to those waiting for an answer, leaving out quiet settings, and
        /// forgets settings no longer kept, such as keybinds Bindrune took over. Returns how many
        /// changes are waiting.
        /// </summary>
        public static int RecordChanges(List<Pin> pins, List<ProfileChange> found)
        {
            var state = ProfileChanges.Read();
            if (state == null) return 0;

            var changed = ProfileChanges.Merge(state, found);
            changed |= ProfileChanges.KeepOnly(state, new HashSet<string>(pins.Select(p => p.Id)));
            if (changed) ProfileChanges.Write(state);
            return state.Waiting.Count;
        }

        /// <summary>
        /// The kept files at launch: each one that changed since its copy is put back, brought
        /// into its copy, or left waiting for an answer (see KeptFiles.Settle), and the launch is
        /// written down, so the next one can tell whether this game closed. Nothing is written
        /// while nothing is kept, and a session file of another version is never written over:
        /// the files are then settled as after a game Keepsake did not see close.
        /// </summary>
        /// <param name="logEnd">When the last game's BepInEx log was last written. See SessionFile.LogEnd.</param>
        public static SettleResult SettleFiles(List<KeptPath> kept, DateTime now, DateTime? logEnd)
        {
            if (kept.Count == 0)
            {
                try
                {
                    if (File.Exists(SessionFile.FilePath) && SessionFile.Read() != null) File.Delete(SessionFile.FilePath);
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not remove {Path.GetFileName(SessionFile.FilePath)}: {ex.Message}");
                }
                return new SettleResult();
            }

            var session = SessionFile.Read();
            var result = KeptFiles.Settle(kept, session ?? new SessionState(), logEnd);

            if (session != null)
            {
                session.Started = now;
                SessionFile.Write(session);
            }

            PinFile.Log?.LogInfo($"Keepsake: {kept.Count} kept file(s) or folder(s), the last game " +
                                 (result.Clean ? "closed with Keepsake" : "was not seen closing") +
                                 $", {result.PutBack} file(s) put back, {result.Updated} copy(s) brought up to date" +
                                 (result.Waiting > 0 ? $", {result.Waiting} file(s) waiting for an answer in the panel" : "") +
                                 " before the mods loaded.");
            return result;
        }

        /// <summary>
        /// Every file Keepsake may write for the kept files: each one in BepInEx/config and its
        /// copy, for the leftover check.
        /// </summary>
        public static IEnumerable<string> KeptFilePaths(IEnumerable<KeptPath> kept)
        {
            foreach (var entry in kept)
            {
                List<string> copies;
                try
                {
                    copies = KeptFiles.CopiedFiles(entry);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var path in copies)
                {
                    yield return KeptFiles.Live(path);
                    yield return KeptFiles.Copy(path);
                }
            }
        }

        /// <summary>
        /// Removes the files a write left behind when the game stopped between writing a file
        /// aside and swapping it in (see PinFile.ReplaceText). The file it was meant to replace
        /// is whole in that case, so the leftover is only in the way: under BepInEx/config, a
        /// profile owner's sync would upload it. Keepsake only writes aside the files it names
        /// here, its own and the cfg files of kept settings, so only those are looked at rather
        /// than every file in the profile. Returns how many were removed.
        /// </summary>
        public static int RemoveLeftovers(IEnumerable<string> written)
        {
            var removed = 0;
            foreach (var path in written.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var leftover = path + PinFile.TempSuffix;
                try
                {
                    if (!File.Exists(leftover)) continue;
                    File.Delete(leftover);
                    removed++;
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not remove the leftover {leftover}: {ex.Message}");
                }
            }

            return removed;
        }
    }
}
