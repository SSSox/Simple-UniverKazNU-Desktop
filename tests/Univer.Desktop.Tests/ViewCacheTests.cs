using System.Collections.Generic;
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

        [Fact]
        public void PreferencesSnapshotHitsWithoutRebuildingAndDetectsInPlaceEdits()
        {
            var cache = new ViewCache();
            var prefs = new Preferences
            {
                Animations = true,
                Readable = true,
                Language = "ru",
                Pins = new List<PinnedLink> { new PinnedLink { Title = "Schedule", Url = "https://univer.kaznu.kz/student/myschedule/" } },
                TileOrder = new List<string> { "/student/myschedule/" },
                HiddenTiles = new List<string>()
            };
            string cached;
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            var built = cache.Store(prefs, false, false, "script-1");
            Assert.True(cache.TryGet(prefs, false, false, out cached));
            Assert.Same(built, cached);
            Assert.Equal(1, cache.Builds);

            // A separate Preferences instance with equal values still hits.
            var clone = new Preferences
            {
                Animations = true,
                Readable = true,
                Language = "ru",
                Pins = new List<PinnedLink> { new PinnedLink { Title = "Schedule", Url = "https://univer.kaznu.kz/student/myschedule/" } },
                TileOrder = new List<string> { "/student/myschedule/" },
                HiddenTiles = null
            };
            Assert.True(cache.TryGet(clone, false, false, out cached));
            Assert.Same(built, cached);

            // In-place mutations of every tracked field miss the cache.
            Assert.False(cache.TryGet(prefs, true, false, out cached));
            Assert.False(cache.TryGet(prefs, false, true, out cached));
            prefs.Animations = false;
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            prefs.Animations = true;
            prefs.Readable = false;
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            prefs.Readable = true;
            prefs.Language = "en";
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            prefs.Language = "ru";
            prefs.Pins[0].Title = "Changed";
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            prefs.Pins[0].Title = "Schedule";
            prefs.Pins[0].Url = "https://univer.kaznu.kz/student/umkd/";
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            prefs.Pins[0].Url = "https://univer.kaznu.kz/student/myschedule/";
            prefs.TileOrder.Add("/student/umkd/");
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            prefs.TileOrder.RemoveAt(1);
            prefs.HiddenTiles.Add("/student/transcript/");
            Assert.False(cache.TryGet(prefs, false, false, out cached));
            prefs.HiddenTiles.Clear();

            // Restored to original values: still a hit.
            Assert.True(cache.TryGet(prefs, false, false, out cached));
            Assert.Equal(1, cache.Builds);
        }
    }
}
