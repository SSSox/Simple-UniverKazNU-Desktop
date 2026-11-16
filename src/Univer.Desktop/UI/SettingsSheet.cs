using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // A group of settings as one sheet, like a date group in Downloads: one outline, its rows divided by hairlines. The lines
    // are given at design size and scale with the sheet, so the DPI and the app's zoom need nothing more.
    internal sealed class SettingsSheet : Panel
    {
        public int DesignHeight { get; set; } = 1;
        public int[] Lines { get; set; } = new int[0];
        public SettingsSheet()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var palette = Theme.Current; var scale = Height / (float)System.Math.Max(1, DesignHeight);
            g.Clear(Theme.Behind(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), Kit.RadiusCard * scale))
            {
                using (var fill = new SolidBrush(palette.Surface)) g.FillPath(fill, path);
                using (var pen = new Pen(palette.Line)) g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.None;
            using (var line = new SolidBrush(palette.Line))
                foreach (var y in Lines) g.FillRectangle(line, 16 * scale, y * scale, Width - 32 * scale, 1);
            base.OnPaint(e);
        }
    }
    // The theme picker's picture: the app in small, in the colours each choice gives it. As Windows shows both, split on a
    // slant, because that choice is the one that changes by itself.
    internal static class ThemeMiniature
    {
        public static void Draw(Graphics g, RectangleF area, float scale, string theme)
        {
            var state = g.Save();
            using (var frame = Theme.Rounded(area, Kit.RadiusMedium * scale))
            {
                g.SetClip(frame, CombineMode.Intersect);
                if (theme == ThemeChoice.System)
                {
                    Window(g, area, scale, Theme.Light);
                    var middle = area.X + area.Width / 2; var slant = area.Height * .22f;
                    using (var half = new GraphicsPath())
                    {
                        half.AddPolygon(new[] { new PointF(middle + slant, area.Y), new PointF(area.Right, area.Y), new PointF(area.Right, area.Bottom), new PointF(middle - slant, area.Bottom) });
                        g.SetClip(half, CombineMode.Intersect);
                    }
                    Window(g, area, scale, Theme.DarkPalette);
                }
                else Window(g, area, scale, theme == ThemeChoice.Dark ? Theme.DarkPalette : Theme.Light);
            }
            g.Restore(state);
        }
        private static void Block(Graphics g, Color color, float x, float y, float width, float height, float radius)
        {
            using (var path = Theme.Rounded(new RectangleF(x, y, width, height), radius)) using (var fill = new SolidBrush(color)) g.FillPath(fill, path);
        }
        // The sidebar with its chosen section, the toolbar, and the tiles of the home screen.
        private static void Window(Graphics g, RectangleF a, float s, Palette p)
        {
            using (var ground = new SolidBrush(p.Background)) g.FillRectangle(ground, a);
            var side = 30 * s; var bar = 14 * s;
            using (var fill = new SolidBrush(p.Surface)) { g.FillRectangle(fill, a.X, a.Y, side, a.Height); g.FillRectangle(fill, a.X + side, a.Y, a.Width - side, bar); }
            using (var line = new SolidBrush(p.Line)) { g.FillRectangle(line, a.X + side, a.Y, 1, a.Height); g.FillRectangle(line, a.X + side, a.Y + bar, a.Width - side, 1); }
            for (var i = 0; i < 4; i++) Block(g, i == 1 ? p.Accent : p.Control, a.X + 7 * s, a.Y + (12 + i * 12) * s, side - 14 * s, 6 * s, 2 * s);
            Block(g, p.Control, a.X + side + 8 * s, a.Y + 5 * s, 34 * s, 4 * s, 2 * s);
            var left = a.X + side + 8 * s; var top = a.Y + bar + 9 * s; var gap = 6 * s; var width = (a.Right - 8 * s - left - gap) / 2; var height = 22 * s;
            for (var i = 0; i < 4; i++)
            {
                var x = left + (i % 2) * (width + gap); var y = top + (i / 2) * (height + gap);
                using (var path = Theme.Rounded(new RectangleF(x, y, width, height), 3 * s))
                {
                    using (var fill = new SolidBrush(p.Surface)) g.FillPath(fill, path);
                    using (var pen = new Pen(p.Line)) g.DrawPath(pen, path);
                }
                Block(g, i == 0 ? p.Accent : p.AccentSoft, x + 5 * s, y + 6 * s, 10 * s, 10 * s, 2 * s);
                Block(g, p.Control, x + 19 * s, y + 9 * s, System.Math.Max(4 * s, width - 26 * s), 4 * s, 2 * s);
            }
        }
    }
}
