using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The new-profile road on the live public site, with no account (self-test -Live only). A new profile has no language
    // cookie and the site's default is Kazakh: the first page after Settings «Выйти из аккаунта» and after the site's own
    // logout must open in the chosen language, the toolbar must never show a Kazakh heading meanwhile, and no banner follows.
    internal sealed partial class MainForm
    {
        private static readonly Regex KazakhLetters = new Regex("[ӘәҒғҚқҢңӨөҰұҮүҺһІі]");
        private const string FirstLoadProbe = "(function(){var lang='';for(var f of document.querySelectorAll('.header img')){if(f.closest('a'))continue;" +
            "var m=new URL(f.src,location.href).pathname.match(/\\/(ru|kz|en)\\.png$/i);if(m){lang=m[1].toLowerCase();break;}}" +
            "var login=document.querySelector('#login_frm');return location.pathname+'|'+lang+'|'+(login?'login':'')+'|'+" +
            "(login&&Array.from(login.querySelectorAll('input[type=password]')).some(function(p){return p.value;})?'typed':'');})()";

        private async Task FirstLoadQaAsync(string qa, List<string> results)
        {
            Check(profileAbsentAtStart, "the first session found no profile folder, so a first start opens the language link", results);
            var kept = prefs.AutoLoginPaused;
            // The synthetic sign-in is saved while the session is on about:blank, so it never meets the live sign-in page.
            freshForQa = false;
            await ApplySettingsAsync(SettingsAction.Save, new Credentials { Login = "qa-fixture", Password = Guid.NewGuid().ToString("N") }, "ru", prefs.Readable);
            Check(credentials != null && sessionAddress == "about:blank", "a synthetic sign-in is saved away from the live site", results);
            try
            {
                freshForQa = true;
                SettingsAction pressed;
                using (var form = new SettingsForm(credentials, prefs.Clone(), false, null, SidebarZoom))
                {
                    form.ApplyAsync = (action, next, language, readable) => ApplySettingsAsync(action, next, language, readable);
                    form.Show(this); await Task.Delay(150);
                    var samples = FirstLoadSamplesAsync(qa, "ru");
                    form.LogoutForQa.PerformClick();
                    await samples;
                    for (var wait = DateTime.UtcNow.AddSeconds(10); form.Action == SettingsAction.None && DateTime.UtcNow < wait;) await Task.Delay(50);
                    pressed = form.Action;
                }
                Check(pressed == SettingsAction.Remove, "Settings «Выйти из аккаунта» was pressed", results);
                Check(credentials == null && storage.LoadCredentials() == null && prefs.AutoLoginPaused,
                    "Settings «Выйти из аккаунта» deletes the saved sign-in and pauses automatic sign-in", results);
                await FirstLoadChecksAsync("ru", "after Settings logout", results);

                // Control: without a language cookie the site answers in Kazakh, so the checks above tell the fix from luck.
                await AutomationController.EvaluateAsync(main, "(function(){window.__univerQaControl='';fetch('/user/login',{credentials:'omit',cache:'no-store'})" +
                    ".then(function(r){return r.text();}).then(function(t){var d=new DOMParser().parseFromString(t,'text/html'),lang='none';" +
                    "for(var f of d.querySelectorAll('.header img')){if(f.closest('a'))continue;var m=new URL(f.getAttribute('src'),location.href).pathname.match(/\\/(ru|kz|en)\\.png$/i);if(m){lang=m[1].toLowerCase();break;}}" +
                    "window.__univerQaControl=lang;}).catch(function(){window.__univerQaControl='error';});return 'started';})()");
                string control = null;
                for (var wait = DateTime.UtcNow.AddSeconds(15); string.IsNullOrEmpty(control) && DateTime.UtcNow < wait; await Task.Delay(100))
                    control = await AutomationController.EvaluateAsync(main, "window.__univerQaControl||''");
                QaTrace("first-load-control " + control);
                Check(control == "kz", "control: without a language cookie the live site answers in Kazakh", results);

                // The site's own logout keeps the chosen language too; English cannot pass by the site's default.
                prefs.Language = "en"; Localize(); await RefreshDocumentScriptsAsync();
                var english = FirstLoadSamplesAsync(qa, "en");
                await LogoutAsync();
                await english;
                await FirstLoadChecksAsync("en", "after the site's logout", results);
            }
            finally
            {
                // The session rotations restarted the poll timer; later steps run without it, as before this one.
                freshForQa = false; timer.Stop();
                prefs.Language = "ru"; prefs.AutoLoginPaused = kept; Localize();
                await storage.SavePreferencesAsync(prefs.Clone()); await RefreshDocumentScriptsAsync();
                if (main != null && main.IsBrowserInitialized) main.Load("about:blank");
            }
        }

        private readonly List<string> firstLoadPages = new List<string>(), firstLoadHeadings = new List<string>(), firstLoadShapes = new List<string>(), firstLoadBanners = new List<string>();
        // Self-test only: every placeholder ShowRoute draws, so a redirect between two samples cannot hide one; and a call at
        // the end of each session start.
        private List<string> shownForQa;
        private Action sessionCreatedForQa;

        // From the press until a second after the cover lifts over the sign-in page: every page the session showed and every
        // toolbar heading, with a window frame under the cover and one after it.
        private async Task FirstLoadSamplesAsync(string qa, string language)
        {
            firstLoadPages.Clear(); firstLoadHeadings.Clear(); firstLoadShapes.Clear(); firstLoadBanners.Clear();
            var before = main; var covered = false; DateTime? revealed = null;
            var leftOver = messages.Current?.Key;
            var shown = shownForQa = new List<string>();
            try
            {
            for (var until = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < until; await Task.Delay(60))
            {
                var heading = (pageTitle.Text + " | " + pageState.Text).Trim();
                if (!firstLoadHeadings.Contains(heading)) firstLoadHeadings.Add(heading);
                var banner = messages.Current?.Key;
                if (banner != null && banner != leftOver && !firstLoadBanners.Contains(banner)) firstLoadBanners.Add(banner);
                if (main == before || main == null || !main.IsBrowserInitialized) continue;
                if (loading.Visible && !firstLoadShapes.Contains(loading.ShapeName)) firstLoadShapes.Add(loading.ShapeName);
                if (loading.Visible && !covered) { covered = true; FirstLoadFrame(Path.Combine(qa, "first-load-" + language + "-cover.png")); }
                var page = await AutomationController.EvaluateAsync(main, FirstLoadProbe);
                if (page != null && page.Split('|')[1].Length > 0 && !firstLoadPages.Contains(page)) firstLoadPages.Add(page);
                if (revealed == null && !loading.Visible && page != null && page.Split('|')[2] == "login") revealed = DateTime.UtcNow;
                if (revealed != null && DateTime.UtcNow - revealed.Value > TimeSpan.FromSeconds(1)) break;
            }
            }
            finally { shownForQa = null; }
            foreach (var drawn in shown) if (!firstLoadShapes.Contains(drawn)) firstLoadShapes.Add(drawn);
            QaTrace("first-load-" + language + " address " + sessionAddress + " pages " + string.Join(" ; ", firstLoadPages) + " headings " + string.Join(" ; ", firstLoadHeadings) +
                " placeholders " + string.Join(" ; ", firstLoadShapes) + " banners " + string.Join(" ; ", firstLoadBanners));
            FirstLoadFrame(Path.Combine(qa, "first-load-" + language + ".png"));
            try
            {
                using (var client = main.GetDevToolsClient())
                    File.WriteAllBytes(Path.Combine(qa, "first-load-" + language + "-page.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            }
            catch (Exception e) { QaTrace("first-load-page-capture " + e.GetType().Name); }
        }

        private async Task FirstLoadChecksAsync(string language, string when, List<string> results)
        {
            Check(sessionAddress == SitePolicy.LanguageSwitch(language), "the new profile " + when + " opens with the " + language + " flag link", results);
            var last = await AutomationController.EvaluateAsync(main, FirstLoadProbe);
            Check(firstLoadPages.Count > 0 && firstLoadPages.All(page => page.Split('|')[1] == language) && last != null && last.Split('|')[2] == "login",
                "every page the new profile showed " + when + " was in " + language + ", ending on the sign-in page", results);
            Check(firstLoadPages.All(page => page.Split('|')[3].Length == 0) && last.Split('|')[3].Length == 0, "nothing was typed into the live sign-in form " + when, results);
            Check(firstLoadHeadings.All(heading => !KazakhLetters.IsMatch(heading)), "the toolbar heading never showed Kazakh " + when, results);
            Check(messages.Current?.Action != MessageAction.Settings, "no banner over the sign-in page " + when, results);
            // The session ends on the site's sign-in page: the cover draws that page from the start, never the home page first.
            Check(firstLoadShapes.Count > 0 && firstLoadShapes.All(shape => shape == "SignIn"),
                "the cover drew the sign-in page's placeholder all along " + when + " (" + string.Join(", ", firstLoadShapes) + ")", results);
            Check(!firstLoadBanners.Contains("saving"), "no «Сохранение…» banner while the session was replaced " + when + " (" + string.Join(", ", firstLoadBanners) + ")", results);
        }

        // Evidence, not a check: PrintWindow takes Chromium's area too, but a hidden QA window may give an empty picture.
        private void FirstLoadFrame(string path)
        {
            try { SaveNativeFrameForMotionQa(this, path); }
            catch (Exception e) { QaTrace("first-load-frame " + Path.GetFileName(path) + " " + e.GetType().Name); }
        }
    }
}
