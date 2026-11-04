using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Home tiles, "All sections" with useful links, the success toast and loading placeholders at several window sizes.
        private async Task MotionQaAsync(string qa, List<string> results, string heading)
        {
            var monotonic = Motion.EaseOut(0) == 0 && Motion.EaseOut(1) == 1;
            for (var i = 1; i <= 100; i++) monotonic &= Motion.EaseOut(i / 100f) >= Motion.EaseOut((i - 1) / 100f);
            Check(monotonic && Motion.EaseOut(.2f) > .6f, "native easing responds early and settles monotonically", results);
            float sample = 0; var completed = false;
            var cancelled = new Tween(t => sample = t, () => completed = true, 140, 0);
            cancelled.Step(.4f); cancelled.Cancel(); cancelled.Finish();
            Check(sample == .4f && !completed, "interrupting feedback preserves the current value without completing the cancelled action", results);
            var useful = string.Join("", new[] { "https://academic.oup.com/journals|Oxford academic journals", "https://open.kaznu.kz/|Open KazNU", "https://apps.apple.com/us/app/mobile-univer/id1|Univer (iOS)",
                "https://www.facebook.com/kaznu|Facebook", "https://twitter.com/kaznu|Twitter", "https://omc.kaznu.kz/|Open Master Class", "https://www.youtube.com/kaznu|Youtube", "https://elib.kaznu.kz/|ЭБС",
                "https://kaizen.kaznu.kz/|Кайдзен", "/user/loginMoodle|СДО Moodle", "https://app.oqylyq.kz/|Oqylyq", "https://play.google.com/store/apps/details?id=x|Univer (Android)",
                "https://lib.kaznu.kz/|Библиотека", "https://wifi.kaznu.kz/|WiFi" }.Select(v => v.Split('|')).Select(v => "<a href='" + v[0] + "' target='_blank'>" + v[1] + "</a><br>"));
            var home = "<style>table{border-collapse:collapse;border-spacing:0}td{padding:0}</style><table class='wrappertable'><tr id='l1'><td class='header'>КазНУ<a href='/lang/change/kz/'><img src='/img/kz.png'></a><img src='/img/ru.png'><a href='/lang/change/en/'><img src='/img/en.png'></a></td></tr>" +
                "<tr id='l2'><td><div id='logo'></div><div class='links'><a href='/user/pass/'>Профиль</a><a href='/user/logout'>Выход</a></div></td></tr>" +
                "<tr id='l3'><td class='center'><a class='active' href='/news/student'><div class='center'><span style='color:#1b5fa6'>Главная</span></div></a><a href='/student/bachelor/'><div class='center'>Бакалавр</div></a></td></tr>" +
                "<tr id='qa-menu-row'><td><table class='inner'><tr><td class='center'><div id='menu-home'><table id='tools'><tr><td class='center'><table class='tool'><tr><td><ul id='study'>" +
                "<li><a href='/student/umkd/'>УМКД</a></li><li><a href='/student/myschedule/'>Расписание</a></li><li><a href='/student/transcript/'>Транскрипт</a></li>" +
                "<li><a href='/student/attendance/full/'>Журнал посещаемости</a></li><li><a href='/my/messages/index/'>Сообщения</a></li><li><a href='/faq/'>Часто задаваемые вопросы</a><span><a href='/fixture/exams/'>Расписание экзаменов с очень длинным названием для проверки переноса текста</a></span></li></ul></td></tr>" +
                "<tr><td class='bottom-center'>Главная</td></tr></table></td></tr></table></div>" +
                "</td></tr></table></td></tr><tr id='l4'><td><table class='inner'><tr><td class='left'></td><td class='center'><h1>Новости</h1><p>Синтетическая новость для проверки макета.</p><div class='links' id='useful'>" + useful + "</div></td><td class='right'></td></tr></table></td></tr></table>";
            prefs.Pins = new List<PinnedLink> { new PinnedLink { Title = "Сообщения", Url = SitePolicy.Origin + "/my/messages/index/" }, new PinnedLink { Title = "Youtube", Url = "https://www.youtube.com/kaznu" } };
            await RefreshDocumentScriptsAsync();
            SetPresentation(main, true); main.LoadHtml(heading + home, SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync();
            await ApplyPresentationAsync(); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-quick .univer-app-link-icon').length>=6&&document.querySelectorAll('.univer-app-useful .univer-app-link-icon').length===14?'icons':''") == "icons",
                "home tiles and every useful link carry an icon", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-quick .univer-app-reorder').focus();'focused'");
            var reorderHost = main.GetBrowser().GetHost();
            reorderHost.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = 35 });
            reorderHost.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = 35 });
            await Task.Delay(400);
            Check(prefs.TileOrder.Count == 8 && prefs.TileOrder.Last() == SitePolicy.Origin + "/student/myschedule/" &&
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-quick .univer-app-tile:last-child>a').pathname==='/student/myschedule/'?'ordered':''") == "ordered",
                "trusted keyboard reorder moves fixed tiles among pins and persists the order through the validated bridge", results);
            // The unpin cross sits on the tile's centre line, drawn as a centred mask rather than a text glyph.
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-unpin'),t=b.closest('.univer-app-tile').getBoundingClientRect(),r=b.getBoundingClientRect();" +
                "return Math.abs((r.top+r.bottom)/2-(t.top+t.bottom)/2)<=1&&b.textContent===''&&getComputedStyle(b,'::before').width==='14px'?'centred':''})()") == "centred", "unpin cross is centred in its tile", results);
            foreach (var size in new[] { new Size(1360, 860), new Size(1100, 760), new Size(960, 700), new Size(900, 620) })
            {
                Size = size; await Task.Delay(450);
                var tag = size.Width + "x" + size.Height;
                Check(await AutomationController.EvaluateAsync(main, "(function(){var q=document.querySelector('.univer-app-quick'),w=document.documentElement.clientWidth;" +
                    "return Array.from(q.querySelectorAll('.univer-app-tile')).every(function(t){var r=t.getBoundingClientRect();return r.left>=0&&r.right<=w+.5&&r.height>=44;})&&document.documentElement.scrollWidth<=w+1?'fits':''})()") == "fits",
                    "home tiles fit without horizontal scrolling at " + tag, results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "home-" + tag + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-sections').click();'open'"); await Task.Delay(350);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var m=document.querySelector('.univer-app-menu'),r=m.getBoundingClientRect(),w=document.documentElement.clientWidth;" +
                    "var cards=Array.from(m.querySelectorAll('.univer-app-useful li'));return m.open&&r.left>=0&&r.right<=w+.5&&cards.length===14&&cards.every(function(li){var s=getComputedStyle(li);return s.borderTopWidth==='1px'&&li.getBoundingClientRect().width>=180;})?'menu':''})()") == "menu",
                    "all sections panel shows bordered useful links inside the window at " + tag, results);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var a=document.querySelector('#tools a[href=\"/fixture/exams/\"]'),b=a.nextElementSibling,r=a.getBoundingClientRect(),p=b.getBoundingClientRect(),row=a.parentElement.getBoundingClientRect();return b.classList.contains('univer-app-pin')&&p.width>=28&&p.right<=row.right+1&&p.left>=r.right-1&&r.height>40&&getComputedStyle(b).display!=='none'&&!document.querySelector('#tools a[href=\"/student/umkd/\"]').nextElementSibling?'pins':''})()") == "pins",
                    "nested long menu link keeps its visible pin beside the wrapped label and fixed links have no duplicate action at " + tag, results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "menu-" + tag + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-menu-close').click();'closed'"); await Task.Delay(250);
            }
            Size = new Size(1360, 860);
            // Бакалавр and Е-услуги: their own purpose instead of repeating the home tiles.
            var shell = home.Substring(0, home.IndexOf("<h1>", StringComparison.Ordinal));
            var bachelor = shell + "<table class='mt' id='qa-gpa-source' style='height:70px'><tr class='top'><td class='ct message'><ul><li>Переводной GPA = 2.85</li><li>Общий GPA = 3.12</li></ul></td></tr></table><table class='mt'><tr class='top'><td class='ct'>Информационный бюллетень для студентов</td></tr><tr><td class='ct'><ol class='univer-app-list'><li><b>Уважаемые студенты! Синтетическое объявление.</b><br></li></ol></td></tr></table>" +
                "<table class='mt'><tr class='top'><td class='ct'>Личные данные</td></tr><tr><td class='ct'><img src='/content/icons/info.png'><b>Здравствуйте, Студент.</b><br></td></tr><tr class='brk'><td class='ct'>Обучение</td></tr><tr><td class='tt' id='qa-learning'><table class='inner'><tr><td><li>Форма обучения:</li></td><td>Очная</td><td><li>Уровень обучения:</li></td><td>Первое высшее образование</td></tr><tr><td><li>Факультет:</li></td><td>Синтетический факультет с длинным названием для проверки переноса</td><td><li>Специальность:</li></td><td>Синтетическая специальность</td></tr><tr><td><li>Курс:</li></td><td>1</td></tr></table></td></tr><tr><td class='ct'><ul class='univer-app-list'><li><a href='/Content/instructions/Files/guide.docx'>Руководство пользователя Бакалавр</a></li></ul></td></tr>" +
                "<tr class='brk'><td class='ct'>Логин и пароль для системы print.kaznu.kz и WiFi-сети КазНУ.</td></tr><tr class='mid' id='qa-credential-row'><td class='tt' id='qa-credential-source'><div class='inner'><table class='inner'><tr><td><li>Логин:</li></td><td>synthetic_login</td><td><li>Пароль:</li></td><td>synthetic_secret</td></tr><tr><td><li>Другой логин:</li></td><td>synthetic_other</td><td><li>Пароль:</li></td><td>synthetic_other_secret</td></tr></table></div></td></tr>" +
                "<tr class='mid' id='qa-credential-padding'><td class='pd'></td></tr><tr class='bot' id='qa-credential-below'><td class='ct'>Синтетический нижний блок</td></tr></table></td><td class='right'></td></tr></table></td></tr></table>";
            SetPresentation(main, true); main.LoadHtml(heading + bachelor.Replace("class='active' href='/news/student'", "href='/news/student'").Replace("href='/student/bachelor/'", "class='active' href='/student/bachelor/'"), SitePolicy.Origin + "/student/bachelor/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-gpa>div').length===2&&document.querySelectorAll('.univer-app-quick .univer-app-tile').length>=6?'gpa':''") == "gpa", "bachelor has the study tiles and the GPA cards", results);
            foreach (var size in new[] { new Size(1360, 860), new Size(900, 620) })
            {
                Size = size; await Task.Delay(350);
                await AutomationController.EvaluateAsync(main, "window.scrollTo(0,0);'top'"); await Task.Delay(250);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var q=document.querySelector('.univer-app-quick').getBoundingClientRect(),g=document.querySelector('.univer-app-gpa').getBoundingClientRect(),h=document.getElementById('l1').getBoundingClientRect(),p=document.querySelector('.univer-app-panel').getBoundingClientRect();return q.top-h.bottom>=23&&q.top-h.bottom<=26&&Math.abs(q.left-g.left)<2&&Math.abs(q.left-p.left)<2&&g.top-q.bottom>=23&&g.top-q.bottom<=26&&getComputedStyle(document.getElementById('qa-gpa-source')).display==='none'&&Array.from(document.querySelectorAll('.univer-app-panel-head')).every(function(n){return parseFloat(getComputedStyle(n).paddingLeft)>=18;})?'rhythm':''})()") == "rhythm",
                    "observed separate menu carrier, nested content and top/brk headers share gutters and 24px vertical gaps at " + size.Width, results);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('#qa-learning .univer-app-pairs'),r=t.getBoundingClientRect(),cell=document.getElementById('qa-learning').getBoundingClientRect();return t&&r.left-cell.left>=17&&Array.from(t.rows).every(function(row){return getComputedStyle(row).display==='grid'&&getComputedStyle(row).borderRadius==='0px'&&Array.from(row.cells).every(function(c){var x=c.getBoundingClientRect();return x.left>=r.left-1&&x.right<=r.right+1;});})?'learning':''})()") == "learning",
                    "learning records have an inset, stable label columns and no touching nested cards at " + size.Width, results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "spacing-bachelor-" + size.Width + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "document.getElementById('qa-learning').scrollIntoView({block:'center'});'learning'"); await Task.Delay(250);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "spacing-learning-" + size.Width + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
            }
            Size = new Size(1360, 860); await Task.Delay(250);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var h=document.getElementById('qa-credential-source'),p=h.querySelector(':scope>.univer-app-credentials'),o=h.querySelector('.univer-app-credential-original');return p&&p.getBoundingClientRect().height>0&&getComputedStyle(o).display==='none'&&p.querySelectorAll('.univer-app-credential-value')[1].textContent==='••••••••'&&p.querySelectorAll('.univer-app-credential-copy').length===2&&getComputedStyle(document.getElementById('qa-credential-padding')).display==='none'&&getComputedStyle(document.getElementById('qa-credential-below')).display==='none'?'protected':'';})()") == "protected",
                "Bachelor real tt layout masks the password, hides source pairs and trailing rows despite table CSS", results);
            await AutomationController.EvaluateAsync(main, "document.getElementById('qa-credential-source').scrollIntoView({block:'center'});'credential block'");
            await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var h=document.getElementById('qa-credential-source'),p=h.querySelector('.univer-app-credentials'),r=h.getBoundingClientRect(),v=p.querySelector('.univer-app-credential-label').getBoundingClientRect();return v.left-r.left>=17&&!document.querySelector('.univer-app-credential-note,.univer-app-quick-hint')?'spacing':''})()") == "spacing",
                "Bachelor credentials have a content inset and neither removed helper text returns", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "bachelor-credentials.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "window.scrollTo(0,0);'top'"); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var a=document.querySelector('.univer-app-tabs>a.active');return a&&[a].concat(Array.from(a.querySelectorAll('*'))).every(function(n){return getComputedStyle(n).color==='rgb(255, 255, 255)';})?'white':''})()") == "white",
                "active Bachelor tab keeps white nested text", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "bachelor.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var desk = shell + "<div class='ts'><a href='/ssc/UserServiceList'>Услуги</a><a href='/ssc/StatementList'>Заявки е-услуг</a></div><h2>Услуги</h2><ul><li>Логин и пароль для системы print.kaznu.kz и WiFi-сети: Логин: synthetic Пароль: synthetic</li></ul></td><td class='right'></td></tr></table></td></tr></table>";
            SetPresentation(main, true); main.LoadHtml(heading + desk, SitePolicy.Origin + "/ssc/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('a.univer-app-service-card').length===2&&document.querySelectorAll('.univer-app-credential-value')[1].textContent==='••••••••'&&document.querySelector('.univer-app-overview-requests-panel')?'desk':''") == "desk", "e-services desk shows cards, latest requests and a hidden password", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "eservices.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var services = shell + "<h2>Список услуг</h2><table class='inner'><tr><th>#</th><th>Услуга</th><th></th></tr>" + string.Join("", new[] { "Внешняя академическая мобильность", "Возврат из академического отпуска", "Воинский учёт", "Справка об обучении", "Справка для иностранцев" }.Select((n, i) => "<tr><td>" + (i + 1) + "</td><td>" + n + "</td><td><a href='/ssc/apply/" + i + "'>Подать заявку</a></td></tr>")) + "</table></td><td class='right'></td></tr></table></td></tr></table>";
            SetPresentation(main, true); main.LoadHtml(heading + services, SitePolicy.Origin + "/ssc/UserServiceList"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-ssc-nav a[aria-current=page]')&&document.querySelector('.univer-app-service-search')?'list':''") == "list", "service list has the services switch and a search", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "eservices-list.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            // A trusted press on a page select opens the app-drawn list. An earlier toast expiring would resize the page and close
            // the list, so the shell settles first.
            if (toast.Shown && messages.Current?.Key == toast.Key) { messages.Dismiss(); RenderFeedback(); }
            var pick = shell + "<p><select id='qa-week' onchange='window.qaWeek=this.value'><option value='1'>1 неделя</option><option value='2' selected>2 неделя</option><option value='3'>3 неделя</option></select></p></td><td class='right'></td></tr></table></td></tr></table>";
            Motion.Enabled = true; prefs.Animations = true;
            SetPresentation(main, true); main.LoadHtml(heading + pick, SitePolicy.Origin + "/student/myschedule/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(300);
            await AutomationController.EvaluateAsync(main, "window.qaDown=[];window.qaResized=0;addEventListener('resize',function(){window.qaResized++;});document.addEventListener('mousedown',function(e){window.qaDown.push(e.isTrusted+':'+e.target.tagName+':'+e.button);},true);'ok'");
            var at = (await AutomationController.EvaluateAsync(main, "(function(){var r=document.getElementById('qa-week').getBoundingClientRect();return Math.round(r.left+10)+','+Math.round(r.top+r.height/2);})()")).Split(',');
            int selectX = int.Parse(at[0]), selectY = int.Parse(at[1]);
            using (var client = main.GetDevToolsClient())
            {
                await PressQaPointAsync(client, selectX, selectY, "window.qaDown.length>0");
                File.WriteAllBytes(Path.Combine(qa, "select-open.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            }
            // The list is built inside the trusted mousedown itself, so it must be there as soon as the press is observed.
            var opened = await AutomationController.EvaluateAsync(main, "(function(){var l=document.querySelector('.univer-app-select');return window.qaDown[0]==='true:SELECT:0'&&l&&l.querySelectorAll('[role=option]').length===3&&l.querySelector('[aria-selected=true]').textContent==='2 неделя'?'list':JSON.stringify({down:window.qaDown,resized:window.qaResized,list:!!l})})()");
            if (opened != "list") QaTrace("select-open " + opened + " feedback=" + feedback.Visible + " toast=" + toast.Shown);
            Check(opened == "list", "page select opens the app-drawn list immediately", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var e=new KeyboardEvent('keydown',{key:'Tab',bubbles:true,cancelable:true});document.querySelector('.univer-app-select').dispatchEvent(e);return !e.defaultPrevented&&!document.querySelector('.univer-app-select')&&document.activeElement===document.getElementById('qa-week')?'tab':''})()") == "tab",
                "Tab closes the page list without trapping focus or changing the selected value", results);
            var presses = await AutomationController.EvaluateAsync(main, "String(window.qaDown.length)");
            using (var client = main.GetDevToolsClient()) await PressQaPointAsync(client, selectX, selectY, "window.qaDown.length>" + presses);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-select')&&/^true:SELECT:0$/.test(window.qaDown[window.qaDown.length-1])?'open':''") == "open",
                "trusted input reopens the list after Tab before selecting an option", results);
            await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-select [role=option]')[2].click();'chosen'"); await Task.Delay(200);
            Check(await AutomationController.EvaluateAsync(main, "window.qaWeek==='3'&&!document.querySelector('.univer-app-select')?'chosen':''") == "chosen", "choosing from the list fires the page's change handler and closes it", results);
            Motion.Enabled = false;
            SetPresentation(main, true); main.LoadHtml(heading + home, SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync();
            // The app preference controls pointer feedback; repeated navigation never animates.
            prefs.Animations = false; await ApplyPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "document.documentElement.classList.contains('univer-app-still')?'still':''") == "still", "animations setting reaches the page", results);
            prefs.Animations = true; await ApplyPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "!document.documentElement.classList.contains('univer-app-still')&&getComputedStyle(document.querySelector('.univer-app-quick>.univer-app-tile')).animationName==='none'?'ready':''") == "ready",
                "home tiles are immediately visible even with animations on", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-sections'),m=document.querySelector('.univer-app-menu');b.click();m.querySelector('input').focus();document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));return !m.open&&document.activeElement===b?'closed':''})()") == "closed",
                "Escape closes sections immediately and returns keyboard focus", results);
            using (var client = main.GetDevToolsClient())
            {
                // Emulate both states explicitly: the user's Windows setting may already request reduced motion.
                await client.Emulation.SetEmulatedMediaAsync(features: new List<CefSharp.DevTools.Emulation.MediaFeature> { new CefSharp.DevTools.Emulation.MediaFeature { Name = "prefers-reduced-motion", Value = "no-preference" } });
                await AutomationController.EvaluateAsync(main, "document.documentElement.classList.remove('univer-app-keyboard');'pointer'");
                Check(await AutomationController.EvaluateAsync(main, "getComputedStyle(document.querySelector('.univer-app-quick a')).transitionDuration==='0.18s'?'pointer':''") == "pointer",
                    "pointer press feedback is enabled before the reduced motion check", results);
                // Chromium 109 does not consistently route hover into an invisible QA window. Force the actual CSS state.
                await client.DOM.EnableAsync();
                await client.CSS.EnableAsync();
                var rootNode = (await client.DOM.GetDocumentAsync()).Root;
                var hoverNode = (await client.DOM.QuerySelectorAsync(rootNode.NodeId, ".univer-app-quick a")).NodeId;
                await client.CSS.ForcePseudoStateAsync(hoverNode, new[] { "hover" });
                await Task.Delay(220);
                Check(await AutomationController.EvaluateAsync(main, "getComputedStyle(document.querySelector('.univer-app-quick a')).transform==='matrix(1, 0, 0, 1, 0, -2)'?'hover':''") == "hover",
                    "pointer hover visibly lifts a home tile in full motion", results);
                Check(await AutomationController.EvaluateAsync(main, "(function(){document.documentElement.classList.add('univer-app-still');var still=getComputedStyle(document.querySelector('.univer-app-quick a')).transform==='none';document.documentElement.classList.remove('univer-app-still');return still?'still':''})()") == "still",
                    "animations off also disables hover travel", results);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var a=document.querySelector('.univer-app-tabs>a.active');return a&&getComputedStyle(a).backgroundColor==='rgb(27, 95, 166)'&&[a].concat(Array.from(a.querySelectorAll('*'))).every(function(n){return getComputedStyle(n).color==='rgb(255, 255, 255)';})?'white':''})()") == "white",
                    "blue active header tab and every nested label have white text", results);
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-menu').open=true;'open'");
                await Task.Delay(50);
                await AutomationController.EvaluateAsync(main, "(function(){var m=document.querySelector('.univer-app-menu');window.__univerMotionUI.stop();window.__univerMotionUI.reveal(m,'popup');window.qaPopupAnimation=m.getAnimations()[0];window.qaPopupAnimation.pause();window.qaPopupAnimation.currentTime=0;return 'start';})()");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var s=getComputedStyle(document.querySelector('.univer-app-menu'));return +s.opacity<.3&&s.transform!=='none'?'start':''})()") == "start",
                    "menu entrance has a real visible starting frame in Chromium", results);
                File.WriteAllBytes(Path.Combine(qa, "motion-menu-start.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "window.qaPopupAnimation.currentTime=90;'middle'");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var s=getComputedStyle(document.querySelector('.univer-app-menu'));return +s.opacity>.3&&+s.opacity<1&&s.transform!=='none'?'middle':''})()") == "middle",
                    "menu entrance interpolates opacity and transform in its middle frame", results);
                File.WriteAllBytes(Path.Combine(qa, "motion-menu-middle.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "window.qaPopupAnimation.currentTime=240;'end'");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var s=getComputedStyle(document.querySelector('.univer-app-menu'));return +s.opacity===1&&s.transform==='none'?'end':''})()") == "end",
                    "menu entrance settles without leaving a transform or opacity behind", results);
                File.WriteAllBytes(Path.Combine(qa, "motion-menu-end.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var m=document.querySelector('.univer-app-menu');window.__univerMotionUI.exit(m);m.open=false;var g=document.querySelector('[data-univer-motion-ghost]');window.qaGhost=g;window.qaGhostAnimation=g.getAnimations()[0];window.qaGhostAnimation.pause();window.qaGhostAnimation.currentTime=60;return !m.open&&g.getAttribute('aria-hidden')==='true'&&getComputedStyle(g).pointerEvents==='none'&&g.querySelector('[id],[name],[onclick]')===null?'exit':''})()") == "exit",
                    "menu closes its real controls immediately while a passive shrinking visual exits", results);
                File.WriteAllBytes(Path.Combine(qa, "fluid-menu-exit.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                Check(await AutomationController.EvaluateAsync(main, "window.__univerMotionUI.stop();document.querySelector('[data-univer-motion-ghost]')===null?'clean':''") == "clean", "cancelling an exit removes every passive popup snapshot", results);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var n=document.getElementById('l4');window.__univerMotionUI.zoom(n,.9);var a=n.getAnimations()[0];a.pause();a.currentTime=0;return getComputedStyle(n).transform==='matrix(0.9, 0, 0, 0.9, 0, 0)'&&a.effect.getTiming().duration===180?'zoom':''})()") == "zoom",
                    "pointer zoom has a real interpolated scale layer without changing the browser size per frame", results);
                File.WriteAllBytes(Path.Combine(qa, "fluid-zoom-start.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "window.__univerMotionUI.stop();'zoom-end'");
                await AutomationController.EvaluateAsync(main, "window.__univerMotionUI.stop();document.querySelector('.univer-app-menu').open=false;'closed'");
                Check(await AutomationController.EvaluateAsync(main, "(function(){window.__univerShortcutsV1.configure([]);document.documentElement.classList.remove('univer-app-keyboard');window.qaFirstTile=document.querySelector('.univer-app-quick a');window.qaFirstTile.focus();window.__univerShortcutsV1.configure([{title:'Синтетическая ссылка',url:'https://univer.kaznu.kz/my/messages/index/'}]);var p=document.querySelector('.univer-app-quick>.univer-app-pinned'),a=p.getAnimations()[0];a.pause();a.currentTime=0;return document.activeElement===window.qaFirstTile&&document.querySelector('.univer-app-quick a')===window.qaFirstTile&&+getComputedStyle(p).opacity<.3?'pin':''})()") == "pin",
                    "new pin has a visible entrance while existing tiles and focus remain mounted", results);
                File.WriteAllBytes(Path.Combine(qa, "motion-pin-start.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "window.__univerShortcutsV1.configure([]);'removed'");
                await client.Emulation.SetEmulatedMediaAsync(features: new List<CefSharp.DevTools.Emulation.MediaFeature> { new CefSharp.DevTools.Emulation.MediaFeature { Name = "prefers-reduced-motion", Value = "reduce" } });
                await Task.Delay(50);
                Check(await AutomationController.EvaluateAsync(main, "getComputedStyle(document.querySelector('.univer-app-quick a')).transitionDuration==='0s'?'reduced':''") == "reduced",
                    "Windows reduced motion removes pointer transitions", results);
                Check(await AutomationController.EvaluateAsync(main, "getComputedStyle(document.querySelector('.univer-app-quick a')).transform==='none'?'still':''") == "still",
                    "reduced motion also removes tile hover travel", results);
                await client.CSS.ForcePseudoStateAsync(hoverNode, new string[0]);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var m=document.querySelector('.univer-app-menu');m.open=true;window.__univerMotionUI.stop();window.__univerMotionUI.reveal(m,'popup');var a=m.getAnimations()[0];a.pause();a.currentTime=0;var s=getComputedStyle(m);return +s.opacity<1&&s.transform==='none'?'fade':''})()") == "fade",
                    "reduced motion retains a visible menu fade without travel", results);
                await AutomationController.EvaluateAsync(main, "window.__univerMotionUI.stop();document.querySelector('.univer-app-menu').open=false;'closed'");
                await client.Emulation.SetEmulatedMediaAsync(features: new List<CefSharp.DevTools.Emulation.MediaFeature>());
            }
            prefs.Pins = new List<PinnedLink>(); await RefreshDocumentScriptsAsync();
            // The page zoom also enlarges the sidebar and Downloads, and returns them to size.
            var sidebarBefore = sidebar.Width; var fontBefore = sideHome.Font.Size;
            SetZoom(150); ShowDownloads(); await Task.Delay(100);
            Check(sidebar.Width > sidebarBefore && sideHome.Font.Size > fontBefore && zoomIn.Visible, "zoom enlarges the sidebar and stays available on Downloads", results);
            SaveBitmap(this, Path.Combine(qa, "zoom-150.png"));
            SetZoom(100); HideDownloads(); await Task.Delay(100);
            Check(sidebar.Width == sidebarBefore && Math.Abs(sideHome.Font.Size - fontBefore) < 0.01f, "zoom back to 100% restores the sidebar", results);
            // The success toast floats over the page, is announced and leaves when its time is up.
            lastDownloadPath = Path.Combine(Path.GetTempPath(), "Синтетический файл.docx"); SetStatus("downloadComplete");
            Check(toast.Visible && toast.Shown && toast.Key == "downloadComplete" && !feedback.Visible && toast.Bottom <= workspace.ClientSize.Height, "download success appears as a toast, not a banner", results);
            QaSnapshot(Path.Combine(qa, "toast-ru.png"), toast.Width, toast.Height, bitmap => toast.DrawToBitmap(bitmap, new Rectangle(Point.Empty, toast.Size)));
            messages.Dismiss(); RenderFeedback();
            Check(!toast.Shown && messages.Current?.Key != "downloadComplete", "dismissed toast leaves", results);
            Motion.Enabled = true; Motion.KeyboardInput = true;
            toast.Show(); toast.Hide();
            Check(!toast.Visible && !toast.Shown, "keyboard dismissal of a toast is immediate", results);
            Motion.KeyboardInput = false; toast.Show(); await Task.Delay(240);
            var settledTop = toast.Top;
            toast.Hide(); await Task.Delay(45);
            Check(Motion.Reduced ? !toast.Visible : toast.Top >= settledTop && toast.Bottom <= workspace.ClientSize.Height,
                "toast leaves toward its entrance edge without clipping actions", results);
            toast.Show(); await Task.Delay(160);
            Check(toast.Visible && toast.Shown && toast.Enabled, "reopening a toast cancels the previous exit completion", results);
            Motion.KeyboardInput = true; toast.Hide(); Motion.Enabled = false;
            lastDownloadPath = null;
            Motion.Enabled = true; Motion.KeyboardInput = false;
            using (var noticeForm = new System.Windows.Forms.Form { StartPosition = System.Windows.Forms.FormStartPosition.Manual, Location = new Point(-30000, -30000), ClientSize = new Size(480, 260) })
            using (var transient = new ToastPanel(1, 300))
            {
                var anchor = new ModernButton { Text = "Fixture focus", Location = new Point(10, 10) }; noticeForm.Controls.Add(anchor); noticeForm.Controls.Add(transient);
                var noticeStatus = new UiStatus(); noticeStatus.Update("downloading");
                transient.Expired += () => { noticeStatus.Dismiss(); transient.Render(noticeStatus.Current, "", "", "", "", ""); };
                noticeForm.Show(this); anchor.Focus();
                for (var chunk = 0; chunk < 10; chunk++)
                {
                    transient.Render(noticeStatus.Current, "Загрузка", "Fixture " + chunk + "%", "Загрузки", "", "Закрыть");
                    await Task.Delay(45);
                }
                Check(noticeStatus.Current == null && !transient.Shown, "download progress toast expires while progress text keeps updating", results);
                noticeStatus.Update("downloadComplete"); transient.Render(noticeStatus.Current, "Готово", "Fixture complete", "Загрузки", "", "Закрыть");
                Check(transient.Shown, "completion creates a new notice after the progress notice expired", results);
                await Task.Delay(400);
                Check(noticeStatus.Current == null && !transient.Shown, "download completion toast expires through the actual native timer", results);
                noticeForm.Close();
            }
            foreach (var route in new[] { "/news/student", "/student/myschedule/", "/student/attendance/full/", "document" })
            {
                loading.SetRoute(route == "document" ? route : SitePolicy.Origin + route); loading.Visible = true; loading.BringToFront(); loading.Refresh();
                SaveBitmap(loading, Path.Combine(qa, "skeleton-" + (route == "document" ? "document" : route.Trim('/').Replace('/', '-')) + ".png"));
            }
            loading.Visible = false;
            Motion.Enabled = true; Motion.KeyboardInput = false;
            using (var fluidSettings = new SettingsForm(null, prefs))
            {
                fluidSettings.Show(this); await Task.Delay(250);
                Check(!DescendantsForMotionQa(fluidSettings).OfType<MotionSurface>().Any(), "settings entrance has no stale bitmap overlay", results);
                MotionSurface.Stop(fluidSettings); fluidSettings.SelectAppearanceForQa();
                var surface = DescendantsForMotionQa(fluidSettings).OfType<MotionSurface>().FirstOrDefault();
                Check(Motion.Reduced ? surface == null : surface != null && !surface.TabStop && surface.Parent != null, "settings tab moves live controls only when Windows allows motion", results);
                if (surface != null)
                {
                    surface.SampleForQa(0, false); SaveNativeFrameForMotionQa(fluidSettings, Path.Combine(qa, "fluid-settings-start.png"));
                    surface.SampleForQa(.6f, false); SaveNativeFrameForMotionQa(fluidSettings, Path.Combine(qa, "fluid-settings-middle.png"));
                    surface.SampleForQa(1, false); await Task.Delay(250); SaveNativeFrameForMotionQa(fluidSettings, Path.Combine(qa, "fluid-settings-end.png"));
                }
                fluidSettings.SaveButton.Focus();
                Check(fluidSettings.SaveButton.Focused, "live settings controls keep focus during a visual transition", results);
                using (var nativeMenu = new System.Windows.Forms.ContextMenuStrip { Font = Theme.Font(Theme.Body) })
                {
                    var clicked = 0; var action = nativeMenu.Items.Add("Синтетическое действие"); action.Click += (s, e) => clicked++;
                    Motion.FadeIn(nativeMenu); Motion.KeyboardInput = false;
                    nativeMenu.Show(fluidSettings, new Point(80, 110)); await Task.Delay(20);
                    Check(nativeMenu.Visible && (Motion.Reduced || nativeMenu.Region != null), "native menu remains active while its native region grows from its trigger", results);
                    action.PerformClick(); nativeMenu.Close(System.Windows.Forms.ToolStripDropDownCloseReason.ItemClicked);
                    Check(clicked == 1 && !nativeMenu.Visible && MotionPopup.ActiveCount > 0, "native menu command executes once while its passive layered HWND exits", results);
                    nativeMenu.Show(fluidSettings, new Point(80, 110)); await Task.Delay(210);
                    // A loaded machine (a 32-bit run beside other work) can finish the cancelled exit a few frames later.
                    for (var until = DateTime.UtcNow.AddSeconds(1); MotionPopup.ActiveCount != 0 && DateTime.UtcNow < until;) await Task.Delay(20);
                    if (MotionPopup.ActiveCount != 0 || !nativeMenu.Visible) QaTrace("qa-menu-reopen active=" + MotionPopup.ActiveCount + " visible=" + nativeMenu.Visible);
                    Check(nativeMenu.Visible && MotionPopup.ActiveCount == 0, "native menu reopen cancels an older exit and leaves no passive window", results);
                    Motion.KeyboardInput = true; nativeMenu.Close(System.Windows.Forms.ToolStripDropDownCloseReason.Keyboard);
                    Check(!nativeMenu.Visible && MotionPopup.ActiveCount == 0, "keyboard dismissal of native menus leaves no animation window", results);
                }
                Motion.KeyboardInput = false;
                // The fade lasts 220 ms; a busy machine may deliver its last frames late, so wait for the end within a bound.
                MotionSurface.StopTree(fluidSettings); fluidSettings.Close();
                for (var fadeEnd = DateTime.UtcNow.AddMilliseconds(1000); !fluidSettings.IsDisposed && fluidSettings.Visible && DateTime.UtcNow < fadeEnd;) await Task.Delay(40);
                Check(fluidSettings.IsDisposed || !fluidSettings.Visible, "pointer settings close completes its bounded window fade", results);
            }
            var previousTray = trayIcon; var previousHint = trayHintShown; var previousCloseCommand = closeCommand; var previousTrayPreference = prefs.Tray;
            var previousVisible = Visible;
            using (var isolatedTray = new System.Windows.Forms.NotifyIcon { Visible = false })
            {
                trayIcon = isolatedTray; trayHintShown = true; closeCommand = true; prefs.Tray = true;
                try
                {
                    Motion.KeyboardInput = false;
                    var closingArgs = new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.UserClosing, false);
                    Check(HideToTray(closingArgs) && closingArgs.Cancel, "animated tray hide preserves the existing close-command and session policy", results);
                    RestoreFromTray(); await Task.Delay(260);
                    Check(Visible && Opacity == 1 && WindowState != System.Windows.Forms.FormWindowState.Minimized, "restore during tray motion cancels the pending hide and restores the window state", results);
                    HideToTray(new System.Windows.Forms.FormClosingEventArgs(System.Windows.Forms.CloseReason.UserClosing, false)); await Task.Delay(260);
                    Check(!Visible && Opacity == 1, "tray transition completes with no invisible foreground window", results);
                    RestoreFromTray();
                }
                finally { trayIcon = previousTray; trayHintShown = previousHint; closeCommand = previousCloseCommand; prefs.Tray = previousTrayPreference; if (!previousVisible) Hide(); }
            }
            Motion.Enabled = false; Motion.KeyboardInput = true;
        }
        private static IEnumerable<System.Windows.Forms.Control> DescendantsForMotionQa(System.Windows.Forms.Control root)
        {
            foreach (System.Windows.Forms.Control child in root.Controls)
            { yield return child; foreach (var descendant in DescendantsForMotionQa(child)) yield return descendant; }
        }
        // A failed PrintWindow still fails the run; only GDI+ trouble with the picture itself is tolerated (QaSnapshot).
        private static void SaveNativeFrameForMotionQa(System.Windows.Forms.Control control, string path)
        {
            QaSnapshot(path, control.ClientSize.Width, control.ClientSize.Height, bitmap =>
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var device = graphics.GetHdc();
                    try { if (!PrintWindow(control.Handle, device, 3)) throw new InvalidOperationException("Native fixture PrintWindow failed"); }
                    finally { graphics.ReleaseHdc(device); }
                }
            });
        }
        // A trusted left press at a page point. DevTools input is preferred; Chromium 109 may not route it to the hidden test
        // window, so the host's own click (trusted too) follows only when the page has not observed the press in time.
        private async Task PressQaPointAsync(CefSharp.DevTools.DevToolsClient client, int x, int y, string observed)
        {
            foreach (var type in new[] { CefSharp.DevTools.Input.DispatchMouseEventType.MousePressed, CefSharp.DevTools.Input.DispatchMouseEventType.MouseReleased })
                await client.Input.DispatchMouseEventAsync(type, x, y, button: CefSharp.DevTools.Input.MouseButton.Left, clickCount: 1);
            if (await WaitQaScriptAsync(observed, 1000)) return;
            var host = main.GetBrowser().GetHost();
            host.SendMouseMoveEvent(new MouseEvent(x, y, CefEventFlags.None), false);
            host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.LeftMouseButton), MouseButtonType.Left, false, 1);
            host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.None), MouseButtonType.Left, true, 1);
            await WaitQaScriptAsync(observed, 2000);
        }
        private async Task<bool> WaitQaScriptAsync(string condition, int milliseconds)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            do
            {
                if (await AutomationController.EvaluateAsync(main, "(" + condition + ")?'yes':''") == "yes") return true;
                await Task.Delay(50);
            }
            while (DateTime.UtcNow < until);
            return false;
        }
    }
}
