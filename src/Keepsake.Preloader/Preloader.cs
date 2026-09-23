using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Mono.Cecil;

namespace Keepsake
{
    /// <summary>
    /// Puts your kept values back into the cfg files before any plugin loads.
    ///
    /// A profile sync lands before the game starts, so at launch the cfg files hold the profile's
    /// values. Plugins read their settings as they load, many of them only once, and the order
    /// plugins load in is not ours to choose. A preloader patcher runs before every plugin, so
    /// each mod reads your value from its own file as if it had always been there.
    ///
    /// It patches nothing: BepInEx calls Initialize on every patcher it finds, which is all this
    /// needs. Whatever value it replaces is remembered as the profile's, which is what a setting
    /// goes back to when you stop keeping it.
    /// </summary>
    public static class Preloader
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        public static void Initialize()
        {
            var log = Logger.CreateLogSource("Keepsake");
            PinFile.Log = log;

            try
            {
                Restore(log);
            }
            catch (Exception ex)
            {
                log.LogError($"Keepsake: putting your values back failed: {ex}");
            }
        }

        private static void Restore(ManualLogSource log)
        {
            var pins = PinFile.Read();
            if (pins == null || pins.Count == 0) return;

            int restored = 0, missing = 0;
            var learned = false;

            foreach (var group in pins.GroupBy(p => p.File, StringComparer.OrdinalIgnoreCase))
            {
                var path = PinFile.Absolute(group.Key);
                if (!File.Exists(path))
                {
                    // The mod writes the file when it first binds its settings, and the plugin
                    // puts the value in then.
                    missing += group.Count();
                    continue;
                }

                CfgText cfg;
                try
                {
                    cfg = CfgText.Load(path);
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Keepsake: could not read {group.Key}: {ex.Message}");
                    missing += group.Count();
                    continue;
                }

                foreach (var pin in group)
                {
                    if (!cfg.TryGet(pin.Section, pin.Key, out var current))
                    {
                        missing++;
                        continue;
                    }

                    if (current == pin.Value)
                    {
                        if (pin.Profile == null)
                        {
                            pin.Profile = current;
                            learned = true;
                        }
                        continue;
                    }

                    // Anything other than your value here came from the profile.
                    if (pin.Profile != current) learned = true;
                    pin.Profile = current;

                    cfg.Set(pin.Section, pin.Key, pin.Value);
                    restored++;
                    log.LogInfo($"Keepsake: kept your value for {pin.File} [{pin.Section}] {pin.Key}: {pin.Value} (the profile has {current}).");
                }

                if (!cfg.Changed) continue;

                try
                {
                    cfg.Save(path);
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Keepsake: could not write {group.Key}: {ex.Message}");
                }
            }

            if (learned) PinFile.Write(pins);

            log.LogInfo($"Keepsake: {pins.Count} kept setting(s), {restored} put back before the mods loaded" +
                        (missing > 0 ? $", {missing} not in their cfg file yet." : "."));
        }
    }
}
