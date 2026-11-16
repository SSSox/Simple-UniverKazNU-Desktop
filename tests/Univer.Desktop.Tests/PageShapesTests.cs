using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // The loading placeholder's memory of page layouts: the page script's answer is checked, pages of one kind share a shape,
    // and only the site's own pages are remembered.
    public class PageShapesTests
    {
        private const string Answer = "{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,32,100,1024,140,10,1,1],[1,48,120,300,10,4,0,0]]}";

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
        [InlineData("{\"v\":2,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[]}")]
        [InlineData("{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,0]]}")]
        [InlineData("{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[2,1,1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,4,0]]}")]
        [InlineData("{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,\"1\",1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":1,\"w\":1104,\"o\":900,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]}")]
        [InlineData("{\"v\":1,\"w\":50,\"o\":50,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]}")]
        public void AMalformedAnswerIsNoShapeAtAll(string json)
        {
            Assert.Null(PageShape.Parse(json));
        }

        [Fact]
        public void TooManyItemsAreRefused()
        {
            var items = string.Join(",", System.Linq.Enumerable.Repeat("[1,1,1,1,1,0,0,0]", PageShape.MaxItems + 1));
            Assert.Null(PageShape.Parse("{\"v\":1,\"w\":1104,\"o\":1104,\"h\":777,\"items\":[" + items + "]}"));
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
            Assert.Equal(0, PageShapes.Load("{\"v\":1,\"pages\":[{\"k\":\"https://example.com\",\"w\":1104,\"o\":1104,\"h\":777,\"items\":[[0,1,1,1,1,0,0,0]]},{\"k\":\"/a\",\"w\":1}]}").Count);
            Assert.Equal(0, PageShapes.Load("garbage").Count);
            Assert.Equal(0, PageShapes.Read(Path.Combine(Path.GetTempPath(), "missing-" + System.Guid.NewGuid().ToString("N") + ".json")).Count);
        }
    }
}
