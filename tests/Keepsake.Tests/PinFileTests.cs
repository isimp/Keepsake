using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    public class PinFileTests
    {
        [Fact]
        public void APinsFileSavedByAnEditorReadsTheSame()
        {
            var path = Path.Combine(Path.GetTempPath(), "keepsake-pins-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                // Notepad and friends: a byte order mark, Windows line endings, a blank line at the end.
                var text = PinFile.Version + "\r\na.cfg\tGeneral\tVolume\t0.2\t0.8\r\n\r\n";
                File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray());

                var pins = PinFile.Parse(File.ReadAllLines(path));

                var pin = Assert.Single(pins);
                Assert.Equal("0.2", pin.Value);
                Assert.Equal("0.8", pin.Profile);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ANewerPinsFileBehindAByteOrderMarkIsStillNotRead()
        {
            var path = Path.Combine(Path.GetTempPath(), "keepsake-pins-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var text = "# keepsake pins v2\r\na.cfg\tGeneral\tVolume\t0.2\r\n";
                File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray());

                Assert.Null(PinFile.Parse(File.ReadAllLines(path)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void LinesWithTooFewPartsAreSkipped()
        {
            var pins = PinFile.Parse(new[] { PinFile.Version, "a.cfg\tS\tK", "a.cfg\tS\tK2\tv" });
            Assert.Equal(new[] { "K2" }, pins.Select(p => p.Key));
        }

        [Fact]
        public void TheLastLineForASettingWins()
        {
            var pins = PinFile.Parse(new[] { "a.cfg\tS\tK\tfirst", "a.cfg\tS\tK\tsecond" });
            Assert.Equal("second", pins.Single().Value);
        }

        [Fact]
        public void FileNamesMatchWhateverTheirCaseAndSlashes()
        {
            Assert.Equal(PinFile.IdOf("Sub/Mod.cfg", "S", "K"), PinFile.IdOf("sub/mod.cfg", "S", "K"));

            var pin = PinFile.Parse(new[] { "Sub\\Mod.cfg\tS\tK\tv" }).Single();
            Assert.Equal("Sub/Mod.cfg", pin.File);
        }

        [Fact]
        public void SectionsAndKeysAreCaseSensitiveLikeBepInEx() =>
            Assert.NotEqual(PinFile.IdOf("a.cfg", "S", "K"), PinFile.IdOf("a.cfg", "s", "k"));

        [Theory]
        [InlineData("plain", true)]
        [InlineData("H + LeftAlt", true)]
        [InlineData("", true)]
        [InlineData("tab\there", false)]
        [InlineData("line\nbreak", false)]
        [InlineData(" padded", false)]
        [InlineData(null, false)]
        public void OnlyValuesThatSurviveTheFileAreStorable(string value, bool storable) =>
            Assert.Equal(storable, PinFile.Storable(value));

        [Fact]
        public void ReplaceTextWritesANewFileAndReplacesAnOldOneLeavingNothingBehind()
        {
            var folder = Path.Combine(Path.GetTempPath(), "keepsake-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "file.txt");

                PinFile.ReplaceText(path, "first");
                Assert.Equal("first", File.ReadAllText(path));

                PinFile.ReplaceText(path, "second");
                Assert.Equal("second", File.ReadAllText(path));

                Assert.Equal(new[] { "file.txt" }, Directory.GetFiles(folder).Select(Path.GetFileName));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
