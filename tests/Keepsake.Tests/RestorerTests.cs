using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// The preloader's pass at launch: which values go back into the cfg files, what it records as
    /// the profile's value, and when it reports that the profile changed one.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class RestorerTests
    {
        private const string Cfg =
            "[General]\n" +
            "\n" +
            "## How fast.\n" +
            "# Setting type: Int32\n" +
            "# Default value: 5\n" +
            "Speed = {0}\n" +
            "\n" +
            "## Opens the menu.\n" +
            "# Setting type: KeyCode\n" +
            "# Default value: F1\n" +
            "Open = F1\n";

        private static string CfgWith(string speed) => string.Format(Cfg, speed);

        private static Pin SpeedPin(string value, string profile) =>
            new Pin { File = "a.cfg", Section = "General", Key = "Speed", Value = value, Profile = profile };

        [Fact]
        public void YourValueGoesBackAndWhatItReplacesIsTheProfiles()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("5"));
            var pin = SpeedPin("9", "5");

            var result = Restorer.Apply(new[] { pin }.ToList(), bindrune: false);

            Assert.Equal(1, result.Restored);
            Assert.Equal(CfgWith("9"), File.ReadAllText(profile.CfgPath("a.cfg")));
            Assert.Equal("5", pin.Profile);
            Assert.False(result.Learned);
            Assert.Empty(result.Changes);
        }

        [Fact]
        public void AProfileValueSeenForTheFirstTimeIsRecordedButNotReportedAsAChange()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("5"));
            var pin = SpeedPin("9", null);

            var result = Restorer.Apply(new[] { pin }.ToList(), bindrune: false);

            Assert.Equal("5", pin.Profile);
            Assert.True(result.Learned);
            Assert.Empty(result.Changes);
        }

        [Fact]
        public void AProfileValueThatMovedIsReported()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("7"));
            var pin = SpeedPin("9", "5");

            var result = Restorer.Apply(new[] { pin }.ToList(), bindrune: false);

            var change = Assert.Single(result.Changes);
            Assert.Equal(pin.Id, change.Id);
            Assert.Equal("5", change.From);
            Assert.Equal("7", change.To);
            Assert.Equal("7", pin.Profile);
            Assert.True(result.Learned);
            Assert.Equal(CfgWith("9"), File.ReadAllText(profile.CfgPath("a.cfg")));
        }

        [Fact]
        public void YourValueAlreadyInPlaceLeavesTheFileAlone()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("9"));
            var written = File.GetLastWriteTimeUtc(profile.CfgPath("a.cfg"));
            var pin = SpeedPin("9", "5");

            var result = Restorer.Apply(new[] { pin }.ToList(), bindrune: false);

            Assert.Equal(0, result.Restored);
            Assert.False(result.Learned);
            Assert.Empty(result.Changes);
            Assert.Equal("5", pin.Profile);
            Assert.Equal(written, File.GetLastWriteTimeUtc(profile.CfgPath("a.cfg")));
        }

        [Fact]
        public void AFileOrLineNotThereYetIsLeftForThePlugin()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("5"));
            var pins = new[]
            {
                new Pin { File = "missing.cfg", Section = "General", Key = "Speed", Value = "9" },
                new Pin { File = "a.cfg", Section = "General", Key = "Later", Value = "9" },
            }.ToList();

            var result = Restorer.Apply(pins, bindrune: false);

            Assert.Equal(2, result.Missing);
            Assert.Equal(0, result.Restored);
            Assert.Equal(CfgWith("5"), File.ReadAllText(profile.CfgPath("a.cfg")));
        }

        [Fact]
        public void KeybindsAreLeftToBindruneWhileItIsInstalled()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("5"));
            Pin OpenPin() => new Pin { File = "a.cfg", Section = "General", Key = "Open", Value = "F7", Profile = "F1" };

            var left = Restorer.Apply(new[] { OpenPin() }.ToList(), bindrune: true);
            Assert.Equal(1, left.ToBindrune);
            Assert.Equal(CfgWith("5"), File.ReadAllText(profile.CfgPath("a.cfg")));

            var kept = Restorer.Apply(new[] { OpenPin() }.ToList(), bindrune: false);
            Assert.Equal(1, kept.Restored);
            Assert.Contains("Open = F7", File.ReadAllText(profile.CfgPath("a.cfg")));
        }

        [Fact]
        public void LeftoverFilesAreRemovedAndNothingElse()
        {
            using var profile = new TestProfile();
            Directory.CreateDirectory(Path.Combine(profile.ConfigDir, "sub"));

            var leftovers = new[]
            {
                Path.Combine(profile.ConfigDir, "a.cfg" + PinFile.TempSuffix),
                Path.Combine(profile.ConfigDir, "sub", "b.cfg" + PinFile.TempSuffix),
                Path.Combine(profile.Root, "keepsake.pins" + PinFile.TempSuffix),
            };
            var others = new[]
            {
                Path.Combine(profile.ConfigDir, "a.cfg"),
                Path.Combine(profile.ConfigDir, "a.cfg" + PinFile.TempSuffix + "x"),
                Path.Combine(profile.Root, "keepsake.pins"),
            };
            foreach (var file in leftovers.Concat(others)) File.WriteAllText(file, "x");

            // Outside BepInEx/config, only the root folder itself holds Keepsake's own file.
            var deeper = Path.Combine(profile.Root, "plugins", "c.cfg" + PinFile.TempSuffix);
            Directory.CreateDirectory(Path.GetDirectoryName(deeper)!);
            File.WriteAllText(deeper, "x");

            var removed = Restorer.RemoveLeftovers(profile.ConfigDir, profile.Root);

            Assert.Equal(3, removed);
            Assert.All(leftovers, f => Assert.False(File.Exists(f), f));
            Assert.All(others.Append(deeper), f => Assert.True(File.Exists(f), f));
        }

        [Fact]
        public void ChangesAreHandedOverOnce()
        {
            using var profile = new TestProfile();
            ProfileChanges.Hand(new[] { new ProfileChange { Id = "a.cfg\tGeneral\tSpeed", From = "5", To = "7" } });

            var change = Assert.Single(ProfileChanges.Take());
            Assert.Equal("a.cfg\tGeneral\tSpeed", change.Id);
            Assert.Equal("5", change.From);
            Assert.Equal("7", change.To);

            Assert.Empty(ProfileChanges.Take());
        }

        [Fact]
        public void ASecondPreloaderFindingNothingKeepsWhatTheFirstFound()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("7"));
            var pins = new[] { SpeedPin("9", "5") }.ToList();

            ProfileChanges.Hand(Restorer.Apply(pins, bindrune: false).Changes);
            ProfileChanges.Hand(Restorer.Apply(pins, bindrune: false).Changes);

            var change = Assert.Single(ProfileChanges.Take());
            Assert.Equal("7", change.To);
        }
    }
}
