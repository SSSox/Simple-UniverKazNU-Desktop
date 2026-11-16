using System;
using System.Drawing;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // Another site shown inside the app: a bar with its history buttons, the site's host (with a lock for https) and the page
    // title, "Open in browser" and Close, over the page itself. It covers the site's toolbar and page; the sidebar stays.
    internal sealed class WebPanel : Panel
    {
        private readonly Panel bar = new SmoothPanel();
        private readonly ModernButton back = new ModernButton(), forward = new ModernButton(), reload = new ModernButton(), outside = new ModernButton(), close = new ModernButton();
        private readonly Label host = new RollLabel(), title = new RollLabel();
        private readonly ToolTip tips;
        private readonly Func<int, int> sz;
        private string language = "ru", pageTitle = "", state = "";
        public ChromiumWebBrowser Browser { get; private set; }
        public event Action CloseRequested;
        public event Action<string> OutsideRequested;
        public int BarHeight { get { return bar.Height; } }
        public string StateKey { get { return state; } }
        public string Address { get { return Browser != null && !Browser.IsDisposed ? Browser.Address ?? "" : ""; } }
        public WebPanel(Func<int, int> sz, ToolTip tips)
        {
            this.sz = sz; this.tips = tips;
            Dock = DockStyle.Fill; BackColor = Theme.Background; Visible = false;
            bar.Dock = DockStyle.Top; bar.BackColor = Theme.Surface; bar.Paint += (s, e) => PaintBar(e.Graphics); bar.Resize += (s, e) => Arrange();
            foreach (var button in new[] { back, forward, reload, close }) { button.Compact = true; bar.Controls.Add(button); }
            back.Icon = UiIcon.Back; forward.Icon = UiIcon.Forward; reload.Icon = UiIcon.Reload; close.Icon = UiIcon.Close;
            outside.Look = ButtonLook.Outline; outside.Icon = UiIcon.External; outside.Font = Theme.Font(Theme.Body); bar.Controls.Add(outside);
            host.Font = Theme.Display(Theme.Body); host.AutoEllipsis = true; host.UseMnemonic = false;
            title.Font = Theme.Font(Theme.Meta); title.ForeColor = Theme.Muted; title.AutoEllipsis = true; title.UseMnemonic = false;
            bar.Controls.Add(host); bar.Controls.Add(title);
            Controls.Add(bar);
            back.Click += (s, e) => GoBack(); forward.Click += (s, e) => { if (Browser?.CanGoForward == true) Browser.Forward(); };
            reload.Click += (s, e) => Reload(); close.Click += (s, e) => CloseRequested?.Invoke();
            outside.Click += (s, e) => OutsideRequested?.Invoke(Address);
        }
        public void Attach(ChromiumWebBrowser browser)
        {
            Browser = browser; Controls.Add(browser); browser.BringToFront();
            pageTitle = ""; state = "webLoading"; Render();
        }
        public void Detach() { Browser = null; pageTitle = ""; state = ""; Render(); }
        // Back from the first page leaves the window, as on a phone.
        public void GoBack() { if (Browser?.CanGoBack == true) Browser.Back(); else CloseRequested?.Invoke(); }
        public void Reload() { if (Browser != null && Browser.IsBrowserInitialized && !string.IsNullOrEmpty(Browser.Address) && Browser.Address != "about:blank") Browser.Reload(); }
        public void Loading(bool busy) { if (busy) state = "webLoading"; else if (state == "webLoading") state = ""; Render(); }
        public void Failed(bool certificate) { state = certificate ? "webInsecure" : "webFailed"; Render(); }
        public void Titled(string value) { pageTitle = value ?? ""; Render(); }
        public void Moved() { Render(); }
        public void SetLanguage(string value) { language = value; Render(); }
        private string Host { get { Uri uri; return Uri.TryCreate(Address, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") ? uri.Host : ""; } }
        private bool Secure { get { return Address.StartsWith("https://", StringComparison.OrdinalIgnoreCase); } }
        private void Render()
        {
            host.Text = Host;
            // The page's own title is the site's words; a state of the window (loading, failed) says what the app knows.
            title.Text = state.Length > 0 ? Strings.Get(state, language) : !Secure && Host.Length > 0 ? Strings.Get("webPlain", language) : pageTitle;
            title.ForeColor = state == "webFailed" || state == "webInsecure" || (!Secure && Host.Length > 0) ? Theme.Danger : Theme.Muted;
            forward.Enabled = Browser?.CanGoForward == true;
            Label(back, Browser?.CanGoBack == true ? "back" : "webClose"); Label(forward, "forward"); Label(reload, "refresh"); Label(close, "webClose");
            outside.Text = Strings.Get("webOutside", language); outside.AccessibleName = outside.Text; tips.SetToolTip(outside, Strings.Get("webOutsideHint", language));
            host.AccessibleName = Strings.Get(Secure ? "webSecure" : "webPlain", language) + " " + Host;
            Arrange(); bar.Invalidate();
        }
        private void Label(ModernButton button, string key) { button.AccessibleName = Strings.Get(key, language); tips.SetToolTip(button, button.AccessibleName); }
        private void Arrange()
        {
            bar.Height = sz(44); var top = sz(8); var x = sz(12);
            back.SetBounds(x, top, sz(28), sz(28)); x += sz(32);
            forward.SetBounds(x, top, sz(28), sz(28)); x += sz(32);
            reload.SetBounds(x, top, sz(28), sz(28)); x += sz(28) + sz(16);
            close.SetBounds(bar.Width - sz(12) - sz(28), top, sz(28), sz(28));
            var outsideWidth = Math.Max(sz(96), TextRenderer.MeasureText(outside.Text ?? "", outside.Font).Width + sz(44));
            outside.SetBounds(close.Left - sz(8) - outsideWidth, top, outsideWidth, sz(28));
            // Narrow windows keep the actions and let the host and title shorten.
            outside.Visible = outside.Left - x > sz(140);
            var words = (outside.Visible ? outside.Left : close.Left) - sz(16) - (x + sz(24));
            host.SetBounds(x + sz(24), sz(4), Math.Max(sz(40), words), sz(20));
            title.SetBounds(x + sz(24), sz(23), host.Width, sz(17));
        }
        private void PaintBar(Graphics g)
        {
            using (var pen = new Pen(Theme.Current.Line)) g.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            if (Host.Length == 0) return;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var icon = new RectangleF(host.Left - sz(22), (bar.Height - sz(16)) / 2f, sz(16), sz(16));
            Icons.Draw(g, Secure ? UiIcon.Lock : UiIcon.Warning, icon, Secure ? Theme.Success : Theme.Danger);
        }
    }
}
