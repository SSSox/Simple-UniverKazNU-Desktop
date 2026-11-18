using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // Links to other sites (Moodle, Google Drive, a teacher's page) open in the app's own browser window, like the Android app,
    // unless the student chose the system browser. The window has a profile of its own: Moodle's sign-in stays between launches,
    // Univer's cookies never reach it, and logout, a new account or Settings › Clear remove it.
    internal sealed partial class MainForm
    {
        private WebPanel webPanel;
        private RequestContext webContext;
        // Files the window is saving: CEF's id → the record shown in Downloads; ids the student cancelled there.
        private readonly Dictionary<int, DownloadRecord> webDownloads = new Dictionary<int, DownloadRecord>();
        private readonly HashSet<int> webCancels = new HashSet<int>();
        // Pages Chromium still holds open in the window's profile; the profile is dropped only after they have closed.
        private readonly object webPages = new object();
        private int webOpen;
        private bool WebShown { get { return webPanel != null && webPanel.Visible; } }
        private void BuildWeb()
        {
            webPanel = new WebPanel(SZ, tips) { Dock = System.Windows.Forms.DockStyle.None };
            workspace.Controls.Add(webPanel);
            workspace.Resize += (s, e) => PlaceWeb();
            webPanel.CloseRequested += CloseWeb;
            webPanel.OutsideRequested += url => { if (SitePolicy.External(url) && OpenOutside(url)) CloseWeb(); };
        }
        // Over the site's toolbar and page, under the toasts.
        private void PlaceWeb() { if (webPanel != null) webPanel.SetBounds(0, 0, workspace.ClientSize.Width, workspace.ClientSize.Height); }
        // A link the student follows: inside the app when allowed, otherwise in the system browser.
        private bool OpensInside(string url) { return (webInsideForQa || !prefs.ExternalLinks && outsideProbe == null && !selfTest) && WebLinks.InApp(url); }
        // prepare: runs before the page is asked for (the hand-off registers its in-memory page there).
        private void ShowWeb(string url, Action<ChromiumWebBrowser> prepare = null)
        {
            if (closing || rotating || webPanel == null || url == null) return;
            HideDownloads(); if (findBar.Searching) findBar.Close();
            // A new browser starts at the page itself: a Load asked of a browser Chromium has not created yet can be lost.
            var created = webPanel.Browser == null;
            var browser = EnsureWebBrowser(url);
            prepare?.Invoke(browser);
            PlaceWeb(); webPanel.SetLanguage(prefs.Language); webPanel.Visible = true; webPanel.BringToFront(); toast.BringToFront();
            webPanel.Cover(zoomPercent / 100f);
            if (!created) browser.Load(url);
            browser.Focus();
        }
        private ChromiumWebBrowser EnsureWebBrowser(string first)
        {
            if (webContext == null)
            {
                // The profile's name is saved before Chromium creates its folder, so the next start keeps (and never cleans) it.
                if (!accountReview && !storage.WebProfileSaved(prefs.WebProfile)) QueueAsync(SavePreferencesAsync);
                webContext = new RequestContext(new RequestContextSettings { CachePath = storage.WebProfilePath(prefs.WebProfile), PersistSessionCookies = true });
            }
            if (webPanel.Browser != null) return webPanel.Browser;
            var browser = new ChromiumWebBrowser(first, webContext) { Dock = System.Windows.Forms.DockStyle.Fill };
            var surface = Theme.Background; browser.BrowserSettings.BackgroundColor = Cef.ColorSetARGB(255, surface.R, surface.G, surface.B);
            Action<Action> current = operation => Dispatch(() => { if (!browser.IsDisposed && webPanel.Browser == browser) operation(); });
            Action<string> open = url => OpenFromWeb(browser, url);
            browser.RequestHandler = new WebRequestHandler(current, UniverFromWeb, url => OpenOutside(url), open, url => CloseWeb());
            browser.LifeSpanHandler = new WebPopupHandler(current, open, WebPageAlive);
            browser.DownloadHandler = new WebDownloadHandler(current, WebDownloadTarget, name => SetStatus("webDownloadBlocked"), WebDownloadProgress, WebDownloadCancelled, ReleaseWebTarget);
            browser.KeyboardHandler = new WebKeys(key => current(() => WebKey(key)));
            browser.LoadingStateChanged += (s, e) => current(() => webPanel.Loading(e.IsLoading));
            browser.FrameLoadStart += (s, e) => { if (e.Frame.IsMain) current(webPanel.Committed); };
            browser.AddressChanged += (s, e) => current(() => webPanel.Moved());
            browser.TitleChanged += (s, e) => current(() => webPanel.Titled(e.Title));
            browser.LoadError += (s, e) => { if (e.Frame.IsMain && e.ErrorCode != CefErrorCode.Aborted) { var code = e.ErrorCode; current(() => webPanel.Failed(CertificateError(code))); } };
            browser.IsBrowserInitializedChanged += (s, e) => current(SetWebZoom);
            webBrowserForQa?.Invoke(browser);
            webPanel.Attach(browser);
            return browser;
        }
        private void WebPageAlive(bool alive) { lock (webPages) { webOpen += alive ? 1 : -1; System.Threading.Monitor.PulseAll(webPages); } }
        private void WaitWebPagesClosed(TimeSpan limit)
        {
            var until = DateTime.UtcNow + limit;
            lock (webPages)
                while (webOpen > 0) { var left = until - DateTime.UtcNow; if (left <= TimeSpan.Zero) return; System.Threading.Monitor.Wait(webPages, left); }
        }
        private static bool CertificateError(CefErrorCode code) { return (int)code <= -200 && (int)code > -300; }
        private void OpenFromWeb(ChromiumWebBrowser browser, string url)
        {
            var decision = WebLinks.Opened(url);
            if (decision == WebNavigation.Univer) UniverFromWeb(url);
            else if (decision == WebNavigation.Shell) OpenOutside(url);
            else if (decision == WebNavigation.Allow) browser.Load(url);
        }
        // A Univer link clicked on another site opens in the main window, signed in as usual.
        private void UniverFromWeb(string url)
        {
            if (main == null || commandBusy || !WebLinks.HandBack(url)) return;
            CloseWeb(); CloseDocument(); main.Load(url);
        }
        private void CloseWeb()
        {
            if (webPanel == null || !webPanel.Visible) return;
            webPanel.Visible = false;
            // The sign-in a site just gave is written now, not only when the app quits.
            webContext?.GetCookieManager(null)?.FlushStore(null);
            // A file still arriving keeps the window's browser (an empty page, so nothing plays on); otherwise it goes.
            var browser = webPanel.Browser;
            if (browser != null && webDownloads.Count > 0) { if (browser.IsBrowserInitialized) browser.Load("about:blank"); }
            else DisposeWebBrowser();
            if (main != null && !main.IsDisposed) main.Focus();
        }
        private void DisposeWebBrowser()
        {
            var browser = webPanel?.Browser; if (browser == null) return;
            webPanel.Detach(); webPanel.Controls.Remove(browser);
            ignoreCefCloseUntil = DateTime.UtcNow.AddSeconds(2);
            browser.Dispose();
            foreach (var record in webDownloads.Values) { record.State = DownloadState.Canceled; record.Error = "downloadCanceled"; record.Finished = DateTime.Now; OnDownloadChanged(record.Clone()); }
            webDownloads.Clear(); lock (webCancels) webCancels.Clear(); lock (webTargets) webTargets.Clear();
        }
        // Everything the window kept: its browser, its profile in memory and the profile folder (now or at the next start).
        private void DisposeWeb()
        {
            if (webPanel != null) { webPanel.Visible = false; DisposeWebBrowser(); }
            // Chromium drops a profile only after its last page has closed; released sooner, it may lose what the page was saving.
            if (webContext != null) WaitWebPagesClosed(TimeSpan.FromSeconds(2));
            webContext?.Dispose(); webContext = null;
        }
        private void WebKey(System.Windows.Forms.Keys key)
        {
            var browser = webPanel?.Browser; if (browser == null || LockShown) return;
            if (key == (System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.Left)) webPanel.GoBack();
            else if (key == (System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.Right)) { if (browser.CanGoForward) browser.Forward(); }
            else if (key == System.Windows.Forms.Keys.F5) webPanel.Reload();
            else if (key == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.P)) { if (browser.IsBrowserInitialized) browser.Print(); }
            else if (key == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.D0) || key == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.NumPad0)) SetZoom(100);
            else if (key == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Oemplus) || key == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Add)) SetZoom(zoomPercent + 10);
            else if (key == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.OemMinus) || key == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Subtract)) SetZoom(zoomPercent - 10);
        }
        private void SetWebZoom()
        {
            var browser = webPanel?.Browser;
            if (browser != null && !browser.IsDisposed && browser.IsBrowserInitialized) browser.SetZoomLevel(Math.Log(zoomPercent / 100.0, 1.2));
        }
        // Moodle's sign-in from Univer: the form Univer posts is posted again from Moodle's own address in the window, so the
        // Moodle session lands in the window's profile. The page lives in memory and is served once; nothing is written to disk.
        private bool HandOffInside(string url, string method, List<KeyValuePair<string, string>> fields)
        {
            if (!OpensInside(url)) return false;
            var address = WebLinks.HandOffAddress(url);
            if (method == "POST" && address == null) return false;
            if (method != "POST") { ShowWeb(url); return WebShown; }
            var page = ResourceHandler.GetByteArray(SignOnPage.AutoSubmitPage(url, method, fields, "Moodle"), Encoding.UTF8, true);
            ShowWeb(address, browser =>
            {
                var pages = browser.ResourceRequestHandlerFactory as ResourceRequestHandlerFactory;
                if (pages == null) browser.ResourceRequestHandlerFactory = pages = new ResourceRequestHandlerFactory();
                pages.RegisterHandler(address, page, ResourceHandler.DefaultMimeType, true);
            });
            return WebShown;
        }
        private string WebDownloadTarget(string suggested)
        {
            var folder = DownloadService.DownloadsFolder();
            try { Directory.CreateDirectory(folder); } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
            // Chromium asks on its own thread, and two files of one name may be asked for before either is created.
            lock (webTargets)
            {
                var path = WebLinks.Target(folder, suggested, webTargets.Contains);
                if (path != null) webTargets.Add(path);
                return path;
            }
        }
        private readonly HashSet<string> webTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private void ReleaseWebTarget(string path) { if (path != null) lock (webTargets) webTargets.Remove(path); }
        private bool WebDownloadCancelled(int id) { lock (webCancels) return webCancels.Contains(id); }
        private void CancelWebDownload(string recordId)
        {
            foreach (var pair in webDownloads) if (pair.Value.Id == recordId) lock (webCancels) webCancels.Add(pair.Key);
        }
        private void WebDownloadProgress(int id, string path, string host, DownloadItem item)
        {
            DownloadRecord record;
            if (!webDownloads.TryGetValue(id, out record))
            {
                if (item.IsComplete || item.IsCancelled) { ReleaseWebTarget(path); return; }
                record = new DownloadRecord { FileName = Path.GetFileName(path), Source = host, State = DownloadState.Running };
                webDownloads[id] = record; SetStatus("downloading");
            }
            record.Received = item.ReceivedBytes; record.Total = item.TotalBytes > 0 ? item.TotalBytes : -1;
            var done = true;
            if (item.IsComplete) { record.State = DownloadState.Completed; record.Path = path; record.FileName = Path.GetFileName(path); record.Total = record.Received; }
            else if (item.IsCancelled) { record.State = DownloadState.Canceled; record.Error = "downloadCanceled"; }
            else if (!item.IsInProgress) { record.State = DownloadState.Failed; record.Error = "downloadFailed"; }
            else done = false;
            if (done) { record.Finished = DateTime.Now; webDownloads.Remove(id); lock (webCancels) webCancels.Remove(id); ReleaseWebTarget(path); }
            OnDownloadChanged(record.Clone());
            if (done) SetStatus(record.State == DownloadState.Completed ? "downloadComplete" : record.Error);
            // The window's browser stayed only for this file.
            if (done && webDownloads.Count == 0 && !WebShown) DisposeWebBrowser();
        }
        // Settings › Clear: a fresh profile for the window; the old folder goes now or, if Chromium still holds it, at the next start.
        private async Task<bool> ClearWebDataAsync()
        {
            if (closing || rotating) return false;
            try
            {
                DisposeWeb();
                var clean = prefs.Clone(); clean.WebProfile = Guid.NewGuid().ToString("N");
                await storage.SavePreferencesAsync(clean); prefs.WebProfile = clean.WebProfile;
                await Task.Delay(500);
                await storage.CleanOldWebProfilesAsync(clean.WebProfile);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
