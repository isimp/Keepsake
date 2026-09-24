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
        /// <param name="bindrune">Whether Bindrune is installed, which keeps keybinds out of this.</param>
        public static RestoreResult Apply(List<Pin> pins, bool bindrune)
        {
            var result = new RestoreResult();

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

                    if (bindrune && BindruneLink.IsKeybindType(cfg.TypeOf(pin.Section, pin.Key)))
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
                    if (changed) result.Changes.Add(new ProfileChange { Id = pin.Id, From = before, To = current });
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
        /// Removes the files a write left behind when the game stopped between writing a file
        /// aside and swapping it in (see PinFile.ReplaceText). The file it was meant to replace
        /// is whole in that case, so the leftover is only in the way: under BepInEx/config, a
        /// profile owner's sync would upload it. Returns how many were removed.
        /// </summary>
        public static int RemoveLeftovers(string configDir, string rootDir)
        {
            return RemoveLeftovers(configDir, SearchOption.AllDirectories) +
                   RemoveLeftovers(rootDir, SearchOption.TopDirectoryOnly);
        }

        private static int RemoveLeftovers(string dir, SearchOption option)
        {
            if (dir == null || !Directory.Exists(dir)) return 0;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*" + PinFile.TempSuffix, option);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: could not look for leftover files in {dir}: {ex.Message}");
                return 0;
            }

            var removed = 0;
            foreach (var file in files)
            {
                // The pattern also matches longer extensions that merely start the same way.
                if (!file.EndsWith(PinFile.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    PinFile.Log?.LogWarning($"Keepsake: could not remove the leftover {file}: {ex.Message}");
                }
            }

            return removed;
        }
    }
}
