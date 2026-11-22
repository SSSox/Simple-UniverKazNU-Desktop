using System;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // The site cannot be reached: the site window shows the app's own copy of the week (OfflineWeekView) under the banner
    // that offers Retry. Only a failed connection raises it (a server that answers, even with an error, shows its own
    // page). It stays through the automatic retries, which would otherwise cover it with the loading placeholder each
    // time, and goes when the site answers, when the student opens something themselves or presses Retry, and with the
    // session (no kept week and no kept totals, nothing to show). The kept course totals (MainForm.LastGrades.cs) stand
    // below the week, or alone when no week was kept.
    internal sealed partial class MainForm
    {
        private OfflineWeekView offlineView;
        private bool siteUnreachable, recoveryLoading;
        private string offlineDrawn;
        private void BuildOffline()
        {
            offlineView = new OfflineWeekView(Theme.DpiScale) { Dock = DockStyle.Fill };
            canvas.Controls.Add(offlineView);
        }
        private void SetUnreachable(bool value)
        {
            if (siteUnreachable == value && (!value || offlineView == null || offlineView.Visible)) return;
            siteUnreachable = value; UpdateOffline();
        }
        // A navigation begins: the reload a retry sent keeps the screen, anything else takes it down.
        private void OfflineNavigation()
        {
            var retry = recoveryLoading; recoveryLoading = false;
            if (!retry) SetUnreachable(false); else UpdateOffline();
        }
        private bool OfflineShown { get { return offlineView != null && offlineView.Visible; } }
        private void UpdateOffline()
        {
            if (offlineView == null || closing) return;
            var show = siteUnreachable && (OfflineWeek.Shows(scheduleWeek) || lastGrades != null) && main != null && Active == main && document == null && !DownloadsVisible && !rotating;
            if (!show) { if (offlineView.Visible) offlineView.Visible = false; return; }
            // Drawn again when the week, the language or the minute changes (finished classes turn quiet as the day goes).
            var now = DateTime.Now;
            var weekPart = OfflineWeek.Shows(scheduleWeek) ? scheduleWeek.GetHashCode() + "/" + scheduleWeek.SavedAt.Ticks : "-";
            var gradesPart = lastGrades != null ? lastGrades.GetHashCode() + "/" + lastGrades.SavedAt.Ticks : "-";
            var drawn = weekPart + "|" + gradesPart + "|" + prefs.Language + "|" + now.ToString("yyyyMMddHHmm");
            if (drawn != offlineDrawn) { offlineDrawn = drawn; offlineView.Render(OfflineWeek.Shows(scheduleWeek) ? scheduleWeek : null, lastGrades, now, prefs.Language); }
            var appearing = !offlineView.Visible;
            if (appearing) offlineView.Visible = true;
            offlineView.BringToFront();
            // The keys that went to the failed page now scroll the schedule.
            if (appearing && (main.ContainsFocus || ActiveControl == null)) offlineView.FocusSheet();
        }
    }
}
