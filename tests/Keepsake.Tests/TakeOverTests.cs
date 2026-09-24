using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// Taking over the keys Bindrune held, run on the shared sample: which keys match which
    /// settings, and what each becomes in the setting's own form.
    /// </summary>
    public class TakeOverTests
    {
        private static List<BindruneKey> SampleKeys() =>
            BindruneLink.ParseKeys(File.ReadAllLines(Path.Combine("contract", "bindrune.keys")));

        private static KeybindSlot<string> Slot(string section, string key, bool keyCode) => new KeybindSlot<string>
        {
            BindruneId = BindruneKeys.IdOf("isimp.Example", section, key),
            IsKeyCode = keyCode,
            Setting = section + "/" + key,
        };

        [Fact]
        public void KeysInUseAreMatchedToTheirSettings()
        {
            var matches = BindruneKeys.Match(SampleKeys(), new[]
            {
                Slot("Keys", "Open", keyCode: true),
                Slot("Keys", "Shortcut", keyCode: false),
                Slot("Section: with colon", "Toggle", keyCode: true),
            });

            // Toggle is kept aside in Bindrune rather than in use, so it is not offered.
            Assert.Equal(new[] { "Keys/Open", "Keys/Shortcut" }, matches.Select(m => m.Setting));
        }

        [Fact]
        public void AKeyForAnUnloadedSettingIsLeftOut() =>
            Assert.Empty(BindruneKeys.Match(SampleKeys(), new[] { Slot("Keys", "Other", keyCode: true) }));

        [Fact]
        public void ASectionWithAColonStillMatches()
        {
            var keys = new[] { new BindruneKey { Id = "cfg:isimp.Example:Section: with colon:Toggle", Yours = "F7", Profile = "F8", Active = true } };
            var match = BindruneKeys.Match(keys, new[] { Slot("Section: with colon", "Toggle", keyCode: true) }).Single();

            Assert.Equal("F7", match.Yours);
            Assert.Equal("F8", match.Profile);
        }

        [Fact]
        public void AKeyCodeSettingGetsItsKey()
        {
            var match = BindruneKeys.Match(SampleKeys(), new[] { Slot("Keys", "Open", keyCode: true) }).Single();
            Assert.Equal("K", match.Yours);
            Assert.Equal("H", match.Profile);
        }

        [Fact]
        public void AShortcutSettingGetsItsModifiers()
        {
            var match = BindruneKeys.Match(SampleKeys(), new[] { Slot("Keys", "Shortcut", keyCode: false) }).Single();
            Assert.Equal("H + LeftAlt", match.Yours);
        }

        [Fact]
        public void AKeyCodeSettingTakesTheMainKeyOfAShortcut() =>
            Assert.Equal("H", BindruneKeys.AsSetting("H + LeftAlt", isKeyCode: true));

        [Fact]
        public void NoKeyIsTheSettingsOwnNone()
        {
            Assert.Equal(TomlTypeConverter.ConvertToString(KeyCode.None, typeof(KeyCode)), BindruneKeys.AsSetting("none", isKeyCode: true));
            Assert.Equal(TomlTypeConverter.ConvertToString(KeyboardShortcut.Empty, typeof(KeyboardShortcut)),
                BindruneKeys.AsSetting("none", isKeyCode: false));
        }

        [Fact]
        public void AKeyThatCannotBeReadIsLeftOutRatherThanTakenAsNoKey()
        {
            Assert.Null(BindruneKeys.AsSetting("Not + A + Key", isKeyCode: false));

            var keys = new[] { new BindruneKey { Id = BindruneKeys.IdOf("isimp.Example", "Keys", "Open"), Yours = "Not + A + Key", Active = true } };
            Assert.Empty(BindruneKeys.Match(keys, new[] { Slot("Keys", "Open", keyCode: true) }));
        }

        [Fact]
        public void AnAliasedKeyReadsBackAsTheSameKey()
        {
            // RightMeta, RightCommand and RightApple are one KeyCode under three names.
            var stored = BindruneKeys.AsSetting("RightMeta", isKeyCode: true);
            Assert.Equal(KeyCode.RightMeta, (KeyCode)TomlTypeConverter.ConvertToValue(stored, typeof(KeyCode)));
        }

        [Fact]
        public void WhatTheSettingStoresReadsBackAsTheSameShortcut()
        {
            var stored = BindruneKeys.AsSetting("H + LeftAlt + LeftShift", isKeyCode: false);
            var shortcut = (KeyboardShortcut)TomlTypeConverter.ConvertToValue(stored, typeof(KeyboardShortcut));

            Assert.Equal(KeyCode.H, shortcut.MainKey);
            Assert.Equal(new HashSet<KeyCode> { KeyCode.LeftAlt, KeyCode.LeftShift }, new HashSet<KeyCode>(shortcut.Modifiers));
        }
    }
}
