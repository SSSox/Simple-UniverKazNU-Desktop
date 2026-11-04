using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // --self-test runs every step below in an isolated profile; --self-test-only=<step> runs start-up and that one step,
        // for a quick check while working on it. Each step leaves the window as the next one expects: Russian, wide, site shown.
        private async Task SelfTestAsync()
        {
            var output = AppDomain.CurrentDomain.BaseDirectory;
            var results = new List<string>();
            File.Delete(Path.Combine(output, "self-test.ok")); File.Delete(Path.Combine(output, "self-test.failed"));
            var qa = Path.Combine(output, "qa"); Directory.CreateDirectory(qa);
            var only = Array.Find(Environment.GetCommandLineArgs(), a => a.StartsWith("--self-test-only=", StringComparison.Ordinal))?.Substring("--self-test-only=".Length);
            try
            {
                timer.Stop();
                QaTrace("qa-start");
                await StartupQaAsync(results);
                var steps = new List<KeyValuePair<string, Func<Task>>>
                {
                    QaStep("settings", () => SettingsQaAsync(qa, results)),
                    QaStep("shell", () => ShellQaAsync(qa, results)),
                    QaStep("document", () => DocumentQaAsync(qa, results)),
                    QaStep("presentation", () => PresentationQaAsync(qa, results)),
                    QaStep("form-download", () => FormDownloadQaAsync(results)),
                    QaStep("layout-load", () => LayoutLoadQaAsync(results)),
                    QaStep("downloads", () => DownloadsQaAsync(qa, results)),
                    QaStep("office", () => OfficeQaAsync(qa, results)),
                    QaStep("zoom", () => ZoomQaAsync(qa, results)),
                    QaStep("theme", () => ThemeQaAsync(qa, results)),
                    QaStep("notices", () => NoticesQaAsync(qa, results)),
                    QaStep("live", () => liveSmoke ? LiveQaAsync(qa, results) : Task.FromResult(true)),
                    QaStep("logout", () => LogoutQaAsync(results))
                };
                if (only != null && !steps.Exists(step => step.Key == only)) throw new ArgumentException("Unknown self-test step: " + only);
                foreach (var step in steps)
                {
                    if (only != null && step.Key != only) continue;
                    await RestoreQaWindowAsync();
                    QaTrace(step.Key + "-start"); await step.Value(); QaTrace(step.Key + "-done");
                }
                File.WriteAllLines(Path.Combine(output, "self-test.ok"), results);
            }
            catch (Exception exception)
            { File.WriteAllText(Path.Combine(output, "self-test.failed"), exception.GetType().Name + ": " + exception.Message + "\n" + exception.StackTrace); }
            finally { QaTrace("qa-finish"); Close(); }
        }
        private static KeyValuePair<string, Func<Task>> QaStep(string name, Func<Task> run) { return new KeyValuePair<string, Func<Task>>(name, run); }
        private static void QaTrace(string line) { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.trace"), line + "\n"); }
        // The hidden QA window can be minimized from the desktop (Show desktop, Win+D); its docked controls are then 0×0
        // and every layout check would read nonsense. Layout checks call this first.
        private async Task RestoreQaWindowAsync()
        {
            if (WindowState != FormWindowState.Minimized) return;
            QaTrace("qa-window-restored");
            WindowState = FormWindowState.Normal; await Task.Delay(150);
        }
        private async Task StartupQaAsync(List<string> results)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!main.IsBrowserInitialized && DateTime.UtcNow < until) await Task.Delay(100);
            Check(main.IsBrowserInitialized, "CEF initialized", results);
            var locales = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locales");
            Check(new[] { "ru", "en-GB", "en-US" }.All(name => File.Exists(Path.Combine(locales, name + ".pak"))) &&
                Directory.GetFiles(locales, "*.pak").All(file => new[] { "ru", "en-GB", "en-US" }.Contains(Path.GetFileNameWithoutExtension(file).Split('_')[0])),
                "package ships exactly the Chromium locales the app selects", results);
            QaTrace("cef-initialized");
        }
        // Settings save, persistence and the sign-in and settings dialogs in every language.
        private async Task SettingsQaAsync(string qa, List<string> results)
        {
            var initialBrowser = main; var initialProfile = prefs.Profile;
            await CookieSyncQaAsync(results);
            Check(await ApplySettingsAsync(SettingsAction.Save, null, "en", false), "appearance save succeeds", results);
            Check(main == initialBrowser && prefs.Profile == initialProfile, "appearance preserves browser and profile", results);
            Check(!storage.LoadPreferences().Readable && storage.LoadPreferences().Language == "en", "preferences durably saved", results);
            Check(await ApplySettingsAsync(SettingsAction.Manual, null, prefs.Language, prefs.Readable) && storage.LoadPreferences().AutoLoginPaused && storage.LoadCredentials() == null,
                "manual login choice persists without saved credentials", results);
#if LEGACY_ENGINE
            await LegacyWarningQaAsync(results, qa);
#endif
            foreach (var lang in new[] { "ru", "kz", "en" })
            {
                QaTrace("ui-" + lang);
                prefs.Language = lang; Localize(); SetStatus("downloadComplete"); SetStatus("ready");
                Check(messages.Current?.Key == "downloadComplete", "download feedback survives polling " + lang, results);
                Check(sideSettings.AccessibleName == Strings.Get("settings", lang), "accessible language " + lang, results);
                var universityTextSize = TextRenderer.MeasureText(university.Text, university.Font, new Size(university.Width, int.MaxValue), TextFormatFlags.WordBreak);
                Check(university.Height >= universityTextSize.Height && university.Bottom < portalGroup.Top, "university name fits both lines without covering navigation " + lang, results);
                await Task.Delay(60);
                SaveBitmap(this, Path.Combine(qa, "workspace-" + lang + ".png"));
                using (var form = new SettingsForm(null, prefs, true))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); await Task.Delay(40);
#if LEGACY_ENGINE
                    Check(form.LegacyWarningVisible, "legacy warning visible during first sign in " + lang, results);
#else
                    Check(!form.LegacyWarningVisible, "modern sign in has no legacy warning " + lang, results);
#endif
                    SaveBitmap(form, Path.Combine(qa, "signin-" + lang + ".png"));
                    form.SaveButton.PerformClick();
                    Check(!form.IsDisposed && form.LoginInput.Invalid && form.PasswordInput.Invalid, "empty sign in validation " + lang, results);
                    SaveBitmap(form, Path.Combine(qa, "signin-error-" + lang + ".png")); form.Close();
                }
                using (var form = new SettingsForm(new Credentials { Login = "QA student", Password = "synthetic fixture" }, prefs))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); await Task.Delay(40);
