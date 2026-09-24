using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

namespace Keepsake
{
    /// <summary>
    /// Stands in for the plugin class, which needs the game to exist, so the code that reports
    /// through it can be tested. Warnings are collected for the tests to look at.
    /// </summary>
    internal static class Plugin
    {
        public static readonly ManualLogSource Log = new ManualLogSource("Keepsake");

        public static readonly List<string> Warnings = new List<string>();

        public static void WarnOnce(string message, Exception detail = null,
            [CallerFilePath] string file = null, [CallerLineNumber] int line = 0) => Warnings.Add(message);
    }
}
