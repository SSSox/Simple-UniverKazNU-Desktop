using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The app's own copy of this week's classes (Core/ScheduleCopy.cs). The app asks the shown page for it (app → page, as
    // with the notices), never the other way round: the page→app channel carries signals only. Only the schedule page and
    // the Бакалавр page give it; the answer is checked in C# and kept for this login until the session ends.
    internal sealed partial class MainForm
    {
        private static readonly Regex ScheduleSources = new Regex(@"^/student/(?:bachelor|myschedule)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly System.Diagnostics.Stopwatch scheduleClock = System.Diagnostics.Stopwatch.StartNew();
        private ScheduleWeek scheduleWeek;
        private bool scheduleBusy, scheduleClearPending;
        private long scheduleReadAt = long.MinValue;
        private string scheduleReadAddress;
        // The visible --qa-offline run has no saved login: it gives its own synthetic mark, under which Program wrote its week
        // and totals (copies kept for no login at all are never read).
        private readonly string scheduleAccountForQa = Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-offline") >= 0 ? OfflineRunAccount : null;
        private string ScheduleAccount { get { return scheduleAccountForQa ?? ScheduleWeek.AccountMark(credentials?.Login); } }
        // The kept copies are read and used only while the app signs in by itself (Core/KeptCopies.cs): after the site's own
        // logout the login stays saved with automatic sign-in paused, and a copy a failed delete left must stay unread.
        private bool KeptCopiesReadable { get { return KeptCopies.Readable(SignsInByItself(), storageFailed, scheduleAccountForQa != null); } }
        private void LoadScheduleCopy() { scheduleWeek = KeptCopiesReadable ? storage.LoadSchedule(ScheduleAccount) : null; }
        // Called wherever automatic sign-in pauses or resumes (Core/KeptCopies.Step): a pause takes the copies out of memory at
        // once, before any disk write that could fail (the tray, the reminders and the offline screen read memory only); a
        // resume for the same login reads them again. Signing in by hand (Settings) pauses without ending the session: the
        // copies stay on disk for this login until the session ends.
        private void SyncKeptCopies()
        {
            var readable = KeptCopiesReadable; var changed = false;
            switch (KeptCopies.Step(readable, scheduleWeek != null, scheduleClearPending))
            {
                case KeptCopyStep.Drop: scheduleWeek = null; changed = true; break;
                case KeptCopyStep.Load: LoadScheduleCopy(); changed |= scheduleWeek != null; break;
            }
            switch (KeptCopies.Step(readable, examPlan != null, examClearPending))
            {
                case KeptCopyStep.Drop: examPlan = null; changed = true; break;
                case KeptCopyStep.Load: LoadExamPlan(); changed |= examPlan != null; break;
            }
            switch (KeptCopies.Step(readable, lastGrades != null, lastGradesClearPending))
            {
                case KeptCopyStep.Drop: lastGrades = null; changed = true; break;
                case KeptCopyStep.Load: LoadLastGrades(); changed |= lastGrades != null; break;
            }
            if (changed && !closing) { UpdateOffline(); UpdateOfflineOffer(); UpdateTrayHint(); }
        }
        // A signed-in page of the main window: the two pages that know the week are asked once a minute while shown.
        private void ScheduleContent()
        {
            if (scheduleClearPending) { scheduleClearPending = false; QueueAsync(ClearScheduleAsync); return; }
            if (scheduleBusy || closing || rotating || document != null || main == null || Active != main) return;
            var address = main.Address; Uri uri;
            if (!SitePolicy.Trusted(address) || !Uri.TryCreate(address, UriKind.Absolute, out uri) || !ScheduleSources.IsMatch(uri.AbsolutePath)) return;
            var now = scheduleClock.ElapsedMilliseconds;
            if (address == scheduleReadAddress && scheduleReadAt != long.MinValue && now - scheduleReadAt < 60000) return;
            scheduleReadAddress = address; scheduleReadAt = now;
            QueueAsync(() => ReadScheduleAsync(address));
        }
        private async Task ReadScheduleAsync(string address)
        {
            var browser = main;
            if (scheduleBusy || closing || rotating || browser == null || browser.IsDisposed || document != null || browser.Address != address) return;
            scheduleBusy = true;
            try
            {
                var epoch = sessionEpoch; var account = ScheduleAccount;
                string answer, pageMark, raw;
                try { answer = await AutomationController.EvaluateAsync(browser, PageAccount.Script("window.__univerOverviewV1&&typeof window.__univerOverviewV1.week==='function'?window.__univerOverviewV1.week():''")); }
                catch (Exception) { return; }
                // The page went on, the session ended or the account changed while it answered: the answer is dropped. So is a
                // page of another account than the saved login, or one read while the app does not sign in by itself.
                if (closing || rotating || epoch != sessionEpoch || browser != main || browser.Address != address || account != ScheduleAccount) return;
                if (!PageAccount.Split(answer, out pageMark, out raw) || !ReadingAllowed(pageMark)) return;
                var week = ScheduleWeek.Parse(raw, DateTime.Now, account);
                if (week == null) return;
                // The same classes are written again at most once an hour, so the saved date stays close to the last reading.
                if (week.SameAs(scheduleWeek) && week.SavedAt - scheduleWeek.SavedAt < TimeSpan.FromHours(1)) return;
                scheduleWeek = week; UpdateTrayHint();
                try { await storage.SaveScheduleAsync(week); } catch (Exception) { /* The copy in memory still serves this run. */ }
            }
            finally { scheduleBusy = false; }
        }
        // Every logout, Remove and account change (RotateSessionAsync): the copy goes with the session. A file another program
        // holds is removed on the next signed-in page; Storage.FinishSessionClear removes it at the next start too.
        private async Task ClearScheduleAsync()
        {
            scheduleWeek = null; scheduleReadAt = long.MinValue; scheduleReadAddress = null; UpdateOffline(); UpdateTrayHint();
            try { await storage.RemoveScheduleAsync(); scheduleClearPending = false; }
            catch (Exception) { scheduleClearPending = true; }
        }
    }
}
