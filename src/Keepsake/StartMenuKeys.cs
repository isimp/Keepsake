using System;
using HarmonyLib;

namespace Keepsake
{
    /// <summary>
    /// Keeps the start menu's own keyboard handling out of the way while you are typing in the
    /// panel.
    ///
    /// Jotunn's input block only applies in the game scene, so nothing holds the menu back here,
    /// and the menu reads Return straight from ZInput rather than asking whether a text field has
    /// focus. Without this, pressing Return in a text field at the start menu submits whatever the
    /// menu has selected, which starts the game when that is nothing.
    /// </summary>
    internal static class StartMenuKeys
    {
        public static void Patch(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(FejdStartup), "UpdateKeyboard");
                if (target == null)
                {
                    Plugin.WarnOnce("Keepsake: the start menu's keyboard handling was not found, so Return " +
                                    "may reach the menu while you type in the panel.");
                    return;
                }

                harmony.Patch(target, new HarmonyMethod(AccessTools.Method(typeof(StartMenuKeys), nameof(Skip))));
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: could not hold the start menu's keys back while you type: {ex.Message}", ex);
            }
        }

        private static bool Skip() => !UI.KeepsakePanel.Typing;
    }
}
