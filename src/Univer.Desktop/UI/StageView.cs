using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    internal enum StageScene { Welcome, Downloads, Notices }
    // A miniature of the app telling one short story on a loop. Each frame is a function of the clock alone, so a resize or
    // a repaint can never leave a beat half done. Reduced motion and turned-off motion show the poster: the finished story.
    internal sealed class StageView : Control
    {
        private const int WelcomeLoop = 11000, WelcomePoster = 8000, ConveyorStep = 3200, ConveyorMove = 720;
        private readonly StageScene scene;
        private readonly Timer frames = new Timer { Interval = 33 };
        private readonly Stopwatch clock = new Stopwatch();
        private string language = "ru";
        private Font title, body, small, figure;
        private const TextFormatFlags Line = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        public StageView(StageScene scene)
        {
            this.scene = scene;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            TabStop = false; AccessibleRole = AccessibleRole.Graphic;
            frames.Tick += (s, e) => Invalidate();
        }
        private bool Playing { get { return Motion.Enabled && !Motion.Reduced; } }
        public void SetLanguage(string value)
        {
            language = value; AccessibleName = Strings.Get(scene == StageScene.Welcome ? "stageWelcome" : scene == StageScene.Notices ? "stageNotices" : "stageDownloads", language); Invalidate();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && Playing) { clock.Restart(); frames.Start(); } else { frames.Stop(); clock.Stop(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Render(e.Graphics, Moment());
        }
        private long Moment() { return Playing ? clock.ElapsedMilliseconds : scene == StageScene.Welcome ? WelcomePoster : ConveyorStep - 1; }
        private void Render(Graphics g, long now)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Ground);
            if (title == null) { title = Theme.Display(12f); body = Theme.Font(Theme.Body); small = Theme.Font(9f); figure = Theme.Display(21f); }
            var s = g.DpiX / 96f;
            if (scene == StageScene.Welcome) PaintWelcome(g, now % WelcomeLoop, s); else PaintConveyor(g, now, s);
        }
        private Color Ground { get { return scene == StageScene.Notices ? Theme.Surface : Theme.Background; } }
        // A contact sheet of chosen moments, for checking the story without watching it.
        internal void SaveSheetForQa(string path, int[] moments, int columns)
        {
            var rows = (moments.Length + columns - 1) / columns;
            using (var sheet = new Bitmap(columns * (Width + 8), rows * (Height + 8)))
            using (var canvas = Graphics.FromImage(sheet))
            {
                canvas.Clear(Color.Gray);
                for (var i = 0; i < moments.Length; i++)
                    using (var frame = new Bitmap(Width, Height))
                    {
                        using (var g = Graphics.FromImage(frame)) Render(g, moments[i]);
                        canvas.DrawImageUnscaled(frame, i % columns * (Width + 8) + 4, i / columns * (Height + 8) + 4);
                    }
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        // How far a beat has run between two times on the clock: 0 before it starts, 1 after it lands.
        private static float Beat(long now, int from, int to) { return Motion.EaseInOut(Math.Max(0, Math.Min(1, (now - from) / (float)(to - from)))); }
        private static Rectangle Box(float x, float y, float w, float h) { return new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(w), (int)Math.Round(h)); }
        private static void Say(Graphics g, string text, Font font, float x, float y, float w, Color color, TextFormatFlags extra = TextFormatFlags.Left)
        { TextRenderer.DrawText(g, text, font, Box(x, y, w, font.Height + 2), color, Line | extra); }
        private static void Plate(Graphics g, RectangleF r, float radius, Color fill, Color edge)
        {
            using (var path = Theme.Rounded(r, radius))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var pen = new Pen(edge)) g.DrawPath(pen, path);
            }
        }
        // A long, faint fall under something that floats; dark themes lean on the surface step instead.
        private static void Shadow(Graphics g, RectangleF r, float radius, float s, float strength)
        {
            for (var i = 8; i >= 1; i--)
            {
                var spread = i * 2 * s; var alpha = (int)((Theme.Dark ? 16 : 3) * strength);
                using (var path = Theme.Rounded(new RectangleF(r.X - spread, r.Y - spread + 4 * s, r.Width + spread * 2, r.Height + spread * 2), radius + spread))
                using (var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0))) g.FillPath(brush, path);
            }
        }
        private static void Bar(Graphics g, float x, float y, float w, float h, Color color)
        { using (var path = Theme.Rounded(new RectangleF(x, y, w, h), h / 2)) using (var brush = new SolidBrush(color)) g.FillPath(brush, path); }
        // The signature of this interface: a figure in a thin ink rectangle, like a date stamp in a record book.
        private void Stamp(Graphics g, string text, Font font, float x, float y, float h, float s, Color ink, Color edge, Color fill)
        {
            var w = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 12 * s;
            Plate(g, new RectangleF(x, y, w, h), 4 * s, fill, edge);
            TextRenderer.DrawText(g, text, font, Box(x, y, w, h), ink, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        private void PaintWelcome(Graphics g, long now, float s)
        {
            Func<float, float> d = v => v * s;
            // Today's lesson is picked out, then a mark rolls in on its card, then a file lands; the additions leave last first.
            var picked = Beat(now, 700, 1200) * (1 - Beat(now, 9700, 10200));
            // The lesson's progress only ever grows; at the end of the loop it fades with its row instead of running back.
            var running = Beat(now, 1000, 3400) * .62f; var runningLevel = 1 - Beat(now, 9700, 10000);
            var mark = Beat(now, 3000, 3700) * (1 - Beat(now, 9500, 9950));
            var count = (int)Math.Round(51 * Beat(now, 3300, 5000));
            var file = Beat(now, 5400, 6000) * (1 - Beat(now, 9300, 9750));
            var surface = Theme.Surface; var ink = Theme.Ink; var muted = Theme.Muted; var edge = Theme.Border; var filler = Theme.Current.Skeleton;
            // Hero: the app's window, inset top and left, running off the right and bottom edges.
            var win = new RectangleF(d(72), d(40), Math.Max(d(440), Width), Height + d(120));
            Shadow(g, win, d(12), s, 1); Plate(g, win, d(12), surface, edge);
            var rail = win.X + d(48);
            using (var pen = new Pen(edge)) g.DrawLine(pen, rail, win.Y + 1, rail, Height);
            var chrome = new[] { UiIcon.Home, UiIcon.Calendar, UiIcon.Journal, UiIcon.Book };
            for (var i = 0; i < chrome.Length; i++)
            {
                var slot = new RectangleF(win.X + d(8), win.Y + d(12) + i * d(38), d(32), d(32));
                if (i == 1) { using (var path = Theme.Rounded(slot, d(6))) using (var fill = new SolidBrush(Theme.Selected)) g.FillPath(fill, path); }
                Icons.Draw(g, chrome[i], new RectangleF(slot.X + d(7), slot.Y + d(7), d(18), d(18)), i == 1 ? ink : muted);
            }
            var left = rail + d(20); var right = Width - d(24);
            Say(g, Strings.Get("stageSchedule", language), title, left, win.Y + d(16), right - left, ink);
            Say(g, Strings.Get("stageMonday", language), small, left, win.Y + d(46), right - left, muted);
            var y = win.Y + d(72); var rowHeight = d(82);
            if (picked > .01f)
                using (var path = Theme.Rounded(new RectangleF(left - d(8), y, Width + d(8) - left, rowHeight - d(6)), d(8)))
                using (var fill = new SolidBrush(Color.FromArgb((int)(255 * picked), Theme.Hover))) g.FillPath(fill, path);
            var plate = Motion.Mix(surface, Theme.Hover, picked);
            Stamp(g, "09:00–10:50", small, left, y + d(8), d(20), s, Motion.Mix(ink, Theme.AccentInk, picked), Motion.Mix(edge, Theme.AccentInk, picked), plate);
            if (picked > .02f) Say(g, Strings.Get("stageNow", language), small, left + d(92), y + d(10) + d(4) * (1 - picked), d(80), Motion.Mix(plate, Theme.AccentInk, picked));
            Say(g, Strings.Get("stageLesson1", language), body, left, y + d(34), right - left, ink);
            Say(g, Strings.Get("stageRoom1", language), small, left, y + d(54), right - left, muted);
            if (running > 0 && runningLevel > .01f) Bar(g, left, y + rowHeight - d(9), (right - left) * running, d(2), Motion.Mix(plate, Theme.AccentInk, runningLevel));
            y += rowHeight;
            Stamp(g, "11:00–12:50", small, left, y + d(8), d(20), s, ink, edge, surface);
            Say(g, Strings.Get("stageLesson2", language), body, left, y + d(34), right - left, ink);
            Say(g, Strings.Get("stageRoom2", language), small, left, y + d(54), right - left, muted);
            y += rowHeight;
            // The rest of the day is filler at the same pitch, so the list keeps its rhythm.
            for (var i = 0; i < 3; i++, y += rowHeight)
            {
                Bar(g, left, y + d(12), d(84), d(10), filler);
                Bar(g, left, y + d(38), (right - left) * (.72f - i * .14f), d(9), filler);
                Bar(g, left, y + d(57), (right - left) * (.4f - i * .06f), d(7), filler);
            }
            if (mark > .01f) PaintMark(g, d(24), d(292) + d(12) * (1 - mark), mark, count, s);
            if (file > .01f) PaintFile(g, d(168), Height - d(80) + d(10) * (1 - file), Width - d(168) - d(16), file, s);
        }
        // Satellite: a mark card straddling the window's rail. It arrives whole and rises the last few pixels.
        private void PaintMark(Graphics g, float x, float y, float level, int value, float s)
        {
            var r = new RectangleF(x, y, 124 * s, 92 * s);
            var fill = Color.FromArgb((int)(255 * level), Theme.Surface);
            Shadow(g, r, 10 * s, s, level); Plate(g, r, 10 * s, fill, Color.FromArgb((int)(255 * level), Theme.Border));
            var text = Motion.Mix(Theme.Surface, Theme.Muted, level);
            Say(g, Strings.Get("stageMark", language), small, x + 14 * s, y + 12 * s, 96 * s, text);
            var number = value.ToString(); var ink = Motion.Mix(Theme.Surface, Theme.Ink, level);
            Say(g, number, figure, x + 14 * s, y + 34 * s, 60 * s, ink);
            var width = TextRenderer.MeasureText(g, number, figure, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            Say(g, "/ 100", small, x + 14 * s + width + 6 * s, y + 54 * s, 48 * s, text);
        }
        // A saved file settles in the window's corner, where the real notice appears.
        private void PaintFile(Graphics g, float x, float y, float width, float level, float s)
        {
            var r = new RectangleF(x, y, width, 64 * s);
            Shadow(g, r, 10 * s, s, level); Plate(g, r, 10 * s, Color.FromArgb((int)(255 * level), Theme.Surface), Color.FromArgb((int)(255 * level), Theme.Border));
            var ink = Motion.Mix(Theme.Surface, Theme.Ink, level); var muted = Motion.Mix(Theme.Surface, Theme.Muted, level);
            Icons.Draw(g, UiIcon.Check, new RectangleF(x + 14 * s, y + 14 * s, 18 * s, 18 * s), Motion.Mix(Theme.Surface, Theme.Success, level));
            Say(g, Strings.Get("stageFile1", language), body, x + 42 * s, y + 12 * s, width - 54 * s, ink);
            Say(g, Strings.Get("stageSaved", language), small, x + 42 * s, y + 34 * s, width - 54 * s, muted);
        }
        // Downloads with nothing in them: files keep arriving at the top of a ruled list and the oldest runs off the crop.
        private void PaintConveyor(Graphics g, long now, float s)
        {
            var cycle = (int)(now / ConveyorStep); var phase = (int)(now % ConveyorStep);
            var shift = Beat(phase, 0, ConveyorMove); var row = 44 * s; var kinds = new[] { "PDF", "PDF", "DOC" };
            var names = scene == StageScene.Notices ? new[] { "stageNotice1", "stageNotice2", "stageNotice3" } : new[] { "stageFile1", "stageFile2", "stageFile3" };
            var width = Math.Min(Width, (int)(380 * s)); var x = (Width - width) / 2f;
            for (var i = 0; i < 4; i++)
            {
                var y = (i - 1 + shift) * row + 2 * s;
                var index = ((cycle - i + 1) % 3 + 3) % 3;
                var enter = i == 0 ? shift : 1;
                var fade = Math.Max(0, Math.Min(1, 1.15f - y / (Height - row * .4f)));
                var level = enter * fade;
                var ink = Motion.Mix(Ground, Theme.Ink, level); var muted = Motion.Mix(Ground, Theme.Muted, level); var edge = Motion.Mix(Ground, Theme.Border, level);
                if (scene == StageScene.Notices) Icons.Draw(g, UiIcon.Bell, new RectangleF(x + 16 * s, y + 13 * s, 16 * s, 16 * s), Motion.Mix(Ground, Theme.AccentInk, level));
                else Stamp(g, kinds[index], small, x + 4 * s, y + 12 * s, 20 * s, s, muted, edge, Ground);
                Say(g, Strings.Get(names[index], language), body, x + 56 * s, y + 12 * s, width - 120 * s, ink);
                using (var pen = new Pen(edge)) g.DrawLine(pen, x, y + row - 1, x + width, y + row - 1);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { frames.Dispose(); title?.Dispose(); body?.Dispose(); small?.Dispose(); figure?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
