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
        private string ScheduleAccount { get { return ScheduleWeek.AccountMark(credentials?.Login); } }
        private void LoadScheduleCopy() { scheduleWeek = storageFailed ? null : storage.LoadSchedule(ScheduleAccount); }
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
                string raw;
                try { raw = await AutomationController.EvaluateAsync(browser, "window.__univerOverviewV1&&typeof window.__univerOverviewV1.week==='function'?window.__univerOverviewV1.week():''"); }
                catch (Exception) { return; }
                // The page went on, the session ended or the account changed while it answered: the answer is dropped.
                if (closing || rotating || epoch != sessionEpoch || browser != main || browser.Address != address || account != ScheduleAccount) return;
                var week = ScheduleWeek.Parse(raw, DateTime.Now, account);
                if (week == null) return;
                // The same classes are written again at most once an hour, so the saved date stays close to the last reading.
                if (week.SameAs(scheduleWeek) && week.SavedAt - scheduleWeek.SavedAt < TimeSpan.FromHours(1)) return;
                scheduleWeek = week;
                try { await storage.SaveScheduleAsync(week); } catch (Exception) { /* The copy in memory still serves this run. */ }
            }
            finally { scheduleBusy = false; }
        }
        // Every logout, Remove and account change (RotateSessionAsync): the copy goes with the session. A file another program
        // holds is removed on the next signed-in page; Storage.FinishSessionClear removes it at the next start too.
        private async Task ClearScheduleAsync()
        {
            scheduleWeek = null; scheduleReadAt = long.MinValue; scheduleReadAddress = null;
            try { await storage.RemoveScheduleAsync(); scheduleClearPending = false; }
            catch (Exception) { scheduleClearPending = true; }
        }
    }
}
