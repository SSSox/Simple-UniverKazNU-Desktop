using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;

namespace Univer.Desktop.UI
{
    // Find on a page or document, like a browser's find bar: it floats over the browser's upper right corner (no reflow)
    // and uses Chromium's find, PDF included. Enter/F3 next, Shift+Enter/Shift+F3 previous, Escape closes and clears.
    internal sealed class FindBar : Panel
    {
        private readonly TextBox query = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Ink, MaxLength = 256 };
        private readonly CueBanner cue;
        private readonly Label count = new Label();
        private readonly ModernButton previous = new ModernButton(), next = new ModernButton(), close = new ModernButton();
        private readonly float scale;
        private ChromiumWebBrowser target;
        private string language = "ru", searched = "";
        private bool document, pending;
        // Times the current search stepped onto its first match itself (see ShowResult).
        private int activations;
        private int queued;
        private DateTime pendingSince;
        private int D(int value) { return (int)Math.Round(value * scale); }
        public ChromiumWebBrowser Target { get { return target; } }
        public bool Searching { get { return Visible && target != null; } }
        internal TextBox InputForQa { get { return query; } }
        internal string CountForQa { get { return count.Text; } }
        internal Action<string> Trace;
        public FindBar(float scale)
        {
            this.scale = scale; cue = new CueBanner(query);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Visible = false; BackColor = Theme.Surface; AccessibleRole = AccessibleRole.Grouping; Size = new Size(D(440), D(48)); Theme.Tips(tips);
            query.Font = Theme.Font(Theme.Body); count.Font = Theme.Font(Theme.Meta);
            count.ForeColor = Theme.Muted; count.BackColor = Theme.Surface; count.TextAlign = ContentAlignment.MiddleRight; count.UseMnemonic = false; count.AutoEllipsis = true;
            previous.Icon = UiIcon.Up; next.Icon = UiIcon.Down; close.Icon = UiIcon.Close;
            foreach (var button in new[] { previous, next, close }) { button.Compact = true; button.Look = ButtonLook.Quiet; button.BackColor = Theme.Surface; Controls.Add(button); }
            Controls.Add(query); Controls.Add(count);
            query.TextChanged += (s, e) => Search();
            query.KeyDown += (s, e) =>
            {
                Motion.KeyboardInput = true;
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = e.Handled = true; Step(!e.Shift); }
            };
            query.GotFocus += (s, e) => Invalidate(); query.LostFocus += (s, e) => Invalidate();
            previous.Click += (s, e) => Step(false); next.Click += (s, e) => Step(true); close.Click += (s, e) => Close();
            Resize += (s, e) => Arrange();
            SetLanguage(language); Arrange();
        }
        public void SetLanguage(string value)
        {
            language = value;
            SetLabels();
        }
        private void SetLabels()
        {
            var name = Strings.Get(document ? "findInDocument" : "findOnPage", language);
            AccessibleName = query.AccessibleName = name; cue.Text = Strings.Get("findCue", language);
            Tip(previous, "findPrevious"); Tip(next, "findNext"); Tip(close, "findClose");
        }
        private void Tip(ModernButton button, string key) { button.AccessibleName = Strings.Get(key, language); tips.SetToolTip(button, button.AccessibleName); }
        private readonly ToolTip tips = new ToolTip { ShowAlways = true };
        // Opens (or refocuses) the bar for this browser; a different browser starts a fresh search with the same text.
        public void Open(ChromiumWebBrowser browser, bool inDocument)
        {
            if (browser == null || browser.IsDisposed) return;
            if (target != browser) { Stop(); target = browser; }
            if (document != inDocument) { document = inDocument; SetLabels(); }
            Visible = true; BringToFront();
            query.Focus(); query.SelectAll();
            if (query.TextLength > 0 && searched.Length == 0) Search();
        }
        public void Step(bool forward)
        {
            if (!Searching) return;
            if (query.TextLength == 0) { query.Focus(); return; }
            if (searched != query.Text) { Search(); return; }
            // Chromium answers each request with a final result; presses made before it arrives run one by one afterwards,
            // so a quick Enter right after typing is not lost.
            if (pending && DateTime.UtcNow - pendingSince < TimeSpan.FromSeconds(2)) { queued += forward ? 1 : -1; return; }
            Send(forward, true);
        }
        public void Close()
        {
            if (!Visible) return;
            var browser = target; Stop(); target = null; Visible = false;
            if (browser != null && !browser.IsDisposed && browser.Visible) browser.Focus();
        }
        // The page behind the bar went away (navigation, closed document or preview): its matches are gone with it.
        public void Forget(IWebBrowser browser) { if (browser != null && ReferenceEquals(browser, target)) Close(); }
        public void ShowResult(IWebBrowser browser, int total, int ordinal, bool final)
        {
            if (Trace != null) Trace(total + "/" + ordinal + "/" + final);
            if (!Searching || !ReferenceEquals(browser, target) || query.TextLength == 0) return;
            if (final)
            {
                pending = false;
                // A search can end with matches highlighted but none active (a fresh one, or an answer crossing a newer request):
                // step onto the first match so the next press goes to the second, twice at most in case Chromium keeps doing it.
                if (total > 0 && ordinal <= 0 && activations < 2) { activations++; Send(true, true); return; }
                if (total == 0) queued = 0;
                else if (queued != 0) { var forward = queued > 0; queued -= forward ? 1 : -1; Send(forward, true); }
            }
            if (total > 0) { count.ForeColor = Theme.Muted; count.Text = string.Format(Strings.Get("findCount", language), Math.Max(1, ordinal), total); }
            else if (final) { count.ForeColor = Theme.Danger; count.Text = Strings.Get("findNone", language); }
            previous.Enabled = next.Enabled = total > 0;
            if (final) AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            Invalidate();
        }
        // A new search for the typed text; earlier queued presses belonged to the old text.
        private void Search()
        {
            if (!Searching || target.IsDisposed || !target.IsBrowserInitialized) return;
            var host = target.GetBrowser()?.GetHost(); if (host == null) return;
            queued = 0;
            if (query.TextLength == 0) { host.StopFinding(true); searched = ""; pending = false; count.Text = ""; previous.Enabled = next.Enabled = true; Invalidate(); return; }
            searched = query.Text; activations = 0; Send(true, false);
        }
        private void Send(bool forward, bool findNext)
        {
            var host = target != null && !target.IsDisposed && target.IsBrowserInitialized ? target.GetBrowser()?.GetHost() : null;
            if (host == null) { pending = false; return; }
            pending = true; pendingSince = DateTime.UtcNow; host.Find(searched, forward, false, findNext);
        }
        private void Stop()
        {
            if (target != null && !target.IsDisposed && target.IsBrowserInitialized) target.GetBrowser()?.GetHost()?.StopFinding(true);
            searched = ""; count.Text = ""; previous.Enabled = next.Enabled = true; pending = false; activations = 0; queued = 0;
        }
        private void Arrange()
        {
            var button = D(32); var top = (Height - button) / 2; var x = Width - D(8) - button;
            close.SetBounds(x, top, button, button); x -= D(10) + button;
            next.SetBounds(x, top, button, button); x -= D(2) + button;
            previous.SetBounds(x, top, button, button); x -= D(6) + D(84);
            count.SetBounds(x, top, D(84), button);
            var left = D(40);
            query.SetBounds(left, (Height - query.PreferredHeight) / 2, Math.Max(D(60), count.Left - D(8) - left), query.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), D(10)))
            using (var pen = new Pen(query.Focused ? Theme.AccentInk : Theme.Border, query.Focused ? 1.6f : 1)) g.DrawPath(pen, path);
            Icons.Draw(g, UiIcon.Search, new RectangleF(D(12), (Height - D(20)) / 2f, D(20), D(20)), Theme.Muted);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, close.Left - D(5), D(12), close.Left - D(5), Height - D(12));
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}