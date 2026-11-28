using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // A course's UMKD page as captured on 6 November 2026 (breadcrumb, teacher, syllabus block, one table of headed
        // sections with file rows), with synthetic names. The teacher's links are the unobserved part: one as a link in
        // a description, one written out as text, plus addresses that must never open.
        internal static string UmkdCourseFixture()
        {
            Func<int, string, string, string> file = (id, name, description) => "<tr class='file' id='" + id + "'><td><img src='/img/file.png'></td><td><a class='downLoad' id='" + id + "' href='/student/umkd/get/" + id + "/321/2026/1'>" + name + "</a></td>" +
                "<td class='description'>" + description + "</td><td>1</td><td class='description'>34,68 Кб</td><td class='description'>21.09.2026</td><td class='description'><div class='stars'></div></td></tr>";
            return StudyPage(Panel(
                Row("<td class='ct'><a href='/student/umkd/0/2026/1'>УМКД</a> \\ <a href='/student/umkd/321/2026/1'>Социология</a></td>", "top") +
                Row("<td class='ct'>Преподаватель: Синтетический П. П.</td>", "brk") +
                Row("<td class='tt'><div class='inner'><div><h4>Силлабус</h4><a href='/student/interactive/syllabus/print/22/2'>Социология</a></div>" +
                    "<table class='inner' onselectstart='return false' onmousedown='return false'><tr><th width='3%'></th><th>Название файла</th><th>Описание</th><th>Язык. гр.</th><th>Размер</th><th>Дата загрузки</th><th>Оцените качество материала</th></tr>" +
                    "<tr><th colspan='7'>Силлабус</th></tr>" + file(9001, "Силлабус Социология.docx", "") +
                    "<tr><th colspan='7'>Конспекты лекций/тематический материал для практических занятий.</th></tr>" +
                    file(9002, "Лекция 1. Введение.pdf", "<a id='qa-outside' href='https://example.org/lecture?week=1'>Видеозапись лекции</a>") +
                    file(9003, "Лекция 2. Методы.pdf", "Источник: https://example.org/book.") +
                    file(9004, "Лекция 3. Общество.pdf", "<a id='qa-local' href='http://192.168.0.1/admin'>Адрес в локальной сети</a>") +
                    "</table></div></td>")));
        }
        private async Task<string> OutsideAnswerAsync(Func<string> opened, string key)
        {
            var until = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < until && opened() == null && messages.Current?.Key != key) await Task.Delay(50);
            return messages.Current?.Key;
        }
        // Teacher's links, the UMKD folder and the controls that follow the bottom of the window, in real Chromium.
        private async Task StudyFeaturesQaAsync(string qa, List<string> results, string heading)
        {
            string opened = null; outsideProbe = url => { opened = url; return true; };
            try
            {
                // The last visit saw every file but the third lecture: that one is marked as new, after its link.
                const string seen = "<script>localStorage.setItem('univer.app.account.v1','qa-umkd');localStorage.setItem('univer.desktop.umkd-seen.v1',JSON.stringify({owner:'qa-umkd',pages:{'/student/umkd/321/2026/1':{at:Date.now(),ids:['9001/321','9002/321','9003/321']}}}));</script>";
                main.LoadHtml(heading + seen + UmkdCourseFixture(), SitePolicy.Origin + "/student/umkd/321/2026/1"); await WaitQaPresentationAsync();
                var tagged = await AutomationController.EvaluateAsync(main, "(function(){var t=Array.from(document.querySelectorAll('#l4 .univer-app-file-new'));if(t.length!==1)return 'count '+t.length;" +
                    "var lum=function(c){var v=c.match(/[\\d.]+/g).map(Number).slice(0,3).map(function(x){x/=255;return x<=.03928?x/12.92:Math.pow((x+.055)/1.055,2.4)});return .2126*v[0]+.7152*v[1]+.0722*v[2]};" +
                    "var s=getComputedStyle(t[0]),f=lum(s.color),g=lum(s.backgroundColor),ratio=(Math.max(f,g)+.05)/(Math.min(f,g)+.05),a=t[0].previousElementSibling,r=t[0].getBoundingClientRect(),c=t[0].closest('td').getBoundingClientRect();" +
                    "t[0].scrollIntoView({block:'center'});return t[0].closest('tr').id==='9004'&&a&&a.matches('a.downLoad')&&a.textContent==='Лекция 3. Общество.pdf'&&r.height>=18&&r.height<=22&&r.right<=c.right+1&&r.left>=c.left-1&&ratio>=4.5?'tagged':JSON.stringify([t[0].closest('tr').id,r.height,r.left,c.left,r.right,c.right,ratio])})()");
                QaTrace("umkd-new " + tagged);
                await AutomationController.EvaluateAsync(main, "localStorage.removeItem('univer.desktop.umkd-seen.v1');localStorage.removeItem('univer.app.account.v1');'cleared'");
                Check(tagged == "tagged", "a UMKD file added since the last visit is marked after its name, readable and inside its cell", results);
                await Task.Delay(150);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "umkd-new.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "window.scrollTo(0,0);'top'");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var links=Array.from(document.querySelectorAll('#l4 a.univer-app-outside-link')),made=links[1];" +
                    "return links.length===3&&links[0].id==='qa-outside'&&links[0].title==='Ссылка на другой сайт: example.org'&&made.href==='https://example.org/book'&&made.textContent==='https://example.org/book'&&" +
                    "made.parentElement.textContent==='Источник: https://example.org/book.'&&getComputedStyle(links[0],'::after').width==='14px'&&!document.querySelector('#l4 a.downLoad.univer-app-outside-link')?'links':JSON.stringify(links.map(function(a){return a.href}))})()") == "links",
                    "UMKD marks a teacher's link as one to another site and turns a written address into a link", results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "umkd-links.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                // Folding a section hides its file rows on screen, not only in an attribute: the card rows' own display rule
                // must lose to it. The heading is clicked twice, so the page is left as it was.
                const string foldProbe = "(function(){var head=Array.from(document.querySelectorAll('#l4 th[data-univer-fold]')).find(function(t){return /Конспекты/.test(t.textContent)});" +
                    "if(!head)return 'no heading';head.click();var rows=Array.from(document.querySelectorAll('#l4 tr.file')).filter(function(r){return r.hasAttribute('data-univer-folded-row')});" +
                    "var hidden=rows.length===3&&rows.every(function(r){return getComputedStyle(r).display==='none'&&r.getBoundingClientRect().height===0});head.click();" +
                    "var back=Array.from(document.querySelectorAll('#l4 tr.file')).every(function(r){return getComputedStyle(r).display!=='none'});" +
                    "return hidden&&back&&head.getAttribute('aria-expanded')==='true'?'folds':rows.length+':'+rows.map(function(r){return getComputedStyle(r).display}).join(',')})()";
                var folded = await AutomationController.EvaluateAsync(main, foldProbe);
                Check(folded == "folds", "folding a UMKD section hides its file rows on screen and unfolding shows them again (" + folded + ")", results);
                // A clicked fold slides: half way the rows are part high and still on screen, just before the end they are
                // under a pixel (their !important padding and borders shrink too), and once finished they are hidden.
                // Animation time is set by hand, never sampled by the clock.
                const string slideShut = @"(function(){window.qaFoldMotion=window.__univerMotion;window.__univerMotion=true;
var head=Array.from(document.querySelectorAll('#l4 th[data-univer-fold]')).find(function(t){return /Конспекты/.test(t.textContent)});if(!head)return 'no heading';
var all=Array.from(document.querySelectorAll('#l4 tr.file')),full=all.map(function(r){return r.getBoundingClientRect().height});
head.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,detail:1,view:window}));
var rows=all.filter(function(r){return r.hasAttribute('data-univer-folded-row')}),runs=rows.map(function(r){return r.getAnimations()[0]});
if(rows.length!==3||!runs.every(Boolean)||!rows.every(function(r){return r.hasAttribute('data-univer-sliding')}))return 'no slide:'+rows.length+':'+runs.filter(Boolean).length;
var length=runs[0].effect.getComputedTiming().duration;runs.forEach(function(a){a.pause();a.currentTime=length/2});
var mid=rows.every(function(r){var h=r.getBoundingClientRect().height,f=full[all.indexOf(r)];return getComputedStyle(r).display!=='none'&&h>1&&h<f-1});
head.scrollIntoView({block:'start'});window.scrollBy(0,-80);window.qaFoldRuns=runs;return mid?'mid':'mid false'})()";
                var slid = await AutomationController.EvaluateAsync(main, slideShut);
                await Task.Delay(120);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "umkd-fold-midway.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                if (slid == "mid") slid = await AutomationController.EvaluateAsync(main, "(function(){var runs=window.qaFoldRuns,length=runs[0].effect.getComputedTiming().duration;runs.forEach(function(a){a.currentTime=length-1});" +
                    "var end=Array.from(document.querySelectorAll('#l4 tr.file[data-univer-sliding]')).every(function(r){return r.getBoundingClientRect().height<1.5});runs.forEach(function(a){a.finish()});window.qaFoldRuns=null;return end?'slid':'end false'})()");
                await Task.Delay(150);
                const string slideBack = @"(function(){var head=Array.from(document.querySelectorAll('#l4 th[data-univer-fold]')).find(function(t){return /Конспекты/.test(t.textContent)});
var rows=Array.from(document.querySelectorAll('#l4 tr.file')).filter(function(r){return r.hasAttribute('data-univer-folded-row')});
var shut=rows.length===3&&rows.every(function(r){return getComputedStyle(r).display==='none'&&!r.hasAttribute('data-univer-sliding')});
var click=function(){head.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,detail:1,view:window}))},value=function(r){return parseFloat(getComputedStyle(r).getPropertyValue('--univer-slide'))};
click();var runs=rows.map(function(r){return r.getAnimations()[0]});if(!runs.every(Boolean))return 'no unfold';
var length=runs[0].effect.getComputedTiming().duration;runs.forEach(function(a){a.pause();a.currentTime=length/4});var before=value(rows[0]);
click();var after=value(rows[0]),turned=rows.every(function(r){return r.hasAttribute('data-univer-folded-row')})&&before>.05&&before<.95&&Math.abs(after-before)<.05;
click();rows.forEach(function(r){r.getAnimations().forEach(function(a){a.finish()})});return shut&&turned?'turned':'shut '+shut+' turned '+turned+' '+before+'→'+after})()";
                var turned = await AutomationController.EvaluateAsync(main, slideBack); await Task.Delay(150);
                var settled = await AutomationController.EvaluateAsync(main, "(function(){var rows=Array.from(document.querySelectorAll('#l4 tr.file'));window.__univerMotion=window.qaFoldMotion;" +
                    "return rows.every(function(r){return getComputedStyle(r).display!=='none'&&!r.hasAttribute('data-univer-sliding')&&!r.hasAttribute('data-univer-folded-row')&&!r.getAnimations().length})?'open':'stuck'})()");
                Check(slid == "slid" && turned == "turned" && settled == "open", "a clicked UMKD fold slides its rows shut and open, turns back from where it is, and settles with no marks (" + slid + ", " + turned + ", " + settled + ")", results);
                // The real path: a mouse click on the link, Chromium's navigation decision, the check against the page.
                // The page scrolls smoothly, and an earlier scroll may still be running: the link is measured only once
                // it has stopped moving, or the press lands on the row's download link above it.
                await AutomationController.EvaluateAsync(main, "document.getElementById('qa-outside').scrollIntoView({block:'center',behavior:'instant'})");
                const string centre = "(function(){var r=document.getElementById('qa-outside').getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)})()";
                string at = null;
                for (var look = 0; look < 20; look++) { await Task.Delay(100); var now = await AutomationController.EvaluateAsync(main, centre); if (now == null) continue; if (now == at) break; at = now; }
                // Never measured: no click, so the check below fails instead of pressing somewhere else on the page.
                var point = Point.Empty;
                if (at != null) { var xy = at.Split(','); point = new Point(int.Parse(xy[0]), int.Parse(xy[1])); Press(point, false); Press(point, true); }
                var answer = await OutsideAnswerAsync(() => opened, "externalBlocked"); await Task.Delay(200);
                QaTrace("umkd-click at=" + point + " opened=" + opened + " answer=" + answer + " current=" + messages.Current?.Key + " toast=" + toast.Shown + "/" + toast.AccessibleName + " address=" + main.Address);
                Check(opened == "https://example.org/lecture?week=1" && answer == "externalOpened" && toast.Shown && toast.AccessibleName.Contains("example.org") && SitePolicy.StudyMaterials(main.Address),
                    "a click on a teacher's link opens that address in the browser, says so, and the app stays on the course", results);
                await Task.Delay(700);
                QaSnapshot(Path.Combine(qa, "umkd-link-toast.png"), toast.Width, toast.Height, bitmap => toast.DrawToBitmap(bitmap, new Rectangle(Point.Empty, toast.Size)));
                opened = null; messages.Dismiss(); RenderFeedback();
                PageLink(main, "https://example.org/book"); answer = await OutsideAnswerAsync(() => opened, "externalBlocked");
                Check(opened == "https://example.org/book" && answer == "externalOpened", "an address written as text in a material's row opens too", results);
                opened = null; messages.Dismiss(); RenderFeedback();
                PageLink(main, "https://elsewhere.example/phish"); answer = await OutsideAnswerAsync(() => opened, "externalBlocked");
                var foreign = opened == null && answer == "externalBlocked";
                messages.Dismiss(); RenderFeedback();
                PageLink(main, "http://192.168.0.1/admin"); answer = await OutsideAnswerAsync(() => opened, "externalBlocked");
                Check(foreign && opened == null && answer == "externalBlocked" && feedback.Visible, "an address that is not in the materials, or is not a public web page, does not open and the app says why", results);
                messages.Dismiss(); RenderFeedback();
                // Where each file would be kept: the course from the breadcrumb, the section from the heading above the file.
                var first = StudyMaterials.Folder(await AutomationController.EvaluateAsync(main, StudyMaterials.Place(SitePolicy.Origin + "/student/umkd/get/9001/321/2026/1")));
                var third = StudyMaterials.Folder(await AutomationController.EvaluateAsync(main, StudyMaterials.Place(SitePolicy.Origin + "/student/umkd/get/9003/321/2026/1")));
                var unknown = StudyMaterials.Folder(await AutomationController.EvaluateAsync(main, StudyMaterials.Place(SitePolicy.Origin + "/student/umkd/get/1/321/2026/1")));
                QaTrace("umkd-folders " + first + " | " + third + " | " + unknown);
                Check(first == @"Univer УМКД\Социология\Силлабус" && third == @"Univer УМКД\Социология\Конспекты лекций, тематический материал для практических занятий" && unknown == "Univer УМКД",
                    "a UMKD file is placed by its course and the heading it stands under; an unknown file stays in the UMKD folder", results);
                string started = null; downloadProbe = url => { started = url; return true; };
                try { await DownloadAsync(SitePolicy.Origin + "/student/umkd/get/9002/321/2026/1"); }
                finally { downloadProbe = null; }
                Check(started != null, "a UMKD file link still starts its download", results);
                // «Скачать все»: a real click on the button beside a section's heading sends that section's files one after
                // another; the one-file section has no button, and the heading keeps its text and its fold.
                var sent = new List<string>(); downloadProbe = url => { sent.Add(url); return true; };
                try
                {
                    const string head = "document.querySelector('#l4 button.univer-app-fold-all').parentNode.querySelector(':scope>th')";
                    var spot = await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelectorAll('#l4 button.univer-app-fold-all');if(b.length!==1)return 'count '+b.length;var h=" + head + ";" +
                        "if(b[0].closest('th')||h.textContent!=='Конспекты лекций/тематический материал для практических занятий.'||b[0].getAttribute('aria-label')!=='Скачать все файлы раздела'||b[0].dataset.label!=='Скачать все')return 'shape';" +
                        "b[0].scrollIntoView({block:'center',behavior:'instant'});var r=b[0].getBoundingClientRect(),t=h.getBoundingClientRect();if(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)!==b[0])return 'covered';" +
                        "if(r.right>t.right||r.top<t.top||r.bottom>t.bottom)return 'outside';return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2);})()");
                    var before = await AutomationController.EvaluateAsync(main, head + ".getAttribute('aria-expanded')");
                    QaTrace("umkd-all " + spot + " expanded=" + before);
                    var place = (spot ?? "").Split(',');
                    if (place.Length == 2) { var target = new Point(int.Parse(place[0]), int.Parse(place[1])); await Task.Delay(150); Press(target, false); Press(target, true); }
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < until && messages.Current?.Key != "downloadAllDone") await Task.Delay(50);
                    var after = await AutomationController.EvaluateAsync(main, head + ".getAttribute('aria-expanded')");
                    var summary = SectionText("downloadAllDone", "ru");
                    QaTrace("umkd-all sent=" + string.Join(" ", sent) + " key=" + messages.Current?.Key + " text=" + summary + " expanded=" + after);
                    Check(place.Length == 2 && sent.Count == 3 && sent[0].EndsWith("/9002/321/2026/1", StringComparison.Ordinal) && sent[2].EndsWith("/9004/321/2026/1", StringComparison.Ordinal) &&
                        messages.Current?.Key == "downloadAllDone" && summary != null && summary.Contains("Новых файлов: 3") && before == after,
                        "«Скачать все» beside a section's heading sends its three files in order, reports the count, and leaves the fold as it was", results);
                    using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "umkd-download-all.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                    messages.Dismiss(); RenderFeedback();
                }
                finally { downloadProbe = null; }
                await SectionRunQaAsync(results);
                prefs.Readable = false; await ApplyPresentationAsync();
                Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('.univer-app-outside-link')&&!document.getElementById('qa-outside').title&&document.getElementById('9003').cells[2].childNodes.length===1&&" +
                    "document.getElementById('9003').cells[2].textContent==='Источник: https://example.org/book.'?'restored':''") == "restored", "Original view removes the link marks and restores the written address as text", results);
                // Original still answers a click by the same rule: the address is read from the row's own text.
                opened = null; PageLink(main, "https://example.org/book"); answer = await OutsideAnswerAsync(() => opened, "externalBlocked");
                Check(opened == "https://example.org/book", "in Original view the written address is still recognised as the course's own", results);
                messages.Dismiss(); RenderFeedback();
                prefs.Readable = true; await ApplyPresentationAsync();
            }
            finally { outsideProbe = null; }
            await StickyControlsQaAsync(qa, results, heading);
        }
        // A run that takes time, with each file answering the way the download service does (its own message queued after
        // it ends): a double click, at once or at a person's pace, runs the section once to its end and the summary outlasts
        // the last file's message; a click on the button that shows the run stops it; a late Stop starts nothing.
        private async Task SectionRunQaAsync(List<string> results)
        {
            var fetched = new List<string>();
            sectionFetch = async (url, status) =>
            {
                fetched.Add(url); await Task.Delay(1200);
                var outcome = url.Contains("/9003/") ? "downloadFailed" : "downloadComplete";
                Dispatch(() => status(outcome)); return outcome;
            };
            const string button = "document.querySelector('#l4 button.univer-app-fold-all')";
            // The page starts a run only on the student's own click (isTrusted), so the QA presses the button as a person does.
            // landed: every press was measured and reached the button, so a check cannot pass on its first half alone. Each
            // press compares the page's click count with its own count before it: a press counted twice (a late DevTools click
            // and the host's fallback) cannot cover for a later press that is lost.
            var landed = true;
            Func<Task> press = async () =>
            {
                var point = await AutomationController.EvaluateAsync(main, "(function(){var b=" + button + ";if(!window.qaAllHook){window.qaAllHook=1;window.qaAllClicks=0;" +
                    "document.addEventListener('click',function(e){if(e.isTrusted&&e.target.closest&&e.target.closest('button.univer-app-fold-all'))window.qaAllClicks++},true)}" +
                    "b.scrollIntoView({block:'center',behavior:'instant'});var r=b.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)+','+window.qaAllClicks})()");
                if (point == null) { landed = false; QaTrace("umkd-press unmeasured"); return; }
                var xy = point.Split(',');
                var seen = "window.qaAllClicks>" + xy[2];
                using (var client = main.GetDevToolsClient()) await PressQaPointAsync(client, int.Parse(xy[0]), int.Parse(xy[1]), seen);
                if (await AutomationController.EvaluateAsync(main, "(" + seen + ")?'y':''") != "y") { landed = false; QaTrace("umkd-press missed after " + xy[2]); }
            };
            Func<string, Task> settled = async key =>
            {
                var until = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < until && (sectionRun != null || messages.Current?.Key != key)) await Task.Delay(50);
                await Task.Delay(250);
            };
            try
            {
                // The click of the check before is less than 1.5 s old, and the page ignores a click that soon after.
                await Task.Delay(1600);
                // A script's click starts nothing.
                await AutomationController.EvaluateAsync(main, button + ".click();'ok'"); await Task.Delay(300);
                Check(fetched.Count == 0 && sectionRun == null, "a click a page script dispatches on «Скачать все» starts no downloads", results);
                await press(); await press();
                await settled("downloadAllDone");
                var summary = SectionText("downloadAllDone", "ru");
                QaTrace("umkd-run fetched=" + fetched.Count + " key=" + messages.Current?.Key + " text=" + summary);
                Check(landed && fetched.Count == 3 && messages.Current?.Key == "downloadAllDone" && summary != null && summary.Contains("Новых файлов: 2") && summary.Contains("не скачались: 1"),
                    "a double click on «Скачать все» runs the section once to its end, and its summary stays after the last file's own message", results);
                fetched.Clear(); messages.Dismiss(); RenderFeedback(); await Task.Delay(1600); landed = true;
                // The second half of a person's double click comes when the button already shows the run.
                await press();
                var until = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, button + ".hasAttribute('data-univer-busy')?'busy':''") != "busy") await Task.Delay(20);
                await press();
                await settled("downloadAllDone");
                QaTrace("umkd-double fetched=" + fetched.Count + " key=" + messages.Current?.Key);
                Check(landed && fetched.Count == 3 && messages.Current?.Key == "downloadAllDone", "a double click at a person's pace, whose second half sees the run's label, does not stop the run", results);
                fetched.Clear(); messages.Dismiss(); RenderFeedback(); await Task.Delay(1600);
                await press();
                until = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < until && fetched.Count == 0) await Task.Delay(50);
                await Task.Delay(1600);
                await press();
                await settled("downloadAllStopped");
                var label = await AutomationController.EvaluateAsync(main, button + ".dataset.label");
                QaTrace("umkd-stop fetched=" + fetched.Count + " key=" + messages.Current?.Key + " label=" + label);
                Check(fetched.Count > 0 && fetched.Count < 3 && messages.Current?.Key == "downloadAllStopped" && label == "Скачать все",
                    "a click on the button that shows the run stops it after the current file and gives the label back", results);
                fetched.Clear(); messages.Dismiss(); RenderFeedback(); await Task.Delay(1600);
                await AutomationController.EvaluateAsync(main, "window.__univerPagesV1.downloading('Конспекты лекций/тематический материал для практических занятий.',1,3);'ok'");
                await press();
                await Task.Delay(600);
                label = await AutomationController.EvaluateAsync(main, button + ".dataset.label");
                Check(fetched.Count == 0 && sectionRun == null && label == "Скачать все", "a late Stop after the run has ended starts nothing and gives the label back", results);
            }
            finally { sectionFetch = null; messages.Dismiss(); RenderFeedback(); }
        }
        // Collapse and the page numbers stay at the bottom edge of the window while their block is on screen, and rest
        // in their own place once it is reached.
        private async Task<string> StickyProbeAsync(string script) { var value = await AutomationController.EvaluateAsync(main, script); QaTrace("sticky " + value); return value; }
        private async Task StickyControlsQaAsync(string qa, List<string> results, string heading)
        {
            var json = new JavaScriptSerializer();
            var article = "";
            for (var part = 0; part < 40; part++) article += "<p>Студенты могут ознакомиться с программой встречи, выбрать интересующее направление и узнать об условиях участия. Подробности и расписание приведены в объявлении.</p>";
            var rows = NewsQaRow("09", "<strong>Длинное объявление</strong>", article);
            for (var item = 0; item < 14; item++) rows += NewsQaRow("08", "Короткое объявление " + item + ": встреча начнётся в 15:00.");
            var page = NewsQaPage(rows, true);
            var fetch = "<script>sessionStorage.removeItem('univer.desktop.news-filter.v1');sessionStorage.removeItem('univer.desktop.news-page-count.v1');window.fetch=async function(path){var p=+path.split('/').pop(),body=" + json.Serialize(NewsQaPage(NewsQaRow("08", "Синтетическая новость"), true)) +
                ";body=body.replace('Страница 1','Страница '+p).replace('/my/news/2','/my/news/'+(p+1));if(p>=20)body=body.replace(/<a href='[^']+'>2<\\/a>/,'');return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return body;}}};</script>";
            main.LoadHtml(heading + fetch + page, SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync();
            var until = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-pager:not(.univer-app-news-pager-top) a[href=\"/news/student/20\"]')?'ready':''") != "ready") await Task.Delay(100);
            const string measure = "var pager=document.querySelector('.univer-app-news-pager:not(.univer-app-news-pager-top)'),toggle=document.querySelector('.univer-app-news-toggle'),body=document.querySelector('.univer-app-news-body');" +
                "var p=pager.getBoundingClientRect(),t=toggle.getBoundingClientRect(),b=body.getBoundingClientRect(),h=innerHeight;";
            Check(await StickyProbeAsync("(function(){window.__univerMotion=false;scrollTo({top:0,behavior:'instant'});document.querySelector('.univer-app-news-toggle').click();" + measure +
                "return document.documentElement.scrollHeight>h*2&&Math.abs(p.bottom-(h-12))<=1&&p.left>=0&&p.right<=innerWidth&&getComputedStyle(pager).position==='sticky'?'pager':JSON.stringify({bottom:p.bottom,h:h,position:getComputedStyle(pager).position,scroll:document.documentElement.scrollHeight})})()") == "pager",
                "the page numbers of a long news list stay at the bottom edge of the window", results);
            // The site's own div.inner scrolls (overflow:auto); the fixture keeps that, since it is what once held both controls in place.
            Check(await StickyProbeAsync("(function(){var box=document.querySelector('#l4 div.inner'),s=getComputedStyle(box),card=document.querySelector('tr.news').getBoundingClientRect(),de=document.documentElement;" +
                "var pager=document.querySelector('.univer-app-news-pager:not(.univer-app-news-pager-top)'),link=pager.querySelector('a').getBoundingClientRect(),jump=pager.querySelector('.univer-app-news-jump'),j=jump?jump.getBoundingClientRect():link;" +
                "return box.style.overflow==='auto'&&s.overflowX==='visible'&&s.overflowY==='visible'&&de.scrollWidth<=de.clientWidth&&card.width<=900&&card.width>600&&Math.abs(card.left+card.right-de.clientWidth)<=2&&Math.abs(link.top+link.bottom-j.top-j.bottom)<=1&&pager.getBoundingClientRect().height<=56?'column':" +
                "JSON.stringify({inline:box.style.overflow,x:s.overflowX,y:s.overflowY,page:[de.scrollWidth,de.clientWidth],card:card.width,sides:[card.left,card.right],link:[link.top,link.bottom],jump:[j.top,j.bottom],pager:pager.getBoundingClientRect().height})})()") == "column",
                "the news column does not scroll sideways, stays about a sheet wide in the middle of the window and its page numbers sit level", results);
            Check(await StickyProbeAsync("(function(){scrollTo({top:300,behavior:'instant'});" + measure +
                "var band=getComputedStyle(toggle,'::before'),under=document.elementFromPoint(t.right+40,t.top+t.height/2);" +
                "return b.bottom>h+200&&toggle.getAttribute('aria-expanded')==='true'&&Math.abs(t.bottom-(h-78))<=1&&t.bottom<=p.top-8&&t.top>b.top&&band.position==='absolute'&&band.pointerEvents==='none'&&under!==toggle&&document.elementFromPoint(t.left+t.width/2,t.top+t.height/2)===toggle?'toggle':" +
                "JSON.stringify({toggle:[t.top,t.bottom,t.right],pager:[p.top,p.left],body:b.bottom,h:h,band:band.position})})()") == "toggle",
                "Collapse follows an open long article along the bottom edge, one row above the page numbers, and only the button itself is clickable", results);
            await Task.Delay(150);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "sticky-news-following.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Check(await StickyProbeAsync("(function(){scrollTo({top:scrollY+document.querySelector('.univer-app-news-body').getBoundingClientRect().bottom-200,behavior:'instant'});" + measure +
                "return t.top>=b.bottom-1&&t.top<=b.bottom+16&&t.bottom<h-90?'rest':JSON.stringify({toggle:[t.top,t.bottom],body:b.bottom,h:h})})()") == "rest",
                "Collapse stops in its own place at the end of the article", results);
            Check(await StickyProbeAsync("(function(){scrollTo({top:document.documentElement.scrollHeight,behavior:'instant'});" + measure +
                "var last=Array.from(document.querySelectorAll('table.univer-app-news tr.news')).filter(function(r){return r.getClientRects().length}).pop().getBoundingClientRect();" +
                "return p.top>=last.bottom-1&&p.bottom<=h-12+1?'rest':JSON.stringify({pager:[p.top,p.bottom],last:last.bottom,h:h})})()") == "rest",
                "at the end of the list the page numbers rest under the last news item", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "sticky-news-rest.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Check(await StickyProbeAsync("(function(){scrollTo({top:0,behavior:'instant'});var toggle=document.querySelector('.univer-app-news-toggle');toggle.click();var folded=getComputedStyle(toggle).position;" +
                "window.__univerDesktop.apply(false,false,'ru');var gone=!document.querySelector('.univer-app-news-pager,.univer-app-news-toggle');window.__univerDesktop.apply(true,false,'ru');delete window.__univerMotion;" +
                "return folded!=='sticky'&&gone?'plain':JSON.stringify({folded:folded,gone:gone})})()") == "plain",
                "a folded article keeps its button in the text, and Original has neither control", results);
            // The debts memo: its Collapse rides the window edge while the memo is open.
            var memo = "<td class='ct'><b><font color='red'>ВНИМАНИЕ!</font><br>ПАМЯТКА ПО ПОВТОРНОМУ ИЗУЧЕНИЮ ДИСЦИПЛИН<br></b>";
            for (var step = 1; step <= 60; step++) memo += step + ". Синтетический шаг памятки: заявка, рассмотрение в деканате, договор, оплата и проверка статуса в системе перед началом занятий.<br>";
            main.LoadHtml(heading + StudyPage(Panel(Row("<td class='ct'>Информация</td>", "top") + Row(memo + "</td>")) + Panel(Row("<td class='ct'>Список задолженностей студента.</td>", "top") + Row("<td class='tt'><table class='inner'><tr><th>Дисциплина</th><th>Кред.</th></tr><tr><td colspan='2'>Нет данных</td></tr></table></td>"))),
                SitePolicy.Origin + "/student/fail/"); await WaitQaPresentationAsync();
            Check(await StickyProbeAsync("(function(){var m=document.querySelector('#l4 .univer-app-memo'),b=m&&m.querySelector('.univer-app-memo-toggle');if(!b)return 'no memo';var fr=b.getBoundingClientRect(),mr=m.getBoundingClientRect();" +
                "var folded=fr.bottom<=mr.bottom&&fr.top>=mr.top;b.click();scrollTo({top:0,behavior:'instant'});var r=b.getBoundingClientRect(),o=m.getBoundingClientRect(),h=innerHeight;var follows=o.bottom>h+100&&Math.abs(r.bottom-(h-16))<=1&&document.elementFromPoint(r.left+r.width/2,r.top+r.height/2)===b;" +
                "scrollTo({top:document.documentElement.scrollHeight,behavior:'instant'});r=b.getBoundingClientRect();o=m.getBoundingClientRect();var rests=r.bottom<=o.bottom&&r.bottom>=o.bottom-40;" +
                "return folded&&follows&&rests?'memo':JSON.stringify({folded:folded,follows:follows,rests:rests,button:[r.top,r.bottom],memo:o.bottom,h:h})})()") == "memo",
                "Collapse of an open memo follows the bottom edge of the window and rests at the memo's end", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "sticky-memo-rest.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "scrollTo({top:0,behavior:'instant'});'top'");
        }
    }
}
