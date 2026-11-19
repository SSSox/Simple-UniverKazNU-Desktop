using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Reminders before classes: the settings row in each language, Save keeps the choice, and the balloon comes from the
        // kept week only, once per class, never when they are off. Synthetic classes on an isolated, hidden tray icon.
        private async Task RemindersQaAsync(string qa, List<string> results)
        {
            Check(prefs.Reminders && prefs.ReminderLead == ClassReminders.DefaultLead, "reminders before classes are on, 15 minutes ahead, by default", results);
            foreach (var lang in new[] { "ru", "kz", "en" })
            {
                var shown = prefs.Clone(); shown.Language = lang;
                using (var form = new SettingsForm(null, shown))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); form.SelectUpdatesForQa(); await Task.Delay(150);
                    var lead = form.LeadForQa;
                    var fits = form.LeadFitsForQa && lead.Visible && lead.Enabled && form.OverflowForQa().Length == 0;
                    QaTrace("reminders-" + lang + " " + form.LeadFitsForQa + " " + string.Join("|", form.OverflowForQa()));
                    Check(fits && lead.Text == string.Format(Strings.Get("reminderLead", lang), 15) && !form.SaveButton.Enabled,
                        "the reminder row fits with its lead choice and changes nothing until touched (" + lang + ")", results);
                    SaveBitmap(form, Path.Combine(qa, "reminders-" + lang + ".png"));
                    form.ChooseLeadForQa(5); await Task.Delay(50);
                    var chosen = form.ReminderLead == 5 && lead.Text == string.Format(Strings.Get("reminderLead", lang), 5) && form.SaveButton.Enabled && form.LeadFitsForQa;
                    form.RemindersForQa.Checked = false; await Task.Delay(50);
                    Check(chosen && !form.Reminders && !lead.Enabled && form.SaveButton.Enabled, "choosing a lead and turning reminders off are offered to Save (" + lang + ")", results);
                    form.Close();
                }
            }
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, reminders: false, reminderLead: 30) &&
                !prefs.Reminders && prefs.ReminderLead == 30 && !storage.LoadPreferences().Reminders && storage.LoadPreferences().ReminderLead == 30, "Save keeps reminders off and the lead chosen", results);
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, reminders: true, reminderLead: ClassReminders.DefaultLead) && prefs.Reminders,
                "Save turns them back on", results);
            messages.Withdraw("settingsSaved"); RenderFeedback();
            // The balloon: two groups of one class at the same time come together, a later class waits, and nothing repeats.
            var previousTray = trayIcon; var previousWeek = scheduleWeek; var previousOpens = balloonOpens;
            var now = DateTime.Now; var soon = now.AddMinutes(10);
            Func<DateTime, string, ScheduleClass> lesson = (start, room) => new ScheduleClass { Start = start, End = start.AddMinutes(50), Name = "QA Политология", Type = "Лекция", Room = room, Teacher = "" };
            using (var isolatedTray = new NotifyIcon { Visible = false })
            {
                trayIcon = isolatedTray; remindersShown.Clear();
                scheduleWeek = new ScheduleWeek { Start = now.Date.AddDays(-1), End = now.Date.AddDays(2), Classes = new List<ScheduleClass> { lesson(soon, "300"), lesson(soon, "301"), lesson(now.AddMinutes(40), "302") } };
                try
                {
                    var first = RemindClasses(now);
                    Check(first.Count == 2 && first.All(c => c.Start == soon) && balloonOpens == "schedule", "a class within the lead time is announced, together with the one at the same time", results);
                    balloonOpens = null;
                    Check(RemindClasses(now.AddMinutes(1)).Count == 0 && balloonOpens == null, "an announced class is not announced again", results);
                    prefs.Reminders = false;
                    Check(RemindClasses(now.AddMinutes(31)).Count == 0, "turned off, no reminder is shown", results);
                    prefs.Reminders = true;
                    Check(RemindClasses(now.AddMinutes(31)).Select(c => c.Room).SequenceEqual(new[] { "302" }), "the next class is announced in its own time", results);
                    scheduleWeek = null;
                    Check(RemindClasses(now).Count == 0, "without a kept week nothing is announced", results);
                }
                finally { trayIcon = previousTray; scheduleWeek = previousWeek; balloonOpens = previousOpens; prefs.Reminders = true; remindersShown.Clear(); }
            }
            Check(trayIcon != null || RemindClasses(now).Count == 0, "without a tray icon no reminder is shown", results);
        }
    }
}
