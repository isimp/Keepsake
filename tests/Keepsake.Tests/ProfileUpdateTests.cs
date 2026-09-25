using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// What you kept survives Update existing profile in Thunderstore Mod Manager and r2modman,
    /// which deletes the profile folder and puts the updated one in its place under the same name.
    /// Nothing is written outside a profile that does not need it.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class ProfileUpdateTests : IDisposable
    {
        private const string Cfg =
            "[General]\n" +
            "\n" +
            "## How loud.\n" +
            "# Setting type: Single\n" +
            "# Default value: 0.8\n" +
            "Volume = {0}\n";

        private const string Timer = "Seasonality/Solo.Seasonality.bin";

        private static readonly DateTime ModpackTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The mod manager's folder for the game, holding profiles/.</summary>
        private readonly string _game = Path.Combine(Path.GetTempPath(), "keepsake-update-" + Guid.NewGuid().ToString("N"));

        private string ProfileFolder(string name = "Pack") => Path.Combine(_game, "profiles", name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_game, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>A profile laid out as Thunderstore Mod Manager and r2modman lay one out, or without mods.yml as another manager might.</summary>
        private TestProfile Open(string name = "Pack", bool modsYml = true)
        {
            var bepInEx = Path.Combine(ProfileFolder(name), "BepInEx");
            Directory.CreateDirectory(Path.Combine(bepInEx, "config"));
            if (modsYml) File.WriteAllText(Path.Combine(ProfileFolder(name), "mods.yml"), "[]");
            return new TestProfile(bepInEx);
        }

        /// <summary>
        /// A player's profile after a game: the pack's Volume is 0.8 and they keep 0.2, and they keep
        /// Seasonality's timer for their world. Asked, they want a spare copy, unless told otherwise.
        /// </summary>
        private TestProfile Played(string name = "Pack", bool modsYml = true, bool? spare = true)
        {
            var profile = Open(name, modsYml);
            profile.WriteCfg("a.cfg", string.Format(Cfg, "0.8"));
            profile.WritePins("a.cfg\tGeneral\tVolume\t0.2\t0.8");
            WriteTimer(profile, "spring", DateTime.UtcNow.AddHours(-1));
            Assert.Null(FileKeeper.Keep("Seasonality", isFolder: true));

            profile.Launch(logEnd: DateTime.UtcNow.AddHours(-1));
            if (spare != null) Assert.Null(SpareCopy.Answer(spare.Value));
            profile.Close();
            return profile;
        }

        private static void WriteTimer(TestProfile profile, string text, DateTime at)
        {
            var file = profile.CfgPath(Timer);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
            File.SetLastWriteTimeUtc(file, at);
        }

        /// <summary>
        /// Update existing profile: the folder is deleted, and the updated pack takes its name, with
        /// its own Volume and, when given, its own timer, stamped with the zip's time.
        /// </summary>
        private void UpdateExistingProfile(string volume, string timer = null, string name = "Pack")
        {
            Directory.Delete(ProfileFolder(name), true);
            var config = Path.Combine(ProfileFolder(name), "BepInEx", "config");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(ProfileFolder(name), "mods.yml"), "[]");
            File.WriteAllText(Path.Combine(config, "a.cfg"), string.Format(Cfg, volume));

            if (timer == null) return;
            var file = Path.Combine(config, Timer.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, timer);
            File.SetLastWriteTimeUtc(file, ModpackTime);
        }

        private static string Volume(TestProfile profile)
        {
            Assert.True(CfgText.Load(profile.CfgPath("a.cfg")).TryGet("General", "Volume", out var value));
            return value;
        }

        private static string TimerOf(TestProfile profile) => File.ReadAllText(profile.CfgPath(Timer));

        private bool AnythingOutsideTheProfiles() =>
            Directory.GetFileSystemEntries(_game).Any(e => !string.Equals(Path.GetFileName(e), "profiles", StringComparison.OrdinalIgnoreCase));

        [Fact]
        public void WhatYouKeptIsInPlaceAtTheFirstLaunchAfterAnUpdate()
        {
            Played().Dispose();
            UpdateExistingProfile(volume: "0.9", timer: "the pack's");

            using var profile = Open();
            var result = profile.Launch();

            Assert.True(result.FromSpareCopy);
            Assert.Equal("0.2", Volume(profile));
            Assert.Equal("spring", TimerOf(profile));
            Assert.Single(Keeper.Pins);
            Assert.NotNull(FileKeeper.KeptBy(Timer));

            // The pack moved Volume from 0.8 to 0.9 while you kept yours: that waits for an answer.
            var changed = Assert.Single(Keeper.ProfileChanged());
            Assert.Equal("0.9", Keeper.ProfileChangeOf(changed)!.To);

            // And the pack's timer is not lost.
            Assert.Contains(Trash.Of(Timer), v => File.ReadAllText(v.File) == "the pack's");
        }

        [Fact]
        public void AFileThePackDoesNotShipComesBackToo()
        {
            Played().Dispose();
            UpdateExistingProfile(volume: "0.8");

            using var profile = Open();
            profile.Launch();

            Assert.Equal("spring", TimerOf(profile));
        }

        [Fact]
        public void TheNoticeIsForTheLaunchThatBroughtItBackOnly()
        {
            Played().Dispose();
            UpdateExistingProfile(volume: "0.8");

            using var profile = Open();
            profile.Launch();
            Assert.True(SpareCopy.RestoredThisLaunch);

            profile.Close();
            Assert.False(profile.Launch().FromSpareCopy);
            Assert.False(SpareCopy.RestoredThisLaunch);
        }

        [Fact]
        public void WhatYouKeptSurvivesAnUpdateAfterACrash()
        {
            using (var profile = Open())
            {
                profile.WriteCfg("a.cfg", string.Format(Cfg, "0.8"));
                profile.WritePins("a.cfg\tGeneral\tVolume\t0.2\t0.8");
                Assert.Null(SpareCopy.Answer(true));

                // Launched, never closed.
                profile.Launch();
            }

            UpdateExistingProfile(volume: "0.8");

            using var updated = Open();
            updated.Launch();
            Assert.Equal("0.2", Volume(updated));
        }

        [Fact]
        public void WhatYouReleasedStaysReleasedAfterAnUpdate()
        {
            using (var profile = Played())
            {
                Assert.Null(FileKeeper.Release("Seasonality"));
                profile.Close();
            }

            UpdateExistingProfile(volume: "0.8", timer: "the pack's");

            using var updated = Open();
            updated.Launch();
            Assert.Empty(FileKeeper.Kept);
            Assert.Equal("the pack's", TimerOf(updated));
            Assert.Equal("0.2", Volume(updated));
        }

        [Fact]
        public void WhatYouReleasedDoesNotComeBackAfterACrash()
        {
            using var profile = Played();

            Keeper.UnpinAll(Keeper.Pins.Select(p => p.Id).ToList());
            Assert.Null(FileKeeper.Release("Seasonality"));

            // No close: the game crashed. The next launch finds the profile holding nothing kept.
            profile.Launch();

            Assert.False(SpareCopy.RestoredThisLaunch);
            Assert.Empty(Keeper.Pins);
            Assert.Empty(FileKeeper.Kept);
        }

        [Fact]
        public void ARestoreThatFailsPartwayIsFinishedAtTheNextLaunch()
        {
            Played().Dispose();
            UpdateExistingProfile(volume: "0.8");
            var sparePins = Directory.GetFiles(Path.Combine(_game), "keepsake.pins", SearchOption.AllDirectories).Single();
            var spared = File.ReadAllText(sparePins);

            using var profile = Open();
            using (TestProfile.Lock(sparePins))
            {
                profile.Launch();
                profile.Close();
            }

            // The spare copy is still whole, and the next launch brings the rest back.
            Assert.Equal(spared, File.ReadAllText(sparePins));
            profile.Launch();
            Assert.Equal("0.2", Volume(profile));
            Assert.Equal("spring", TimerOf(profile));
            Assert.Single(Keeper.Pins);
        }

        [Fact]
        public void AProfileThatReleasedEverythingIsNotAskedAboutASpareCopy()
        {
            using var profile = Open();
            profile.WriteCfg("a.cfg", string.Format(Cfg, "0.8"));
            profile.WritePins("a.cfg\tGeneral\tVolume\t0.2\t0.8");
            profile.Launch();
            Assert.True(SpareCopy.Asks);

            Keeper.UnpinAll(Keeper.Pins.Select(p => p.Id).ToList());

            // keepsake.pins is still there, holding nothing.
            Assert.True(File.Exists(PinFile.FilePath));
            Assert.False(SpareCopy.Asks);

            File.WriteAllText(profile.CfgPath("state.bin"), "mine");
            Assert.Null(FileKeeper.Keep("state.bin", isFolder: false));
            Assert.True(SpareCopy.Asks);
        }

        [Fact]
        public void AProfileWithoutModsYmlGetsNothingWrittenOutsideIt()
        {
            Played(modsYml: false).Dispose();

            Assert.False(AnythingOutsideTheProfiles());
        }

        [Fact]
        public void AProfileThatKeepsNothingGetsNothingWrittenOutsideIt()
        {
            using var profile = Open();
            profile.WriteCfg("a.cfg", string.Format(Cfg, "0.8"));

            profile.Launch();
            profile.Close();

            Assert.False(AnythingOutsideTheProfiles());
        }

        [Fact]
        public void OneProfilesKeepsakesNeverGoIntoAnother()
        {
            Played("Pack").Dispose();

            using var other = Open("Other");
            other.WriteCfg("a.cfg", string.Format(Cfg, "0.8"));
            other.Launch();

            Assert.False(SpareCopy.RestoredThisLaunch);
            Assert.Empty(Keeper.Pins);
            Assert.Equal("0.8", Volume(other));
        }

        // ---------- what a session changed, then a crash ----------

        [Fact]
        public void AValueChangedInASessionThatCrashedSurvivesAnUpdate()
        {
            using (var profile = Played())
            {
                profile.Launch();
                var config = profile.Mod("a.cfg");
                config.Bind("General", "Volume", 0.8f, "How loud.");
                profile.Index();
                Assert.Null(Keeper.SetValue(profile.Setting("a.cfg", "General", "Volume"), "0.3"));

                // No close: the game crashed.
            }

            UpdateExistingProfile(volume: "0.8");
            using var updated = Open();
            updated.Launch();

            Assert.Equal("0.3", Volume(updated));
        }

        [Fact]
        public void ASettingKeptInASessionThatCrashedSurvivesAnUpdate()
        {
            using (var profile = Played())
            {
                profile.Launch();
                var config = profile.Mod("b.cfg");
                config.Bind("General", "Speed", 5, "How fast.");
                profile.Index();
                Assert.Null(Keeper.Pin(profile.Setting("b.cfg", "General", "Speed")));
            }

            UpdateExistingProfile(volume: "0.8");
            using var updated = Open();
            updated.Launch();

            Assert.NotNull(Keeper.Find(PinFile.IdOf("b.cfg", "General", "Speed")));
        }

        [Fact]
        public void AFileKeptInASessionThatCrashedSurvivesAnUpdate()
        {
            using (var profile = Played())
            {
                profile.Launch();
                var file = profile.CfgPath("Mod/state.json");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "mine");
                Assert.Null(FileKeeper.Keep("Mod/state.json", isFolder: false));
            }

            UpdateExistingProfile(volume: "0.8");
            using var updated = Open();
            updated.Launch();

            Assert.NotNull(FileKeeper.KeptBy("Mod/state.json"));
            Assert.Equal("mine", File.ReadAllText(updated.CfgPath("Mod/state.json")));
        }

        [Fact]
        public void WhatWasReleasedInASessionThatCrashedStaysReleasedAfterAnUpdate()
        {
            using (var profile = Played())
            {
                profile.Launch();
                Assert.Null(FileKeeper.Release("Seasonality"));
                Keeper.UnpinAll(Keeper.Pins.Select(p => p.Id).ToList());
            }

            UpdateExistingProfile(volume: "0.8", timer: "the pack's");
            using var updated = Open();
            updated.Launch();

            Assert.Empty(FileKeeper.Kept);
            Assert.Empty(Keeper.Pins);
            Assert.Equal("0.8", Volume(updated));
            Assert.Equal("the pack's", TimerOf(updated));
        }

        // ---------- asked first ----------

        [Fact]
        public void NothingIsWrittenOutsideTheProfileUntilYouSayYes()
        {
            using var profile = Played(spare: null);
            profile.Launch();
            profile.Close();

            Assert.True(SpareCopy.Asks);
            Assert.False(AnythingOutsideTheProfiles());
        }

        [Fact]
        public void SayingYesMakesTheSpareCopyAtOnceAndKeepsItUpToDate()
        {
            using var profile = Played(spare: null);

            Assert.Null(SpareCopy.Answer(true));
            Assert.False(SpareCopy.Asks);
            Assert.True(AnythingOutsideTheProfiles());

            profile.Launch();
            UpdateExistingProfile(volume: "0.8");
            using var updated = Open();
            updated.Launch();
            Assert.Equal("0.2", Volume(updated));
        }

        [Fact]
        public void SayingYesWhenTheSpareCopyCannotBeMadeSaysSo()
        {
            using var profile = Played(spare: null);

            using (TestProfile.Lock(PinFile.FilePath))
                Assert.NotNull(SpareCopy.Answer(true));

            // Once it can be, the next save makes it whole.
            profile.Close();
            Assert.Equal(File.ReadAllText(PinFile.FilePath), File.ReadAllText(Path.Combine(_game, "Keepsake", "Pack", "keepsake.pins")));
        }

        [Fact]
        public void SayingNoWritesNothingOutsideAndIsNotAskedAgain()
        {
            using var profile = Played(spare: false);
            profile.Launch();
            profile.Close();

            Assert.False(SpareCopy.Asks);
            Assert.False(AnythingOutsideTheProfiles());
        }

        [Fact]
        public void TurningItOffLaterRemovesTheSpareCopy()
        {
            using var profile = Played();
            Assert.True(AnythingOutsideTheProfiles());

            Assert.Null(SpareCopy.Answer(false));
            profile.Launch();
            profile.Close();

            Assert.False(AnythingOutsideTheProfiles());
            Assert.False(SpareCopy.Asks);
        }

        [Fact]
        public void TheQuestionIsOnlyForAManagerProfileThatKeepsSomething()
        {
            using (var empty = Open())
            {
                empty.Launch();
                Assert.False(SpareCopy.Asks);
            }

            using (var gale = Played("Gale", modsYml: false, spare: null))
                Assert.False(SpareCopy.Asks);

            using (var manager = Played("Other", spare: null))
                Assert.True(SpareCopy.Asks);
        }

        [Fact]
        public void YourAnswerComesBackWithTheSpareCopyAfterAnUpdate()
        {
            Played().Dispose();
            UpdateExistingProfile(volume: "0.8");

            using var profile = Open();
            profile.Launch();

            Assert.False(SpareCopy.Asks);
        }

        [Fact]
        public void AfterNoAndAnUpdateYouAreAskedAgainOnceSomethingIsKept()
        {
            Played(spare: false).Dispose();
            UpdateExistingProfile(volume: "0.8");

            using var profile = Open();
            profile.Launch();
            Assert.False(SpareCopy.Asks);

            profile.WritePins("a.cfg\tGeneral\tVolume\t0.2\t0.8");
            Assert.True(SpareCopy.Asks);
        }

        [Fact]
        public void TheAnswerIsInNoFileASyncOrAnExportTakes()
        {
            using var profile = Played();

            var answer = Directory.GetFiles(profile.Root, "keepsake.*").Single(f => File.ReadAllText(f).Contains("yes"));
            Assert.Equal(profile.Root, Path.GetDirectoryName(answer));
            foreach (var ending in new[] { ".cfg", ".txt", ".json", ".yml", ".yaml", ".ini" })
                Assert.False(answer.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void AnUnreadablePinsFileNeitherBringsBackNorOverwritesTheSpareCopy()
        {
            using var profile = Played();
            var sparePins = Directory.GetFiles(Path.Combine(_game), "keepsake.pins", SearchOption.AllDirectories)
                .Single(f => !f.StartsWith(ProfileFolder()));
            var spared = File.ReadAllText(sparePins);
            var mine = File.ReadAllText(PinFile.FilePath);

            using (TestProfile.Lock(PinFile.FilePath))
            {
                Assert.False(profile.Launch().FromSpareCopy);
                profile.Close();
            }

            Assert.Equal(mine, File.ReadAllText(PinFile.FilePath));
            Assert.Equal(spared, File.ReadAllText(sparePins));
        }
    }
}
