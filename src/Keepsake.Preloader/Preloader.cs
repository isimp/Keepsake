using System;
using System.Collections.Generic;
using BepInEx;
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
            var leftovers = Restorer.RemoveLeftovers(Paths.ConfigPath, Paths.BepInExRootPath);
            if (leftovers > 0) log.LogInfo($"Keepsake: removed {leftovers} file(s) left half written by a game that stopped mid-save.");

            var pins = PinFile.Read();
            if (pins == null || pins.Count == 0) return;

            // Keybinds are Bindrune's while it is installed, so none is written here then. See
            // BindruneLink.
            var result = Restorer.Apply(pins, BindruneLink.InstalledOnDisk());

            if (result.Learned) PinFile.Write(pins);
            ProfileChanges.Hand(result.Changes);

            log.LogInfo($"Keepsake: {pins.Count} kept setting(s), {result.Restored} put back before the mods loaded" +
                        (result.Changes.Count > 0 ? $", {result.Changes.Count} of them changed by the profile since the last launch" : "") +
                        (result.Missing > 0 ? $", {result.Missing} not in their cfg file yet" : "") +
                        (result.ToBindrune > 0 ? $", {result.ToBindrune} keybind(s) left for Bindrune to take over." : "."));
        }
    }
}
