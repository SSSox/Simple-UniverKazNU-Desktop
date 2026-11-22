using System;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The app's own copy of the course totals the student last saw (Core/LastGrades.cs), for the offline screen. The home page
    // already reads them from the journal for its grades panel (overview.js), so the app asks the shown home page for that
    // reading (app → page, never the other way round; no request of its own), checks every field in C# and keeps it for this
    // login until the session ends. It is separate from the new-grade notifications' grades.json.
    internal sealed partial class MainForm
    {
        private readonly System.Diagnostics.Stopwatch lastGradesClock = System.Diagnostics.Stopwatch.StartNew();
        private static readonly System.Text.RegularExpressions.Regex HomePath = new System.Text.RegularExpressions.Regex(@"^/student/bachelor/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private LastGrades lastGrades;
        private bool lastGradesBusy, lastGradesClearPending;
        private long lastGradesReadAt = long.MinValue;
        private string lastGradesReadAddress, lastGradesAccountForQa;
        // The login mark the copy belongs to; the QA step sets its own synthetic one.
        private string LastGradesAccount { get { return lastGradesAccountForQa ?? ScheduleAccount; } }
        private void LoadLastGrades() { lastGrades = storageFailed ? null : storage.LoadLastGrades(LastGradesAccount); }
        // A signed-in home page of the main window: asked once a minute while shown, and again after ten seconds while the page
        // has nothing to give yet (it reads the journal a moment after it opens).
        private void LastGradesContent()
        {
            if (lastGradesClearPending) { lastGradesClearPending = false; QueueAsync(ClearLastGradesAsync); return; }
            if (lastGradesBusy || closing || rotating || document != null || main == null || Active != main) return;
            var address = main.Address; Uri uri;
            if (!SitePolicy.Trusted(address) || !Uri.TryCreate(address, UriKind.Absolute, out uri) || !HomePath.IsMatch(uri.AbsolutePath) || LastGradesAccount.Length == 0) return;
            var now = lastGradesClock.ElapsedMilliseconds;
            if (address == lastGradesReadAddress && lastGradesReadAt != long.MinValue && now - lastGradesReadAt < 60000) return;
            lastGradesReadAddress = address; lastGradesReadAt = now;
            QueueAsync(() => ReadLastGradesAsync(address));
        }
        private async Task ReadLastGradesAsync(string address)
        {
            var browser = main;
            if (lastGradesBusy || closing || rotating || browser == null || browser.IsDisposed || document != null || browser.Address != address) return;
            lastGradesBusy = true;
            try
            {
                var epoch = sessionEpoch; var account = LastGradesAccount;
                if (account.Length == 0) return;
                string raw;
                try { raw = await AutomationController.EvaluateAsync(browser, "window.__univerOverviewV1&&typeof window.__univerOverviewV1.grades==='function'?window.__univerOverviewV1.grades():''"); }
                catch (Exception) { return; }
                // The page went on, the session ended or the account changed while it answered: the answer is dropped.
                if (closing || rotating || epoch != sessionEpoch || browser != main || browser.Address != address || account != LastGradesAccount) return;
                var grades = LastGrades.Parse(raw, DateTime.Now, account);
                // Nothing to give yet: ask again soon instead of in a minute.
                if (grades == null) { lastGradesReadAt = lastGradesClock.ElapsedMilliseconds - 50000; return; }
                // The same totals are written again at most once an hour, so the saved time stays close to the last reading.
                if (grades.SameAs(lastGrades) && grades.SavedAt - lastGrades.SavedAt < TimeSpan.FromHours(1)) return;
                lastGrades = grades; UpdateOffline();
                try { await storage.SaveLastGradesAsync(grades); } catch (Exception) { /* The copy in memory still serves this run. */ }
            }
            finally { lastGradesBusy = false; }
        }
        // Every logout, Remove and account change (RotateSessionAsync): the copy goes with the session. A file another program
        // holds is removed on the next signed-in page; Storage.FinishSessionClear removes it at the next start too.
        private async Task ClearLastGradesAsync()
        {
            lastGrades = null; lastGradesReadAt = long.MinValue; lastGradesReadAddress = null; UpdateOffline();
            try { await storage.RemoveLastGradesAsync(); lastGradesClearPending = false; }
            catch (Exception) { lastGradesClearPending = true; }
        }
    }
}
