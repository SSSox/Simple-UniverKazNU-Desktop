using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // «Наверх» in real Chromium on a long synthetic news page: hidden at the top, shown well down without covering any
    // control at several depths and in a narrow window (also on long УМКД and attendance pages), a real mouse click that
    // returns to the top and moves the focus there, the dark theme, and Original without it.
    internal sealed partial class MainForm
    {
        // What the button covers: every visible control under one of its sample points, the button itself left out, and the
        // scroll bar of every box that scrolls sideways whose bar meets the button (found from the boxes, not the points).
        private const string TopQaState = "(function(){var h=document.getElementById('univer-app-top'),b=h&&h.shadowRoot.querySelector('button');" +
            "if(!b)return JSON.stringify({host:!!h});var r=b.getBoundingClientRect(),hit=[];" +
            "for(var y=r.top+2;y<r.bottom;y+=6)for(var x=r.left+2;x<r.right;x+=8){var u=document.elementsFromPoint(x,y).filter(function(e){return e!==h;})[0];" +
            "var c=u&&u.closest('a[href],button,input,select,textarea,summary,label,[role=button],[onclick]');if(c){var q=c.getBoundingClientRect();if(q.width*q.height<innerWidth*innerHeight/4&&hit.indexOf(c)<0)hit.push(c);}}" +
            "Array.prototype.forEach.call(document.querySelectorAll('body *'),function(e){if(e===h||!e.clientHeight||e.scrollWidth<=e.clientWidth)return;" +
            "if(!/auto|scroll/.test(getComputedStyle(e).overflowX))return;var q=e.getBoundingClientRect(),t=q.top+e.clientTop+e.clientHeight;" +
            "if(q.bottom-t>1&&t<r.bottom&&q.bottom>r.top&&q.left<r.right&&q.right>r.left)hit.push(e);});" +
            "var t=document.elementsFromPoint((r.left+r.right)/2,(r.top+r.bottom)/2)[0];" +
            "return JSON.stringify({host:true,on:b.classList.contains('on'),y:Math.round(scrollY),w:innerWidth,vh:innerHeight,left:Math.round(r.left),right:Math.round(innerWidth-r.right),bottom:Math.round(innerHeight-r.bottom),width:Math.round(r.width),height:Math.round(r.height),text:b.textContent,covers:hit.length,front:t===h});})()";
        private async Task<string> TopQaAtAsync(double screens)
        {
            await FixesEvalAsync("window.scrollTo({top:Math.round(innerHeight*" + screens.ToString(System.Globalization.CultureInfo.InvariantCulture) + "),behavior:'instant'});'ok'");
            await Task.Delay(450);
            return await FixesEvalAsync(TopQaState);
        }
        private static bool TopQaShown(string state) { return state.Contains("\"on\":true") && state.Contains("\"covers\":0") && state.Contains("\"front\":true"); }
        // A long course in УМКД: four headed sections of file rows, some with a teacher's link, each with its rating stars.
        private static string TopQaCourse()
        {
            var rows = new StringBuilder();
            for (var i = 0; i < 36; i++)
            {
                if (i % 9 == 0) rows.Append("<tr><th colspan='7'>Раздел " + (i / 9 + 1) + "</th></tr>");
                var id = 9100 + i;
                rows.Append("<tr class='file' id='" + id + "'><td><img src='/img/file.png'></td><td><a class='downLoad' id='" + id + "' href='/student/umkd/get/" + id + "/321/2026/1'>Лекция " + (i + 1) + ".pdf</a></td>" +
                    "<td class='description'>" + (i % 4 == 1 ? "<a href='https://example.org/video/" + i + "'>Видеозапись лекции</a>" : "") + "</td><td>1</td><td class='description'>34,68 Кб</td>" +
                    "<td class='description'>21.09.2026</td><td class='description'><div class='stars'></div></td></tr>");
            }
            return StudyPage(Panel(
                Row("<td class='ct'><a href='/student/umkd/0/2026/1'>УМКД</a> \\ <a href='/student/umkd/321/2026/1'>Социология</a></td>", "top") +
                Row("<td class='tt'><div class='inner'><table class='inner'><tr><th width='3%'></th><th>Название файла</th><th>Описание</th><th>Язык. гр.</th><th>Размер</th><th>Дата загрузки</th><th>Оцените качество материала</th></tr>" +
                    rows + "</table></div></td>")));
        }
        // The attendance journal of a full term: twelve courses, each with its lecture, seminar and СРО matrices.
        private static string TopQaAttendance()
        {
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            var courses = "<div id='l4'><table class='inner'><tr class='link' id='321'><td>1</td><td>Курс 1</td><td>Обязательная</td></tr></table></div>";
            var syllabi = "<div id='l4'><a href='/student/interactive/syllabus/print/22/2'>Силлабус QA</a></div>";
            var body = new StringBuilder();
            for (var i = 0; i < 12; i++)
                body.Append("<tr><td class='ct'>Курс " + (i + 1) + " ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>" +
                    AttendanceQaLesson("top" + i + "l", "лекция", "10") + AttendanceQaLesson("top" + i + "s", "семинар", "26") + AttendanceQaLesson("top" + i + "r", "СРО/СРОП", "15") +
                    "<tr><td class='ct'><b>РК1 (100): </b>51 <b>РК2 (100): </b>0</td></tr>");
            return "<script>window.qaAttendanceClicks=0;window.fetch=async function(path){return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return path.includes('/0/')?" +
                json.Serialize(courses) + ":" + json.Serialize(syllabi) + ";}}};sessionStorage.removeItem('univer.desktop.attendance-kind.v1');</script>" +
                "<table class='wrappertable'><tr id='l4'><td class='center'><table class='mt'><tr><td class='ct'>Журнал посещений и успеваемости</td></tr>" + body + "</table></td></tr></table>";
        }
        // Real page shapes at the current width: a long УМКД course (file rows, teachers' links, rating stars) and the
        // attendance journal (merged matrices with their own horizontal scroll). Where every row is a control (УМКД file
        // rows are links stretched over the whole row) the button may stay hidden mid-list rather than cover one, but it
        // never covers a control and it is there at the end of the page.
        private async Task TopQaLongPagesAsync(string qa, List<string> results, string width)
        {
            foreach (var fixture in new[] { Tuple.Create("umkd", TopQaCourse(), SitePolicy.Origin + "/student/umkd/321/2026/1"),
                Tuple.Create("attendance", TopQaAttendance(), SitePolicy.Origin + "/student/attendance/full/2026/1") })
            {
                main.LoadHtml(fixture.Item2, fixture.Item3); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(700);
                await FixesEvalAsync("window.__univerMotion=false;'still'");
                var shown = 0; var middle = 0; var bad = new List<string>(); var end = "";
                foreach (var depth in new[] { 1.5, 2.5, 3.5, 99 })
                {
                    var state = await TopQaAtAsync(depth); QaTrace("top " + width + " " + fixture.Item1 + " " + depth + " " + state);
                    if (state.Contains("\"on\":true")) { shown++; if (depth < 99) middle++; if (!TopQaShown(state)) bad.Add(depth + ": " + state); }
                    end = state;
                }
                // УМКД rows are links stretched over the row, so there the button may wait for the page end; the journal's
                // rows are not, so the button must show mid-page too.
                var enough = fixture.Item1 != "attendance" || middle >= 2;
                Check(bad.Count == 0 && TopQaShown(end) && enough, "on a long " + fixture.Item1 + " page in a " + width + " window the button never covers a control or a scroll bar and is there at the end" +
                    (fixture.Item1 == "attendance" ? ", and mid-page" : "") + " (" + shown + " of 4 depths)", results);
                await FixesShotAsync(qa, "top-" + fixture.Item1 + "-" + width + ".png");
            }
        }
        private async Task TopQaAsync(string qa, List<string> results)
        {
            var rows = new StringBuilder();
            for (var i = 0; i < 48; i++)
                rows.Append(NewsQaRow((28 - i % 28).ToString("00"), "<strong>Объявление " + (i + 1) + "</strong>", "<a href='/news/view/" + (1000 + i) + "/'>Подробнее о новости " + (i + 1) + "</a>"));
            var news = "<table class='inner'><tr><th>Дата</th><th>Новость</th></tr>" + rows + "</table><table><tr><td class='tb'>Страница 1</td></tr></table>";
            var page = PolishShell(news, false);
            prefs.Readable = true; prefs.Language = "ru";
            await RefreshDocumentScriptsAsync();
            SetPresentation(main, true); main.LoadHtml(page, SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
            await FixesEvalAsync("window.__univerMotion=false;'still'");

            var start = await TopQaAtAsync(0);
            Check(start.Contains("\"host\":true") && start.Contains("\"on\":false"), "at the top of the page there is no Back to top button", results);
            var depths = new[] { 1.6, 2.5, 3.3, 4.1, 5.2 }; var shown = 0; var bad = new List<string>();
            foreach (var depth in depths)
            {
                var state = await TopQaAtAsync(depth); QaTrace("top " + depth + " " + state);
                if (state.Contains("\"on\":true")) { shown++; if (!TopQaShown(state)) bad.Add(depth + ": " + state); }
            }
            Check(shown >= 3 && bad.Count == 0, "well down the page the button shows and never covers a link or button (" + shown + " of " + depths.Length + " depths)", results);
            var deep = await TopQaAtAsync(2.5);
            Check(deep.Contains("\"text\":\"Наверх\"") && deep.Contains("\"height\":40"), "the button reads «Наверх» and is 40 px tall like the page's controls", results);
            await FixesShotAsync(qa, "top-ru.png");
            // A link in the bottom right corner: the button climbs above it instead of covering it.
            await FixesEvalAsync("(function(){var a=document.createElement('a');a.id='qa-corner';a.href='/news/view/1/';a.textContent='Ссылка в углу';" +
                "a.style.cssText='position:fixed;right:12px;bottom:12px;width:260px;height:64px;display:block;background:#fde7e7;z-index:5';document.body.appendChild(a);window.__univerTopV1.refresh();return 'ok';})()");
            await Task.Delay(300);
            var dodge = await FixesEvalAsync(TopQaState); QaTrace("top corner " + dodge);
            int lifted; var mark = dodge.IndexOf("\"bottom\":", StringComparison.Ordinal);
            Check(TopQaShown(dodge) && mark > 0 && int.TryParse(dodge.Substring(mark + 9).Split(',')[0], out lifted) && lifted >= 76 + 6, "a link in the corner moves the button above it", results);
            await FixesShotAsync(qa, "top-corner.png");
            await FixesEvalAsync("document.getElementById('qa-corner').remove();window.__univerTopV1.refresh();'ok'");

            // A real mouse click on the button: back at the top, the button gone and the focus at the top of the page.
            // The button slides back down once the corner link is gone: it is measured only after it has stopped.
            const string centre = "(function(){var b=document.getElementById('univer-app-top').shadowRoot.querySelector('button').getBoundingClientRect();return Math.round((b.left+b.right)/2)+','+Math.round((b.top+b.bottom)/2);})()";
            string placed = null;
            for (var look = 0; look < 20; look++) { await Task.Delay(100); var now = await FixesEvalAsync(centre); if (now.Length == 0) continue; if (now == placed) break; placed = now; }
            var point = (placed ?? "").Split(',');
            int px, py; var after = "";
            if (point.Length == 2 && int.TryParse(point[0], out px) && int.TryParse(point[1], out py))
            {
                // Under load a click can arrive before the button takes hits, or be lost: wait until the point hits the button,
                // then poll for the outcome, and click once more only while the page is still scrolled and the button still shown.
                // The shadow host spans more than the button, so the hit is asked of the shadow root.
                var hit = "(function(){var h=document.getElementById('univer-app-top'),b=h&&h.shadowRoot&&h.shadowRoot.querySelector('button'),e=b&&h.shadowRoot.elementFromPoint(" + px + "," + py + ");return e&&(e===b||b.contains(e))?'hit':'';})()";
                var ready = DateTime.UtcNow.AddSeconds(3);
                while (await FixesEvalAsync(hit) != "hit" && DateTime.UtcNow < ready) await Task.Delay(100);
                var host = main.GetBrowser().GetHost();
                for (var attempt = 0; attempt < 2 && after != "0|false|header"; attempt++)
                {
                    var seen = after.Split('|');
                    if (attempt > 0 && !(seen.Length == 3 && seen[0] != "0" && seen[1] == "true")) break;
                    host.SetFocus(true);
                    host.SendMouseMoveEvent(new MouseEvent(px, py, CefEventFlags.None), false);
                    host.SendMouseClickEvent(new MouseEvent(px, py, CefEventFlags.LeftMouseButton), MouseButtonType.Left, false, 1);
                    host.SendMouseClickEvent(new MouseEvent(px, py, CefEventFlags.None), MouseButtonType.Left, true, 1);
                    var until = DateTime.UtcNow.AddSeconds(4);
                    do { await Task.Delay(150); after = await FixesEvalAsync("(function(){var b=document.getElementById('univer-app-top').shadowRoot.querySelector('button'),a=document.activeElement;return Math.round(scrollY)+'|'+b.classList.contains('on')+'|'+(a&&a.closest('.univer-app-header')?'header':a?a.tagName:'none');})()"); } while (after != "0|false|header" && DateTime.UtcNow < until);
                }
            }
            QaTrace("top click " + after);
            Check(after == "0|false|header", "a click returns to the top, hides the button and puts the focus at the top of the page", results);

            // Narrow window: the page reflows to cards and the button still finds a free spot.
            var size = Size; var state0 = WindowState;
            try
            {
                WindowState = System.Windows.Forms.FormWindowState.Normal; Size = new Size(Math.Max(MinimumSize.Width, 760), 820); await Task.Delay(900);
                var narrowShown = 0; bad.Clear();
                foreach (var depth in new[] { 1.8, 2.9, 4.0 })
                {
                    var state = await TopQaAtAsync(depth); QaTrace("top narrow " + depth + " " + state);
                    if (state.Contains("\"on\":true")) { narrowShown++; if (!TopQaShown(state)) bad.Add(depth + ": " + state); }
                }
                Check(narrowShown >= 2 && bad.Count == 0, "in a narrow window the button still shows and covers nothing (" + narrowShown + " of 3)", results);
                await FixesShotAsync(qa, "top-narrow.png");
                await TopQaLongPagesAsync(qa, results, "narrow");
            }
            finally { Size = size; WindowState = state0; await Task.Delay(600); }
            await TopQaLongPagesAsync(qa, results, "wide");
            main.LoadHtml(page, SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
            await FixesEvalAsync("window.__univerMotion=false;'still'");

            prefs.Language = "kz"; await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(300);
            Check((await TopQaAtAsync(2.5)).Contains("\"text\":\"Жоғарыға\""), "the label follows the app language", results);
            prefs.Language = "ru"; await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync();

            prefs.Theme = ThemeChoice.Dark; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(700);
            Check(TopQaShown(await TopQaAtAsync(2.5)), "the button shows in the dark theme", results);
            await FixesShotAsync(qa, "top-dark.png");
            prefs.Theme = ThemeChoice.Light; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(300);

            prefs.Readable = false; await ApplyPresentationAsync(); await Task.Delay(400);
            Check(await FixesEvalAsync("document.getElementById('univer-app-top')?'there':'gone'") == "gone", "Original has no Back to top button", results);
            prefs.Readable = true; await ApplyPresentationAsync();
        }
    }
}
