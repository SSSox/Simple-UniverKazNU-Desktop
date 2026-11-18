using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // IGPA's period switch in real Chromium, with the site's own joined box: in the reading layout the two choices stand
    // apart on a track and the chosen one is a raised white thumb; Original keeps the site's look.
    internal sealed partial class MainForm
    {
        private const string IgpaQaPage =
            "<style>.igpa-period{display:inline-flex;border:1px solid #d0d7e2;border-radius:6px;overflow:hidden}" +
            ".igpa-period a{padding:6px 12px;color:#1f4f8f;background:#fff;text-decoration:none}.igpa-period a+a{border-left:1px solid #d0d7e2}" +
            ".igpa-period a.igpa-selected{background:#eef3fb}@media print{.igpa-screen-only{display:none}}</style>" +
            "<div class='igpa-ui'><nav class='igpa-nav' role='tablist'><a role='tab' class='igpa-tab-active' href='/student/stud/rating/anket/'>Результат</a>" +
            "<a role='tab' href='/student/stud/rating/anket/add'>Добавить достижение</a><a role='tab' href='/student/stud/rating/anket/my'>Мои заявки <span class='igpa-count'>0</span></a></nav>" +
            "<div class='igpa-head'><h2>Результат</h2><div class='igpa-period igpa-screen-only'><a href='/student/stud/rating/anket/?cumulative=0'>За учебный год</a>" +
            "<a class='igpa-selected' aria-current='page' href='/student/stud/rating/anket/?cumulative=1'>Накопительный</a></div></div>" +
            "<div class='igpa-form-actions'><button type='button' class='igpa-button-primary'>Отправить на проверку</button><button type='button'>Очистить</button></div>" +
            "<div class='igpa-filter igpa-screen-only'><label class='igpa-filter-field'>Вид деятельности<select><option>Все</option></select></label>" +
            "<label class='igpa-filter-field igpa-filter-field-category'>Категория<select><option>Все</option></select></label><button type='button'>Показать</button></div></div>";
        // The smallest gap between neighbouring controls of the form buttons and of the filter: side by side in a row, or one
        // above the other once the row wraps.
        private const string IgpaQaGaps = "(function(){function g(s){var k=document.querySelector(s).children,m=999;for(var i=1;i<k.length;i++){var a=k[i-1].getBoundingClientRect(),b=k[i].getBoundingClientRect();" +
            "m=Math.min(m,b.left>=a.right-1?b.left-a.right:b.top-a.bottom);}return Math.round(m);}return g('.igpa-form-actions')+'|'+g('.igpa-filter');})()";
        // The rows one above the other (period switch, form buttons, filter) and a filter label's text and its list.
        private const string IgpaQaRows = "(function(){function r(s){return document.querySelector(s).getBoundingClientRect();}var p=r('.igpa-period'),f=r('.igpa-form-actions'),l=r('.igpa-filter')," +
            "field=document.querySelector('.igpa-filter-field'),list=field.querySelector('select').getBoundingClientRect(),text=document.createRange();text.selectNodeContents(field.firstChild);" +
            "return [Math.round(f.top-p.bottom),Math.round(l.top-f.bottom),Math.round(list.left-text.getBoundingClientRect().right)].join('|');})()";
        // Gap between the choices, the chosen one's background, weight and colour, the other's background, the track's.
        private const string IgpaQaState = "(function(){var a=document.querySelectorAll('.igpa-period>a'),p=document.querySelector('.igpa-period'),s=getComputedStyle(a[1]),o=getComputedStyle(a[0]);" +
            "return [Math.round(a[1].getBoundingClientRect().left-a[0].getBoundingClientRect().right),s.backgroundColor,s.fontWeight,s.color===o.color?'same':'apart',o.backgroundColor,getComputedStyle(p).backgroundColor].join('|');})()";
        private async Task IgpaQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            prefs.Language = "ru"; prefs.Readable = true; await RefreshDocumentScriptsAsync();
            SetPresentation(main, true); main.LoadHtml(PolishShell(IgpaQaPage, true), SitePolicy.Origin + "/student/stud/rating/anket/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(400);
            var state = await AutomationController.EvaluateAsync(main, IgpaQaState); QaTrace("qa-igpa " + state);
            var cells = (state ?? "").Split('|'); int gap;
            Check(cells.Length == 6 && int.TryParse(cells[0], out gap) && gap >= 2 && cells[1] == "rgb(255, 255, 255)" && cells[2] == "600" && cells[3] == "apart" &&
                cells[4] == "rgba(0, 0, 0, 0)" && cells[5] == "rgb(233, 238, 244)", "IGPA's period choices stand apart and the chosen one is a raised thumb (" + state + ")", results);
            var gaps = await AutomationController.EvaluateAsync(main, IgpaQaGaps); QaTrace("qa-igpa-gaps " + gaps);
            var apart = (gaps ?? "").Split('|'); int formGap, filterGap;
            Check(apart.Length == 2 && int.TryParse(apart[0], out formGap) && int.TryParse(apart[1], out filterGap) && formGap >= 8 && filterGap >= 8,
                "IGPA's form buttons and request filter controls stand at least 8 px apart (" + gaps + ")", results);
            var rows = await AutomationController.EvaluateAsync(main, IgpaQaRows); QaTrace("qa-igpa-rows " + rows);
            var rowGaps = (rows ?? "").Split('|'); int belowPeriod, belowForm, labelGap;
            Check(rowGaps.Length == 3 && int.TryParse(rowGaps[0], out belowPeriod) && int.TryParse(rowGaps[1], out belowForm) && int.TryParse(rowGaps[2], out labelGap) &&
                belowPeriod >= 8 && belowForm >= 8 && labelGap >= 6, "IGPA's period switch, form buttons and filter rows do not touch, and a filter label stands off its list (" + rows + ")", results);
            // Printed, the site's own rule still hides its screen-only filter, and room made for «Наверх» (not printed) leaves
            // the column its full width.
            const string printState = "(function(){var c=document.querySelector('#l4 td.center');return getComputedStyle(document.querySelector('.igpa-filter')).display+'|'+(c?parseFloat(getComputedStyle(c).paddingRight):-1);})()";
            await AutomationController.EvaluateAsync(main, "document.documentElement.setAttribute('data-univer-top-room','160');'ok'");
            string onScreen, printed;
            using (var client = main.GetDevToolsClient())
            {
                onScreen = await AutomationController.EvaluateAsync(main, printState);
                await client.Emulation.SetEmulatedMediaAsync(media: "print");
                try { printed = await AutomationController.EvaluateAsync(main, printState); }
                finally { await client.Emulation.SetEmulatedMediaAsync(media: "screen"); }
            }
            await AutomationController.EvaluateAsync(main, "document.documentElement.removeAttribute('data-univer-top-room');'ok'");
            QaTrace("qa-igpa-print " + onScreen + " / " + printed);
            var screenCells = (onScreen ?? "").Split('|'); var printCells = (printed ?? "").Split('|'); double screenPad, printPad;
            Check(screenCells.Length == 2 && printCells.Length == 2 && screenCells[0] == "flex" && printCells[0] == "none" &&
                double.TryParse(screenCells[1], NumberStyles.Float, CultureInfo.InvariantCulture, out screenPad) && double.TryParse(printCells[1], NumberStyles.Float, CultureInfo.InvariantCulture, out printPad) &&
                screenPad >= 160 && printPad < 160, "printing keeps the site's print rules for IGPA and drops the room of «Наверх» (" + onScreen + " / " + printed + ")", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "igpa-period.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            prefs.Readable = false; await ApplyPresentationAsync(); await Task.Delay(300);
            var original = await AutomationController.EvaluateAsync(main, IgpaQaState); QaTrace("qa-igpa-original " + original);
            Check((original ?? "").Split('|').Length == 6 && original.Split('|')[1] == "rgb(238, 243, 251)", "Original keeps the site's own period switch", results);
            prefs.Readable = true; await ApplyPresentationAsync();
        }
    }
}
