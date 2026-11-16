using System;
using System.Drawing;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // Fill: a tinted row (sections). Thumb: the raised part of a segmented control. Underline: the bar under tabs.
    internal enum PlateStyle { Fill, Thumb, Underline }
    // One highlight that travels between the buttons of a group, instead of each button lighting up on its own. The host
    // paints it and every button repaints its share, so it reads as one object sliding under the labels.
    internal sealed class SelectionPlate
    {
        private readonly Control host;
        private readonly Func<float> radius;
        private Control target;
        private RectangleF rect;
        private float level;
        private Tween move;
        public PlateStyle Style { get; set; }
        // A segmented track is not the host's own colour: its buttons clear to this before painting their share.
        public Func<Color> Ground { get; set; }
        public SelectionPlate(Control host, Func<float> radius) { this.host = host; this.radius = radius; }
        private void Repaint(RectangleF before)
        {
            if (host.IsDisposed) return;
            var area = Rectangle.Inflate(Rectangle.Ceiling(RectangleF.Union(before, rect)), 2, 2);
            host.Invalidate(area, true);
        }
        public void MoveTo(Control next)
        {
            target = next; move?.Cancel();
            var from = rect; var fromLevel = level; var to = (RectangleF)next.Bounds;
            var slide = Kit.Moving(host) && fromLevel > .5f;
            if (!slide) { rect = to; level = 1; Repaint(from); return; }
            move = Motion.Run(Motion.Slide, t =>
            {
                var before = rect; var e = Motion.EaseOut(t);
                rect = new RectangleF(Motion.Lerp(from.X, to.X, e), Motion.Lerp(from.Y, to.Y, e), Motion.Lerp(from.Width, to.Width, e), Motion.Lerp(from.Height, to.Height, e));
                level = Motion.Lerp(fromLevel, 1, e); Repaint(before);
            });
        }
        // A group can end with nothing chosen; a following MoveTo cancels this and slides from where the plate is.
        public void Release(Control old)
        {
            if (target != old) return;
            target = null; move?.Cancel(); var from = level;
            move = Motion.Run(Kit.Fading(host) ? Motion.Fast : 0, t => { level = Motion.Lerp(from, 0, Motion.EaseOut(t)); Repaint(rect); });
        }
        // After a layout change the plate sits on its button without travelling.
        public void Snap() { if (target == null) return; move?.Cancel(); var before = rect; rect = target.Bounds; level = 1; Repaint(before); }
        public void Draw(Graphics g, float dx, float dy)
        {
            if (level <= .01f) return;
            var state = g.SmoothingMode; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var alpha = (int)(255 * Math.Min(1, level)); var scale = g.DpiX / 96f;
            if (Style == PlateStyle.Underline)
                using (var fill = new SolidBrush(Color.FromArgb(alpha, Theme.AccentInk))) g.FillRectangle(fill, rect.X + dx, rect.Bottom + dy - 2 * scale, rect.Width, 2 * scale);
            else
                using (var path = Theme.Rounded(new RectangleF(rect.X + dx + .5f, rect.Y + dy + .5f, rect.Width - 1, rect.Height - 1), radius()))
                using (var fill = new SolidBrush(Color.FromArgb(alpha, Style == PlateStyle.Thumb ? Theme.Current.Raised : Theme.Selected)))
                {
                    g.FillPath(fill, path);
                    if (Style == PlateStyle.Thumb) using (var pen = new Pen(Color.FromArgb(alpha, Theme.Current.Control))) g.DrawPath(pen, path);
                }
            g.SmoothingMode = state;
        }
    }
}
