using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// Nothing Keepsake replaces or releases is lost: each version is among the file's earlier
    /// versions, the newest five of them, and any of those can be put back. When nothing can be
    /// set aside, nothing is replaced.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class TrashTests
    {
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

        private static string[] Earliers(string path) => Trash.Of(path).Select(v => File.ReadAllText(v.File)).ToArray();

        private static string CopyOf(string path) => File.ReadAllText(KeptFiles.Copy(path));

        /// <summary>Kept, one game played and closed, then a sync replaced the file with the owner's.</summary>
        private static string KeptThenSynced(TestProfile profile)
        {
            var file = Write(profile, State, "mine", Earlier);
            Assert.Null(FileKeeper.Keep(State, isFolder: false));
            profile.Launch(Earlier.AddMinutes(1));
            profile.Close(Earlier.AddHours(1));
            Write(profile, State, "the owner's", Earlier.AddDays(1));
            return file;
        }

        /// <summary>Something in the way of the trash folder, so nothing can be set aside.</summary>
        private static void BlockTheTrash(TestProfile profile)
        {
            if (Directory.Exists(Trash.Root)) Directory.Delete(Trash.Root, true);
            File.WriteAllText(Trash.Root, "in the way");
        }

        [Fact]
        public void TheFileYourCopyReplacesAtLaunchIsAnEarlierVersion()
        {
            using var profile = new TestProfile();
            var file = KeptThenSynced(profile);

            profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(1));

            Assert.Equal("mine", File.ReadAllText(file));
            Assert.Contains("the owner's", Earliers(State));
        }

        [Fact]
        public void WhatYouReleaseIsAnEarlierVersion()
        {
            using var profile = new TestProfile();
            Write(profile, State, "mine", Earlier);
            FileKeeper.Keep(State, isFolder: false);

            Assert.Null(FileKeeper.Release(State));

            Assert.Equal(new[] { "mine" }, Earliers(State));
            Assert.False(File.Exists(KeptFiles.Copy(State)));
        }

        [Fact]
        public void WhatAReleasedFolderHeldIsAnEarlierVersionOfEachFile()
        {
            using var profile = new TestProfile();
            Write(profile, "Timers/a.bin", "a", Earlier);
            Write(profile, "Timers/deep/b.bin", "b", Earlier);
            FileKeeper.Keep("Timers", isFolder: true);

            Assert.Null(FileKeeper.Release("Timers"));

            Assert.Equal(new[] { "a" }, Earliers("Timers/a.bin"));
            Assert.Equal(new[] { "b" }, Earliers("Timers/deep/b.bin"));
        }

        [Fact]
        public void KeepingAWaitingFileAsItIsKeepsYourCopyAsAnEarlierVersion()
        {
            using var profile = new TestProfile();
            KeptThenSynced(profile);

            // A crash, the log ending before the sync: the launch cannot tell who wrote the file, so it waits.
            profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(3));
            Assert.NotNull(FileKeeper.WaitingFor(State));

            Assert.Null(FileKeeper.KeepCurrent(State));

            Assert.Equal("the owner's", CopyOf(State));
            Assert.Contains("mine", Earliers(State));
        }

        [Fact]
        public void TheCopyEachGameEndReplacesIsAnEarlierVersionTheNewestFiveKept()
        {
            using var profile = new TestProfile();
            Write(profile, State, "v0", Earlier);
            FileKeeper.Keep(State, isFolder: false);

            for (var i = 1; i <= 7; i++)
            {
                profile.Launch(Earlier.AddHours(i));
                Write(profile, State, "v" + i, Earlier.AddHours(i).AddMinutes(10));
                profile.Close(Earlier.AddHours(i).AddMinutes(20));
            }

            Assert.Equal("v7", CopyOf(State));
            Assert.Equal(new[] { "v6", "v5", "v4", "v3", "v2" }, Earliers(State));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(4)]
        public void AnyEarlierVersionPutBackIsTheFileFromTheNextLaunch(int which)
        {
            using var profile = new TestProfile();
            var file = Write(profile, State, "v0", Earlier);
            FileKeeper.Keep(State, isFolder: false);
            for (var i = 1; i <= 6; i++)
            {
                profile.Launch(Earlier.AddHours(i));
                Write(profile, State, "v" + i, Earlier.AddHours(i).AddMinutes(10));
                profile.Close(Earlier.AddHours(i).AddMinutes(20));
            }

            profile.Launch(Earlier.AddHours(7));
            var version = Trash.Of(State)[which];
            var chosen = File.ReadAllText(version.File);

            Assert.Null(FileKeeper.PutBackVersion(State, version));

            // The mod writes on until the game closes; what you chose still goes in next.
            Write(profile, State, "played on", Earlier.AddHours(7).AddMinutes(10));
            profile.Close(Earlier.AddHours(7).AddMinutes(20));
            profile.Launch(Earlier.AddHours(8), logEnd: Earlier.AddHours(7).AddMinutes(20));

            Assert.Equal(chosen, File.ReadAllText(file));
            Assert.Contains("played on", Earliers(State));
        }

        [Fact]
        public void PuttingBackAVersionOfAReleasedFileKeepsItAgain()
        {
            using var profile = new TestProfile();
            var file = Write(profile, State, "mine", Earlier);
            FileKeeper.Keep(State, isFolder: false);
            FileKeeper.Release(State);
            Write(profile, State, "the owner's", Earlier.AddDays(1));

            Assert.Null(FileKeeper.PutBackVersion(State, Trash.Of(State).Single()));
            Assert.NotNull(FileKeeper.KeptBy(State));

            profile.Close(Earlier.AddDays(1).AddHours(1));
            profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddDays(1).AddHours(1));
            Assert.Equal("mine", File.ReadAllText(file));
        }

        [Fact]
        public void PuttingBackAVersionListsTheFileItReplacesOnce()
        {
            using var profile = new TestProfile();
            KeptThenSynced(profile);
            profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(1));

            // The owner's version after all, then a game, then the launch that puts it in.
            Assert.Null(FileKeeper.PutBackVersion(State, Trash.Of(State).Single(v => File.ReadAllText(v.File) == "the owner's")));
            profile.Close(Earlier.AddDays(2).AddHours(1));
            profile.Launch(Earlier.AddDays(3), logEnd: Earlier.AddDays(2).AddHours(1));

            var earliers = Earliers(State);
            Assert.Equal(earliers.Distinct().Count(), earliers.Length);
            Assert.Contains("mine", earliers);
        }

        [Fact]
        public void PuttingBackAVersionOfAReleasedFileListsWhatWasReleasedOnce()
        {
            using var profile = new TestProfile();
            Write(profile, State, "v1", Earlier);
            FileKeeper.Keep(State, isFolder: false);
            profile.Launch(Earlier.AddMinutes(1));
            Write(profile, State, "v2", Earlier.AddMinutes(10));
            profile.Close(Earlier.AddMinutes(20));
            Assert.Null(FileKeeper.Release(State));

            Assert.Null(FileKeeper.PutBackVersion(State, Trash.Of(State).Single(v => File.ReadAllText(v.File) == "v1")));
            profile.Close(Earlier.AddHours(1));
            profile.Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(1));

            Assert.Equal("v1", File.ReadAllText(profile.CfgPath(State)));
            var earliers = Earliers(State);
            Assert.Equal(new[] { "v2", "v1" }, earliers);
        }

        [Fact]
        public void AFilesEarlierVersionsAreItsOwn()
        {
            using var profile = new TestProfile();
            Write(profile, State, "state", Earlier);
            Write(profile, State + ".bak", "backup", Earlier);
            Write(profile, "Mod/state", "no ending", Earlier);
            FileKeeper.Keep("Mod", isFolder: true);

            FileKeeper.Release("Mod");

            Assert.Equal(new[] { "state" }, Earliers(State));
            Assert.Equal(new[] { "backup" }, Earliers(State + ".bak"));
            Assert.Equal(new[] { "no ending" }, Earliers("Mod/state"));
        }

        [Fact]
        public void EarlierVersionsTakeNoSyncsFileTypes()
        {
            using var profile = new TestProfile();
            Write(profile, "Mod/settings.cfg", "mine", Earlier);
            FileKeeper.Keep("Mod/settings.cfg", isFolder: false);
            FileKeeper.Release("Mod/settings.cfg");

            // A sync takes cfg, txt, json, yml, yaml and ini files from anywhere in the profile.
            foreach (var file in Directory.GetFiles(Trash.Root, "*", SearchOption.AllDirectories))
            foreach (var ending in new[] { ".cfg", ".txt", ".json", ".yml", ".yaml", ".ini" })
                Assert.False(file.EndsWith(ending, StringComparison.OrdinalIgnoreCase), file);
        }

        [Fact]
        public void YourSettingsAsTheGameStartedAreThereAfterAReleaseAll()
        {
            using var profile = new TestProfile();
            profile.WritePins("a.cfg\tGeneral\tVolume\t0.2\t0.8", "a.cfg\tGeneral\tSpeed\t9\t5");
            var before = File.ReadAllText(PinFile.FilePath);
            profile.Launch();

            Keeper.UnpinAll(Keeper.Pins.Select(p => p.Id).ToList());
            Assert.Empty(PinFile.Read()!);

            var lists = Path.Combine(Trash.Root, "lists");
            Assert.Contains(Directory.GetFiles(lists, "keepsake.pins*"), f => File.ReadAllText(f) == before);
        }

        [Fact]
        public void TheLastFiveDifferentListsAreKept()
        {
            using var profile = new TestProfile();
            for (var i = 1; i <= 7; i++)
            {
                profile.WritePins($"a.cfg\tGeneral\tSpeed\t{i}\t5");
                profile.Launch();
                profile.Launch();
            }

            var kept = Directory.GetFiles(Path.Combine(Trash.Root, "lists"), "keepsake.pins*").Select(File.ReadAllText).ToList();
            Assert.Equal(5, kept.Count);
            for (var i = 3; i <= 7; i++) Assert.Contains(kept, k => k.Contains($"Speed\t{i}\t"));
        }

        // ---------- nothing set aside, nothing replaced ----------

        [Fact]
        public void ALaunchThatCannotSetTheFileAsideLeavesItAsItIs()
        {
            using var profile = new TestProfile();
            var file = KeptThenSynced(profile);
            BlockTheTrash(profile);

            profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(1));

            Assert.Equal("the owner's", File.ReadAllText(file));
            Assert.Equal("mine", CopyOf(State));
        }

        [Fact]
        public void ACloseThatCannotSetTheCopyAsideKeepsIt()
        {
            using var profile = new TestProfile();
            Write(profile, State, "v0", Earlier);
            FileKeeper.Keep(State, isFolder: false);
            BlockTheTrash(profile);

            Write(profile, State, "v1", Earlier.AddMinutes(1));
            profile.Close(Earlier.AddMinutes(2));

            Assert.Equal("v0", CopyOf(State));
        }

        [Fact]
        public void AReleaseThatCannotSetTheCopyAsideSaysSoAndKeepsTheFile()
        {
            using var profile = new TestProfile();
            Write(profile, State, "mine", Earlier);
            FileKeeper.Keep(State, isFolder: false);
            BlockTheTrash(profile);

            Assert.NotNull(FileKeeper.Release(State));

            Assert.NotNull(FileKeeper.KeptBy(State));
            Assert.Equal("mine", CopyOf(State));
        }

        [Fact]
        public void KeepingAWaitingFileThatCannotSetTheCopyAsideSaysSo()
        {
            using var profile = new TestProfile();
            KeptThenSynced(profile);
            profile.Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(3));
            Assert.NotNull(FileKeeper.WaitingFor(State));
            BlockTheTrash(profile);

            Assert.NotNull(FileKeeper.KeepCurrent(State));

            Assert.Equal("mine", CopyOf(State));
            Assert.NotNull(FileKeeper.WaitingFor(State));
        }
    }
}
