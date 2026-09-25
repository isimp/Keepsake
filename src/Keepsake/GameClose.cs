using System;

namespace Keepsake
{
    /// <summary>
    /// What the plugin does as the game closes, from each hook the game gives on the way out:
    /// values followed from a config manager that are still waiting are written, kept files are
    /// copied, and the spare copy is brought up to date.
    /// </summary>
    public static class GameClose
    {
        /// <param name="by">Which hook called, recorded for the log.</param>
        /// <param name="at">When the game closed, in UTC; now when left out.</param>
        public static void Run(string by, DateTime? at = null)
        {
            Keeper.Flush();
            FileKeeper.Close(by, at);
            SpareCopy.Update();
        }
    }
}
