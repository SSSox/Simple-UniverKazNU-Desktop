using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CefSharp;
using CefSharp.Handler;
using Univer.Desktop.Core;

namespace Univer.Desktop.Web
{
    internal sealed class ShortcutHandler : KeyboardHandler
    {
        private readonly Action<Keys> dispatch;
        private readonly bool viewer;
        private readonly KeyReleases handled;
        public ShortcutHandler(Action<Keys> dispatch, bool viewer, KeyReleases releases = null)
        { this.dispatch = dispatch; this.viewer = viewer; handled = releases ?? new KeyReleases(); }
        protected override bool OnPreKeyEvent(IWebBrowser chromium, IBrowser browser, KeyType type, int windowsKeyCode,
            int nativeKeyCode, CefEventFlags modifiers, bool systemKey, ref bool shortcut)
        {
            if (type != KeyType.RawKeyDown && type != KeyType.KeyDown) return false;
            handled.KeyDown(windowsKeyCode);
            var key = ShortcutKey(windowsKeyCode, modifiers);
            if (!Accepted(key)) return false;
            handled.Pressed(windowsKeyCode, Environment.TickCount); shortcut = true; dispatch(key); return true;
        }
        private static Keys ShortcutKey(int windowsKeyCode, CefEventFlags modifiers)
        {
            var key = (Keys)windowsKeyCode;
            if ((modifiers & CefEventFlags.AltDown) != 0) key |= Keys.Alt;
            if ((modifiers & CefEventFlags.ControlDown) != 0) key |= Keys.Control;
            if ((modifiers & CefEventFlags.ShiftDown) != 0) key |= Keys.Shift;
            if (key == (Keys.Control | Keys.Shift | Keys.Oemplus)) key = Keys.Control | Keys.Oemplus;
            return key;
        }
        private bool Accepted(Keys key)
        {
            return key == (Keys.Alt | Keys.Left) || key == Keys.F5 || (viewer && key == Keys.Escape) ||
                key == (Keys.Control | Keys.F) || key == Keys.F3 || key == (Keys.Shift | Keys.F3) ||
                key == (Keys.Control | Keys.Oemcomma) || key == (Keys.Control | Keys.P) || key == (Keys.Control | Keys.J) || key == (Keys.Control | Keys.D0) || key == (Keys.Control | Keys.NumPad0) ||
                key == (Keys.Control | Keys.Oemplus) || key == (Keys.Control | Keys.Add) || key == (Keys.Control | Keys.OemMinus) || key == (Keys.Control | Keys.Subtract);
        }
        protected override bool OnKeyEvent(IWebBrowser chromium, IBrowser browser, KeyType type, int windowsKeyCode,
            int nativeKeyCode, CefEventFlags modifiers, bool systemKey)
        {
            // Chromium 109's PDF guest can bypass OnPreKeyEvent. Handle its unconsumed
            // shortcut release, without issuing a second command after a handled key-down.
            if (type == KeyType.RawKeyDown || type == KeyType.KeyDown)
            {
                handled.PageKeyDown();
                // A shortcut press that skipped OnPreKeyEvent (the PDF guest) and came back unconsumed runs now, so letting
                // Ctrl go first does not lose it; its release is then not dispatched again.
                var down = ShortcutKey(windowsKeyCode, modifiers);
                if (!Accepted(down)) return false;
                handled.Pressed(windowsKeyCode, Environment.TickCount); dispatch(down); return true;
            }
            if (type != KeyType.KeyUp) return false;
            var dispatched = handled.Released(windowsKeyCode, Environment.TickCount);
            var key = ShortcutKey(windowsKeyCode, modifiers);
            if (!Accepted(key)) return false;
            if (!dispatched) dispatch(key);
            return true;
        }
    }
    // Chromium's find results ("n of m") for the app's find bar; called on a CEF thread.
    internal sealed class FindResults : IFindHandler
    {
        private readonly Action<IWebBrowser, int, int, bool> result;
        public FindResults(Action<IWebBrowser, int, int, bool> result) { this.result = result; }
        public void OnFindResult(IWebBrowser chromiumWebBrowser, IBrowser browser, int identifier, int count, CefSharp.Structs.Rect selectionRect, int activeMatchOrdinal, bool finalUpdate)
        { result(chromiumWebBrowser, count, activeMatchOrdinal, finalUpdate); }
    }
    // Decides every main-frame navigation of a site browser on Chromium's thread; actions on the app go to the UI thread
    // through dispatch, and each callback is set once before the browser is created.
    internal sealed class NavigationHandler : RequestHandler
    {
        private readonly Action<Action> dispatch;
        private readonly bool viewer;
        public NavigationHandler(Action<Action> dispatch, bool viewer) { this.dispatch = dispatch; this.viewer = viewer; }
        // A safe outside address the user clicked: opened in the Windows default program.
        public Action<string> External { get; set; }
        // A printed document (transcript, IUP, syllabus, schedule) opened from the main browser.
        public Action<string> OpenDocument { get; set; }
        // Runs on Chromium's thread: whether a Moodle sign-in started from the sidebar is waiting for its exit.
        public Func<string, bool> HandOffPending { get; set; }
        public Action<string, string, List<KeyValuePair<string, string>>> OpenHandOff { get; set; }
        // Runs on Chromium's thread: a direct address for a clicked page that would only redirect, or null.
        public Func<string, string> Shortcut { get; set; }
        public Action<string> Load { get; set; }
        public Action<string> Download { get; set; }
        public Action<string> Language { get; set; }
        public Action Logout { get; set; }
        // A link in a printed document: closes the viewer and opens the address in the main browser.
        public Action<string> LeaveDocument { get; set; }
        // A navigation the browser goes ahead with (address, method).
        public Action<string, string> Navigating { get; set; }
        // Runs on Chromium's thread before the navigation continues.
        public Action<string> BeforeNavigate { get; set; }
        // The site sent the requested page to its login form, so the session ended (that address), or the user went
        // somewhere else (null).
        public Action<string> LoginRedirect { get; set; }
        public FormSubmissions Submissions { get; set; }
        // The latest main-frame GET, read and written on Chromium's thread only.
        private string requestedGet;
        // A callback the application forgot to assign must not throw inside a Chromium callback: an exception escaping one
        // ends the process. Navigation is then refused instead, which is visible and recoverable.
        private bool Ready
        {
            get
            {
                return External != null && OpenDocument != null && HandOffPending != null && OpenHandOff != null &&
                    Shortcut != null && Load != null && Download != null && Language != null && Logout != null &&
                    LeaveDocument != null && Navigating != null && BeforeNavigate != null && LoginRedirect != null && Submissions != null;
            }
        }
        protected override bool OnBeforeBrowse(IWebBrowser chromium, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        {
            if (!Ready) return true;
            var url = request.Url; var method = request.Method;
            // An empty frame is not content: it carries no origin of its own and no navigation to decide. Checking it
            // before the frame branch keeps srcdoc/blank frames of the site working without granting them privileges.
            if (SitePolicy.EmptyDocument(url)) return false;
            if (!frame.IsMain) return !SitePolicy.ChildFrame(url); // Remote frames get no automation or native privileges.
            if (!SitePolicy.Trusted(url))
            {
                // While a Moodle sign-in started from "СДО Moodle" is pending, only the site's own exit is handed to the user's browser.
                var pending = HandOffPending(url);
                var fields = pending && method == "POST" ? SingleSignOn.PostFields(request) : null;
                if (pending && SignOnPage.HandOffExit(url, method, isRedirect, fields != null))
                { dispatch(() => OpenHandOff(url, method, fields)); return true; }
                if (userGesture && !isRedirect && SitePolicy.External(url)) dispatch(() => External(url));
                return true;
            }
            // A request of an ended session reaches the login form by a redirect or a page script; the page it asked for
            // reopens after the automatic sign-in. A click of the user's own forgets it.
            if (SitePolicy.LoginPage(url) && (isRedirect || !userGesture)) { var asked = requestedGet; if (SitePolicy.Resumable(asked)) dispatch(() => LoginRedirect(asked)); }
            else if (userGesture && !isRedirect) dispatch(() => LoginRedirect(null));
            requestedGet = method == "GET" ? url : null;
            // Only the user's own click on the site's exit link pauses automatic sign-in; a redirect or script reaching /user/logout
            // ends just the server session, and the login page signs in again.
            if (SitePolicy.UserLogout(url, userGesture, isRedirect)) { dispatch(Logout); return true; }
            if ((SitePolicy.ProtectedDownload(url) || SitePolicy.DocumentDownload(url)) && method == "GET") { dispatch(() => Download(url)); return true; }
            // Server redirects (for example to the read notice at sign-in) are never rewritten.
            // Only a user's click is redirected; application loads (startup, retry, fixtures) go where they were sent.
            var replacement = method == "GET" && !isRedirect && userGesture ? Shortcut(url) : null;
            if (replacement != null && replacement != url) { dispatch(() => Load(replacement)); return true; }
            if (!viewer && SitePolicy.PrintDocument(url) && method == "GET") { dispatch(() => OpenDocument(url)); return true; }
            var language = SitePolicy.Language(url);
            // A link in a printed document leaves the viewer for the main browser.
            if (viewer && userGesture && method == "GET" && !SitePolicy.PrintDocument(url) && language == null)
            { dispatch(() => LeaveDocument(url)); return true; }
            if (userGesture && !isRedirect && method == "GET" && language != null) dispatch(() => Language(language));
            // A fast server redirect to Moodle can arrive before the queued UI callback runs, so the hand-off window opens here.
            if (method == "GET") BeforeNavigate?.Invoke(url);
            else if (method == "POST") Submissions?.Posted(url, Environment.TickCount);
            dispatch(() => Navigating(url, method));
            return false;
        }
        protected override bool OnCertificateError(IWebBrowser chromium, IBrowser browser, CefErrorCode error, string url, ISslInfo info, IRequestCallback callback)
        { return false; }
    }
    internal sealed class PopupHandler : LifeSpanHandler
    {
        private readonly Action<Action> dispatch;
        private readonly Action<string> route;
        public PopupHandler(Action<Action> dispatch, Action<string> route) { this.dispatch = dispatch; this.route = route; }
        // CEF owns child browser windows; managed WinForms owns the application lifetime.
        // Also suppress WM_CLOSE for CEF-created auxiliary windows during session disposal.
        protected override bool DoClose(IWebBrowser chromium, IBrowser browser) { return true; }
        protected override bool OnBeforePopup(IWebBrowser chromium, IBrowser browser, IFrame frame, string url, string name,
            WindowOpenDisposition disposition, bool userGesture, IPopupFeatures features, IWindowInfo windowInfo,
            IBrowserSettings settings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
        {
            newBrowser = null;
            if (SitePolicy.Trusted(url) || (userGesture && SitePolicy.External(url))) dispatch(() => route(url));
            return true;
        }
    }
    internal sealed class SafeDownloadHandler : DownloadHandler
    {
        private readonly Action<Action> dispatch;
        private readonly Action<string, string> download;
        private readonly Action<string> rejected;
        private readonly FormSubmissions submissions;
        public SafeDownloadHandler(Action<Action> dispatch, Action<string, string> download, Action<string> rejected, FormSubmissions submissions)
        { this.dispatch = dispatch; this.download = download; this.rejected = rejected; this.submissions = submissions; }
#if LEGACY_ENGINE
        protected override void OnBeforeDownload(IWebBrowser chromium, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#else
        protected override bool OnBeforeDownload(IWebBrowser chromium, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback)
#endif
        {
            // The validated native downloader owns the save; never continue CEF's unchecked flow.
            // A blob: or data: file the page builds cannot be fetched and checked again, so say so.
            if (!SitePolicy.Trusted(item.Url)) dispatch(() => rejected("downloadUnsupported"));
            else if (submissions.Answers(item.Url, Environment.TickCount)) dispatch(() => rejected("downloadFromForm"));
            else { var url = item.Url; var name = item.SuggestedFileName; dispatch(() => download(url, name)); }
#if !LEGACY_ENGINE
            return true; // Handled here; never let Chromium's default download UI save an unchecked response.
#endif
        }
        protected override void OnDownloadUpdated(IWebBrowser chromium, IBrowser browser, DownloadItem item, IDownloadItemCallback callback)
        { if (item.IsInProgress && !callback.IsDisposed) callback.Cancel(); }
    }
}
