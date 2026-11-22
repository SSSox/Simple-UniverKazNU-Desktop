using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Course totals around today, read yesterday afternoon. The offline step and Program (--qa-offline) use them.
        internal static LastGrades OfflineGradesForQa(DateTime now, string account)
        {
            Func<string, string, string, LastGradeScore> score = (label, max, value) => new LastGradeScore { Label = label, Max = max, Value = value };
            var courses = new List<LastGradeCourse>
            {
                new LastGradeCourse { Name = "QA Алгебра", Scores = new List<LastGradeScore> { score("РК1", "100", "71"), score("РК2", "100", "88.5") } },
                new LastGradeCourse { Name = "QA История политических учений Казахстана и мировая политическая мысль", Scores = new List<LastGradeScore> { score("РК1", "100", "49.95"), score("Экзамен", "40", "31") } },
                new LastGradeCourse { Name = "QA Физика", Scores = new List<LastGradeScore> { score("Итог", "", "93") } }
            };
            return new LastGrades(account, now.Date.AddDays(-1).AddHours(15).AddMinutes(20), courses);
        }
        // «Последние оценки»: the app asks the shown home page for the grades its panel last read from the journal (the page's
        // kept record), checks them, keeps them for this login, and a page that gives nothing leaves the copy alone. A fixture
        // page with its own record; no network.
        private async Task LastGradesQaAsync(List<string> results)
        {
            var file = Path.Combine(storage.Root, LastGradesFile.Name);
            lastGradesAccountForQa = ScheduleWeek.AccountMark("qa-last-grades");
            try
            {
                await ClearLastGradesAsync();
                var record = "{owner:'qa-last',language:'ru',grades:{at:Date.now()-3600000,list:[" +
                    "{name:'QA Алгебра',token:'qa алгебра',scores:[{label:'РК1',max:'100',value:'71'},{label:'РК2',max:'100',value:''}]}," +
                    "{name:'QA Физика',token:'qa физика',scores:[]}]}}";
                var page = "<!doctype html><html><head><title>last-grades-qa</title></head><body><div id=\"l4\">QA</div><script>localStorage.setItem('univer.app.account.v1','qa-last');" +
                    "localStorage.setItem('univer.desktop.overview.v1',JSON.stringify(" + record + "));window.fetch=function(){return Promise.reject(new Error('qa'));};</script></body></html>";
                main.LoadHtml(page, SitePolicy.Origin + "/student/bachelor/");
                for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title+'|'+typeof (window.__univerOverviewV1&&window.__univerOverviewV1.grades)") != "last-grades-qa|function";) await Task.Delay(100);
                await ReadLastGradesAsync(main.Address);
                var saved = storage.LoadLastGrades(lastGradesAccountForQa);
                Check(lastGrades != null && lastGrades.Courses.Count == 1 && lastGrades.Courses[0].Name == "QA Алгебра" && lastGrades.Courses[0].Scores.Count == 1 && LastGrades.Score(lastGrades.Courses[0].Scores[0]) == "71/100" &&
                    saved != null && saved.SameAs(lastGrades), "the app reads the home page's last grades (only the scores that have a value) and keeps them for this login", results);
                Check(File.Exists(file) && !File.ReadAllText(file).Contains("<") && storage.LoadLastGrades(ScheduleWeek.AccountMark("qa-other")) == null, "the kept totals hold parsed text only and no other login gets them", results);
                var kept = lastGrades;
                await AutomationController.EvaluateAsync(main, "localStorage.removeItem('univer.desktop.overview.v1');'cleared'");
                await ReadLastGradesAsync(main.Address);
                Check(lastGrades == kept && File.Exists(file), "a page with nothing to give leaves the saved totals alone", results);
                Check(LastGrades.Parse("{\"v\":2,\"at\":" + (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds + ",\"courses\":[]}", DateTime.Now, lastGradesAccountForQa) == null, "an answer out of shape is refused", results);
                await ClearLastGradesAsync();
                Check(!File.Exists(file) && lastGrades == null, "clearing the session's totals removes the file", results);
            }
            finally
            {
                await AutomationController.EvaluateAsync(main, "localStorage.removeItem('univer.desktop.overview.v1');localStorage.removeItem('univer.app.account.v1');'cleared'");
                lastGradesAccountForQa = null; lastGradesBusy = false; await ClearLastGradesAsync();
            }
        }
    }
}
