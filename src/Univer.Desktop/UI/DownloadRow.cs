using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // One file in "Загрузки": type badge, name, state line and the actions view, external app, folder and remove.
    internal sealed class DownloadRow : Panel
    {
        public event Action<DownloadRecord> OpenInside, OpenOutside, ShowInFolder, CancelDownload, RemoveRecord;
        private readonly float scale;
        private readonly Label name = new Label(), meta = new Label();
        private readonly ModernButton open = new ModernButton(), outside = new ModernButton(), folder = new ModernButton(), remove = new ModernButton();
        private readonly ToolTip tips = new ToolTip();
        private DownloadRecord item;
        private string language;
        // Control.Visible reads false while the downloads page is hidden, so layout uses the row's own state:
        // a file finished in the background still gets its action buttons placed.
        private bool actions;
        // One badge font for every row and repaint; progress updates repaint running rows several times a second.
        private static Font badgeFont;
        public DownloadRow(float scale, string language)
        {
            this.scale = scale; this.language = language;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            // The card's colour: buttons round their corners into it. Only the corners outside the card show the page.
            BackColor = Theme.Surface; Theme.Tips(tips);
            name.Font = Theme.Strong(Theme.Body); name.ForeColor = Theme.Ink; name.AutoEllipsis = true; name.UseMnemonic = false; name.BackColor = Theme.Surface;
            meta.Font = Theme.Font(Theme.Meta); meta.ForeColor = Theme.Muted; meta.AutoEllipsis = true; meta.UseMnemonic = false; meta.BackColor = Theme.Surface;
            open.Look = ButtonLook.Primary; outside.Look = ButtonLook.Outline; outside.Icon = UiIcon.External;
            folder.Look = ButtonLook.Outline; folder.Icon = UiIcon.Folder; folder.Compact = true; remove.Icon = UiIcon.Close; remove.Compact = true;
            foreach (var button in new[] { open, outside, folder, remove }) { button.BackColor = Theme.Surface; button.Font = Theme.Font(Theme.Meta); }
            open.Click += (s, e) => { if (item != null) OpenInside?.Invoke(item); };
            outside.Click += (s, e) => { if (item != null) OpenOutside?.Invoke(item); };
            folder.Click += (s, e) => { if (item != null) ShowInFolder?.Invoke(item); };
            remove.Click += (s, e) => { if (item == null) return; if (item.State == DownloadState.Running) CancelDownload?.Invoke(item); else RemoveRecord?.Invoke(item); };
            foreach (var control in new Control[] { name, meta, open, outside, folder, remove }) Controls.Add(control);
            Resize += (s, e) => Arrange();
        }
        private int D(int value) { return (int)Math.Round(value * scale); }
        public void SetLanguage(string value) { language = value; if (item != null) Update(item); }
        public void Update(DownloadRecord value)
        {
            item = value;
            var exists = item.State == DownloadState.Completed && File.Exists(item.Path);
            name.Text = string.IsNullOrEmpty(item.FileName) ? "…" : item.FileName;
            meta.Text = Status(exists) + " · " + (item.Finished ?? item.Started).ToString("dd.MM.yyyy HH:mm") + (string.IsNullOrEmpty(item.Source) ? "" : " · " + item.Source);
            open.Text = Strings.Get("openInside", language); outside.Text = Strings.Get("openOutside", language);
            actions = exists; open.Visible = outside.Visible = folder.Visible = exists;
            outside.Enabled = exists && FileNames.CanOpen(item.Path);
            remove.AccessibleName = Strings.Get(item.State == DownloadState.Running ? "cancelDownload" : "removeFromList", language);
            tips.SetToolTip(remove, remove.AccessibleName); tips.SetToolTip(folder, Strings.Get("showInFolder", language)); folder.AccessibleName = Strings.Get("showInFolder", language);
            open.AccessibleName = open.Text + ": " + name.Text; outside.AccessibleName = outside.Text + ": " + name.Text;
            tips.SetToolTip(outside, outside.Text);
            AccessibleName = name.Text; Arrange(); Invalidate();
        }
        private string Status(bool exists)
        {
            if (item.State == DownloadState.Running)
                return Strings.Get("stateRunning", language) + " " + (item.Total > 0 ? (item.Received * 100 / Math.Max(1, item.Total)) + "% · " + FileKinds.Size(item.Received, language) + " / " + FileKinds.Size(item.Total, language) : FileKinds.Size(item.Received, language));
            if (item.State == DownloadState.Completed) return exists ? FileKinds.Size(item.Total >= 0 ? item.Total : item.Received, language) : Strings.Get("stateMissing", language);
            return Strings.Get(item.State == DownloadState.Canceled ? "stateCanceled" : "stateFailed", language);
        }
        private void Arrange()
        {
            // Actions keep one order for every file type: view, external app, folder, remove.
            // A narrow window folds the long external-app label into its icon; the tooltip keeps the full name.
            var right = Width - D(14); var height = D(34); var top = (Height - height) / 2; var left = D(66);
            remove.SetBounds(right - D(34), top, D(34), height); right = remove.Left - D(6);
            if (actions) { folder.SetBounds(right - D(38), top, D(38), height); right = folder.Left - D(8); }
            var viewWidth = actions ? Math.Max(D(96), TextRenderer.MeasureText(open.Text, open.Font).Width + D(30)) + D(12) : 0;
            var outsideWidth = TextRenderer.MeasureText(outside.Text, outside.Font).Width + D(50);
            outside.Compact = actions && right - outsideWidth - D(8) - viewWidth - left < D(280);
            if (outside.Compact) outsideWidth = D(38);
            if (actions) { outside.SetBounds(right - outsideWidth, top, outsideWidth, height); right = outside.Left - D(8); }
            if (actions) { var width = viewWidth - D(12); open.SetBounds(right - width, top, width, height); right = open.Left - D(12); }
            name.SetBounds(left, D(11), Math.Max(D(40), right - left), D(24));
            meta.SetBounds(left, D(35), Math.Max(D(40), right - left), D(20));
        }
        internal bool ActionsPlacedForQa { get { return actions && open.Left > meta.Left && open.Right <= outside.Left && outside.Right <= folder.Left && folder.Right <= remove.Left && open.Bottom <= Height; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Behind(this));
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), D(12)))
            {
                using (var fill = new SolidBrush(Theme.Surface)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            if (item == null) return;
            var kind = FileKinds.Of(item.FileName);
            var color = BadgeColor(kind);
            var badge = new RectangleF(D(14), (Height - D(40)) / 2f, D(40), D(40));
            using (var path = Theme.Rounded(badge, D(9))) using (var fill = new SolidBrush(Color.FromArgb(28, color))) g.FillPath(fill, path);
            if (badgeFont == null) badgeFont = Theme.Strong(8);
            TextRenderer.DrawText(g, FileKinds.Badge(kind), badgeFont, Rectangle.Round(badge), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (item.State == DownloadState.Running)
            {
                var bar = new RectangleF(D(66), Height - D(9), Math.Max(D(40), (actions ? open.Left : remove.Left) - D(78)), D(4));
                using (var track = new SolidBrush(Theme.Border)) g.FillRectangle(track, bar);
                var share = item.Total > 0 ? Math.Min(1f, item.Received / (float)item.Total) : 0.3f;
                using (var fill = new SolidBrush(Theme.Accent)) g.FillRectangle(fill, bar.X, bar.Y, bar.Width * share, bar.Height);
            }
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
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
