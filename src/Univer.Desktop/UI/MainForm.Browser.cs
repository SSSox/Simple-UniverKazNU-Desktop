using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.DevTools;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private readonly HashSet<ChromiumWebBrowser> preparedBrowsers = new HashSet<ChromiumWebBrowser>();
        private readonly Dictionary<ChromiumWebBrowser, DevToolsClient> pageClients = new Dictionary<ChromiumWebBrowser, DevToolsClient>();
        private readonly Dictionary<ChromiumWebBrowser, string> documentScripts = new Dictionary<ChromiumWebBrowser, string>();
        private readonly KeyReleases keyReleases = new KeyReleases();
        private string BootstrapScript(bool viewer)
        { return "window.__univerBootstrapOrigin=location.origin;\n" + automation.DocumentScript(prefs, viewer, Credentials.SignInActive(credentials, prefs)); }
        // Document-start scripts embed language, view and sign-in state, so a preference change re-registers them. Refreshes
        // run one after another: overlapping ones would leave an extra bundle registered on every later page.
        private Task scriptRefresh = Task.FromResult(true);
        private Task RefreshDocumentScriptsAsync() { scriptRefresh = RefreshAfterAsync(scriptRefresh); return scriptRefresh; }
        private async Task RefreshAfterAsync(Task previous)
        {
            // An earlier refresh handles its own failures; one that still failed must not stop this one.
            try { await previous; } catch (Exception) { }
            await RefreshDocumentScriptsNowAsync();
        }
        private async Task RefreshDocumentScriptsNowAsync()
        {
            foreach (var pair in new List<KeyValuePair<ChromiumWebBrowser, DevToolsClient>>(pageClients))
            {
                var browser = pair.Key; string previous;
                if (closing || browser.IsDisposed || !documentScripts.TryGetValue(browser, out previous)) continue;
                try
                {
                    var added = pair.Value.Page.AddScriptToEvaluateOnNewDocumentAsync(BootstrapScript(browser == document));
                    await TaskDeadline.AwaitAsync(added, TimeSpan.FromSeconds(8), stopping);
                    var identifier = (await added).Identifier;
                    if (string.IsNullOrEmpty(identifier) || browser.IsDisposed || !pageClients.ContainsKey(browser)) continue;
                    documentScripts[browser] = identifier;
                    await TaskDeadline.AwaitAsync(pair.Value.Page.RemoveScriptToEvaluateOnNewDocumentAsync(previous), TimeSpan.FromSeconds(8), stopping);
                }
                catch (Exception) { /* The polling tick still applies the current preferences after every load. */ }
            }
        }
        // A tick can still see the previous, fully loaded document while the next request is in flight.
        // Revealing it would flash the old page and invite repeated clicks; wait for the new document.
        private ChromiumWebBrowser awaitingCommit;
        private DateTime awaitingSince;
        private void CommitSeen(ChromiumWebBrowser browser) { if (awaitingCommit == browser) awaitingCommit = null; }
        private void SetPresentation(ChromiumWebBrowser browser, bool busy) { SetPresentation(browser, busy, false); }
        private void SetPresentation(ChromiumWebBrowser browser, bool busy, bool force)
        {
            if (closing || browser != Active) return;
            if (!busy && !force && awaitingCommit == browser && DateTime.UtcNow - awaitingSince < TimeSpan.FromSeconds(20)) return;
            var wasBusy = loading.Visible;
            if (busy && !wasBusy) presentationBegan = DateTime.UtcNow;
            var reveal = ++revealGeneration;
            if (busy) { var shown = !loading.Visible; loading.Visible = true; if (!DownloadsVisible) { loading.BringToFront(); if (OfflineShown) offlineView.BringToFront(); } if (shown && loading.IsHandleCreated) loading.Refresh(); return; }
            if (!wasBusy) return;
            if (Program.FirstReveal == 0) Program.FirstReveal = Program.Clock.ElapsedMilliseconds;
            if (selfTest || accountReview) { loading.Visible = false; ScheduleShape(browser); QueueAsync(UpdateSiteTitleAsync); return; }
            // Uncover a few frames later, once Chromium has painted the revealed page; uncovering at once
            // could show the compositor's empty surface for a frame.
            QueueAsync(async () =>
            {
                await Task.Delay(48);
                if (closing || reveal != revealGeneration) return;
                loading.Visible = false; ScheduleShape(browser);
                if (prefs.Animations && !Motion.KeyboardInput && browser == Active)
                    await AutomationController.EvaluateAsync(browser, "if(window.__univerMotionUI && !window.__univerAutomationV1?.probe().hasPendingForm){window.__univerMotionUI.reveal(document.getElementById('l4'),'content');} 'motion'");
                await UpdateSiteTitleAsync();
            });
        }
        private int revealGeneration;
        // What the first session found and the address each session opened with; the live self-test reads them, and
        // freshForQa lets it take the new-profile road a self-test otherwise skips.
        private bool sessionStarted, profileAbsentAtStart, freshForQa, loggingOut, languageHop, accountStarting;
        private string sessionAddress;
        private void CreateSession()
        {
            var epoch = ++sessionEpoch;
            // A new profile (first start, or the one a logout makes) has no language cookie, so the site would answer
            // in Kazakh until automation switched it. Its first page is the chosen flag's link, which redirects to the
            // start page in that language; an app load carries no user gesture, so it changes no preference.
            var absent = !System.IO.Directory.Exists(storage.ProfilePath(prefs.Profile));
            if (!sessionStarted) { sessionStarted = true; profileAbsentAtStart = absent; }
            var fresh = (!selfTest || freshForQa) && absent;
            context = new RequestContext(new RequestContextSettings { CachePath = storage.ProfilePath(prefs.Profile), PersistSessionCookies = true });
            guard = new SessionGuard();
            automation = new AutomationController(guard, key => { if (epoch == sessionEpoch) SetStatus(key); }, CloseDocument, SetPresentation);
            automation.SignedInContent = () => { if (epoch == sessionEpoch) SignedInWork(); };
            automation.RecoveryNeeded = () => { if (epoch == sessionEpoch) PageNeedsRecovery(); };
            automation.Dark = Theme.Dark;
            resumeDownload = null;
            lastDownloadPath = null; siteTitle = siteSection = "";
            // Download records belong to the app-wide history, so updates from an ended session are still kept.
            downloads = new DownloadService(context, key => Dispatch(() => { if (epoch == sessionEpoch) SetStatus(key); }),
                record => Dispatch(() => OnDownloadChanged(record)), id => Dispatch(() => ForgetRecord(id)));
            sessionAddress = fresh ? SitePolicy.LanguageSwitch(prefs.Language) : selfTest ? "about:blank" : SitePolicy.Home;
            main = CreateBrowser(sessionAddress, false);
            canvas.Controls.Add(main); main.BringToFront(); error = false; timer.Start();
            languageHop = false;
            presentationBegan = DateTime.UtcNow; ShowRoute(fresh && OpensOnSignIn() ? SitePolicy.LoginAddress : SitePolicy.Home); loading.Visible = true; loading.BringToFront();
            sessionCreatedForQa?.Invoke();
        }
        // A new profile the app will not sign in by itself (a logout; a start without a saved sign-in or with a paused one) opens
        // on the site's sign-in page: the cover draws that page from the start instead of the home page. A new account's
        // rotation is paused only until it ends, and signs in.
        private bool OpensOnSignIn() { return loggingOut || !rotating && !Credentials.SignInActive(credentials, prefs); }
        // A language flag only redirects to a page: the placeholder already shown (that page's) stays, for the flag link and for
        // its redirect to the home page (a new profile's flag link goes on through the home page to the sign-in page). So does the
        // one of the page asked for while the app signs in by itself on the sign-in page (a new account's session too, paused
        // only until its rotation ends); a refused or stopped sign-in shows that page.
        private bool KeepsPlaceholder(string url, bool afterFlag = false)
        {
            return SitePolicy.Language(url) != null || afterFlag && SitePolicy.SamePage(url, SitePolicy.Home)
                || SitePolicy.LoginPage(url) && (accountStarting || Credentials.SignInActive(credentials, prefs)) && !guard.Blocked;
        }
        private ChromiumWebBrowser CreateBrowser(string url, bool viewer)
        {
            var epoch = sessionEpoch;
            var browser = new ChromiumWebBrowser("about:blank", context) { Dock = DockStyle.Fill };
            // Before a page paints, Chromium shows the theme's page colour instead of a white or black surface.
            var surface = Theme.Background; browser.BrowserSettings.BackgroundColor = Cef.ColorSetARGB(255, surface.R, surface.G, surface.B);
            // The first page of a session may pass the site's read notice; later home/news clicks skip it.
            var loadedOnce = false;
            var submissions = new FormSubmissions();
            Action<Action> current = operation => Dispatch(() => { if (epoch == sessionEpoch && !browser.IsDisposed) operation(); });
            browser.KeyboardHandler = new ShortcutHandler(key => current(() => { var message = new Message(); ProcessCmdKey(ref message, key); }), viewer, keyReleases);
            browser.FindHandler = new FindResults((source, total, ordinal, final) => current(() => findBar.ShowResult(source, total, ordinal, final)));
            browser.RequestHandler = new NavigationHandler(current, viewer)
            {
                External = value => PageLink(browser, value), OpenDocument = OpenDocument, LeaveDocument = LeaveDocument,
                HandOffPending = value => !viewer && SignOnPending(value),
                OpenHandOff = (value, method, fields) => current(() => OpenHandOff(value, method, fields)),
                OpenOnlineTest = value => current(() => OpenOnlineTest(value)),
                Shortcut = value => !viewer && loadedOnce ? SitePolicy.NewsShortcut(value) : null,
                Load = value => current(() => browser.Load(value)),
                Download = value => QueueAsync(() => DownloadAsync(value)),
                Language = value => QueueAsync(() => AdoptLanguageAsync(value)),
                Logout = () => QueueAsync(LogoutAsync),
                Navigating = (value, method) => { if (Active == browser) BeginNavigation(browser, viewer, value, method); },
                BeforeNavigate = value =>
                { if (!viewer && SitePolicy.SingleSignOn(value)) System.Threading.Interlocked.Exchange(ref signOnUntil, DateTime.UtcNow.AddSeconds(90).Ticks); },
                LoginRedirect = value => { if (!viewer) automation.ResumeAfterLogin(value); },
                Submissions = submissions
            };
            browser.LifeSpanHandler = new PopupHandler(current, value =>
            {
                if (SitePolicy.ProtectedDownload(value) || SitePolicy.DocumentDownload(value)) QueueAsync(() => DownloadAsync(value));
                else if (SitePolicy.PrintDocument(value)) OpenDocument(value);
                else if (SitePolicy.Trusted(value)) browser.Load(value);
                else PageLink(browser, value);
            });
            browser.DownloadHandler = new SafeDownloadHandler(current, (value, name) => QueueAsync(() => DownloadAsync(value, name)), SetStatus, submissions);
            browser.LoadingStateChanged += (s, e) => current(() =>
            { if (!e.IsLoading) CommitSeen(browser); if (Active == browser) { if (!e.IsLoading && resumeDownload != null) resumeDownload.Loaded = true; SetStatus(e.IsLoading ? "loading" : "ready"); UpdateLocation(); UpdateHistoryButtons(); if (!e.IsLoading) QueueAsync(UpdateSiteTitleAsync); } });
            browser.FrameLoadStart += (s, e) => { if (e.Frame.IsMain) current(() => { CommitSeen(browser); ForgetFind(browser); }); };
            browser.LoadError += (s, e) =>
            {
                if (e.Frame.IsMain) current(() => CommitSeen(browser));
                if (e.Frame.IsMain && e.ErrorCode != CefErrorCode.Aborted) current(() =>
                {
                    if (Active != browser) return;
                    error = true; SetPresentation(browser, false, true); failureCode = e.ErrorCode.ToString(); NoteEvent("load failed " + failureCode + " " + e.FailedUrl);
                    // A certificate error stays with the user; a lost connection reloads by itself.
                    var certificate = failureCode.StartsWith("Cert", StringComparison.Ordinal);
                    SetStatus(certificate ? "certificateError" : "networkError");
                    if (!certificate) ScheduleRecovery();
                    SetUnreachable(!certificate && browser == main);
                });
            };
            browser.FrameLoadEnd += (s, e) =>
            {
                if (!e.Frame.IsMain) return;
                if (SitePolicy.Trusted(e.Url)) loadedOnce = true;
                var code = e.HttpStatusCode;
                current(() =>
                {
                    // Every finished page of the site window is read for notifications once it is shown.
                    if (!viewer && SitePolicy.Trusted(e.Url)) noticeScanDue = true;
                    if (Active != browser) return;
                    // A late error event from an earlier load must not outlive a trusted page that has since loaded successfully;
                    // CEF reports its own error pages with a chrome-error URL, which is not trusted.
                    if (error && code >= 200 && code < 400 && SitePolicy.Trusted(e.Url)) error = false;
                    // The site answered (even with an error page of its own): the kept week gives way to it.
                    if (browser == main && code >= 200 && SitePolicy.Trusted(e.Url)) SetUnreachable(false);
                    // Without automatic sign-in nothing else marks a recovered page as working again.
                    if (code >= 200 && code < 400 && SitePolicy.Trusted(e.Url) && (credentials == null || !credentials.Usable() || prefs.AutoLoginPaused)) RecoverySucceeded();
                    if (code >= 400)
                    {
                        error = true; SetPresentation(browser, false, true); failureCode = code.ToString(); SetStatus("httpError");
                        // An overloaded or unavailable server reloads by itself; a missing page does not.
                        if (code >= 500 || code == 408 || code == 429) ScheduleRecovery();
                    }
                    SetZoom(zoomPercent);
                    if (!error) QueueAsync(() => automation.TickAsync(browser, credentials, prefs, viewer));
                });
            };
            var started = false;
            Action initialize = () =>
            {
                if (started || !browser.IsBrowserInitialized) return; started = true;
                // Every browser window, the file viewer's included, lives on CEF's one UI thread: the first one hooks Ctrl+wheel for all.
                current(() => { ctrlWheel.Attach(browser.GetBrowser().GetHost().GetWindowHandle()); QueueAsync(() => PrepareBrowserAsync(browser, url, viewer, epoch, current)); });
            };
            browser.IsBrowserInitializedChanged += (s, e) => initialize(); initialize();
            return browser;
        }
        // Before its first page: a DevTools session that receives the page's binding messages with the context they came
        // from, and the document-start scripts. Only then does the browser load url.
        private async Task PrepareBrowserAsync(ChromiumWebBrowser browser, string url, bool viewer, int epoch, Action<Action> current)
        {
            var deadline = TimeSpan.FromSeconds(8);
            try
            {
                var client = browser.GetDevToolsClient(); pageClients[browser] = client;
                // CefSharp 109's PostMessage event exposes a short-lived native frame on a pool thread.
                // Runtime bindings carry managed context IDs; copy the engine's origin before dispatch.
                var contexts = new ScriptContexts();
                client.Page.FrameNavigated += (s, e) => { if (string.IsNullOrEmpty(e.Frame.ParentId)) contexts.MainFrame(e.Frame.Id); };
                client.AddEventHandler<RuntimeContextEvent>("Runtime.executionContextCreated", (s, e) =>
                { if (e.Context != null) contexts.Created(e.Context.Id, e.Context.Origin, e.Context.AuxData?.IsDefault == true ? e.Context.AuxData.FrameId : null); });
                client.Runtime.ExecutionContextDestroyed += (s, e) => contexts.Destroyed(e.ExecutionContextId);
                client.Runtime.ExecutionContextsCleared += (s, e) => contexts.Cleared();
                client.Runtime.BindingCalled += (s, e) =>
                {
                    if (e.Name != "__univerNativeNotify") return;
                    bool trusted, main; contexts.Check(e.ExecutionContextId, out trusted, out main);
                    var payload = e.Payload;
                    current(() =>
                    {
                        // Only the app's own scripts hold the key; anything else from the page is not ours.
                        var message = automation.Unwrap(payload);
                        ReceivePageMessage(browser, viewer, message, trusted && message != null, main);
                    });
                };
                await TaskDeadline.AwaitAsync(client.Page.EnableAsync(), deadline, stopping);
                var tree = client.Page.GetFrameTreeAsync(); await TaskDeadline.AwaitAsync(tree, deadline, stopping);
                contexts.MainFrame((await tree).FrameTree.Frame.Id);
                await TaskDeadline.AwaitAsync(client.Runtime.EnableAsync(), deadline, stopping);
                await TaskDeadline.AwaitAsync(client.Runtime.AddBindingAsync("__univerNativeNotify"), deadline, stopping);
                var addScript = client.Page.AddScriptToEvaluateOnNewDocumentAsync(BootstrapScript(viewer));
                await TaskDeadline.AwaitAsync(addScript, deadline, stopping);
                var registered = await addScript;
                if (string.IsNullOrEmpty(registered.Identifier)) throw new InvalidOperationException("Document script registration failed");
                if (browser.IsDisposed || closing || epoch != sessionEpoch) return;
                preparedBrowsers.Add(browser); documentScripts[browser] = registered.Identifier;
                if (url == "about:blank") SetPresentation(browser, false); else browser.Load(url);
            }
            catch (Exception)
            { if (!closing && Active == browser) { error = true; failureCode = "BrowserPreparation"; SetPresentation(browser, false); SetStatus("networkError"); } }
        }
        // The shown browser goes ahead with a navigation: remember it for Retry, cover the page until it is prepared.
        private void BeginNavigation(ChromiumWebBrowser browser, bool viewer, string url, string method)
        {
            if (accountReview) ReviewNavigated(url, method);
            if (SitePolicy.SingleSignOn(url) && method == "GET") BeginSignOn();
            // «Онлайн тест» leaves for another site and the site window loads its page again: the placeholder is that page's.
            var route = viewer ? "document" : url;
            if (!viewer && SitePolicy.OnlineTest(url) && method == "GET")
            {
                var now = DateTime.UtcNow; var page = SitePolicy.OnlineTestReturnPage(lastGet, onlineTestReturn, onlineTestReturnAt, now);
                if (page != onlineTestReturn || page == lastGet) onlineTestReturnAt = now;
                onlineTestReturn = page; route = page ?? url;
            }
            lastMethod = method; if (method == "GET") lastGet = url;
            var afterFlag = languageHop; if (!viewer) languageHop = SitePolicy.Language(url) != null;
            awaitingCommit = browser; awaitingSince = DateTime.UtcNow; if (viewer || !KeepsPlaceholder(url, afterFlag)) ShowRoute(route);
            error = false; messages.BeginNavigation(); automation.Navigating(); presentationBegan = DateTime.UtcNow; SetPresentation(browser, true); RenderFeedback(); UpdatePollingRate();
            if (browser == main) OfflineNavigation();
        }
        private void ReceivePageMessage(ChromiumWebBrowser browser, bool viewer, string value, bool trusted, bool mainFrame)
        {
            // The site window keeps reading its sections while a printed document is shown over it.
            if (value != null && value.StartsWith(Notices.MessagePrefix, StringComparison.Ordinal)) { ReceiveNoticeMessage(browser, viewer, value, trusted, mainFrame); return; }
            if (!trusted || value == null || Active != browser) return;
            // A press in any frame of the shown page.
            if (value == "univer-wake") { PageWoken(browser); return; }
            if (value == "univer-busy") { SetPresentation(browser, true); QueueAsync(() => automation.TickAsync(browser, credentials, prefs, viewer)); return; }
            // The main document has parsed: one tick now uncovers it (or signs in) without waiting for the 200 ms poll.
            if (value == "univer-dom") { if (mainFrame) { automation.DocumentParsed(); QueueAsync(() => automation.TickAsync(browser, credentials, prefs, viewer)); } return; }
            if (value.StartsWith("univer-ajax|", StringComparison.Ordinal))
            {
                // Correlated outcomes come only from the main document's automation, which also watches its same-origin frames.
                if (!mainFrame) return;
                var parts = value.Split('|');
                if (parts.Length == 4 && (parts[1] == "reauth" || parts[1] == "agreement") && (parts[3] == "success" || parts[3] == "failure"))
                    automation.AjaxOutcome(parts[1], parts[2], parts[3] == "success");
                return;
            }
            // The language is never taken from a message: only the user's own click on a flag (a main-frame navigation with a
            // gesture, BrowserHandlers) changes it.
            if (mainFrame && !viewer && value.StartsWith(Shortcuts.MessagePrefix, StringComparison.Ordinal)) QueueAsync(() => PinAsync(browser, value.Substring(Shortcuts.MessagePrefix.Length)));
            if (mainFrame && !viewer && value.StartsWith(DownloadBatch.MessagePrefix, StringComparison.Ordinal)) QueueAsync(() => DownloadSectionAsync(browser, value.Substring(DownloadBatch.MessagePrefix.Length)));
            if (mainFrame && !viewer && value.StartsWith(ScheduleExport.MessagePrefix, StringComparison.Ordinal)) { var kind = ScheduleExport.Kind(value.Substring(ScheduleExport.MessagePrefix.Length)); QueueAsync(() => ScheduleExportAsync(browser, kind)); }
            if (mainFrame && !viewer && value.StartsWith(GradeReport.MessagePrefix, StringComparison.Ordinal)) { var report = value.Substring(GradeReport.MessagePrefix.Length); QueueAsync(() => ReportAsync(browser, GradeReport.Action(report), GradeReport.Page(report))); }
        }
        private void OpenDocument(string url)
        {
            if (closing || commandBusy || !SitePolicy.PrintDocument(url)) return;
            HideDownloads(); automation.Navigating();
            if (document != null) { document.Load(url); return; }
            sourceGet = lastGet; sourceMethod = lastMethod; siteTitle = siteSection = "";
            main.Visible = false; document = CreateBrowser(url, true); canvas.Controls.Add(document); document.BringToFront(); UpdateOffline();
            presentationBegan = DateTime.UtcNow; ShowRoute("document"); SetPresentation(document, true);
            messages.BeginNavigation(); Localize();
        }
        private void CloseDocument()
        {
            if (document == null) return;
            automation.Navigating(); var closed = document; document = null;
            ignoreCefCloseUntil = DateTime.UtcNow.AddSeconds(2);
            canvas.Controls.Remove(closed); closed.Dispose();
            preparedBrowsers.Remove(closed); documentScripts.Remove(closed);
            DevToolsClient pageClient; if (pageClients.TryGetValue(closed, out pageClient)) { pageClient.Dispose(); pageClients.Remove(closed); }
            // The page behind the viewer was hidden; keep the placeholder over it until it has painted again.
            if (!selfTest && !accountReview) { ShowRoute(sourceGet); loading.Visible = true; loading.BringToFront(); }
            if (main != null) { main.Visible = true; main.BringToFront(); if (loading.Visible) loading.BringToFront(); }
            SetPresentation(main, false, true);
            lastGet = sourceGet; lastMethod = sourceMethod; siteTitle = siteSection = "";
            error = false; messages.Update("ready"); Localize(); UpdateHistoryButtons(); QueueAsync(UpdateSiteTitleAsync);
            UpdateOffline();
        }
        // Opens a viewer link in main like the same click there; the link back to the source page only closes the viewer.
        private void LeaveDocument(string url)
        {
            if (document == null || commandBusy || closing) return;
            CloseDocument();
            if (main == null || (lastMethod == "GET" && SitePolicy.SamePage(url, lastGet))) return;
            main.Load(SitePolicy.NewsShortcut(url) ?? url);
        }
        private void CloseBrowsers()
        {
            HideDownloads();
            ignoreCefCloseUntil = DateTime.UtcNow.AddSeconds(2);
            foreach (var pageClient in pageClients.Values) pageClient.Dispose();
            pageClients.Clear(); documentScripts.Clear();
            if (document != null) { canvas.Controls.Remove(document); document.Dispose(); document = null; }
            if (main != null) { canvas.Controls.Remove(main); main.Dispose(); main = null; }
            preparedBrowsers.Clear();
        }
        private void Dispatch(Action action)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!closing && !IsDisposed) action(); })); }
            catch (InvalidOperationException) { }
        }
    }
}