#if LEGACY_ENGINE
                    Check(form.LegacyWarningVisible && prefs.LegacyWarningDismissed, "settings warning visible after startup suppression " + lang, results);
#else
                    Check(!form.LegacyWarningVisible, "modern account settings have no legacy warning " + lang, results);
#endif
                    Check(form.PasswordInput.Input.UseSystemPasswordChar, "password initially masked " + lang, results);
                    form.PasswordInput.Reveal.PerformClick(); Check(!form.PasswordInput.Input.UseSystemPasswordChar, "password reveal " + lang, results);
                    form.PasswordInput.Reveal.PerformClick(); Check(form.PasswordInput.Input.UseSystemPasswordChar, "password mask restored " + lang, results);
                    SaveBitmap(form, Path.Combine(qa, "account-" + lang + ".png"));
                    form.SelectAppearanceForQa(); SaveBitmap(form, Path.Combine(qa, "appearance-" + lang + ".png"));
                    Check(form.AboutFitsForQa(), "about line shows version, engine and unofficial notice without clipping " + lang, results);
#if LEGACY_ENGINE
                    Check(form.LegacyWarningVisible, "legacy warning survives settings tab switch " + lang, results);
#else
                    Check(!form.LegacyWarningVisible, "modern appearance has no legacy warning " + lang, results);
