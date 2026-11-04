using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private static Preferences Copy(Preferences source) { return source.Clone(); }
        private async Task OpenSettingsAsync(bool first = false)
        {
            if (settingsOpen || commandBusy || closing) return;
            settingsOpen = true;
            var installUpdate = false;
            try
            {
                var shown = Copy(prefs); shown.AutoLoginPaused |= guard.Stopped;
                using (var form = new SettingsForm(credentials, shown, first, updates))
                {
                    form.OpenReleasePage = () => External(Updates.ReleasePage(updates.Available));
                    form.ApplyAsync = (action, next, language, readable) => ApplySettingsAsync(action, next, language, readable, form.Tray, form.Animations, form.SelectedTheme);
                    form.ShowDialog(this);
                    // Closing first launch with X also means manual login, never implicit credential saving.
                    if (first && form.Action == SettingsAction.None) await ApplySettingsAsync(SettingsAction.Manual, null, prefs.Language, prefs.Readable);
                    installUpdate = form.UpdateRequested;
                }
            }
            finally { settingsOpen = false; }
            if (installUpdate) ShowUpdate(true);
        }
        private async Task<bool> ApplySettingsAsync(SettingsAction action, Credentials next, string language, bool readable, bool? tray = null, bool? animations = null, string theme = null)
        {
            if (commandBusy || closing) return false;
            commandBusy = true; SetCommandEnabled(false); SetStatus("saving");
            var languageChanged = prefs.Language != language; var rotated = false;
            var logout = action == SettingsAction.Logout || action == SettingsAction.Remove;
            try
            {
                if (logout) { await EndSessionAsync(action == SettingsAction.Remove); return !closing; }
                var change = action == SettingsAction.Manual ? SettingsChange.AppearanceOnly : SettingsDecision.Classify(credentials, next, prefs.AutoLoginPaused || guard.Stopped);
                var updated = Copy(prefs); updated.Language = language; updated.Readable = readable; if (tray.HasValue) updated.Tray = tray.Value; if (animations.HasValue) updated.Animations = animations.Value; if (theme != null) updated.Theme = ThemeChoice.Normalize(theme);
                if (action == SettingsAction.Manual) updated.AutoLoginPaused = true;
                if (change != SettingsChange.AppearanceOnly)
                {
                    automation.Navigating(); guard.Stop(); prefs.AutoLoginPaused = true;
                    // Persist the stop before replacing credentials or clearing the browser.
                    updated.AutoLoginPaused = true; await storage.SavePreferencesAsync(updated); prefs = updated; guard.Stop();
                    if (change == SettingsChange.NewCredentials) { await storage.SaveCredentialsAsync(next); credentials = next; }
                    rotated = true; await RotateSessionAsync();
                    updated = Copy(prefs); updated.AutoLoginPaused = false;
                    await storage.SavePreferencesAsync(updated); prefs = updated;
                }
                else { await storage.SavePreferencesAsync(updated); prefs = updated; }
                if (closing) return false;
                storageFailed = false; messages.ClearStorageError();
                if (action == SettingsAction.Manual) { messages.FinishSettings(); SetStatus("manualSignIn"); }
                else SetStatus("settingsSaved");
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
            else SetStatus(logout ? "paused" : "automationFailed");
        }
        private async Task EndSessionAsync(bool remove)
        {
            var stopped = Copy(prefs); stopped.AutoLoginPaused = true;
            guard.Stop(); automation.Navigating(); prefs.AutoLoginPaused = true;
            await storage.SavePreferencesAsync(stopped); prefs = stopped;
            if (remove) { await storage.RemoveCredentialsAsync(); credentials = null; }
            await RotateSessionAsync();
            if (!closing) { messages.FinishSettings(); SetStatus("paused"); Localize(); }
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
            rotating = true; timer.Stop(); automation.Navigating(); downloads.Dispose(); sessionEpoch++;
            var replaced = false;
            try
            {
                await ClearNoticesAsync();
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
                var clean = Copy(prefs); clean.Profile = Guid.NewGuid().ToString("N"); clean.AutoLoginPaused = true;
                await storage.SavePreferencesAsync(clean); prefs = clean;
                stopping.ThrowIfCancellationRequested();
                CreateSession(); replaced = true; messages.NewSession(); lastGet = SitePolicy.Home; lastMethod = "GET";
                await storage.CleanOldProfilesAsync(clean.Profile);
            }
            catch (Exception) when (!closing && !replaced) { RecoverSession(); throw; }
            finally { rotating = false; if (!closing) timer.Start(); }
        }
        // A rotation stopped part-way has already ended the old session (its events no longer match sessionEpoch), so start a
        // fresh empty profile; the stored pause keeps automatic sign-in off, as the request intended.
        private void RecoverSession()
        {
            CloseBrowsers(); context?.Dispose(); context = null;
            var fresh = Copy(prefs); fresh.Profile = Guid.NewGuid().ToString("N"); fresh.AutoLoginPaused = true; prefs = fresh;
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
            var result = await service.DownloadAsync(url, browser?.Address, agent, suggestedName, source, key);
            // Nothing was saved and nothing keeps running, so the click gets an answer instead of silence.
            if (result == "offOrigin" || result == "downloadBusy") { SetStatus(result); return; }
            if (result != "downloadLogin" || service != downloads || closing || rotating || browser != Active || browser == null || browser.IsDisposed) return;
            // The server ended the session behind an idle page. With saved sign-in, reload the page so the usual login
            // automation signs in again, then repeat this GET once; an explicit logout or a stopped guard stays manual.
            if (credentials == null || prefs.AutoLoginPaused || guard.Blocked || url == resumedUrl || (resumeDownload != null && resumeDownload.Url == url)) return;
            resumeDownload = new PendingDownload { Url = url, Name = suggestedName, Until = DateTime.UtcNow.AddSeconds(90) };
            SetStatus("downloadSigningIn");
            browser.Load(lastMethod == "GET" && SitePolicy.Trusted(browser.Address) ? browser.Address : lastGet);
        }
        private sealed class PendingDownload { public string Url, Name; public DateTime Until; public bool Loaded; public DownloadRecord Earlier; }
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
            // One repeat only: a second login answer for the same file is left to the user.
            QueueAsync(async () => { resumedUrl = pending.Url; try { await DownloadAsync(pending.Url, pending.Name); } finally { resumedUrl = null; } });
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
                var target = method == "POST" ? SingleSignOn.WriteHandOffPage(url, method, fields, "Moodle") : url;
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                SetStatus("signOnOpened");
            }
            catch (Exception) { SetStatus("signOnFailed"); }
            // The app returns to where the user was; the sign-in page is never left on screen.
            if (main != null && !main.IsDisposed) main.Load(signOnReturn ?? SitePolicy.Origin + "/news/student");
        }
        private void PrintPage()
        {
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
        { if (SitePolicy.External(url)) try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { SetStatus("externalError"); } }
        private void SetZoom(int percent)
        {
            var previousZoom = zoomPercent;
            zoomPercent = Math.Max(50, Math.Min(200, percent));
            if (previousZoom != zoomPercent) MotionSurface.StopTree(this);
            zoomLabel.Text = zoomPercent + "%"; prefs.Zoom = zoomPercent;
            if (Active?.IsBrowserInitialized == true) Active.SetZoomLevel(Math.Log(zoomPercent / 100.0, 1.2));
            ApplyChromeZoom();
            if (Motion.Enabled && !Motion.KeyboardInput && previousZoom != zoomPercent)
            {
                var browser = Active; var targetZoom = zoomPercent;
                QueueAsync(async () =>
                {
                    await Task.Delay(32);
                    if (closing || browser != Active || browser == null || browser.IsDisposed || targetZoom != zoomPercent || Motion.KeyboardInput) return;
                    await AutomationController.EvaluateAsync(browser, "window.__univerMotionUI?.zoom(document.getElementById('l4')," + (previousZoom / (double)targetZoom).ToString(System.Globalization.CultureInfo.InvariantCulture) + "); 'zoom'");
                });
            }
        }
        private readonly CtrlWheel ctrlWheel;
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
            if (Math.Abs(chromeZoom - zoomPercent / 100f) < 0.001f) return;
            chromeZoom = zoomPercent / 100f;
            UiZoom.ApplyFonts(sidebar, SidebarZoom); ArrangeShell(); sidebar.Invalidate();
            downloadsView?.SetZoom(chromeZoom); ArrangeToolbar();
        }
        private void Back()
        {
            if (DownloadsVisible) { if (downloadsView.Previewing) downloadsView.ClosePreview(); else HideDownloads(); return; }
            if (document != null) CloseDocument(); else if (Active?.CanGoBack == true) Active.Back();
        }
        // The read notice is confirmed once at startup; home goes straight to the news list afterwards.
        private void GoHome() { NavigateSection("/news/student"); }
        private void NavigateSection(string path)
        {
            if (commandBusy || main == null) return;
            CloseDocument(); HideDownloads();
            var target = SitePolicy.Origin + path;
            // A repeated click on the section that is already loading would queue another server request.
            if (main.IsLoading && string.Equals(lastGet, target, StringComparison.OrdinalIgnoreCase)) return;
            main.Load(target);
        }
        // Toolbar refresh and F5 act on what is shown; the downloads page re-reads its list instead of the hidden site.
        private void RefreshCurrent() { if (DownloadsVisible) downloadsView.RefreshView(); else Retry(); }
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
            Active.Load(RetryTarget());
        }
        private void RecoverySucceeded() { recoveries = 0; recoverAt = null; }
        private void RecoverNow() { if (recoverAt.HasValue) recoverAt = DateTime.UtcNow; }
        private void NetworkAvailabilityChanged(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e) { if (e.IsAvailable) Dispatch(RecoverNow); }
        private void PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Resume) Dispatch(RecoverNow); }
    }
}
