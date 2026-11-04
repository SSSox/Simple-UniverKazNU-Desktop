using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // The Notifications panel, unread first: a click opens the page that resolves an item and marks it read, the check
    // mark only marks it read. It floats over the page's upper right corner like the find bar; Escape hides it.
    internal sealed class NoticesPanel : Panel
    {
        public event Action<SiteNotice> OpenNotice;
        public event Action<string> ReadNotice;
        public event Action ReadAll, CheckNow, CloseRequested;
        private readonly Label title = new Label(), summary = new Label(), emptyTitle = new Label(), emptyText = new Label();
        private readonly ModernButton refresh = new ModernButton(), readAll = new ModernButton(), close = new ModernButton();
        private readonly Panel list = new SmoothPanel { AutoScroll = true };
        private readonly List<NoticeRow> rows = new List<NoticeRow>();
        private readonly ToolTip tips = new ToolTip { ShowAlways = true };
        private readonly float scale;
        private string language = "ru", drawn;
        // Control.Visible reads false while the panel itself is hidden, so the layout keeps what it shows.
        private bool checking, signedOut, unread, failed;
        private int D(int value) { return (int)Math.Round(value * scale); }
        public NoticesPanel(float scale)
        {
            this.scale = scale;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Visible = false; BackColor = Theme.Surface; AccessibleRole = AccessibleRole.Dialog; Theme.Tips(tips);
            title.Font = Theme.Strong(Theme.Emphasis); title.ForeColor = Theme.Ink; title.AutoEllipsis = true; title.UseMnemonic = false;
            summary.Font = Theme.Font(Theme.Meta); summary.ForeColor = Theme.Muted; summary.AutoEllipsis = true; summary.UseMnemonic = false;
            emptyTitle.Font = Theme.Strong(Theme.Body); emptyTitle.ForeColor = Theme.Ink; emptyTitle.UseMnemonic = false; emptyTitle.TextAlign = ContentAlignment.BottomCenter;
            emptyText.Font = Theme.Font(Theme.Meta); emptyText.ForeColor = Theme.Muted; emptyText.UseMnemonic = false; emptyText.TextAlign = ContentAlignment.TopCenter;
            foreach (var label in new[] { title, summary, emptyTitle, emptyText }) label.BackColor = Theme.Surface;
            refresh.Icon = UiIcon.Reload; close.Icon = UiIcon.Close; refresh.Compact = close.Compact = true;
            readAll.Look = ButtonLook.Quiet; readAll.Font = Theme.Font(Theme.Meta);
            foreach (var button in new[] { refresh, readAll, close }) { button.BackColor = Theme.Surface; Controls.Add(button); }
            list.BackColor = Theme.Surface; list.Controls.Add(emptyTitle); list.Controls.Add(emptyText);
            Controls.Add(title); Controls.Add(summary); Controls.Add(list);
            refresh.Click += (s, e) => CheckNow?.Invoke(); readAll.Click += (s, e) => ReadAll?.Invoke(); close.Click += (s, e) => CloseRequested?.Invoke();
            Resize += (s, e) => Arrange(); list.Resize += (s, e) => LayoutRows();
        }
        public string Language { get { return language; } }
        internal int RowsForQa { get { return rows.Count; } }
        internal string[] RowNamesForQa { get { return rows.Select(r => r.AccessibleName).ToArray(); } }
        internal void ReadFirstForQa() { if (rows.Count > 0) rows[0].PressReadForQa(); }
        internal void ReadAllForQa() { readAll.PerformClick(); }
        internal void CloseForQa() { close.PerformClick(); }
        internal string StatusForQa { get { return summary.Text; } }
        internal Color[] RowColorsForQa { get { return rows.Select(r => r.BackColor).ToArray(); } }
        // Draws the list; rows are rebuilt only when what they show changes, so the reader's focus stays on them.
        public void Render(IList<SiteNotice> notices, string value, bool busy, bool withoutSignIn, bool checkFailed)
        {
            var languageChanged = language != value; language = value; checking = busy; signedOut = withoutSignIn; failed = checkFailed && !busy && !withoutSignIn;
            title.Text = Strings.Get("notices", language); AccessibleName = title.Text;
            var status = checking ? Strings.Get("noticesChecking", language) : failed ? Strings.Get("noticesCheckFailed", language) : "";
            if (status != summary.Text && status.Length > 0 && Visible) AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            summary.Text = status; summary.ForeColor = failed ? Theme.Danger : Theme.Muted; AccessibleDescription = status;
            refresh.Enabled = !checking && !signedOut;
            Tip(refresh, "noticesRefresh"); Tip(close, "noticesClose");
            readAll.Text = readAll.AccessibleName = Strings.Get("noticesReadAll", language);
            unread = notices.Any(n => !n.Read); readAll.Visible = unread;
            emptyTitle.Text = Strings.Get("noticesEmpty", language);
            emptyText.Text = Strings.Get(signedOut ? "noticesSignedOut" : "noticesEmptyText", language);
            var signature = language + "\n" + string.Join("\n", notices.Select(n => n.Key + "|" + n.Title + "|" + Notices.Shown(n) + "|" + n.Read + "|" + n.Text + "|" +
                string.Join(";", n.Details.Select(d => d.Title + "/" + d.Until))));
            if (signature != drawn || languageChanged)
            {
                drawn = signature;
                var focused = rows.FindIndex(r => r.ContainsFocus);
                list.SuspendLayout();
                foreach (var row in rows) { list.Controls.Remove(row); row.Dispose(); }
                rows.Clear();
                foreach (var notice in notices)
                {
                    var row = new NoticeRow(scale, notice, language);
                    row.OpenNotice += item => OpenNotice?.Invoke(item); row.ReadNotice += key => ReadNotice?.Invoke(key);
                    rows.Add(row); list.Controls.Add(row);
                }
                list.ResumeLayout();
                // The row under the reader's focus, or its neighbour once it was marked read and moved down.
                if (focused >= 0 && rows.Count > 0) rows[Math.Min(focused, rows.Count - 1)].Focus();
            }
            emptyTitle.Visible = emptyText.Visible = rows.Count == 0;
            Arrange(); Invalidate();
        }
        // The first notification, or the close button when there is none.
        public void FocusFirst() { if (rows.Count > 0) rows[0].Focus(); else close.Focus(); }
        private void Tip(ModernButton button, string key) { button.AccessibleName = Strings.Get(key, language); tips.SetToolTip(button, button.AccessibleName); }
        // The height the panel wants at a width: its header and every row, or the empty message.
        public int PreferredHeight(int width)
        {
            var inner = Math.Max(D(160), width - D(2));
            var content = rows.Count == 0 ? D(132) : rows.Sum(r => r.HeightFor(inner)) + D(6) * Math.Max(0, rows.Count - 1) + D(12);
            return HeaderFor(width) + content + D(8);
        }
        // The status line wraps to a second line at a narrow width rather than cutting the message.
        private int SummaryHeight(int width)
        {
            if (!checking && !failed) return 0;
            var size = TextRenderer.MeasureText(summary.Text, summary.Font, new Size(Math.Max(D(40), width - D(64)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return Math.Min(D(44), size.Height + D(2));
        }
        private int HeaderFor(int width) { var status = SummaryHeight(width); return status == 0 ? D(58) : D(46) + status + D(10); }
        private void Arrange()
        {
            var button = D(32); var top = D(12); var x = Width - D(10) - button;
            close.SetBounds(x, top, button, button); x -= D(4) + button;
            refresh.SetBounds(x, top, button, button); x -= D(4);
            if (unread) { var width = TextRenderer.MeasureText(readAll.Text, readAll.Font).Width + D(24); readAll.SetBounds(x - width, top, width, button); x = readAll.Left; }
            title.SetBounds(D(48), top + D(4), Math.Max(D(40), x - D(56)), D(24));
            summary.SetBounds(D(48), top + button + D(2), Math.Max(D(40), Width - D(64)), Math.Max(1, SummaryHeight(Width))); summary.Visible = checking || failed;
            var header = HeaderFor(Width);
            list.SetBounds(D(1), header, Width - D(2), Math.Max(0, Height - header - D(8)));
            LayoutRows();
        }
        private void LayoutRows()
        {
            var width = list.ClientSize.Width - D(16); var y = D(4) + list.AutoScrollPosition.Y;
            foreach (var row in rows) { var height = row.HeightFor(width); row.SetBounds(D(8), y, width, height); y += height + D(6); }
            emptyTitle.SetBounds(D(16), D(20), Math.Max(D(40), list.ClientSize.Width - D(32)), D(28));
            emptyText.SetBounds(D(24), emptyTitle.Bottom + D(4), Math.Max(D(40), list.ClientSize.Width - D(48)), D(64));
        }
        // Escape closes the panel from any of its controls.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { CloseRequested?.Invoke(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), D(12))) using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            Icons.Draw(g, UiIcon.Bell, new RectangleF(D(16), D(17), D(22), D(22)), Theme.AccentInk);
            using (var pen = new Pen(Theme.Border)) { var header = HeaderFor(Width); g.DrawLine(pen, D(12), header - D(1), Width - D(12), header - D(1)); }
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width > 0 && Height > 0) using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), D(12))) Region = new Region(path);
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
    // One notification: its kind badge, an unread dot, the title with the count, what the count lists (or the page's
    // message) and a check mark that marks it read. The whole row opens the page; Enter and Space do the same.
    internal sealed class NoticeRow : Control
    {
        public event Action<SiteNotice> OpenNotice;
        public event Action<string> ReadNotice;
        private readonly SiteNotice notice;
        private readonly ModernButton read = new ModernButton { Icon = UiIcon.Check, Compact = true, Look = ButtonLook.Quiet };
        private readonly ToolTip tips = new ToolTip { ShowAlways = true };
        private readonly float scale;
        private readonly string heading, body;
        private bool hovered;
        private static Font titleFont, bodyFont;
        private int D(int value) { return (int)Math.Round(value * scale); }
        public NoticeRow(float scale, SiteNotice notice, string language)
        {
            this.scale = scale; this.notice = notice;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.AllPaintingInWmPaint, true);
            // An unread card is tinted; the check mark sits on the same colour.
            TabStop = true; Cursor = Cursors.Hand; BackColor = notice.Read ? Theme.Surface : Theme.Current.InfoFill; AccessibleRole = AccessibleRole.PushButton; Theme.Tips(tips);
            if (titleFont == null) { titleFont = Theme.Strong(Theme.Body); bodyFont = Theme.Font(Theme.Meta); }
            var blocked = notice.Kind == SiteNotice.Blocked;
            heading = blocked ? notice.Title : notice.Title + " (" + Notices.Shown(notice) + ")";
            if (heading.Trim().Length == 0) heading = Strings.Get("notices", language);
            // A count shows what it counts (the open questionnaires and their dates); a page message is its own text.
            body = blocked ? notice.Text : string.Join("\n", notice.Details.Select(d => "• " + (d.Until.Length == 0 ? d.Title : string.Format(Strings.Get("noticeDetailUntil", language), d.Title, d.Until))));
            read.BackColor = BackColor; read.Visible = !notice.Read;
            read.AccessibleName = string.Format(Strings.Get("noticeMarkRead", language), heading); tips.SetToolTip(read, read.AccessibleName);
            read.Click += (s, e) => ReadNotice?.Invoke(notice.Key);
            Controls.Add(read);
            AccessibleName = heading; AccessibleDescription = string.Join(". ", new[] { body.Replace("\n", "; "), Strings.Get(notice.Read ? "noticeIsRead" : "noticeUnread", language) }.Where(t => t.Length > 0));
            AccessibleDefaultActionDescription = Strings.Get("noticeOpen", language);
            tips.SetToolTip(this, Strings.Get("noticeOpen", language));
        }
        internal void PressReadForQa() { read.PerformClick(); }
        private int TextLeft { get { return D(58); } }
        private int TextWidth(int width) { return Math.Max(D(60), width - TextLeft - D(notice.Read ? 14 : 48)); }
        public int HeightFor(int width)
        {
            var text = body.Length == 0 ? 0 : Math.Min(D(120), TextRenderer.MeasureText(body, bodyFont, new Size(TextWidth(width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
            return Math.Max(D(58), D(12) + D(22) + (text > 0 ? D(2) + text : 0) + D(12));
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); read.SetBounds(Width - D(42), D(10), D(32), D(32)); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Motion.KeyboardInput = false; Focus(); base.OnMouseDown(e); }
        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if (e.Button == MouseButtons.Left) OpenNotice?.Invoke(notice); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            Motion.KeyboardInput = true;
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = e.SuppressKeyPress = true; OpenNotice?.Invoke(notice); return; }
            base.OnKeyDown(e);
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new RowAccessibleObject(this); }
        private sealed class RowAccessibleObject : ControlAccessibleObject
        {
            private readonly NoticeRow owner;
            public RowAccessibleObject(NoticeRow owner) : base(owner) { this.owner = owner; }
            public override string DefaultAction { get { return owner.AccessibleDefaultActionDescription; } }
            public override void DoDefaultAction() { owner.OpenNotice?.Invoke(owner.notice); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Behind(this));
            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(card, D(10)))
            {
                using (var fill = new SolidBrush(hovered || Focused ? Theme.Hover : BackColor)) g.FillPath(fill, path);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.AccentInk, 2f)) g.DrawPath(pen, path);
            }
            var blocked = notice.Kind == SiteNotice.Blocked;
            var badge = new RectangleF(D(12), D(12), D(34), D(34));
            using (var fill = new SolidBrush(blocked ? Theme.Current.WarningFill : Theme.Selected)) g.FillEllipse(fill, badge);
            if (blocked) using (var pen = new Pen(Theme.Current.Warning)) g.DrawEllipse(pen, badge);
            Icons.Draw(g, blocked ? UiIcon.Warning : UiIcon.Journal, new RectangleF(badge.X + D(8), badge.Y + D(8), D(18), D(18)), blocked ? Theme.Current.Warning : Theme.AccentInk);
            if (!notice.Read) using (var dot = new SolidBrush(Theme.Danger)) g.FillEllipse(dot, badge.Right - D(8), badge.Top - D(1), D(10), D(10));
            var textWidth = TextWidth(Width);
            TextRenderer.DrawText(g, heading, titleFont, new Rectangle(TextLeft, D(12), textWidth, D(22)), notice.Read ? Theme.Muted : Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (body.Length > 0)
                TextRenderer.DrawText(g, body, bodyFont, new Rectangle(TextLeft, D(36), textWidth, Height - D(36) - D(8)), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
