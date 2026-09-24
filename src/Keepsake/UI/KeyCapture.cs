using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace Keepsake.UI
{
    /// <summary>
    /// Waits for the next key pressed and hands it over in the form a keybind setting stores it.
    /// For a KeyboardShortcut, the modifiers held at that moment come with it; a setting of a
    /// single KeyCode takes the key alone. A modifier pressed and released with nothing else
    /// becomes the key itself. Escape cancels, and the left mouse button is never taken, since it
    /// is what the panel's buttons are clicked with.
    ///
    /// While it waits, every key belongs to it: Plugin.Update hands the frame here and does
    /// nothing else, so the key pressed does not also open, close or type anywhere.
    /// </summary>
    public static class KeyCapture
    {
        private static readonly KeyCode[] Modifiers =
        {
            KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftCommand, KeyCode.RightCommand,
        };

        private static KeyCode[] _candidates;

        private static Setting _setting;
        private static Action<string> _done;

        /// <summary>The modifier pressed while waiting, which becomes the key if nothing else follows.</summary>
        private static KeyCode _lone;

        public static bool Active => _setting != null;

        public static bool IsCapturingFor(Setting setting) => setting != null && _setting == setting;

        /// <summary>Called when the capture starts or ends, so the panel can say so.</summary>
        public static Action Changed;

        public static void Start(Setting setting, Action<string> done)
        {
            _setting = setting;
            _done = done;
            _lone = KeyCode.None;
            Changed?.Invoke();
        }

        public static void Cancel()
        {
            if (_setting == null) return;
            _setting = null;
            _done = null;
            Changed?.Invoke();
        }

        public static void Tick()
        {
            if (_setting == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }

            foreach (var key in Candidates())
            {
                if (!Input.GetKeyDown(key)) continue;

                if (Modifiers.Contains(key))
                {
                    _lone = key;
                    continue;
                }

                Finish(key, Modifiers.Where(Input.GetKey).ToArray());
                return;
            }

            // A modifier let go of before any other key is the key wanted.
            if (_lone != KeyCode.None && Input.GetKeyUp(_lone)) Finish(_lone, new KeyCode[0]);
        }

        /// <summary>The setting's value for no key at all.</summary>
        public static string NoKey(Setting setting) => AsValue(setting, KeyCode.None, new KeyCode[0]);

        private static void Finish(KeyCode key, KeyCode[] modifiers)
        {
            var done = _done;
            var value = AsValue(_setting, key, modifiers);

            _setting = null;
            _done = null;
            Changed?.Invoke();

            if (value != null) done?.Invoke(value);
        }

        private static string AsValue(Setting setting, KeyCode key, KeyCode[] modifiers)
        {
            try
            {
                if (setting.Entry.SettingType == typeof(KeyCode))
                    return TomlTypeConverter.ConvertToString(key, typeof(KeyCode));

                var shortcut = key == KeyCode.None ? KeyboardShortcut.Empty : new KeyboardShortcut(key, modifiers);
                return TomlTypeConverter.ConvertToString(shortcut, typeof(KeyboardShortcut));
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce($"Keepsake: could not write {key} as a key: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>Every key worth listening for, each value once, without the left mouse button.</summary>
        private static KeyCode[] Candidates() =>
            _candidates ?? (_candidates = Enum.GetValues(typeof(KeyCode)).Cast<KeyCode>()
                .Distinct()
                .Where(k => k != KeyCode.None && k != KeyCode.Mouse0 && k != KeyCode.Escape)
                .ToArray());
    }
}
