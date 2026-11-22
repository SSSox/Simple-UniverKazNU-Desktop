using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The app's own copy of the exams and midterms (Core/ExamCopy.cs) and the tray reminders from it: the evening before at
    // 19:00 and the lead time before the start (prefs.ReminderLead, like the classes). The app asks the shown exam or midterm
    // schedule page for its list (app → page, never the other way round), checks every field in C#, and keeps it for this
    // login until the session ends. A reminder is announced once: the plan keeps what it said, so a restart does not repeat it.
    internal sealed partial class MainForm
    {
        private readonly System.Diagnostics.Stopwatch examClock = System.Diagnostics.Stopwatch.StartNew();
        private ExamPlan examPlan;
        private bool examBusy, examClearPending;
        private long examReadAt = long.MinValue;
        private string examReadAddress, examAccountForQa, examTitleShown, examBodyShown;
        private bool? examHiddenForQa;
        // The last write of what was announced; the QA step waits for it.
        private Task examSaving = Task.FromResult(true);
        // The login mark the plan belongs to; the QA step sets its own synthetic one.
        private string ExamAccount { get { return examAccountForQa ?? ScheduleAccount; } }
        private void LoadExamPlan() { var account = ExamAccount; examPlan = storageFailed || account.Length == 0 ? null : storage.LoadExams(account); }
        // A signed-in page of the main window: the exam and midterm schedule pages are asked once a minute while shown.
        private void ExamContent()
        {
            if (examClearPending) { examClearPending = false; QueueAsync(ClearExamsAsync); return; }
            if (examBusy || closing || rotating || document != null || main == null || Active != main) return;
            var address = main.Address;
            if (!SitePolicy.ExamPage(address) || ExamAccount.Length == 0) return;
            var now = examClock.ElapsedMilliseconds;
            if (address == examReadAddress && examReadAt != long.MinValue && now - examReadAt < 60000) return;
            examReadAddress = address; examReadAt = now;
            QueueAsync(() => ReadExamsAsync(address));
        }
        private async Task ReadExamsAsync(string address)
        {
            var browser = main;
            if (examBusy || closing || rotating || browser == null || browser.IsDisposed || document != null || browser.Address != address) return;
            examBusy = true;
            try
            {
                var epoch = sessionEpoch; var account = ExamAccount;
                if (account.Length == 0) return;
                string raw;
                try { raw = await AutomationController.EvaluateAsync(browser, "window.__univerOverviewV1&&typeof window.__univerOverviewV1.exams==='function'?window.__univerOverviewV1.exams():''"); }
                catch (Exception) { return; }
                // The page went on, the session ended or the account changed while it answered: the answer is dropped.
                if (closing || rotating || epoch != sessionEpoch || browser != main || browser.Address != address || account != ExamAccount || !SitePolicy.ExamPage(address)) return;
                var now = DateTime.Now; ExamKind kind; List<ExamItem> items;
                if (!ExamPlan.Parse(raw, now, out kind, out items)) return;
                var current = examPlan != null && examPlan.Account == account ? examPlan : null;
                var plan = (current ?? ExamPlan.Empty(account, now)).Replace(kind, items, now);
                // The same list is written again at most once an hour.
                if (current != null && plan.SameAs(current) && plan.SavedAt - current.SavedAt < TimeSpan.FromHours(1)) return;
                examPlan = plan;
                try { await storage.SaveExamsAsync(plan); } catch (Exception) { /* The copy in memory still serves this run. */ }
            }
            finally { examBusy = false; }
        }
        // The balloon for the reminder that is due, if any; the tray timer asks every 20 s (MainForm.Reminders.cs). Behind the
        // PIN it names no subject or room, and a click only brings the window forward.
        private List<ExamReminder> RemindExams(DateTime now)
        {
            if (closing || rotating || trayIcon == null || !prefs.Reminders || examPlan == null || examPlan.Account != ExamAccount) return new List<ExamReminder>();
            var due = examPlan.Due(now, prefs.ReminderLead);
            if (due.Count == 0) return due;
            examPlan = examPlan.WithAnnounced(due.Select(r => r.Key));
            var plan = examPlan; var lang = prefs.Language; var hidden = examHiddenForQa ?? LockArmed;
            examSaving = SaveExamPlanQuietAsync(plan);
            balloonOpens = due[0].Item.Kind == ExamKind.Exam ? "exams" : "midterms";
            examTitleShown = ExamReminders.Title(due, now, lang, hidden); examBodyShown = ExamReminders.Body(due, lang, hidden);
            trayIcon.ShowBalloonTip(10000, examTitleShown, examBodyShown, ToolTipIcon.Info);
            return due;
        }
        private async Task SaveExamPlanQuietAsync(ExamPlan plan)
        {
            try { await storage.SaveExamsAsync(plan); } catch (Exception) { /* Said once more after a restart at worst. */ }
        }
        // Every logout, Remove and account change (RotateSessionAsync): the copy goes with the session. A file another program
        // holds is removed on the next signed-in page; Storage.FinishSessionClear removes it at the next start too.
        private async Task ClearExamsAsync()
        {
            examPlan = null; examReadAt = long.MinValue; examReadAddress = null;
            try { await storage.RemoveExamsAsync(); examClearPending = false; }
            catch (Exception) { examClearPending = true; }
        }
    }
}
