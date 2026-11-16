using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // What every row shares, so a zoom step changes one object instead of a font per row.
    internal sealed class RowStyle : IDisposable
    {
        public float Scale { get; private set; }
        public Font Name { get; private set; }
        public Font Meta { get; private set; }
        public Font Badge { get; private set; }
        public Font Heading { get; private set; }
        public ToolTip Tips { get; }
        public RowStyle(ToolTip tips) { Tips = tips; }
        public int D(int value) { return (int)Math.Round(value * Scale); }
        public void Set(float scale, float zoom)
        {
            Dispose(); Scale = scale;
            Name = Theme.Strong(Theme.Body * zoom); Meta = Theme.Font(Theme.Meta * zoom); Badge = Theme.Strong((Theme.Caption - 1) * zoom); Heading = Theme.Strong(Theme.Meta * zoom);
        }
        public void Dispose() { Name?.Dispose(); Meta?.Dispose(); Badge?.Dispose(); Heading?.Dispose(); }
    }
    // One file in "Загрузки". The whole row opens the file; the rarer actions (external app, folder, remove) replace its
    // size and date under the pointer, so the list stays calm. It is painted as one control: 60 files are 60 windows, not
    // 400, which keeps zooming and scrolling light. While it runs a meter fills along the row and the cancel stays visible.
    internal sealed class DownloadRow : Control
    {
        public event Action<DownloadRecord> OpenInside, OpenOutside, ShowInFolder, CancelDownload, RemoveRecord;
        public event Action<DownloadRow, int> Step;
        public event Action<DownloadRow, Point> MenuRequested;
        private enum Part { None, Outside, Folder, Remove }
        private struct Action2 { public Part Part; public UiIcon Icon; public Rectangle Area; }
        private readonly RowStyle style;
        private readonly List<Action2> actions = new List<Action2>();
        private DownloadRecord item;
        private string language, name = "", meta = "", size = "", when = "";
        private bool exists, pointed, pressed;
        private Part hot = Part.None;
        private float hoverLevel, share, shareTarget = -1, arrival = 1;
        private Tween hover, fill, arriving;
        private Bitmap picture;
        // Rows of one date group read as one sheet: only its ends are rounded.
        public string Heading { get; private set; }
        private bool first = true, last = true;
        public DownloadRow(RowStyle style, string language)
        {
            this.style = style; this.language = language;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; AccessibleRole = AccessibleRole.ListItem; Cursor = Cursors.Hand; BackColor = Theme.Background;
        }
        private int D(int value) { return style.D(value); }
        public DownloadRecord Item { get { return item; } }
        public int WantedHeight { get { return D(58) + (Heading != null ? D(36) : 0); } }
        private int Top0 { get { return Heading != null ? D(36) : 0; } }
        public bool Place(string heading, bool isFirst, bool isLast)
        {
            var changed = heading != Heading || first != isFirst || last != isLast;
            Heading = heading; first = isFirst; last = isLast;
            if (changed) { Arrange(); Invalidate(); }
            return changed;
        }
        public void Restyle() { Arrange(); Invalidate(); }
        // A reused row starts clean: the pointer that was over it may never have said it left.
        public void Recycle()
        {
            hover?.Cancel(); fill?.Cancel(); arriving?.Cancel(); picture?.Dispose(); picture = null;
            pointed = pressed = false; hot = Part.None; hoverLevel = share = 0; shareTarget = -1; arrival = 1; item = null;
        }
        public void SetLanguage(string value) { language = value; if (item != null) Update(item); }
        // recheck: look at the disk even though the record is the same.
        public void Update(DownloadRecord value, bool recheck = false)
        {
            var known = item != null && item.Id == value.Id && item.State == value.State && item.Path == value.Path;
            item = value;
            if (!known || recheck) exists = item.State == DownloadState.Completed && File.Exists(item.Path);
            name = string.IsNullOrEmpty(item.FileName) ? "…" : item.FileName;
            var running = item.State == DownloadState.Running;
            var source = string.IsNullOrEmpty(item.Source) ? "" : item.Source;
            meta = running ? Status() : exists ? (source.Length > 0 ? source : Strings.Get("downloadsNoSource", language)) : Status() + (source.Length > 0 ? " · " + source : "");
            size = exists ? FileKinds.Size(item.Total >= 0 ? item.Total : item.Received, language) : "";
            when = (item.Finished ?? item.Started).ToString("dd.MM.yyyy HH:mm");
            Cursor = exists ? Cursors.Hand : Cursors.Default;
            AccessibleName = name; AccessibleDescription = meta + (size.Length > 0 ? ", " + size : "") + ", " + when;
            AccessibleDefaultActionDescription = Strings.Get("openInside", language);
            Arrange(); Fill(); Invalidate();
        }
        private string Status()
        {
            if (item.State == DownloadState.Running)
                return Strings.Get("stateRunning", language) + " " + (item.Total > 0 ? (item.Received * 100 / Math.Max(1, item.Total)) + "% · " + FileKinds.Size(item.Received, language) + " / " + FileKinds.Size(item.Total, language) : FileKinds.Size(item.Received, language));
            if (item.State == DownloadState.Completed) return Strings.Get("stateMissing", language);
            return Strings.Get(item.State == DownloadState.Canceled ? "stateCanceled" : "stateFailed", language);
        }
        // The meter travels to each new share instead of stepping with every progress report.
        private void Fill()
        {
            var running = item.State == DownloadState.Running;
            var target = !running ? -1 : item.Total > 0 ? Math.Min(1f, item.Received / (float)item.Total) : .3f;
            if (Math.Abs(target - shareTarget) < .001f) return;
            shareTarget = target; fill?.Cancel(); if (!running) return;
            var from = share;
            fill = Motion.Run(Kit.Moving(this) ? 800 : 0, t => { share = Motion.Lerp(from, target, Motion.Travel(t)); if (!IsDisposed) Invalidate(); });
        }
        private void Arrange()
        {
            actions.Clear(); if (item == null) return;
            var box = D(32); var right = Width - D(12); var top = Top0 + (D(58) - box) / 2;
            Action<Part, UiIcon> add = (part, icon) => { right -= box; actions.Add(new Action2 { Part = part, Icon = icon, Area = new Rectangle(right, top, box, box) }); right -= D(2); };
            add(Part.Remove, UiIcon.Close);
            if (exists) { add(Part.Folder, UiIcon.Folder); if (FileNames.CanOpen(item.Path)) add(Part.Outside, UiIcon.External); }
        }
        protected override void OnResize(EventArgs e) { Arrange(); base.OnResize(e); }
        internal bool ActionsPlacedForQa { get { return exists && actions.Count >= 2 && actions.TrueForAll(a => a.Area.Right <= Width && a.Area.Left >= Width / 2 && a.Area.Bottom <= Height); } }
        private void Hover(bool on)
        {
            if (pointed == on) return;
            pointed = on; hover?.Cancel(); var from = hoverLevel; var to = on ? 1f : 0f;
            hover = Motion.Run(Kit.Moving(this) ? 140 : 0, t => { hoverLevel = Motion.Lerp(from, to, t); if (!IsDisposed) Invalidate(); });
        }
        private Part PartAt(Point at)
        {
            foreach (var action in actions) if (action.Area.Contains(at)) return action.Part;
            return Part.None;
        }
        private string PartName(Part part)
        {
            switch (part)
            {
                case Part.Outside: return Strings.Get("openOutside", language);
                case Part.Folder: return Strings.Get("showInFolder", language);
                case Part.Remove: return Strings.Get(item?.State == DownloadState.Running ? "cancelDownload" : "removeFromList", language);
                default: return "";
            }
        }
        private bool InCard(Point at) { return at.Y >= Top0; }
        protected override void OnMouseEnter(EventArgs e) { Hover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover(false); SetHot(Part.None); base.OnMouseLeave(e); }
        private void SetHot(Part part)
        {
            if (hot == part) return;
            hot = part; style.Tips.SetToolTip(this, PartName(part)); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Hover(InCard(e.Location)); SetHot(PartAt(e.Location));
            Cursor = !InCard(e.Location) ? Cursors.Default : hot != Part.None || exists ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e) { Motion.KeyboardInput = false; if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            var wasPressed = pressed; pressed = false; Invalidate();
            if (e.Button == MouseButtons.Right && InCard(e.Location)) MenuRequested?.Invoke(this, e.Location);
            else if (e.Button == MouseButtons.Left && wasPressed && InCard(e.Location) && ClientRectangle.Contains(e.Location)) Perform(PartAt(e.Location));
            base.OnMouseUp(e);
        }
        private void Perform(Part part)
        {
            if (item == null) return;
            switch (part)
            {
                case Part.Outside: OpenOutside?.Invoke(item); break;
                case Part.Folder: ShowInFolder?.Invoke(item); break;
                case Part.Remove: Remove(); break;
                default: Open(); break;
            }
        }
        public bool CanOpen { get { return exists; } }
        public bool CanOpenOutside { get { return exists && FileNames.CanOpen(item.Path); } }
        public void Open() { if (exists) OpenInside?.Invoke(item); }
        public void OpenElsewhere() { if (CanOpenOutside) OpenOutside?.Invoke(item); }
        public void Reveal() { if (exists) ShowInFolder?.Invoke(item); }
        public void Remove() { if (item == null) return; if (item.State == DownloadState.Running) CancelDownload?.Invoke(item); else RemoveRecord?.Invoke(item); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Enter || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            Motion.KeyboardInput = true;
            if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None) { Open(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None) { Remove(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { Step?.Invoke(this, e.KeyCode == Keys.Up ? -1 : 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift)) { MenuRequested?.Invoke(this, new Point(D(58), Top0 + D(44))); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        // A new row arrives whole: its picture fades in over its own place while the rows below make room.
        public void Arrive()
        {
            if (!Kit.Moving(this) || Width < 2 || Height < 2) return;
            picture = MotionPopup.Capture(this);
            if (picture == null) return;
            arrival = 0; arriving?.Cancel();
            arriving = Motion.Run(320, t => { arrival = Motion.Arrive(t); if (!IsDisposed) Invalidate(); }, () => { arrival = 1; picture?.Dispose(); picture = null; if (!IsDisposed) Invalidate(); });
        }
        private GraphicsPath Sheet(RectangleF area, float radius)
        {
            var path = new GraphicsPath(); var d = radius * 2;
            if (first) { path.AddArc(area.Left, area.Top, d, d, 180, 90); path.AddArc(area.Right - d, area.Top, d, d, 270, 90); }
            else { path.AddLine(area.Left, area.Top, area.Right, area.Top); }
            if (last) { path.AddArc(area.Right - d, area.Bottom - d, d, d, 0, 90); path.AddArc(area.Left, area.Bottom - d, d, d, 90, 90); }
            else { path.AddLine(area.Right, area.Bottom, area.Left, area.Bottom); }
            path.CloseFigure(); return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Behind(this));
            if (picture != null)
            {
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = arrival });
                    var drop = (int)Math.Round(-6 * style.Scale * (1 - arrival));
                    g.DrawImage(picture, new Rectangle(0, drop, Width, Height), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel, attributes);
                }
                return;
            }
            var palette = Theme.Current; var top = Top0;
            if (Heading != null)
                TextRenderer.DrawText(g, Heading, style.Heading, new Rectangle(D(4), 0, Width - D(8), top - D(6)), palette.Muted, TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            var ground = Motion.Mix(palette.Surface, palette.Hover, hoverLevel);
            if (pressed && hot == Part.None) ground = Motion.Mix(ground, palette.Control, .35f);
            // The rows of a group share one outline: only the group's ends close it.
            var area = new RectangleF(.5f, top + (first ? .5f : 0), Width - 1, Height - top - (first ? .5f : 0) - (last ? 1f : 0)); float radius = D(Kit.RadiusLarge);
            using (var path = Sheet(area, radius)) using (var brush = new SolidBrush(ground)) g.FillPath(brush, path);
            using (var pen = new Pen(palette.Line))
            {
                var from = area.Top + (first ? radius : 0); var to = area.Bottom - (last ? radius : 0);
                g.DrawLine(pen, area.Left, from, area.Left, to); g.DrawLine(pen, area.Right, from, area.Right, to);
                if (first) { g.DrawArc(pen, area.Left, area.Top, radius * 2, radius * 2, 180, 90); g.DrawLine(pen, area.Left + radius, area.Top, area.Right - radius, area.Top); g.DrawArc(pen, area.Right - radius * 2, area.Top, radius * 2, radius * 2, 270, 90); }
                if (last) { g.DrawArc(pen, area.Left, area.Bottom - radius * 2, radius * 2, radius * 2, 90, 90); g.DrawLine(pen, area.Left + radius, area.Bottom, area.Right - radius, area.Bottom); g.DrawArc(pen, area.Right - radius * 2, area.Bottom - radius * 2, radius * 2, radius * 2, 0, 90); }
            }
            if (item == null) return;
            // The rows of a sheet are divided under their text, not across the badge column.
            var kind = FileKinds.Of(item.FileName); var color = BadgeColor(kind);
            var badge = new RectangleF(D(14), top + (D(58) - D(32)) / 2f, D(32), D(32));
            using (var path = Theme.Rounded(badge, D(Kit.RadiusMedium))) using (var brush = new SolidBrush(Color.FromArgb(28, color))) g.FillPath(brush, path);
            TextRenderer.DrawText(g, FileKinds.Badge(kind), style.Badge, Rectangle.Round(badge), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var left = D(60); var running = item.State == DownloadState.Running;
            var rest = Width - D(16) - (size.Length > 0 || !running ? D(124) : 0);
            var tools = actions.Count > 0 ? actions[actions.Count - 1].Area.Left - D(10) : Width - D(16);
            var textRight = (int)Motion.Lerp((float)rest, tools, running ? 1 : hoverLevel);
            var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            var ink = exists || running ? palette.Ink : palette.Muted;
            TextRenderer.DrawText(g, name, style.Name, new Rectangle(left, top + D(9), Math.Max(D(40), textRight - left), D(21)), ink, flags);
            TextRenderer.DrawText(g, meta, style.Meta, new Rectangle(left, top + D(30), Math.Max(D(40), textRight - left), D(18)), palette.Muted, flags);
            // Size and date rest on the right; under the pointer the same place holds the actions.
            var quiet = running ? 0 : 1 - hoverLevel;
            if (quiet > .01f)
            {
                var tone = Motion.Mix(ground, palette.Muted, quiet); var right = TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                TextRenderer.DrawText(g, size, style.Meta, new Rectangle(Width - D(16) - D(120), top + D(9), D(120), D(21)), tone, right);
                TextRenderer.DrawText(g, when, style.Meta, new Rectangle(Width - D(16) - D(120), top + D(30), D(120), D(18)), tone, right);
            }
            var shown = running ? 1 : hoverLevel;
            if (shown > .01f)
                foreach (var action in actions)
                {
                    if (action.Part == hot)
                        using (var path = Theme.Rounded(action.Area, D(Kit.RadiusMedium))) using (var brush = new SolidBrush(Color.FromArgb((int)(255 * shown), palette.Control))) g.FillPath(brush, path);
                    var icon = RectangleF.Inflate(action.Area, -D(8), -D(8));
                    Icons.Draw(g, action.Icon, icon, Color.FromArgb((int)(255 * shown), action.Part == hot && action.Part == Part.Remove ? palette.Danger : palette.Ink));
                }
            if (running) Kit.Meter(g, new RectangleF(left, Height - D(6), Math.Max(D(40), tools - left), D(3)), share, Theme.Accent, palette.Track);
            else if (!last) using (var line = new SolidBrush(palette.Line)) { g.SmoothingMode = SmoothingMode.None; g.FillRectangle(line, left, Height - 1, Width - left - 1, 1); g.SmoothingMode = SmoothingMode.AntiAlias; }
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(new RectangleF(1.5f, top + 1.5f, Width - 4, Height - top - 4), D(Kit.RadiusMedium))) using (var pen = new Pen(Theme.AccentInk, 1.5f)) g.DrawPath(pen, path);
        }
        private static Color BadgeColor(FileKind kind)
        {
            switch (kind)
            {
                case FileKind.Pdf: return Theme.Current.Kinds[0];
                case FileKind.Word: return Theme.Current.Kinds[1];
                case FileKind.Excel: return Theme.Current.Kinds[2];
                case FileKind.PowerPoint: return Theme.Current.Kinds[3];
                case FileKind.Image: return Theme.Current.Kinds[4];
                default: return Theme.Muted;
            }
        }
        internal static Color Tone(string fileName) { return BadgeColor(FileKinds.Of(fileName)); }
        protected override void Dispose(bool disposing) { if (disposing) { hover?.Cancel(); fill?.Cancel(); arriving?.Cancel(); picture?.Dispose(); } base.Dispose(disposing); }
    }
}
