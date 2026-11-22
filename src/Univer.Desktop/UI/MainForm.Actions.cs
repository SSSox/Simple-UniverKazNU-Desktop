using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private async Task OpenSettingsAsync(bool first = false)
        {
            if (settingsOpen || commandBusy || closing || LockShown) return;
            settingsOpen = true;
            var installUpdate = false;
            try
            {
                var shown = prefs.Clone(); shown.AutoLoginPaused |= guard.Stopped;
                using (var form = new SettingsForm(credentials, shown, first, updates, first ? 1f : SidebarZoom))
                {
                    form.OpenReleasePage = () => External(Updates.ReleasePage(updates.Available));
                    form.StartupState = ReadAutostart();
                    form.ApplyAsync = (action, next, language, readable) => ApplySettingsAsync(action, next, language, readable, form.Tray, form.Animations, form.SelectedTheme, form.ExternalLinks, form.StartupChanged ? form.Startup : (bool?)null, form.Reminders, form.ReminderLead, form.Grades, form.GradeInterval, form.StartPage);
                    form.ClearWebData = ClearWebDataAsync; form.SaveDiagnostics = SaveDiagnosticsAsync;
                    form.PinSet = () => PinSet; form.SetPin = SetPinAsync; form.ChangePin = ChangePinAsync; form.DisablePin = DisablePinAsync;
                    form.ShowDialog(this);
                    // Closing first launch with X also means manual login, never implicit credential saving.
                    if (first && form.Action == SettingsAction.None) await ApplySettingsAsync(SettingsAction.Manual, null, prefs.Language, prefs.Readable);
                    installUpdate = form.UpdateRequested;
                }
            }
            finally { settingsOpen = false; }
            if (installUpdate) ShowUpdate(true);
        }
        private async Task<bool> ApplySettingsAsync(SettingsAction action, Credentials next, string language, bool readable, bool? tray = null, bool? animations = null, string theme = null, bool? externalLinks = null, bool? startup = null, bool? reminders = null, int? reminderLead = null, bool? grades = null, int? gradeInterval = null, string startPage = null)
        {
            if (commandBusy || closing) return false;
            commandBusy = true; SetCommandEnabled(false);
            var languageChanged = prefs.Language != language; var rotated = false; var gradesWere = prefs.Grades;
            var logout = action == SettingsAction.Remove;
            // Settings' own button says it is working; a logout puts no banner over the window (the owner's request).
            if (!logout) SetStatus("saving");
            try
            {
                if (logout) { await EndSessionAsync(true); return !closing; }
                var change = action == SettingsAction.Manual ? SettingsChange.AppearanceOnly : SettingsDecision.Classify(credentials, next, prefs.AutoLoginPaused || guard.Stopped);
                var updated = prefs.Clone(); updated.Language = language; updated.Readable = readable; if (tray.HasValue) updated.Tray = tray.Value; if (animations.HasValue) updated.Animations = animations.Value; if (theme != null) updated.Theme = ThemeChoice.Normalize(theme); if (externalLinks.HasValue) updated.ExternalLinks = externalLinks.Value;
                if (reminders.HasValue) updated.Reminders = reminders.Value; if (reminderLead.HasValue) updated.ReminderLead = ClassReminders.Lead(reminderLead.Value);
                if (grades.HasValue) updated.Grades = grades.Value; if (gradeInterval.HasValue) updated.GradeInterval = GradeNotice.Interval(gradeInterval.Value); if (startPage != null) updated.StartPage = StartPages.Normalize(startPage);
                if (action == SettingsAction.Manual) updated.AutoLoginPaused = true;
                if (change != SettingsChange.AppearanceOnly)
                {
                    automation.Navigating(); guard.Stop(); prefs.AutoLoginPaused = true;
                    // Persist the stop before replacing credentials or clearing the browser, and with it the mark that the
                    // old session still has to go: a run that ends before the profiles are replaced is finished by the next.
                    updated.AutoLoginPaused = true; updated.SessionClearPending = true; await storage.SavePreferencesAsync(updated); prefs = updated; guard.Stop();
                    if (change == SettingsChange.NewCredentials) { await SaveAccountAsync(next); credentials = next; }
                    // The new session is paused until the rotation ends, and then signs in by itself.
                    rotated = true; accountStarting = true;
                    try
                    {
                        await RotateSessionAsync();
                        updated = prefs.Clone(); updated.AutoLoginPaused = false;
                        await storage.SavePreferencesAsync(updated); prefs = updated;
                    }
                    finally { accountStarting = false; }
                }
                else { await storage.SavePreferencesAsync(updated); prefs = updated; }
                if (closing) return false;
                storageFailed = false; messages.ClearStorageError(); GradesChanged(gradesWere);
                // Windows' own line, written after the preferences: a refused write leaves the rest saved and says so.
                var startupRefused = startup.HasValue && !ApplyAutostart(startup.Value);
                if (action == SettingsAction.Manual) { messages.FinishSettings(); SetStatus("manualSignIn"); }
                else SetStatus(startupRefused ? "autostartFailed" : "settingsSaved");
                Motion.Enabled = prefs.Animations && !selfTest && !accountReview; ApplyTheme(); Localize(); if (languageChanged) automation.LanguageSelected();
                await ApplyPresentationAsync(); await RefreshDocumentScriptsAsync();
                return true;
            }
            catch (Exception e)
            {
                if (closing) return false;
                messages.FinishSettings();
                // The saved sign-in is in place, and a Chromium deadline while ending the old profile already started a
                // fresh one: it signs in by itself like any other start, so the change took effect.
                if (rotated && !StorageFault(e))
                {
                    prefs.AutoLoginPaused = false; QueueAsync(SavePreferencesAsync); SetStatus("sessionReset"); Localize();
                    return true;
                }
                guard.Stop(); ReportSessionFault(e, logout); Localize();
                return false;
            }
            finally { commandBusy = false; if (!closing) SetCommandEnabled(true); }
        }
        private static bool StorageFault(Exception e) { return e is IOException || e is UnauthorizedAccessException || e is System.Security.Cryptography.CryptographicException; }
        // A failed disk write is a storage error. A Chromium deadline while ending the old session is not: the session was
        // already replaced by a fresh one with automatic sign-in paused, which is all a logout asks for.
        private void ReportSessionFault(Exception e, bool logout)
        {
            if (StorageFault(e)) { storageFailed = true; SetStatus("storageError"); }
            // A logout's fresh session already shows the sign-in page; it needs no banner.
            else if (!logout) SetStatus("automationFailed");
        }
        private async Task EndSessionAsync(bool remove)
        {
            var stopped = prefs.Clone(); stopped.AutoLoginPaused = true; stopped.SessionClearPending = true;
            guard.Stop(); automation.Navigating(); prefs.AutoLoginPaused = true;
            await storage.SavePreferencesAsync(stopped); prefs = stopped;
            if (remove) { await RemoveAccountAsync(); credentials = null; }
            // The sidebar says so while the old session ends: under the cover it must not still read «Вход сохранён».
            if (!closing) UpdateAccountLine();
            loggingOut = true;
            try { await RotateSessionAsync(); } finally { loggingOut = false; }
            // The sign-in page in the chosen language says it all: no banner over it (the owner's request).
            if (!closing) { messages.FinishSettings(); RenderFeedback(); Localize(); }
        }
        private async Task LogoutAsync()
        {
            if (commandBusy || closing) return;
            commandBusy = true; SetCommandEnabled(false);
            try { await EndSessionAsync(false); }
            catch (Exception e) { if (!closing) { guard.Stop(); ReportSessionFault(e, true); Localize(); } }
            finally { commandBusy = false; if (!closing) SetCommandEnabled(true); }
        }
        private async Task RotateSessionAsync()
        {
            rotating = true; timer.Stop(); automation.Navigating(); downloads.Dispose(); sessionEpoch++; ForgetShapes(); DisposeWeb();
            var replaced = false;
            try
            {
                await ClearNoticesAsync(); await ClearScheduleAsync(); await ClearExamsAsync(); await ClearGradesAsync(); await ClearLastGradesAsync();
                var manager = context.GetCookieManager(null);
                await TaskDeadline.AwaitAsync(manager.DeleteCookiesAsync(), TimeSpan.FromSeconds(8), stopping);
                rotationFaultForQa?.Invoke();
                await TaskDeadline.AwaitAsync(manager.FlushStoreAsync(), TimeSpan.FromSeconds(8), stopping);
                await TaskDeadline.AwaitAsync(context.ClearHttpAuthCredentialsAsync(), TimeSpan.FromSeconds(8), stopping);
                if (main != null && main.IsBrowserInitialized && !main.IsDisposed)
                {
                    using (var client = main.GetDevToolsClient())
                        await TaskDeadline.AwaitAsync(client.Storage.ClearDataForOriginAsync(SitePolicy.Origin, "all"), TimeSpan.FromSeconds(8), stopping);
                }
                stopping.ThrowIfCancellationRequested();
                CloseBrowsers(); context.Dispose(); context = null;
                // The other-site window's sign-ins (Moodle) belonged to the session that ends here.
                var clean = prefs.Clone(); clean.Profile = Guid.NewGuid().ToString("N"); clean.WebProfile = Guid.NewGuid().ToString("N"); clean.AutoLoginPaused = true; clean.SessionClearPending = false;
                await storage.SavePreferencesAsync(clean); prefs = clean;
                stopping.ThrowIfCancellationRequested();
                CreateSession(); replaced = true; messages.NewSession(); lastGet = SitePolicy.Home; lastMethod = "GET";
                await storage.CleanOldProfilesAsync(clean.Profile); await storage.CleanOldWebProfilesAsync(clean.WebProfile);
            }
            catch (Exception) when (!closing && !replaced) { RecoverSession(); throw; }
            finally { rotating = false; if (!closing) timer.Start(); }
        }
        // A rotation stopped part-way has already ended the old session (its events no longer match sessionEpoch), so start
        // fresh empty profiles, for the other-site window too; the stored pause keeps automatic sign-in off, as the request
        // intended.
        private void RecoverSession()
        {
            CloseBrowsers(); context?.Dispose(); context = null;
            var fresh = prefs.Clone(); fresh.Profile = Guid.NewGuid().ToString("N"); fresh.WebProfile = Guid.NewGuid().ToString("N");
            fresh.AutoLoginPaused = true; fresh.SessionClearPending = false; prefs = fresh;
            CreateSession(); messages.NewSession(); lastGet = SitePolicy.Home; lastMethod = "GET";
            QueueAsync(SavePreferencesAsync);
        }
        private async Task SavePreferencesAsync()
        {
            try { await storage.SavePreferencesAsync(prefs); if (!closing && !storageFailed) { messages.ClearStorageError(); RenderFeedback(); } }
            catch (Exception) { if (!closing) { storageFailed = true; SetStatus("storageError"); } }
        }
        private async Task SetReadableAsync(bool value)
        {
            if (commandBusy || closing) return;
            prefs.Readable = value; Localize(); await SavePreferencesAsync(); await ApplyPresentationAsync(); await RefreshDocumentScriptsAsync();
        }
        private async Task ApplyPresentationAsync()
        {
            if (closing || rotating) return;
            // Keep the same session state when rendering: a layout toggle must not uncover a pending login.
            // Explicit logout remains suspended through AutoLoginPaused.
            await automation.TickAsync(Active, credentials, prefs, document != null);
        }
        private async Task AdoptLanguageAsync(string value)
        {
            if (value != "ru" && value != "kz" && value != "en") return;
            if (prefs.Language == value || commandBusy) return;
            prefs.Language = value; automation.LanguageSelected(); Localize(); await SavePreferencesAsync(); await RefreshDocumentScriptsAsync();
        }
        private void SetCommandEnabled(bool enabled)
        { foreach (var control in new Control[] { toolbar, sidebar, canvas }) control.Enabled = enabled; }
        private void QueueAsync(Func<Task> operation)
        {
            Dispatch(async () =>
            {
                try { await operation(); }
                catch (Exception) { if (!closing) SetStatus("networkError"); }
            });
        }
        // again: the user chose "Download again" for a file the list already has.
        private async Task DownloadAsync(string url, string suggestedName = null, bool again = false)
        {
            if (closing || rotating || commandBusy) return;
            var browser = Active; var service = downloads;
            var agent = await AutomationController.EvaluateAsync(browser, "navigator.userAgent");
            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                var encoded = new JavaScriptSerializer().Serialize(url);
                suggestedName = await AutomationController.EvaluateAsync(browser,
                    "(function(){var a=Array.from(document.querySelectorAll('a[href]')).find(function(a){return a.href===" + encoded + ";});return a?(a.getAttribute('download')||a.textContent.trim()):'';})()");
            }
            // A logout or credential change that began during the page reads ends this download before it starts.
            if (service != downloads || closing || rotating || commandBusy) return;
            var key = DownloadKeys.Of(url); var source = document == null ? siteTitle : "";
            // A file already downloaded and still on disk: offer it instead of saving a second copy.
            var earlier = again || url == resumedUrl ? null : history?.Downloaded(key, suggestedName, source);
            if (earlier != null) { duplicate = new PendingDownload { Url = url, Name = suggestedName, Earlier = earlier }; SetStatus("downloadDuplicate"); return; }
            if (downloadProbe != null && downloadProbe(url)) return;
            // A UMKD file goes to its course and section under "Univer УМКД"; the page says which they are.
            var folder = SitePolicy.ProtectedDownload(url) ? StudyMaterials.Folder(document == null ? await AutomationController.EvaluateAsync(browser, StudyMaterials.Place(url)) : null) : null;
            if (service != downloads || closing || rotating || commandBusy) return;
            string link = null;
            var result = await service.DownloadAsync(url, browser?.Address, agent, suggestedName, source, key, folder, found => link = found);
            // A material that is the teacher's link opens like any other link the user clicks.
            if (result == "studyLink")
            {
                if (service != downloads || closing || rotating) return;
                if (SitePolicy.StudyLink(link)) OpenWebLink(link); else SetStatus("externalBlocked");
                return;
            }
            // Nothing was saved and nothing keeps running, so the click gets an answer instead of silence.
            if (result == "offOrigin" || result == "downloadBusy") { SetStatus(result); return; }
            if (result != "downloadLogin" || service != downloads || closing || rotating || browser != Active || browser == null || browser.IsDisposed) return;
            // The server ended the session behind an idle page. With saved sign-in, reload the page so the usual login
            // automation signs in again, then repeat this GET once; an explicit logout or a stopped guard stays manual.
            // A section waiting for its sign-in keeps its place: the same sign-in serves the student's next click.
            if (credentials == null || prefs.AutoLoginPaused || guard.Blocked || url == resumedUrl || (resumeDownload != null && (resumeDownload.Url == url || resumeDownload.Run != null))) return;
            resumeDownload = new PendingDownload { Url = url, Name = suggestedName, Until = DateTime.UtcNow.AddSeconds(90) };
            SetStatus("downloadSigningIn");
            browser.Load(lastMethod == "GET" && SitePolicy.Trusted(browser.Address) ? browser.Address : lastGet);
        }
        // Run and Rest: a section's «Скачать все» that goes on from Url, the refused file, after the sign-in.
        private sealed class PendingDownload { public string Url, Name; public DateTime Until; public bool Loaded; public DownloadRecord Earlier; public SectionRun Run; public IList<string> Rest; }
        private PendingDownload resumeDownload, duplicate;
        // QA: notes a download that would start, in place of the request.
        private Func<string, bool> downloadProbe;
        private void OpenDuplicate()
        {
            var pending = duplicate; duplicate = null; messages.Dismiss(); RenderFeedback();
            if (pending != null) ShowDownloads(pending.Earlier);
        }
        private void DownloadAgain()
        {
            var pending = duplicate; duplicate = null; messages.Dismiss(); RenderFeedback();
            if (pending != null) QueueAsync(() => DownloadAsync(pending.Url, pending.Name, true));
        }
        private string resumedUrl;
        private void ResumeDownload()
        {
            var pending = resumeDownload;
            if (pending == null || !pending.Loaded) return;
            resumeDownload = null;
            if (DateTime.UtcNow > pending.Until) { SetStatus("downloadLogin"); return; }
            if (pending.Run != null) { ResumeSectionRun(pending); return; }
            // One repeat only: a second login answer for the same file is left to the user.
            QueueAsync(async () => { resumedUrl = pending.Url; try { await DownloadAsync(pending.Url, pending.Name); } finally { resumedUrl = null; } });
        }
        // A hand-off to Moodle that never came within its time leaves no «Открытие Moodle…» behind.
        private void ExpireSignOn()
        {
            if (DateTime.UtcNow.Ticks < System.Threading.Interlocked.Read(ref signOnUntil)) return;
            if (messages.Withdraw("signOnOpening")) RenderFeedback();
        }
        private void ExpireResumedDownload()
        {
            if (resumeDownload == null || DateTime.UtcNow <= resumeDownload.Until) return;
            resumeDownload = null; SetStatus("downloadLogin");
        }
        // "СДО Moodle" opens Univer's /user/loginMoodle form in the app. With saved credentials the usual
        // sign-in automation fills it under the loading cover; Univer's exit to Moodle opens in the user's browser.
        private long signOnUntil;
        private string signOnReturn;
        private void BeginSignOn()
        {
            signOnReturn = SitePolicy.Trusted(lastGet) && !SitePolicy.SingleSignOn(lastGet) ? lastGet : SitePolicy.Origin + "/news/student";
            System.Threading.Interlocked.Exchange(ref signOnUntil, DateTime.UtcNow.AddSeconds(90).Ticks);
            SetStatus("signOnOpening");
        }
        // Runs on Chromium's thread: only a pending hand-off and a plain HTTPS target qualify.
        private bool SignOnPending(string url)
        { return DateTime.UtcNow.Ticks < System.Threading.Interlocked.Read(ref signOnUntil) && SignOnPage.CanOpen(url); }
        private void OpenHandOff(string url, string method, List<KeyValuePair<string, string>> fields)
        {
            System.Threading.Interlocked.Exchange(ref signOnUntil, 0);
            if (closing) return;
            try
            {
                // Moodle in the app's own window is the answer itself: «Открытие Moodle…» leaves with it.
                if (HandOffInside(url, method, fields)) { if (messages.Withdraw("signOnOpening")) RenderFeedback(); main?.Load(signOnReturn ?? SitePolicy.Origin + "/news/student"); return; }
                var target = method == "POST" ? SingleSignOn.WriteHandOffPage(url, method, fields, "Moodle") : url;
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                SetStatus("signOnOpened");
            }
            catch (Exception) { SetStatus("signOnFailed"); }
            // The app returns to where the user was; the sign-in page is never left on screen.
            if (main != null && !main.IsDisposed) main.Load(signOnReturn ?? SitePolicy.Origin + "/news/student");
        }
        // «Онлайн тест»: Univer signs the student in to online-test.kaznu.kz by redirecting its own route. That redirect opens
        // like any outside link the student follows (the app's link window, or the browser when links open there), and the
        // site window, which never left its page, loads that page again so no loading cover is left behind.
        private string onlineTestReturn; private DateTime onlineTestReturnAt;
        private void OpenOnlineTest(string url)
        {
            if (closing || main == null || main.IsDisposed || !SitePolicy.OnlineTestPortal(url)) return;
            if (OpensInside(url)) ShowWeb(url); else External(url);
            main.Load(onlineTestReturn ?? SitePolicy.Origin + "/news/student");
            onlineTestReturn = null;
        }
        private void PrintPage()
        {
            if (LockShown) return;
            try
            {
                // On the downloads page only an open file is printed, never the hidden site behind it.
                if (DownloadsVisible) downloadsView.PrintPreview();
                else if (Active?.IsBrowserInitialized == true) Active.Print();
            }
            catch (Exception) { SetStatus("printFailed"); }
        }
        private void OpenDownloads()
        { try { Process.Start(new ProcessStartInfo(DownloadService.DownloadsFolder()) { UseShellExecute = true }); } catch (Exception) { SetStatus("downloadFailed"); } }
        private void External(string url)
        { if (outsideProbe != null) outsideProbe(url); else OpenOutside(url); }
        private bool OpenOutside(string url)
        {
            if (!SitePolicy.External(url)) return false;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; }
            catch (Exception) { SetStatus("externalError"); return false; }
        }
        private string openedHost = "";
        // QA: notes an outside address that would open, in place of the user's browser.
        private Func<string, bool> outsideProbe;
        // A link the user clicked in a site page. Other pages keep the general rule for safe outside links; on a course's
        // UMKD page the address must be a web page that stands in that page's own materials, so a script or a frame
        // cannot send the user's browser somewhere the teacher never pointed to.
        private void PageLink(ChromiumWebBrowser browser, string url)
        {
            if (!SitePolicy.External(url)) return;
            if (browser != main || !SitePolicy.StudyMaterials(browser.Address)) { if (OpensInside(url)) ShowWeb(url); else External(url); return; }
            QueueAsync(() => OpenStudyLinkAsync(browser, url));
        }
        private async Task OpenStudyLinkAsync(ChromiumWebBrowser browser, string url)
        {
            var page = browser.Address; var epoch = sessionEpoch;
            var found = SitePolicy.StudyLink(url) ? await AutomationController.EvaluateAsync(browser, StudyMaterials.LinkCheck(url)) : null;
            if (closing || epoch != sessionEpoch || browser.IsDisposed || browser != main || !SitePolicy.SamePage(browser.Address, page)) return;
            if (found != StudyMaterials.Found) { SetStatus("externalBlocked"); return; }
            OpenWebLink(url);
        }
        // A web link the user chose to follow.
        private void OpenWebLink(string url)
        {
            if (OpensInside(url)) { ShowWeb(url); return; }
            if (outsideProbe != null ? outsideProbe(url) : OpenOutside(url)) { openedHost = SitePolicy.Host(url); SetStatus("externalOpened"); }
        }
        // " · Univer УМКД\Course\Section" for a file kept in the UMKD folder, so the message says where it went.
        private static string StudyFolderNote(string path)
        {
            var downloads = DownloadService.DownloadsFolder(); var folder = Path.GetDirectoryName(path) ?? "";
            return StudyFolders.Inside(folder, downloads) ? " · " + folder.Substring(downloads.TrimEnd(Path.DirectorySeparatorChar).Length + 1) : "";
        }
        private void SetZoom(int percent)
        {
            var previousZoom = zoomPercent;
            zoomPercent = Math.Max(50, Math.Min(200, percent));
            if (previousZoom != zoomPercent) MotionSurface.StopTree(this);
            zoomLabel.Text = zoomPercent + "%"; prefs.Zoom = zoomPercent;
            if (previousZoom == zoomPercent || !CurtainFits) { ApplyZoom(); return; }
            // Steps gather behind one picture of the window; the layout changes once, when they stop.
            if (curtain == null) { curtain = new ZoomCurtain(this, Theme.Background); curtainZoom = appliedZoom; }
            curtain.Aim(zoomPercent / (float)curtainZoom);
            zoomRound++; zoomSettle.Stop(); zoomSettle.Start();
        }
        private bool CurtainFits { get { return !selfTest && !accountReview && Visible && WindowState != FormWindowState.Minimized && Opacity >= .99 && ClientSize.Width > 0 && ClientSize.Height > 0; } }
        private ZoomCurtain curtain;
        // zoomChanges counts zoom levels given to the browser: a summary picture taken across one is dropped (MainForm.Report.cs).
        private int curtainZoom, zoomRound, zoomChanges;
        private readonly System.Windows.Forms.Timer zoomSettle = new System.Windows.Forms.Timer { Interval = 150 };
        private void ApplyZoom()
        {
            if (appliedZoom != zoomPercent) zoomChanges++;
            appliedZoom = zoomPercent;
            if (Active?.IsBrowserInitialized == true) Active.SetZoomLevel(Math.Log(zoomPercent / 100.0, 1.2));
            SetWebZoom();
            ApplyChromeZoom();
        }
        private async void ZoomSettled(object sender, EventArgs e)
        {
            zoomSettle.Stop(); var round = zoomRound;
            ApplyZoom();
            // Chromium needs a moment to draw the page at its new size; the app's own pages are ready at once.
            var site = !DownloadsVisible;
            await Task.Delay(site ? 180 : 30);
            if (closing || round != zoomRound || curtain == null) return;
            curtain.Reveal(); curtain = null;
        }
        private readonly BrowserMouse browserMouse;
        private readonly WheelSteps wheelSteps = new WheelSteps();
        // Ctrl+wheel over the site or a shown file is the app zoom: one 10% step per wheel notch, as Ctrl+plus/minus.
        // time: the wheel message's own time, so a delay before this runs does not count as a pause.
        private void WheelZoom(int delta, int time)
        {
            var steps = wheelSteps.Add(delta, time);
            if (steps != 0 && !closing) SetZoom(zoomPercent + 10 * steps);
        }
        private float chromeZoom = 1;
        // The same zoom enlarges the app's own pages: the sidebar (within a usable range) and Downloads.
        private void ApplyChromeZoom()
        {
            loading.Zoom = zoomPercent / 100f;
            if (Math.Abs(chromeZoom - zoomPercent / 100f) < 0.001f) return;
            chromeZoom = zoomPercent / 100f;
            var chrome = SidebarZoom;
            UiZoom.ApplyFonts(sidebar, chrome); UiZoom.ApplyFonts(toolbar, chrome); ArrangeShell(); sidebar.Invalidate();
            feedback.SetZoom(chrome); toast.SetZoom(chrome); findBar.SetZoom(chrome); noticesPanel.SetZoom(chrome);
            downloadsView?.SetZoom(chromeZoom); offlineView?.SetZoom(chromeZoom); ArrangeToolbar(); RenderNotices(); PlaceFind();
        }
        private void Back()
        {
            if (DownloadsVisible) { if (downloadsView.Previewing) downloadsView.ClosePreview(); else HideDownloads(); return; }
            if (document != null) CloseDocument(); else if (Active?.CanGoBack == true) Active.Back();
        }
        // Forward belongs to the site only, as the toolbar button says (UpdateHistoryButtons): not in Downloads or the viewer.
        private void Forward() { if (!DownloadsVisible && document == null && Active?.CanGoForward == true) Active.Forward(); }
        // The mouse's side buttons (BrowserMouse over a page, WM_APPCOMMAND over the app's own parts): Back and Forward of
        // what is shown, with the gates of Alt+Left. Not through ProcessCmdKey, which would mark the input as keyboard.
        private void SideButton(bool forward)
        {
            if (closing || LockShown || LockGate.Engaged || commandBusy) return;
            if (WebShown) { var browser = webPanel?.Browser; if (!forward) webPanel.GoBack(); else if (browser != null && browser.CanGoForward) browser.Forward(); return; }
            if (forward) Forward(); else Back();
        }
        private void NavigateSection(string path)
        {
            if (commandBusy || main == null) return;
            CloseWeb(); CloseDocument(); HideDownloads();
            var target = SitePolicy.Origin + path;
            // A repeated click on the section that is already loading would queue another server request.
            if (main.IsLoading && string.Equals(lastGet, target, StringComparison.OrdinalIgnoreCase)) return;
            main.Load(target);
        }
        // Toolbar refresh and F5 act on what is shown; the downloads page re-reads its list instead of the hidden site.
        private void RefreshCurrent() { if (DownloadsVisible) { downloadsView.RefreshView(); QueueAsync(PruneDownloadsAsync); } else Retry(); }
        private void Retry()
        {
            if (commandBusy || Active == null) return;
            recoverAt = null; recoveries = 0;
            error = false; messages.Retry(); automation.Retry(); RenderFeedback();
            Active.Load(RetryTarget());
        }
        // Reload only a GET route; a notice/login POST is never replayed by Retry or automatic recovery.
        private string RetryTarget() { return lastMethod == "GET" && SitePolicy.Trusted(Active.Address) ? Active.Address : lastGet; }
        // Automatic recovery reloads the shown GET page after a failed load, stalled or refused sign-in, or unconfirmed notice,
        // with growing delays (at once when the network returns); explicit logout, a hard stop and certificate errors never do.
        private static readonly int[] RecoveryDelays = { 5, 15, 30, 60, 120, 300 };
        private DateTime? recoverAt;
        private DateTime recoveredAt;
        private int recoveries;
        // Self-test: how often a reload was asked for (self-test fixtures never reload).
        private int recoveryRequests;
        private void ScheduleRecovery()
        {
            recoveryRequests++;
            if (selfTest || accountReview || closing || recoverAt.HasValue) return;
            recoverAt = DateTime.UtcNow.AddSeconds(RecoveryDelays[Math.Min(recoveries, RecoveryDelays.Length - 1)]);
            RenderFeedback();
        }
        // The page's automation asks for a reload. Until the reload just sent replaces the page, its old document is not a
        // new failure.
        private void PageNeedsRecovery()
        {
            var now = DateTime.UtcNow;
            if (now - recoveredAt < TimeSpan.FromSeconds(5) || (awaitingCommit == Active && now - awaitingSince < TimeSpan.FromSeconds(20))) { recoveryRequests++; return; }
            ScheduleRecovery();
        }
        private void RunRecovery()
        {
            if (!recoverAt.HasValue || DateTime.UtcNow < recoverAt.Value) return;
            // A command, the settings dialog or a session change goes first; the reload follows on a later tick.
            if (commandBusy || settingsOpen || rotating || Active == null || Active.IsDisposed) return;
            recoverAt = null; recoveries++; recoveredAt = DateTime.UtcNow;
            error = false; messages.Retry(); automation.Recover(); RenderFeedback();
            recoveryLoading = Active == main;
            Active.Load(RetryTarget());
        }
        private void RecoverySucceeded() { recoveries = 0; recoverAt = null; }
        private void RecoverNow() { if (recoverAt.HasValue) recoverAt = DateTime.UtcNow; }
        private void NetworkAvailabilityChanged(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e) { if (e.IsAvailable) Dispatch(RecoverNow); }
        private void PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatch(RecoverNow); }
    }
}
