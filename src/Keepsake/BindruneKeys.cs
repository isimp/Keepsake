using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Keepsake
{
    /// <summary>A loaded keybind setting, named the way Bindrune names it.</summary>
    public sealed class KeybindSlot<T>
    {
        /// <summary>cfg:&lt;mod guid&gt;:&lt;section&gt;:&lt;key&gt;, the id Bindrune gives a config bind.</summary>
        public string BindruneId;

        /// <summary>True for a setting of a single KeyCode, false for a KeyboardShortcut.</summary>
        public bool IsKeyCode;

        public T Setting;
    }

    /// <summary>A key Bindrune holds, matched to its setting, in the form that setting stores it.</summary>
    public sealed class KeybindMatch<T>
    {
        public T Setting;
        public string Yours;
        public string Profile;
    }

    /// <summary>
    /// Matching the keys in bindrune.keys to loaded settings, kept apart from the game so it can
    /// be tested on its own. See BindruneLink.
    /// </summary>
    public static class BindruneKeys
    {
        public static string IdOf(string modGuid, string section, string key) => $"cfg:{modGuid}:{section}:{key}";

        /// <summary>
        /// The keys in use that belong to one of the given settings, each in that setting's form.
        /// Settings are matched by the whole id rather than by taking Bindrune's ids apart, since a
        /// section or a setting name may itself contain a colon.
        /// </summary>
        public static List<KeybindMatch<T>> Match<T>(IEnumerable<BindruneKey> keys, IEnumerable<KeybindSlot<T>> slots)
        {
            var byId = new Dictionary<string, KeybindSlot<T>>();
            foreach (var slot in slots) byId[slot.BindruneId] = slot;

            var matches = new List<KeybindMatch<T>>();
            foreach (var key in keys)
            {
                if (!key.Active || !byId.TryGetValue(key.Id, out var slot)) continue;

                var yours = AsSetting(key.Yours, slot.IsKeyCode);
                if (yours == null) continue;

                matches.Add(new KeybindMatch<T> { Setting = slot.Setting, Yours = yours, Profile = AsSetting(key.Profile, slot.IsKeyCode) });
            }

            return matches;
        }

        /// <summary>
        /// A key as Bindrune stores it, which is how BepInEx writes a KeyboardShortcut, in the form
        /// a setting stores it. A setting of a single KeyCode takes the main key. Null when it
        /// cannot be read.
        /// </summary>
        public static string AsSetting(string stored, bool isKeyCode)
        {
            try
            {
                var none = string.IsNullOrEmpty(stored) || stored == "none";
                var shortcut = none ? KeyboardShortcut.Empty : KeyboardShortcut.Deserialize(stored);

                // Deserialize answers text it cannot read with no key at all, which is not the
                // key that was meant, so it is left out rather than taken over as no key.
                if (!none && shortcut.MainKey == KeyCode.None && !string.Equals(stored, "None", StringComparison.OrdinalIgnoreCase))
                    return null;

                return isKeyCode
                    ? TomlTypeConverter.ConvertToString(shortcut.MainKey, typeof(KeyCode))
                    : TomlTypeConverter.ConvertToString(shortcut, typeof(KeyboardShortcut));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
