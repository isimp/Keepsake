using System.Linq;
using Xunit;

namespace Keepsake.Tests
{
    /// <summary>The panel's lists count numbers the way people do.</summary>
    public class NaturalOrderTests
    {
        private static string[] Sorted(params string[] names) => names.OrderBy(n => n, NaturalOrder.IgnoreCase).ToArray();

        [Fact]
        public void NumbersCountAsNumbers()
        {
            var names = Enumerable.Range(1, 15).Select(n => n + "-file.txt").Reverse().ToArray();
            Assert.Equal(Enumerable.Range(1, 15).Select(n => n + "-file.txt"), Sorted(names));
        }

        [Fact]
        public void SectionsNumberedByModsSortByTheirNumber() =>
            Assert.Equal(new[] { "1 - Settings", "2 - Seasons", "10 - Tweaks" }, Sorted("10 - Tweaks", "1 - Settings", "2 - Seasons"));

        [Fact]
        public void NumbersInsideNamesAndCaseAndLeadingZeros()
        {
            Assert.Equal(new[] { "Item2", "item10", "Item20" }, Sorted("Item20", "item10", "Item2"));
            Assert.Equal(new[] { "a", "A1", "a01b", "a1b", "b" }, Sorted("b", "a1b", "A1", "a", "a01b"));
            Assert.True(NaturalOrder.IgnoreCase.Compare("x99999999999999999999", "x100000000000000000000") < 0);
        }

        [Fact]
        public void TheOrderIsTotal()
        {
            var c = NaturalOrder.IgnoreCase;
            Assert.Equal(0, c.Compare("same", "same"));
            Assert.NotEqual(0, c.Compare("a", "A"));
            Assert.NotEqual(0, c.Compare("01", "1"));
            Assert.Equal(-c.Compare("a", "A"), c.Compare("A", "a"));
            Assert.True(c.Compare(null, "a") < 0);
        }
    }
}