#endif
                    var pending = new TaskCompletionSource<bool>(); var calls = 0;
                    form.ApplyAsync = (action, next, language, readable) => { calls++; return pending.Task; };
                    form.SaveButton.PerformClick(); form.SaveButton.PerformClick();
                    Check(calls == 1 && !form.SaveButton.Enabled, "save blocks duplicate operation " + lang, results);
                    form.Close(); Check(!form.IsDisposed, "close waits for save " + lang, results);
                    pending.SetResult(false); await Task.Delay(40);
                    Check(!form.IsDisposed && form.SaveButton.Enabled, "save failure stays editable " + lang, results);
                    SaveBitmap(form, Path.Combine(qa, "save-error-" + lang + ".png")); form.Close();
                }
            }
            prefs.Language = "ru"; Localize();
        }
        // The sidebar and toolbar in a narrow window: icons only, the toggle that shows names, no overlaps.
        private async Task ShellQaAsync(string qa, List<string> results)
        {
            Width = 900; prefs.Language = "ru"; prefs.Sidebar = true; manualExpanded = false; Localize();
            Check(sidebar.Width == D(68) && !university.Visible && !brand.Visible, "narrow window compacts navigation", results);
            Check(!pageTitle.Bounds.IntersectsWith(mode.Bounds), "narrow toolbar has no overlap", results);
            Check(pageState.Visible == (pageState.Text.Length > 0) && (!pageState.Visible || pageState.Top >= pageTitle.Bottom - D(2)), "toolbar subtitle never covers the title", results);
            Check(menu.AccessibleName == Strings.Get("expandSidebar", "ru") && tips.GetToolTip(scheduleButton).Contains(Strings.Get("scheduleHint", "ru")) && tips.GetToolTip(scheduleButton).StartsWith(scheduleButton.Text),
                "compact rail names each section and what it contains", results);
            SaveBitmap(this, Path.Combine(qa, "workspace-narrow.png"));
            await ToggleSidebarAsync();
            Check(sidebar.Width == D(232) && portalGroup.Visible && appGroup.Visible && menu.AccessibleName == Strings.Get("collapseSidebar", "ru") && prefs.Sidebar && tips.GetToolTip(scheduleButton) == Strings.Get("scheduleHint", "ru"),
                "toolbar button shows section names in a narrow window", results);
            Check(!pageTitle.Bounds.IntersectsWith(mode.Bounds) && downloadsButton.Top > appGroup.Bottom && appGroup.Top > materialsButton.Bottom, "expanded sidebar groups do not overlap", results);
            SaveBitmap(this, Path.Combine(qa, "workspace-narrow-expanded.png"));
            await ToggleSidebarAsync();
            Check(sidebar.Width == D(68) && !prefs.Sidebar && !portalGroup.Visible, "toolbar button folds the sidebar back to icons", results);
            prefs.Sidebar = true; manualExpanded = false; ArrangeShell();
            Width = 1360; Localize();
        }
        // Find on a site page, the separate viewer of printed documents and its way back, error recovery and toasts.
        private async Task DocumentQaAsync(string qa, List<string> results)
        {
            Width = 1360; prefs.Language = "ru"; prefs.Readable = true; Localize();
            var sourceBrowser = main;
            await SiteFindQaAsync(qa, results);
            var sourceAddress = main.Address;
            OpenDocument(SitePolicy.Origin + "/student/transcript");
            var until = DateTime.UtcNow.AddSeconds(10);
            while (document != null && !document.IsBrowserInitialized && DateTime.UtcNow < until) await Task.Delay(50);
            Check(document != null && !main.Visible, "document opens separately", results);
            prefs.Language = "en"; Width = 900; Localize();
            Check(!pageTitle.Visible || !pageTitle.Bounds.IntersectsWith(mode.Bounds), "document toolbar avoids overlap", results);
            Check(find.Visible && !find.Bounds.IntersectsWith(mode.Bounds) && (!pageTitle.Visible || !pageTitle.Bounds.IntersectsWith(find.Bounds)) && find.AccessibleName == Strings.Get("findInDocument", "en"),
                "document toolbar names find for the document and keeps it clear of the title", results);
            // The viewer shows whatever the isolated profile receives (with --live-smoke, the real site's redirects to its
            // sign-in page); once its address has been stable for a moment, synthetic text replaces it for a known match count.
            until = DateTime.UtcNow.AddSeconds(12); var settledAddress = ""; var settledSince = DateTime.UtcNow;
            while (document != null && DateTime.UtcNow < until)
            {
                if (document.IsLoading || document.Address != settledAddress) { settledAddress = document.Address; settledSince = DateTime.UtcNow; }
                else if (DateTime.UtcNow - settledSince > TimeSpan.FromMilliseconds(1500)) break;
                await Task.Delay(100);
            }
            until = DateTime.UtcNow.AddSeconds(10);
            while (document != null && DateTime.UtcNow < until && (document.IsLoading || await Web.AutomationController.EvaluateAsync(document,
                "document.body?(document.body.innerHTML='<h1>Transcript</h1><p>QA course one</p><p>QA course two</p>','ok'):''") != "ok")) await Task.Delay(100);
            await FindRoundQaAsync("printed document", "QA", 2, 2, results);
            document.GetBrowser().GetHost().SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = (int)Keys.Escape });
            until = DateTime.UtcNow.AddSeconds(2);
            while (document != null && DateTime.UtcNow < until) await Task.Delay(50);
            Check(document == null && main == sourceBrowser && main.Visible && main.Address == sourceAddress, "Escape returns to preserved source browser", results);
            var delayedClose = new FormClosingEventArgs(CloseReason.TaskManagerClosing, false); OnFormClosing(delayedClose);
            Check(delayedClose.Cancel && !closing, "delayed CEF close cannot exit the application", results);
            OpenDocument(SitePolicy.Origin + "/student/transcript");
            until = DateTime.UtcNow.AddSeconds(10);
            while (document != null && !document.IsBrowserInitialized && DateTime.UtcNow < until) await Task.Delay(50);
            commandBusy = true; LeaveDocument(SitePolicy.Origin + "/student/attendance/full/"); commandBusy = false;
            Check(document != null, "viewer link waits while a settings or logout command runs", results);
            LeaveDocument(lastGet.TrimEnd('/'));
            Check(document == null && main == sourceBrowser && main.Visible && main.Address == sourceAddress, "viewer link back to the source page only closes the viewer", results);
            ReportUnexpectedError();
            Check(messages.Current?.Key == "unexpectedError" && messages.Current.Action == MessageAction.Retry && !exitingOnError && !closing,
                "an unexpected UI error leaves a Retry banner instead of a crash dialog", results);
            messages.BeginNavigation(); unexpectedErrors.Clear(); RenderFeedback();
            toast.Render(new UiMessage("downloading", MessageTone.Working, MessageAction.Downloads, 60, true), "Загрузка", "Скачивается", "Все загрузки", "", "Закрыть");
            var shortBottom = toast.Bottom;
            toast.Render(new UiMessage("downloadComplete", MessageTone.Success, MessageAction.Downloads, 65, true), "Готово", "Файл сохранён. " + new string('Ф', 120), "Открыть файл", "Все загрузки", "Закрыть");
            Check(toast.Visible && toast.Bottom == shortBottom && toast.Bottom <= toast.Parent.ClientSize.Height, "a toast that grows while shown keeps its bottom edge inside the window", results);
            toast.Render(null, "", "", "", "", ""); RenderFeedback();
            Width = 1360; prefs.Language = "ru"; Localize();
        }
        // --live-smoke only: the real sign-in page over HTTPS, read but never submitted, in both views and widths.
        private async Task LiveQaAsync(string qa, List<string> results)
        {
            main.Load("https://univer.kaznu.kz/user/login");
            var until = DateTime.UtcNow.AddSeconds(25); string probe = null;
            while (DateTime.UtcNow < until)
            {
                probe = await AutomationController.EvaluateAsync(main, "document.querySelector('#login_frm') ? 'login-dom' : ''");
                if (probe == "login-dom") break; await Task.Delay(200);
            }
            if (probe != "login-dom")
            {
                var state = await AutomationController.EvaluateAsync(main, "JSON.stringify({ready:document.readyState,login:!!document.querySelector('#login_frm'),forms:document.forms.length})");
                QaTrace("live-state " + state + " error " + failureCode);
                SaveBitmap(this, Path.Combine(qa, "workspace-live-blocked.png"));
            }
            Check(probe == "login-dom", "live trusted HTTPS login DOM (no submission)", results);
            // The live capture is separate from synthetic download/status fixtures.
            messages.NewSession(); messages.Update("ready"); RenderFeedback();
            until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                await ApplyPresentationAsync();
                var applied = await AutomationController.EvaluateAsync(main, "window.__univerAutomationV1 && document.getElementById('univer-desktop-style') && window.__univerAutomationV1.probe().language === 'ru' ? 'styled' : ''");
                if (applied == "styled") break;
                await Task.Delay(150);
            }
            await Task.Delay(150); PerformLayout();
            var visible = await AutomationController.EvaluateAsync(main, "(function(){var f=document.querySelector('#login_frm');if(!f)return '';return Array.from(f.querySelectorAll('input[type=password]')).some(function(p){var r=p.getBoundingClientRect();return r.width>100&&r.height>10&&r.top>=0&&r.bottom<innerHeight;})?'visible-login':'';})()");
            Check(visible == "visible-login", "live password field readable within viewport", results);
            var styled = await AutomationController.EvaluateAsync(main, "document.documentElement.classList.contains('univer-mobile') && document.getElementById('univer-app-mobile-style') && getComputedStyle(document.querySelector('#login_form')).borderRadius === '16px' ? 'modern' : ''");
            Check(styled == "modern", "live desktop site adapter applied", results);
            await AutomationController.EvaluateAsync(main, "window.__univerQaForm=document.getElementById('login_frm');window.__univerQaField=window.__univerQaForm.querySelector('input[type=password]');window.__univerQaHandler=window.__univerQaForm.getAttribute('onsubmit');'remembered'");
            await SetReadableAsync(false);
            // A polling tick already in flight applies the switch on the next tick, so wait for it instead of reading once.
            string restored = null;
            for (var wait = DateTime.UtcNow.AddSeconds(3); restored != "original" && DateTime.UtcNow < wait; await Task.Delay(100))
                restored = await AutomationController.EvaluateAsync(main, "document.getElementById('login_frm')===window.__univerQaForm && window.__univerQaForm.querySelector('input[type=password]')===window.__univerQaField && window.__univerQaForm.getAttribute('onsubmit')===window.__univerQaHandler && !document.documentElement.classList.contains('univer-mobile') && !document.getElementById('univer-app-mobile-style') && !document.getElementById('univer-desktop-style') ? 'original' : ''");
            Check(restored == "original", "live original mode preserves form and controls", results);
            using (var client = main.GetDevToolsClient())
                File.WriteAllBytes(Path.Combine(qa, "site-original.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await SetReadableAsync(true); Width = 900; await Task.Delay(250);
            var fits = await AutomationController.EvaluateAsync(main, "(function(){return Array.from(document.querySelectorAll('#login_frm input[type=password]')).some(function(p){var r=p.getBoundingClientRect();return r.width>100 && r.left>=0 && r.right<=innerWidth && r.top>=0 && r.bottom<innerHeight;})?'fits':'';})()");
            Check(fits == "fits", "live narrow login controls fit viewport", results);
            using (var client = main.GetDevToolsClient())
                File.WriteAllBytes(Path.Combine(qa, "site-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 1360; await Task.Delay(250);
            using (var client = main.GetDevToolsClient())
            {
                var capture = await client.Page.CaptureScreenshotAsync();
                var bytes = capture.Data;
                File.WriteAllBytes(Path.Combine(qa, "site-live.png"), bytes);
                using (var stream = new MemoryStream(bytes))
                using (var site = Image.FromStream(stream))
                    QaSnapshot(Path.Combine(qa, "workspace-live.png"), Width, Height, shell =>
                    {
                        var visibleCanvas = canvas.Visible; canvas.Visible = false;
                        try { DrawToBitmap(shell, new Rectangle(Point.Empty, Size)); }
                        finally { canvas.Visible = visibleCanvas; }
                        var origin = canvas.PointToScreen(Point.Empty); origin.Offset(-Left, -Top);
                        using (var graphics = Graphics.FromImage(shell)) graphics.DrawImage(site, new Rectangle(origin, canvas.Size));
                    });
            }
        }
        // Logout clears the session and replaces the profile; an interrupted rotation still ends in a fresh paused session.
        private async Task LogoutQaAsync(List<string> results)
        {
            var initialBrowser = main; var initialProfile = prefs.Profile;
            await context.GetCookieManager(null).SetCookieAsync(SitePolicy.Origin, new CefSharp.Cookie { Name = "qa_cookie", Value = Guid.NewGuid().ToString("N"), Path = "/", Secure = true });
            await EndSessionAsync(false);
            Check(prefs.AutoLoginPaused && storage.LoadPreferences().AutoLoginPaused, "logout durably pauses automation", results);
            Check(main != initialBrowser && prefs.Profile != initialProfile, "logout replaces session profile", results);
            var afterLogout = await context.GetCookieManager(null).VisitUrlCookiesAsync(SitePolicy.Origin, true);
            Check(!afterLogout.Exists(cookie => cookie.Name == "qa_cookie"), "logout removes previous session cookie", results);
            ShowDownloads();
            Check(downloadsView.Parent == canvas && DownloadsVisible, "downloads remain attached and available after logout", results);
#if LEGACY_ENGINE
            Check(prefs.LegacyWarningDismissed && storage.LoadPreferences().LegacyWarningDismissed, "legacy warning choice survives actual logout and session rotation", results);
#endif
            HideDownloads();
            var beforeFault = main; var faultProfile = prefs.Profile; var faulted = false;
            rotationFaultForQa = () => { throw new TimeoutException("QA rotation fault"); };
            try { await EndSessionAsync(false); } catch (TimeoutException) { faulted = true; } finally { rotationFaultForQa = null; }
            Check(faulted && main != null && main != beforeFault && context != null && prefs.Profile != faultProfile && prefs.AutoLoginPaused && !rotating,
                "an interrupted session rotation starts a fresh paused session instead of leaving a stale or missing browser", results);
            // Preparation runs through the session's own dispatcher, so it completes only when the new browser's events are current.
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(preparedBrowsers.Contains(main), "the recovered session browser is prepared through its current-session events", results);
            // New credentials are saved before the old profile ends; a Chromium deadline there leaves a fresh session that
            // signs in by itself instead of a paused one. Synthetic credentials stay in this isolated self-test profile.
            rotationFaultForQa = () => { throw new TimeoutException("QA rotation fault"); };
            bool changed;
            try { changed = await ApplySettingsAsync(SettingsAction.Save, new Credentials { Login = "qa-fixture", Password = Guid.NewGuid().ToString("N") }, prefs.Language, prefs.Readable); }
            finally { rotationFaultForQa = null; }
            Check(changed && credentials != null && !prefs.AutoLoginPaused && !guard.Blocked && messages.Shows("sessionReset") && !commandBusy,
                "a Chromium deadline while saving new credentials keeps automatic sign-in on in the fresh session", results);
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            for (var wait = DateTime.UtcNow.AddSeconds(5); storage.LoadPreferences().AutoLoginPaused && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(!storage.LoadPreferences().AutoLoginPaused, "the resumed automatic sign-in is saved for the next launch", results);
            rotationFaultForQa = () => { throw new TimeoutException("QA rotation fault"); };
            try { await LogoutAsync(); } finally { rotationFaultForQa = null; }
            Check(!storageFailed && messages.Shows("paused") && !messages.Shows("storageError") && prefs.AutoLoginPaused && !commandBusy,
                "a Chromium deadline during logout leaves a paused session, not a storage failure", results);
            // Chromium must not shut down while the recovered browser is still being prepared.
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(preparedBrowsers.Contains(main), "the browser recovered after the logout deadline is prepared before exit", results);
        }
        // Self-test only: makes the next session rotation fail after its cookies were cleared.
        private Action rotationFaultForQa;
        private static void Check(bool passed, string name, List<string> results)
        { if (!passed) throw new InvalidOperationException(name); results.Add("PASS " + name); }
        // Screenshots are evidence, not checks: a 32-bit run can fail inside GDI+, so a capture is retried once after a
        // collection and otherwise skipped with a trace line.
        private static void QaSnapshot(string path, int width, int height, Action<Bitmap> draw)
        {
            if (width <= 0 || height <= 0) { QaTrace("qa-bitmap-skipped " + Path.GetFileName(path) + " size " + width + "x" + height); return; }
            for (var attempt = 1; ; attempt++)
            {
                try { using (var bitmap = new Bitmap(width, height)) { draw(bitmap); bitmap.Save(path); } return; }
                catch (Exception e) when (e is ArgumentException || e is System.Runtime.InteropServices.ExternalException || e is OutOfMemoryException)
                {
                    if (attempt == 2) { QaTrace("qa-bitmap-skipped " + Path.GetFileName(path) + " " + e.GetType().Name + ": " + e.Message); return; }
                    QaTrace("qa-bitmap-retry " + Path.GetFileName(path) + " " + e.GetType().Name);
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                }
            }
        }
        private static void SaveBitmap(Control control, string path)
        {
            // WinForms WM_PRINT must not synchronously ask a CEF render thread to paint.
            // The live browser image is captured separately through DevTools and composited above.
            var owner = control as MainForm; var visibleCanvas = owner != null && owner.canvas.Visible;
            if (owner != null) owner.canvas.Visible = false;
            try { QaSnapshot(path, control.Width, control.Height, bitmap => control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size))); }
            finally { if (owner != null) owner.canvas.Visible = visibleCanvas; }
        }
    }
}
