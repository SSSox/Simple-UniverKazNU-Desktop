using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class DocxTablesTests
    {
        private static string Render(string body, string hint = null)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false)))
                    writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body>" + body + "</w:body></w:document>");
                memory.Position = 0;
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Read)) return DocxHtml.Convert(zip, new PreviewOptions("QA", hint)).Html;
            }
        }
        private static string Cell(string text, string properties = "")
        { return "<w:tc><w:tcPr>" + properties + "</w:tcPr><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:tc>"; }
        private static XElement Table(string html)
        {
            var start = html.IndexOf("<table", StringComparison.Ordinal);
            var end = html.IndexOf("</table>", start, StringComparison.Ordinal) + 8;
            return XElement.Parse(Regex.Replace(html.Substring(start, end - start), "<col\\b([^>]*)>", "<col$1/>"));
        }
        // Expand HTML spans like a browser, checking where labels actually land in the grid.
        private static string[,] Matrix(XElement table, int columns)
        {
            var rows = table.Elements("tr").ToArray(); var grid = new string[rows.Length, columns];
            for (var r = 0; r < rows.Length; r++)
            {
                var c = 0;
                foreach (var cell in rows[r].Elements("td"))
                {
                    while (c < columns && grid[r, c] != null) c++;
                    var width = (int?)cell.Attribute("colspan") ?? 1;
                    var height = (int?)cell.Attribute("rowspan") ?? 1;
                    for (var y = r; y < r + height; y++) for (var x = c; x < c + width; x++)
                    { Assert.Null(grid[y, x]); grid[y, x] = cell.Value; }
                    c += width;
                }
            }
            return grid;
        }
        [Fact]
        public void VerticalAndHorizontalMergesKeepHeadersOverTheirColumns()
        {
            var span = "<w:gridSpan w:val='2'/>";
            var before = "<w:trPr><w:gridBefore w:val='1'/></w:trPr>";
            var html = Render("<w:tbl><w:tr>" + before + Cell("A", span + "<w:vMerge w:val='restart'/>") + Cell("B", span) + "</w:tr>" +
                "<w:tr>" + before + Cell("", span + "<w:vMerge/>") + Cell("C") + Cell("D") + "</w:tr>" +
                "<w:tr>" + before + Cell("", span + "<w:vMerge w:val='continue'/>") + Cell("E", span) + "</w:tr>" +
                "<w:tr>" + before + Cell("F", span) + Cell("G", span) + "</w:tr></w:tbl>");
            var table = Table(html); var grid = Matrix(table, 5);
            Assert.Equal("3", table.Elements("tr").First().Elements("td").ElementAt(1).Attribute("rowspan").Value);
            Assert.Equal(new[] { "", "A", "A", "C", "D" }, Enumerable.Range(0, 5).Select(c => grid[1, c]));
            Assert.Equal(new[] { "", "A", "A", "E", "E" }, Enumerable.Range(0, 5).Select(c => grid[2, c]));
            Assert.Equal(new[] { "", "F", "F", "G", "G" }, Enumerable.Range(0, 5).Select(c => grid[3, c]));
        }
        [Fact]
        public void AGapOrDifferentColumnSpanEndsThePreviousVerticalMerge()
        {
            var html = Render("<w:tbl><w:tr>" + Cell("A", "<w:vMerge w:val='restart'/>") + Cell("B") + "</w:tr>" +
                "<w:tr>" + Cell("Gap") + Cell("C") + "</w:tr><w:tr>" + Cell("Orphan", "<w:vMerge/>") + Cell("D") + "</w:tr>" +
                "<w:tr>" + Cell("Different", "<w:gridSpan w:val='2'/><w:vMerge/>") + "</w:tr></w:tbl>");
            Assert.DoesNotContain("rowspan", html);
            Assert.Contains("<p>Orphan</p>", html); Assert.Contains("<p>Different</p>", html);
        }
        [Fact]
        public void NestedTablesShareOneScrollSurfaceAndKeepAllText()
        {
            var html = Render("<w:tbl><w:tr><w:tc>" + "<w:tbl><w:tr>" + Cell("Nested") + "</w:tr></w:tbl>" +
                "<w:p><w:r><w:t>After nested table</w:t></w:r></w:p></w:tc></w:tr></w:tbl>");
            Assert.Single(Regex.Matches(html, "class=\"table-scroll\"").Cast<Match>());
            Assert.Equal(2, Regex.Matches(html, "<table[ >]").Count);
            Assert.Contains("Nested", html); Assert.Contains("After nested table", html);
        }
        [Fact]
        public void GridProportionsAreNumericAndCultureIndependent()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var html = Render("<w:tbl><w:tblGrid><w:gridCol w:w='701'/><w:gridCol w:w='993'/><w:gridCol w:w='1842'/></w:tblGrid>" +
                    "<w:tr>" + Cell("1") + Cell("2") + Cell("3") + "</w:tr></w:tbl>");
                var cols = Table(html).Element("colgroup").Elements("col").ToArray();
                Assert.Equal(3, cols.Length);
                var widths = cols.Select(c => double.Parse(Regex.Match(c.Attribute("style").Value, "[0-9.]+").Value, CultureInfo.InvariantCulture)).ToArray();
                Assert.InRange(widths.Sum(), 99.99, 100.01); Assert.True(widths[0] < widths[1] && widths[1] < widths[2]);
                Assert.DoesNotContain(",", cols[0].Attribute("style").Value);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
        [Theory]
        [InlineData("<w:gridSpan w:val='2147483647'/>")]
        [InlineData("<w:gridSpan w:val='513'/>")]
        public void ExcessiveGridSpansFailWithoutAllocatingAnUnboundedGrid(string properties)
        { Assert.Throws<InvalidDataException>(() => Render("<w:tbl><w:tr>" + Cell("A", properties) + "</w:tr></w:tbl>")); }
        [Fact]
        public void UntrustedGridWidthsAndHintsCannotInjectMarkup()
        {
            var html = Render("<w:tbl><w:tblGrid><w:gridCol w:w='&quot; onmouseover=&quot;bad'/></w:tblGrid><w:tr>" + Cell("Safe") + "</w:tr></w:tbl>", "<script>bad</script>");
            Assert.DoesNotContain("onmouseover", html); Assert.DoesNotContain("<script", html);
            Assert.Contains("&lt;script&gt;bad&lt;/script&gt;", html); Assert.Contains("Safe", html);
        }
    }
}
