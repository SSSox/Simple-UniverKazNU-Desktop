using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // While a page loads, a softly pulsing empty copy of the page layout shows the destination:
    // the header, then placeholder tiles, tables or text that match where the student is going.
    internal sealed class LoadingView : Panel
    {
        private enum Shape { Home, Bachelor, Schedule, Grades, Courses, Files, Table, Document, Page }
        // The site's header in CSS pixels: where the tray of tabs starts after the logo, and how wide it is.
        private const int LogoBox = 128, TrayWidth = 271;
        private readonly Timer animation = new Timer { Interval = 500 };
        private readonly Stopwatch clock = new Stopwatch();
        private Shape shape = Shape.Page;
        private string slowText = "";
        private float zoom = 1;
        public float Zoom { get { return zoom; } set { if (Math.Abs(zoom - value) < .001f) return; zoom = value; Invalidate(); } }
        private static Color Bone { get { return Theme.Current.Skeleton; } }
        private static Color Card { get { return Theme.Surface; } }
        private static Color Edge { get { return Theme.Border; } }
        public LoadingView()
        {
            // No AllPaintingInWmPaint: the background is erased with the page colour the moment the view is shown.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Dock = DockStyle.Fill; BackColor = Theme.Background;
            Font = Theme.Font(Theme.Meta); ForeColor = Theme.Muted; AccessibleRole = AccessibleRole.Animation;
            animation.Tick += (s, e) =>
            {
                if (Motion.Enabled && !Motion.Reduced) Invalidate();
                else if (clock.ElapsedMilliseconds > 3000) { Invalidate(); animation.Stop(); }
            };
            VisibleChanged += (s, e) =>
            {
                // Reduced motion keeps the loading view static; full motion pulses gently, one breath in 3.6 s, so 15 fps is smooth.
                // A newly shown child window is black until it paints, so paint it before Windows composes the next frame.
                if (Visible) { clock.Restart(); animation.Interval = Motion.Enabled && !Motion.Reduced ? 66 : 500; animation.Start(); if (IsHandleCreated) Refresh(); } else { animation.Stop(); clock.Stop(); }
            };
        }
        public void SetLanguage(string language) { Text = AccessibleName = Strings.Get("loading", language); slowText = Strings.Get("loadingProgress", language); Invalidate(); }
        // The destination picks the placeholder layout; unknown pages get a generic heading with text blocks.
        public void SetRoute(string url)
        {
            var path = "";
            try { path = url == "document" ? "document" : new Uri(url).AbsolutePath.ToLowerInvariant(); } catch (Exception) { }
            Func<string, bool> under = prefix => path.StartsWith(prefix, StringComparison.Ordinal);
            shape = path == "document" ? Shape.Document
                : under("/student/myschedule") ? Shape.Schedule
                : under("/student/attendance") ? Shape.Grades
                : under("/student/bachelor") ? Shape.Bachelor
                : under("/news/student") || path == "/" || under("/ssc") && path.Length <= 5 ? Shape.Home
                : System.Text.RegularExpressions.Regex.IsMatch(path, @"^/student/umkd(/0/\d+/\d+)?/?$") ? Shape.Courses
                : under("/student/umkd/") ? Shape.Files
                : under("/student/") ? Shape.Table
                : Shape.Page;
            Invalidate();
        }
        // A shape of the page: a card or a control with an outline, drawn under the placeholder bars.
        private struct Block { public RectangleF Area; public float Radius; public Color Fill, Line; public bool Dashed; }
        // The page's shapes change with the window, the route, the zoom and the theme, not with the pulse: they are drawn once
        // into a picture, and a frame only lays the pulsing bars over it.
        private Bitmap picture;
        private GraphicsPath bars;
        private Palette drawnPalette;
        private Shape drawnShape;
        private float drawnZoom;
        private Core.PageShape drawnCapture, captured;
        // The page's real layout from its last visit; used while it fits the window, otherwise the drawn shape stands in.
        public Core.PageShape Captured { get { return captured; } set { if (captured == value) return; captured = value; Invalidate(); } }
        private bool Drawn { get { return picture != null && picture.Width == Width && picture.Height == Height && drawnPalette == Theme.Current && drawnShape == shape && drawnZoom == zoom && drawnCapture == captured; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 1 || Height < 1) return;
            var g = e.Graphics; var s = Theme.DpiScale * zoom; var ms = clock.ElapsedMilliseconds;
            if (!Drawn)
            {
                picture?.Dispose(); bars?.Dispose();
                picture = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb); picture.SetResolution(g.DpiX, g.DpiY);
                bars = new GraphicsPath(); drawnPalette = Theme.Current; drawnShape = shape; drawnZoom = zoom; drawnCapture = captured;
                using (var canvas = Graphics.FromImage(picture)) Compose(canvas, s);
            }
            g.DrawImageUnscaled(picture, 0, 0); g.SmoothingMode = SmoothingMode.AntiAlias;
            var pulse = Motion.Enabled && !Motion.Reduced ? .90f + .10f * (float)(.5 + .5 * Math.Cos(ms / 3600.0 * 2 * Math.PI)) : 1f;
            using (var fill = new SolidBrush(Color.FromArgb((int)(255 * pulse), Bone))) g.FillPath(fill, bars);
            // A slow site gets a quiet word of reassurance instead of an apparently frozen window.
            if (ms > 3000 && slowText.Length > 0)
            {
                var size = TextRenderer.MeasureText(slowText, Font);
                var pill = new RectangleF((Width - size.Width) / 2f - 16 * s, Height - 56 * s, size.Width + 32 * s, size.Height + 8 * s);
                using (var path = Theme.Rounded(pill, 8 * s)) using (var fill = new SolidBrush(Card)) using (var pen = new Pen(Edge)) { g.FillPath(fill, path); g.DrawPath(pen, path); }
                TextRenderer.DrawText(g, slowText, Font, Rectangle.Round(pill), ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
        // The page below follows the app's zoom, and so do these measures: they are the site's own, in CSS pixels.
        private void Compose(Graphics g, float s)
        {
            g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            Func<float, float> d = v => v * s;
            var bones = new List<Block>(); var blocks = new List<Block>();
            var palette = Theme.Current; var outline = palette.Control;
            Action<float, float, float, float, float> bar = (x, y0, w, h, r) => bones.Add(new Block { Area = new RectangleF(x, y0, w, h), Radius = r });
            Action<RectangleF, float, Color, Color> block = (area, r, fill, line) => blocks.Add(new Block { Area = area, Radius = r, Fill = fill, Line = line });
            Func<float, float, float, float, RectangleF> box = (x, y0, w, h) => new RectangleF(x, y0, w, h);
            var control = d(40); var round = control / 2;
            var narrow = Width < d(900); var gutter = narrow ? d(16) : d(24);
            var pageWidth = Math.Min(Width - 2 * gutter, d(1400)); var left = (Width - pageWidth) / 2; var right = left + pageWidth;
            var document = shape == Shape.Document;
            if (!document && captured != null && captured.Fits(Width / s)) { ComposeCaptured(g, s, bones, blocks); Paint(g, bones, blocks); return; }
            var headerBottom = document ? 0 : d(56);
            if (!document)
            {
                // The site's header: its logo from 1040 px up, the two catalogues in one tray, then All sections, the
                // language flags and the account. The logo's box is kept so nothing shifts when the page arrives.
                using (var white = new SolidBrush(Card)) g.FillRectangle(white, 0, 0, Width, headerBottom);
                using (var line = new Pen(Edge)) g.DrawLine(line, 0, headerBottom, Width, headerBottom);
                var top = d(8); var tray = left;
                if (Width >= d(1040)) { bar(left + d(8), d(20), d(66), d(16), d(4)); tray = left + d(LogoBox); }
                block(box(tray, d(6), d(TrayWidth), d(44)), d(14), palette.Track, palette.Line);
                bar(tray + d(4), d(10), d(126), d(36), d(10)); bar(tray + d(156), d(22), d(90), d(12), d(4));
                var x = right - d(124);
                block(box(x, top, d(124), control), d(12), Card, outline); bar(x + d(18), top + d(14), d(88), d(12), d(4));
                if (!narrow)
                {
                    x -= d(10) + d(126); block(box(x, top, d(126), control), d(12), palette.Track, palette.Line);
                    x -= d(10) + d(134); block(box(x, top, d(134), control), d(12), Card, outline); bar(x + d(18), top + d(14), d(98), d(12), d(4));
                }
            }
            // The home tiles, four to a row on a wide window, with the dashed new-section slot after the eight; returns the y below them.
            Func<float, float> Tiles = top =>
            {
                var tileGap = d(12); var tileColumns = Math.Max(1, (int)((pageWidth + tileGap) / (d(220) + tileGap))); var tile = (pageWidth - (tileColumns - 1) * tileGap) / tileColumns;
                for (var i = 0; i < 8; i++)
                {
                    var r = box(left + i % tileColumns * (tile + tileGap), top + i / tileColumns * (d(60) + tileGap), tile, d(60)); block(r, d(12), Card, outline);
                    bar(r.Left + d(14), r.Top + d(14), d(32), d(32), d(8)); bar(r.Left + d(58), r.Top + d(24), Math.Min(d(100), tile * .42f), d(12), d(4));
                }
                var slotHeight = 8 % tileColumns == 0 ? d(52) : d(60);
                blocks.Add(new Block { Area = box(left + 8 % tileColumns * (tile + tileGap), top + 8 / tileColumns * (d(60) + tileGap), tile, slotHeight), Radius = d(12), Fill = BackColor, Line = outline, Dashed = true });
                return top + 8 / tileColumns * (d(60) + tileGap) + slotHeight + d(24);
            };
            var y = headerBottom + d(24);
            switch (shape)
            {
                case Shape.Home:
                    y = Tiles(y);
                    // The news are one reading column about a sheet wide, centred, its heading included; the tiles above run the page's full width.
                    var column = Math.Min(pageWidth, d(900)); var columnLeft = left + (pageWidth - column) / 2;
                    bar(columnLeft, y + d(12), d(104), d(22), d(5)); y += d(48);
                    // The news filter: search, then two quiet buttons.
                    var button = d(132); var period = d(104); var field = column - button - period - d(16);
                    block(box(columnLeft, y, field, control), d(10), Card, outline); bar(columnLeft + d(40), y + d(14), field * .3f, d(12), d(4));
                    block(box(columnLeft + field + d(8), y, period, control), d(10), Card, outline); bar(columnLeft + field + d(24), y + d(14), period - d(44), d(12), d(4));
                    block(box(columnLeft + column - button, y, button, control), d(10), Card, outline); bar(columnLeft + column - button + d(18), y + d(14), button - d(36), d(12), d(4));
                    y += control + d(14);
                    var newsHeights = new[] { 390, 160, 300 };
                    for (var n = 0; n < newsHeights.Length && y < Height; n++)
                    {
                        // A date chip, a rule, then the title and the text; a long one ends with its Show in full button.
                        var news = box(columnLeft, y, column, d(newsHeights[n])); block(news, d(14), Card, Edge);
                        bar(news.Left + d(24), news.Top + d(22), d(84), d(28), d(8));
                        block(box(news.Left + d(24), news.Top + d(60), news.Width - d(48), 1), 0, Edge, Edge);
                        var lines = newsHeights[n] > 200 ? 5 : 1; var textTop = news.Top + d(newsHeights[n] > 200 ? 130 : 96);
                        if (newsHeights[n] > 200) bar(news.Left + d(24), news.Top + d(92), news.Width * .42f, d(18), d(4));
                        for (var l = 0; l < lines; l++) bar(news.Left + d(24), textTop + l * d(26), (news.Width - d(48)) * (l == lines - 1 ? .55f : .96f - l % 2 * .06f), d(11), d(4));
                        if (newsHeights[n] > 300) bar(news.Left + d(24), news.Bottom - d(36), d(128), d(14), d(4));
                        y += d(newsHeights[n]) + d(16);
                    }
                    break;
                case Shape.Bachelor:
                    // The home page's blocks in their order: the tiles, the two GPA cards, then today's classes and the grades side by side (stacked when narrow).
                    y = Tiles(y);
                    for (var i = 0; i < 2; i++)
                    {
                        var gpa = box(left + i * d(252), y, d(240), d(84)); block(gpa, d(12), Card, Edge);
                        bar(gpa.Left + d(20), gpa.Top + d(16), d(64), d(26), d(5)); bar(gpa.Left + d(20), gpa.Top + d(54), d(120), d(12), d(4));
                    }
                    y += d(84) + d(20);
                    var panelGap = d(16); var panelColumns = Math.Min(2, Math.Max(1, (int)((pageWidth + panelGap) / (d(340) + panelGap)))); var panelWidth = (pageWidth - (panelColumns - 1) * panelGap) / panelColumns;
                    var panelHeights = new[] { 260f, 448f };
                    for (var i = 0; i < 2; i++)
                    {
                        var panelTop = panelColumns == 1 ? y + (i == 0 ? 0 : d(panelHeights[0]) + panelGap) : y;
                        var panelBox = box(left + i % panelColumns * (panelWidth + panelGap), panelTop, panelWidth, d(panelHeights[i])); if (panelBox.Top > Height) break;
                        block(panelBox, d(12), Card, Edge); block(box(panelBox.Left + 1, panelBox.Top + d(56), panelBox.Width - 2, 1), 0, Edge, Edge);
                        bar(panelBox.Left + d(20), panelBox.Top + d(20), d(120), d(16), d(4)); bar(panelBox.Right - d(20) - d(96), panelBox.Top + d(22), d(96), d(12), d(4));
                        if (i == 0)
                        {
                            bar(panelBox.Left + d(20), panelBox.Top + d(76), d(170), d(12), d(4));
                            for (var n = 0; n < 2; n++)
                            {
                                var rowTop = panelBox.Top + d(108) + n * d(72);
                                bar(panelBox.Left + d(20), rowTop + d(6), d(44), d(14), d(4)); bar(panelBox.Left + d(20), rowTop + d(28), d(44), d(11), d(4));
                                bar(panelBox.Left + d(84), rowTop + d(4), Math.Min(d(240), panelBox.Width * .55f), d(14), d(4)); bar(panelBox.Left + d(84), rowTop + d(28), Math.Min(d(160), panelBox.Width * .36f), d(11), d(4));
                            }
                        }
                        else
                            for (var n = 0; n < 8; n++)
                            {
                                var rowTop = panelBox.Top + d(66) + n * d(47);
                                bar(panelBox.Left + d(20), rowTop + d(14), Math.Min(d(240) - n % 3 * d(36), panelBox.Width * .5f), d(13), d(4));
                                for (var chip = 0; chip < 2; chip++) block(box(panelBox.Right - d(20) - d(68) * (chip + 1) - d(8) * chip, rowTop + d(6), d(68), d(34)), d(8), Card, Edge);
                                if (n < 7) block(box(panelBox.Left + d(20), rowTop + d(46), panelBox.Width - d(40), 1), 0, Edge, Edge);
                            }
                    }
                    break;
                case Shape.Schedule:
                    left += d(10); right -= d(10); pageWidth -= d(20);
                    bar(left, y, d(250), d(20), d(5)); y += d(36);
                    for (var i = 0; i < 2; i++) { block(box(left + i * d(180), y, d(172), control), d(10), Card, outline); bar(left + i * d(180) + d(18), y + d(14), d(136), d(12), d(4)); }
                    y += control + d(12);
                    // The week row: a step back, the week list and a step forward, then the site's caption of the grid.
                    var weekList = Math.Min(d(420), pageWidth - 2 * (control + d(8)));
                    block(box(left, y, control, control), d(10), Card, outline); bar(left + d(15), y + d(14), d(10), d(12), d(3));
                    block(box(left + control + d(8), y, weekList, control), d(10), Card, outline); bar(left + control + d(24), y + d(14), Math.Min(d(190), weekList - d(48)), d(12), d(4));
                    var forward = left + control + d(8) + weekList + d(8); block(box(forward, y, control, control), d(10), Card, outline); bar(forward + d(15), y + d(14), d(10), d(12), d(3));
                    y += control + d(14);
                    bar(left + d(8), y, d(270), d(16), d(4)); y += d(40);
                    block(box(left, y, d(196), control + d(4)), d(10), palette.Track, palette.Track); block(box(left + d(2), y + d(2), d(90), control), d(8), Card, palette.Line); y += control + d(20);
                    // The card for the class under way or the next one, then the syllabus panel: title, switch and refresh, a rule, the week's СРО.
                    var now = box(left, y, pageWidth, d(150)); block(now, d(12), Card, palette.Line);
                    bar(now.Left + d(16), now.Top + d(16), d(110), d(12), d(4)); bar(now.Right - d(16) - d(96), now.Top + d(12), d(96), d(22), d(5));
                    bar(now.Left + d(16), now.Top + d(48), Math.Min(d(320), now.Width * .5f), d(16), d(4)); bar(now.Left + d(16), now.Top + d(74), Math.Min(d(220), now.Width * .34f), d(12), d(4));
                    block(box(now.Left + d(16), now.Top + d(100), now.Width - d(32), d(6)), d(3), palette.Track, palette.Track); bar(now.Left + d(16), now.Top + d(122), d(180), d(12), d(4));
                    y += d(150) + d(16);
                    var syllabus = box(left, y, pageWidth, d(120)); block(syllabus, d(12), Card, Edge);
                    bar(syllabus.Left + d(16), syllabus.Top + d(26), d(170), d(16), d(4)); bar(syllabus.Right - d(16) - d(60), syllabus.Top + d(28), d(60), d(12), d(4));
                    block(box(syllabus.Right - d(16) - d(60) - d(20) - d(190), syllabus.Top + d(24), d(34), d(20)), d(10), palette.Track, palette.Line); bar(syllabus.Right - d(16) - d(60) - d(20) - d(144), syllabus.Top + d(28), d(140), d(12), d(4));
                    block(box(syllabus.Left + 1, syllabus.Top + d(68), syllabus.Width - 2, 1), 0, Edge, Edge); bar(syllabus.Left + d(16), syllabus.Top + d(86), d(150), d(14), d(4)); bar(syllabus.Right - d(30), syllabus.Top + d(90), d(14), d(8), d(3));
                    y += d(120) + d(24);
                    var chipX = left; var chips = new[] { 84, 134, 112, 96, 108, 114, 104 };
                    for (var i = 0; i < chips.Length && chipX + d(chips[i]) < right; i++)
                    {
                        if (i == 0) bar(chipX, y, d(chips[i]), control, round); else block(box(chipX, y, d(chips[i]), control), round, Card, outline);
                        chipX += d(chips[i]) + d(8);
                    }
                    y += control + d(14);
                    var dayGap = d(12); var dayColumns = Math.Max(1, (int)((pageWidth + dayGap) / (d(240) + dayGap))); var day = (pageWidth - (dayColumns - 1) * dayGap) / dayColumns;
                    for (var i = 0; i < 6; i++)
                    {
                        var row = i / dayColumns; var top = y + row * (d(204) + dayGap); if (top > Height) break;
                        var r = box(left + i % dayColumns * (day + dayGap), top, day, d(204)); block(r, d(12), Card, Edge);
                        block(box(r.Left + 1, r.Top + d(41), r.Width - 2, 1), 0, Edge, Edge); bar(r.Left + d(14), r.Top + d(14), day * .5f, d(14), d(4));
                        bar(r.Left + d(14), r.Top + d(56), d(84), d(24), d(6)); bar(r.Left + d(14), r.Top + d(92), day * .72f, d(14), d(4)); bar(r.Left + d(14), r.Top + d(116), day * .5f, d(12), d(4));
                        block(box(r.Left + d(14), r.Top + d(146), d(100), control), d(10), Card, outline);
                    }
                    break;
                case Shape.Grades:
                    left += d(10); right -= d(10); pageWidth -= d(20);
                    bar(left + d(8), y, d(290), d(20), d(5)); y += d(38);
                    bar(left, y, d(74), d(12), d(4)); y += d(22);
                    // The subject buttons wrap onto as many rows as the window needs.
                    var courseX = left; var courses = new[] { 58, 112, 104, 128, 162, 246, 330, 150 };
                    for (var i = 0; i < courses.Length; i++)
                    {
                        var courseWidth = d(courses[i]); if (courseX > left && courseX + courseWidth > right) { courseX = left; y += control + d(6); }
                        if (i == 0) bar(courseX, y, courseWidth, control, d(8)); else { block(box(courseX, y, courseWidth, control), d(8), Card, outline); bar(courseX + d(16), y + d(14), courseWidth - d(32), d(12), d(4)); }
                        courseX += courseWidth + d(8);
                    }
                    y += control + d(14); block(box(left, y, pageWidth, 1), 0, Edge, Edge); y += d(14);
                    bar(left, y, d(86), d(12), d(4)); y += d(22);
                    block(box(left, y, Math.Min(d(380), pageWidth), control + d(4)), d(10), palette.Track, palette.Track); block(box(left + d(2), y + d(2), d(108), control), d(8), Card, palette.Line); y += control + d(20);
                    // The summary card lists every subject with its two scores; the subjects' own cards follow in two columns.
                    var summaryHeight = d(54 + 8 * 47 + 10); var grades = box(left, y, pageWidth, summaryHeight); block(grades, d(12), Card, Edge);
                    block(box(grades.Left + 1, grades.Top + d(54), grades.Width - 2, 1), 0, Edge, Edge); bar(grades.Left + d(20), grades.Top + d(20), d(160), d(14), d(4));
                    for (var n = 0; n < 8; n++)
                    {
                        var rowTop = grades.Top + d(60) + n * d(47);
                        bar(grades.Left + d(20), rowTop + d(15), Math.Min(grades.Width * .34f, d(260) - n % 3 * d(40)), d(12), d(4));
                        block(box(grades.Right - d(20) - d(68), rowTop + d(7), d(68), d(34)), d(8), Card, Edge); block(box(grades.Right - d(20) - d(144), rowTop + d(7), d(68), d(34)), d(8), Card, Edge);
                    }
                    y += summaryHeight + d(16);
                    var subjectColumns = pageWidth >= d(700) ? 2 : 1; var subjectWidth = (pageWidth - (subjectColumns - 1) * d(16)) / subjectColumns;
                    for (var i = 0; y + i / subjectColumns * d(256) < Height; i++)
                    {
                        var subject = box(left + i % subjectColumns * (subjectWidth + d(16)), y + i / subjectColumns * d(256), subjectWidth, d(240)); block(subject, d(12), Card, Edge);
                        bar(subject.Left + d(20), subject.Top + d(20), subjectWidth * .4f, d(16), d(4)); block(box(subject.Left + 1, subject.Top + d(54), subject.Width - 2, 1), 0, Edge, Edge);
                        for (var n = 0; n < 4; n++) bar(subject.Left + d(20), subject.Top + d(76) + n * d(36), (subject.Width - d(40)) * (n % 2 == 0 ? .8f : .55f), d(12), d(4));
                    }
                    break;
                case Shape.Courses:
                    bar(left, y, d(70), d(20), d(5)); y += d(36);
                    block(box(left, y, d(212), control), d(10), Card, outline); block(box(right - d(212), y, d(212), control), d(10), Card, outline); y += control + d(26);
                    bar(left, y, d(130), d(16), d(4)); y += d(32);
                    block(box(left, y, Math.Min(d(420), pageWidth), control), d(10), Card, outline); bar(left + d(40), y + d(14), d(130), d(12), d(4)); y += control + d(8);
                    var cardGap = d(10); var cardColumns = Math.Max(1, (int)((pageWidth + cardGap) / (d(300) + cardGap))); var cardWidth = (pageWidth - (cardColumns - 1) * cardGap) / cardColumns;
                    for (var i = 0; i < 9; i++)
                    {
                        var r = box(left + i % cardColumns * (cardWidth + cardGap), y + i / cardColumns * (d(68) + cardGap), cardWidth, d(68)); if (r.Top > Height) break;
                        block(r, d(12), Card, outline); bar(r.Left + d(14), r.Top + d(14), d(40), d(40), d(10)); bar(r.Left + d(66), r.Top + d(18), Math.Min(d(170), cardWidth * .55f), d(13), d(4)); bar(r.Left + d(66), r.Top + d(40), d(84), d(10), d(4));
                    }
                    break;
                case Shape.Files:
                    bar(left, y, d(240), d(20), d(5)); y += d(36);
                    bar(left, y, d(220), d(15), d(4)); y += d(28);
                    for (var i = 0; i < 3; i++) { block(box(left + d(64) + i * d(136), y, d(128), d(34)), d(10), Card, outline); }
                    bar(left, y + d(10), d(54), d(13), d(4)); y += d(46);
                    var files = box(left, y, pageWidth, Height); block(files, d(12), Card, Edge);
                    for (var n = 0; y < Height; n++)
                    {
                        if (n % 4 == 0) { block(box(files.Left + 1, y, files.Width - 2, d(38)), 0, palette.Track, Edge); bar(files.Left + d(16), y + d(13), Math.Min(d(340), files.Width * .4f), d(12), d(4)); y += d(38); }
                        bar(files.Left + d(16), y + d(9), d(34), d(34), d(8)); bar(files.Left + d(62), y + d(20), Math.Min(d(300) - n % 3 * d(40), files.Width * .4f), d(12), d(4));
                        bar(files.Right - d(166), y + d(20), d(60), d(11), d(4)); bar(files.Right - d(84), y + d(20), d(68), d(11), d(4));
                        block(box(files.Left + 1, y + d(52), files.Width - 2, 1), 0, Edge, Edge); y += d(52);
                    }
                    break;
                case Shape.Document:
                    var sheetWidth = Math.Min(Width - 2 * gutter, d(1040));
                    var sheet = box((Width - sheetWidth) / 2, d(24), sheetWidth, Height); block(sheet, d(10), Card, Edge); y = sheet.Top + d(56);
                    bar(sheet.Left + d(48), y, sheet.Width * .5f, d(22), d(5)); y += d(56);
                    for (var l = 0; y < Height; l++) { bar(sheet.Left + d(48), y, (sheet.Width - d(96)) * (l % 5 == 4 ? .6f : .96f - (l % 3) * .05f), d(11), d(4)); y += l % 5 == 4 ? d(42) : d(24); }
                    break;
                default:
                    bar(left, y, d(280), d(20), d(5)); y += d(40);
                    var table = box(left, y, pageWidth, Height); block(table, d(12), Card, Edge);
                    var cells = narrow ? 2 : shape == Shape.Table ? 5 : 3; var cell = (table.Width - d(40)) / cells;
                    for (var c = 0; c < cells; c++) bar(table.Left + d(20) + c * cell, table.Top + d(20), cell * (c == 0 ? .6f : .4f), d(12), d(4));
                    y = table.Top + d(52);
                    for (var n = 0; y < Height; n++)
                    {
                        for (var c = 0; c < cells; c++) bar(table.Left + d(20) + c * cell, y + d(18), cell * (c == 0 ? .75f - n % 3 * .12f : .32f), d(11), d(4));
                        y += d(48);
                    }
                    break;
            }
            Paint(g, bones, blocks);
        }
        private void Paint(Graphics g, List<Block> bones, List<Block> blocks)
        {
            foreach (var item in blocks)
                using (var path = Theme.Rounded(item.Area, item.Radius)) using (var fill = new SolidBrush(item.Fill)) using (var pen = new Pen(item.Line) { DashStyle = item.Dashed ? DashStyle.Dash : DashStyle.Solid })
                { if (item.Fill.A > 0) g.FillPath(fill, path); if (item.Line.A > 0) g.DrawPath(pen, path); }
            foreach (var item in bones) using (var path = Theme.Rounded(item.Area, Math.Min(item.Radius, Math.Min(item.Area.Width, item.Area.Height) / 2))) bars.AddPath(path, false);
        }
        // The page as it was last shown: its cards, controls and plates as outlined shapes, its text and pictures as pulsing
        // bars, a coloured plate (the selected tab, a primary button) as a bar of its own.
        private void ComposeCaptured(Graphics g, float s, List<Block> bones, List<Block> blocks)
        {
            var palette = Theme.Current;
            foreach (var item in captured.Items)
            {
                var area = new RectangleF(item[1] * s, item[2] * s, item[3] * s, item[4] * s);
                if (area.Top > Height || area.Bottom < 0 || area.Width < 1 || area.Height < 1) continue;
                if (item[0] == 1) { bones.Add(new Block { Area = area, Radius = Math.Max(3 * s, item[5] * s) }); continue; }
                if (item[6] == 3) { bones.Add(new Block { Area = area, Radius = item[5] * s }); continue; }
                var fill = item[6] == 1 ? Card : item[6] == 2 ? palette.Track : Color.Transparent;
                var line = item[7] == 0 ? Color.Transparent : item[6] == 0 && item[7] == 1 ? palette.Control : Edge;
                blocks.Add(new Block { Area = area, Radius = item[5] * s, Fill = fill, Line = line, Dashed = item[7] == 2 });
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); picture?.Dispose(); bars?.Dispose(); } base.Dispose(disposing); }
    }
}
