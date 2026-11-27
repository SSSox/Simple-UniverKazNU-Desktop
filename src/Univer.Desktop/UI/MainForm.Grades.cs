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
    // minutes the attestation page is read by a plain GET with the profile's cookies (Core/GradeFetch.cs): not a page load,
    // so no automation tick and no sign-in follow it, and a sign-in page or an error is simply no reading. Its table is
    // compared with the kept reading of this login; changed scores come as one tray balloon (behind a
    // PIN without the scores), and the reading becomes the new baseline. Only with a saved login (its mark) and a tray
    // icon; the kept reading goes with the session and when the option is turned off.
    internal sealed partial class MainForm
    {
        private GradeWatch gradeWatch = new GradeWatch();
        private bool gradesBusy, gradesClearPending, gradesClearInFlight;
        private long gradesDueAt = long.MinValue;
        private const long GradeRetryMs = 60000;
        private string gradeAgent;
        // The last balloon's words, for the self-test.
        private string gradeTitleShown, gradeBodyShown;
        // Self-test gives the login's mark and replaces the network with fixture pages.
        private string gradeAccountForQa;
        private Func<string, CancellationToken, Task<string>> gradeReadForQa;
        private string GradeAccount { get { return gradeAccountForQa ?? ScheduleAccount; } }
        private void LoadGradeSheet() { if (!storageFailed && prefs.Grades) gradeWatch.LoadBaseline(GradeAccount, storage.LoadGrades); }
        // A signed-in page: a reading is due once the interval has passed since the last one (the first, a minute after start).
        private void GradeContent()
        {
            if (gradesClearPending)
            {
                QueueCopyClear(ref gradesClearInFlight, ClearGradesAsync, () => gradesClearInFlight = false);
                return;
            }
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
            if (!prefs.Grades || gradesClearPending || gradesBusy || closing || rotating || main == null || main.IsDisposed || context == null) return;
            var epoch = sessionEpoch; var account = GradeAccount; var source = context; var watch = gradeWatch;
            if (account.Length == 0) return;
            var started = scheduleClock.ElapsedMilliseconds;
            var pageBlocked = false;
            var completed = false;
            gradesBusy = true;
            try
            {
                if (gradeAgent == null && gradeReadForQa == null)
                {
                    var agent = await AutomationController.EvaluateAsync(main, "navigator.userAgent");
                    if (!Current(epoch, account, source)) return;
                    gradeAgent = agent;
                }
                // The shown page must be the saved login's (ReadingAllowed), before the request and again before anything is kept:
                // a student who signed in by hand after the site's own logout gets no check under the saved login.
                pageBlocked = !await PageAllowedAsync(epoch, account, source);
                if (pageBlocked || watch != gradeWatch) return;
                // A PIN unlock can recover a saved login after the startup load.
                if (!storageFailed) watch.LoadBaseline(account, storage.LoadGrades);
                if (watch.HasPending)
                {
                    var wasWaitingForTray = watch.HasUnannouncedReading;
                    completed = await watch.RecordAsync(null, AnnounceGrades, storage.SaveGradesAsync);
                    if (completed || wasWaitingForTray || watch != gradeWatch || !Current(epoch, account, source)) return;
                    // The save still failed. Keep checking for grades while the file is unavailable.
                    pageBlocked = !await PageAllowedAsync(epoch, account, source);
                    if (pageBlocked || watch != gradeWatch) return;
                }
                var html = await (gradeReadForQa ?? ((url, token) => ReadAttestationAsync(source, url, token)))(SitePolicy.Origin + GradeFetch.Path, stopping);
                if (html == null || !Current(epoch, account, source) || !await PageAllowedAsync(epoch, account, source)) return;
                var at = DateTime.Now;
                var sheet = await Task.Run(() => GradeSheet.Parse(html, account, at));
                if (sheet == null || watch != gradeWatch || !Current(epoch, account, source)) return;
                completed = await watch.RecordAsync(sheet, AnnounceGrades, storage.SaveGradesAsync);
            }
            catch (Exception) { /* No reading this time. */ }
            finally
            {
                gradesBusy = false;
                if (watch == gradeWatch && Current(epoch, account, source))
                {
                    var now = scheduleClock.ElapsedMilliseconds;
                    gradesDueAt = pageBlocked ? Math.Min(gradesDueAt, now + GradeRetryMs) :
                        (completed ? started : now) + watch.NextDelay(prefs.GradeInterval, completed);
                }
            }
        }
        private async Task<bool> PageAllowedAsync(long epoch, string account, IRequestContext source)
        {
            string mark;
            try { mark = await PageMarkAsync(); } catch (Exception) { return false; }
            return mark != null && Current(epoch, account, source) && ReadingAllowed(mark);
        }
        private bool Current(long epoch, string account, IRequestContext source)
        {
            return !closing && !rotating && prefs.Grades && !gradesClearPending && epoch == sessionEpoch && source == context && account == GradeAccount;
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
        private bool AnnounceGrades(List<GradeChange> changes)
        {
            if (trayIcon == null) return false;
            var lang = prefs.Language;
            var title = GradeNotice.Title(changes, lang); var body = GradeNotice.Body(changes, lang, LockArmed);
            trayIcon.ShowBalloonTip(10000, title, body, ToolTipIcon.Info);
            gradeTitleShown = title; gradeBodyShown = body;
            balloonOpens = "grades";
            return true;
        }
        // Settings saved: turned off, the kept reading goes (turned on again, the first reading is silent); a shorter
        // interval brings the next reading closer.
        private void GradesChanged(bool wereOn)
        {
            if (!prefs.Grades)
            {
                if (wereOn)
                {
                    gradesClearPending = true;
                    QueueCopyClear(ref gradesClearInFlight, ClearGradesAsync, () => gradesClearInFlight = false);
                }
                return;
            }
            if (gradesDueAt != long.MinValue) gradesDueAt = Math.Min(gradesDueAt, scheduleClock.ElapsedMilliseconds + GradeNotice.Interval(prefs.GradeInterval) * 60000L);
        }
        // Every logout, Remove and account change (RotateSessionAsync), and the option turned off. A file another program
        // holds is removed on the next signed-in page; Storage.FinishSessionClear removes it at the next start too.
        private async Task ClearGradesAsync()
        {
            gradesClearPending = true;
            gradeWatch = new GradeWatch(); gradesDueAt = long.MinValue; gradeAgent = null;
            try { await storage.RemoveGradesAsync(); gradesClearPending = false; }
            catch (Exception) { gradesClearPending = true; }
        }
    }
}
