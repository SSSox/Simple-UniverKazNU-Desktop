using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // While a page loads, a softly pulsing empty copy of the page layout shows the destination:
    // the header, then placeholder tiles, tables or text that match where the student is going.
    internal sealed class LoadingView : Panel
    {
        private enum Shape { Home, Bachelor, Schedule, Grades, Courses, Files, Table, Document, Page, Web, Recipe }
        private RecipeStep[] recipe;
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
        // The destination picks the placeholder layout; unknown pages get a generic heading with text blocks. "web" is another
        // site in the link window: no Univer header, only a column of text.
        public void SetRoute(string url)
        {
            var path = "";
            try { path = url == "document" || url == "web" ? url : new Uri(url).AbsolutePath.ToLowerInvariant(); } catch (Exception) { }
            Func<string, bool> under = prefix => path.StartsWith(prefix, StringComparison.Ordinal);
            var found = PageRecipes.Find(path);
            shape = path == "document" ? Shape.Document
                : path == "web" ? Shape.Web
                : under("/student/myschedule") ? Shape.Schedule
                : under("/student/attendance") ? Shape.Grades
                : under("/student/bachelor") ? Shape.Bachelor
                : under("/news/student") || path == "/" || under("/ssc") && path.Length <= 5 ? Shape.Home
                : Regex.IsMatch(path, @"^/student/umkd(/0/\d+/\d+)?/?$") ? Shape.Courses
                : under("/student/umkd/") ? Shape.Files
                : found != null ? Shape.Recipe
                : under("/student/") ? Shape.Table
                : Shape.Page;
            recipe = shape == Shape.Recipe ? found : null;
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
        private RecipeStep[] drawnRecipe;
        private float drawnZoom;
        private Core.PageShape drawnCapture, captured;
        // The page's real layout from its last visit; used while it fits the window, otherwise the drawn shape stands in.
        public Core.PageShape Captured { get { return captured; } set { if (captured == value) return; captured = value; Invalidate(); } }
        private bool Drawn { get { return picture != null && picture.Width == Width && picture.Height == Height && drawnPalette == Theme.Current && drawnShape == shape && drawnRecipe == recipe && drawnZoom == zoom && drawnCapture == captured; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 1 || Height < 1) return;
            var g = e.Graphics; var s = Theme.DpiScale * zoom; var ms = clock.ElapsedMilliseconds;
            if (!Drawn)
            {
                picture?.Dispose(); bars?.Dispose();
                picture = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb); picture.SetResolution(g.DpiX, g.DpiY);
                bars = new GraphicsPath(); drawnPalette = Theme.Current; drawnShape = shape; drawnRecipe = recipe; drawnZoom = zoom; drawnCapture = captured;
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
            var document = shape == Shape.Document || shape == Shape.Web;
            if (!document && captured != null && captured.Fits(Width / s)) { ComposeCaptured(s, bones, blocks); DrawBlocks(g, bones, blocks); return; }
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
                case Shape.Web:
                    var columnWidth = Math.Min(Width - 2 * gutter, d(960)); var columnX = (Width - columnWidth) / 2; y = d(40);
                    bar(columnX, y, columnWidth * .45f, d(24), d(6)); y += d(60);
                    for (var l = 0; y < Height; l++) { bar(columnX, y, columnWidth * (l % 5 == 4 ? .6f : .96f - (l % 3) * .05f), d(11), d(4)); y += l % 5 == 4 ? d(42) : d(24); }
                    break;
                case Shape.Recipe:
                    Draw(recipe, bones, blocks, s, y);
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
            DrawBlocks(g, bones, blocks);
        }
        // A page made of parts, drawn top down until the window is full. Measures are the site's own, in CSS pixels: the page
        // column keeps 32 px from the window (16 in a narrow one) and is at most 1400 wide.
        private void Draw(RecipeStep[] steps, List<Block> bones, List<Block> blocks, float s, float y)
        {
            Func<float, float> d = v => v * s;
            var palette = Theme.Current; var outline = palette.Control; var narrow = Width < d(900);
            var pageWidth = Math.Min(Width - 2 * d(narrow ? 16 : 32), d(1400)); var left = (Width - pageWidth) / 2; var right = left + pageWidth;
            Action<float, float, float, float, float> bar = (x, top, w, h, r) => bones.Add(new Block { Area = new RectangleF(x, top, Math.Max(1, w), h), Radius = r });
            Action<float, float, float, float, float, Color, Color> block = (x, top, w, h, r, fill, line) => blocks.Add(new Block { Area = new RectangleF(x, top, w, h), Radius = r, Fill = fill, Line = line });
            Action<float, float, float, float, float> dashed = (x, top, w, h, r) => blocks.Add(new Block { Area = new RectangleF(x, top, w, h), Radius = r, Fill = BackColor, Line = outline, Dashed = true });
            Action<float, float, float> rule = (x, top, w) => block(x, top, w, 1, 0, Edge, Edge);
            foreach (var step in steps)
            {
                if (y > Height) return;
                switch (step.Part)
                {
                    case RecipePart.Column:
                        pageWidth = Math.Min(pageWidth, d(step.A)); left = (Width - pageWidth) / 2; right = left + pageWidth;
                        break;
                    case RecipePart.Title:
                        bar(left, y + d(4), Math.Min(d(step.A), pageWidth * .8f), d(20), d(5)); y += d(48);
                        break;
                    case RecipePart.Subtitle:
                        var subtitle = Math.Min(d(step.A), pageWidth * .7f);
                        bar(step.B == 1 ? left + (pageWidth - subtitle) / 2 : left, y + d(2), subtitle, d(16), d(4)); y += d(36);
                        break;
                    case RecipePart.Back:
                        block(left, y, d(68), d(36), d(10), Card, outline); bar(left + d(16), y + d(12), d(36), d(12), d(4)); y += d(52);
                        break;
                    case RecipePart.Notice:
                        // A folded help note, a tinted note with an icon, a one-line warning, or a white card ending in a button.
                        if (step.A == 0) { block(left, y, pageWidth, d(44), d(12), palette.InfoFill, palette.Line); bar(left + d(16), y + d(14), d(16), d(16), d(8)); bar(left + d(44), y + d(16), d(70), d(12), d(4)); y += d(64); break; }
                        var card = step.B == 2; var noteHeight = d(24 + 22 * step.A + (card ? 48 : 0));
                        block(left, y, pageWidth, noteHeight, d(12), card ? Card : step.B == 1 ? palette.WarningFill : palette.InfoFill, card ? Edge : palette.Line);
                        var text = card ? left + d(16) : left + d(44);
                        if (!card) bar(left + d(16), y + d(13), d(16), d(16), d(8));
                        for (var l = 0; l < step.A; l++)
                            bar(text, y + d(16 + 22 * l), (right - d(16) - text) * (step.A == 1 ? .7f : l == 0 ? .3f : l == step.A - 1 ? .45f : .92f - l % 3 * .07f), d(11), d(4));
                        if (card) block(left + d(16), y + noteHeight - d(48), d(152), d(32), d(8), Card, outline);
                        y += noteHeight + d(24);
                        break;
                    case RecipePart.Accordion:
                        var tall = step.B == 1; var rowHeight = d(tall ? 96 : 52); var widths = new[] { 240, 330, 380, 300, 260, 350 };
                        for (var i = 0; i < step.A && y < Height; i++)
                        {
                            block(left, y, pageWidth, rowHeight, d(12), Card, Edge);
                            var room = pageWidth - d(72);
                            if (tall) for (var l = 0; l < 3; l++) bar(left + d(16), y + d(20 + 22 * l), room * (l == 2 ? .4f + i % 3 * .1f : .94f - l * .04f), d(11), d(4));
                            else
                            {
                                var label = Math.Min(room * .7f, d(widths[i % widths.Length])); bar(left + d(16), y + d(20), label, d(12), d(4));
                                if (step.B == 2) block(left + d(28) + label, y + d(16), d(28), d(20), d(10), palette.Track, palette.Track);
                            }
                            bar(right - d(28), y + rowHeight / 2 - d(4), d(12), d(8), d(3));
                            y += rowHeight + d(tall ? 20 : 24);
                        }
                        break;
                    case RecipePart.Search:
                        var search = Math.Min(d(456), pageWidth);
                        block(left, y, search, d(40), d(10), Card, outline); bar(left + d(14), y + d(14), d(12), d(12), d(6)); bar(left + d(36), y + d(14), d(110), d(12), d(4)); y += d(56);
                        break;
                    case RecipePart.Pills:
                        var pills = new[] { 84, 156, 120 }; var pillX = left;
                        for (var i = 0; i < step.A; i++)
                        {
                            var pill = d(pills[i % pills.Length]);
                            // A narrow column wraps the pills, as the site's flex row does.
                            if (i > 0 && pillX + pill > right) { pillX = left; y += d(44); }
                            if (i == 0) bar(pillX, y, pill, d(36), d(18)); else { block(pillX, y, pill, d(36), d(18), Card, outline); bar(pillX + d(18), y + d(12), pill - d(36), d(12), d(4)); }
                            pillX += pill + d(8);
                        }
                        y += d(60);
                        break;
                    case RecipePart.Tabs:
                        var tabX = left;
                        for (var i = 0; i < step.A; i++)
                        {
                            if (tabX > left && tabX + d(78) > right) { tabX = left; y += d(44); }
                            block(tabX, y, d(78), d(36), d(10), i == 0 ? palette.Track : Card, outline); bar(tabX + d(14), y + d(12), d(50), d(12), d(4)); tabX += d(86);
                        }
                        y += d(52);
                        break;
                    case RecipePart.SelectCard:
                        block(left, y, pageWidth, d(104), d(12), Card, Edge); bar(left + d(28), y + d(26), d(150), d(12), d(4));
                        block(left + d(28), y + d(50), pageWidth - d(56), d(34), d(8), Card, outline); bar(left + d(40), y + d(61), d(60), d(12), d(4)); bar(right - d(48), y + d(64), d(10), d(6), d(3));
                        y += d(128);
                        break;
                    case RecipePart.Empty:
                        dashed(left, y, pageWidth, d(80), d(12)); bar(left + d(16), y + d(34), d(12), d(12), d(3)); bar(left + d(36), y + d(34), d(120), d(12), d(4)); y += d(104);
                        break;
                    case RecipePart.Pager:
                        var step2 = Math.Min(d(240), (pageWidth - d(16)) / 2);
                        block(left, y, step2, d(36), d(10), Card, outline); bar(left + d(16), y + d(12), step2 - d(32), d(12), d(4));
                        block(right - step2, y, step2, d(36), d(10), Card, outline); bar(right - step2 + d(16), y + d(12), step2 - d(32), d(12), d(4));
                        y += d(60);
                        break;
                    case RecipePart.Facts:
                        // Label and value pairs in a ruled grid; B = 3 is the study plan's grid of counts (a label and a number).
                        var counts = step.B == 3; var pairs = narrow ? 1 : step.B; var factRows = narrow ? step.A * step.B : step.A;
                        var factHeight = factRows * d(48); var column = pageWidth / pairs;
                        block(left, y, pageWidth, factHeight, d(12), Card, Edge);
                        for (var c = 1; c < pairs; c++) block(left + c * column, y + 1, 1, factHeight - 2, 0, Edge, Edge);
                        for (var r = 0; r < factRows && y + r * d(48) < Height; r++)
                        {
                            var rowTop = y + r * d(48); if (r > 0) rule(left + 1, rowTop, pageWidth - 2);
                            for (var c = 0; c < pairs; c++)
                            {
                                var x = left + c * column;
                                if (counts) { bar(x + d(14), rowTop + d(18), column * (.5f + (r + c) % 3 * .1f), d(11), d(4)); bar(x + column - d(26), rowTop + d(18), d(10), d(11), d(3)); }
                                else { bar(x + d(14), rowTop + d(18), Math.Min(d(110), column * .3f), d(10), d(4)); bar(x + column * .36f, rowTop + d(17), Math.Min(d(170), column * .5f), d(12), d(4)); }
                            }
                        }
                        y += factHeight + d(24);
                        break;
                    case RecipePart.Table:
                        // Its heading row, then rows; a table without rows shows the site's "no data" line.
                        var cells = narrow ? Math.Min(step.A, 3) : step.A; var unit = (pageWidth - d(32)) / (cells + 1);
                        var tableHeight = d(44) + (step.B == 0 ? d(72) : step.B * d(52));
                        block(left, y, pageWidth, tableHeight, d(12), Card, Edge); rule(left + 1, y + d(44), pageWidth - 2);
                        Func<int, float> cellX = c => left + d(16) + (c == 0 ? 0 : (c + 1) * unit);
                        for (var c = 0; c < cells; c++) bar(cellX(c), y + d(16), (c == 0 ? 2 : 1) * unit * .4f, d(11), d(4));
                        if (step.B == 0) { bar(left + d(16), y + d(74), d(12), d(12), d(3)); bar(left + d(36), y + d(74), d(80), d(12), d(4)); }
                        for (var r = 0; r < step.B && y + d(44) + r * d(52) < Height; r++)
                        {
                            var rowTop = y + d(44) + r * d(52); if (r > 0) rule(left + 1, rowTop, pageWidth - 2);
                            for (var c = 0; c < cells; c++)
                                if (step.C == 1 && c == cells - 1) bar(cellX(c), rowTop + d(10), Math.Min(d(76), unit - d(8)), d(32), d(8));
                                else bar(cellX(c), rowTop + d(20), (c == 0 ? 2 : 1) * unit * (c == 0 ? .7f - r % 3 * .12f : .5f), d(11), d(4));
                        }
                        y += tableHeight + d(24);
                        break;
                    case RecipePart.DocCards:
                        var docColumns = pageWidth >= d(700) ? 3 : pageWidth >= d(460) ? 2 : 1; var docWidth = (pageWidth - (docColumns - 1) * d(8)) / docColumns;
                        for (var i = 0; i < step.A; i++)
                        {
                            var x = left + i % docColumns * (docWidth + d(8)); var top = y + i / docColumns * d(60);
                            block(x, top, docWidth, d(52), d(12), Card, Edge); bar(x + d(14), top + d(14), d(24), d(24), d(6));
                            bar(x + d(50), top + d(14), docWidth * .62f, d(11), d(4)); bar(x + d(50), top + d(31), docWidth * .34f, d(11), d(4));
                        }
                        y += (step.A + docColumns - 1) / docColumns * d(60) + d(16);
                        break;
                    case RecipePart.DocRows:
                        for (var i = 0; i < step.A; i++)
                        {
                            block(left, y, pageWidth, d(48), d(12), Card, Edge); bar(left + d(14), y + d(12), d(24), d(24), d(6));
                            bar(left + d(50), y + d(18), Math.Min(d(320), pageWidth * .45f) - i % 2 * d(40), d(12), d(4));
                            if (step.B == 1) { block(right - d(78), y + d(8), d(64), d(32), d(8), Card, outline); bar(right - d(64), y + d(19), d(36), d(10), d(4)); }
                            y += d(56);
                        }
                        y += d(16);
                        break;
                    case RecipePart.Stats:
                        var statColumns = Math.Max(1, Math.Min(step.A, (int)((pageWidth + d(12)) / d(212)))); var stat = (pageWidth - (statColumns - 1) * d(12)) / statColumns;
                        for (var i = 0; i < step.A; i++)
                        {
                            var x = left + i % statColumns * (stat + d(12)); var top = y + i / statColumns * d(140);
                            block(x, top, stat, d(128), d(12), i == 0 ? palette.InfoFill : Card, Edge);
                            bar(x + d(16), top + d(22), stat * .45f, d(11), d(4)); bar(x + d(16), top + d(58), stat * .4f, d(24), d(6)); bar(x + d(16), top + d(98), stat * .32f, d(10), d(4));
                        }
                        y += (step.A + statColumns - 1) / statColumns * d(140) + d(12);
                        break;
                    case RecipePart.News:
                        // A date plate, the title, three lines and Show in full.
                        for (var i = 0; i < step.A && y < Height; i++)
                        {
                            block(left, y, pageWidth, d(200), d(12), Card, Edge);
                            block(left + d(16), y + d(16), d(58), d(58), d(10), palette.InfoFill, palette.Line); bar(left + d(30), y + d(26), d(30), d(18), d(4)); bar(left + d(26), y + d(50), d(38), d(10), d(4));
                            bar(left + d(20), y + d(86), d(50), d(9), d(4));
                            var newsText = left + d(88); var newsWidth = right - d(16) - newsText;
                            bar(newsText, y + d(20), newsWidth * (.7f - i % 2 * .2f), d(15), d(4));
                            for (var l = 0; l < 3; l++) bar(newsText, y + d(56 + 20 * l), newsWidth * (l == 2 ? .5f : .95f), d(10), d(4));
                            block(newsText, y + d(152), d(152), d(32), d(8), Card, outline);
                            y += d(212);
                        }
                        break;
                    case RecipePart.Action:
                        bar(right - d(120), y, d(120), d(36), d(8)); y += d(48);
                        break;
                    case RecipePart.Inputs:
                        // The GPA calculator: a row of three fields per subject, Add a subject, then the result plate.
                        var inputsHeight = d(16 + 40 + 16 + 36 + 16 + 48 + 16) + step.A * d(56); block(left, y, pageWidth, inputsHeight, d(12), Card, Edge);
                        var inner = pageWidth - d(32); var shares = new[] { .4f, .36f, .24f }; var inputX = left + d(16);
                        var fieldX = inputX; for (var c = 0; c < 3; c++) { bar(fieldX + d(12), y + d(30), inner * shares[c] * .4f, d(11), d(4)); fieldX += inner * shares[c]; }
                        var inputTop = y + d(56);
                        for (var r = 0; r < step.A; r++)
                        {
                            fieldX = inputX; for (var c = 0; c < 3; c++) { block(fieldX + d(12), inputTop + d(10), inner * shares[c] - d(24), d(34), d(8), Card, outline); fieldX += inner * shares[c]; }
                            inputTop += d(56);
                        }
                        inputTop += d(16); block(inputX, inputTop, d(160), d(36), d(10), Card, outline); bar(inputX + d(16), inputTop + d(12), d(128), d(12), d(4)); inputTop += d(52);
                        block(inputX, inputTop, d(152), d(48), d(12), Card, Edge); bar(inputX + d(16), inputTop + d(19), d(64), d(10), d(4)); bar(inputX + d(92), inputTop + d(14), d(44), d(20), d(5));
                        y += inputsHeight + d(24);
                        break;
                    case RecipePart.Video:
                        block(left, y, pageWidth, d(122), d(6), palette.Track, palette.Track); y += d(138);
                        break;
                    case RecipePart.ButtonNote:
                        block(left, y, d(200), d(40), d(10), Card, outline); bar(left + d(16), y + d(14), d(168), d(12), d(4));
                        if (pageWidth > d(520)) { block(left + d(216), y, pageWidth - d(216), d(40), d(10), palette.SuccessTrack, palette.SuccessLine); bar(left + d(236), y + d(14), Math.Min(d(480), pageWidth - d(260)), d(12), d(4)); }
                        y += d(72);
                        break;
                    case RecipePart.Footer:
                        // The site's footer: three folded sections and the copyright line.
                        y += d(16); var footer = new[] { 80, 200, 140 };
                        for (var i = 0; i < 3; i++) { block(left, y, pageWidth, d(44), d(12), Card, Edge); bar(left + d(16), y + d(16), d(footer[i]), d(12), d(4)); bar(right - d(28), y + d(18), d(12), d(8), d(3)); y += d(52); }
                        bar(left + pageWidth / 2 - d(150), y + d(16), d(300), d(10), d(4)); y += d(48);
                        break;
                    case RecipePart.Photo:
                        // Personal data: the e-mail field, the current and new photo, and the file picker.
                        var photoX = left + d(28); var photoWidth = pageWidth - d(56);
                        block(left, y, pageWidth, d(420), d(12), Card, Edge);
                        bar(photoX, y + d(28), d(60), d(12), d(4)); block(photoX, y + d(50), photoWidth, d(34), d(8), Card, outline); bar(photoX + d(12), y + d(61), Math.Min(d(220), photoWidth * .4f), d(12), d(4));
                        block(photoX, y + d(108), photoWidth, d(206), d(12), Card, Edge);
                        for (var i = 0; i < 2; i++) { bar(photoX + d(16) + i * d(118), y + d(126), d(84), d(12), d(4)); block(photoX + d(16) + i * d(118), y + d(150), d(98), d(130), d(8), i == 0 ? palette.Track : Card, Edge); }
                        bar(photoX, y + d(338), d(160), d(12), d(4)); dashed(photoX, y + d(360), photoWidth, d(38), d(8)); block(photoX + d(6), y + d(364), d(102), d(30), d(8), Card, outline);
                        y += d(444);
                        break;
                    case RecipePart.YearBar:
                        // IGPA's heading with the year stepper, a rule, the year line with its admission badge.
                        bar(left, y + d(8), d(110), d(20), d(5)); block(right - d(148), y, d(148), d(36), d(10), Card, outline); bar(right - d(118), y + d(12), d(88), d(12), d(4));
                        y += d(52); rule(left, y, pageWidth); y += d(20);
                        // The year line gives way to the badge in a narrow column.
                        bar(left, y + d(4), Math.Max(d(40), Math.Min(d(160), pageWidth - d(178))), d(12), d(4)); block(right - d(162), y - d(4), d(162), d(28), d(6), palette.SuccessTrack, palette.SuccessLine); bar(right - d(150), y + d(4), d(138), d(12), d(4));
                        y += d(44);
                        break;
                    case RecipePart.Segment:
                        // The heading and the period switch (a track with the chosen half raised); below the heading when the column
                        // has no room for both.
                        var segment = d(210);
                        bar(left, y + d(9), d(96), d(16), d(4));
                        if (pageWidth < d(330)) y += d(40);
                        block(right - segment, y, segment, d(34), d(8), palette.Track, palette.Track); block(right - segment / 2, y + d(2), segment / 2 - d(2), d(30), d(6), Card, Edge);
                        bar(right - segment + d(14), y + d(11), segment / 2 - d(28), d(12), d(4)); bar(right - segment / 2 + d(12), y + d(11), segment / 2 - d(28), d(12), d(4));
                        y += d(50);
                        break;
                    case RecipePart.Fields:
                        // A form of sections: each a heading and its select.
                        var fieldsHeight = d(24) + step.A * d(132); block(left, y, pageWidth, fieldsHeight, d(12), Card, Edge);
                        var sectionX = left + d(20); var sectionWidth = pageWidth - d(40); var sectionTop = y + d(24);
                        for (var i = 0; i < step.A && sectionTop < Height; i++)
                        {
                            block(sectionX, sectionTop, sectionWidth, d(112), d(10), Card, Edge); bar(sectionX + d(16), sectionTop + d(20), d(150), d(14), d(4)); rule(sectionX + 1, sectionTop + d(52), sectionWidth - 2);
                            block(sectionX + d(14), sectionTop + d(64), sectionWidth - d(28), d(36), d(8), Card, outline); bar(sectionX + d(28), sectionTop + d(76), Math.Min(d(220), sectionWidth * .4f), d(12), d(4));
                            sectionTop += d(132);
                        }
                        y += fieldsHeight + d(24);
                        break;
                    case RecipePart.Cards:
                        // Questionnaires: the course, the teacher with the answered mark beside, the date of the answers.
                        for (var i = 0; i < step.A && y < Height; i++)
                        {
                            block(left, y, pageWidth, d(180), d(12), Card, Edge); var cardX = left + d(16); var mark = left + pageWidth * .68f;
                            bar(cardX, y + d(20), d(72), d(9), d(3)); bar(cardX, y + d(40), Math.Min(d(200), pageWidth * .4f) - i % 2 * d(40), d(14), d(4));
                            bar(cardX, y + d(76), d(110), d(9), d(3)); bar(cardX, y + d(96), Math.Min(d(240), pageWidth * .45f), d(13), d(4));
                            bar(mark, y + d(76), d(64), d(9), d(3)); bar(mark + d(26), y + d(94), d(12), d(12), d(3));
                            bar(cardX, y + d(128), d(84), d(9), d(3)); bar(cardX, y + d(148), d(80), d(13), d(4));
                            y += d(192);
                        }
                        break;
                    case RecipePart.Questions:
                        // Questions: two lines of the question, then the five marks of the scale.
                        for (var i = 0; i < step.A && y < Height; i++)
                        {
                            block(left, y, pageWidth, d(136), d(12), Card, Edge); var question = pageWidth - d(40);
                            bar(left + d(20), y + d(22), question * (.85f - i % 2 * .1f), d(13), d(4)); bar(left + d(20), y + d(44), question * .45f, d(13), d(4));
                            var option = Math.Min(d(150), question / 5);
                            for (var o = 0; o < 5; o++) { var x = left + d(20) + o * option; bar(x, y + d(88), d(18), d(18), d(9)); bar(x + d(26), y + d(92), Math.Min(d(80), option - d(40)), d(10), d(4)); }
                            y += d(148);
                        }
                        break;
                    case RecipePart.Centred:
                        block(left, y, pageWidth, d(52), d(12), Card, Edge); block(left + pageWidth / 2 - d(38), y + d(10), d(76), d(32), d(8), Card, outline); bar(left + pageWidth / 2 - d(24), y + d(20), d(48), d(12), d(4));
                        y += d(76);
                        break;
                    case RecipePart.Boxed:
                        block(left, y, pageWidth, d(120), d(12), Card, Edge); bar(left + d(14), y + d(16), Math.Min(d(220), pageWidth * .5f), d(13), d(4)); rule(left + 1, y + d(40), pageWidth - 2);
                        bar(left + d(16), y + d(74), d(12), d(12), d(3)); bar(left + d(36), y + d(74), d(80), d(12), d(4));
                        y += d(144);
                        break;
                    case RecipePart.Rows:
                        // The student's record: label and value rows, ruled every two.
                        var rowsHeight = d(24) + step.A * d(31) + (step.A - 1) / 2 * d(14); block(left, y, pageWidth, rowsHeight, d(12), Card, Edge); var line = y + d(16);
                        for (var r = 0; r < step.A && line < Height; r++)
                        {
                            bar(left + d(28), line + d(10), Math.Min(d(150), pageWidth * .25f) - r % 3 * d(20), d(10), d(4));
                            bar(left + Math.Min(d(254), pageWidth * .38f), line + d(9), Math.Min(d(260), pageWidth * .45f) - r % 4 * d(30), d(12), d(4));
                            line += d(31);
                            if (r % 2 == 1 && r < step.A - 1) { rule(left + d(14), line + d(6), pageWidth - d(28)); line += d(14); }
                        }
                        y += rowsHeight + d(24);
                        break;
                    case RecipePart.Form:
                        // New appeal: a centred form of two selects, the text, the attachments and the two buttons.
                        var formWidth = Math.Min(d(620), pageWidth); var formX = left + (pageWidth - formWidth) / 2;
                        bar(formX, y + d(4), d(170), d(18), d(5)); y += d(40);
                        for (var f = 0; f < 2; f++) { bar(formX, y, d(110), d(12), d(4)); y += d(22); block(formX, y, formWidth, d(34), d(6), Card, outline); bar(formX + d(12), y + d(11), d(160), d(12), d(4)); y += d(56); }
                        bar(formX, y, d(150), d(12), d(4)); y += d(22); block(formX, y, formWidth, d(146), d(6), Card, outline); y += d(156);
                        bar(formX + formWidth - d(50), y, d(50), d(10), d(4)); y += d(34);
                        bar(formX, y, d(80), d(12), d(4)); y += d(22); dashed(formX, y, formWidth, d(38), d(8)); block(formX + d(6), y + d(4), d(102), d(30), d(8), Card, outline); bar(formX + d(116), y + d(14), d(84), d(10), d(4)); y += d(50);
                        bar(formX, y, Math.Min(d(380), formWidth * .7f), d(10), d(4)); y += d(34);
                        block(formX + formWidth - d(150), y, d(58), d(34), d(8), Card, outline); bar(formX + formWidth - d(82), y, d(82), d(34), d(8)); y += d(58);
                        break;
                }
            }
        }
        private void DrawBlocks(Graphics g, List<Block> bones, List<Block> blocks)
        {
            foreach (var item in blocks)
                using (var path = Theme.Rounded(item.Area, item.Radius)) using (var fill = new SolidBrush(item.Fill)) using (var pen = new Pen(item.Line) { DashStyle = item.Dashed ? DashStyle.Dash : DashStyle.Solid })
                { if (item.Fill.A > 0) g.FillPath(fill, path); if (item.Line.A > 0) g.DrawPath(pen, path); }
            foreach (var item in bones) using (var path = Theme.Rounded(item.Area, Math.Min(item.Radius, Math.Min(item.Area.Width, item.Area.Height) / 2))) bars.AddPath(path, false);
        }
        // The page as it was last shown: its cards, controls and plates as outlined shapes, its text and pictures as pulsing
        // bars, a coloured plate (the selected tab, a primary button) as a bar of its own.
        private void ComposeCaptured(float s, List<Block> bones, List<Block> blocks)
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
