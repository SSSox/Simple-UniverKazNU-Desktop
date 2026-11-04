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
        private enum Shape { Home, Schedule, Table, Document, Page }
        private readonly Timer animation = new Timer { Interval = 500 };
        private readonly Stopwatch clock = new Stopwatch();
        private Shape shape = Shape.Page;
        private string slowText = "";
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
                // Reduced motion keeps the loading view static; full motion pulses gently at 30 fps.
                // A newly shown child window is black until it paints, so paint it before Windows composes the next frame.
                if (Visible) { clock.Restart(); animation.Interval = Motion.Enabled && !Motion.Reduced ? 33 : 500; animation.Start(); if (IsHandleCreated) Refresh(); } else { animation.Stop(); clock.Stop(); }
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
                : under("/news/student") || under("/student/bachelor") || path == "/" || under("/ssc") && path.Length <= 5 ? Shape.Home
                : under("/student/") ? Shape.Table
                : Shape.Page;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            var s = g.DpiX / 96f; Func<float, float> d = v => v * s;
            var ms = clock.ElapsedMilliseconds;
            const float appear = 1;
            var pulse = Motion.Enabled && !Motion.Reduced ? .90f + .10f * (float)(.5 + .5 * Math.Cos(ms / 3600.0 * 2 * Math.PI)) : 1f;
            var bones = new List<RectangleF>(); var cards = new List<RectangleF>();
            // Site header in Reading mode: brand, section tabs and the right-hand tools.
            var header = new RectangleF(0, 0, Width, shape == Shape.Document ? 0 : d(52));
            if (shape != Shape.Document)
            using (var white = new SolidBrush(Color.FromArgb((int)(255 * appear), Card))) g.FillRectangle(white, header);
            if (shape != Shape.Document)
            using (var line = new Pen(Color.FromArgb((int)(255 * appear), Edge))) g.DrawLine(line, 0, header.Bottom, Width, header.Bottom);
            var narrow = Width < d(900); var margin = narrow ? d(16) : d(32);
            if (shape != Shape.Document)
            {
            bones.Add(new RectangleF(margin, d(19), d(96), d(14)));
            if (!narrow) for (var i = 0; i < 3; i++) bones.Add(new RectangleF(margin + d(150) + i * d(92), d(14), d(76), d(24)));
            bones.Add(new RectangleF(Width - margin - d(112), d(12), d(112), d(28)));
            if (!narrow) bones.Add(new RectangleF(Width - margin - d(330), d(12), d(124), d(28)));
            }
            var page = new RectangleF(margin, header.Bottom + d(20), Math.Min(Width - 2 * margin, d(1400)), Height);
            var y = page.Top;
            switch (shape)
            {
                case Shape.Home:
                    var columns = Math.Max(1, (int)((page.Width + d(10)) / d(210))); var tile = (page.Width - (columns - 1) * d(10)) / columns;
                    for (var i = 0; i < Math.Min(columns, 5); i++)
                    {
                        var r = new RectangleF(page.Left + i * (tile + d(10)), y, tile, d(56)); cards.Add(r);
                        bones.Add(new RectangleF(r.Left + d(14), r.Top + d(16), d(24), d(24))); bones.Add(new RectangleF(r.Left + d(48), r.Top + d(22), Math.Min(d(110), tile * .45f), d(12)));
                    }
                    y += d(56) + d(30); bones.Add(new RectangleF(page.Left, y, d(132), d(20))); y += d(36);
                    var search = new RectangleF(page.Left, y, page.Width, d(64)); cards.Add(search); bones.Add(new RectangleF(search.Left + d(14), search.Top + d(14), search.Width * .5f, d(36))); y += d(88);
                    for (var n = 0; n < 2 && y < Height; n++)
                    {
                        var news = new RectangleF(page.Left, y, page.Width, d(260)); cards.Add(news);
                        bones.Add(new RectangleF(news.Left + d(24), news.Top + d(22), d(86), d(42)));
                        bones.Add(new RectangleF(news.Left + d(24), news.Top + d(92), news.Width * .42f, d(16)));
                        for (var l = 0; l < 4; l++) bones.Add(new RectangleF(news.Left + d(24), news.Top + d(126) + l * d(26), (news.Width - d(48)) * (l == 3 ? .55f : .92f - l * .08f), d(11)));
                        y += d(284);
                    }
                    break;
                case Shape.Schedule:
                    bones.Add(new RectangleF(page.Left, y, d(220), d(22))); y += d(40);
                    for (var i = 0; i < (narrow ? 4 : 7); i++) bones.Add(new RectangleF(page.Left + i * d(92), y, d(82), d(32)));
                    y += d(52);
                    for (var n = 0; n < 6 && y < Height; n++)
                    {
                        var row = new RectangleF(page.Left, y, page.Width, d(84)); cards.Add(row);
                        bones.Add(new RectangleF(row.Left + d(20), row.Top + d(20), d(64), d(14))); bones.Add(new RectangleF(row.Left + d(20), row.Top + d(44), d(48), d(12)));
                        bones.Add(new RectangleF(row.Left + d(120), row.Top + d(20), (row.Width - d(140)) * (.6f - n % 3 * .1f), d(16)));
                        bones.Add(new RectangleF(row.Left + d(120), row.Top + d(48), (row.Width - d(140)) * .3f, d(12)));
                        y += d(96);
                    }
                    break;
                case Shape.Document:
                    var sheet = new RectangleF((Width - Math.Min(Width - 2 * margin, d(820))) / 2, header.Bottom + d(24), Math.Min(Width - 2 * margin, d(820)), Height);
                    cards.Add(sheet); y = sheet.Top + d(48);
                    bones.Add(new RectangleF(sheet.Left + d(56), y, sheet.Width * .5f, d(22))); y += d(56);
                    for (var l = 0; y < Height; l++) { bones.Add(new RectangleF(sheet.Left + d(56), y, (sheet.Width - d(112)) * (l % 5 == 4 ? .6f : .96f - (l % 3) * .05f), d(11))); y += l % 5 == 4 ? d(42) : d(24); }
                    break;
                default:
                    bones.Add(new RectangleF(page.Left, y, d(160), d(12))); y += d(24);
                    bones.Add(new RectangleF(page.Left, y, d(280), d(22))); y += d(44);
                    var table = new RectangleF(page.Left, y, page.Width, Height); cards.Add(table);
                    var cells = narrow ? 2 : shape == Shape.Table ? 5 : 3; var cell = (table.Width - d(40)) / cells;
                    for (var c = 0; c < cells; c++) bones.Add(new RectangleF(table.Left + d(20) + c * cell, table.Top + d(20), cell * (c == 0 ? .6f : .4f), d(12)));
                    y = table.Top + d(52);
                    for (var n = 0; y < Height; n++)
                    {
                        for (var c = 0; c < cells; c++) bones.Add(new RectangleF(table.Left + d(20) + c * cell, y + d(18), cell * (c == 0 ? .75f - n % 3 * .12f : .32f), d(11)));
                        y += d(48);
                    }
                    break;
            }
            foreach (var r in cards)
                using (var path = Theme.Rounded(r, d(12)))
                using (var fill = new SolidBrush(Color.FromArgb((int)(255 * appear), Card))) using (var pen = new Pen(Color.FromArgb((int)(255 * appear), Edge)))
                { g.FillPath(fill, path); g.DrawPath(pen, path); }
            using (var all = new GraphicsPath())
            {
                foreach (var r in bones) using (var path = Theme.Rounded(r, Math.Min(d(6), r.Height / 2))) all.AddPath(path, false);
                using (var fill = new SolidBrush(Color.FromArgb((int)(255 * appear * pulse), Bone))) g.FillPath(fill, all);
            }
            // A slow site gets a quiet word of reassurance instead of an apparently frozen window.
            if (ms > 3000 && slowText.Length > 0)
            {
                var size = TextRenderer.MeasureText(slowText, Font);
                var pill = new RectangleF((Width - size.Width) / 2f - d(16), Height - d(56), size.Width + d(32), size.Height + d(8));
                using (var path = Theme.Rounded(pill, pill.Height / 2)) using (var fill = new SolidBrush(Card)) using (var pen = new Pen(Edge)) { g.FillPath(fill, path); g.DrawPath(pen, path); }
                TextRenderer.DrawText(g, slowText, Font, Rectangle.Round(pill), ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
}
