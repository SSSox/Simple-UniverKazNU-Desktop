using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // «Наверх» in real Chromium on a long synthetic news page: hidden at the top, shown well down in one fixed place
    // without covering a small control at several depths and in a narrow window (also on long УМКД, attendance and
    // service pages, where the page makes room), a real mouse click that returns to the top and moves the focus there,
    // the dark theme, and Original without it.
    internal sealed partial class MainForm
    {
        // What the button covers: every visible control under one of its sample points, the button itself left out. As in
        // back-to-top.js, a control wider than 480 px (a row that is one big link) or reaching past the window (a cell of a
        // box that scrolls sideways) stays clickable beside the button and does not count.
        private const string TopQaState = "(function(){var h=document.getElementById('univer-app-top'),b=h&&h.shadowRoot.querySelector('button');" +
            "if(!b)return JSON.stringify({host:!!h});var r=b.getBoundingClientRect(),hit=[],v=window.visualViewport,W=v?v.width:innerWidth,H=v?v.height:innerHeight;" +
            "for(var y=r.top+2;y<r.bottom;y+=6)for(var x=r.left+2;x<r.right;x+=8){var u=document.elementsFromPoint(x,y).filter(function(e){return e!==h;})[0];" +
            "var c=u&&u.closest('a[href],button,input,select,textarea,summary,label,[role=button],[onclick]');if(c){var q=c.getBoundingClientRect();if(q.width<=480&&q.right<=W+1&&hit.indexOf(c)<0)hit.push(c);}}" +
            "var t=document.elementsFromPoint((r.left+r.right)/2,(r.top+r.bottom)/2)[0];" +
            "return JSON.stringify({host:true,on:b.classList.contains('on'),y:Math.round(scrollY),w:innerWidth,vw:Math.round(W),vh:innerHeight,room:document.documentElement.getAttribute('data-univer-top-room')||'',left:Math.round(r.left),right:Math.round(W-r.right),bottom:Math.round(H-r.bottom),width:Math.round(r.width),height:Math.round(r.height),text:b.textContent,covers:hit.length,front:t===h});})()";
        private async Task<string> TopQaAtAsync(double screens)
        {
            await FixesEvalAsync("window.scrollTo({top:Math.round(innerHeight*" + screens.ToString(System.Globalization.CultureInfo.InvariantCulture) + "),behavior:'instant'});'ok'");
            await Task.Delay(450);
            return await FixesEvalAsync(TopQaState);
        }
        // Shown, in front, over no small control, and in its one place: 24 px from the right, 56 px above the bottom.
        private static bool TopQaShown(string state) { return state.Contains("\"on\":true") && state.Contains("\"covers\":0") && state.Contains("\"front\":true") && state.Contains("\"right\":24,\"bottom\":56,"); }
        // Whether the page beside the button can spare its room: 360 px must remain, as in back-to-top.js (160 px is the room
        // of the Russian label). Measured on the visual viewport, as the script does.
        private static bool TopQaSpare(string state) { var width = Regex.Match(state, "\"vw\":(\\d+)"); return width.Success && int.Parse(width.Groups[1].Value) - 160 >= 360; }
        // A page with a control under the button's place: room made and the button shown, or, with no room to spare, no button.
        private static bool TopQaPlaced(string end, int shown) { return TopQaSpare(end) ? !end.Contains("\"room\":\"\"") && TopQaShown(end) && shown >= 2 : end.Contains("\"room\":\"\"") && shown == 0; }
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
        // A long list of services, each row closed by «Подать заявку» at its right end, where the button stands.
        private static string TopQaServices()
        {
            var rows = new StringBuilder();
            for (var i = 0; i < 40; i++) rows.Append("<div style='display:flex;justify-content:space-between;align-items:center;gap:16px;padding:10px 0;border-bottom:1px solid #dde3ea'><span>Услуга " + (i + 1) +
                "</span><a href='/ssc/apply/" + i + "' style='flex:none;padding:6px 12px;border:1px solid #bfd3ea;border-radius:8px'>Подать заявку</a></div>");
            return PolishShell("<div class='qa-services'>" + rows + "</div>", false);
        }
        // Real page shapes at the current width: a long УМКД course (file rows, teachers' links, rating stars), the
        // attendance journal (merged matrices with their own horizontal scroll) and a long service list. At every depth the
        // button is in its place and over no small control: where one would pass under it, the page made room.
        private async Task TopQaLongPagesAsync(string qa, List<string> results, string width)
        {
            foreach (var fixture in new[] { Tuple.Create("umkd", TopQaCourse(), SitePolicy.Origin + "/student/umkd/321/2026/1"),
                Tuple.Create("attendance", TopQaAttendance(), SitePolicy.Origin + "/student/attendance/full/2026/1"),
                Tuple.Create("services", TopQaServices(), SitePolicy.Origin + "/ssc/UserServiceList") })
            {
                main.LoadHtml(fixture.Item2, fixture.Item3); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(700);
                await FixesEvalAsync("window.__univerMotion=false;'still'");
                var shown = 0; var bad = new List<string>(); var end = "";
                foreach (var depth in new[] { 1.5, 2.5, 3.5, 99 })
                {
                    var state = await TopQaAtAsync(depth); QaTrace("top " + width + " " + fixture.Item1 + " " + depth + " " + state);
                    if (state.Contains("\"on\":true")) { shown++; if (!TopQaShown(state)) bad.Add(depth + ": " + state); }
                    end = state;
                }
                await FixesShotAsync(qa, "top-" + fixture.Item1 + "-" + width + ".png");
                // The service rows put «Подать заявку» under the button's place in any window: the page makes room, or, too narrow
                // to spare it (a large display scale), shows no button.
                var services = fixture.Item1 == "services";
                Check(bad.Count == 0 && (services ? TopQaPlaced(end, shown) : TopQaShown(end) && shown >= 2), "on a long " + fixture.Item1 + " page in a " + width +
                    " window the button keeps its place, covers no small control" + (services ? (TopQaSpare(end) ? " (the page made room)" : " (no room to spare, so no button)") : "") +
                    " and is there mid-page and at the end (" + shown + " of 4 depths)", results);
            }
        }
        // The service list zoomed in: at 150% the QA window makes room; at 200% (about 480 CSS px) it cannot spare it and shows
        // no button. Either way the button covers nothing.
        private async Task TopQaZoomedAsync(string qa, List<string> results)
        {
            var before = zoomPercent;
            try
            {
                foreach (var zoom in new[] { 150, 200 })
                {
                    zoomPercent = zoom; ApplyZoom();
                    main.LoadHtml(TopQaServices(), SitePolicy.Origin + "/ssc/UserServiceList"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(700);
                    await FixesEvalAsync("window.__univerMotion=false;'still'");
                    var shown = 0; var bad = new List<string>(); var end = "";
                    foreach (var depth in new[] { 1.5, 2.5, 3.5, 99 })
                    {
                        var state = await TopQaAtAsync(depth); QaTrace("top zoom " + zoom + " " + depth + " " + state);
                        if (state.Contains("\"on\":true")) { shown++; if (!TopQaShown(state)) bad.Add(depth + ": " + state); }
                        end = state;
                    }
                    await FixesShotAsync(qa, "top-services-" + zoom + ".png");
                    // A page narrower than the room plus 360 px (the QA window at 200%) keeps its column, and then the button stays off.
                    Check(bad.Count == 0 && TopQaPlaced(end, shown), "zoomed to " + zoom + "% the button never stands over a small control: " +
                        (TopQaSpare(end) ? "the page makes room" : "no room to spare, so no button") + " (" + shown + " of 4 depths, " + Regex.Match(end, "\"vw\":(\\d+)").Groups[1].Value + " px wide)", results);
                }
            }
            finally { zoomPercent = before; ApplyZoom(); }
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
            // News links sit on the left: the news page needs no room and keeps its full column.
            Check(deep.Contains("\"room\":\"\""), "a page whose controls stay clear of the button keeps its full width", results);

            // A real mouse click on the button: back at the top, the button gone and the focus at the top of the page.
            // The button rises 8 px as it fades in: it is measured once it has settled.
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

            // Narrow window: the page reflows to cards and the button keeps its place over no small control.
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
            await TopQaZoomedAsync(qa, results);
            main.LoadHtml(page, SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
            await FixesEvalAsync("window.__univerMotion=false;'still'");

            prefs.Language = "kz"; await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(300);
            Check((await TopQaAtAsync(2.5)).Contains("\"text\":\"Жоғарыға\""), "the label follows the app language", results);
            prefs.Language = "ru"; await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync();

            prefs.Theme = ThemeChoice.Dark; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(700);
            Check(TopQaShown(await TopQaAtAsync(2.5)), "the button shows in the dark theme", results);
            await FixesShotAsync(qa, "top-dark.png");
            prefs.Theme = ThemeChoice.Light; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(300);

            // A loaded x86 run may finish Original later than 400 ms: wait up to 3 s; a button that stays still fails.
            prefs.Readable = false; await ApplyPresentationAsync(); await Task.Delay(400);
            var original = "";
            for (var i = 0; i < 14 && original != "gone"; i++) { original = await FixesEvalAsync("document.getElementById('univer-app-top')?'there':'gone'"); if (original != "gone") await Task.Delay(200); }
            Check(original == "gone", "Original has no Back to top button", results);
            prefs.Readable = true; await ApplyPresentationAsync();
        }
    }
}
