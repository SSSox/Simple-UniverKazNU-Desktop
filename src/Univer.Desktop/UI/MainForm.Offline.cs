using System;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // The app's own copies (the week, the coming exams, the last course totals) on a screen over the site window
    // (OfflineWeekView). Two roads lead to it:
    // - The site cannot be reached (siteUnreachable): only a failed connection raises it (a server that answers, even with an
    //   error, shows its own page). It stays through the automatic retries, which would otherwise cover it with the loading
    //   placeholder each time, and goes when the site answers, when the student opens something themselves or presses Retry.
    // - The student opens it from the offer (Core/OfflineEntry.cs): the site fails or waits too long, the app signs in by
    //   itself and a copy of this login is kept. The banner says so with «Открыть офлайн», beside a problem's own buttons.
    //   Opening changes nothing on the site: sign-in goes on under the screen, nothing is sent again. The screen goes by itself
    //   once the site shows signed-in content, and with the student's own Retry, «Вернуться на сайт», a section, Back/Forward.
    // Both go with the session, behind the PIN (nothing shows until the unlock) and when nothing is kept.
    internal sealed partial class MainForm
    {
        private OfflineWeekView offlineView;
        private bool siteUnreachable, recoveryLoading, offlineOffered;
        private string offlineDrawn;
        private readonly OfflineEntry offlineEntry = new OfflineEntry();
        private readonly System.Diagnostics.Stopwatch offlineClock = System.Diagnostics.Stopwatch.StartNew();
        // QA moves the offer's clock instead of waiting for it.
        private long offlineShiftForQa;
        private long OfflineNow { get { return offlineClock.ElapsedMilliseconds + offlineShiftForQa; } }
        // QA: the app signs in by itself (self-test has no saved sign-in); null in a real run.
        private bool? offlineSignsInForQa;
        private bool OfflineSignsIn { get { return offlineSignsInForQa ?? SignsInByItself(); } }
        private void BuildOffline()
        {
            offlineView = new OfflineWeekView(Theme.DpiScale) { Dock = DockStyle.Fill };
            offlineView.RetryPressed += Retry;
            offlineView.BackPressed += LeaveOffline;
            feedback.Offline.Click += (s, e) => OpenOffline();
            canvas.Controls.Add(offlineView);
        }
        private void SetUnreachable(bool value)
        {
            if (siteUnreachable == value && (!value || offlineView == null || offlineView.Visible)) return;
            siteUnreachable = value; UpdateOffline();
        }
        // A navigation begins: the reload a retry sent keeps the screen, anything else takes the unreachable one down. A screen
        // the student opened stays: sign-in's own navigations go on under it.
        private void OfflineNavigation()
        {
            var retry = recoveryLoading; recoveryLoading = false;
            if (!retry) SetUnreachable(false); else UpdateOffline();
        }
        private bool OfflineShown { get { return offlineView != null && offlineView.Visible; } }
        private bool OfflineHasCopy(DateTime now) { return OfflineWeek.Shows(scheduleWeek) || lastGrades != null || OfflineWeek.Exams(examPlan, now).Count > 0; }
        private void UpdateOffline()
        {
            if (offlineView == null || closing) return;
            var now = DateTime.Now;
            var chosen = offlineEntry.Chosen && !siteUnreachable;
            var show = (siteUnreachable || (offlineEntry.Chosen && OfflineSignsIn)) && OfflineHasCopy(now) && main != null && Active == main && document == null && !DownloadsVisible && !rotating && !LockShown;
            if (!show) { if (offlineView.Visible) offlineView.Visible = false; return; }
            // Drawn again when a copy, the language, the road or the minute changes (finished classes turn quiet as the day goes).
            var weekPart = OfflineWeek.Shows(scheduleWeek) ? scheduleWeek.GetHashCode() + "/" + scheduleWeek.SavedAt.Ticks : "-";
            var gradesPart = lastGrades != null ? lastGrades.GetHashCode() + "/" + lastGrades.SavedAt.Ticks : "-";
            var examPart = examPlan != null ? examPlan.GetHashCode() + "/" + examPlan.SavedAt.Ticks : "-";
            var drawn = weekPart + "|" + gradesPart + "|" + examPart + "|" + chosen + "|" + prefs.Language + "|" + now.ToString("yyyyMMddHHmm");
            if (drawn != offlineDrawn) { offlineDrawn = drawn; offlineView.Render(OfflineWeek.Shows(scheduleWeek) ? scheduleWeek : null, lastGrades, examPlan, now, prefs.Language, chosen); }
            var appearing = !offlineView.Visible;
            if (appearing) offlineView.Visible = true;
            offlineView.BringToFront();
            // The keys that went to the failed page (or to the banner's button) now scroll the schedule.
            if (appearing && (main.ContainsFocus || feedback.ContainsFocus || ActiveControl == null)) offlineView.FocusSheet();
        }
        // Each tick of the main timer (QA calls it itself): how the site window is doing, and whether the offer shows.
        private void WatchOffline()
        {
            if (offlineView == null || closing) return;
            if (LockShown || rotating || main == null || Active != main || document != null || DownloadsVisible || !OfflineSignsIn) offlineEntry.Forget();
            else
            {
                var failed = (error && OfflineEntry.Failure(failureCode)) || guard.Stopped;
                // A reload just sent (recoveryLoading until its navigation begins, then the page loading) is still a wait: the offer must
                // not blink off, nor a closed one come back, at every automatic retry.
                var waiting = loading.Visible || recoveryLoading || main.IsLoading || guard.Blocked || recoverAt.HasValue || SitePolicy.LoginPage(main.Address);
                offlineEntry.Observe(waiting, failed, OfflineNow);
            }
            UpdateOfflineOffer();
        }
        private void UpdateOfflineOffer()
        {
            var offered = !OfflineShown && OfflineSignsIn && offlineEntry.Offers(OfflineNow, OfflineHasCopy(DateTime.Now), LockShown);
            if (offered == offlineOffered) return;
            offlineOffered = offered;
            if (offered) messages.Update("offlineOffer"); else messages.Withdraw("offlineOffer");
            RenderFeedback();
        }
        private void OpenOffline()
        {
            if (closing || !OfflineSignsIn || !offlineEntry.Open(OfflineNow, OfflineHasCopy(DateTime.Now), LockShown)) return;
            UpdateOffline(); UpdateOfflineOffer();
            if (OfflineShown) offlineView.FocusSheet();
        }
        // The student leaves the screen they opened: the site shows again, and the offer returns if the trouble goes on.
        private void LeaveOffline()
        {
            if (!offlineEntry.Leave()) return;
            UpdateOffline(); UpdateOfflineOffer();
            if (Active == main && main != null && !OfflineShown) main.Focus();
        }
        // Signed-in content of the site (SignedInWork): the trouble is over and an opened screen goes by itself.
        private void OfflineSiteWorks()
        {
            var left = offlineEntry.SiteWorks();
            if (left) UpdateOffline();
            UpdateOfflineOffer();
        }
        private void OfflineDeclined() { offlineEntry.Decline(); UpdateOfflineOffer(); }
        // A new session: nothing of the previous one's trouble or choice carries over.
        private void OfflineNewSession() { offlineEntry.Reset(); offlineOffered = false; messages.Withdraw("offlineOffer"); UpdateOffline(); }
        // The lock came or went: behind it nothing of the copies shows, and the offer waits for the unlock.
        private void OfflineLockChanged() { if (LockShown) offlineEntry.Forget(); UpdateOffline(); UpdateOfflineOffer(); }
    }
}
