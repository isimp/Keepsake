using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// Kept files: a copy saved as the game closes, and at launch a file that changed since told
    /// by when it was written. While the game ran, and it is yours. After a game Keepsake saw
    /// close, and a profile sync replaced it, so your copy goes back. After any other game, and
    /// it waits for you to choose.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class KeptFilesTests
    {
        private const string Timers = "Seasonality/LastSeasonChangeData";

        private const string Solo = Timers + "/Solo.Seasonality.bin";

        private static readonly DateTime Earlier = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>A file a mod wrote, with a time of its own so the checks never depend on the clock.</summary>
        private static string Write(TestProfile profile, string path, string text, DateTime? at = null)
        {
            var full = profile.CfgPath(path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
            File.SetLastWriteTimeUtc(full, at ?? Earlier);
            return full;
        }

        /// <summary>
        /// A launch: the preloader settles the kept files, given when the last game's log ended,
        /// and the plugin starts afresh.
        /// </summary>
        private static SettleResult Launch(DateTime at, DateTime? logEnd = null)
        {
            var result = Restorer.SettleFiles(KeptFiles.Read(), at, logEnd);
            FileKeeper.Reset();
            return result;
        }

        /// <summary>The plugin seeing the game close.</summary>
        private static void Close(DateTime at) => FileKeeper.Close("test", at);

        private static string CopyOf(string path) => File.ReadAllText(KeptFiles.Copy(path));

        /// <summary>A file left waiting: kept, then a crash, then a change after the log ended.</summary>
        private static string Waiting(TestProfile profile)
        {
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);
            Launch(Earlier.AddHours(1));

            Write(profile, Solo, "the owner's", Earlier.AddDays(1));
            var result = Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(3));
            Assert.Equal(1, result.Waiting);
            return file;
        }

        [Fact]
        public void AKeptFolderIsCopiedOutsideConfigUnderANameNoSyncTakes()
        {
            using var profile = new TestProfile();
            Write(profile, Solo, "one");

            Assert.Null(FileKeeper.Keep(Timers, isFolder: true));

            var copy = Path.Combine(profile.Root, "keepsake-files", "Seasonality", "LastSeasonChangeData", "Solo.Seasonality.bin.kept");
            Assert.Equal("one", File.ReadAllText(copy));
            Assert.Contains(Timers + "/", File.ReadAllLines(KeptFiles.ListPath));

            // A sync takes cfg, txt, json, yml, yaml and ini files from anywhere in the profile.
            foreach (var ending in new[] { ".cfg", ".txt", ".json", ".yml", ".yaml", ".ini" })
            {
                Assert.False(copy.EndsWith(ending));
                Assert.False(KeptFiles.ListPath.EndsWith(ending));
                Assert.False(SessionFile.FilePath.EndsWith(ending));
            }
        }

        [Fact]
        public void AFileASyncRemovedIsPutBackAtLaunch()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "one");
            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(1));

            File.Delete(file);

            Assert.Equal(1, Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(1)).PutBack);
            Assert.Equal("one", File.ReadAllText(file));
            Assert.Equal(Earlier, File.GetLastWriteTimeUtc(file));
        }

        [Fact]
        public void AFileASyncReplacedAfterTheGameClosedIsPutBack()
        {
            using var profile = new TestProfile();
            var file = Write(profile, "Mod/state.json", "mine");
            FileKeeper.Keep("Mod/state.json", isFolder: false);
            Close(Earlier.AddHours(1));

            Write(profile, "Mod/state.json", "the owner's", Earlier.AddDays(1));

            var result = Launch(Earlier.AddDays(1).AddMinutes(1), logEnd: Earlier.AddHours(1).AddSeconds(2));
            Assert.True(result.Clean);
            Assert.Equal(1, result.PutBack);
            Assert.Equal("mine", File.ReadAllText(file));
        }

        [Fact]
        public void AFileNobodyTouchedIsLeftAsItIs()
        {
            using var profile = new TestProfile();
            Write(profile, Solo, "one");
            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(1));

            var result = Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(1));
            Assert.Equal(0, result.PutBack + result.Updated + result.Waiting);
        }

        [Fact]
        public void WhatTheModWritesWhileTheGameRunsIsWhatComesBack()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);

            // The season moved on, and a new world was made.
            Write(profile, Solo, "summer", Earlier.AddHours(1));
            var added = Write(profile, Timers + "/Other.Seasonality.bin", "winter", Earlier.AddHours(1));
            Close(Earlier.AddHours(2));

            File.Delete(file);
            File.Delete(added);
            Assert.Equal(2, Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(2)).PutBack);
            Assert.Equal("summer", File.ReadAllText(file));
            Assert.Equal("winter", File.ReadAllText(added));
        }

        [Fact]
        public void ASyncAfterTheModWroteIsUndoneWithTheModsVersion()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);

            Write(profile, Solo, "summer", Earlier.AddHours(1));
            Close(Earlier.AddHours(2));
            Write(profile, Solo, "the owner's", Earlier.AddDays(1));

            Assert.Equal(1, Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(2)).PutBack);
            Assert.Equal("summer", File.ReadAllText(file));
        }

        [Fact]
        public void AModWritingAfterKeepsakeClosedStillCountsAsTheGames()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(1));

            // Another mod saves its file on the way out, after Keepsake's last hook.
            Write(profile, Solo, "late", Earlier.AddHours(1).AddSeconds(5));

            var result = Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(1).AddSeconds(10));
            Assert.True(result.Clean);
            Assert.Equal(1, result.Updated);
            Assert.Equal("late", File.ReadAllText(file));
            Assert.Equal("late", CopyOf(Solo));
        }

        [Theory]
        [InlineData("2026-08-01T09:00:00Z")] // the pack's author last changed the file weeks ago
        [InlineData("2024-01-01T00:00:00Z")] // the fixed time Thunderstore Mod Manager's exports give every file
        [InlineData("1980-01-01T00:00:00Z")] // a zip written without times
        public void AModpackFileWithAnOldTimeAfterACleanCloseIsPutBack(string stamped)
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring", Earlier.AddHours(1));
            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(2));

            // A mod manager extracts the pack's file and gives it the time stored in the zip.
            Write(profile, Solo, "the pack's", DateTime.Parse(stamped, null, System.Globalization.DateTimeStyles.AdjustToUniversal));

            var result = Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(2));
            Assert.True(result.Clean);
            Assert.Equal(1, result.PutBack);
            Assert.Equal("spring", File.ReadAllText(file));
        }

        [Fact]
        public void AfterACrashAModpackFileWithAnOldTimeIsTakenAsYours()
        {
            // The limit of a game Keepsake did not see close: an old time reads as written during it.
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);
            Launch(Earlier.AddHours(1));

            Write(profile, Solo, "the pack's", Earlier.AddHours(-5));

            var result = Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(2));
            Assert.Equal(1, result.Updated);
            Assert.Equal("the pack's", File.ReadAllText(file));
        }

        [Fact]
        public void AfterACrashWhatTheModWroteBeforeTheLogEndedIsKept()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);
            Launch(Earlier.AddHours(1));

            // No close: the game crashed after the mod wrote.
            Write(profile, Solo, "summer", Earlier.AddHours(2));

            var result = Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(2).AddSeconds(30));
            Assert.False(result.Clean);
            Assert.Equal(1, result.Updated);
            Assert.Equal("summer", File.ReadAllText(file));
            Assert.Equal("summer", CopyOf(Solo));
        }

        [Fact]
        public void AfterACrashAFileWrittenAfterTheLogEndedWaitsAndIsLeftAlone()
        {
            using var profile = new TestProfile();
            var file = Waiting(profile);

            Assert.Equal("the owner's", File.ReadAllText(file));
            Assert.Equal("spring", CopyOf(Solo));
            Assert.NotNull(FileKeeper.WaitingFor(Solo));
            Assert.Equal(1, FileKeeper.WaitingCount);

            // Still waiting at the launch after, and still untouched.
            var again = Launch(Earlier.AddDays(3), logEnd: Earlier.AddDays(2).AddHours(1));
            Assert.Equal(1, again.Waiting);
            Assert.Equal("the owner's", File.ReadAllText(file));
        }

        [Fact]
        public void GamesPlayedWithoutKeepsakeKeepWhatTheModWrote()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(1));

            // Keepsake is off, or gone and installed again later; the mod carries on meanwhile.
            Write(profile, Solo, "autumn", Earlier.AddDays(3));

            var result = Launch(Earlier.AddDays(10), logEnd: Earlier.AddDays(3).AddHours(1));
            Assert.False(result.Clean);
            Assert.Equal(1, result.Updated);
            Assert.Equal("autumn", File.ReadAllText(file));
        }

        [Fact]
        public void ASyncAfterGamesWithoutKeepsakeWaits()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(1));

            Write(profile, Solo, "the owner's", Earlier.AddDays(3));

            var result = Launch(Earlier.AddDays(4), logEnd: Earlier.AddDays(1));
            Assert.Equal(1, result.Waiting);
            Assert.Equal("the owner's", File.ReadAllText(file));
        }

        [Fact]
        public void ALogThatWentOnLongAfterTheCloseIsNotAGameKeepsakeSawClose()
        {
            var state = new SessionState { Started = Earlier, Closed = Earlier.AddHours(1) };

            Assert.True(KeptFiles.ClosedCleanly(state, Earlier.AddHours(1).AddSeconds(30)));
            Assert.False(KeptFiles.ClosedCleanly(state, Earlier.AddHours(1) + KeptFiles.LogGrace + TimeSpan.FromSeconds(1)));
            Assert.True(KeptFiles.ClosedCleanly(state, logEnd: null));

            // A launch after the close, and no close since: a crash, or a game the plugin did not load in.
            state.Started = Earlier.AddHours(2);
            Assert.False(KeptFiles.ClosedCleanly(state, Earlier.AddHours(1)));
            Assert.False(KeptFiles.ClosedCleanly(new SessionState(), Earlier));
        }

        [Fact]
        public void TheFirstLaunchWithoutASessionFileNeverPutsACopyBackOverAChange()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);

            // As after an update from 0.4.0, which kept no session file.
            Write(profile, Solo, "the owner's", Earlier.AddDays(1));

            var result = Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(5));
            Assert.Equal(1, result.Waiting);
            Assert.Equal("the owner's", File.ReadAllText(file));
        }

        [Fact]
        public void PutBackRestoresYourCopyAtTheNextLaunchAndTheCloseLeavesItAlone()
        {
            using var profile = new TestProfile();
            var file = Waiting(profile);

            Assert.Null(FileKeeper.PutBack(Solo));
            Assert.Equal(0, FileKeeper.WaitingCount);

            // The mod writes on during the game, but your copy is what you chose.
            Write(profile, Solo, "the owner's, played on", Earlier.AddDays(2).AddHours(1));
            Close(Earlier.AddDays(2).AddHours(2));
            Assert.Equal("spring", CopyOf(Solo));

            var result = Launch(Earlier.AddDays(3), logEnd: Earlier.AddDays(2).AddHours(2));
            Assert.Equal(1, result.PutBack);
            Assert.Equal("spring", File.ReadAllText(file));
            Assert.Null(FileKeeper.WaitingFor(Solo));
        }

        [Fact]
        public void KeepThisOneMakesTheCopyFromTheFile()
        {
            using var profile = new TestProfile();
            var file = Waiting(profile);

            Assert.Null(FileKeeper.KeepCurrent(Solo));

            Assert.Equal("the owner's", CopyOf(Solo));
            Assert.Null(FileKeeper.WaitingFor(Solo));
            Assert.DoesNotContain(Solo, File.ReadAllText(SessionFile.FilePath));

            Close(Earlier.AddDays(2).AddHours(1));
            var result = Launch(Earlier.AddDays(3), logEnd: Earlier.AddDays(2).AddHours(1));
            Assert.Equal(0, result.PutBack + result.Updated + result.Waiting);
            Assert.Equal("the owner's", File.ReadAllText(file));
        }

        [Fact]
        public void AWaitingFileThatMatchesItsCopyAgainNoLongerWaits()
        {
            using var profile = new TestProfile();
            var file = Waiting(profile);

            File.Copy(KeptFiles.Copy(Solo), file, true);
            File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(KeptFiles.Copy(Solo)));

            Assert.Equal(0, Launch(Earlier.AddDays(3)).Waiting);
            Assert.Null(FileKeeper.WaitingFor(Solo));
        }

        [Fact]
        public void ReleasingAWaitingFileForgetsTheQuestion()
        {
            using var profile = new TestProfile();
            var file = Waiting(profile);

            FileKeeper.Release(Timers);

            Assert.Null(FileKeeper.WaitingFor(Solo));
            Assert.Equal("the owner's", File.ReadAllText(file));
        }

        [Fact]
        public void TheSessionFileIsThereOnlyWhileSomethingIsKept()
        {
            using var profile = new TestProfile();
            Write(profile, Solo, "one");

            Launch(Earlier);
            Assert.False(File.Exists(SessionFile.FilePath));

            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(1));
            Assert.True(File.Exists(SessionFile.FilePath));

            FileKeeper.Release(Timers);
            Close(Earlier.AddHours(2));
            Assert.False(File.Exists(SessionFile.FilePath));
        }

        [Fact]
        public void ASessionFileOfANewerVersionIsNeverWrittenOverAndNothingIsPutBackOverAChange()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "spring");
            FileKeeper.Keep(Timers, isFolder: true);
            var newer = "# keepsake session v99\nclosed\t2026-09-01T13:00:00.0000000Z\n";
            File.WriteAllText(SessionFile.FilePath, newer);

            Close(Earlier.AddHours(1));
            Write(profile, Solo, "the owner's", Earlier.AddDays(1));
            var result = Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(1));

            Assert.Equal(newer, File.ReadAllText(SessionFile.FilePath));
            Assert.Equal(0, result.PutBack);
            Assert.Equal("the owner's", File.ReadAllText(file));
        }

        [Fact]
        public void TheSessionFileReadsBackWhatWasWritten()
        {
            var state = new SessionState { Started = Earlier, Closed = Earlier.AddHours(1), ClosedBy = "exit" };
            state.Waiting.Add(new WaitingFile { Path = Solo });
            state.Waiting.Add(new WaitingFile { Path = "Mod/state.json", PutBack = true });

            var read = SessionFile.Parse(SessionFile.Format(state));

            Assert.Equal(Earlier, read.Started);
            Assert.Equal(DateTimeKind.Utc, read.Started!.Value.Kind);
            Assert.Equal(Earlier.AddHours(1), read.Closed);
            Assert.Equal("exit", read.ClosedBy);
            Assert.False(read.WaitingFor(Solo)!.PutBack);
            Assert.True(read.WaitingFor("mod/STATE.json")!.PutBack);

            // A path a hand edit pointed outside config is dropped.
            Assert.Empty(SessionFile.Parse(new[] { SessionFile.Version, "[files]", "../BepInEx.cfg\tput back" }).Waiting);
        }

        [Fact]
        public void TheLogEndIsTheLatestOfTheLogFiles()
        {
            using var profile = new TestProfile();
            Assert.Null(SessionFile.LogEnd());

            var log = Path.Combine(profile.Root, "LogOutput.log");
            var second = Path.Combine(profile.Root, "LogOutput.log.1");
            File.WriteAllText(log, "a");
            File.WriteAllText(second, "b");
            File.SetLastWriteTimeUtc(log, Earlier);
            File.SetLastWriteTimeUtc(second, Earlier.AddHours(1));

            Assert.Equal(Earlier.AddHours(1), SessionFile.LogEnd());
        }

        [Fact]
        public void FilesASyncBroughtIntoAKeptFolderAreLeftAlone()
        {
            using var profile = new TestProfile();
            Write(profile, Solo, "one");
            FileKeeper.Keep(Timers, isFolder: true);
            Close(Earlier.AddHours(1));

            var owners = Write(profile, Timers + "/Server.Seasonality.bin", "the owner's", Earlier.AddDays(1));

            var result = Launch(Earlier.AddDays(2), logEnd: Earlier.AddHours(1));
            Assert.Equal(0, result.PutBack + result.Updated + result.Waiting);
            Assert.Equal("the owner's", File.ReadAllText(owners));
        }

        [Fact]
        public void ReleasingRemovesTheCopiesAndLeavesTheFile()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "one");
            FileKeeper.Keep(Timers, isFolder: true);

            FileKeeper.Release(Timers);

            Assert.Equal("one", File.ReadAllText(file));
            Assert.False(Directory.Exists(Path.Combine(profile.Root, "keepsake-files", "Seasonality", "LastSeasonChangeData")));
            Assert.False(File.Exists(KeptFiles.ListPath));
        }

        [Fact]
        public void KeepingAFolderTakesInWhatWasKeptInsideIt()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "one");
            FileKeeper.Keep(Solo, isFolder: false);

            FileKeeper.Keep("Seasonality", isFolder: true);

            var kept = Assert.Single(FileKeeper.Kept);
            Assert.Equal("Seasonality", kept.Path);
            Assert.Same(kept, FileKeeper.KeptBy(Solo));

            Close(Earlier.AddHours(1));
            File.Delete(file);
            Assert.Equal(1, Launch(Earlier.AddDays(1), logEnd: Earlier.AddHours(1)).PutBack);
        }

        [Theory]
        [InlineData("../BepInEx.cfg")]
        [InlineData("Mod/../../x")]
        [InlineData("/etc/passwd")]
        [InlineData("C:/Windows/win.ini")]
        [InlineData("  ")]
        public void APathOutsideConfigIsNeverKept(string path)
        {
            using var profile = new TestProfile();

            Assert.Null(KeptFiles.Normalise(path));
            Assert.NotNull(FileKeeper.Keep(path, isFolder: false));
            Assert.Empty(KeptFiles.Parse(new[] { KeptFiles.Version, path }));
        }

        [Fact]
        public void AListOfANewerVersionIsNeverWrittenOver()
        {
            using var profile = new TestProfile();
            var newer = "# keepsake files v99\nSeasonality/\n";
            File.WriteAllText(KeptFiles.ListPath, newer);
            Write(profile, "Mod/state.json", "mine");

            Assert.NotNull(FileKeeper.Keep("Mod/state.json", isFolder: false));
            Assert.Equal(newer, File.ReadAllText(KeptFiles.ListPath));
        }

        [Fact]
        public void TheFilesListShowsFilesAndFoldersButNotSettingsLogsOrLeftovers()
        {
            using var profile = new TestProfile();
            Write(profile, "a.cfg", "settings of a loaded mod");
            Write(profile, "BepInEx.cfg", "BepInEx's own");
            Write(profile, "other.cfg", "a file of another kind");
            Write(profile, "notes.yml", "a list");
            Write(profile, "Seasonality/Tweaks/Plants.yml", "tweaks");
            Write(profile, "Seasonality/Seasonality-LogOutput.log", "a log");
            Write(profile, "Seasonality/x.bin" + PinFile.TempSuffix, "half written");

            var items = FileKeeper.Browse(new[] { "a.cfg" });

            Assert.Equal(new[] { "notes.yml", "other.cfg", "Seasonality/", "Seasonality/Tweaks/", "Seasonality/Tweaks/Plants.yml" },
                items.Select(i => i.Path + (i.IsFolder ? "/" : "")));
            Write(profile, "Seasonality/Default/tree@winter.png", "an image");
            items = FileKeeper.Browse(new[] { "a.cfg" });
            var folder = items.Single(i => i.Path == "Seasonality");
            Assert.Equal(2, folder.Files);
            Assert.Equal(1, folder.Images);
            Assert.True(FileKeeper.IsImage("x/Tree@Fall.PNG"));
            Assert.False(FileKeeper.IsImage("x/World.Seasonality.bin"));
        }

        [Fact]
        public void TheFilesListCountsNumbersInNames()
        {
            using var profile = new TestProfile();
            foreach (var n in new[] { 10, 2, 1, 11 }) Write(profile, $"Test/{n}-file.txt", "x");
            Write(profile, "Test 2/a.txt", "x");
            Write(profile, "Test 10/a.txt", "x");

            var paths = FileKeeper.Browse(new string[0]).Select(i => i.Path + (i.IsFolder ? "/" : "")).ToArray();

            Assert.Equal(new[]
            {
                "Test/", "Test/1-file.txt", "Test/2-file.txt", "Test/10-file.txt", "Test/11-file.txt",
                "Test 2/", "Test 2/a.txt", "Test 10/", "Test 10/a.txt",
            }, paths);
        }

        [Fact]
        public void TheLeftoverCheckCoversKeptFilesAndTheirCopies()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Solo, "one");
            FileKeeper.Keep(Timers, isFolder: true);

            var paths = Restorer.KeptFilePaths(KeptFiles.Read()).ToList();

            Assert.Contains(file, paths);
            Assert.Contains(KeptFiles.Copy(Solo), paths);
        }
    }
}
