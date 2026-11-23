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
                    QaStep("autostart", () => AutostartQaAsync(qa, results)),
                    QaStep("reminders", () => RemindersQaAsync(qa, results)),
                    QaStep("shell", () => ShellQaAsync(qa, results)),
                    QaStep("navigation", () => NavigationQaAsync(qa, results)),
                    QaStep("document", () => DocumentQaAsync(qa, results)),
                    QaStep("presentation", () => PresentationQaAsync(qa, results)),
                    QaStep("form-download", () => FormDownloadQaAsync(results)),
                    QaStep("layout-load", () => LayoutLoadQaAsync(results)),
                    QaStep("downloads", () => DownloadsQaAsync(qa, results)),
                    QaStep("download-delete", () => DeleteQaAsync(results)),
                    QaStep("office", () => OfficeQaAsync(qa, results)),
                    QaStep("zoom", () => ZoomQaAsync(qa, results)),
                    QaStep("polish", () => PolishQaAsync(qa, results)),
                    QaStep("top", () => TopQaAsync(qa, results)),
                    QaStep("skeletons", () => SkeletonQaAsync(qa, results)),
                    QaStep("igpa", () => IgpaQaAsync(qa, results)),
                    QaStep("report", () => ReportQaAsync(qa, results)),
                    QaStep("speed", () => SpeedQaAsync(results)),
                    QaStep("idle", () => IdleQaAsync(results)),
                    QaStep("kept", () => KeptQaAsync(qa, results)),
                    QaStep("schedule-copy", () => ScheduleCopyQaAsync(results)),
                    QaStep("exams", () => ExamsQaAsync(results)),
                    QaStep("schedule-export", () => ScheduleExportQaAsync(qa, results)),
                    QaStep("last-grades", () => LastGradesQaAsync(results)),
                    QaStep("offline", () => OfflineQaAsync(qa, results)),
                    QaStep("offline-entry", () => OfflineEntryQaAsync(qa, results)),
                    QaStep("grades", () => GradesQaAsync(qa, results)),
                    QaStep("diagnostics", () => DiagnosticsQaAsync(qa, results)),
                    QaStep("product", () => ProductQaAsync(qa, results)),
                    QaStep("theme", () => ThemeQaAsync(qa, results)),
                    QaStep("notices", () => NoticesQaAsync(qa, results)),
                    QaStep("updates", () => UpdatesQaAsync(qa, results)),
                    QaStep("update-entry", () => UpdateEntryQaAsync(results)),
                    QaStep("online-test", () => OnlineTestQaAsync(results)),
                    QaStep("live", () => liveSmoke ? LiveQaAsync(qa, results) : Task.FromResult(true)),
                    QaStep("first-load", () => liveSmoke ? FirstLoadQaAsync(qa, results) : Task.FromResult(true)),
                    QaStep("lock", () => LockQaAsync(qa, results)),
                    QaStep("logout", () => LogoutQaAsync(results))
                };
                if (only != null && !steps.Exists(step => step.Key == only)) throw new ArgumentException("Unknown self-test step: " + only);
                foreach (var step in steps)
                {
                    if (only != null && step.Key != only) continue;
                    await RestoreQaWindowAsync();
                    var clock = System.Diagnostics.Stopwatch.StartNew(); QaTrace(step.Key + "-start"); await step.Value(); QaTrace(step.Key + "-done " + clock.ElapsedMilliseconds + " ms");
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
            await WebSettingsQaAsync(results);
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
                    if (lang == "ru")
                    {
                        form.StageForQa.SaveSheetForQa(Path.Combine(qa, "stage-welcome.png"), new[] { 0, 1100, 2400, 3500, 4300, 6100, 9000, 9900 }, 4);
                        using (var conveyor = new StageView(StageScene.Downloads) { Size = new Size(D(520), D(148)) }) { conveyor.SetLanguage("ru"); conveyor.SaveSheetForQa(Path.Combine(qa, "stage-downloads.png"), new[] { 100, 400, 800, 3100, 3500, 6500 }, 2); }
                    }
                    form.SaveButton.PerformClick();
                    Check(!form.IsDisposed && form.LoginInput.Invalid && form.PasswordInput.Invalid, "empty sign in validation " + lang, results);
                    SaveBitmap(form, Path.Combine(qa, "signin-error-" + lang + ".png")); form.Close();
                }
                using (var form = new SettingsForm(new Credentials { Login = "QA student", Password = "synthetic fixture" }, prefs))
                {
                    form.Load += (s, e) => form.ScaleWidthsForQa(1.5f); form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); await Task.Delay(40);
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
                    form.SelectUpdatesForQa(); SaveBitmap(form, Path.Combine(qa, "app-" + lang + ".png"));
                    Check(form.AboutFitsForQa(), "about line shows version, engine and unofficial notice without clipping " + lang, results);
                    var overflow = form.OverflowForQa(); if (overflow.Length > 0) QaTrace("qa-settings-overflow " + lang + " " + string.Join(" | ", overflow));
                    Check(overflow.Length == 0, "every setting keeps its name and short line on one line " + lang, results);
                    Check(form.TabsFitForQa, "tabs keep the width of their words after the window's scale " + lang, results);
                    Check(form.HugsFitForQa, "buttons keep the width of their words after the window's scale " + lang, results);
                    var spoken = form.Language; var option = Array.IndexOf(new[] { "ru", "kz", "en" }, spoken); var neighbour = (option + 1) % 3;
                    form.SelectAppearanceForQa(); form.PressForQa(form.LanguageForQa(option), Keys.Right);
                    Check(form.Language != spoken && form.LanguageForQa(neighbour).TabStop && !form.LanguageForQa(option).TabStop, "an arrow chooses the next language and the group stays one Tab stop " + lang, results);
                    form.PressForQa(form.LanguageForQa(neighbour), Keys.Left);
                    Check(form.Language == spoken, "the opposite arrow chooses it back " + lang, results);
