using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;

namespace Univer.Desktop.Web
{
    // Drives the trusted page from the polling tick: view, sign-in, the site's re-auth dialog, language, the read notice
    // and agreement, and when the page may be revealed. One tick at a time; a navigation drops older generations' work.
    internal sealed class AutomationController
    {
        // Limits on the automation clock, in milliseconds.
        private const long OutcomeLimit = 25000, FormLimit = 45000, LanguageWindow = 60000, LanguagePause = 5000;
        private const int LanguageAttempts = 3;
        private const string ProbeScript = "window.__univerAutomationV1 ? JSON.stringify(window.__univerAutomationV1.probe()) : 'bootstrap'";
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        // Page scripts in load order, read once per process and reused by every session and registration.
        private static readonly Lazy<string> Bundle = new Lazy<string>(() => Scripts("paint-gate.js", "page-common.js", "univer-automation.js", "news.js", "study-links.js", "study-pages.js", "course-tasks.js", "attendance.js", "overview.js", "shortcuts.js", "notices.js", "univer-layout.js", "theme.js", "desktop.js"));
        private readonly string bundle = Bundle.Value;
        // The scripts name the origin themselves; ScriptBundle rewrites it from SitePolicy, the single native source.
        private static string Scripts(params string[] names)
        { return string.Join("\n", Array.ConvertAll(names, name => ScriptBundle.WithOrigin(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", name))))); }
        private readonly SessionGuard guard;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Action<string> status;
        private readonly Action closeViewer;
        private readonly Action<ChromiumWebBrowser, bool> presentation;
        // Raised when a signed-in page is shown with nothing left for automatic sign-in to do.
        public Action SignedInContent;
        // Raised when only a reload of the page can continue: a stalled or refused sign-in, an unconfirmed notice or
        // agreement, a login form that never became usable.
        public Action RecoveryNeeded;
        private readonly Queue<long> switches = new Queue<long>();
        private int generation;
        private bool busy, failedAgreement, resumePending;
        private string pendingAgreement, failedNotice, stopStatus, resumeUrl;
        private long agreementAt, switchUntil, began;
        private readonly AppearanceTimer noticeTimer = new AppearanceTimer(), agreementTimer = new AppearanceTimer();
        public AutomationController(SessionGuard guard, Action<string> status, Action closeViewer, Action<ChromiumWebBrowser, bool> presentation)
        { this.guard = guard; this.status = status; this.closeViewer = closeViewer; this.presentation = presentation; }
        // Registered to run at document start: the page scripts, the paint gate's sign-in state and, once the document
        // is parsed, the chosen view.
        public string DocumentScript(Preferences prefs, bool viewer, bool active)
        { return bundle + "\n" + ConfigurePaint(active) + Early(prefs) + "document.addEventListener('DOMContentLoaded',function(){" + ApplyView(prefs, viewer) + "},{once:true});"; }
        // Before the page has parsed: the reading layout in the dark theme starts on a dark page instead of the site's white.
        private string Early(Preferences prefs) { return "window.__univerTheme=" + Theme() + ";window.__univerThemeV1&&window.__univerThemeV1.early(" + Js(prefs.Readable) + ");"; }
        private string Theme() { return Dark ? "'dark'" : "'light'"; }
        private static string Js(bool value) { return value ? "true" : "false"; }
        private static string ConfigurePaint(bool active) { return "window.__univerPaintV1.configure(" + Js(active) + ");"; }
        // Motion, pinned shortcuts (plain data from validated preferences) and the reading layout of this view.
        private string ApplyView(Preferences prefs, bool viewer)
        {
            return "window.__univerMotion=" + Js(prefs.Animations) + ";window.__univerTheme=" + Theme() + ";window.__univerShortcutsV1&&window.__univerShortcutsV1.configure(" + Shortcuts.ToJson(prefs.Pins) + "," + Shortcuts.OrderJson(prefs.TileOrder) + ");" +
                "window.__univerDesktop.apply(" + Js(prefs.Readable) + "," + Js(viewer) + "," + Quote(prefs.Language) + ");";
        }
        // The app's dark theme, for the reading layout; Original layout ignores it.
        public bool Dark { get; set; }
        public void Navigating() { generation++; began = clock.ElapsedMilliseconds; guard.Navigated(); }
        public void Retry()
        {
            generation++; guard.Retry(); pendingAgreement = null; failedAgreement = false; stopStatus = null;
            failedNotice = null; noticeTimer.Reset(); agreementTimer.Reset(); switches.Clear();
        }
        // An automatic reload: this page's failures and confirmation reservations are forgotten, every attempt budget stays.
        public void Recover()
        {
            generation++; pendingAgreement = null; failedAgreement = false;
            failedNotice = null; noticeTimer.Reset(); agreementTimer.Reset();
            guard.Notices.Forget(); guard.Agreements.Forget();
        }
        // The page the site replaced by its login form, reopened once after the automatic sign-in; null forgets it.
        public void ResumeAfterLogin(string url) { resumeUrl = url; resumePending = false; }
        public void LanguageSelected() { generation++; switches.Clear(); switchUntil = clock.ElapsedMilliseconds + LanguagePause; }
        public void AjaxOutcome(string kind, string challenge, bool success)
        {
            if (kind == "reauth") guard.ReauthResult(challenge, success);
            else if (kind == "agreement" && pendingAgreement != null && pendingAgreement == challenge)
            { pendingAgreement = null; failedAgreement = !success; }
        }
        public async Task TickAsync(ChromiumWebBrowser browser, Credentials credentials, Preferences prefs, bool viewer)
        {
            if (busy || browser == null || browser.IsDisposed || !browser.IsBrowserInitialized || !SitePolicy.Trusted(browser.Address)) return;
            busy = true; var epoch = generation;
            try { await StepAsync(browser, credentials, prefs, viewer, epoch); }
            catch (Exception) { if (epoch == generation && !browser.IsDisposed) status("scriptWaiting"); }
            finally
            {
                try { await RevealAsync(browser, credentials, prefs, viewer, epoch); }
                catch (Exception) { /* Native deadline exposes a recoverable error instead of an endless cover. */ }
                busy = false;
            }
        }
        // Probes the page, applies the view, then takes at most one sign-in, language, notice or agreement action.
        private async Task StepAsync(ChromiumWebBrowser browser, Credentials credentials, Preferences prefs, bool viewer, int epoch)
        {
            var raw = await EvaluateAsync(browser, ProbeScript);
            if (raw == "bootstrap") raw = await EvaluateAsync(browser, bundle + "\nJSON.stringify(window.__univerAutomationV1.probe())");
            if (epoch != generation || raw == null || !SitePolicy.Trusted(browser.Address)) return;
            var p = new Probe(json.Deserialize<Dictionary<string, object>>(raw));
            if (!p.Bool("trusted")) return;
            var active = credentials != null && !prefs.AutoLoginPaused;
            // One round trip; each step stays isolated so a failing layout cannot hide the viewer's legacy Back.
            var applied = await EvaluateAsync(browser, "try{" + ConfigurePaint(active && !guard.Blocked && !guard.CoverSuspended) + "}catch(_){}try{" + ApplyView(prefs, viewer) + "}catch(_){}" +
                "window.__univerDesktop.close ? 'close' : 'ok'");
            // Poll legacy history.back/go without exposing a privileged native bridge.
            if (viewer && applied == "close") { closeViewer(); return; }
            if (epoch != generation || !p.Bool("ready")) return;
            var now = clock.ElapsedMilliseconds; var doc = p.Str("document");
            if (active && await PendingSignInAsync(browser, credentials, p, doc, now, viewer)) return;
            if (await LanguageAsync(browser, prefs, p, now)) return;
            if (!active) return;
            if (p.Bool("loginPage")) { await LoginAsync(browser, credentials, p, doc, now); return; }
            if (guard.LoginDocument != null && p.Bool("hasContent")) { guard.Authenticated(); resumePending = resumeUrl != null; status("ready"); }
            if (await NoticeAsync(browser, p, doc, now, epoch)) return;
            await AgreementAsync(browser, p, doc, now);
            if (!p.Bool("notice") && !p.Bool("agreement"))
            {
                status("ready");
                if (!p.Bool("hasContent") || guard.LoginDocument != null || guard.ReauthChallenge != null) return;
                // The read notice and agreement come first; then the page the ended session was asked for.
                var resume = resumeUrl;
                if (resumePending && !viewer)
                {
                    resumeUrl = null; resumePending = false;
                    // The new generation keeps this page covered until the reopened one is prepared.
                    if (!SitePolicy.SamePage(resume, browser.Address) && SitePolicy.Resumable(resume)) { generation++; browser.Load(resume); return; }
                }
                SignedInContent?.Invoke();
            }
        }
        // Signing in again needs no user: a newer document of the site continues, and a reload brings one.
        private void Restore(bool reload) { status("sessionRestoring"); if (reload) RecoveryNeeded?.Invoke(); }
        // Outcomes of an earlier sign-in, re-authentication or agreement, and the site's own sign-in dialog. True when
        // this tick has nothing more to do.
        private async Task<bool> PendingSignInAsync(ChromiumWebBrowser browser, Credentials credentials, Probe p, string doc, long now, bool viewer)
        {
            if (guard.ReauthChallenge != null)
            {
                var outcome = p.Str("reauthOutcome");
                if (p.Str("reauthResultChallenge") == guard.ReauthChallenge && (outcome == "success" || outcome == "failure"))
                    guard.ReauthResult(guard.ReauthChallenge, outcome == "success");
                guard.ReauthTimedOut(now);
            }
            // A newer page of the signed-in account (the header's logout link, the notice or agreement it is shown, or a
            // printed document) ends a block that proved nothing about the password.
            if (guard.Blocked && !p.Bool("loginPage") && !p.Bool("reauth") &&
                (p.Bool("authenticated") || p.Bool("notice") || p.Bool("agreement") || (viewer && p.Bool("hasContent"))) && guard.AuthenticatedPage(doc))
                status("ready");
            if (guard.Stopped) { status(stopStatus ?? "automationFailed"); return true; }
            if (p.Bool("reauth"))
            {
                if (p.Int("reauthAttempt") > 1) guard.ReauthRejected(doc, now);
                else if (p.Bool("reauthReady") && guard.TryReauth(p.Str("reauthChallenge"), doc, now))
                {
                    status("signingIn");
                    var result = await CallAsync(browser, "reauthenticate", doc, p.Str("reauthChallenge"), credentials.Login, credentials.Password);
                    // The dialog's readonly login belongs to another account: never replace it.
                    if (result == "account-mismatch") { guard.Stop(); stopStatus = "accountMismatch"; status(stopStatus); }
                    else if (result == "not-ready") guard.ReleaseReauth(p.Str("reauthChallenge"));
                    return true;
                }
                // A refused dialog reloads the page, whose ended session then shows the full-page login form.
                if (guard.Blocked) Restore(guard.NeedsNewDocument(doc));
                return true;
            }
            if (guard.ReauthChallenge != null) { status("signingIn"); return true; }
            // Blocked away from the login form: a reload lets the site show the page or ask for sign-in again.
            if (guard.Blocked && !p.Bool("loginPage")) { Restore(true); return true; }
            if (pendingAgreement != null)
            {
                var outcome = p.Str("agreementOutcome");
                if (p.Str("agreementResultEpisode") == pendingAgreement && outcome == "success") pendingAgreement = null;
                else if (outcome == "failure" || now - agreementAt > OutcomeLimit) { failedAgreement = true; pendingAgreement = null; }
                if (pendingAgreement != null) { status("confirming"); return true; }
            }
            if (failedAgreement) { status("agreementFailed"); RecoveryNeeded?.Invoke(); return true; }
            return false;
        }
        // The site shows another language than the chosen one: switch it back, at most three times a minute. True when
        // this tick has nothing more to do; a site that keeps its language never holds up sign-in or the notice.
        private async Task<bool> LanguageAsync(ChromiumWebBrowser browser, Preferences prefs, Probe p, long now)
        {
            var actual = p.Str("language");
            // A flag click navigates away: the notice, agreement or sign-in form on this page goes first, and the page
            // after it switches the language.
            if (actual.Length == 0 || actual == prefs.Language || now <= switchUntil || p.Bool("hasPendingForm")) return false;
            while (switches.Count > 0 && now - switches.Peek() > LanguageWindow) switches.Dequeue();
            if (switches.Count >= LanguageAttempts) { status("languageFailed"); return false; }
            switches.Enqueue(now); switchUntil = now + LanguagePause;
            await CallAsync(browser, "switchLanguage", prefs.Language);
            return true;
        }
        private async Task LoginAsync(ChromiumWebBrowser browser, Credentials credentials, Probe p, string doc, long now)
        {
            if (guard.LoginVisible(doc, now))
            {
                if (guard.Stopped) status(stopStatus ?? "automationFailed"); else Restore(guard.NeedsNewDocument(doc));
                return;
            }
            if (p.Bool("loginReady") && guard.TryLogin(doc, now))
            {
                status("signingIn");
                // Only a confirmed non-submission returns the reservation; a lost result stays reserved against a double POST.
                if (await CallAsync(browser, "login", doc, credentials.Login, credentials.Password) == "not-ready") guard.ReleaseLogin(doc);
            }
            // A full budget: the next login follows by itself.
            else if (p.Bool("loginReady") && guard.LoginDocument == null) Restore(false);
            // A form that never became usable is reloaded.
            else if (!p.Bool("loginReady") && now - began > FormLimit) { status("elementsMissing"); RecoveryNeeded?.Invoke(); }
        }
        // The known read notice. True when this tick has nothing more to do.
        private async Task<bool> NoticeAsync(ChromiumWebBrowser browser, Probe p, string doc, long now, int epoch)
        {
            if (!p.Bool("notice")) { noticeTimer.Reset(); if (p.Bool("hasContent")) guard.Notices.Observe(false, now); return false; }
            guard.Notices.Observe(true, now); var key = p.Str("noticeKey"); var episode = p.Str("noticeEpisode");
            if (failedNotice == episode || noticeTimer.Expired(episode, now, OutcomeLimit)) { failedNotice = episode; status("noticeFailed"); RecoveryNeeded?.Invoke(); return true; }
            status("confirming");
            if (!guard.Notices.CanSubmit(key, now)) return false;
            var ready = p.Bool("checked") ? p.Bool("submitEnabled") : await CallAsync(browser, "prepareNotice", doc, episode) == "ready";
            if (epoch != generation) return true;
            if (ready)
            {
                guard.Notices.Submitted(key, now);
                if (await CallAsync(browser, "submitNotice", doc, episode) == "not-ready") guard.Notices.Release(key);
            }
            return false;
        }
        // The explicitly approved agreement: tick its checkbox, then submit it once.
        private async Task AgreementAsync(ChromiumWebBrowser browser, Probe p, string doc, long now)
        {
            if (!p.Bool("agreement")) { agreementTimer.Reset(); if (p.Bool("hasContent")) guard.Agreements.Observe(false, now); return; }
            guard.Agreements.Observe(true, now); var key = p.Str("agreementKey"); var episode = p.Str("agreementEpisode");
            if (guard.Agreements.CanSubmit(key, now))
            {
                if (!p.Bool("agreementChecked")) await CallAsync(browser, "checkAgreement", doc, episode);
                else if (p.Bool("agreementEnabled"))
                {
                    guard.Agreements.Submitted(key, now); pendingAgreement = episode; agreementAt = now;
                    if (await CallAsync(browser, "submitAgreement", doc, episode) == "not-ready") { guard.Agreements.Release(key); pendingAgreement = null; }
                }
            }
            if (agreementTimer.Expired(episode, now, OutcomeLimit)) { failedAgreement = true; status("agreementFailed"); }
        }
        // Reveals the prepared page once it no longer waits for a sign-in, language switch or agreement; a failure is
        // revealed too, so the page is never left under the loading cover.
        private async Task RevealAsync(ChromiumWebBrowser browser, Credentials credentials, Preferences prefs, bool viewer, int epoch)
        {
            if (epoch != generation || browser.IsDisposed || !SitePolicy.Trusted(browser.Address)) return;
            var latest = await EvaluateAsync(browser, "window.__univerAutomationV1 ? JSON.stringify(window.__univerAutomationV1.probe()) : null");
            if (latest == null || epoch != generation) return;
            var value = new Probe(json.Deserialize<Dictionary<string, object>>(latest));
            var active = credentials != null && !prefs.AutoLoginPaused;
            var failed = guard.Blocked || guard.CoverSuspended || failedAgreement || (failedNotice != null && failedNotice == value.Str("noticeEpisode"));
            var languagePending = !value.Bool("hasPendingForm") && value.Str("language").Length > 0 && value.Str("language") != prefs.Language && switches.Count < LanguageAttempts;
            // Reveal at DOMContentLoaded: images and late resources keep loading under the adapted page. After an automatic
            // sign-in the page in between stays covered until the page that was asked for reopens.
            var hold = !value.Bool("domReady") || (!failed && (languagePending || (active && (value.Bool("hasPendingForm") || guard.ReauthChallenge != null || pendingAgreement != null || (resumePending && !viewer)))));
            if (hold) { presentation(browser, true); return; }
            var result = await EvaluateAsync(browser, "window.__univerPaintV1.complete(" + Js(failed) + "," + Js(prefs.Readable) + "," + Js(viewer) + "," + Quote(prefs.Language) + ")");
            if (epoch == generation && result == "paint-ready") presentation(browser, false);
        }
        public async Task<string> CallAsync(ChromiumWebBrowser browser, string method, params string[] args)
        {
            var quoted = Array.ConvertAll(args, Quote);
            return await EvaluateAsync(browser, "window.__univerAutomationV1." + method + "(" + string.Join(",", quoted) + ")");
        }
        private string Quote(string value) { return json.Serialize(value).Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029"); }
        internal static async Task<string> EvaluateAsync(ChromiumWebBrowser browser, string script)
        {
            if (browser == null || browser.IsDisposed || !browser.IsBrowserInitialized || !SitePolicy.Trusted(browser.Address)) return null;
            using (var frame = browser.GetMainFrame())
            {
                if (!frame.IsValid || !SitePolicy.Trusted(frame.Url)) return null;
                try
                {
                    var result = await frame.EvaluateScriptAsync(script, timeout: TimeSpan.FromSeconds(3));
                    return result.Success ? result.Result as string : null;
                }
                catch (TaskCanceledException)
                {
                    // CEF cancels pending IPC when navigation destroys the context or its three-second deadline passes; the bounded poll
                    // retries, so no caller token is passed.
                    return null;
                }
            }
        }
        // The page's probe answer: missing keys read as empty, false or zero.
        private sealed class Probe
        {
            private readonly Dictionary<string, object> values;
            public Probe(Dictionary<string, object> values) { this.values = values ?? new Dictionary<string, object>(); }
            public string Str(string key) { object value; return values.TryGetValue(key, out value) ? value as string ?? "" : ""; }
            public bool Bool(string key) { object value; return values.TryGetValue(key, out value) && value is bool && (bool)value; }
            public int Int(string key) { object value; return values.TryGetValue(key, out value) ? Convert.ToInt32(value) : 0; }
        }
    }
}
