using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>A file in a version the reader does not know is left alone, never guessed at.</summary>
    public class VersionGateTests
    {
        [Fact]
        public void BindruneKeysOfAnotherVersionAreNotRead()
        {
            var lines = new[] { "# bindrune state v4", "[keys]", "cfg:a:b:c\tK\tH\t1" };
            Assert.Null(BindruneLink.ParseKeys(lines));
        }

        [Fact]
        public void BindruneKeysWithoutAVersionAreNotRead()
        {
            var lines = new[] { "[keys]", "cfg:a:b:c\tK\tH\t1" };
            Assert.Null(BindruneLink.ParseKeys(lines));
        }

        [Fact]
        public void AnEmptyBindruneFileHoldsNoKeys() =>
            Assert.Empty(BindruneLink.ParseKeys(new string[0]));

        [Fact]
        public void PinsOfANewerVersionAreNotRead()
        {
            var lines = new[] { "# keepsake pins v2", "a.cfg\tS\tK\tv\tp" };
            Assert.Null(PinFile.Parse(lines));
        }

        [Fact]
        public void PinsWithoutAVersionLineAreRead()
        {
            // A file started by hand: the version line is written on the next save.
            var pins = PinFile.Parse(new[] { "a.cfg\tS\tK\tv" });
            Assert.Single(pins);
            Assert.Null(pins.Single().Profile);
        }
    }
}
