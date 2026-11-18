using System;
using System.Collections.Generic;
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
        // The schedule, the home page, the UMKD course list, course pages and printed syllabi as observed on
        // 11 November 2026, with synthetic ids, names, programme codes and topics.
        private const string SyllabusMine = "6B01234", SyllabusOther = "6B09999";
        private const string SyllabusLong = "Подготовить письменный разбор источников по теме синтетического курса, сравнить подходы разных авторов и записать выводы в таблицу. " +
            "Для каждого источника указать год, основную мысль и возражения, которые к ней выдвигались. Затем выбрать два противоположных взгляда и показать, на каких данных они построены. " +
            "В конце дать собственную оценку и подготовить устное сообщение на пять минут с тремя вопросами к группе.";
        private static string SyllabusEntry(string type, string name, string topic, string score, bool rubric = false)
        {
            return "<tr><td><div><div><div>" + type + ":</div><div><div>" + name + "</div></div></div><div><div><div><div>" + topic + "</div></div></div></div><div><div>" + score + "</div></div>" +
                (rubric ? "<div><b>Рубрикатор критериального оценивания</b>[<a href='' class='toggle-link'>Показать</a>]<div class='rubricator-content'><table><tbody><tr><td class='it'>9</td><td>Критерий</td></tr></tbody></table></div></div>" : "") + "</div></td></tr>";
        }
        private static string SyllabusWeek(int week, params string[] rows) { return "<tr><td class='it'>" + week + "</td><td colspan='3'><table class='full-width'><tbody>" + string.Join("", rows) + "</tbody></table></td></tr>"; }
        private static string SyllabusGroup(string code, string division = "русское")
        {
            return "Группа №: 1000001 (Образовательная программа: " + code + " Синтетическая программа, Уровень обучения: Первое высшее образование, Семестр: 1, Курс обучения: 1, Языковое отделение: " + division + ")";
        }
        private static string SyllabusFixture(string year, string groups, string lecturer, params string[] weeks)
        {
            return "<div class='noprint'><a href='/student/interactive/syllabus/list/'>[Назад]</a></div><div id='content'><table class='students'><tbody>" +
                "<tr><td class='label'>Автор:</td><td class='text'>Образцов О. О.</td></tr><tr><td class='label'>Академический год</td><td class='text'>" + year + "</td></tr>" +
                "<tr><td class='label'>Образовательная программа</td><td class='text'>" + groups + ";</td></tr>" +
                "<tr><td class='label'>Лектор-(ы):</td><td class='text'><div class='input-row'><div class='input-field'><span>" + lecturer + "</span></div></div></td></tr></tbody></table>" +
                "<div class='table-scroll'><table class='students'><thead><tr><td class='centered'><label>Неделя</label></td><td class='centered'><label>Тип занятия</label></td><td class='centered'><label>Тема</label></td><td class='centered'><label>Максимальный балл</label></td></tr></thead>" +
                "<tbody><tr><td colspan='4'><h1>МОДУЛЬ 1 Синтетический модуль</h1></td></tr>" + string.Join("", weeks) + "</tbody></table></div></div>";
        }
        private static string SyllabusCoursePage(params string[] links)
        {
            var list = "";
            for (var i = 0; i + 1 < links.Length; i += 2) list += "<a href='/student/interactive/syllabus/print/" + links[i] + "/2'>" + links[i + 1] + "</a>";
            return StudyPage(Panel(Row("<td class='tt'><div class='inner'><div><h4>Силлабус</h4>" + list + "</div><table class='inner'><tr><th></th><th>Название файла</th></tr></table></div></td>")));
        }
        private static string SyllabusLesson(string name, string type, string teacher)
        {
            return "<td class='field'><div class='groups'><div><p class='teacher'>" + name + "(" + type + ")</p><p class='teacher'>" + teacher + "</p><p class='params'><span class='aud_faculty'>ФИТ</span><span>Ауд.: 309</span></p></div></div></td>";
        }
        // The address of a week's own page in the site's week list; week 4 is the current one.
        private static string SyllabusWeekRoute(int week, out string label)
        {
            var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7) + 7 * (week - 4));
            Func<int, string> date = offset => monday.AddDays(offset).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
            label = "Неделя 0" + week + ". " + date(0) + " - " + date(6);
            return "/student/myschedule/2026/1/" + date(0) + "/" + date(6) + "/";
        }
        // Weeks 3 to 5 of one timetable. The group has its Culture seminar in week 4 only, and the syllabus writes it for week 5.
        internal static string SyllabusScheduleFixture(int week = 4)
        {
            var empty = "<td class='field'></td>"; var options = "";
            for (var number = 3; number <= 5; number++) { string label; var route = SyllabusWeekRoute(number, out label); options += "<option value='" + route + "'" + (number == week ? " selected" : "") + ">" + label + "</option>"; }
            Func<string, string, string> row = (time, cells) => "<tr><td class='time'><div class='divIE' style='display:none'>" + time + "</div></td>" + cells + "</tr>";
            return "<table class='wrappertable'><tr id='l4'><td class='center'><select id='weekSelect'>" + options + "</select>" +
                "<div class='inner'><table class='schedule'><tr><th>&nbsp;</th><th>ПН</th><th>ВТ</th><th>СР</th><th>ЧТ</th><th>ПТ</th><th>СБ</th></tr>" +
                row("09:00-09:50", SyllabusLesson("Политология", "лекция", "Учителева А. П.") + SyllabusLesson("Культурология", "лекция", "Незнакомов Н. Н.") + SyllabusLesson("Политология", "семинар", "Учителева А. П.") +
                    (week == 4 ? SyllabusLesson("Культурология", "семинар", "Незнакомов Н. Н.") : empty) + empty + empty) +
                row("10:00-10:50", SyllabusLesson("Политология", "семинар", "Помощников П. П.") + SyllabusLesson("Иностранный язык", "семинар", "Языкова Я. Я.") + SyllabusLesson("Политология", "семинар", "Учителева А. П.") + empty + empty + empty) +
                "</table></div></td></tr></table>";
        }
        // The pages behind the schedule, served in place of the network for the adapter's background reads.
        private void RegisterSyllabusPages()
        {
            var factory = main.ResourceRequestHandlerFactory as ResourceRequestHandlerFactory;
            if (factory == null) return;
            Action<string, string> page = (route, html) => factory.RegisterHandler(SitePolicy.Origin + route, ResourceHandler.GetByteArray("<!doctype html><meta charset='utf-8'>" + html, Encoding.UTF8), "text/html", false);
            page("/student/bachelor/", StudyPage(Panel(Row("<td class='tt'><div class='inner'><table class='inner'><tr><td><li>Форма обучения: </li></td><td>очная</td><td><li>Отделение: </li></td><td>русское</td></tr>" +
                "<tr><td><li>Факультет: </li></td><td>Синтетический факультет</td><td><li>Специальность: </li></td><td>" + SyllabusMine + " Синтетическая безопасность</td></tr></table></div></td>"))));
            var names = new[] { "Политология", "Культурология", "Иностранный язык", "История Казахстана" };
            var rows = "";
            for (var i = 0; i < names.Length; i++) rows += "<tr class='link' id='" + (7001 + i) + "'><td><img src='/img/folder.png'></td><td>" + names[i] + "</td><td>Обязательная</td></tr>";
            page("/student/umkd/", StudyPage(Panel(Row("<td class='tt'><div class='inner'><table class='inner'><tr><th></th><th>Дисциплина</th><th>Тип</th></tr>" + rows + "</table></div></td>"))) +
                "<script>document.querySelectorAll('tr.link').forEach(function(r){r.addEventListener('dblclick',function(){window.location = \"/student/umkd/\" + this.id + \"/2026/1\";});});</script>");
            page("/student/umkd/7001/2026/1", SyllabusCoursePage("9001", "Политология (другой факультет)", "9002", "Политология 25-26", "9004", "Политология (Осень 2026)", "9003", "Политология каз. отделение"));
            page("/student/umkd/7002/2026/1", SyllabusCoursePage("9010", "Культурология"));
            page("/student/umkd/7003/2026/1", SyllabusCoursePage("9020", "ИС_Ин.яз_Юриспруденция"));
            page("/student/umkd/7004/2026/1", SyllabusCoursePage("9030", "История Казахстана"));
            var print = "/student/interactive/syllabus/print/";
            page(print + "9001/2", SyllabusFixture("2026", SyllabusGroup(SyllabusOther), "Чужой Ч. Ч.", SyllabusWeek(4, SyllabusEntry("лекция", "лекция 4", "Чужая тема", "1"))));
            page(print + "9002/2", SyllabusFixture("2025", SyllabusGroup(SyllabusMine), "Учителева Анна Петровна", SyllabusWeek(4, SyllabusEntry("лекция", "лекция 4", "Прошлогодняя тема", "1"))));
            page(print + "9003/2", SyllabusFixture("2026", SyllabusGroup(SyllabusMine, "казахское"), "Образцов О. О.", SyllabusWeek(4, SyllabusEntry("лекция", "лекция 4", "Тема казахского отделения", "1"))));
            page(print + "9004/2", SyllabusFixture("2026", SyllabusGroup(SyllabusOther) + " " + SyllabusGroup(SyllabusMine), "Учителева Анна Петровна",
                SyllabusWeek(4, SyllabusEntry("лекция", "лекция 4", "Политическая власть и её источники", "1"), SyllabusEntry("семинар", "семинар 4", SyllabusLong, "7"),
                    SyllabusEntry("СРО/СРОП", "СРОП 2", "СРОП 2. Консультация по выполнению СРО 1", "0", true))));
            page(print + "9010/2", SyllabusFixture("2026", SyllabusGroup(SyllabusMine), "Образцов О. О.",
                SyllabusWeek(4, SyllabusEntry("лекция", "лекция 4", "Культура как система знаков", "0"), SyllabusEntry("СРО/СРОП", "СРО/СРОП 4", "Консультация по выполнению СРС 1 «Символы культуры»", "0", true),
                    SyllabusEntry("СРО/СРОП", "СРО/СРОП 4", "Прием СРС 1 «Символы культуры»", "22", true)),
                SyllabusWeek(5, SyllabusEntry("семинар", "семинар 5", "Семинар о знаках культуры", "6"))));
            page(print + "9020/2", SyllabusFixture("2026", SyllabusGroup(SyllabusOther), "Языкова Я. Я.", SyllabusWeek(4, SyllabusEntry("семинар", "семинар 4", "Чужой семинар", "5"))));
            page(print + "9030/2", SyllabusFixture("2026", SyllabusGroup(SyllabusMine), "Образцов О. О.",
                SyllabusWeek(4, SyllabusEntry("лекция", "лекция 4", "Тюркская цивилизация", "0"), SyllabusEntry("СРО/СРОП", "СРО 1", "Контрольная работа. Письменно.", "15", true), SyllabusEntry("СРО/СРОП", "СРОП 3", "Разъяснение замечаний по СРО", "0"))));
            // Every week of the site's week list, read to learn in which weeks the group has each class.
            for (var week = 3; week <= 5; week++) page(SyllabusWeekRoute(week, out _), SyllabusScheduleFixture(week));
        }
        // Native checks: the background reads through the real request path, the topic under each class, the week's
        // independent work, the limits and the buttons as Chromium lays them out, narrow width and Original.
        private async Task SyllabusQaAsync(string qa, List<string> results, string heading)
        {
            RegisterSyllabusPages();
            main.LoadHtml(heading + SyllabusScheduleFixture(), SitePolicy.Origin + "/student/myschedule/"); await WaitQaPresentationAsync();
            await AutomationController.EvaluateAsync(main, "localStorage.removeItem('univer.desktop.syllabus.v1');localStorage.removeItem('univer.desktop.syllabus-ui.v1');'clear'");
            prefs.Readable = false; await ApplyPresentationAsync(); prefs.Readable = true; await ApplyPresentationAsync();
            var ready = await WaitForAsync("(function(){var s=window.__univerSyllabusV1&&window.__univerSyllabusV1.state();if(!s||s.model!=='ready'||s.weeks==='loading'||!s.jobs.length||s.jobs.some(function(j){return j.status==='queued'||j.status==='loading'}))return '';" +
                "if(window.qaSyllabusMotion===undefined)window.qaSyllabusMotion=window.__univerMotion;window.__univerMotion=false;var all=Array.from(document.querySelectorAll('#l4 .univer-app-days button')).pop();if(all&&all.getAttribute('aria-pressed')!=='true')all.click();return document.querySelector('#l4 .univer-app-syl-task[data-state=ready]')?'ready':''})()", 12000);
            var lesson = "var lesson=function(name,type,n){return Array.from(document.querySelectorAll('#l4 .univer-app-lesson')).filter(function(c){return c.querySelector('.univer-app-lesson-name').textContent===name&&c.querySelector('.univer-app-lesson-type').textContent.toLowerCase()===type})[n||0]};" +
                "var topics=function(c){return Array.from(c.querySelectorAll('.univer-app-syl-topic')).map(function(t){return t.textContent}).join('|')};";
            Check(ready == "ready" && await AutomationController.EvaluateAsync(main, "(function(){" + lesson + "var s=window.__univerSyllabusV1.state().jobs.map(function(j){return j.status}).join(',');" +
                "var foreign=lesson('Иностранный язык','семинар').querySelector('.univer-app-syl-task'),source=lesson('Политология','лекция').querySelector('.univer-app-syl-source');" +
                "var notes=Array.from(document.querySelectorAll('#l4 .univer-app-syl-note')).map(function(n){return n.textContent}).join('|');" +
                "return topics(lesson('Политология','лекция'))==='Политическая власть и её источники'&&topics(lesson('Культурология','лекция'))==='Культура как система знаков'&&!foreign&&" +
                "source.getAttribute('href')==='/student/interactive/syllabus/print/9004/2'&&s==='ready,ready,unmatched,ready'&&notes===''&&!document.querySelector('#l4 .univer-app-syl-week')&&!document.querySelector('#l4 .univer-app-syl-label')?'topics':JSON.stringify({s:s,notes:notes,lecture:topics(lesson('Политология','лекция'))})})()") == "topics",
                "each class shows the topic of its week and kind from the syllabus of the student's own programme, year and division", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var rows=Array.from(document.querySelectorAll('#l4 .univer-app-syl-item')).map(function(r){return r.querySelector('.univer-app-syl-course').textContent+'/'+r.querySelector('.univer-app-syl-meta').textContent+'/'+r.querySelector('.univer-app-syl-topic').textContent}).join('|');" +
                "var text=document.querySelector('#l4 .univer-app-schedule').textContent;" +
                "return rows==='Культурология/макс. балл: 22/Прием СРС 1 «Символы культуры»|История Казахстана/СРО 1 · макс. балл: 15/Контрольная работа. Письменно.'&&!/Консультация|Разъяснение|СРОП/.test(text)&&document.querySelector('#l4 .univer-app-syl-count').textContent==='2'?'sro':rows})()") == "sro",
                "the week's independent work is listed by course, with courses that have no class this week and without consultations", results);
            // The other weeks come through the real request path; the seminar written for week 5 is the group's only one, held in week 4.
            Check(await AutomationController.EvaluateAsync(main, "(function(){" + lesson + "var held=lesson('Культурология','семинар'),names=Array.from(held.querySelectorAll('.univer-app-syl-entry-name')).map(function(n){return n.textContent}).join('|');" +
                "var notes=Array.from(document.querySelectorAll('#l4 .univer-app-syl-note')).map(function(n){return n.textContent}).join('|');" +
                "return topics(held)==='Семинар о знаках культуры|Прием СРС 1 «Символы культуры»'&&names==='семинар 5 · макс. балл: 6|СРО · макс. балл: 22'&&" +
                "notes===''&&window.__univerSyllabusV1.state().weeks==='ready'&&topics(lesson('Политология','семинар')).length>100?'order':JSON.stringify({topics:topics(held),names:names,notes:notes,weeks:window.__univerSyllabusV1.state().weeks})})()") == "order",
                "a seminar the group has in another week than the syllabus takes its topic by the order of classes, with the week's work named beside it", results);
            // Real layout: hidden controls take no room, a long topic stays within three lines until it is opened.
            Check(await AutomationController.EvaluateAsync(main, "(function(){" + lesson + "var seminar=lesson('Политология','семинар'),topic=seminar.querySelector('.univer-app-syl-topic'),more=seminar.querySelector('.univer-app-syl-more');" +
                "var line=parseFloat(getComputedStyle(topic).lineHeight),folded=topic.getBoundingClientRect().height,cut=topic.textContent.length;more.click();var open=topic.getBoundingClientRect().height,full=topic.textContent.length,label=more.textContent;more.click();" +
                "var short=lesson('Культурология','лекция').querySelector('.univer-app-syl-more'),hidden=Array.from(document.querySelectorAll('#l4 .univer-app-syl-panel [hidden],#l4 .univer-app-syl-task [hidden]')).filter(function(n){return n.getClientRects().length}).length;" +
                "return folded<=line*3+1&&cut<=" + 181 + "&&open>folded+line&&full>300&&label==='Свернуть'&&topic.getBoundingClientRect().height===folded&&more.getClientRects().length&&!short.getClientRects().length&&hidden===0?'limits':JSON.stringify({folded:folded,line:line,cut:cut,open:open,full:full,hidden:hidden})})()") == "limits",
                "a long topic is cut to 180 characters and three lines until its button opens it, and hidden buttons take no room", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){" + lesson + "var twin=lesson('Политология','семинар',2),first=lesson('Политология','семинар',1);" +
                "return first.querySelector('.univer-app-syl-topic')&&twin.querySelector('.univer-app-syl-task').dataset.state==='same'&&twin.querySelector('.univer-app-syl-same').textContent==='та же тема, что в 09:00'&&!twin.querySelector('.univer-app-syl-topic')?'same':''})()") == "same",
                "the second class of a double period names the first instead of repeating its topic", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var panel=document.querySelector('#l4 .univer-app-syl-panel'),box=panel.getBoundingClientRect(),bad=[];" +
                "Array.from(panel.querySelectorAll('button,a')).concat(Array.from(document.querySelectorAll('#l4 .univer-app-syl-task button,#l4 .univer-app-syl-task a'))).forEach(function(b){if(!b.getClientRects().length)return;var r=b.getBoundingClientRect(),big=b.matches('.univer-app-syl-switch,.univer-app-syl-refresh,.univer-app-syl-panel .univer-app-syl-fold');" +
                "if(b.matches('.univer-app-syl-course'))return;if(r.height<(big?40:28)-.5||(big&&b.scrollWidth>b.clientWidth+1))bad.push(b.className+':'+Math.round(r.height));});" +
                "var fold=document.querySelector('#l4 .univer-app-syl-panel .univer-app-syl-fold'),list=document.querySelector('#l4 .univer-app-syl-list');" +
                "return !bad.length&&Math.abs(fold.getBoundingClientRect().left-list.getBoundingClientRect().left)<=1&&document.documentElement.scrollWidth<=innerWidth+1&&box.width>400?'controls':JSON.stringify(bad)})()") == "controls",
                "the panel's controls keep the site's control height, their words unbroken, and line up with the list", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "syllabus-schedule.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Check(await AutomationController.EvaluateAsync(main, "(function(){" + lesson + "var visible=function(){return Array.from(document.querySelectorAll('#l4 .univer-app-syl-task')).filter(function(t){return t.getClientRects().length}).length};" +
                "var fold=lesson('Политология','лекция').querySelector('.univer-app-syl-fold');fold.click();var body=lesson('Политология','лекция').querySelector('.univer-app-syl-body'),folded=!body.getClientRects().length&&lesson('Политология','лекция').querySelector('.univer-app-syl-fold').getAttribute('aria-expanded')==='false';" +
                "var other=!!lesson('Политология','семинар').querySelector('.univer-app-syl-body').getClientRects().length,before=visible(),toggle=document.querySelector('#l4 .univer-app-syl-switch');toggle.click();var off=visible(),checked=toggle.getAttribute('aria-checked');" +
                "var sro=document.querySelector('#l4 .univer-app-syl-panel .univer-app-syl-fold');sro.click();var list=!document.querySelector('#l4 .univer-app-syl-sro-body').getClientRects().length;" +
                "var saved=JSON.parse(localStorage.getItem('univer.desktop.syllabus-ui.v1'));toggle.click();sro.click();lesson('Политология','лекция').querySelector('.univer-app-syl-fold').click();" +
                "return folded&&other&&before>=4&&off===0&&checked==='false'&&list&&saved.lessons===false&&saved.sro===false&&Object.keys(saved.folded).length===1&&visible()===before?'buttons':JSON.stringify({folded:folded,other:other,before:before,off:off,list:list,saved:saved})})()") == "buttons",
                "a task folds on its own button, the switch hides every task, the week's list folds, and the choices are remembered", results);
            Width = 900; await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var p=document.querySelector('#l4 .univer-app-syl-panel').getBoundingClientRect(),inside=Array.from(document.querySelectorAll('#l4 .univer-app-syl-panel *,#l4 .univer-app-syl-task *')).every(function(n){var r=n.getBoundingClientRect();return !r.width||r.right<=innerWidth+1});" +
                "return document.documentElement.scrollWidth<=innerWidth+1&&p.right<=innerWidth&&inside?'narrow':''})()") == "narrow", "narrow schedule keeps the syllabus panel and the tasks inside the window", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "syllabus-schedule-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 1360;
            // A very wide window, emulated so the check does not depend on the screen.
            using (var client = main.GetDevToolsClient())
            {
                await client.Emulation.SetDeviceMetricsOverrideAsync(2200, 1100, 1, false); await Task.Delay(300);
                var wide = await AutomationController.EvaluateAsync(main, "(function(){var view=document.querySelector('#l4 .univer-app-schedule'),tabs=Array.from(view.querySelectorAll('.univer-app-days button')),box=function(n){return n.getBoundingClientRect()};" +
                    "var all=tabs.filter(function(b){return b.getAttribute('aria-pressed')==='true'})[0]||tabs[tabs.length-1],other=tabs.filter(function(b){return b!==all})[0];all.click();var days=Array.from(view.querySelectorAll('.univer-app-day')).filter(function(d){return d.getClientRects().length}),w=days.map(function(d){return box(d).width});" +
                    "var even=days.length===6&&w.every(function(x){return Math.abs(x-w[0])<=1})&&w[0]>300&&Math.abs(box(days[0]).left-box(view).left)<=1&&Math.abs(box(days[5]).right-box(view).right)<=1&&days.every(function(d){return Math.abs(box(d).top-box(days[0]).top)<=1});" +
                    "var full=box(view).width,mid=function(n){return (box(n).left+box(n).right)/2},rest=Array.from(view.children).filter(function(n){return !n.classList.contains('univer-app-day')&&n.getClientRects().length});" +
                    "var column=rest.length>=3&&rest.every(function(n){return box(n).width<=1401})&&rest.filter(function(n){return box(n).width>1300}).every(function(n){return Math.abs(mid(n)-mid(view))<=1})&&rest.every(function(n){return Math.abs(box(n).left-box(rest[0]).left)<=1});other.click();var one=box(view).width,day=Array.from(view.querySelectorAll('.univer-app-day')).filter(function(d){return d.getClientRects().length});" +
                    "var single=day.length===1&&one<=1401&&box(day[0]).width<=1401&&Math.abs(box(day[0]).left-box(view).left)<=1&&Math.abs(mid(view)-mid(view.parentElement))<=1&&Math.abs(box(rest[0]).left-box(view).left)<=1;all.click();" +
                    "return even&&single&&column&&full>1700?'wide':JSON.stringify({column:column,rest:rest.map(function(n){return [n.className,box(n).left,box(n).width]}),w:w,full:full,one:one,days:days.length,even:even,single:single,shown:day.length})})()");
                QaTrace("syllabus-wide " + wide);
                File.WriteAllBytes(Path.Combine(qa, "syllabus-schedule-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await client.Emulation.ClearDeviceMetricsOverrideAsync(); await Task.Delay(200);
                Check(wide == "wide", "in a very wide window the six days share the whole width equally, while the panels, the tabs and one day alone keep to one centred page column", results);
            }
            prefs.Readable = false; await ApplyPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('[class*=univer-app-syl],#univer-syllabus-style,[data-syllabus]')&&document.querySelector('table.schedule').getClientRects().length?'restored':''") == "restored",
                "Original view removes the syllabus panel, the tasks and their style", results);
            prefs.Readable = true; await ApplyPresentationAsync();
            await AutomationController.EvaluateAsync(main, "localStorage.removeItem('univer.desktop.syllabus.v1');localStorage.removeItem('univer.desktop.syllabus-ui.v1');window.__univerMotion=window.qaSyllabusMotion;'clear'");
        }
    }
}
