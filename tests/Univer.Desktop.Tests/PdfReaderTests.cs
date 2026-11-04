using System;
using System.Collections.Generic;
using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class PreviewRoutesTests
    {
        [Theory]
        [InlineData("/token/%D0%9A%D1%83%D1%80%D1%81%20QA.pdf", new[] { "token", "Курс QA.pdf" })]
        [InlineData("/token/file/Name.pdf", new[] { "token", "file", "Name.pdf" })]
        [InlineData("/assets/pdfjs/cmaps/Adobe-Japan1-UCS2.bcmap", new[] { "assets", "pdfjs", "cmaps", "Adobe-Japan1-UCS2.bcmap" })]
        public void PathsSplitIntoUnescapedSegments(string path, string[] expected)
        {
            string[] segments;
            Assert.True(PreviewRoutes.TrySplit(path, out segments));
            Assert.Equal(expected, segments);
        }
        [Theory]
        [InlineData("")] [InlineData("/")] [InlineData("/token")] [InlineData("/token/")] [InlineData("/token//x")]
        [InlineData("/token/..")] [InlineData("/token/%2E%2E")] [InlineData("/token/a%2Fb")] [InlineData("/token/a%5Cb")]
        [InlineData("/token/C%3A")] [InlineData("/token/%00")] [InlineData("token/a")]
        public void UnsafeOrEmptySegmentsAreRefused(string path)
        {
            string[] segments;
            Assert.False(PreviewRoutes.TrySplit(path, out segments));
        }
        [Fact]
        public void OnlyTheReaderFoldersOfTheAppAssetsAreServed()
        {
            var root = Path.Combine(Path.GetTempPath(), "univer-assets-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "pdfjs", "cmaps"));
                Directory.CreateDirectory(Path.Combine(root, "pdf-reader"));
                File.WriteAllText(Path.Combine(root, "pdfjs", "cmaps", "a.bcmap"), "x");
                File.WriteAllText(Path.Combine(root, "pdf-reader", "reader.mjs"), "x");
                File.WriteAllText(Path.Combine(root, "desktop.js"), "x");
                Func<string, string> resolve = path => { string[] segments; return PreviewRoutes.TrySplit(path, out segments) ? PreviewRoutes.AssetFile(root, segments) : null; };
                Assert.Equal(Path.Combine(root, "pdfjs", "cmaps", "a.bcmap"), resolve("/assets/pdfjs/cmaps/a.bcmap"));
                Assert.Equal(Path.Combine(root, "pdf-reader", "reader.mjs"), resolve("/assets/pdf-reader/reader.mjs"));
                // The site automation scripts, other folders, hidden names, missing files and folders themselves stay private.
                foreach (var refused in new[] { "/assets/desktop.js", "/assets/other/desktop.js", "/assets/pdfjs/.hidden", "/assets/pdfjs/missing.mjs",
                    "/assets/pdfjs/cmaps", "/assets/pdfjs/%D0%B0.mjs", "/token/pdfjs/cmaps/a.bcmap" })
                    Assert.Null(resolve(refused));
            }
            finally { Directory.Delete(root, true); }
        }
        [Fact]
        public void ReaderFilesHaveTheirTypes()
        {
            Assert.Equal("text/javascript", PreviewRoutes.MimeType("pdf.min.mjs"));
            Assert.Equal("text/css", PreviewRoutes.MimeType("reader.css"));
            Assert.Equal("application/wasm", PreviewRoutes.MimeType("openjpeg.wasm"));
            Assert.Equal("application/octet-stream", PreviewRoutes.MimeType("Adobe-Japan1-UCS2.bcmap"));
            Assert.Equal("text/plain", PreviewRoutes.MimeType("LICENSE"));
        }
        [Theory]
        [InlineData("https://univer-files.local/token/a.pdf", true)]
        [InlineData("http://univer-files.local/token/a.pdf", false)]
        [InlineData("https://univer-files.local:444/token/a.pdf", false)]
        [InlineData("https://user@univer-files.local/token/a.pdf", false)]
        [InlineData("https://univer-files.local.example/token/a.pdf", false)]
        [InlineData("not a url", false)]
        public void OnlyTheViewerHostIsOwned(string url, bool owned) { Assert.Equal(owned, PreviewRoutes.Owns(url)); }
        [Fact]
        public void PageAndFileAddressesEscapeTheName()
        {
            Assert.Equal("https://univer-files.local/t/%D0%9A%D1%83%D1%80%D1%81%20QA.pdf", PreviewRoutes.PageUrl("t", "Курс QA.pdf"));
            Assert.Equal("file/%23%3F.pdf", PreviewRoutes.FilePath("#?.pdf"));
            Assert.Equal("file/#?.pdf", PreviewRoutes.FileKey("#?.pdf"));
            Assert.Equal("https://univer-files.local/t/file", PreviewRoutes.PageUrl("t", ""));
        }
    }
    public class PdfReaderPageTests
    {
        [Fact]
        public void PageCarriesItsFileScaleAndTextsWithoutInlineScript()
        {
            var html = PdfReaderPage.Html("Курс QA.pdf", "kz", 1.25, new Dictionary<string, string> { { "loading", "Ашылуда…" }, { "noText", "</script><script>alert(1)</script>" } });
            Assert.Contains("<html lang=\"kk\">", html);
            Assert.Contains("<title>Курс QA.pdf</title>", html);
            Assert.Contains("<script type=\"module\" src=\"/assets/pdf-reader/start.mjs\"></script>", html);
            Assert.Contains("<link rel=\"stylesheet\" href=\"/assets/pdf-reader/reader.css\">", html);
            Assert.Contains("\"file\":\"file/%D0%9A%D1%83%D1%80%D1%81%20QA.pdf\"", html);
            Assert.Contains("\"scale\":1.25", html);
            Assert.Contains("<p class=\"status\" role=\"status\">Ашылуда…</p>", html);
            // The configuration is data: a text cannot close its script element and start one of its own.
            Assert.Equal(2, html.Split(new[] { "</script>" }, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("<script>", html);
        }
        [Fact]
        public void TitleIsEncodedAndLanguageDefaultsToRussian()
        {
            var html = PdfReaderPage.Html("<b>\"x\"</b>.pdf", "ru", 0, null);
            Assert.Contains("<html lang=\"ru\">", html);
            Assert.Contains("<title>&lt;b&gt;&quot;x&quot;&lt;/b&gt;.pdf</title>", html);
            Assert.Contains("\"scale\":1", html);
            Assert.DoesNotContain("<b>", html);
        }
    }
}
