using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // QA step "navigation": Alt+Right and Ctrl+R, the mouse's side buttons over a page (messages posted to Chromium's window,
    // the way Windows delivers a press: DevTools input would bypass the hook) and over the app's own parts (WM_APPCOMMAND),
    // the kept Downloads order, the start page and the tray tooltip.
    internal sealed partial class MainForm
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private async Task NavigationQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            var original = main.ResourceRequestHandlerFactory;
            var pages = original as ResourceRequestHandlerFactory ?? new ResourceRequestHandlerFactory();
            main.ResourceRequestHandlerFactory = pages;
            var first = SitePolicy.Origin + "/student/qa-nav/?page=1"; var second = SitePolicy.Origin + "/student/qa-nav/?page=2";
            foreach (var url in new[] { first, second })
                pages.RegisterHandler(url, ResourceHandler.GetByteArray(PolishShell("<p id='qa-nav'>" + url.Substring(url.Length - 1) + "</p>", false), Encoding.UTF8), "text/html", false);
            Func<string, Task<bool>> at = async url =>
            {
                var until = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < until && !(main.Address == url && !main.IsLoading && await FixesEvalAsync("document.readyState") == "complete")) await Task.Delay(50);
                return main.Address == url;
            };
            Action<Keys> press = key => { var message = new Message(); ProcessCmdKey(ref message, key); };
            // XBUTTON1 is Back, XBUTTON2 Forward: down then up, as a click arrives.
            Action<int> side = button =>
            {
                var window = main.GetBrowser().GetHost().GetWindowHandle(); var which = (IntPtr)(button << 16);
                PostMessage(window, 0x020B, which, IntPtr.Zero); PostMessage(window, 0x020C, which, IntPtr.Zero);
            };
            try
            {
                main.Load(first); Check(await at(first), "navigation fixture: first page", results);
                main.Load(second); Check(await at(second), "navigation fixture: second page", results);
                press(Keys.Alt | Keys.Left); var back = await at(first);
                press(Keys.Alt | Keys.Right); Check(back && await at(second), "Alt+Left goes back and Alt+Right forward in the site", results);
                side(1); var mouseBack = await at(first);
                side(2); Check(mouseBack && await at(second), "the mouse's side buttons over the page go back and forward", results);
                SendMessage(Handle, 0x0319, Handle, (IntPtr)(1 << 16)); var appBack = await at(first);
                SendMessage(Handle, 0x0319, Handle, (IntPtr)(2 << 16)); Check(appBack && await at(second), "the side buttons over the app's own parts (WM_APPCOMMAND) go back and forward", results);
                await FixesEvalAsync("window.qaNavMark=1;'ok'");
                var methodWas = lastMethod; lastMethod = "GET"; press(Keys.Control | Keys.R);
                var reloaded = false; var until = DateTime.UtcNow.AddSeconds(8);
                while (!reloaded && DateTime.UtcNow < until) { await Task.Delay(100); reloaded = !main.IsLoading && await FixesEvalAsync("document.readyState==='complete'&&!window.qaNavMark?'fresh':''") == "fresh"; }
                lastMethod = methodWas;
                Check(reloaded && main.Address == second, "Ctrl+R reloads the shown page like F5", results);
                // Downloads: Forward has nothing to do there; Back (a side button) closes it.
                press(Keys.Alt | Keys.Left); await at(first);
                ShowDownloads(); await Task.Delay(300);
                press(Keys.Alt | Keys.Right); await Task.Delay(400);
                var stayed = DownloadsVisible && main.Address == first;
                side(1); var closeUntil = DateTime.UtcNow.AddSeconds(3);
                while (DownloadsVisible && DateTime.UtcNow < closeUntil) await Task.Delay(50);
                Check(stayed && !DownloadsVisible && main.Address == first, "in Downloads Alt+Right does nothing and the Back side button closes it", results);
            }
            finally
            {
                HideDownloads();
                main.ResourceRequestHandlerFactory = original;
                pages.UnregisterHandler(first); pages.UnregisterHandler(second);
            }

            // The Downloads order is the one chosen last; a new view starts with it.
            var orderWas = prefs.DownloadsOrder;
            downloadsView.SortForQa = DownloadSort.Size;
            var kept = prefs.DownloadsOrder == "size";
            using (var fresh = new DownloadsView(layoutScale, External)) { fresh.Order = DownloadOrder.Parse(prefs.DownloadsOrder); kept &= fresh.SortForQa == DownloadSort.Size; }
            downloadsView.SortForQa = DownloadOrder.Parse(orderWas); prefs.DownloadsOrder = orderWas;
            Check(kept, "the chosen Downloads order is kept and a new start opens with it", results);

            // The start page: the chosen section when the app signs in by itself, else the home page.
            var credentialsWere = credentials; var pageWas = prefs.StartPage; var pausedWas = prefs.AutoLoginPaused;
            try
            {
                prefs.StartPage = "schedule"; prefs.AutoLoginPaused = false;
                credentials = new Credentials { Login = "qa-start", Password = "qa" };
                var chosen = StartAddress() == SitePolicy.Origin + "/student/myschedule/";
                prefs.AutoLoginPaused = true; var paused = StartAddress() == SitePolicy.Home;
                credentials = null; var none = StartAddress() == SitePolicy.Home;
                Check(chosen && paused && none, "the start page opens the chosen section only when the app signs in by itself", results);
            }
            finally { credentials = credentialsWere; prefs.StartPage = pageWas; prefs.AutoLoginPaused = pausedWas; }
            foreach (var lang in new[] { "ru", "kz", "en" })
            {
                var shown = prefs.Clone(); shown.Language = lang;
                using (var form = new SettingsForm(null, shown))
                {
                    form.Show(this); form.SelectAppearanceForQa(); await Task.Delay(150);
                    var button = form.StartPageForQa;
                    var named = button.Text == Strings.Get("home", lang) && button.AccessibleName.StartsWith(Strings.Get("startPageTitle", lang), StringComparison.Ordinal);
                    form.ChooseStartPageForQa("bachelor"); await Task.Delay(50);
                    Check(form.StartPage == "bachelor" && button.Text == Strings.Get("bachelor", lang) && form.StartPageFitsForQa, "Settings offers the Bachelor page as the start page (" + lang + ")", results);
                    if (lang == "ru") SaveBitmap(form, Path.Combine(qa, "settings-start-bachelor.png"));
                    form.ChooseStartPageForQa("attestation"); await Task.Delay(50);
                    Check(named && form.StartPage == "attestation" && button.Text == Strings.Get("attestation", lang) && form.SaveButton.Enabled && form.StartPageFitsForQa,
                        "Settings offers the start page with its name, fits it on the View tab and offers the choice to Save (" + lang + ")", results);
                    if (lang == "ru") SaveBitmap(form, Path.Combine(qa, "settings-start-page.png"));
                    form.Close();
                }
            }
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, startPage: "journal") && prefs.StartPage == "journal" && storage.LoadPreferences().StartPage == "journal",
                "Save keeps the chosen start page", results);
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, startPage: StartPages.Home) && prefs.StartPage == StartPages.Home, "Save puts the home page back", results);
            messages.Withdraw("settingsSaved"); RenderFeedback();

            // The tray tooltip: the next class, its name hidden behind the PIN (ClassReminders.TrayText has the words).
            var trayWas = trayIcon; var weekWas = scheduleWeek;
            using (var isolatedTray = new NotifyIcon { Visible = false })
            {
                trayIcon = isolatedTray;
                var now = DateTime.Now; var start = now.AddHours(1);
                scheduleWeek = new ScheduleWeek { Start = now.Date.AddDays(-1), End = now.Date.AddDays(2), Classes = new List<ScheduleClass> { new ScheduleClass { Start = start, End = start.AddMinutes(50), Name = "QA Философия", Type = "", Room = "1", Teacher = "" } } };
                try
                {
                    UpdateTrayHint();
                    Check(isolatedTray.Text == trayHintForQa && trayHintForQa.Contains("QA Философия") && trayHintForQa.Contains(start.ToString("HH:mm")) && trayHintForQa.Length <= ClassReminders.TrayTextLimit,
                        "the tray tooltip names the next class and its time", results);
                    scheduleWeek = null; UpdateTrayHint();
                    Check(isolatedTray.Text == Strings.Get("appTitle", prefs.Language), "without a kept week the tooltip is the app's name", results);
                }
                finally { trayIcon = trayWas; scheduleWeek = weekWas; }
            }
        }
    }
}
