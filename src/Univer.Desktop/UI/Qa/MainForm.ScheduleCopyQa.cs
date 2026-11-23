using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // The schedule page of the current week as the site draws it: the week selector and the weekly table, one class a day.
        private static string ScheduleWeekFixture(DateTime monday, bool current, string mark = null)
        {
            var shown = current ? monday : monday.AddDays(-7);
            var range = shown.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + " - " + shown.AddDays(6).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            var html = new StringBuilder("<!doctype html><html><head><title>schedule</title></head><body>" + (mark == null ? "" : "<script>localStorage.setItem('" + PageAccount.Key + "','" + mark + "');</script>") + "<div id=\"l4\"><select id=\"weekSelect\"><option value=\"/student/myschedule/2026/1/12/\" selected>" + range + "</option></select>");
            html.Append("<table class=\"schedule\"><tr><th>Время</th><th>Пн</th><th>Вт</th><th>Ср</th><th>Чт</th><th>Пт</th><th>Сб</th></tr><tr><td>23:00 - 23:50</td>");
            for (var day = 0; day < 6; day++) html.Append("<td><p>QA Политология " + (day + 1) + "(Лекция)</p><p>Синтетический П. П.</p><p>ФФ / Ауд.: 30" + day + "</p></td>");
            return html.Append("</tr></table></div></body></html>").ToString();
        }
        // The app's own copy of the week: read by the app from the shown schedule page, checked, saved for this login only,
        // left alone by another week, and removed with the session (the logout step checks that part).
        private async Task ScheduleCopyQaAsync(List<string> results)
        {
            var today = DateTime.Today; var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var file = Path.Combine(storage.Root, ScheduleFile.Name);
            await ClearScheduleAsync();
            var address = SitePolicy.Origin + "/student/myschedule/";
            var markBefore = await KeepMarkForQaAsync();
            gateLoginForQa = "qa-schedule";
            try
            {
                // A page of another account (the student who signed in by hand after the site's own logout): nothing is kept.
                main.LoadHtml(ScheduleWeekFixture(monday, true, PageAccount.Mark("qa-other")), address);
                for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title+'|'+typeof (window.__univerOverviewV1&&window.__univerOverviewV1.week)") != "schedule|function";) await Task.Delay(100);
                await ReadScheduleAsync(main.Address);
                Check(scheduleWeek == null && !File.Exists(file), "a schedule page of another account than the saved login is not kept", results);
                main.LoadHtml(ScheduleWeekFixture(monday, true, PageAccount.Mark("qa-schedule")), address);
                // The page shown before has the same module and title, so the wait is for this page's own mark too.
                for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title+'|'+typeof (window.__univerOverviewV1&&window.__univerOverviewV1.week)+'|'+localStorage.getItem('" + PageAccount.Key + "')") != "schedule|function|" + PageAccount.Mark("qa-schedule");) await Task.Delay(100);
                QaTrace("schedule-copy-page " + main.Address + " " + (document != null) + " " + rotating);
                await ReadScheduleAsync(main.Address);
                var saved = storage.LoadSchedule(ScheduleAccount);
                var classes = scheduleWeek?.Classes ?? new List<ScheduleClass>();
                Check(scheduleWeek != null && classes.Count == 6 && classes[0].Start == monday.AddHours(23) && classes[0].Name == "QA Политология 1" && classes[0].Type == "Лекция" && classes[0].Room == "300" &&
                    classes[0].Teacher == "Синтетический П. П." && saved != null && saved.SameAs(scheduleWeek),
                    "the app reads this week's classes from the schedule page and keeps them for this login", results);
                Check(File.Exists(file) && !File.ReadAllText(file).Contains("<"), "the kept copy holds parsed text, not the page", results);
                var before = File.GetLastWriteTimeUtc(file);
                main.LoadHtml(ScheduleWeekFixture(monday, false), address);
                for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title+'|'+(window.__univerOverviewV1&&window.__univerOverviewV1.week())") != "schedule|";) await Task.Delay(100);
                await ReadScheduleAsync(main.Address);
                Check(scheduleWeek != null && scheduleWeek.Classes.Count == 6 && File.GetLastWriteTimeUtc(file) == before, "another week shown on the page leaves the copy as it was", results);
                Check(storage.LoadSchedule(ScheduleWeek.AccountMark("qa-other")) == null, "another login never gets this copy", results);
                await ClearScheduleAsync();
                Check(!File.Exists(file) && scheduleWeek == null, "clearing the session's copy removes the file", results);
            }
            finally
            {
                gateLoginForQa = null;
                await RestoreMarkForQaAsync(markBefore);
            }
        }
    }
}
