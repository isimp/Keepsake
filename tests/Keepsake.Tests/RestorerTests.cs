using System.Collections.Generic;
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

        [Theory]
        [InlineData("../outside.cfg")]
        [InlineData("sub/../../outside.cfg")]
        [InlineData("/outside.cfg")]
        [InlineData("C:/outside.cfg")]
        public void AKeptSettingInAFileOutsideConfigIsNeverWritten(string file)
        {
            using var profile = new TestProfile();
            var outside = Path.Combine(profile.Root, "outside.cfg");
            File.WriteAllText(outside, CfgWith("5"));
            if (file.StartsWith("/") || file.StartsWith("C:")) file = outside.Replace('\\', '/');
            profile.WritePins($"{file}\tGeneral\tSpeed\t9\t5");

            profile.Launch();

            Assert.Equal(CfgWith("5"), File.ReadAllText(outside));
            Assert.Empty(PinFile.Read()!);
        }

        [Fact]
        public void ACfgFileThatCannotBeWrittenAtLaunchGetsYourValueOnceTheModLoads()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("5"));
            profile.WritePins("a.cfg\tGeneral\tSpeed\t9\t5");

            using (new FileStream(profile.CfgPath("a.cfg"), FileMode.Open, FileAccess.Read, FileShare.Read))
                profile.Launch();
            Assert.Equal(CfgWith("5"), File.ReadAllText(profile.CfgPath("a.cfg")));

            // The mod binds its settings from the file as it is, and the plugin puts yours in.
            var config = profile.Mod("a.cfg");
            var speed = config.Bind("General", "Speed", 5, "How fast.");
            profile.Index();
            Keeper.Reconcile();

            Assert.Equal(9, speed.Value);
            Assert.Equal("5", Keeper.Find(PinFile.IdOf("a.cfg", "General", "Speed"))!.Profile);
        }

        [Fact]
        public void ACfgFileThatCannotBeReadAtLaunchLeavesWhatWasKeptAsItWas()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("7"));
            profile.WritePins("a.cfg\tGeneral\tSpeed\t9\t5");
            var pins = File.ReadAllBytes(PinFile.FilePath);

            using (TestProfile.Lock(profile.CfgPath("a.cfg")))
                profile.Launch();

            Assert.Equal(pins, File.ReadAllBytes(PinFile.FilePath));
            Assert.Empty(Keeper.ProfileChanged());
        }

        [Fact]
        public void AKeybindIsYoursToKeepWhileBindruneIsDisabledByItsModManager()
        {
            using var profile = new TestProfile();
            var plugin = Path.Combine(profile.Root, "plugins", "isimp-Bindrune");
            Directory.CreateDirectory(plugin);
            File.WriteAllText(Path.Combine(plugin, "Bindrune.dll.old"), "disabled");
            Assert.False(BindruneLink.InstalledOnDisk());

            File.WriteAllText(Path.Combine(plugin, "Bindrune.dll"), "enabled");
            Assert.True(BindruneLink.InstalledOnDisk());

            // Deeper than a mod manager puts it still counts.
            File.Delete(Path.Combine(plugin, "Bindrune.dll"));
            var deep = Path.Combine(plugin, "lib", "net");
            Directory.CreateDirectory(deep);
            File.WriteAllText(Path.Combine(deep, "Bindrune.dll"), "enabled");
            Assert.True(BindruneLink.InstalledOnDisk());
        }

        [Fact]
        public void YourValueGoesBackAndWhatItReplacesIsTheProfiles()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("5"));
            var pin = SpeedPin("9", "5");

            var result = Restorer.Apply(new[] { pin }.ToList(), bindruneInstalled: () => false);

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

            var result = Restorer.Apply(new[] { pin }.ToList(), bindruneInstalled: () => false);

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

            var result = Restorer.Apply(new[] { pin }.ToList(), bindruneInstalled: () => false);

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

            var result = Restorer.Apply(new[] { pin }.ToList(), bindruneInstalled: () => false);

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

            var result = Restorer.Apply(pins, bindruneInstalled: () => false);

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

            var left = Restorer.Apply(new[] { OpenPin() }.ToList(), bindruneInstalled: () => true);
            Assert.Equal(1, left.ToBindrune);
            Assert.Equal(CfgWith("5"), File.ReadAllText(profile.CfgPath("a.cfg")));

            var kept = Restorer.Apply(new[] { OpenPin() }.ToList(), bindruneInstalled: () => false);
            Assert.Equal(1, kept.Restored);
            Assert.Contains("Open = F7", File.ReadAllText(profile.CfgPath("a.cfg")));
        }

        [Fact]
        public void LeftoverFilesOfWhatKeepsakeWritesAreRemovedAndNothingElse()
        {
            using var profile = new TestProfile();
            Directory.CreateDirectory(Path.Combine(profile.ConfigDir, "sub"));

            var written = new[]
            {
                profile.CfgPath("a.cfg"),
                Path.Combine(profile.ConfigDir, "sub", "b.cfg"),
                profile.PinsPath,
                ProfileChanges.FilePath,
            };
            var leftovers = written.Take(3).Select(f => f + PinFile.TempSuffix).ToArray();
            var others = new[]
            {
                profile.CfgPath("a.cfg"),
                profile.CfgPath("a.cfg" + PinFile.TempSuffix + "x"),
                profile.CfgPath("other.cfg" + PinFile.TempSuffix),
                profile.PinsPath,
            };
            foreach (var file in leftovers.Concat(others)) File.WriteAllText(file, "x");

            var removed = Restorer.RemoveLeftovers(written);

            Assert.Equal(3, removed);
            Assert.All(leftovers, f => Assert.False(File.Exists(f), f));
            Assert.All(others, f => Assert.True(File.Exists(f), f));
        }

        [Fact]
        public void BindruneIsOnlyLookedForWhenAKeptSettingIsAKeybind()
        {
            using var profile = new TestProfile();
            profile.WriteCfg("a.cfg", CfgWith("5"));
            var asked = 0;
            bool Installed()
            {
                asked++;
                return true;
            }

            Restorer.Apply(new[] { SpeedPin("9", "5") }.ToList(), Installed);
            Assert.Equal(0, asked);

            var pins = new[]
            {
                new Pin { File = "a.cfg", Section = "General", Key = "Open", Value = "F7", Profile = "F1" },
                SpeedPin("9", "5"),
            }.ToList();
            var result = Restorer.Apply(pins, Installed);
            Assert.Equal(1, asked);
            Assert.Equal(1, result.ToBindrune);
        }

        // ---------- changes waiting for an answer ----------

        /// <summary>One launch of the preloader with the cfg holding the profile's value given.</summary>
        private static RestoreResult Launch(TestProfile profile, List<Pin> pins, string profileValue)
        {
            profile.WriteCfg("a.cfg", CfgWith(profileValue));
            var result = Restorer.Apply(pins, bindruneInstalled: () => false);
            Restorer.RecordChanges(pins, result.Changes);
            return result;
        }

        [Fact]
        public void AChangeWaitsAcrossLaunchesUntilAnswered()
        {
            using var profile = new TestProfile();
            var pins = new[] { SpeedPin("9", "5") }.ToList();

            Launch(profile, pins, "7");
            Launch(profile, pins, "9");

            var change = Assert.Single(ProfileChanges.Read().Waiting);
            Assert.Equal(pins[0].Id, change.Id);
            Assert.Equal("5", change.From);
            Assert.Equal("7", change.To);
        }

        [Fact]
        public void AProfileThatChangesAgainKeepsWhereItStarted()
        {
            using var profile = new TestProfile();
            var pins = new[] { SpeedPin("9", "5") }.ToList();

            Launch(profile, pins, "7");
            Launch(profile, pins, "8");

            var change = Assert.Single(ProfileChanges.Read().Waiting);
            Assert.Equal("5", change.From);
            Assert.Equal("8", change.To);
        }

        [Fact]
        public void AProfileThatGoesBackIsNoChangeAnyMore()
        {
            using var profile = new TestProfile();
            var pins = new[] { SpeedPin("9", "5") }.ToList();

            Launch(profile, pins, "7");
            Launch(profile, pins, "5");

            Assert.Empty(ProfileChanges.Read().Waiting);
            Assert.False(File.Exists(ProfileChanges.FilePath));
        }

        [Fact]
        public void ChangesOfSettingsNoLongerKeptAreDropped()
        {
            using var profile = new TestProfile();
            Launch(profile, new[] { SpeedPin("9", "5") }.ToList(), "7");

            Restorer.RecordChanges(new List<Pin>(), new List<ProfileChange>());

            Assert.Empty(ProfileChanges.Read().Waiting);
        }

        [Fact]
        public void AChangesFileOfANewerVersionIsNeverWrittenOver()
        {
            using var profile = new TestProfile();
            var newer = "# keepsake changes v99\na.cfg\tGeneral\tSpeed\t1\t2\n";
            File.WriteAllText(ProfileChanges.FilePath, newer);

            Launch(profile, new[] { SpeedPin("9", "5") }.ToList(), "7");

            Assert.Null(ProfileChanges.Read());
            Assert.Equal(newer, File.ReadAllText(ProfileChanges.FilePath));
        }

        // ---------- quiet settings ----------

        private static void MakeQuiet(Pin pin)
        {
            var state = new ChangeState();
            state.Quiet.Add(QuietSetting.Of(pin));
            ProfileChanges.Write(state);
        }

        [Fact]
        public void AQuietSettingsChangeIsRecordedWithoutAsking()
        {
            using var profile = new TestProfile();
            var pins = new[] { SpeedPin("9", "5") }.ToList();
            MakeQuiet(pins[0]);

            var result = Launch(profile, pins, "7");

            // Release still has the profile's latest value to put back.
            Assert.Equal("7", pins[0].Profile);
            Assert.Single(result.Changes);
            Assert.Empty(ProfileChanges.Read().Waiting);
            Assert.True(ProfileChanges.Read().IsQuiet(pins[0].Id));
        }

        [Fact]
        public void AQuietMarkGoesWithTheSettingOnceItIsNoLongerKept()
        {
            using var profile = new TestProfile();
            MakeQuiet(SpeedPin("9", "5"));

            Restorer.RecordChanges(new List<Pin>(), new List<ProfileChange>());

            Assert.False(File.Exists(ProfileChanges.FilePath));
        }

        [Fact]
        public void TheChangesFileReadsBackWhatWasWritten()
        {
            using var profile = new TestProfile();
            var state = new ChangeState();
            state.Waiting.Add(ProfileChange.Of(SpeedPin("9", "5"), "5", "7"));
            state.Quiet.Add(new QuietSetting { File = "sub/b.cfg", Section = "Section: with colon", Key = "Volume" });

            ProfileChanges.Write(state);
            var read = ProfileChanges.Read();

            var change = Assert.Single(read.Waiting);
            Assert.Equal(("a.cfg", "General", "Speed", "5", "7"), (change.File, change.Section, change.Key, change.From, change.To));
            var quiet = Assert.Single(read.Quiet);
            Assert.Equal(("sub/b.cfg", "Section: with colon", "Volume"), (quiet.File, quiet.Section, quiet.Key));
        }
    }
}
