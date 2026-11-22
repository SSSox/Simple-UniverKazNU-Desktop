using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ViewCacheTests
    {
        private static string Key(string a, string[] pins, string[] order = null)
        { return ViewCache.Key(new[] { a }, pins, order ?? new string[0]); }

        [Fact]
        public void ValuesDecideNotObjects()
        {
            var cache = new ViewCache();
            var one = cache.Get(Key("ru", new[] { "t", "u" }), () => "A");
            // A different list object with the same contents is a hit.
            var two = cache.Get(Key("ru", new[] { "t", "u" }), () => "B");
            Assert.Equal("A", one); Assert.Equal("A", two); Assert.Equal(1, cache.Builds);
        }

        [Fact]
        public void AnyChangedValueRebuilds()
        {
            var cache = new ViewCache();
            cache.Get(Key("ru", new[] { "t", "u" }), () => "A");
            Assert.Equal("B", cache.Get(Key("kz", new[] { "t", "u" }), () => "B"));
            Assert.Equal("C", cache.Get(Key("kz", new[] { "t", "v" }), () => "C"));
            Assert.Equal("D", cache.Get(Key("kz", new[] { "t", "v" }, new[] { "x" }), () => "D"));
            Assert.Equal(4, cache.Builds);
        }

        [Fact]
        public void ItemBoundariesAreNotBlurred()
        {
            Assert.NotEqual(Key("ru", new[] { "ab", "c" }), Key("ru", new[] { "a", "bc" }));
            Assert.NotEqual(Key("ru", new[] { "a" }, new[] { "b" }), Key("ru", new[] { "a", "b" }));
        }
    }
}
