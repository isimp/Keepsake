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
        public void AChangeMadeElsewhereIsFollowedAndWrittenOnceItStops()
        {
            using var profile = new TestProfile();
            var speed = Speed(profile);
            profile.Index();
            var setting = profile.Setting("a.cfg", "General", "Speed");
            Keeper.Pin(setting);

            speed.Value = 12;
            Keeper.Tick(10f);

            Assert.Equal("12", Keeper.Find(setting.Id).Value);
            Assert.Equal(new[] { Line("Speed", "5", "5") }, profile.PinLines());

            Keeper.Tick(10f + Keeper.FollowDelay / 2f);
            Assert.Equal(new[] { Line("Speed", "5", "5") }, profile.PinLines());

            Keeper.Tick(10f + Keeper.FollowDelay);
            Assert.Equal(new[] { Line("Speed", "12", "5") }, profile.PinLines());
        }

        [Fact]
        public void ASliderDraggedForSecondsIsWrittenOnlyWhenLetGo()
        {
            using var profile = new TestProfile();
            var speed = Speed(profile);
            profile.Index();
            Keeper.Pin(profile.Setting("a.cfg", "General", "Speed"));
            var before = PinFile.Stamp();

            // A config manager sets the value in every frame it moves.
            var time = 0f;
            for (var value = 6; value <= 60; value++)
            {
                speed.Value = value;
                Keeper.Tick(time += 0.05f);
            }

            Assert.Equal(before, PinFile.Stamp());

            Keeper.Tick(time + Keeper.FollowDelay);
            Assert.Equal(new[] { Line("Speed", "60", "5") }, profile.PinLines());
        }

        [Fact]
        public void AFollowedValueGoesAlongWithAnyOtherWrite()
        {
            using var profile = new TestProfile();
            var config = profile.Mod("a.cfg");
            var speed = config.Bind("General", "Speed", 5, "How fast.");
            config.Bind("General", "Size", 1.5f, "How big.");
            profile.Index();
            Keeper.Pin(profile.Setting("a.cfg", "General", "Speed"));

            speed.Value = 12;
            Keeper.Pin(profile.Setting("a.cfg", "General", "Size"));

            Assert.Equal(new[] { Line("Size", "1.5", "1.5"), Line("Speed", "12", "5") }, profile.PinLines());
        }

        [Fact]
        public void AChangeFromAnotherThreadWaitsForTheMainThread()
        {
            using var profile = new TestProfile();
            var speed = Speed(profile);
            profile.Index();
            var setting = profile.Setting("a.cfg", "General", "Speed");
            Keeper.Pin(setting);
            string told = null;
            Keeper.Changed = id => told = id;

            // As a mod's own file watcher would, reloading its cfg file on a thread of its own.
            Keeper.MainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var worker = new System.Threading.Thread(() => speed.Value = 12);
            worker.Start();
            worker.Join();

            Assert.Null(told);
            Assert.Equal("5", Keeper.Find(setting.Id).Value);
            Assert.False(Session.IsChanged(setting));

            Keeper.Tick(0f);

            Assert.Equal(setting.Id, told);
            Assert.Equal("12", Keeper.Find(setting.Id).Value);
            Assert.True(Session.IsChanged(setting));
        }

        [Fact]
        public void AFlushWritesAFollowedValueWithoutLosingAHandEdit()
        {
            using var profile = new TestProfile();
            var speed = Speed(profile);
            profile.Index();
            Keeper.Pin(profile.Setting("a.cfg", "General", "Speed"));

            speed.Value = 12;
            File.AppendAllText(profile.PinsPath, "b.cfg\tOther\tColour\tred\tblue\n");
            Keeper.Flush();

            Assert.Equal(new[] { Line("Speed", "12", "5"), "b.cfg\tOther\tColour\tred\tblue" }, profile.PinLines());
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
            var state = new ChangeState();
            state.Waiting.Add(new ProfileChange { File = "a.cfg", Section = "General", Key = "Speed", From = "5", To = "7" });
            ProfileChanges.Write(state);

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

                // Answered for good: the next launch does not bring it back.
                Assert.Empty(ProfileChanges.Read().Waiting);
            }
        }

        [Fact]
        public void StoppingAskingAnswersTheChangeAndLastsAcrossLaunches()
        {
            var (profile, speed, setting) = ProfileMovedSpeed();
            using (profile)
            {
                Keeper.SetQuiet(setting.Id, true);

                Assert.Equal(8, speed.Value);
                Assert.Empty(Keeper.ProfileChanged());
                Assert.True(Keeper.IsQuiet(setting.Id));
                Assert.True(ProfileChanges.Read().IsQuiet(setting.Id));

                Keeper.SetQuiet(setting.Id, false);
                Assert.False(Keeper.IsQuiet(setting.Id));
                Assert.False(File.Exists(ProfileChanges.FilePath));
            }
        }

        [Fact]
        public void AQuietSettingStaysQuietWhenReconcileFindsAChange()
        {
            using var profile = new TestProfile();
            profile.WritePins(Line("Speed", "8", "5"));
            profile.WriteCfg("a.cfg", "[General]\nSpeed = 7\n");
            var state = new ChangeState();
            state.Quiet.Add(new QuietSetting { File = "a.cfg", Section = "General", Key = "Speed" });
            ProfileChanges.Write(state);
            Speed(profile);

            Keeper.Reconcile();

            Assert.Empty(Keeper.ProfileChanged());
            Assert.Equal(new[] { Line("Speed", "8", "7") }, profile.PinLines());
        }

        [Fact]
        public void ReleasingForgetsThatASettingWasQuiet()
        {
            var (profile, _, setting) = ProfileMovedSpeed();
            using (profile)
            {
                Keeper.SetQuiet(setting.Id, true);
                Keeper.Unpin(setting.Id);

                Assert.False(File.Exists(ProfileChanges.FilePath));
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

        // ---------- releasing many ----------

        [Fact]
        public void ReleasingSettingsNoModBoundTakesThemOutInOneGo()
        {
            using var profile = new TestProfile();
            profile.WritePins(Line("Speed", "8", "5"), "gone.cfg\tGeneral\tOld\t1\t2", "gone.cfg\tGeneral\tOlder\t3\t4");
            Speed(profile);
            Keeper.Reconcile();

            var released = Keeper.UnpinAll(new[] { PinFile.IdOf("gone.cfg", "General", "Old"), PinFile.IdOf("gone.cfg", "General", "Older") });

            Assert.Equal(2, released);
            Assert.Equal(new[] { Line("Speed", "8", "5") }, profile.PinLines());
        }

        // ---------- reading the settings ----------

        [Fact]
        public void ARefreshThatFindsNothingNewKeepsTheOrderWithoutSorting()
        {
            using var profile = new TestProfile();
            var config = profile.Mod("a.cfg");
            config.Bind("General", "Speed", 5, "How fast.");
            profile.Index();
            Assert.True(SettingIndex.LastRefreshSorted);

            profile.Index();
            Assert.False(SettingIndex.LastRefreshSorted);
            Assert.Single(SettingIndex.All);

            config.Bind("General", "Accel", 2, "How quick.");
            profile.Index();
            Assert.True(SettingIndex.LastRefreshSorted);
            Assert.Equal(new[] { "Accel", "Speed" }, SettingIndex.All.Select(s => s.Key));
        }
    }
}
