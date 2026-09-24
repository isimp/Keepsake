using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// Kept files: a copy saved while the game runs, put back at launch when a profile sync
    /// replaced or removed the file, and nothing else touched.
    /// </summary>
    [Collection(ProfileCollection.Name)]
    public class KeptFilesTests
    {
        private const string Timers = "Seasonality/LastSeasonChangeData";

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

        /// <summary>The next launch: the preloader reads the list and puts copies back.</summary>
        private static int Launch() => KeptFiles.Restore(KeptFiles.Read());

        [Fact]
        public void AKeptFolderIsCopiedOutsideConfigUnderANameNoSyncTakes()
        {
            using var profile = new TestProfile();
            Write(profile, Timers + "/Solo.Seasonality.bin", "one");

            Assert.Null(FileKeeper.Keep(Timers, isFolder: true));

            var copy = Path.Combine(profile.Root, "keepsake-files", "Seasonality", "LastSeasonChangeData", "Solo.Seasonality.bin.kept");
            Assert.Equal("one", File.ReadAllText(copy));
            Assert.Contains(Timers + "/", File.ReadAllLines(KeptFiles.ListPath));

            // A sync takes cfg, txt, json, yml, yaml and ini files from anywhere in the profile.
            foreach (var ending in new[] { ".cfg", ".txt", ".json", ".yml", ".yaml", ".ini" })
            {
                Assert.False(copy.EndsWith(ending));
                Assert.False(KeptFiles.ListPath.EndsWith(ending));
            }
        }

        [Fact]
        public void AFileASyncRemovedIsPutBackAtLaunch()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Timers + "/Solo.Seasonality.bin", "one");
            FileKeeper.Keep(Timers, isFolder: true);

            File.Delete(file);

            Assert.Equal(1, Launch());
            Assert.Equal("one", File.ReadAllText(file));
            Assert.Equal(Earlier, File.GetLastWriteTimeUtc(file));
        }

        [Fact]
        public void AFileASyncReplacedIsPutBackAtLaunch()
        {
            using var profile = new TestProfile();
            var file = Write(profile, "Mod/state.json", "mine");
            FileKeeper.Keep("Mod/state.json", isFolder: false);

            Write(profile, "Mod/state.json", "the owner's", Earlier.AddDays(1));

            Assert.Equal(1, Launch());
            Assert.Equal("mine", File.ReadAllText(file));
        }

        [Fact]
        public void AFileNobodyTouchedIsLeftAsItIs()
        {
            using var profile = new TestProfile();
            Write(profile, Timers + "/Solo.Seasonality.bin", "one");
            FileKeeper.Keep(Timers, isFolder: true);

            Assert.Equal(0, Launch());
        }

        [Fact]
        public void WhatTheModWritesWhileTheGameRunsIsWhatComesBack()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Timers + "/Solo.Seasonality.bin", "spring");
            FileKeeper.Keep(Timers, isFolder: true);

            // The season moved on, and a new world was made.
            Write(profile, Timers + "/Solo.Seasonality.bin", "summer", Earlier.AddHours(1));
            var added = Write(profile, Timers + "/Other.Seasonality.bin", "winter");
            FileKeeper.Flush();

            File.Delete(file);
            File.Delete(added);
            Assert.Equal(2, Launch());
            Assert.Equal("summer", File.ReadAllText(file));
            Assert.Equal("winter", File.ReadAllText(added));
        }

        [Fact]
        public void FilesASyncBroughtIntoAKeptFolderAreLeftAlone()
        {
            using var profile = new TestProfile();
            Write(profile, Timers + "/Solo.Seasonality.bin", "one");
            FileKeeper.Keep(Timers, isFolder: true);

            var owners = Write(profile, Timers + "/Server.Seasonality.bin", "the owner's");

            Assert.Equal(0, Launch());
            Assert.Equal("the owner's", File.ReadAllText(owners));
        }

        [Fact]
        public void ReleasingRemovesTheCopiesAndLeavesTheFile()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Timers + "/Solo.Seasonality.bin", "one");
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
            var file = Write(profile, Timers + "/Solo.Seasonality.bin", "one");
            FileKeeper.Keep(Timers + "/Solo.Seasonality.bin", isFolder: false);

            FileKeeper.Keep("Seasonality", isFolder: true);

            var kept = Assert.Single(FileKeeper.Kept);
            Assert.Equal("Seasonality", kept.Path);
            Assert.Same(kept, FileKeeper.KeptBy(Timers + "/Solo.Seasonality.bin"));

            File.Delete(file);
            Assert.Equal(1, Launch());
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
        public void TheLeftoverCheckCoversKeptFilesAndTheirCopies()
        {
            using var profile = new TestProfile();
            var file = Write(profile, Timers + "/Solo.Seasonality.bin", "one");
            FileKeeper.Keep(Timers, isFolder: true);

            var paths = Restorer.KeptFilePaths(KeptFiles.Read()).ToList();

            Assert.Contains(file, paths);
            Assert.Contains(KeptFiles.Copy(Timers + "/Solo.Seasonality.bin"), paths);
        }
    }
}
