using System;
using System.Collections.Generic;
using System.Linq;

namespace Keepsake
{
    /// <summary>What a launch did, beyond what it wrote in the log.</summary>
    public sealed class LaunchResult
    {
        /// <summary>Whether the profile had lost what it kept, and the spare copy brought it back. See SpareCopy.</summary>
        public bool FromSpareCopy;
    }

    /// <summary>
    /// The preloader's launch, apart from the moment it runs in: before any plugin loads, a profile
    /// that lost what it kept gets its spare copy back, Keepsake's lists are set aside, kept files
    /// are settled, kept values go back into their cfg files, and the spare copy is brought up to
    /// date.
    /// </summary>
    public static class Launcher
    {
        /// <param name="now">When the launch runs, in UTC.</param>
        /// <param name="logEnd">When the last game's BepInEx log was last written. See SessionFile.LogEnd.</param>
        /// <param name="bindruneInstalled">Whether Bindrune is installed. See Restorer.Apply.</param>
        public static LaunchResult Run(DateTime now, DateTime? logEnd, Func<bool> bindruneInstalled)
        {
            var result = new LaunchResult();

            // A profile folder replaced whole by its mod manager gets what it kept back first.
            try
            {
                result.FromSpareCopy = SpareCopy.Restore() > 0;
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogError($"Keepsake: bringing back the spare copy failed: {ex}");
            }
            SpareCopy.RestoredThisLaunch = result.FromSpareCopy;

            Trash.KeepLists(new[] { PinFile.FilePath, ProfileChanges.FilePath, KeptFiles.ListPath });

            try
            {
                Restore(now, logEnd, bindruneInstalled);
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogError($"Keepsake: putting your values back failed: {ex}");
            }

            try
            {
                SpareCopy.Update();
            }
            catch (Exception ex)
            {
                PinFile.Log?.LogWarning($"Keepsake: saving the spare copy failed: {ex.Message}");
            }

            return result;
        }

        private static void Restore(DateTime now, DateTime? logEnd, Func<bool> bindruneInstalled)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var pins = PinFile.Read();
            var kept = KeptFiles.Read();

            var written = new List<string> { PinFile.FilePath, ProfileChanges.FilePath, KeptFiles.ListPath, SessionFile.FilePath };
            if (pins != null) written.AddRange(pins.Select(p => PinFile.Absolute(p.File)));
            if (kept != null) written.AddRange(Restorer.KeptFilePaths(kept));
            var leftovers = Restorer.RemoveLeftovers(written);
            if (leftovers > 0) PinFile.Log?.LogInfo($"Keepsake: removed {leftovers} file(s) left half written by a game that stopped mid-save.");

            if (kept != null) Restorer.SettleFiles(kept, now, logEnd);

            if (pins == null) return;
            if (pins.Count == 0)
            {
                // Nothing kept, so no change can be waiting for an answer either.
                Restorer.RecordChanges(pins, new List<ProfileChange>());
                return;
            }

            // Keybinds are Bindrune's while it is installed, so none is written here then. See
            // BindruneLink.
            var result = Restorer.Apply(pins, bindruneInstalled);

            if (result.Learned) PinFile.Write(pins);
            var waiting = Restorer.RecordChanges(pins, result.Changes);

            PinFile.Log?.LogInfo($"Keepsake: {pins.Count} kept setting(s), {result.Restored} put back before the mods loaded" +
                                 (result.Changes.Count > 0 ? $", {result.Changes.Count} of them changed by the profile since the last launch" : "") +
                                 (waiting > 0 ? $", {waiting} profile change(s) waiting for an answer in the panel" : "") +
                                 (result.Missing > 0 ? $", {result.Missing} not in their cfg file yet" : "") +
                                 (result.ToBindrune > 0 ? $", {result.ToBindrune} keybind(s) left for Bindrune to take over" : "") +
                                 $", in {clock.Elapsed.TotalMilliseconds:0.0} ms.");
        }
    }
}
