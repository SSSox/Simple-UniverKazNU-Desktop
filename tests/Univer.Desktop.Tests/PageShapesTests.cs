using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // The loading placeholder's memory of page layouts: the page script's answer is checked, pages of one kind share a shape,
    // and only the site's own pages are remembered.
    public class PageShapesTests
    {
        private const string Answer = "{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,32,100,1024,140,10,1,1],[1,48,120,300,10,4,0,0]]}";

        [Fact]
        public void AWellFormedAnswerIsAShape()
        {
            var shape = PageShape.Parse(Answer);
            Assert.NotNull(shape);
            Assert.Equal(2, shape.Items.Count);
            Assert.Equal(new[] { 0, 32, 100, 1024, 140, 10, 1, 1 }, shape.Items[0]);
            Assert.True(shape.Fits(1104)); Assert.True(shape.Fits(1106.5f)); Assert.False(shape.Fits(1000));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":3,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[]}")]
        [InlineData("{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,0]]}")]
        [InlineData("{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[2,1,1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,4,0]]}")]
        [InlineData("{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,\"1\",1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":2,\"w\":1104,\"o\":900,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":2,\"w\":50,\"o\":50,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]}")]
        public void AMalformedAnswerIsNoShapeAtAll(string json)
        {
            Assert.Null(PageShape.Parse(json));
        }

        [Fact]
        public void ShapesSavedByAnEarlierVersionAreDroppedOnLoad()
        {
            // Version 1 took bars stuck to the window's bottom (the news pager) as part of the first screen.
            const string old = "{\"v\":1,\"pages\":[{\"k\":\"/news/student/#\",\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,32,100,1024,140,10,1,1]]}]}";
            Assert.Equal(0, PageShapes.Load(old).Count);
            Assert.Equal(1, PageShapes.Load(old.Replace("\"v\":1", "\"v\":2")).Count);
        }

        [Fact]
        public void TooManyItemsAreRefused()
        {
            var items = string.Join(",", System.Linq.Enumerable.Repeat("[1,1,1,1,1,0,0,0]", PageShape.MaxItems + 1));
            Assert.Null(PageShape.Parse("{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[" + items + "]}"));
        }

        [Fact]
        public void PagesOfOneKindShareAKeyAndOtherSitesHaveNone()
        {
            Assert.Equal("/student/umkd/#/#/#", PageShapes.Key("https://univer.kaznu.kz/student/umkd/99281/2026/1"));
            Assert.Equal(PageShapes.Key("https://univer.kaznu.kz/student/umkd/99281/2026/1"), PageShapes.Key("https://univer.kaznu.kz/Student/UMKD/12/2025/2/?x=1#top"));
            Assert.Equal("/student/myschedule", PageShapes.Key("https://univer.kaznu.kz/student/myschedule/"));
            Assert.Equal("/", PageShapes.Key("https://univer.kaznu.kz/"));
            Assert.Null(PageShapes.Key("https://example.com/student/myschedule/"));
            Assert.Null(PageShapes.Key("http://univer.kaznu.kz/student/myschedule/"));
            Assert.Null(PageShapes.Key("document"));
            Assert.Null(PageShapes.Key(null));
        }

        [Fact]
        public void TheLatestShapesAreKeptAndSurviveARestart()
        {
            var shapes = new PageShapes(); var shape = PageShape.Parse(Answer);
            for (var i = 0; i < 45; i++) shapes.Remember("https://univer.kaznu.kz/page" + new string('x', i + 1) + "/", shape);
            Assert.Equal(40, shapes.Count);
            Assert.Null(shapes.Find("https://univer.kaznu.kz/pagex/"));
            Assert.NotNull(shapes.Find("https://univer.kaznu.kz/page" + new string('x', 45) + "/"));
            shapes.Remember("https://example.com/", shape);
            Assert.Equal(40, shapes.Count);

            var path = Path.Combine(Path.GetTempPath(), "page-shapes-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                shapes.Write(path);
                var loaded = PageShapes.Read(path);
                Assert.Equal(40, loaded.Count);
                Assert.Equal(shape.Items[1], loaded.Find("https://univer.kaznu.kz/page" + new string('x', 45) + "/").Items[1]);
                loaded.Clear(); Assert.Equal(0, loaded.Count);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ADamagedFileStartsEmpty()
        {
            Assert.Equal(0, PageShapes.Load("{\"v\":2,\"pages\":[{\"k\":\"https://example.com\",\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]},{\"k\":\"/a\",\"w\":1}]}").Count);
            Assert.Equal(0, PageShapes.Load("garbage").Count);
            Assert.Equal(0, PageShapes.Read(Path.Combine(Path.GetTempPath(), "missing-" + System.Guid.NewGuid().ToString("N") + ".json")).Count);
        }

        [Fact]
        public void FromDictionaryValidatesWithAndWithoutVersion()
        {
            var dict = new System.Collections.Generic.Dictionary<string, object>
            {
                { "w", 1104 }, { "o", 1104 }, { "h", 777 },
                { "items", new object[] { new object[] { 0, 32, 100, 1024, 140, 10, 1, 1 } } }
            };
            Assert.Null(PageShape.FromDictionary(null));
            Assert.Null(PageShape.FromDictionary(dict, requireVersion: true));
            var shape = PageShape.FromDictionary(dict, requireVersion: false);
            Assert.NotNull(shape);
            Assert.Equal(1104, shape.Width);
            Assert.Single(shape.Items);
            Assert.Equal(new[] { 0, 32, 100, 1024, 140, 10, 1, 1 }, shape.Items[0]);

            dict["v"] = PageShape.Version;
            Assert.NotNull(PageShape.FromDictionary(dict, requireVersion: true));
            dict["v"] = PageShape.Version + 1;
            Assert.Null(PageShape.FromDictionary(dict, requireVersion: true));
        }

        [Fact]
        public void LoadHandlesExtraEntryVersionAndMixedInvalidPages()
        {
            const string mixed = "{\"v\":2,\"pages\":[" +
                "{\"k\":\"/first\",\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,32,100,500,80,8,1,1]]}," +
                "{\"k\":\"/bad\",\"w\":10,\"o\":10,\"h\":10,\"items\":[[0,1,1,1,1,0,0,0]]}," +
                "{\"k\":\"/second\",\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[1,48,120,300,10,4,0,0]]}" +
                "]}";
            var loaded = PageShapes.Load(mixed);
            Assert.Equal(2, loaded.Count);
            Assert.NotNull(loaded.Find("https://univer.kaznu.kz/first"));
            Assert.Null(loaded.Find("https://univer.kaznu.kz/bad"));
            Assert.Equal(new[] { 1, 48, 120, 300, 10, 4, 0, 0 }, loaded.Find("https://univer.kaznu.kz/second").Items[0]);
        }

        [Fact]
        public void CloneCreatesAnIndependentSnapshotForBackgroundSerialization()
        {
            var shapes = new PageShapes();
            var first = PageShape.Parse(Answer);
            var second = PageShape.Parse("{\"v\":2,\"w\":1000,\"o\":1000,\"h\":700,\"items\":[[0,10,20,300,40,6,2,0]]}");
            shapes.Remember("https://univer.kaznu.kz/student/myschedule/", first);

            var snapshot = shapes.Clone();
            shapes.Remember("https://univer.kaznu.kz/student/attestation/", second);
            shapes.Clear();

            Assert.Equal(0, shapes.Count);
            Assert.Equal(1, snapshot.Count);
            var reloaded = PageShapes.Load(snapshot.Serialize());
            Assert.Equal(1, reloaded.Count);
            Assert.NotNull(reloaded.Find("https://univer.kaznu.kz/student/myschedule/"));
            Assert.Null(reloaded.Find("https://univer.kaznu.kz/student/attestation/"));
        }
    }
}
