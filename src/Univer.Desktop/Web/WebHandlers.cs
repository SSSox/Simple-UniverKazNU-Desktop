using System;
using System.Windows.Forms;
using CefSharp;
using CefSharp.Handler;
using Univer.Desktop.Core;

namespace Univer.Desktop.Web
{
    // The app's own browser window for other sites. No scripts of the app run there and no page can call the app: these
    // handlers only decide where a navigation, a new window or a file may go. CEF calls them on its threads.
    internal sealed class WebRequestHandler : RequestHandler
    {
        private readonly Action<Action> dispatch;
        private readonly Action<string> univer, shell, revisit;
        private readonly Action<string> open;
        // univer: a Univer page the user went to from here; shell: a mail or phone link; open: a page asked for in a new tab;
        // revisit: the one-time Moodle hand-off address reached again (Back past the first page).
        public WebRequestHandler(Action<Action> dispatch, Action<string> univer, Action<string> shell, Action<string> open, Action<string> revisit)
        { this.dispatch = dispatch; this.univer = univer; this.shell = shell; this.open = open; this.revisit = revisit; }
        protected override bool OnBeforeBrowse(IWebBrowser chromium, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        {
            var url = request.Url; var main = frame.IsMain;
            var decision = WebLinks.Decide(url, main);
            if (decision == WebNavigation.Allow)
            {
                // The hand-off page is served once, from memory; asking the real site for it later only shows an error page.
                if (main && WebLinks.HandOff(url) && request.TransitionType.HasFlag(TransitionType.ForwardBack)) { dispatch(() => revisit(url)); return true; }
                return false;
            }
            // A Univer page goes back to the main window only when the user asked for it; a redirect or a script there is
            // simply stopped, so another site cannot steer the signed-in Univer session (to logout, for one).
            if (decision == WebNavigation.Univer && userGesture && !isRedirect && request.Method == "GET" && WebLinks.HandBack(url)) dispatch(() => univer(url));
            else if (decision == WebNavigation.Shell && userGesture && !isRedirect) dispatch(() => shell(url));
            return true;
        }
        protected override bool OnOpenUrlFromTab(IWebBrowser chromium, IBrowser browser, IFrame frame, string url, WindowOpenDisposition disposition, bool userGesture)
        {
            if (userGesture && WebLinks.Opened(url) != WebNavigation.Block) dispatch(() => open(url));
            return true;
        }
        // A broken or untrusted certificate stops the page; Chromium shows why. There is no way past it here.
        protected override bool OnCertificateError(IWebBrowser chromium, IBrowser browser, CefErrorCode error, string url, ISslInfo info, IRequestCallback callback)
        { return false; }
        // Sign-in boxes of other sites' servers (HTTP authentication) are not shown: nothing is typed into a box the app drew.
        protected override bool GetAuthCredentials(IWebBrowser chromium, IBrowser browser, string originUrl, bool isProxy, string host, int port, string realm, string scheme, IAuthCallback callback)
        { return false; }
    }
    // A new window or tab a page asks for opens in the same window, after the same check as a click.
    internal sealed class WebPopupHandler : LifeSpanHandler
    {
        private readonly Action<Action> dispatch;
        private readonly Action<string> open;
        private readonly Action<bool> alive;
        // alive: true once Chromium has created the page, false once it has closed it (both on CEF's thread).
        public WebPopupHandler(Action<Action> dispatch, Action<string> open, Action<bool> alive) { this.dispatch = dispatch; this.open = open; this.alive = alive; }
        protected override bool DoClose(IWebBrowser chromium, IBrowser browser) { return true; }
        protected override void OnAfterCreated(IWebBrowser chromium, IBrowser browser) { if (!browser.IsPopup) alive(true); }
        protected override void OnBeforeClose(IWebBrowser chromium, IBrowser browser) { if (!browser.IsPopup) alive(false); }
        protected override bool OnBeforePopup(IWebBrowser chromium, IBrowser browser, IFrame frame, string url, string name,
            WindowOpenDisposition disposition, bool userGesture, IPopupFeatures features, IWindowInfo windowInfo,
            IBrowserSettings settings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
        {
            newBrowser = null;
            if (userGesture && WebLinks.Opened(url) != WebNavigation.Block) dispatch(() => open(url));
            return true;
        }
    }
    // Files from other sites go to Downloads under their own safe names; programs and scripts are refused before a byte arrives.
    internal sealed class WebDownloadHandler : DownloadHandler
    {
        private readonly Action<Action> dispatch;
        private readonly Func<string, string> target;
        private readonly Action<string> refused;
        private readonly Action<int, string, string, DownloadItem> progress;
        private readonly Func<int, bool> cancelled;
        private readonly Action<string> unused;
        // target: the path for a suggested name, or null to refuse; progress: (id, path, host, item) on every update;
        // cancelled: whether the student cancelled that download in Downloads (asked on CEF's thread);
        // unused: gives back a path target reserved when the download could not continue.
        public WebDownloadHandler(Action<Action> dispatch, Func<string, string> target, Action<string> refused, Action<int, string, string, DownloadItem> progress, Func<int, bool> cancelled, Action<string> unused)
        { this.dispatch = dispatch; this.target = target; this.refused = refused; this.progress = progress; this.cancelled = cancelled; this.unused = unused; }
#if LEGACY_ENGINE
        protected override void OnBeforeDownload(IWebBrowser chromium, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#else
        protected override bool OnBeforeDownload(IWebBrowser chromium, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#endif
        {
            var path = SitePolicy.External(item.Url) || item.Url.StartsWith("blob:", StringComparison.Ordinal) || item.Url.StartsWith("data:", StringComparison.Ordinal) ? target(item.SuggestedFileName) : null;
            if (path == null) dispatch(() => refused(item.SuggestedFileName));
            else if (!callback.IsDisposed) { using (callback) callback.Continue(path, false); }
            else unused(path);
#if !LEGACY_ENGINE
            return true;
#endif
        }
        protected override void OnDownloadUpdated(IWebBrowser chromium, IBrowser browser, DownloadItem item, IDownloadItemCallback callback)
        {
            if (item.IsInProgress && cancelled(item.Id) && !callback.IsDisposed) callback.Cancel();
            if (string.IsNullOrEmpty(item.FullPath)) return;
            var id = item.Id; var path = item.FullPath; var host = SitePolicy.Host(item.OriginalUrl ?? item.Url);
            dispatch(() => progress(id, path, host, item));
        }
    }
    // The window's keys inside a page: Alt+Left/Right walk its history (Back from the first page closes it), F5 and Ctrl+R reload, Ctrl+P
    // prints, Ctrl+0/+/- zoom. Escape stays with the page (it closes the page's own menus and must not lose a half-typed form);
    // on the window's bar it closes the window.
    internal sealed class WebKeys : KeyboardHandler
    {
        private readonly Action<Keys> dispatch;
        public WebKeys(Action<Keys> dispatch) { this.dispatch = dispatch; }
        public static bool Accepted(Keys key)
        {
            return key == (Keys.Alt | Keys.Left) || key == (Keys.Alt | Keys.Right) || key == Keys.F5 || key == (Keys.Control | Keys.R) || key == (Keys.Control | Keys.P) ||
                key == (Keys.Control | Keys.D0) || key == (Keys.Control | Keys.NumPad0) || key == (Keys.Control | Keys.Oemplus) || key == (Keys.Control | Keys.Add) ||
                key == (Keys.Control | Keys.OemMinus) || key == (Keys.Control | Keys.Subtract);
        }
        protected override bool OnPreKeyEvent(IWebBrowser chromium, IBrowser browser, KeyType type, int windowsKeyCode,
            int nativeKeyCode, CefEventFlags modifiers, bool systemKey, ref bool shortcut)
        {
            if (LockGate.Engaged) return true;
            if (type != KeyType.RawKeyDown && type != KeyType.KeyDown) return false;
            var key = (Keys)windowsKeyCode;
            if ((modifiers & CefEventFlags.AltDown) != 0) key |= Keys.Alt;
            if ((modifiers & CefEventFlags.ControlDown) != 0) key |= Keys.Control;
            if ((modifiers & CefEventFlags.ShiftDown) != 0) key |= Keys.Shift;
            if (key == (Keys.Control | Keys.Shift | Keys.Oemplus)) key = Keys.Control | Keys.Oemplus;
            if (!Accepted(key)) return false;
            shortcut = true; dispatch(key); return true;
        }
    }
}
