using System;
using System.Globalization;

namespace Univer.Desktop.Core
{
    // «Сводка успеваемости»: the sheet grade-report.js builds over the grades journal. The page says only which of a fixed set
    // of actions the student pressed; the app makes the file, names it and puts it in Downloads.
    internal static class GradeReport
    {
        public const string MessagePrefix = "univer-report:";
        public const string Print = "print", Pdf = "pdf", Png = "png", Copy = "copy", Reveal = "reveal";
        // A picture is twice the sheet's CSS size, whatever the zoom: 760 CSS px give a 1520 px wide PNG.
        public const double PictureScale = 2;
        // A4 in inches, and 12 mm margins, as the sheet's own @page rule says.
        public const double PaperWidth = 8.27, PaperHeight = 11.69, Margin = 0.47;

        // A long summary is cut into pictures between courses (grade-report.js); a page asking for more is refused.
        public const int MaxPages = 12;
        private const string CopyPage = Copy + ":";

        // The action of a message, or null for anything else. "copy:N" copies page N alone.
        public static string Action(string payload)
        {
            switch (payload)
            {
                case Print: case Pdf: case Png: case Copy: case Reveal: return payload;
                default: return Page(payload) > 0 ? Copy : null;
            }
        }
        // The page of "copy:N" (1 … MaxPages, plain digits without a leading zero); 0 for anything else.
        public static int Page(string payload)
        {
            if (payload == null || !payload.StartsWith(CopyPage, StringComparison.Ordinal)) return 0;
            var digits = payload.Substring(CopyPage.Length);
            if (digits.Length == 0 || digits.Length > 2 || digits[0] == '0') return 0;
            var page = 0;
            foreach (var c in digits) { if (c < '0' || c > '9') return 0; page = page * 10 + (c - '0'); }
            return page <= MaxPages ? page : 0;
        }
        // "Успеваемость 2026-11-18.pdf": the page's own word in the app's language and the day it was made; a picture of one of
        // several pages adds its number, and a set that would meet an earlier one takes one number for all its pages.
        public static string FileName(string language, DateTime made, string extension, int page = 0, int set = 0)
        {
            var word = language == "kz" ? "Үлгерім" : language == "en" ? "Grades" : "Успеваемость";
            var name = word + " " + made.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (set > 0) name += " (" + set.ToString(CultureInfo.InvariantCulture) + ")";
            if (page > 0)
            {
                var number = page.ToString(CultureInfo.InvariantCulture);
                name += language == "kz" ? " " + number + "-бет" : language == "en" ? " page " + number : " стр. " + number;
            }
            return FileNames.Safe(name + "." + extension);
        }
        // The app's own page zoom; DevTools takes a clip in the page's zoomed pixels (measured 18 Nov 2026: at 110% a clip of the
        // sheet's CSS box drew the sheet 1.1 times larger and cut its right edge and bottom, at 90% it took in the page beside it).
        public static double ClipZoom(double zoom)
        { return double.IsNaN(zoom) || zoom < 0.25 || zoom > 5 ? 1 : zoom; }
        // DevTools draws each pixel of the clip at its scale times the screen's. The screen's scale is the page's device pixel
        // ratio without the zoom (a ratio the page could not give counts as 1), and the scale undoes both, so a CSS pixel of the
        // sheet becomes two pixels of the picture at any zoom and DPI.
        public static double CaptureScale(double ratio, double zoom)
        {
            var clip = ClipZoom(zoom); var screen = ratio / clip;
            if (double.IsNaN(screen) || double.IsInfinity(screen) || screen < 0.5 || screen > 5) screen = 1;
            return PictureScale / (screen * clip);
        }
        // Pixels DevTools draws per CSS pixel of the sheet: the capture scale times the screen's own scale (the window's real
        // DPI; a page reporting a smaller ratio would otherwise get a larger scale) times the zoom, and never less than the picture's.
        public static double DrawnScale(double scale, double screen, double zoom)
        {
            if (double.IsNaN(screen) || double.IsInfinity(screen) || screen <= 0) screen = 1;
            return Math.Max(PictureScale, scale * screen * ClipZoom(zoom));
        }
        // A picture of the whole sheet: its width already fitted, its height twice the sheet's within rounding (at most 24 px, less
        // than the sheet's bottom padding, however tall the page). A shorter one was cut by the renderer and is not saved.
        public static bool Whole(int width, int height, double cssWidth, double cssHeight)
        {
            var tall = cssHeight * PictureScale;
            return Math.Abs(width - PictureWidth(cssWidth)) <= 2 && Math.Abs(height - tall) <= Math.Min(24, Math.Max(3, tall * 0.005));
        }
        // The sheet the page reports, in CSS pixels, and the pixels per CSS pixel it would be drawn at (DrawnScale): a sheet too
        // small, too long or a picture too large is refused, so a page script cannot make the app draw a bitmap of hundreds of
        // megabytes.
        public static bool Fits(double width, double height, double scale)
        {
            if (double.IsNaN(width) || double.IsNaN(height) || double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) return false;
            // 25 million pixels: a decoded picture stays under about 100 MB (FitPicture, the clipboard copy).
            return width >= 100 && height >= 100 && width <= 2000 && height <= 8000 && width * scale <= 4000 && height * scale <= 16000 && width * scale * height * scale <= 25e6;
        }
        // The width a picture of a sheet must have; one off by more than a pixel or two is scaled to it.
        public static int PictureWidth(double cssWidth) { return (int)Math.Round(cssWidth * PictureScale); }
    }
}
