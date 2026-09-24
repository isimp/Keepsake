using Xunit;

namespace Keepsake.Tests
{
    /// <summary>The preloader's view of a cfg file, which has to agree with how BepInEx reads one.</summary>
    public class CfgTextTests
    {
        private const string Cfg =
            "## Settings file was created by plugin Example v1.0.0\r\n" +
            "## Plugin GUID: isimp.Example\r\n" +
            "\r\n" +
            "[General]\r\n" +
            "\r\n" +
            "## Turns it on.\r\n" +
            "# Setting type: Boolean\r\n" +
            "# Default value: true\r\n" +
            "Enabled = true\r\n" +
            "\r\n" +
            "[Keys]\r\n" +
            "\r\n" +
            "## Opens it.\r\n" +
            "## Second line of the description.\r\n" +
            "# Setting type: KeyCode\r\n" +
            "# Default value: H\r\n" +
            "Open = H\r\n" +
            "\r\n" +
            "# Setting type: KeyboardShortcut\r\n" +
            "Shortcut = H + LeftAlt\r\n" +
            "Enabled = false\r\n";

        [Fact]
        public void SettingAValueChangesOnlyItsLine()
        {
            var cfg = CfgText.Parse(Cfg);
            Assert.True(cfg.Set("Keys", "Open", "K"));

            Assert.True(cfg.Changed);
            Assert.Equal(Cfg.Replace("Open = H\r\n", "Open = K\r\n"), cfg.Text);
        }

        [Fact]
        public void TheSameValueChangesNothing()
        {
            var cfg = CfgText.Parse(Cfg);
            Assert.True(cfg.Set("Keys", "Open", "H"));
            Assert.False(cfg.Changed);
        }

        [Fact]
        public void ASettingIsFoundOnlyInItsOwnSection()
        {
            var cfg = CfgText.Parse(Cfg);

            Assert.True(cfg.TryGet("General", "Enabled", out var general));
            Assert.Equal("true", general);
            Assert.True(cfg.TryGet("Keys", "Enabled", out var keys));
            Assert.Equal("false", keys);
        }

        [Fact]
        public void AMissingSettingIsReportedAndNotAdded()
        {
            var cfg = CfgText.Parse(Cfg);

            Assert.False(cfg.TryGet("Keys", "Missing", out _));
            Assert.False(cfg.Set("Keys", "Missing", "X"));
            Assert.Equal(Cfg, cfg.Text);
        }

        [Fact]
        public void TheLastLineOfASettingIsTheOneThatCountsLikeInBepInEx()
        {
            var cfg = CfgText.Parse("[S]\nK = first\nK = second\n");
            Assert.True(cfg.TryGet("S", "K", out var value));
            Assert.Equal("second", value);

            cfg.Set("S", "K", "third");
            Assert.Equal("[S]\nK = first\nK = third\n", cfg.Text);
        }

        [Theory]
        [InlineData("General", "Enabled", "Boolean")]
        [InlineData("Keys", "Open", "KeyCode")]
        [InlineData("Keys", "Shortcut", "KeyboardShortcut")]
        public void TheTypeIsReadFromTheNoteAboveTheSetting(string section, string key, string type) =>
            Assert.Equal(type, CfgText.Parse(Cfg).TypeOf(section, key));

        [Fact]
        public void ASettingWithoutATypeNoteHasNoType() =>
            Assert.Null(CfgText.Parse(Cfg).TypeOf("Keys", "Enabled"));

        [Theory]
        [InlineData("KeyCode", true)]
        [InlineData("KeyboardShortcut", true)]
        [InlineData("String", false)]
        [InlineData(null, false)]
        public void OnlyKeyTypesAreKeybinds(string type, bool keybind) =>
            Assert.Equal(keybind, BindruneLink.IsKeybindType(type));
    }
}
