using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // One choice in a PickMenu; Group starts a new captioned part of the list.
    internal sealed class PickItem
    {
        public string Group, Text, Note;
        public object Value;
        public bool Selected;
    }
    // A filter's list: large rows, the chosen one ticked, counts on the right and a search field once the list is long.
    // A drop-down rather than a form, so the main window keeps its active title bar.
    internal sealed class PickMenu : ToolStripDropDown
    {
        private readonly PickList list;
        private readonly ToolStripControlHost host;
        public event Action<object> Chosen;
        public PickMenu(string searchCue)
        {
            AutoClose = true; DropShadowEnabled = true; Padding = Padding.Empty; Margin = Padding.Empty; AutoSize = false;
            list = new PickList(searchCue); list.Chosen += value => { Close(ToolStripDropDownCloseReason.ItemClicked); Chosen?.Invoke(value); };
            list.Dismissed += () => Close(ToolStripDropDownCloseReason.Keyboard);
            host = new ToolStripControlHost(list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false };
            Items.Add(host); Motion.FadeIn(this);
        }
        internal PickList ListForQa { get { return list; } }
        public void Show(Control anchor, IEnumerable<PickItem> items, float scale, float zoom, int minimumWidth)
        {
            list.Fill(items.ToList(), scale, zoom, minimumWidth);
            host.Size = list.Size; Size = list.Size; BackColor = Theme.Current.Raised;
            var old = Region; Region = new Region(Rounded(list.Size, (int)Math.Round(Kit.RadiusLarge * scale))); old?.Dispose();
            var at = anchor.PointToScreen(new Point(0, anchor.Height + (int)Math.Round(6 * scale)));
            var screen = Screen.FromControl(anchor).WorkingArea;
            if (at.X + Width > screen.Right) at.X = Math.Max(screen.Left, anchor.PointToScreen(new Point(anchor.Width, 0)).X - Width);
            if (at.Y + Height > screen.Bottom) at.Y = Math.Max(screen.Top, anchor.PointToScreen(Point.Empty).Y - Height - (int)Math.Round(6 * scale));
            base.Show(at);
            list.Enter();
        }
        private static GraphicsPath Rounded(Size size, int radius) { return Theme.Rounded(new RectangleF(0, 0, size.Width, size.Height), radius); }
    }
    internal sealed class PickList : Control
    {
        private sealed class Line { public PickItem Item; public string Caption; public int Top, Height; }
        private readonly TextInput search = new TextInput(false);
        private readonly List<Line> lines = new List<Line>();
        private List<PickItem> items = new List<PickItem>();
        private float scale = 1, zoom = 1;
        private int offset, content, hover = -1, current = -1, top;
        // The arrows draw their own place in the list; a tick alone marks the chosen line until then.
        private bool keyed;
        private Font body, strong, caption;
        public event Action<object> Chosen;
        public event Action Dismissed;
        public PickList(string searchCue)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            search.Cue = searchCue; search.Input.AccessibleName = searchCue; search.Input.TextChanged += (s, e) => { Build(); Invalidate(); };
            search.Input.KeyDown += (s, e) => Key(e); Controls.Add(search); AccessibleRole = AccessibleRole.List;
        }
        private int D(int value) { return (int)Math.Round(value * scale); }
        private bool Searching { get { return items.Count > 8; } }
        public void Fill(List<PickItem> values, float newScale, float newZoom, int minimumWidth)
        {
            items = values; scale = newScale; zoom = newZoom; offset = 0; hover = -1; keyed = false;
            body?.Dispose(); strong?.Dispose(); caption?.Dispose();
            body = Theme.Font(Theme.Body * zoom); strong = Theme.Strong(Theme.Body * zoom); caption = Theme.Strong(Theme.Meta * zoom);
            search.Zoom = zoom; search.Font = body; search.Input.Clear(); search.Visible = Searching; BackColor = Theme.Current.Raised;
            var widest = values.Select(v => TextRenderer.MeasureText(v.Text, strong, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + (string.IsNullOrEmpty(v.Note) ? 0 : TextRenderer.MeasureText(v.Note, body).Width + D(14))).DefaultIfEmpty(0).Max();
            var width = Math.Max(minimumWidth, Math.Min(D(460), widest + D(70)));
            var ring = D(TextInput.Ring); top = Searching ? D(12) + D(40) + D(4) : D(6);
            Build();
            var height = Math.Min(D(420), top + content + D(6));
            Size = new Size(width, Math.Max(D(60), height));
            if (Searching) search.SetBounds(D(10) - ring, D(10) - ring, width - D(20) + ring * 2, D(40) + ring * 2);
            current = lines.FindIndex(l => l.Item != null && l.Item.Selected); Reveal(current);
        }
        // The self-test picks an offered line as a click on it would.
        internal void ChooseForQa(object value) { var at = lines.FindIndex(l => l.Item != null && Equals(l.Item.Value, value)); if (at >= 0) Chosen?.Invoke(lines[at].Item.Value); }
        public void Enter()
        {
            if (Searching) search.Input.Focus(); else Focus();
        }
        private void Build()
        {
            lines.Clear(); var y = 0; var query = (search.Input.Text ?? "").Trim();
            string group = null;
            foreach (var item in items)
            {
                if (query.Length > 0 && item.Text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                if (item.Group != null && item.Group != group) { group = item.Group; lines.Add(new Line { Caption = group, Top = y, Height = D(30) }); y += D(30); }
                lines.Add(new Line { Item = item, Top = y, Height = D(38) }); y += D(38);
            }
            content = y; offset = Math.Max(0, Math.Min(offset, MaxOffset)); current = hover = -1;
            if (query.Length > 0) { current = lines.FindIndex(l => l.Item != null); keyed = true; }
        }
        private int MaxOffset { get { return Math.Max(0, content - (Height - top - D(6))); } }
        private int LineAt(Point at)
        {
            if (at.Y < top) return -1;
            var y = at.Y - top + offset;
            return lines.FindIndex(l => l.Item != null && y >= l.Top && y < l.Top + l.Height);
        }
        private void Reveal(int index)
        {
            if (index < 0) return;
            var line = lines[index]; var room = Height - top - D(6);
            if (line.Top < offset) offset = line.Top; else if (line.Top + line.Height > offset + room) offset = line.Top + line.Height - room;
            Invalidate();
        }
        private void Move(int delta)
        {
            var at = current;
            for (var n = 0; n < lines.Count; n++)
            {
                at = (at + delta + lines.Count) % Math.Max(1, lines.Count);
                if (lines[at].Item != null) { current = at; keyed = true; Reveal(at); return; }
            }
        }
        private void Key(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down) { Move(1); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Up) { Move(-1); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { if (current >= 0) Chosen?.Invoke(lines[current].Item.Value); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { Dismissed?.Invoke(); e.Handled = e.SuppressKeyPress = true; }
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Enter || keyData == Keys.Escape || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) { Key(e); base.OnKeyDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { var at = LineAt(e.Location); if (at != hover) { hover = at; Invalidate(); } Cursor = at >= 0 ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { var at = LineAt(e.Location); if (e.Button == MouseButtons.Left && at >= 0) Chosen?.Invoke(lines[at].Item.Value); base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { offset = Math.Max(0, Math.Min(MaxOffset, offset - Math.Sign(e.Delta) * D(76))); hover = LineAt(e.Location); Invalidate(); base.OnMouseWheel(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var palette = Theme.Current; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(palette.Raised);
            var view = new Rectangle(0, top, Width, Height - top - D(6)); g.SetClip(view);
            if (lines.Count == 0) TextRenderer.DrawText(g, "—", body, view, palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i]; var y = top + line.Top - offset;
                if (y + line.Height < top || y > view.Bottom) continue;
                if (line.Item == null)
                {
                    TextRenderer.DrawText(g, line.Caption, caption, new Rectangle(D(16), y, Width - D(32), line.Height - D(4)), palette.Muted, TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    continue;
                }
                var row = new RectangleF(D(6), y + D(1), Width - D(12), line.Height - D(2));
                if (i == hover || (keyed && i == current))
                    using (var path = Theme.Rounded(row, D(Kit.RadiusMedium))) using (var fill = new SolidBrush(i == hover ? palette.Hover : palette.Control)) g.FillPath(fill, path);
                var ink = line.Item.Selected ? palette.AccentInk : palette.Ink;
                var right = Width - D(16);
                if (line.Item.Selected) { Icons.Draw(g, UiIcon.Check, new RectangleF(right - D(16), y + (line.Height - D(16)) / 2f, D(16), D(16)), palette.AccentInk); right -= D(24); }
                if (!string.IsNullOrEmpty(line.Item.Note))
                {
                    var note = TextRenderer.MeasureText(line.Item.Note, body, Size.Empty, TextFormatFlags.NoPadding).Width;
                    TextRenderer.DrawText(g, line.Item.Note, body, new Rectangle(right - note, y, note, line.Height), palette.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    right -= note + D(12);
                }
                TextRenderer.DrawText(g, line.Item.Text, line.Item.Selected ? strong : body, new Rectangle(D(16), y, Math.Max(D(40), right - D(16)), line.Height), ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            g.ResetClip();
            if (content > view.Height)
            {
                var track = view.Height - D(8); var thumb = Math.Max(D(24), track * view.Height / content); var y = view.Top + D(4) + (track - thumb) * offset / Math.Max(1, MaxOffset);
                using (var path = Theme.Rounded(new RectangleF(Width - D(6), y, D(3), thumb), D(2))) using (var fill = new SolidBrush(palette.Control)) g.FillPath(fill, path);
            }
            using (var path = Theme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), D(Kit.RadiusLarge))) using (var pen = new Pen(palette.Control)) g.DrawPath(pen, path);
        }
        protected override void Dispose(bool disposing) { if (disposing) { body?.Dispose(); strong?.Dispose(); caption?.Dispose(); } base.Dispose(disposing); }
    }
}
