using System;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public sealed class GradeReportTests
    {
        [Theory]
        [InlineData("document-1", "42", 7, true)]
        [InlineData("document-1", null, 7, false)]
        [InlineData("document-1", "", 7, false)]
        [InlineData("document-1", "0", 7, false)]
        [InlineData("document-1", "43", 7, false)]
        [InlineData("document-2", "42", 7, false)]
        [InlineData(null, "42", 7, false)]
        [InlineData("", "42", 7, false)]
        [InlineData("document-1", "42", 8, false)]
        public void Source_RequiresTheSameDocumentOpeningAndSession(string document, string opening, int generation, bool expected)
        {
            var source = new GradeReport.Source("document-1", "42", 7);
            Assert.Equal(expected, source.StillOpen(new GradeReport.Source(document, opening, generation)));
        }

        [Theory]
        [InlineData(null, "42")]
        [InlineData("", "42")]
        [InlineData("document-1", null)]
        [InlineData("document-1", "")]
        [InlineData("document-1", "0")]
        public void Source_RequiresAPositiveOrigin(string document, string opening)
        {
            var source = new GradeReport.Source(document, opening, 7);
            Assert.False(source.StillOpen(new GradeReport.Source(document, opening, 7)));
        }

        [Fact]
        public void Source_UnansweredFinalQueryCancelsAFinishedExport()
        {
            var source = new GradeReport.Source("document-1", "42", 7);
            Assert.True(source.StillOpen(new GradeReport.Source("document-1", "42", 7)));
            // PDF rendering finished, but a reload destroyed the renderer context before the final query replied.
            Assert.False(source.StillOpen(null));
        }

        [Theory]
        [InlineData("print", "print")]
        [InlineData("pdf", "pdf")]
        [InlineData("png", "png")]
        [InlineData("copy", "copy")]
        [InlineData("reveal", "reveal")]
        [InlineData("PDF", null)]
        [InlineData("pdf ", null)]
        [InlineData("pdf:C:\\Windows\\x.pdf", null)]
        [InlineData("open", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        [InlineData("copy:1", "copy")]
        [InlineData("copy:12", "copy")]
        [InlineData("copy:13", null)]
        [InlineData("copy:0", null)]
        [InlineData("copy:02", null)]
        [InlineData("copy:", null)]
        [InlineData("copy:1 ", null)]
        [InlineData("copy:-1", null)]
        [InlineData("copy:١", null)]
        [InlineData("png:2", null)]
        public void Action_OnlyTheFixedSet(string payload, string expected) { Assert.Equal(expected, GradeReport.Action(payload)); }
        // «Копировать» beside one page of a long summary: that page's number, 0 for the whole sheet or anything else.
        [Theory]
        [InlineData("copy", 0)]
        [InlineData("copy:3", 3)]
        [InlineData("copy:12", 12)]
        [InlineData("copy:13", 0)]
        [InlineData("copy:003", 0)]
        [InlineData("png", 0)]
        [InlineData(null, 0)]
        public void Page_OnlyAPageNumberInRange(string payload, int expected) { Assert.Equal(expected, GradeReport.Page(payload)); }

        [Theory]
        [InlineData("ru", "pdf", 0, 0, "Успеваемость 2026-11-18.pdf")]
        [InlineData("kz", "png", 0, 0, "Үлгерім 2026-11-18.png")]
        [InlineData("en", "pdf", 0, 0, "Grades 2026-11-18.pdf")]
        [InlineData("xx", "png", 0, 0, "Успеваемость 2026-11-18.png")]
        [InlineData("ru", "png", 2, 0, "Успеваемость 2026-11-18 стр. 2.png")]
        [InlineData("kz", "png", 2, 0, "Үлгерім 2026-11-18 2-бет.png")]
        [InlineData("en", "png", 2, 0, "Grades 2026-11-18 page 2.png")]
        [InlineData("ru", "png", 1, 3, "Успеваемость 2026-11-18 (3) стр. 1.png")]
        public void FileName_PageWordInTheAppLanguageAndTheDay(string language, string extension, int page, int set, string expected)
        { Assert.Equal(expected, GradeReport.FileName(language, new DateTime(2026, 11, 18, 23, 59, 0), extension, page, set)); }

        // The picture keeps one size and one content at any zoom and screen scale: twice the sheet's CSS size. DevTools takes
        // the clip in zoomed pixels, so the scale also undoes the zoom (18 Nov 2026: at 110% the sheet was cut at the right).
        [Theory]
        [InlineData(1, 1, 2)]
        [InlineData(2, 2, 1)]
        [InlineData(1.1, 1.1, 1.8181818182)]
        [InlineData(0.9, 0.9, 2.2222222222)]
        [InlineData(1.5, 1, 1.3333333333)]
        [InlineData(3, 2, 0.6666666667)]
        [InlineData(1.375, 1.1, 1.4545454545)]
        [InlineData(0, 1, 2)]
        [InlineData(double.NaN, 1, 2)]
        [InlineData(1, 0, 2)]
        [InlineData(100, 1, 2)]
        [InlineData(5, 1, 0.4)]
        public void CaptureScale_TwiceTheCssSizeAtAnyZoom(double ratio, double zoom, double expected) { Assert.Equal(expected, GradeReport.CaptureScale(ratio, zoom), 6); }
        [Theory]
        [InlineData(1.1, 1.1)]
        [InlineData(0.5, 0.5)]
        [InlineData(2, 2)]
        [InlineData(0, 1)]
        [InlineData(9, 1)]
        [InlineData(double.NaN, 1)]
        public void ClipZoom_TheAppsOwnZoomWithinItsRange(double zoom, double expected) { Assert.Equal(expected, GradeReport.ClipZoom(zoom), 6); }
        // A picture that came back smaller than the sheet (cut by the renderer) is not saved as the summary.
        [Theory]
        [InlineData(1520, 4448, 760, 2224, true)]
        [InlineData(1520, 4450, 760, 2224.7, true)]
        [InlineData(1520, 4000, 760, 2224, false)]
        [InlineData(1400, 4448, 760, 2224, false)]
        // A very tall page keeps the same few pixels of rounding: 40 px short of 16000 is more than rounding, the picture was cut.
        [InlineData(1520, 15960, 760, 8000, false)]
        [InlineData(1520, 15990, 760, 8000, true)]
        public void Whole_OnlyAPictureOfTheWholeSheet(int width, int height, double cssWidth, double cssHeight, bool expected)
        { Assert.Equal(expected, GradeReport.Whole(width, height, cssWidth, cssHeight)); }
        // A sheet the page reports is taken only within a picture budget: a page script could otherwise ask for a huge bitmap.
        [Theory]
        [InlineData(760, 730, 2, true)]
        [InlineData(760, 7900, 2, true)]
        [InlineData(760, 9000, 2, false)]
        [InlineData(2000, 4000, 4, false)]
        [InlineData(2000, 8000, 1.9, false)]
        [InlineData(99, 730, 2, false)]
        [InlineData(760, 99, 2, false)]
        [InlineData(760, 730, double.NaN, false)]
        public void Fits_OnlyAPictureOfSensibleSize(double width, double height, double scale, bool expected) { Assert.Equal(expected, GradeReport.Fits(width, height, scale)); }
        // DevTools draws at the capture scale times the screen's real scale: a page that reports a smaller ratio than the
        // screen has gets a larger capture scale, and the budget counts what is really drawn.
        // Pixels per CSS pixel: the capture scale times the screen's real scale times the zoom of the clip.
        [Theory]
        [InlineData(2, 1, 1, 2)]
        [InlineData(1, 2, 1, 2)]
        [InlineData(4, 2, 1, 8)]
        [InlineData(4.0 / 3, 1, 1, 2)]
        [InlineData(2, double.NaN, 1, 2)]
        [InlineData(2, 1, 2, 4)]
        [InlineData(1.8181818182, 1, 1.1, 2)]
        public void DrawnScale_CountsTheRealScreen(double scale, double screen, double zoom, double expected) { Assert.Equal(expected, GradeReport.DrawnScale(scale, screen, zoom), 6); }
        [Fact]
        public void Fits_RefusesAPageThatUnderstatesItsRatio()
        {
            // On a 200% screen a page reporting ratio 0.5 × zoom gets capture scale 4: 1000 × 1560 CSS px would be 8000 × 12480.
            Assert.False(GradeReport.Fits(1000, 1560, GradeReport.DrawnScale(GradeReport.CaptureScale(0.5, 1), 2, 1)));
            Assert.True(GradeReport.Fits(760, 1560, GradeReport.DrawnScale(GradeReport.CaptureScale(2, 1), 2, 1)));
            // At 200% zoom a page reporting ratio 1 instead of 2 gets twice the scale, and the budget sees it.
            Assert.False(GradeReport.Fits(760, 7900, GradeReport.DrawnScale(GradeReport.CaptureScale(1, 2), 1, 2)));
            Assert.True(GradeReport.Fits(760, 7900, GradeReport.DrawnScale(GradeReport.CaptureScale(2, 2), 1, 2)));
            // Windows goes up to 500%: an honest long sheet still fits there.
            Assert.True(GradeReport.Fits(760, 7900, GradeReport.DrawnScale(GradeReport.CaptureScale(5, 1), 5, 1)));
        }
        [Fact]
        public void PictureWidth_IsTwiceTheSheet() { Assert.Equal(1520, GradeReport.PictureWidth(760)); Assert.Equal(1521, GradeReport.PictureWidth(760.4)); }
    }
}
