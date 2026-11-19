using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // QA step "kept": Бакалавр's panels are drawn from the last visit as soon as the page is shown, while the site's pages
    // are still silent; when they cannot be read at all the kept grades stay and say when they were read. Synthetic pages
    // only; the record made here is removed at the end.
    internal sealed partial class MainForm
    {
        private async Task KeptQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            var theme = prefs.Theme; var size = Size;
            prefs.Language = "ru"; prefs.Readable = true; await RefreshDocumentScriptsAsync();
            var json = new JavaScriptSerializer();
            var names = new[] { "Социология", "Психология", "Культурология" }; var marks = new[] { "87", "90", "63" };
            var journal = "<table class='wrappertable'><tr id='l4'><td class='center'><table class='mt'><tr><td class='ct'>Журнал посещений и успеваемости</td></tr>" + string.Concat(names.Select((name, i) => PolishCourse(name, marks[i], "15"))) + "</table></td></tr></table>";
            var page = PolishShell("<table class='mt' style='height:70px'><tr class='top'><td class='ct message'><ul><li>Общий GPA = 3.12</li></ul></td></tr></table>", true);
            // answers: the site's pages come at once; silent: they never come; down: they fail.
            Func<string, string> fixture = mode => "<script>localStorage.setItem('univer.app.account.v1','qa-kept');window.fetch=async function(path){" +
                (mode == "silent" ? "await new Promise(function(){});" : mode == "down" ? "throw new Error('offline');" : "") +
                "return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return " + json.Serialize(journal) + ";}}};</script>" + page;
            Func<string, Task> open = async mode =>
            {
                SetPresentation(main, true); main.LoadHtml(fixture(mode), SitePolicy.Origin + "/student/bachelor/");
                await WaitQaPresentationAsync(); await ApplyPresentationAsync();
            };
            const string grades = "Array.from(document.querySelectorAll('.univer-app-overview-grades .univer-app-overview-courses>li')).length";
            const string state = "(function(){var box=document.querySelector('.univer-app-overview-grades');if(!box)return 'none';return " + grades + "+'|'+!!box.querySelector('.univer-app-pending')+'|'+(box.querySelector('.univer-app-overview-stale')||{textContent:''}).textContent})()";
            try
            {
                Size = new Size(1360, 860);
                await AutomationController.EvaluateAsync(main, "localStorage.removeItem('univer.desktop.overview.v1');'cleared'");
                await open("answers");
                var until = DateTime.UtcNow.AddSeconds(4); var record = "";
                while (DateTime.UtcNow < until && (record = await AutomationController.EvaluateAsync(main, "(JSON.parse(localStorage.getItem('univer.desktop.overview.v1')||'{}').grades||{list:[]}).list.length+'|'+" + grades) ?? "") != "3|3") await Task.Delay(100);
                Check(record == "3|3", "a first visit reads the grades from the site and keeps them", results);
                // The site's pages never answer here: what is on screen when the page is uncovered came from the record.
                await open("silent");
                var shown = await AutomationController.EvaluateAsync(main, state);
                QaTrace("kept silent " + shown);
                Check(shown == "3|false|", "the next visit shows the kept grades as soon as the page is shown", results);
                await open("down");
                until = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < until && !(shown = await AutomationController.EvaluateAsync(main, state) ?? "").Contains("Сохранено")) await Task.Delay(100);
                QaTrace("kept down " + shown);
                Check(shown.StartsWith("3|false|Сохранено ", StringComparison.Ordinal) && shown.EndsWith(". Обновить не удалось.", StringComparison.Ordinal), "kept grades stay when the site cannot be read and say when they were read", results);
                // The note is inside its card, not clipped, and readable in both themes and in a narrow window.
                const string note = "(function(){var n=document.querySelector('.univer-app-overview-stale'),box=n.closest('.univer-app-overview'),a=n.getBoundingClientRect(),b=box.getBoundingClientRect();" +
                    "var lum=function(c){var v=c.match(/[\\d.]+/g).map(Number).slice(0,3).map(function(x){x/=255;return x<=.03928?x/12.92:Math.pow((x+.055)/1.055,2.4)});return .2126*v[0]+.7152*v[1]+.0722*v[2]};" +
                    "var back='',e=n;while(e&&!back){var c=getComputedStyle(e).backgroundColor;if(!/rgba\\(.*,\\s*0\\)|transparent/.test(c))back=c;e=e.parentElement}back=back||'rgb(255,255,255)';" +
                    "var f=lum(getComputedStyle(n).color),g=lum(back),ratio=(Math.max(f,g)+.05)/(Math.min(f,g)+.05);n.scrollIntoView({block:'center'});" +
                    "return (a.left>=b.left&&a.right<=b.right+1&&a.bottom<=b.bottom+1&&n.scrollWidth<=n.clientWidth+1)+'|'+ratio.toFixed(2)})()";
                Func<string, Task> look = async name =>
                {
                    await Task.Delay(300);
                    var seen = (await AutomationController.EvaluateAsync(main, note) ?? "").Split('|'); double ratio = 0;
                    QaTrace("kept note " + name + " " + string.Join("|", seen));
                    Check(seen.Length == 2 && seen[0] == "true" && double.TryParse(seen[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ratio) && ratio >= 4.5, "the note fits its card and keeps 4.5:1 contrast (" + name + ")", results);
                    await Task.Delay(150);
                    using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "kept-" + name + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                };
                await look("light");
                Size = MinimumSize; await look("narrow");
                Size = new Size(1360, 860);
                prefs.Theme = ThemeChoice.Dark; ApplyTheme(); await RefreshDocumentScriptsAsync();
                await open("down");
                until = DateTime.UtcNow.AddSeconds(4);
                while (DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-overview-stale')?'note':''") != "note") await Task.Delay(100);
                await look("dark");
            }
            finally
            {
                await AutomationController.EvaluateAsync(main, "localStorage.removeItem('univer.desktop.overview.v1');localStorage.removeItem('univer.app.account.v1');'cleared'");
                prefs.Theme = theme; ApplyTheme(); await RefreshDocumentScriptsAsync(); Size = size;
            }
        }
    }
}
