using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // A tray balloon before a class (Core/ClassReminders.cs), from the kept week only: nothing is read from the site for it.
    // Each class once per run; a click opens the schedule (behind the PIN, only the lock). With a PIN set the balloon
    // does not name the class.
    internal sealed partial class MainForm
    {
        private readonly HashSet<string> remindersShown = new HashSet<string>(StringComparer.Ordinal);
        private Timer reminderTimer;
        // Self-test and the review have no tray icon, so no reminder is ever shown there by the clock.
        private void BuildReminders()
        {
            if (trayIcon == null) return;
            reminderTimer = new Timer { Interval = 20000 };
            reminderTimer.Tick += (s, e) => { UpdateTrayHint(); RemindClasses(DateTime.Now); };
            reminderTimer.Start();
        }
        private List<ScheduleClass> RemindClasses(DateTime now)
        {
            if (closing || rotating || trayIcon == null || !prefs.Reminders) return new List<ScheduleClass>();
            var due = ClassReminders.Due(scheduleWeek, now, prefs.ReminderLead, remindersShown);
            if (due.Count == 0) return due;
            foreach (var lesson in due) remindersShown.Add(ClassReminders.Key(lesson));
            var lang = prefs.Language;
            balloonOpens = "schedule";
            trayIcon.ShowBalloonTip(10000, ClassReminders.Title(due, now, lang), ClassReminders.Body(due, lang, LockArmed), ToolTipIcon.Info);
            return due;
        }
        private void DisposeReminders() { reminderTimer?.Stop(); reminderTimer?.Dispose(); reminderTimer = null; }
    }
}
