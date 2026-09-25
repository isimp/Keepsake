using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// A file of Keepsake's that cannot be read, held open by a scanner or an editor, is never
    /// written over: nothing it holds is lost, actions that need it say they failed, and once it
    /// can be read again Keepsake carries on from what it holds. The same goes for a value that
    /// cannot be saved to keepsake.pins.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class UnreadableFilesTests
    {
        private const string Cfg =
            "[General]\n" +
            "\n" +
            "## How loud.\n" +
            "# Setting type: Single\n" +
            "# Default value: 0.8\n" +
            "Volume = {0}\n";

        private const string State = "Mod/state.json";

        private static readonly DateTime Earlier = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private static string Write(TestProfile profile, string path, string text, DateTime at)
        {
            var full = profile.CfgPath(path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
            File.SetLastWriteTimeUtc(full, at);
            return full;
        }

        /// <summary>A mod with two settings, Volume kept at 0.2 over the profile's 0.8.</summary>
        private static TestProfile WithAKeptVolume()
        {
            var profile = new TestProfile();
            profile.WriteCfg("a.cfg", string.Format(Cfg, "0.8"));
            profile.WritePins("a.cfg\tGeneral\tVolume\t0.2\t0.8");
            return profile;
        }

        private static Setting BindSpeed(TestProfile profile)
        {
            var config = profile.Mod("b.cfg");
            config.Bind("General", "Speed", 5, "How fast.");
            profile.Index();
            return profile.Setting("b.cfg", "General", "Speed");
        }

        [Fact]
        public void APinsFileThatCannotBeReadIsLeftAsItIsAndSoAreTheCfgFiles()
        {
            using var profile = WithAKeptVolume();
            var pins = File.ReadAllBytes(PinFile.FilePath);
            var cfg = File.ReadAllText(profile.CfgPath("a.cfg"));

            using (TestProfile.Lock(PinFile.FilePath))
            {
                profile.Launch();
                profile.Close();
            }

            Assert.Equal(pins, File.ReadAllBytes(PinFile.FilePath));
            Assert.Equal(cfg, File.ReadAllText(profile.CfgPath("a.cfg")));
        }

        [Fact]
        public void KeptValuesThatCouldNotBeReadAtTheStartAreAskedForAgainAndPutInOnceTheyCanBe()
        {
            using var profile = WithAKeptVolume();
            var config = profile.Mod("a.cfg");
            var volume = config.Bind("General", "Volume", 0.8f, "How loud.");
            profile.Index();

            // Held open through the preloader and the plugin's first look at the start menu.
            using (TestProfile.Lock(PinFile.FilePath))
            {
                profile.Launch();
                profile.Index();
                Assert.True(Keeper.Reconcile() > 0);
            }

            Assert.Equal(0.8f, volume.Value);

            Keeper.Reconcile();
            Assert.Equal(0.2f, volume.Value);
        }

        [Fact]
        public void KeepingWhileThePinsFileCannotBeReadSaysSo()
        {
            using var profile = WithAKeptVolume();
            var speed = BindSpeed(profile);

            using (TestProfile.Lock(PinFile.FilePath))
                Assert.NotNull(Keeper.Pin(speed));
        }

        [Fact]
        public void ReleasingWhileThePinsFileCannotBeReadChangesNothing()
        {
            using var profile = WithAKeptVolume();
            profile.Launch();
            var pins = File.ReadAllBytes(PinFile.FilePath);
            var cfg = File.ReadAllText(profile.CfgPath("a.cfg"));

            using (TestProfile.Lock(PinFile.FilePath))
            {
                Keeper.Sync();
                Assert.Equal(0, Keeper.UnpinAll(new[] { PinFile.IdOf("a.cfg", "General", "Volume") }));
            }

            Assert.Equal(pins, File.ReadAllBytes(PinFile.FilePath));
            Assert.Equal(cfg, File.ReadAllText(profile.CfgPath("a.cfg")));
        }

        /// <param name="editedElsewhere">
        /// Whether the file changed since Keepsake read it, so it has to be read again and cannot
        /// be; otherwise what Keepsake holds is up to date and only the writing fails.
        /// </param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ChangesThatCannotBeSavedToThePinsFileSaySo(bool editedElsewhere)
        {
            using var profile = WithAKeptVolume();
            var speed = BindSpeed(profile);
            Assert.Null(Keeper.Pin(speed));
            var pins = File.ReadAllBytes(PinFile.FilePath);
            var other = profile.Mod("c.cfg");
            other.Bind("General", "Size", 3, "How big.");
            profile.Index();

            if (editedElsewhere) File.SetLastWriteTimeUtc(PinFile.FilePath, DateTime.UtcNow.AddMinutes(1));
            using (TestProfile.Lock(PinFile.FilePath))
            {
                Assert.NotNull(Keeper.Pin(profile.Setting("c.cfg", "General", "Size")));
                Assert.NotNull(Keeper.SetValue(speed, "7"));
                Assert.NotNull(Keeper.Unpin(speed.Id));
            }

            Assert.Equal(pins, File.ReadAllBytes(PinFile.FilePath));
            Assert.Equal("5", speed.Current);
            Assert.Equal("5", Keeper.Find(speed.Id)!.Value);
            Assert.Null(Keeper.Find(PinFile.IdOf("c.cfg", "General", "Size")));
        }

        [Fact]
        public void AValueChangedInAConfigManagerWhileThePinsFileCannotBeReadIsSavedOnceItCan()
        {
            using var profile = WithAKeptVolume();
            var config = profile.Mod("b.cfg");
            var speed = config.Bind("General", "Speed", 5, "How fast.");
            profile.Index();
            Assert.Null(Keeper.Pin(profile.Setting("b.cfg", "General", "Speed")));

            File.SetLastWriteTimeUtc(PinFile.FilePath, DateTime.UtcNow.AddMinutes(1));
            using (TestProfile.Lock(PinFile.FilePath))
            {
                Keeper.Sync();
                speed.Value = 8;
                Keeper.Flush();
            }

            Keeper.Flush();

            Assert.Equal("8", PinFile.Read()!.Single(p => p.Key == "Speed").Value);
            Assert.Contains(PinFile.Read()!, p => p.Key == "Volume");
        }

        [Fact]
        public void AValueChangedInAConfigManagerIsSavedAtCloseEvenIfThePinsFileWasHeldUntilThen()
        {
            using var profile = WithAKeptVolume();
            var config = profile.Mod("b.cfg");
            var speed = config.Bind("General", "Speed", 5, "How fast.");
            profile.Index();
            Assert.Null(Keeper.Pin(profile.Setting("b.cfg", "General", "Speed")));

            using (TestProfile.Lock(PinFile.FilePath))
            {
                speed.Value = 8;
                Keeper.Flush();
            }

            profile.Close();

            Assert.Equal("8", PinFile.Read()!.Single(p => p.Key == "Speed").Value);
        }

        /// <summary>
        /// A setting of a mod's own type, written as its text is. BepInEx escapes tabs and line
        /// breaks in a string setting, but a type with a converter of its own can write them.
        /// </summary>
        private sealed class Word
        {
            public string Text;
        }

        /// <summary>A mod that turns one value into one with a tab in it, which keepsake.pins cannot hold.</summary>
        private sealed class TurnsXIntoATab : BepInEx.Configuration.AcceptableValueBase
        {
            public TurnsXIntoATab() : base(typeof(Word)) { }
            public override object Clamp(object value) => ((Word)value).Text == "x" ? new Word { Text = "x\tadjusted" } : value;
            public override bool IsValid(object value) => ((Word)value).Text != "x";
            public override string ToDescriptionString() => "# The mod adjusts x.";
        }

        static UnreadableFilesTests() =>
            BepInEx.Configuration.TomlTypeConverter.AddConverter(typeof(Word), new BepInEx.Configuration.TypeConverter
            {
                ConvertToString = (value, type) => ((Word)value).Text,
                ConvertToObject = (text, type) => new Word { Text = text },
            });

        [Fact]
        public void AValueTheModTurnsIntoOneThatCannotBeKeptIsRefusedAndNothingChanges()
        {
            using var profile = WithAKeptVolume();
            var config = profile.Mod("b.cfg");
            var name = config.Bind("General", "Name", new Word { Text = "plain" }, new BepInEx.Configuration.ConfigDescription("A name.", new TurnsXIntoATab()));
            profile.Index();
            var setting = profile.Setting("b.cfg", "General", "Name");
            Assert.Null(Keeper.Pin(setting));

            Assert.NotNull(Keeper.SetValue(setting, "x"));

            Assert.Equal("plain", name.Value.Text);
            Assert.Equal("plain", Keeper.Find(setting.Id)!.Value);
            Assert.Contains(PinFile.Read()!, p => p.Key == "Name" && p.Value == "plain");
        }

        [Fact]
        public void OnceThePinsFileCanBeReadAgainKeepingAddsToWhatItHolds()
        {
            using var profile = WithAKeptVolume();
            using (TestProfile.Lock(PinFile.FilePath))
                profile.Launch();

            Assert.Null(Keeper.Pin(BindSpeed(profile)));

            var ids = PinFile.Read()!.Select(p => p.Id).ToList();
            Assert.Contains(PinFile.IdOf("a.cfg", "General", "Volume"), ids);
            Assert.Contains(PinFile.IdOf("b.cfg", "General", "Speed"), ids);
        }

        [Fact]
        public void AFilesListThatCannotBeReadLeavesKeptFilesAsTheyAre()
        {
            using var profile = new TestProfile();
            var file = Write(profile, State, "mine", Earlier);
            FileKeeper.Keep(State, isFolder: false);
            profile.Close(Earlier.AddHours(1));
            Write(profile, State, "the owner's", Earlier.AddDays(1));
            var list = File.ReadAllBytes(KeptFiles.ListPath);

            using (TestProfile.Lock(KeptFiles.ListPath))
            {
                profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(1));
                Assert.NotNull(FileKeeper.Keep("Mod/other.json", isFolder: false));
                profile.Close(Earlier.AddDays(2).AddHours(1));
            }

            Assert.Equal("the owner's", File.ReadAllText(file));
            Assert.Equal(list, File.ReadAllBytes(KeptFiles.ListPath));

            // Readable again: the next launch carries on, and your copy goes back.
            profile.Launch(Earlier.AddDays(3), logEnd: Earlier.AddHours(1));
            Assert.Equal("mine", File.ReadAllText(file));
        }

        [Fact]
        public void ASessionFileThatCannotBeReadIsLeftAsItIsAndNothingIsPutBackOverAChange()
        {
            using var profile = new TestProfile();
            var file = Write(profile, State, "mine", Earlier);
            FileKeeper.Keep(State, isFolder: false);
            profile.Close(Earlier.AddHours(1));
            Write(profile, State, "the owner's", Earlier.AddDays(1));
            var session = File.ReadAllBytes(SessionFile.FilePath);

            // Without the session file the launch cannot know the last game closed with Keepsake.
            using (TestProfile.Lock(SessionFile.FilePath))
            {
                profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(1));
                profile.Close(Earlier.AddDays(2).AddHours(1));
            }

            Assert.Equal(session, File.ReadAllBytes(SessionFile.FilePath));
            Assert.Equal("the owner's", File.ReadAllText(file));
            Assert.Equal("mine", File.ReadAllText(KeptFiles.Copy(State)));
        }

        [Fact]
        public void AChangesFileThatCannotBeReadKeepsTheChangesWaiting()
        {
            using var profile = WithAKeptVolume();

            // The profile moves Volume while you keep yours: a change waits for an answer.
            profile.WriteCfg("a.cfg", string.Format(Cfg, "0.9"));
            profile.Launch();
            Assert.Single(Keeper.ProfileChanged());
            var changes = File.ReadAllBytes(ProfileChanges.FilePath);

            // And again, while the changes file cannot be read.
            profile.WriteCfg("a.cfg", string.Format(Cfg, "0.7"));
            using (TestProfile.Lock(ProfileChanges.FilePath))
            {
                profile.Launch();
                profile.Close();
            }

            Assert.Equal(changes, File.ReadAllBytes(ProfileChanges.FilePath));
            profile.Launch();
            Assert.Single(Keeper.ProfileChanged());
        }
    }
}
