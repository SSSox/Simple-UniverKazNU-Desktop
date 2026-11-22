using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // A tray balloon before a class (Core/ClassReminders.cs), from the kept week only: nothing is read from the site for it.
    // Each class once per run; a click opens the schedule (behind the PIN, only the lock). With a PIN set the balloon
    // does not name the class. When an exam reminder (MainForm.Exams.cs) is due on the same tick, both come in ONE balloon
    // (CombinedReminder): a second one would replace the first and its click target.
    internal sealed partial class MainForm
    {
        private readonly HashSet<string> remindersShown = new HashSet<string>(StringComparer.Ordinal);
        private Timer reminderTimer;
        // The last combined balloon's words, for the self-test.
        private string combinedTitleShown, combinedBodyShown;
        // Self-test and the review have no tray icon, so no reminder is ever shown there by the clock.
        private void BuildReminders()
        {
            if (trayIcon == null) return;
            reminderTimer = new Timer { Interval = 20000 };
            reminderTimer.Tick += (s, e) => { UpdateTrayHint(); RemindDue(DateTime.Now); };
            reminderTimer.Start();
        }
        // The classes and the exams due now: both in one balloon, else the one that is due.
        private void RemindDue(DateTime now)
        {
            var classes = ClassesDue(now); var exams = ExamsDue(now);
            if (classes.Count > 0 && exams.Count > 0) AnnounceBoth(classes, exams, now);
            else if (classes.Count > 0) AnnounceClasses(classes, now);
            else if (exams.Count > 0) AnnounceExams(exams, now);
        }
        // The classes due now (nothing is changed).
        private List<ScheduleClass> ClassesDue(DateTime now)
        {
            if (closing || rotating || trayIcon == null || !prefs.Reminders) return new List<ScheduleClass>();
            return ClassReminders.Due(scheduleWeek, now, prefs.ReminderLead, remindersShown);
        }
        private List<ScheduleClass> RemindClasses(DateTime now)
        {
            var due = ClassesDue(now);
            if (due.Count > 0) AnnounceClasses(due, now);
            return due;
        }
        private void AnnounceClasses(List<ScheduleClass> due, DateTime now)
        {
            foreach (var lesson in due) remindersShown.Add(ClassReminders.Key(lesson));
            var lang = prefs.Language;
            balloonOpens = "schedule";
            trayIcon.ShowBalloonTip(10000, ClassReminders.Title(due, now, lang), ClassReminders.Body(due, lang, LockArmed), ToolTipIcon.Info);
        }
        private void AnnounceBoth(List<ScheduleClass> classes, List<ExamReminder> exams, DateTime now)
        {
            foreach (var lesson in classes) remindersShown.Add(ClassReminders.Key(lesson));
            MarkExamsAnnounced(exams);
            var lang = prefs.Language; var hidden = ExamsHidden;
            balloonOpens = "schedule";
            combinedTitleShown = CombinedReminder.Title(classes, exams, now, lang, hidden); combinedBodyShown = CombinedReminder.Body(classes, exams, lang, hidden);
            trayIcon.ShowBalloonTip(10000, combinedTitleShown, combinedBodyShown, ToolTipIcon.Info);
        }
        private void DisposeReminders() { reminderTimer?.Stop(); reminderTimer?.Dispose(); reminderTimer = null; }
    }
}
