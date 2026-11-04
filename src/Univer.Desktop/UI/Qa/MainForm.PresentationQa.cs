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
        private async Task PresentationQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            prefs.Language = "ru"; prefs.Readable = true; prefs.AutoLoginPaused = false; guard.Retry();
            credentials = new Credentials { Login = "fixture-account", Password = "synthetic fixture" };
            var heading = "<script>window.qaFirstOpacity=getComputedStyle(document.documentElement).opacity;window.qaGateFirst=!!window.__univerPaintV1;window.qaBootOrigin=window.__univerBootstrapOrigin;window.qaSubmits=0;function jQuery(){return{bind:function(k,f){window.qaHooks=window.qaHooks||{};window.qaHooks[k]=f;}}}</script>";
            var course = @"<table><tr id='l4'><td><table class='inner'><tr><th>№</th><th>Дисциплина</th><th>Тип дисциплины</th></tr><tr class='link' id='fixture'><td>1</td><td>История и культура: синтетическая дисциплина</td><td>Обязательная</td></tr></table></td></tr></table>";
            SetPresentation(main, true); main.LoadHtml(heading + course, SitePolicy.Origin + "/student/umkd/");
            await WaitQaPresentationAsync();
            QaTrace("qa-first " + await AutomationController.EvaluateAsync(main, "JSON.stringify({first:window.qaFirstOpacity,firstGate:window.qaGateFirst,bootOrigin:window.qaBootOrigin,gate:!!window.__univerPaintV1,origin:location.origin,ready:document.readyState,sheets:document.adoptedStyleSheets.length})"));
            Check(await AutomationController.EvaluateAsync(main, "window.qaFirstOpacity==='0'?'covered':''") == "covered", "document-start cover precedes first page script", results);
            await AutomationController.EvaluateAsync(main, "window.__univerNativeNotify('univer-language:kz');'sent'"); await Task.Delay(250);
            Check(prefs.Language == "kz", "native message uses a verified main execution context", results);
            prefs.Language = "ru"; Localize();
            await AutomationController.EvaluateAsync(main, "window.addEventListener('message',function(e){if(typeof e.data==='string'&&e.data.indexOf('qa-bound:')===0)window.qaBound=e.data});var f=document.createElement('iframe');f.id='qa-opaque';f.setAttribute('sandbox','allow-scripts');f.srcdoc=\"<script>parent.postMessage('qa-bound:'+typeof __univerNativeNotify,'*');__univerNativeNotify('univer-language:en')<\\/script>\";document.body.appendChild(f);'sandbox'"); await Task.Delay(250);
            var opaqueUntil = DateTime.UtcNow.AddSeconds(3); string opaqueBinding;
            do { opaqueBinding = await AutomationController.EvaluateAsync(main, "window.qaBound"); if (opaqueBinding != null) break; await Task.Delay(100); } while (DateTime.UtcNow < opaqueUntil);
            await Task.Delay(250);
            // Modern Chromium may isolate this sandbox in another renderer with no Runtime binding.
            // Both outcomes must leave the native language unchanged; require proof that the frame ran.
            Check((opaqueBinding == "qa-bound:function" || opaqueBinding == "qa-bound:undefined") && prefs.Language == "ru", "opaque frame cannot change native language through the binding", results);
            QaTrace("qa-opaque-safe " + opaqueBinding);
            await AutomationController.EvaluateAsync(main, "document.getElementById('qa-opaque').remove();'removed'");
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('.univer-app-courses'),c=t.rows[1].cells[1];return getComputedStyle(t).display==='table'&&c.getBoundingClientRect().width>160&&getComputedStyle(t.rows[1].cells[0]).display!=='none'?'wide':''})()") == "wide", "UMKD wide table keeps readable discipline and aligned first column", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "umkd-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 900; await Task.Delay(250);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-courses').rows[1].cells[1].getBoundingClientRect().width>160?'readable':''") == "readable", "UMKD narrow card keeps discipline readable", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "umkd-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 1360;
            var empty = @"<table class='wrappertable'><tr id='l4'><td><table class='mt'><tr><td class='lt'></td><td class='ts'><a href='/student/educplan/1/' style='float:left'>Семестр 1</a><a href='/student/educplan/2/' style='float:right'>Семестр 2</a></td><td class='rt'></td></tr></table><table class='inner'><tr><th>№</th><th>Обращение</th><th>Дата</th><th>Статус</th><th></th></tr><tr><td colspan='5'>Обращения не найдены</td></tr></table><table class='cols'><tr><td>Инструкция</td><td><table class='inner'><tr><th>Дисциплина</th><th>Дата</th></tr><tr><td colspan='2'>Нет расписания</td></tr></table></td></tr></table></td></tr></table>";
            main.LoadHtml(heading + empty, SitePolicy.Origin + "/my/appeals/index/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var c=document.querySelector('.univer-app-card-section td'),t=c.closest('table');return c.getBoundingClientRect().width>t.getBoundingClientRect().width*.9?'full':''})()") == "full", "empty state spans all desktop columns", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var c=document.querySelector('table.cols'),a=c.rows[0].cells;return a[1].getBoundingClientRect().top>=a[0].getBoundingClientRect().bottom?'stacked':''})()") == "stacked", "legacy instruction columns stack above their readable table", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('.ts'),r=t.getBoundingClientRect();return r.height>=44&&Array.from(t.querySelectorAll('a')).every(function(a){return a.getBoundingClientRect().bottom<=r.bottom+1})?'visible':''})()") == "visible", "semester tabs remain visible in the adapted layout", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "empty-tables.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var flow = @"<table><tr id='l4'><td><table class='mt'><tr><td class='lt'></td><td class='ct'>Инструкции</td><td class='rt'></td></tr><tr><td class='lt'></td><td class='ct'><div id='qa-instructions' style='float:left;width:50%'><p>Инструкция для студента: синтетическая проверка читаемого блока.</p><a href='#schedule'>Инструкция</a></div><div style='float:left;width:50%'></div></td><td class='rt'></td></tr></table><table class='mt' id='schedule'><tr><td class='lt'></td><td class='ct'>Расписание экзаменов</td><td class='rt'></td></tr></table></td></tr></table>";
            main.LoadHtml(heading + flow, SitePolicy.Origin + "/student/myexam/schedule/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var e=document.getElementById('qa-instructions');return getComputedStyle(e).cssFloat==='none'&&e.getBoundingClientRect().width>e.parentNode.getBoundingClientRect().width*.9?'flow':''})()") == "flow", "exam instruction float no longer overlaps the schedule", results);
            var contracts = @"<table><tr id='l4'><td><table class='inner'><tr><th><div class='full-width'><div>Название</div><div>Договор</div><div>Обязательно</div><div>Дата создания</div><div>Договор подписан</div><div></div></div></th></tr><tr><td><div class='full-width'>Данные не найдены</div></td></tr></table></td></tr></table>";
            main.LoadHtml(heading + contracts, SitePolicy.Origin + "/student/contract/list/"); await WaitQaPresentationAsync();
            Width = 900; await Task.Delay(200);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('.univer-app-div-records');return getComputedStyle(t.querySelector('th>.full-width')).display==='grid'&&t.parentNode.classList.contains('univer-app-table-scroll')?'contracts':''})()") == "contracts", "observed contract DIV headings keep aligned columns and accessible scrolling", results);
            Width = 1360;
            await ScheduleQaAsync(qa, results, heading);
            await SmartScheduleQaAsync(qa, results, heading);
            await NewsQaAsync(qa, results, heading);
            await AttendanceQaAsync(qa, results, heading);
            await StudyPagesQaAsync(qa, results, heading);
            await InterfaceDetailsQaAsync(qa, results, heading);
            await MotionQaAsync(qa, results, heading);
            var notice = @"<p>Синтетическая страница</p><form id='attentionSt' action='/student/attentionStudents/1'><input id='check' type='checkbox' onclick='document.getElementById(&quot;btn&quot;).disabled=false'><label for='check'>Я подтверждаю, что прочёл данное уведомление</label><button id='btn' type='submit' disabled onclick='event.preventDefault();window.qaSubmits++;this.closest(&quot;form&quot;).remove()'>Отправить</button></form>";
            SetPresentation(main, true); main.LoadHtml(heading + notice, SitePolicy.Origin + "/student/attentionStudents/1");
            SaveBitmap(loading, Path.Combine(qa, "loading-ru.png"));
            await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "window.qaFirstOpacity==='0'&&window.qaSubmits===1&&!document.getElementById('attentionSt')?'notice':''") == "notice", "known notice stays covered and submits original handler once", results);
            Check(messages.Current == null || messages.Current.Key != "confirming", "known notice has no visible notification message", results);
            // The session ends right after the notice: only the login form comes in between, and the new session shows the
            // same notice again at once. It is a new appearance of a new session, sent again.
            Check(guard.TryLogin("qa-login-form", 0), "fixture login of a new session is accepted", results); guard.Authenticated();
            SetPresentation(main, true); main.LoadHtml(heading + notice, SitePolicy.Origin + "/student/attentionStudents/1");
            await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "window.qaSubmits===1&&!document.getElementById('attentionSt')?'again':''") == "again" && !messages.Shows("noticeFailed"),
                "the notice of a new session is confirmed again after a login page in between", results);
            var auth = @"<div id='auth_modal'><span id='auth_try'>1</span><form id='modal_auth_frm' action='/user/ajl'><input id='modal_auth_login' type='text' name='login' readonly value='fixture-account'><input id='modal_auth_pass' type='password' name='password'><input id='modal_auth_key' type='hidden' name='key' value='fixture-key'><input type='hidden' name='cheat' value='fixture-cheat'><input id='modal_auth_ajl_btn' type='submit' onclick='event.preventDefault();window.ajl()'></form></div>";
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            await AutomationController.EvaluateAsync(main, "window.qaCode=1;window.qaReauth=0;window.ajl=function(){window.qaReauth++;setTimeout(function(){window.qaHooks['ajaxSuccess.univerApp'](null,{responseText:JSON.stringify({code:window.qaCode})},{url:'/user/ajl',type:'POST'});if(window.qaCode===1)document.getElementById('auth_modal').hidden=true;},120);};document.body.insertAdjacentHTML('beforeend'," + json.Serialize(auth) + ");'created'");
            Check(await AutomationController.EvaluateAsync(main, "document.documentElement.style.opacity==='0'?'covered':''") == "covered", "late reauthentication is hidden before polling", results);
            await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "window.qaReauth===1&&document.getElementById('modal_auth_login').readOnly&&document.getElementById('modal_auth_key').value==='fixture-key'?'success':''") == "success", "silent AJAX sign in preserves readonly account and hidden fields", results);
            var until = DateTime.UtcNow.AddMilliseconds(1700);
            while (DateTime.UtcNow < until) { await automation.TickAsync(main, credentials, prefs, false); await Task.Delay(100); }
            await AutomationController.EvaluateAsync(main, "document.getElementById('auth_modal').hidden=false;'again'");
            await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "window.qaReauth===2?'twice':''") == "twice" && !guard.Blocked, "new AJAX challenge restores session without minute lockout", results);
            until = DateTime.UtcNow.AddMilliseconds(1700);
            while (DateTime.UtcNow < until) { await automation.TickAsync(main, credentials, prefs, false); await Task.Delay(100); }
            await AutomationController.EvaluateAsync(main, "window.qaCode=0;document.getElementById('auth_modal').hidden=false;'failure'");
            var requestsBefore = recoveryRequests;
            await WaitQaPresentationAsync();
            for (var settle = DateTime.UtcNow.AddSeconds(2); recoveryRequests == requestsBefore && DateTime.UtcNow < settle;)
            { await automation.TickAsync(main, credentials, prefs, false); await Task.Delay(100); }
            Check(guard.Blocked && !guard.Stopped && !loading.Visible && messages.Shows("sessionRestoring") && recoveryRequests > requestsBefore,
                "refused silent sign in uncovers the page and reloads it for a new sign-in instead of waiting for the user", results);
            // Suspend polling to prove that a verified AJAX outcome survives document replacement.
            guard.Retry(); messages.NewSession(); timer.Stop();
            await AutomationController.EvaluateAsync(main, "window.qaCode=1;document.getElementById('auth_modal').remove();document.body.insertAdjacentHTML('beforeend'," + json.Serialize(auth) + ");'created'");
            var probe = json.Deserialize<Dictionary<string, object>>(await AutomationController.EvaluateAsync(main, "JSON.stringify(window.__univerAutomationV1.probe())"));
            var challenge = (string)probe["reauthChallenge"]; var reserved = guard.TryReauth(challenge, (string)probe["document"], 0);
            var callResult = await automation.CallAsync(main, "reauthenticate", (string)probe["document"], challenge, credentials.Login, credentials.Password);
            // Polling stays stopped, so only the correlated bridge message can settle the challenge. Wait for it (the fixture
            // answers after a page timer) instead of a fixed delay; navigating first would cancel that timer.
            var settleUntil = DateTime.UtcNow.AddSeconds(3);
            do await Task.Delay(50); while (guard.ReauthChallenge != null && !guard.Blocked && DateTime.UtcNow < settleUntil);
            QaTrace("qa-bridge reserved=" + reserved + " call=" + callResult + " pending=" + (guard.ReauthChallenge != null) + " blocked=" + guard.Blocked + " " + await AutomationController.EvaluateAsync(main, "JSON.stringify({bridge:typeof window.CefSharp,post:typeof window.CefSharp?.PostMessage,calls:window.qaReauth,outcome:window.__univerAutomationV1.probe().reauthOutcome})"));
            main.LoadHtml(heading + "<p>Синтетическая страница после AJAX</p>", SitePolicy.Origin + "/student/bachelor/");
            await Task.Delay(200);
            Check(guard.ReauthChallenge == null && !guard.Blocked, "correlated AJAX success survives navigation between polling ticks", results);
            timer.Start(); await WaitQaPresentationAsync();
            credentials = null; prefs.AutoLoginPaused = true; guard.Retry(); messages.NewSession(); Localize();
        }
        private async Task InterfaceDetailsQaAsync(string qa, List<string> results, string heading)
        {
            var fixture = @"<table class='wrappertable'><tr id='l1'><td class='header'>Университет<span><a href='/lang/change/kz/'><img src='/img/kz.png'></a><br><img src='/img/ru.png'><a href='/lang/change/en/'><img src='/img/en.png'></a></span></td></tr><tr id='l4'><td class='center'><table class='inner table_sort' id='qa-requests'><thead><tr><th onclick='window.qaSort=1'>Номер</th><th>Услуга</th><th>Статус</th></tr></thead><tbody><tr><td>123456</td><td>Синтетическая справка</td><td>Завершена</td></tr></tbody></table><table class='inner' id='qa-profile'><tr><th>Данные</th><td></td><th>фото</th></tr><tr><td><label>E-mail<input type='email'></label></td><td>Текущее фото</td></tr><tr><th colspan='3'></th></tr><tr><td><button type='button' onclick='window.qaSave=1'>Сохранить</button></td></tr></table></td></tr></table>";
            // Flags keep their real paths for detection, but this offline fixture never waits on the live server for them.
            var pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            foreach (var flag in new[] { "kz", "ru", "en" }) main.RegisterResourceHandler(SitePolicy.Origin + "/img/" + flag + ".png", new MemoryStream(pixel), "image/gif");
            Width = 1360; main.LoadHtml(heading + fixture, SitePolicy.Origin + "/student/mydata/edit/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.getElementById('qa-requests'),h=t.rows[0];h.cells[0].click();return h.getBoundingClientRect().height>30&&Math.abs(h.cells[1].getBoundingClientRect().left-t.rows[1].cells[1].getBoundingClientRect().left)<2&&window.qaSort===1?'headings':''})()") == "headings", "wide sortable table keeps visible original column headings and handlers", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var f=document.querySelector('.univer-app-flags'),a=Array.from(f.children);return a.length===3&&a.every(function(e){return Math.abs(e.getBoundingClientRect().top-a[0].getBoundingClientRect().top)<2})?'flags':''})()") == "flags", "nested e-service flags align on one row", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.getElementById('qa-profile');t.querySelector('button').click();return t.rows[0].getBoundingClientRect().height===0&&t.rows[2].getBoundingClientRect().height===0&&t.querySelector('input').getBoundingClientRect().height>=40&&window.qaSave===1?'form':''})()") == "form", "profile removes decorative blank rows while retaining fields and save handler", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "interface-details.png"), (await client.Page.CaptureScreenshotAsync()).Data);
        }
        private async Task ScheduleQaAsync(string qa, List<string> results, string heading)
        {
            var schedule = @"<table class='wrappertable'><tr id='l4'><td class='center'><table class='mt'><tr><td class='lt'></td><td class='ct' id='schedule-title'>Расписание учебных занятий</td><td class='rt'></td></tr><tr><td class='lt'></td><td class='ts'><a style='float:left;width:48%' href='#previous'>Предыдущий семестр</a><a style='float:right;width:48%' href='#next'>Следующий семестр</a></td><td class='rt'></td></tr><tr><td class='lt'></td><td class='ts'><div style='float:right;width:30%;height:30px'><select id='weekSelect'><option>Неделя 1. 05.10.2026 – 11.10.2026</option><option>Неделя 2. 12.10.2026 – 18.10.2026</option></select></div></td><td class='rt'></td></tr><tr><td class='lt'></td><td class='ct' id='grid-title'>Сетка расписания для студентов</td><td class='rt'></td></tr></table><table class='schedule'><tr><th></th><th>Пн</th><th>Вт</th><th>Ср</th><th>Чт</th><th>Пт</th><th>Сб</th></tr><tr><td class='time'>09:00–09:50</td><td rowspan='2'><div class='groups'><div id='week-a'><p>Алгебра и аналитическая геометрия</p><p class='params'><span>101</span><span>Корпус 2</span></p><a href='#syllabus' onclick='event.preventDefault();window.qaLessonClicks=(window.qaLessonClicks||0)+1'>Силлабус</a></div><div id='week-b' style='display:none'><p>Дискретная математика</p></div></div></td><td></td><td></td><td></td><td></td><td></td></tr><tr><td class='time'>10:00–10:50</td><td><p>История Казахстана</p><p>Аудитория 205</p></td><td></td><td></td><td></td><td></td></tr></table></td></tr></table>";
            main.LoadHtml(heading + schedule, SitePolicy.Origin + "/student/myschedule/"); await WaitQaPresentationAsync();
            await AutomationController.EvaluateAsync(main, "(function(){var v=document.querySelector('.univer-app-schedule'),s=document.querySelector('.univer-app-schedule-scroll'),box=document.createElement('div');box.className='inner';box.id='legacy-schedule-scroll';box.style.cssText='height:120px;overflow:auto;position:relative';v.before(box);box.append(v,s);return 'wrapped';})()"); await Task.Delay(220);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var box=document.getElementById('legacy-schedule-scroll');return getComputedStyle(box).overflow==='visible'&&box.getBoundingClientRect().height>120?'expanded':''})()") == "expanded", "observed DIV.inner timetable container no longer clips the agenda", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var v=document.querySelector('.univer-app-schedule');return v.getBoundingClientRect().height>150&&v.querySelectorAll('.univer-app-lesson').length===2&&getComputedStyle(document.querySelector('table.schedule')).display==='none'?'agenda':''})()") == "agenda", "schedule agenda is visible in a wide window with the source grid hidden", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var w=document.getElementById('weekSelect').getBoundingClientRect(),h=document.getElementById('grid-title').getBoundingClientRect();return w.height>=40&&w.bottom<=h.top+1?'clear':''})()") == "clear", "week selector is readable and does not overlap the next heading", results);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-lesson time').textContent==='09:00–10:50'?'time':''") == "time", "schedule agenda keeps the rowspan lesson duration", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-schedule a').click();document.querySelectorAll('.univer-app-days button')[1].click();'clicked'");
            Check(await AutomationController.EvaluateAsync(main, "window.qaLessonClicks===1&&document.querySelectorAll('.univer-app-day:not([hidden])').length===1?'interactive':''") == "interactive", "schedule links and day filters retain working handlers", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-days button:last-child').click();'all'");
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "schedule-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 900; await Task.Delay(200);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var v=document.querySelector('.univer-app-schedule');return v.getBoundingClientRect().height>150&&document.documentElement.scrollWidth<=innerWidth+2?'narrow':''})()") == "narrow", "schedule agenda fits a narrow window", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "schedule-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-schedule-modes button')[1].click();'grid'"); await Task.Delay(150);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('table.schedule'),s=t.parentElement;return t.getBoundingClientRect().height>80&&s.scrollWidth>s.clientWidth&&document.querySelector('.univer-app-day').getBoundingClientRect().height===0?'grid':''})()") == "grid", "explicit week grid shows the intact scrollable timetable", results);
            await AutomationController.EvaluateAsync(main, "document.getElementById('week-a').style.display='none';document.getElementById('week-b').style.display='block';'week'"); await Task.Delay(220);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-schedule').dataset.mode==='grid'&&document.querySelector('.univer-app-schedule').textContent.includes('Дискретная математика')?'updated':''") == "updated", "late week updates refresh the agenda without losing grid mode", results);
            prefs.Readable = false; await ApplyPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('.univer-app-schedule,.univer-app-table-scroll')&&document.querySelector('table.schedule').rows[1].cells[1].rowSpan===2?'restored':''") == "restored", "original schedule DOM and merged cells restore after Reading mode", results);
            prefs.Readable = true; Width = 1360; await ApplyPresentationAsync();
        }
        // The summary card and lesson statuses follow the page clock. The fixture moves that clock to midday of the current
        // date (it keeps running), so the class, its countdown and the week stay valid even when the run crosses midnight.
        private async Task SmartScheduleQaAsync(string qa, List<string> results, string heading)
        {
            var now = DateTime.Now.Date.AddHours(12); var shift = (long)(now - DateTime.Now).TotalMilliseconds;
            var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)); var today = (now - monday).Days;
            var minute = now.Hour * 60 + now.Minute; var start = minute - 30; var end = minute + 50;
            heading += "<script>(function(){var D=Date,s=" + shift + ";function Q(){return arguments.length?new(Function.prototype.bind.apply(D,[null].concat([].slice.call(arguments))))():new D(D.now()+s)}Q.prototype=D.prototype;Q.now=function(){return D.now()+s};Q.UTC=D.UTC;Q.parse=D.parse;window.Date=Q})()</script>";
            Func<int, string> clock = m => (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
            var cells = "";
            for (var day = 0; day < 7; day++)
                cells += day == today ? "<td><div class='groups'><div><p>Алгебра и аналитическая геометрия</p><p class='params'><span>Аудитория 101</span></p></div></div></td>" : "<td></td>";
            var html = "<table class='wrappertable'><tr id='l4'><td class='center'><select id='weekSelect'><option selected>Неделя 7. " + monday.ToString("dd.MM.yyyy") + " - " + monday.AddDays(6).ToString("dd.MM.yyyy") + "</option></select>" +
                "<div class='inner'><table class='schedule'><tr><th></th><th>ПН</th><th>ВТ</th><th>СР</th><th>ЧТ</th><th>ПТ</th><th>СБ</th><th>ВС</th></tr><tr><td class='time'>" + clock(start) + "-" + clock(end) + "</td>" + cells + "</tr></table></div></td></tr></table>";
            main.LoadHtml(heading + html, SitePolicy.Origin + "/student/myschedule/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var c=document.querySelector('.univer-app-now'),b=document.querySelector('.univer-app-lesson-badge');return c&&!c.hidden&&c.dataset.state==='now'&&c.getBoundingClientRect().height>60&&c.querySelector('.univer-app-now-progress>span').getBoundingClientRect().width>0&&/^Идёт · ещё \\d+ мин$/.test(b.textContent)?'live':''})()") == "live", "schedule summary shows the class in progress with a countdown and progress", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "schedule-today.png"), (await client.Page.CaptureScreenshotAsync()).Data);
        }
        private async Task WaitQaPresentationAsync()
        {
            var until = DateTime.UtcNow.AddSeconds(10); var ready = "";
            while (DateTime.UtcNow < until)
            {
                await automation.TickAsync(main, credentials, prefs, false);
                ready = await AutomationController.EvaluateAsync(main, "window.__univerAutomationV1&&document.readyState==='complete'?'complete':''");
                if (ready == "complete" && !loading.Visible) return;
                await Task.Delay(100);
            }
            throw new InvalidOperationException("Presentation did not finish within its QA deadline: ready=" + ready + " cover=" + loading.Visible +
                " awaiting=" + (awaitingCommit == main) + " loading=" + main.IsLoading + " url=" + main.Address);
        }
    }
}
