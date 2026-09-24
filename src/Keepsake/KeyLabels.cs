using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Keepsake
{
    /// <summary>
    /// What to call a key on screen: the label printed on it under the player's keyboard layout.
    ///
    /// Keys are stored as KeyCodes, which Unity names by their position on a US keyboard, so on a
    /// German keyboard KeyCode.Z is the key labelled Y. Mods read them the same way, so what is
    /// stored is right and only what is shown changes. The label comes from
    /// ZInput.KeyCodeToDisplayName, the same name Valheim's own controls settings show.
    ///
    /// Only a single visible character is accepted, which covers the letters, digits and
    /// punctuation that move between layouts. Anything else, including any failure, falls back
    /// to Unity's name, so keys such as LeftShift read exactly as they are stored.
    /// </summary>
    internal static class KeyLabels
    {
        private static readonly Dictionary<KeyCode, string> Cache = new Dictionary<KeyCode, string>();

        /// <summary>
        /// A setting's value as it is shown: a keybind in the keyboard's labels, modifiers first,
        /// anything else as it is stored. A key pressed alone on a KeyCode setting is its label.
        /// </summary>
        public static string Shown(Setting setting, string value)
        {
            if (value == null || setting == null || !setting.IsKeybind) return value;

            try
            {
                if (setting.Entry.SettingType == typeof(KeyCode))
                    return Of((KeyCode)TomlTypeConverter.ConvertToValue(value, typeof(KeyCode)));

                var shortcut = KeyboardShortcut.Deserialize(value);
                if (shortcut.MainKey == KeyCode.None) return value;

                // Modifiers in KeyCode order, as Bindrune writes them, so one shortcut reads the
                // same in both.
                return string.Join(" + ", shortcut.Modifiers.Distinct().OrderBy(k => (int)k).Select(Of)
                    .Concat(new[] { Of(shortcut.MainKey) }).ToArray());
            }
            catch (Exception)
            {
                return value;
            }
        }

        public static string Of(KeyCode key)
        {
            if (Cache.TryGetValue(key, out var known)) return known;

            var name = key.ToString();
            try
            {
                var shown = ZInput.KeyCodeToDisplayName(key);

                // The game answers "$KeyCode ... did not have corresponding ButtonControl" before a
                // keyboard exists. That is not an answer to remember, so it is not cached.
                if (shown == null || shown.StartsWith("$KeyCode ", StringComparison.Ordinal)) return name;

                var label = Pick(shown.Trim(), name);
                Cache[key] = label;
                return label;
            }
            catch
            {
                return name;
            }
        }

        private static string Pick(string shown, string name)
        {
            if (shown.Length != 1) return name;

            var c = shown[0];
            if (char.IsWhiteSpace(c) || char.IsControl(c)) return name;

            return char.ToUpperInvariant(c).ToString();
        }
    }
}
