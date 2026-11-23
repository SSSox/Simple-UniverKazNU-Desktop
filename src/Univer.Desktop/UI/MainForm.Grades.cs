using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // New grades (Core/GradeWatch.cs): while a signed-in page of the site is shown (SignedInWork), every prefs.GradeInterval
    // minutes the attestation page is read by a plain GET with the profile's cookies (Web/GradeFetch.cs): not a page load,
    // so no automation tick and no sign-in follow it, and a sign-in page or an error is simply no reading. Its table is
    // compared with the kept reading of this login; changed scores of the same courses come as one tray balloon (behind a
    // PIN without the scores), and the reading becomes the new baseline. Only with a saved login (its mark) and a tray
    // icon; the kept reading goes with the session and when the option is turned off.
    internal sealed partial class MainForm
    {
        private GradeSheet gradeSheet;
        private bool gradesBusy, gradesClearPending;
        private long gradesDueAt = long.MinValue;
        private const long GradeRetryMs = 60000;
        private string gradeAgent;
        // The last balloon's words, for the self-test.
        private string gradeTitleShown, gradeBodyShown;
        // Self-test gives the login's mark and replaces the network with fixture pages.
        private string gradeAccountForQa;
        private Func<string, CancellationToken, Task<string>> gradeReadForQa;
        private string GradeAccount { get { return gradeAccountForQa ?? ScheduleAccount; } }
        private void LoadGradeSheet() { gradeSheet = storageFailed || !prefs.Grades ? null : storage.LoadGrades(GradeAccount); }
        // A signed-in page: a reading is due once the interval has passed since the last one (the first, a minute after start).
        private void GradeContent()
        {
            if (gradesClearPending) { gradesClearPending = false; QueueAsync(ClearGradesAsync); return; }
            if (!prefs.Grades || gradesBusy || closing || rotating || accountReview || trayIcon == null || main == null || context == null || GradeAccount.Length == 0) return;
            var now = scheduleClock.ElapsedMilliseconds;
            if (gradesDueAt == long.MinValue) { gradesDueAt = now + 60000; return; }
            if (now < gradesDueAt) return;
            gradesDueAt = now + GradeNotice.Interval(prefs.GradeInterval) * 60000L;
            QueueAsync(ReadGradesAsync);
        }
        // Never throws: a background check that fails says nothing (QueueAsync would show a network error).
        private async Task ReadGradesAsync()
        {
            if (gradesBusy || closing || rotating || main == null || main.IsDisposed || context == null) return;
            gradesBusy = true;
            try
            {
                var epoch = sessionEpoch; var account = GradeAccount; var source = context;
                if (account.Length == 0) return;
                if (gradeAgent == null && gradeReadForQa == null)
                {
                    gradeAgent = await AutomationController.EvaluateAsync(main, "navigator.userAgent");
                    if (closing || rotating || epoch != sessionEpoch) return;
                }
                // The shown page must be the saved login's (ReadingAllowed), before the request and again before anything is kept:
                // a student who signed in by hand after the site's own logout gets no check under the saved login.
                // Not allowed yet (the mark could not be read while a page was being replaced, or another account's page is
                // shown): asked again in a minute, not after the whole interval. That ask is a local read of the page only; the
                // site is requested only once the page is allowed.
                if (!await PageAllowedAsync(epoch, account, source))
                {
                    if (Current(epoch, account, source)) gradesDueAt = Math.Min(gradesDueAt, scheduleClock.ElapsedMilliseconds + GradeRetryMs);
                    return;
                }
                var html = await (gradeReadForQa ?? ((url, token) => ReadAttestationAsync(source, url, token)))(SitePolicy.Origin + GradeFetch.Path, stopping);
                // The session ended, the account changed or the app is closing while the page answered: the reading is dropped.
                if (html == null || !Current(epoch, account, source) || !await PageAllowedAsync(epoch, account, source)) return;
                var at = DateTime.Now;
                var sheet = await Task.Run(() => GradeSheet.Parse(html, account, at));
                if (sheet == null || !Current(epoch, account, source)) return;
                var changes = GradeSheet.Changes(gradeSheet, sheet);
                gradeSheet = sheet;
                try { await storage.SaveGradesAsync(sheet); } catch (Exception) { /* The reading in memory still serves this run. */ }
                if (changes.Count > 0 && Current(epoch, account, source)) AnnounceGrades(changes);
            }
            catch (Exception) { /* No reading this time. */ }
            finally { gradesBusy = false; }
        }
        private async Task<bool> PageAllowedAsync(long epoch, string account, IRequestContext source)
        {
            string mark;
            try { mark = await PageMarkAsync(); } catch (Exception) { return false; }
            return mark != null && Current(epoch, account, source) && ReadingAllowed(mark);
        }
        private bool Current(long epoch, string account, IRequestContext source)
        {
            return !closing && !rotating && prefs.Grades && epoch == sessionEpoch && source == context && account == GradeAccount;
        }
        private async Task<string> ReadAttestationAsync(IRequestContext source, string url, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var http = new SessionHttp(source, gradeAgent, SitePolicy.Origin + "/", TimeSpan.FromSeconds(30)))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(40));
                return await GradeFetch.ReadAsync(http.GetAsync, url, deadline.Token);
            }
        }
        private void AnnounceGrades(List<GradeChange> changes)
        {
            if (trayIcon == null) return;
            var lang = prefs.Language;
            gradeTitleShown = GradeNotice.Title(changes, lang); gradeBodyShown = GradeNotice.Body(changes, lang, LockArmed);
            balloonOpens = "grades";
            trayIcon.ShowBalloonTip(10000, gradeTitleShown, gradeBodyShown, ToolTipIcon.Info);
        }
        // Settings saved: turned off, the kept reading goes (turned on again, the first reading is silent); a shorter
        // interval brings the next reading closer.
        private void GradesChanged(bool wereOn)
        {
            if (!prefs.Grades) { if (wereOn) QueueAsync(ClearGradesAsync); return; }
            if (gradesDueAt != long.MinValue) gradesDueAt = Math.Min(gradesDueAt, scheduleClock.ElapsedMilliseconds + GradeNotice.Interval(prefs.GradeInterval) * 60000L);
        }
        // Every logout, Remove and account change (RotateSessionAsync), and the option turned off. A file another program
        // holds is removed on the next signed-in page; Storage.FinishSessionClear removes it at the next start too.
        private async Task ClearGradesAsync()
        {
            gradeSheet = null; gradesDueAt = long.MinValue; gradeAgent = null;
            try { await storage.RemoveGradesAsync(); gradesClearPending = false; }
            catch (Exception) { gradesClearPending = true; }
        }
    }
}
