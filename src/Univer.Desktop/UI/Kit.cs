using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // The stageshow kit's small parts for a native window. Sizes, radii and timing are the kit's; colours stay the app's.
    internal static class Kit
    {
        public const int RadiusSmall = 6, RadiusMedium = 8, RadiusLarge = 10, RadiusCard = 12;
        // Whether a change may fade: the control is on screen and the pointer drives. Motion.Run decides for the app's setting.
        public static bool Fading(Control control) { return control.IsHandleCreated && control.Visible && !Motion.KeyboardInput; }
        // Whether something may travel; Windows' reduced motion stops that, not the fades.
        public static bool Moving(Control control) { return Fading(control) && Motion.Enabled && !Motion.Reduced; }
        // A new child window is black until its first paint: paints it and everything in it before Windows shows the frame.
        public static void PaintNow(Control control)
        {
            if (!control.Visible || !control.IsHandleCreated) return;
            control.Update();
            foreach (Control child in control.Controls) PaintNow(child);
        }
        // A pending arc on a quiet track. The caller supplies the turn, so one timer can drive several.
        public static void Spinner(Graphics g, RectangleF area, float turn, Color color, float stroke)
        {
            var state = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = RectangleF.Inflate(area, -stroke / 2, -stroke / 2);
            using (var track = new Pen(Color.FromArgb(50, color), stroke)) g.DrawEllipse(track, box);
            using (var arc = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, box, turn * 360, 100);
            g.SmoothingMode = state;
        }
        // A usage bar: 6 px, fully rounded, its fill ending on a round cap.
        public static void Meter(Graphics g, RectangleF bar, float share, Color tone, Color track)
        {
            var state = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(bar, bar.Height / 2)) using (var fill = new SolidBrush(track)) g.FillPath(fill, path);
            var width = Math.Max(bar.Height, bar.Width * Math.Max(0, Math.Min(1, share)));
            using (var path = Theme.Rounded(new RectangleF(bar.X, bar.Y, width, bar.Height), bar.Height / 2)) using (var fill = new SolidBrush(tone)) g.FillPath(fill, path);
            g.SmoothingMode = state;
        }
        // Keycaps for a shortcut: each key on its own small cap, the row returning its width. Null graphics only measures.
        public static int Keys(Graphics g, string combo, Font font, Point at, Color ink, Color fill, Color edge, float scale)
        {
            var x = at.X; var height = (int)Math.Round(18 * scale); var gap = (int)Math.Round(3 * scale);
            foreach (var key in combo.Split('+'))
            {
                var text = TextRenderer.MeasureText(key, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                var width = Math.Max(height, text.Width + (int)Math.Round(8 * scale));
                if (g != null)
                {
                    var cap = new RectangleF(x + .5f, at.Y + .5f, width - 1, height - 1);
                    var state = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = Theme.Rounded(cap, 4 * scale)) using (var brush = new SolidBrush(fill)) using (var pen = new Pen(edge)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
                    g.SmoothingMode = state;
                    TextRenderer.DrawText(g, key, font, new Rectangle(x, at.Y, width, height), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
                x += width + gap;
            }
            return x - gap - at.X;
        }
        // The chip a file travels as: its type on a small tile and its name, on the raised surface.
        public static Bitmap FileChip(string name, string kind, Color tone, float scale)
        {
            using (var font = Theme.Font(Theme.Meta)) using (var small = Theme.Strong(Theme.Caption))
            {
                if (name.Length > 28) name = name.Substring(0, 27) + "…";
                var text = TextRenderer.MeasureText(name, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int pad = (int)(6 * scale), tile = (int)(24 * scale), height = tile + pad * 2, width = pad + tile + (int)(8 * scale) + text.Width + (int)(12 * scale);
                var picture = new Bitmap(width + 2, height + 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(picture))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (var path = Theme.Rounded(new RectangleF(1, 1, width - 1, height - 1), Math.Min(height / 2f, 10 * scale)))
                    using (var fill = new SolidBrush(Theme.Current.Raised)) using (var edge = new Pen(Theme.Current.Control)) { g.FillPath(fill, path); g.DrawPath(edge, path); }
                    var box = new RectangleF(1 + pad, 1 + pad, tile, tile);
                    using (var path = Theme.Rounded(box, RadiusSmall * scale)) using (var fill = new SolidBrush(Color.FromArgb(32, tone))) g.FillPath(fill, path);
                    using (var mark = new SolidBrush(tone)) using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(kind, small, mark, box, center);
                    using (var ink = new SolidBrush(Theme.Ink)) g.DrawString(name, font, ink, 1 + pad + tile + 8 * scale, 1 + (height - text.Height) / 2f - 1);
                }
                return picture;
            }
        }
    }
    // A switch in place of a check box: a setting is on or off now. The words sit left, the switch on the row's right edge.
    internal sealed class Toggle : CheckBox
    {
        private float level;
        private Tween move;
        private float zoom = 1;
        public float Zoom { get { return zoom; } set { zoom = value; Invalidate(); } }
        // One short line under the name; the whole row answers the pointer.
        private string hint = "";
        private bool hovered;
        public string Hint { get { return hint; } set { hint = value ?? ""; AccessibleDescription = hint; Invalidate(); } }
        internal bool FitsForQa
        {
            get
            {
                var room = Width - (int)(42 * Theme.DpiScale * zoom) - 2; var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                return TextRenderer.MeasureText(Text, Font, Size.Empty, flags).Width <= room && TextRenderer.MeasureText(hint, HintFont, Size.Empty, flags).Width <= room;
            }
        }
        // Follows the name's size, so the app's zoom needs nothing more.
        private Font hintFont;
        private Font HintFont
        {
            get
            {
                var size = Font.Size * Theme.Meta / Theme.Body;
                if (hintFont == null || Math.Abs(hintFont.Size - size) > .01f) { hintFont?.Dispose(); hintFont = Theme.Font(size); }
                return hintFont;
            }
        }
        public Toggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false; Cursor = Cursors.Hand; BackColor = Theme.Surface;
        }
        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            move?.Cancel(); var from = level; var to = Checked ? 1f : 0f;
            move = Motion.Run(Kit.Moving(this) ? 250 : 0, t => { level = Motion.Lerp(from, to, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Behind(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = g.DpiX / 96f * zoom; float width = 30 * scale, height = 18 * scale, knob = 14 * scale, inset = 2 * scale;
            var track = new RectangleF(Width - width - 2, (Height - height) / 2, width, height);
            using (var path = Theme.Rounded(track, height / 2)) using (var fill = new SolidBrush(Motion.Mix(hovered ? Theme.Current.Muted : Theme.Current.Control, hovered ? Theme.AccentHover : Theme.Accent, level))) g.FillPath(fill, path);
            var x = track.X + inset + (width - knob - inset * 2) * level;
            using (var fill = new SolidBrush(Color.White)) g.FillEllipse(fill, x, track.Y + inset, knob, knob);
            var ink = Enabled ? Theme.Ink : Theme.Current.Disabled; var words = (int)track.X - (int)(12 * scale); var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            if (hint.Length == 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, words, Height), ink, flags);
            else
            {
                int title = (int)(20 * scale), line = (int)(18 * scale), top = (Height - title - line) / 2;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, top, words, title), ink, flags);
                TextRenderer.DrawText(g, hint, HintFont, new Rectangle(0, top + title, words, line), Theme.Muted, flags);
            }
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(RectangleF.Inflate(track, 1.5f, 1.5f), height / 2 + 1.5f)) using (var pen = new Pen(Theme.AccentInk, 1.5f)) g.DrawPath(pen, path);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void Dispose(bool disposing) { if (disposing) { move?.Cancel(); hintFont?.Dispose(); } base.Dispose(disposing); }
    }
    // Text that changes in place: only the changed span rolls, whatever follows moves by its measured width, and the words
    // that stayed never stir. A number changes digit by digit the same way.
    internal class RollLabel : Label
    {
        private string before = "";
        private float level = 1;
        private Tween roll;
        public int Duration { get; set; } = 300;
        public override string Text
        {
            get { return base.Text; }
            set
            {
                var old = base.Text; base.Text = value;
                if (old == (value ?? "")) return;
                roll?.Cancel();
                if (old.Length == 0 || string.IsNullOrEmpty(value) || !Kit.Moving(this)) { level = 1; return; }
                before = old;
                roll = Motion.Run(Duration, t => { level = Motion.EaseOut(t); if (!IsDisposed) Invalidate(); }, () => { level = 1; if (!IsDisposed) Invalidate(); });
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (level >= 1) { base.OnPaint(e); return; }
            var g = e.Graphics; var now = Text; var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int head = 0; while (head < now.Length && head < before.Length && now[head] == before[head]) head++;
            int tail = 0; while (tail < now.Length - head && tail < before.Length - head && now[now.Length - 1 - tail] == before[before.Length - 1 - tail]) tail++;
            string start = now.Substring(0, head), end = tail > 0 ? now.Substring(now.Length - tail) : "", oldMiddle = before.Substring(head, before.Length - head - tail), newMiddle = now.Substring(head, now.Length - head - tail);
            Func<string, int> width = s => s.Length == 0 ? 0 : TextRenderer.MeasureText(g, s, Font, Size.Empty, flags).Width;
            int startWidth = width(start), oldWidth = width(oldMiddle), newWidth = width(newMiddle), endWidth = width(end);
            var line = TextRenderer.MeasureText(g, "0", Font, Size.Empty, flags).Height;
            var total = startWidth + Motion.Lerp(oldWidth, newWidth, level) + endWidth;
            if (total > Width) { base.OnPaint(e); return; }
            var right = TextAlign == ContentAlignment.TopRight || TextAlign == ContentAlignment.MiddleRight || TextAlign == ContentAlignment.BottomRight;
            var center = TextAlign == ContentAlignment.TopCenter || TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.BottomCenter;
            var middle = TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.MiddleRight;
            var x = right ? Width - total : center ? (Width - total) / 2 : 0; var y = middle ? (Height - line) / 2 : 0;
            var ground = BackColor.A == 255 ? BackColor : Theme.Behind(this); var rise = (int)Math.Round(line * .6f);
            TextRenderer.DrawText(g, start, Font, new Point(x, y), ForeColor, flags);
            TextRenderer.DrawText(g, oldMiddle, Font, new Point(x + startWidth, y - (int)Math.Round(rise * level)), Motion.Mix(ForeColor, ground, level), flags);
            TextRenderer.DrawText(g, newMiddle, Font, new Point(x + startWidth, y + (int)Math.Round(rise * (1 - level))), Motion.Mix(ground, ForeColor, level), flags);
            TextRenderer.DrawText(g, end, Font, new Point(x + startWidth + Motion.Lerp(oldWidth, newWidth, level), y), ForeColor, flags);
        }
        protected override void Dispose(bool disposing) { if (disposing) roll?.Cancel(); base.Dispose(disposing); }
    }
}