#if LEGACY_ENGINE
                    Check(form.LegacyWarningVisible, "legacy warning survives settings tab switch " + lang, results);
#else
                    Check(!form.LegacyWarningVisible, "modern appearance has no legacy warning " + lang, results);
#endif
                    var pending = new TaskCompletionSource<bool>(); var calls = 0;
                    form.ApplyAsync = (action, next, language, readable) => { calls++; return pending.Task; };
                    form.ChangeForQa(); form.SaveButton.PerformClick(); form.SaveButton.PerformClick();
                    Check(calls == 1 && !form.SaveButton.Enabled, "save blocks duplicate operation " + lang, results);
                    form.Close(); Check(!form.IsDisposed, "close waits for save " + lang, results);
                    pending.SetResult(false); await Task.Delay(40);
                    Check(!form.IsDisposed && form.SaveButton.Enabled, "save failure stays editable " + lang, results);
                    Check(form.SaveButton.AccessibleName == form.SaveButton.Text && form.CancelForQa.AccessibleName == form.CancelForQa.Text, "the footer's accessible names follow its words " + lang, results);
                    SaveBitmap(form, Path.Combine(qa, "save-error-" + lang + ".png")); form.Close();
                }
            }
            prefs.Language = "ru"; Localize();
        }
        // Settings > App: the system-browser choice is saved and read back; Clear gives the in-app browser a new profile and
        // removes the old folder, and the button reports it.
        private async Task WebSettingsQaAsync(List<string> results)
        {
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, externalLinks: true) && prefs.ExternalLinks && storage.LoadPreferences().ExternalLinks,
                "the system-browser choice for links is saved", results);
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, externalLinks: false) && !prefs.ExternalLinks && !storage.LoadPreferences().ExternalLinks,
                "links go back inside the app when the choice is turned off", results);
            var before = prefs.WebProfile; var oldFolder = storage.WebProfilePath(before);
            Directory.CreateDirectory(oldFolder); File.WriteAllText(Path.Combine(oldFolder, "Cookies"), "synthetic fixture");
            using (var form = new SettingsForm(null, prefs))
            {
                form.ClearWebData = ClearWebDataAsync; form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); form.SelectUpdatesForQa(); await Task.Delay(40);
                form.WebDataClearForQa.PerformClick();
                var until = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < until && form.WebDataHintForQa != Strings.Get("webDataCleared", form.Language)) await Task.Delay(100);
                Check(form.WebDataHintForQa == Strings.Get("webDataCleared", form.Language) && form.WebDataClearForQa.Enabled, "Clear reports that other sites' data is gone", results);
                form.Close();
            }
            Check(prefs.WebProfile != before && storage.LoadPreferences().WebProfile == prefs.WebProfile && !Directory.Exists(oldFolder),
                "Clear saves a new in-app profile and removes the old folder", results);
        }
        // The sidebar and toolbar in a narrow window: icons only, the toggle that shows names, no overlaps.
        private async Task ShellQaAsync(string qa, List<string> results)
        {
            Width = 900; prefs.Language = "ru"; prefs.Sidebar = true; manualExpanded = false; Localize();
            Check(sidebar.Width == D(64) && !university.Visible && !brand.Visible, "narrow window compacts navigation", results);
            Check(!pageTitle.Bounds.IntersectsWith(mode.Bounds), "narrow toolbar has no overlap", results);
            Check(pageState.Visible == (pageState.Text.Length > 0) && (!pageState.Visible || pageState.Top >= pageTitle.Bottom - D(2)), "toolbar subtitle never covers the title", results);
            Check(menu.AccessibleName == Strings.Get("expandSidebar", "ru") && tips.GetToolTip(scheduleButton).Contains(Strings.Get("scheduleHint", "ru")) && tips.GetToolTip(scheduleButton).StartsWith(scheduleButton.Text),
                "compact rail names each section and what it contains", results);
            SaveBitmap(this, Path.Combine(qa, "workspace-narrow.png"));
            await ToggleSidebarAsync();
            Check(sidebar.Width == D(240) && portalGroup.Visible && appGroup.Visible && menu.AccessibleName == Strings.Get("collapseSidebar", "ru") && prefs.Sidebar && tips.GetToolTip(scheduleButton) == Strings.Get("scheduleHint", "ru"),
                "toolbar button shows section names in a narrow window", results);
            Check(!pageTitle.Bounds.IntersectsWith(mode.Bounds) && downloadsButton.Top > appGroup.Bottom && appGroup.Top > materialsButton.Bottom, "expanded sidebar groups do not overlap", results);
            SaveBitmap(this, Path.Combine(qa, "workspace-narrow-expanded.png"));
            await ToggleSidebarAsync();
            Check(sidebar.Width == D(64) && !prefs.Sidebar && !portalGroup.Visible, "toolbar button folds the sidebar back to icons", results);
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
            var styled = await AutomationController.EvaluateAsync(main, "document.documentElement.classList.contains('univer-mobile') && document.getElementById('univer-app-mobile-style') && document.getElementById('univer-desktop-style') && getComputedStyle(document.querySelector('#login_form')).paddingTop === '28px' ? 'modern' : getComputedStyle(document.querySelector('#login_form')).paddingTop");
            QaTrace("live-styled " + styled);
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
#if LEGACY_ENGINE
            var warningDismissed = prefs.LegacyWarningDismissed;
#endif
            // What the app keeps from a page (ReadingAllowed): only while it signs in by itself, and only a page that carries the
            // saved login's mark or none. After the site's own logout (login kept, sign-in paused) a student who signs in by hand
            // gets nothing kept under the saved login.
            var savedCredentials = credentials; var wasPaused = prefs.AutoLoginPaused;
            try
            {
                credentials = new Credentials { Login = "QA Gate", Password = "synthetic fixture" }; prefs.AutoLoginPaused = false;
                var own = PageAccount.Mark("qa gate");
                Check(ReadingAllowed(own) && ReadingAllowed("") && !ReadingAllowed(PageAccount.Mark("qa-other")), "readings are kept while the app signs in by itself, for the saved login's mark or none, not for another's", results);
                prefs.AutoLoginPaused = true;
                Check(!ReadingAllowed(own) && !ReadingAllowed(""), "after the site's own logout (sign-in paused) nothing is kept from the pages", results);
            }
            finally { credentials = savedCredentials; prefs.AutoLoginPaused = wasPaused; }
            await context.GetCookieManager(null).SetCookieAsync(SitePolicy.Origin, new CefSharp.Cookie { Name = "qa_cookie", Value = Guid.NewGuid().ToString("N"), Path = "/", Secure = true });
            var copy = Path.Combine(storage.Root, ScheduleFile.Name); File.WriteAllText(copy, "{}"); scheduleWeek = new ScheduleWeek { Classes = new List<ScheduleClass>() };
            var examCopy = Path.Combine(storage.Root, ExamFile.Name); File.WriteAllText(examCopy, "{}"); examPlan = ExamPlan.Empty("qa-logout", DateTime.Now);
            var gradesCopy = Path.Combine(storage.Root, LastGradesFile.Name); File.WriteAllText(gradesCopy, "{}"); lastGrades = OfflineGradesForQa(DateTime.Now, "qa-logout");
            await EndSessionAsync(false);
            Check(!File.Exists(copy) && scheduleWeek == null, "logout removes the app's copy of the week", results);
            Check(!File.Exists(gradesCopy) && lastGrades == null, "logout removes the app's copy of the last grades", results);
            Check(!File.Exists(examCopy) && examPlan == null, "logout removes the app's copy of the exams", results);
            Check(messages.Current?.Action != MessageAction.Settings, "a logout puts no banner over the sign-in page", results);
            Check(prefs.AutoLoginPaused && storage.LoadPreferences().AutoLoginPaused, "logout durably pauses automation", results);
            Check(!prefs.SessionClearPending && !storage.LoadPreferences().SessionClearPending, "a finished logout leaves no session for the next start to clear", results);
            Check(main != initialBrowser && prefs.Profile != initialProfile, "logout replaces session profile", results);
            var afterLogout = await context.GetCookieManager(null).VisitUrlCookiesAsync(SitePolicy.Origin, true);
            Check(!afterLogout.Exists(cookie => cookie.Name == "qa_cookie"), "logout removes previous session cookie", results);
            ShowDownloads();
            Check(downloadsView.Parent == canvas && DownloadsVisible, "downloads remain attached and available after logout", results);
#if LEGACY_ENGINE
            Check(prefs.LegacyWarningDismissed == warningDismissed && storage.LoadPreferences().LegacyWarningDismissed == warningDismissed,
                "legacy warning choice survives actual logout and session rotation", results);
#endif
            HideDownloads();
            var beforeFault = main; var faultProfile = prefs.Profile; var faultWebProfile = prefs.WebProfile; var faulted = false; var remembered = false;
            rotationFaultForQa = () => { remembered = storage.LoadPreferences().SessionClearPending; throw new TimeoutException("QA rotation fault"); };
            try { await EndSessionAsync(false); } catch (TimeoutException) { faulted = true; } finally { rotationFaultForQa = null; }
            Check(remembered, "a logout in progress is remembered on disk until the session is replaced", results);
            for (var wait = DateTime.UtcNow.AddSeconds(5); SavedPreferencesForQa().SessionClearPending && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(!prefs.SessionClearPending && !SavedPreferencesForQa().SessionClearPending && prefs.WebProfile != faultWebProfile,
                "the session recovered from an interrupted logout replaces both profiles and clears the reminder", results);
            Check(faulted && main != null && main != beforeFault && context != null && prefs.Profile != faultProfile && prefs.AutoLoginPaused && !rotating,
                "an interrupted session rotation starts a fresh paused session instead of leaving a stale or missing browser", results);
            // Preparation runs through the session's own dispatcher, so it completes only when the new browser's events are current.
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(preparedBrowsers.Contains(main), "the recovered session browser is prepared through its current-session events", results);
            // New credentials are saved before the old profile ends; a Chromium deadline there leaves a fresh session that
            // signs in by itself instead of a paused one. Synthetic credentials stay in this isolated self-test profile.
            var newAccountOnSignIn = true;
            rotationFaultForQa = () => { newAccountOnSignIn = OpensOnSignIn(); throw new TimeoutException("QA rotation fault"); };
            bool changed;
            try { changed = await ApplySettingsAsync(SettingsAction.Save, new Credentials { Login = "qa-fixture", Password = Guid.NewGuid().ToString("N") }, prefs.Language, prefs.Readable); }
            finally { rotationFaultForQa = null; }
            Check(changed && credentials != null && !prefs.AutoLoginPaused && !guard.Blocked && messages.Shows("sessionReset") && !commandBusy,
                "a Chromium deadline while saving new credentials keeps automatic sign-in on in the fresh session", results);
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            for (var wait = DateTime.UtcNow.AddSeconds(5); SavedPreferencesForQa().AutoLoginPaused && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(!SavedPreferencesForQa().AutoLoginPaused, "the resumed automatic sign-in is saved for the next launch", results);
            // The loading cover: a new account's session signs in by itself and opens on the home page; while the app signs in,
            // the sign-in page keeps the asked page's placeholder, and once its sign-in is stopped the sign-in page is drawn.
            Check(!newAccountOnSignIn, "a new account's fresh session is covered by the home page's placeholder, not the sign-in page's", results);
            var keptWhileSigningIn = KeepsPlaceholder(SitePolicy.LoginAddress);
            guard.Stop();
            Check(keptWhileSigningIn && !KeepsPlaceholder(SitePolicy.LoginAddress),
                "the sign-in page keeps the asked page's placeholder only while the app signs in by itself", results);
            // The sidebar's account line is read while the old session ends (under the cover), not after the new one is up.
            string pausedLine = null; var logoutOnSignIn = false;
            rotationFaultForQa = () => { pausedLine = localAccount.Text; logoutOnSignIn = OpensOnSignIn(); throw new TimeoutException("QA rotation fault"); };
            try { await LogoutAsync(); } finally { rotationFaultForQa = null; }
            // A start that finds a paused saved sign-in on a new profile (an interrupted logout finished at start) is not signed in either.
            Check(logoutOnSignIn && credentials != null && prefs.AutoLoginPaused && OpensOnSignIn(),
                "a logout's new profile, and a start on a new profile with a paused saved sign-in, open on the sign-in page's placeholder", results);
            Check(!storageFailed && !messages.Shows("storageError") && messages.Current?.Action != MessageAction.Settings && prefs.AutoLoginPaused && !commandBusy,
                "a Chromium deadline during logout leaves a paused session without a banner, not a storage failure", results);
            Check(pausedLine == Strings.Get("localPaused", prefs.Language), "while the site's logout replaces the session the sidebar already says automatic sign-in is paused (" + pausedLine + ")", results);
            // Chromium must not shut down while the recovered browser is still being prepared.
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(preparedBrowsers.Contains(main), "the browser recovered after the logout deadline is prepared before exit", results);
            // Settings «Выйти из аккаунта» removes the sign-in: under the cover the sidebar must not still say it is saved.
            // A new account's session is paused until its rotation ends, and then signs in: meanwhile the sign-in page keeps the
            // placeholder already shown.
            var startingKept = false;
            sessionCreatedForQa = () => startingKept = KeepsPlaceholder(SitePolicy.LoginAddress);
            try { await ApplySettingsAsync(SettingsAction.Save, new Credentials { Login = "qa-fixture", Password = Guid.NewGuid().ToString("N") }, prefs.Language, prefs.Readable); }
            finally { sessionCreatedForQa = null; }
            Check(startingKept, "while a new account's session starts, the sign-in page keeps the placeholder already shown", results);
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            string removedLine = null;
            rotationFaultForQa = () => removedLine = localAccount.Text;
            try { await EndSessionAsync(true); } finally { rotationFaultForQa = null; }
            Check(credentials == null && removedLine == Strings.Get("localManual", prefs.Language) && localAccount.Text == removedLine,
                "while «Выйти из аккаунта» replaces the session the sidebar no longer says the sign-in is saved (" + removedLine + ")", results);
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            Check(preparedBrowsers.Contains(main), "the browser after removing the sign-in is prepared before exit", results);
        }
        // Self-test only: makes the next session rotation fail after its cookies were cleared.
        private Action rotationFaultForQa;
        // A queued save may be replacing preferences.json at that moment: a busy file is read again.
        private Preferences SavedPreferencesForQa()
        {
            for (var tries = 0; ; tries++)
            {
                try { return storage.LoadPreferences(); }
                catch (IOException) when (tries < 20) { System.Threading.Thread.Sleep(50); }
            }
        }
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
