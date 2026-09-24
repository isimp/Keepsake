using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// Keeping, releasing and setting values while the game runs, against real BepInEx settings
    /// in a temporary profile: what is kept, what is recorded as the profile's value, and what the
    /// pins file holds afterwards.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class KeeperTests
    {
        private static string Line(string key, string value, string profile) =>
            $"a.cfg\tGeneral\t{key}\t{value}\t{profile}";

        private static ConfigEntry<int> Speed(TestProfile profile, AcceptableValueBase acceptable = null) =>
            profile.Mod("a.cfg").Bind("General", "Speed", 5, new ConfigDescription("How fast.", acceptable));

        // ---------- keeping and releasing ----------

        [Fact]
        public void KeepingRecordsTheValueAtLaunchAsTheProfilesNotYours()
        {
            using var profile = new TestProfile();
            var speed = Speed(profile);
            profile.Index();

            // Changed in a config manager first, then kept.
            speed.Value = 8;
            Assert.Null(Keeper.Pin(profile.Setting("a.cfg", "General", "Speed")));

            Assert.Equal(new[] { Line("Speed", "8", "5") }, profile.PinLines());
        }

        [Fact]
        public void ReleasingPutsTheProfilesValueBack()
        {
            using var profile = new TestProfile();
            var speed = Speed(profile);
            profile.Index();
            speed.Value = 8;
            var setting = profile.Setting("a.cfg", "General", "Speed");
            Keeper.Pin(setting);

            Keeper.Unpin(setting.Id);

            Assert.Equal(5, speed.Value);
            Assert.Empty(profile.PinLines());
            Assert.Contains("Speed = 5", File.ReadAllText(profile.CfgPath("a.cfg")));
        }

        [Fact]
        public void KeepingAgainAfterAReleaseRemembersTheProfilesValue()
        {
            using var profile = new TestProfile();
            profile.WritePins(Line("Speed", "8", "5"));
            profile.WriteCfg("a.cfg", "[General]\nSpeed = 8\n");
            var speed = Speed(profile);
            profile.Index();
            var setting = profile.Setting("a.cfg", "General", "Speed");

            // Kept at launch, so the value at launch was already yours.
            Keeper.Unpin(setting.Id);
            speed.Value = 9;
            Keeper.Pin(setting);

            Assert.Equal(new[] { Line("Speed", "9", "5") }, profile.PinLines());
        }

        [Fact]
        public void KeepAllKeepsEveryChangeInOneGoAndSkipsWhatIsKept()
        {
            using var profile = new TestProfile();
            var config = profile.Mod("a.cfg");
            var speed = config.Bind("General", "Speed", 5, "How fast.");
            var size = config.Bind("General", "Size", 1.5f, "How big.");
            var name = config.Bind("General", "Name", "north", "What it is called.");
            profile.Index();

            speed.Value = 8;
            Keeper.Pin(profile.Setting("a.cfg", "General", "Speed"));
            size.Value = 2.5f;
            name.Value = "south";

            var kept = Keeper.PinAll(Session.Changed());

            Assert.Equal(2, kept);
            Assert.Equal(new[] { Line("Name", "south", "north"), Line("Size", "2.5", "1.5"), Line("Speed", "8", "5") }, profile.PinLines());
        }

        // ---------- your value ----------

        [Fact]
        public void AChangeMadeElsewhereIsFollowed()
        {
            using var profile = new TestProfile();
            var speed = Speed(profile);
            profile.Index();
            Keeper.Pin(profile.Setting("a.cfg", "General", "Speed"));

            speed.Value = 12;

            Assert.Equal(new[] { Line("Speed", "12", "5") }, profile.PinLines());
        }

        [Fact]
        public void KeepsakesOwnWritesDoNotCountAsChangesThisSession()
        {
            using var profile = new TestProfile();
            Speed(profile);
            profile.Index();
            var setting = profile.Setting("a.cfg", "General", "Speed");
            Keeper.Pin(setting);

            Assert.Null(Keeper.SetValue(setting, "7"));

            Assert.Equal("7", setting.Current);
            Assert.False(Session.IsChanged(setting));
        }

        [Fact]
        public void AValueTheSettingCannotReadIsRefused()
        {
            using var profile = new TestProfile();
            Speed(profile);
            profile.Index();
            var setting = profile.Setting("a.cfg", "General", "Speed");
            Keeper.Pin(setting);

            Assert.NotNull(Keeper.SetValue(setting, "fast"));

            Assert.Equal("5", setting.Current);
            Assert.Equal(new[] { Line("Speed", "5", "5") }, profile.PinLines());
        }

        [Fact]
        public void AValueOutsideTheRangeIsKeptAsTheSettingTakesIt()
        {
            using var profile = new TestProfile();
            Speed(profile, new AcceptableValueRange<int>(0, 10));
            profile.Index();
            var setting = profile.Setting("a.cfg", "General", "Speed");
            Keeper.Pin(setting);

            Assert.Null(Keeper.SetValue(setting, "50"));

            Assert.Equal(new[] { Line("Speed", "10", "5") }, profile.PinLines());
        }

        // ---------- values put in after launch ----------

        [Fact]
        public void ReconcilePutsInAValueTheFileDidNotHaveYet()
        {
            using var profile = new TestProfile();
            profile.WritePins("a.cfg\tGeneral\tSpeed\t8");
            var speed = Speed(profile);

            Assert.Equal(0, Keeper.Reconcile());

            Assert.Equal(8, speed.Value);
            Assert.Equal(new[] { Line("Speed", "8", "5") }, profile.PinLines());
            Assert.Empty(Keeper.ProfileChanged());
        }

        [Fact]
        public void ReconcileReportsAProfileValueThatMoved()
        {
            using var profile = new TestProfile();
            profile.WritePins(Line("Speed", "8", "5"));
            profile.WriteCfg("a.cfg", "[General]\nSpeed = 7\n");
            var speed = Speed(profile);

            Keeper.Reconcile();

            var id = profile.Setting("a.cfg", "General", "Speed").Id;
            var change = Keeper.ProfileChangeOf(id);
            Assert.NotNull(change);
            Assert.Equal("5", change.From);
            Assert.Equal("7", change.To);
            Assert.Equal(8, speed.Value);
        }

        [Fact]
        public void ReconcileCountsKeptSettingsNoModHasBound()
        {
            using var profile = new TestProfile();
            profile.WritePins(Line("Speed", "8", "5"));

            Assert.Equal(1, Keeper.Reconcile());
        }

        // ---------- the profile's changes ----------

        /// <summary>A launch at which the preloader found the profile had moved Speed from 5 to 7.</summary>
        private static (TestProfile profile, ConfigEntry<int> speed, Setting setting) ProfileMovedSpeed()
        {
            var profile = new TestProfile();
            profile.WritePins(Line("Speed", "8", "7"));
            profile.WriteCfg("a.cfg", "[General]\nSpeed = 8\n");
            ProfileChanges.Hand(new[] { new ProfileChange { Id = PinFile.IdOf("a.cfg", "General", "Speed"), From = "5", To = "7" } });

            var speed = Speed(profile);
            profile.Index();
            return (profile, speed, profile.Setting("a.cfg", "General", "Speed"));
        }

        [Fact]
        public void TheProfilesChangesFromThePreloaderAreShown()
        {
            var (profile, _, setting) = ProfileMovedSpeed();
            using (profile)
            {
                Assert.Equal(new[] { setting.Id }, Keeper.ProfileChanged());
                Assert.Equal("5", Keeper.ProfileChangeOf(setting.Id).From);
            }
        }

        [Fact]
        public void UsingTheProfilesValueKeepsTheSettingAtIt()
        {
            var (profile, speed, setting) = ProfileMovedSpeed();
            using (profile)
            {
                Assert.Null(Keeper.UseProfiles(setting));

                Assert.Equal(7, speed.Value);
                Assert.Equal(new[] { Line("Speed", "7", "7") }, profile.PinLines());
                Assert.Empty(Keeper.ProfileChanged());
            }
        }

        [Fact]
        public void KeepingYoursLeavesTheValueAndClearsTheChange()
        {
            var (profile, speed, setting) = ProfileMovedSpeed();
            using (profile)
            {
                Keeper.KeepMine(setting.Id);

                Assert.Equal(8, speed.Value);
                Assert.Equal(new[] { Line("Speed", "8", "7") }, profile.PinLines());
                Assert.Empty(Keeper.ProfileChanged());
            }
        }

        [Fact]
        public void SettingAnotherValueOrReleasingClearsTheChange()
        {
            var (profile, _, setting) = ProfileMovedSpeed();
            using (profile)
            {
                Keeper.SetValue(setting, "9");
                Assert.Empty(Keeper.ProfileChanged());
            }

            (profile, _, setting) = ProfileMovedSpeed();
            using (profile)
            {
                Keeper.Unpin(setting.Id);
                Assert.Empty(Keeper.ProfileChanged());
                Assert.Null(Keeper.ProfileChangeOf(setting.Id));
            }
        }

        // ---------- the pins file ----------

        [Fact]
        public void AHandEditIsTakenInBeforeTheNextWrite()
        {
            using var profile = new TestProfile();
            var config = profile.Mod("a.cfg");
            config.Bind("General", "Speed", 5, "How fast.");
            config.Bind("General", "Size", 1.5f, "How big.");
            profile.Index();
            Keeper.Pin(profile.Setting("a.cfg", "General", "Speed"));

            File.AppendAllText(profile.PinsPath, "b.cfg\tOther\tColour\tred\tblue\n");
            Keeper.Pin(profile.Setting("a.cfg", "General", "Size"));

            Assert.Equal(new[] { Line("Size", "1.5", "1.5"), Line("Speed", "5", "5"), "b.cfg\tOther\tColour\tred\tblue" }, profile.PinLines());
        }

        [Fact]
        public void APinsFileOfANewerVersionIsNeverWrittenOver()
        {
            using var profile = new TestProfile();
            var newer = "# keepsake pins v99\na.cfg\tGeneral\tSpeed\t8\t5\n";
            File.WriteAllText(profile.PinsPath, newer);
            Speed(profile);
            profile.Index();

            Keeper.Pin(profile.Setting("a.cfg", "General", "Speed"));

            Assert.Equal(newer, File.ReadAllText(profile.PinsPath));
            Assert.Contains(Plugin.Warnings, w => w.Contains("could not be read"));
        }
    }
}
