using System.IO;
using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>
    /// The two files Keepsake and Bindrune read from each other, checked against the samples in
    /// tests/contract, which Bindrune holds identical copies of. A test here failing means a
    /// format changed: update the sample, the version line, and Bindrune, together.
    /// </summary>
    public class ContractTests
    {
        private static string[] Sample(string name) => File.ReadAllLines(Path.Combine("contract", name));

        private static string[] Data(string[] lines) => lines.Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();

        [Fact]
        public void KeepsakeWritesThePinsSample()
        {
            var sample = Sample("keepsake.pins");
            var pins = PinFile.Parse(sample);
            Assert.NotNull(pins);

            var written = PinFile.Format(pins);

            Assert.Equal(PinFile.Version, sample[0]);
            Assert.Equal(PinFile.Version, written[0]);
            Assert.Equal(Data(sample), Data(written));
        }

        [Fact]
        public void KeepsakeReadsTheBindruneSample()
        {
            var keys = BindruneLink.ParseKeys(Sample("bindrune.keys"));
            Assert.NotNull(keys);

            Assert.Equal(new[]
            {
                "cfg:isimp.Example:Keys:Open",
                "cfg:isimp.Example:Keys:Shortcut",
                "cfg:isimp.Example:Section: with colon:Toggle",
                "cfg:isimp.Example:Keys:Cleared",
            }, keys.Select(k => k.Id));

            var open = keys[0];
            Assert.Equal("K", open.Yours);
            Assert.Equal("H", open.Profile);
            Assert.True(open.Active);

            Assert.Equal("H + LeftAlt", keys[1].Yours);
            Assert.Equal("none", keys[1].Profile);
            Assert.False(keys[2].Active);
        }

        [Fact]
        public void TheBindruneSampleIsTheVersionKeepsakeReads() =>
            Assert.Equal(BindruneLink.StateVersion, Sample("bindrune.keys")[0]);

        [Fact]
        public void SectionsAfterTheKeysAreNotReadAsKeys()
        {
            var keys = BindruneLink.ParseKeys(Sample("bindrune.keys"));
            Assert.DoesNotContain(keys, k => k.Yours == "vanilla:Map");
        }
    }
}
