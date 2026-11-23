using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // An exam or midterm schedule page as the site draws it: a heading table, then the list with a date row over its exams.
        private static string ExamPageFixture(string title, string rows, string mark = null)
        {
            return "<!doctype html><html><head><title>" + title + "</title></head><body>" + (mark == null ? "" : "<script>localStorage.setItem('" + PageAccount.Key + "','" + mark + "');</script>") + "<div id=\"l4\"><table><tr><td><table id=\"head\"><tr><th>Дисциплина</th><th>Преподаватель</th><th>Тип</th><th>Аудитория</th></tr></table>" +
                "<table id=\"scheduleList\">" + rows + "</table></td></tr></table></div></body></html>";
        }
        private static string ExamRowsFixture(DateTime start, string subject, string kind)
        {
            return "<tr><th colspan=\"4\">" + start.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + "</th></tr><tr><td>" + subject + "</td><td>Синтетический П. П.</td><td>" + kind + "</td><td>Корпус: ФИТ | Ауд.: 309</td></tr>";
        }
        private async Task LoadExamPageAsync(string path, string title, string rows, string mark = null)
        {
            main.LoadHtml(ExamPageFixture(title, rows, mark), SitePolicy.Origin + path);
            for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title+'|'+typeof (window.__univerOverviewV1&&window.__univerOverviewV1.exams)") != title + "|function";) await Task.Delay(100);
            await ReadExamsAsync(main.Address);
        }
        // Exam and midterm reminders: the app reads the shown exam or midterm page (consultations and past exams left out),
        // keeps one list per kind for this login, says each reminder once (the evening before from 19:00, and the lead before
        // the start) even across a restart, and names nothing behind the PIN. Fixture pages and an isolated hidden tray icon.
        private async Task ExamsQaAsync(List<string> results)
        {
            var today = DateTime.Today; var day = today.AddDays(30);
            var file = Path.Combine(storage.Root, ExamFile.Name);
            var previousTray = trayIcon; var previousOpens = balloonOpens; var previousLead = prefs.ReminderLead; var previousReminders = prefs.Reminders;
            var markBefore = await KeepMarkForQaAsync();
            examAccountForQa = ScheduleWeek.AccountMark("qa-exams"); gateLoginForQa = "qa-exams";
            try
            {
                await ClearExamsAsync();
                var rows = ExamRowsFixture(day.AddHours(13), "QA Алгебра", "Экзамен") + ExamRowsFixture(day.AddHours(15), "QA Алгебра", "Консультация") + ExamRowsFixture(today.AddDays(-3).AddHours(9), "QA Прошедший", "Экзамен");
                // A page of another account (the student who signed in by hand after the site's own logout): nothing is kept.
                await LoadExamPageAsync("/student/myexam/schedule/", "exams-other-qa", rows, PageAccount.Mark("qa-other"));
                Check(examPlan == null && !File.Exists(file), "an exam page of another account than the saved login is not kept", results);
                // Its own mark is accepted (the mark stays in the site's storage for the pages after it).
                await LoadExamPageAsync("/student/myexam/schedule/", "exams-qa", rows, PageAccount.Mark("qa-exams"));
                var saved = storage.LoadExams(examAccountForQa);
                Check(examPlan != null && examPlan.Items.Count == 1 && examPlan.Items[0].Subject == "QA Алгебра" && examPlan.Items[0].Kind == ExamKind.Exam && examPlan.Items[0].Start == day.AddHours(13) &&
                    examPlan.Items[0].Place == "ФИТ · 309" && saved != null && saved.SameAs(examPlan), "the app reads the coming exams from the exam page, without a consultation or a past exam, and keeps them for this login", results);
                Check(File.Exists(file) && !File.ReadAllText(file).Contains("<") && storage.LoadExams(ScheduleWeek.AccountMark("qa-other")) == null, "the kept list holds parsed text only and no other login gets it", results);
                await LoadExamPageAsync("/student/mymidterm/schedule/", "midterms-qa", ExamRowsFixture(day.AddDays(2).AddHours(9), "QA Физика", "Рубежный контроль"));
                Check(examPlan != null && examPlan.Items.Select(i => i.Kind).SequenceEqual(new[] { ExamKind.Exam, ExamKind.Midterm }), "a midterm page adds its own kind and leaves the exams", results);
                await LoadExamPageAsync("/student/myexam/schedule/", "exams-empty-qa", "<tr><td colspan=\"4\">Расписание отсутствует</td></tr>");
                Check(examPlan != null && examPlan.Items.Count == 1 && examPlan.Items[0].Kind == ExamKind.Midterm, "an exam page with no schedule clears only the exams", results);
                // A list that is not on the page yet: three asks after ten seconds, then the minute (ReadRetry).
                main.LoadHtml(ExamPageFixture("exams-wait-qa", "").Replace("<table id=\"scheduleList\"></table>", ""), SitePolicy.Origin + "/student/myexam/schedule/");
                for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title+'|'+typeof (window.__univerOverviewV1&&window.__univerOverviewV1.exams)") != "exams-wait-qa|function";) await Task.Delay(100);
                examRetry.Reset(); var waits = new List<long>();
                for (var i = 0; i < 4; i++) { await ReadExamsAsync(main.Address); waits.Add(examClock.ElapsedMilliseconds - examReadAt); }
                QaTrace("exams-retry " + string.Join(",", waits));
                Check(waits[0] >= 49000 && waits[1] >= 49000 && waits[2] >= 49000 && waits[3] < 5000 && examRetry.Used == ReadRetry.ShortTries, "a list that is not on the page yet is asked again after ten seconds, three times, then every minute", results);
                // The same address loaded again (F5): the reader starts over, at once and with its short retries.
                var sameAddress = main.Address; examReadAddress = sameAddress;
                main.LoadHtml(ExamPageFixture("exams-wait-qa2", "").Replace("<table id=\"scheduleList\"></table>", ""), sameAddress);
                var startedOver = await UntilForQa(() => examReadAddress == null, 8000);
                for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title") != "exams-wait-qa2";) await Task.Delay(100);
                ExamContent(); await UntilForQa(() => examRetry.Used == 1 && !examBusy, 5000);
                QaTrace("exams-f5 " + startedOver + " " + (examReadAddress == sameAddress) + " used=" + examRetry.Used + " wait=" + (examClock.ElapsedMilliseconds - examReadAt));
                Check(startedOver && examReadAddress == sameAddress && examRetry.Used == 1 && examClock.ElapsedMilliseconds - examReadAt >= 49000, "the same page loaded again is read at once and gets its short retries back", results);
                ExamKind kind; List<ExamItem> items;
                Check(!ExamPlan.Parse("{\"v\":2,\"kind\":\"exam\",\"items\":[]}", DateTime.Now, out kind, out items) && !ExamPlan.Parse("{\"v\":1,\"kind\":\"consult\",\"items\":[]}", DateTime.Now, out kind, out items) &&
                    !ExamPlan.Parse("{\"v\":1,\"kind\":\"exam\",\"items\":[{\"kind\":\"exam\",\"subject\":\"\",\"start\":\"2030-01-01T10:00\"}]}", DateTime.Now, out kind, out items), "an answer out of shape is refused", results);

                // The reminders, on synthetic plans and an isolated tray icon.
                var lang = prefs.Language; var account = examAccountForQa;
                var exam = new ExamItem { Kind = ExamKind.Exam, Subject = "QA Секретный предмет", Start = today.AddDays(11).AddHours(13), Place = "777" };
                var tomorrow = today.AddDays(10);
                using (var isolatedTray = new NotifyIcon { Visible = false })
                {
                    trayIcon = isolatedTray; prefs.Reminders = true; prefs.ReminderLead = 15; examHiddenForQa = false;
                    examPlan = ExamPlan.Empty(account, DateTime.Now).Replace(ExamKind.Exam, new[] { exam }, DateTime.Now); balloonOpens = null; examTitleShown = examBodyShown = null;
                    Check(RemindExams(tomorrow.AddHours(18).AddMinutes(59)).Count == 0 && balloonOpens == null, "no reminder before 19:00 the evening before", results);
                    var evening = RemindExams(tomorrow.AddHours(19));
                    Check(evening.Count == 1 && evening[0].Evening && balloonOpens == "exams" && examTitleShown == string.Format(Strings.Get("examTomorrow", lang), Strings.Get("examKindExam", lang)) &&
                        examBodyShown.Contains("QA Секретный предмет") && examBodyShown.Contains("777") && examBodyShown.Contains("13:00"), "at 19:00 the evening before the exam is announced with its subject, room and time, and a click opens the exam page", results);
                    balloonOpens = null;
                    Check(RemindExams(tomorrow.AddHours(19).AddMinutes(1)).Count == 0 && balloonOpens == null, "an announced reminder is not announced again", results);
                    await examSaving;
                    examPlan = storage.LoadExams(account);
                    Check(examPlan != null && examPlan.Announced.Count == 1 && RemindExams(tomorrow.AddHours(19).AddMinutes(30)).Count == 0, "after a restart the evening reminder that was said is not said again", results);
                    Check(RemindExams(exam.Start.AddMinutes(-16)).Count == 0, "nothing between the evening reminder and the lead", results);
                    var soon = RemindExams(exam.Start.AddMinutes(-15));
                    Check(soon.Count == 1 && !soon[0].Evening && examTitleShown == string.Format(Strings.Get("examIn", lang), 15, Strings.Get("examKindExam", lang)) && balloonOpens == "exams", "the lead before the start announces the exam once more", results);
                    Check(RemindExams(exam.Start.AddMinutes(-14)).Count == 0 && RemindExams(exam.Start.AddMinutes(1)).Count == 0, "no repeat, and nothing once the exam has begun", results);
                    // Behind the PIN no subject or room, but the kind and the time.
                    examHiddenForQa = true; balloonOpens = null;
                    examPlan = ExamPlan.Empty(account, DateTime.Now).Replace(ExamKind.Midterm, new[] { new ExamItem { Kind = ExamKind.Midterm, Subject = exam.Subject, Start = exam.Start, Place = "777" } }, DateTime.Now);
                    var locked = RemindExams(exam.Start.AddMinutes(-10));
                    Check(locked.Count == 1 && examTitleShown == string.Format(Strings.Get("examSoon", lang), Strings.Get("examKindMidterm", lang)) && examBodyShown.Contains("13:00") &&
                        !examBodyShown.Contains("Секретный") && !examBodyShown.Contains("777") && balloonOpens == "midterms", "behind the PIN the balloon names no subject or room", results);
                    examHiddenForQa = false; prefs.Reminders = false;
                    examPlan = ExamPlan.Empty(account, DateTime.Now).Replace(ExamKind.Exam, new[] { exam }, DateTime.Now);
                    Check(RemindExams(exam.Start.AddMinutes(-10)).Count == 0, "turned off, no exam reminder is shown", results);
                }
            }
            finally
            {
                trayIcon = previousTray; balloonOpens = previousOpens; prefs.ReminderLead = previousLead; prefs.Reminders = previousReminders; examHiddenForQa = null; examAccountForQa = null; gateLoginForQa = null;
                await RestoreMarkForQaAsync(markBefore);
                await ClearExamsAsync();
            }
            Check(!File.Exists(file) && examPlan == null, "clearing the session's copy removes the exam file", results);
        }
    }
}
