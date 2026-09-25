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
    /// goes back to when you stop keeping it. The work itself is Launcher's.
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
                Launcher.Run(DateTime.UtcNow, SessionFile.LogEnd(), BindruneLink.InstalledOnDisk);
            }
            catch (Exception ex)
            {
                log.LogError($"Keepsake: the launch failed: {ex}");
            }
        }
    }
}
