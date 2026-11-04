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
        // Distance courses as observed on 29 October 2026: course list, curriculum, weeks, a week's task list and a task,
        // with synthetic ids, names and text. Each page carries the route template its own double-click handler uses.
        private const string CourseRoute = "/student/discussion/";
        private static string Day(int offset) { return DateTime.Today.AddDays(offset).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture); }
        private static string Handler(string script) { return "<script>document.querySelectorAll('tr.link').forEach(function(r){r.addEventListener('dblclick',function(){" + script + "});});</script>"; }
        internal static string CourseIndexFixture()
        {
            var names = new[] { "История Казахстана (0)", "Политология (0)", "Социология (2)" };
            var rows = "";
            for (var i = 0; i < names.Length; i++) rows += "<tr class='link' id='" + (7101 + i) + "'><td><img src='/img/folder.png'></td><td>" + names[i] + "</td></tr>";
            return StudyPage(Panel(Row("<td class='ct'><span><a>Назад</a> / <a href='/student/discussion/index/2026/1/'>Список дисциплин 2026-2027 год Осенний Семестр</a></span></td>", "top") +
                Row("<td class='ts'><a class='lt' id='subjectShow_btn' href='#'><img src='/img/door-in-icon.png'>Открыть дисциплину</a></td>") +
                Row("<td class='ct' style='text-align:center'>2026-2027 год Осенний Семестр</td>", "brk") +
                Row("<td class='tt'><div class='inner'><table class='inner' onselectstart='return false' onmousedown='return false'><tr><th width='10%'></th><th>Дисциплина</th></tr>" + rows + "</table></div></td>"))) +
                Handler("if(this.id==='other'){window.location='/student/discussion/acpos/list/other/2026/1';}else{window.location=\"/student/discussion/acpos/list/\" + this.id + \"/2026/1/\";}");
        }
        private static string CoursePlansFixture(int subject)
        {
            return StudyPage(Panel(Row("<td class='ct'><span><a href='/student/discussion/index/2026/1/'>Список дисциплин 2026-2027 год Осенний Семестр</a></span></td>", "top") +
                Row("<td class='ts'><a class='lt' id='specialityOpen_btn' href='#'><img src='/img/door-in-icon.png'>Открыть специальность</a></td>") +
                Row("<td class='tt'><div class='inner'><table class='inner'><tr><th></th><th>Специальность</th><th>Форма обучения</th><th>Курс</th></tr>" +
                    "<tr class='link' id='5" + subject + "'><td><img src='/img/folder.png'></td><td>6B00000-Синтетическая специальность (0)</td><td>очная</td><td>1</td></tr></table></div></td>"))) +
                Handler("window.location = '/student/discussion/acpos/week/list/' + this.id + '/" + subject + "/2026/1/';");
        }
        private static string CourseWeeksFixture(int subject, int tasksInCurrentWeek)
        {
            var rows = "";
            for (var week = 1; week <= 15; week++)
            {
                var start = (week - 9) * 7 - 2;
                rows += "<tr class='link" + (week == 9 ? " normal" : "") + "' id='" + week + "'><td><img src='/img/folder.png'></td><td>" + week + "</td><td>" + Day(start) + " 00:00-" + Day(start + 6) + " 00:00</td>" +
                    "<td>" + (week == 9 ? 2 : 0) + "</td><td>" + (week == 9 ? tasksInCurrentWeek : week == 7 ? 1 : 0) + "</td></tr>";
            }
            var plan = "5" + subject + "/" + subject + "/2026/1/";
            return StudyPage(Panel(Row("<td class='ct'><span><a href='/student/discussion/index/2026/1/'>Список дисциплин 2026-2027 год Осенний Семестр</a> / <a href='/student/discussion/acpos/list/" + subject + "/2026/1/'>Список Уч. планов</a></span></td>", "top") +
                Row("<td class='ts'><a class='lt' id='add_task_btn' href='#'><img src='/img/file_add.png'>Задания</a><a class='lt' id='lessons_btn' href='#'><img src='/img/file_add.png'>Проведенные уроки</a></td>") +
                Row("<td class='ct' style='text-align:center'>2026-2027 год Осенний Семестр (История Казахстана)</td>", "brk") +
                Row("<td class='tt'><table class='inner'><tr><th width='20px'><img src='/img/folder.png'></th><th width='60px'>Неделя</th><th>Период</th><th width='150px'>Количество ресурсов</th><th width='150px'>Количество заданий</th><th></th></tr></table>" +
                    "<div class='inner scrool' style='height:240px;overflow-y:scroll'><table class='inner' onselectstart='return false' onmousedown='return false'>" + rows + "</table></div></td>"))) +
                Handler("window.location = \"/student/discussion/task/list/" + plan + "\" + this.id+\"/\";") +
                "<script>function qaResources(checked){window.location = \"/student/discussion/resource/" + plan + "\" + checked.attr(\"id\") + \"/\";}</script>";
        }
        private static string CourseTaskListFixture(int subject, int week, string title, int start, int end)
        {
            var plan = "5" + subject + "/" + subject + "/2026/1/" + week + "/";
            return StudyPage(Panel(Row("<td class='ct'><span><a href='/student/discussion/acpos/week/list/5" + subject + "/" + subject + "/2026/1/'>Недели</a></span></td>", "top") +
                Row("<td class='ts'><a class='lt' id='mark_btn' href='#'>Сводный список</a><a class='lt' id='preview_btn' href='#'>Просмотр</a></td>") +
                Row("<td class='tt'><div class='inner'><table class='inner'><tr><th width='20px'></th><th>Название</th><th width='100px'>Дата начала (дд/мм/гггг)</th><th width='100px'>Дата окончания (дд/мм/гггг)</th><th width='80px'>Дата созд.</th><th width='60px'>Участников</th></tr>" +
                    "<tr class='link' id='90" + week + "' title='Синтетическое описание'><td><img src='/img/file.png'></td><td>" + title + " (0). Автор: Синтетический Преподаватель</td><td>" + Day(start) + "</td><td>" + Day(end) + "</td><td>" + Day(start) + "</td><td>42</td></tr>" +
                    "<tr class='empty'><td colspan='6'></td></tr></table></div></td>"))) +
                Handler("window.location = \"/student/discussion/task/preview/\" + this.id + \"/" + plan + "0/0/0/\";");
        }
        internal static string CourseTaskFixture()
        {
            const string plan = "План семинарского занятия: 1. Первое синтетическое направление темы. 2. Второе направление и его этапы. 3. Третье направление и события 1858 г. " +
                "4.Четвёртое направление без пробела после номера. 5. Пятое направление и итоги. Студентам предстоит сравнить подходы разных исследователей и подготовить краткие выводы по каждому пункту плана.";
            return StudyPage(Panel(Row("<td class='ct'><a href='/student/discussion/acpos/week/list/57101/7101/2026/1/'>Недели</a> <a href='/student/discussion/acpos/list/7101/2026/1/'>Список календарей</a> <a href='/student/discussion/task/list/57101/7101/2026/1/9/'>Задания</a> / Синтетическое задание по истории (История Казахстана).</td>", "top") +
                Row("<td class='ct'><img src='/img/info.png' alt=''><a id='help_btn' href='#'>Информация: <strong>Интернет-консультант</strong></a><div id='help_div' style='display:none'><hr></div></td>") +
                Row("<td class='ct'><table class='xp'><tr><td><li>Предмет:</li></td><td>История Казахстана</td><td><li>Срок академического календаря:</li></td><td>01.09.2026-12.12.2026</td><td><li>Количество недель:</li></td><td>15</td><td><li>Текущая неделя:</li></td><td>9</td></tr></table></td>") +
                Row("<td class='tt'><div class='inner' style='height:260px;overflow-y:scroll'><table class='inner'><tr><th>Информация</th><th>Сообщения</th></tr><tr class='message'><td valign='top'><div class='inf'>" +
                    "<span class='btn'><a class='btn ans' href='#mes' title='Отправить сообщение автору' name='user_77' onclick='window.qaReply=1'><img src='/img/reply.png' alt=''></a></span>" +
                    "<span class='title'>Название задания:</span>Синтетическое задание по истории<br><span class='capt' title='(teacher)'>Автор:</span>Синтетический Преподаватель<br>" +
                    "<span class='duration'>Продолжительность:</span><br>" + Day(-3) + " 00:00-" + Day(1) + " 23:00<span class='title'>Максимальное кол-во ответов:</span><span>2</span><br>" +
                    "<span class='title'>Максимальный балл:</span>10<br></div></td><td valign='top'><span class='header'>Формат ответа:</span>Ответ в виде файла<br><span class='header'>Описание:</span>" + plan + "</td></tr></table></div></td>") +
                Row("<td class='tt'><div class='inner'><table class='inner'><tr id='false'><th>Информация</th><th>Сообщения</th></tr></table></div></td>", "mid' id='group_list") +
                Row("<td class='ct'>Написать сообщение</td>", "brk") +
                Row("<td class='ct'><a name='mes'></a><form id='send_message' action='/student/discussion/task/send/909/57101/7101/2026/1/9/' method='post' enctype='multipart/form-data' onsubmit='window.qaSubmitted=1;return false'><table><tr><td><label for='user_to'>Кому:</label><select id='user_to' name='user_to'><option value='77'>Синтетический Преподаватель</option></select></td></tr>" +
                    "<tr><td><label>Прикрепить файлы</label><a href='#' id='fileS'>Добавить</a></td><td><input id='sb_btn' type='button' value='Отправить сообщение' onclick='window.qaSubmitted=1'></td></tr></table></form></td>")));
        }
        // The pages behind the course list, served in place of the network for the background reads and navigations.
        private void RegisterCoursePages()
        {
            var factory = main.ResourceRequestHandlerFactory as ResourceRequestHandlerFactory;
            if (factory == null) return;
            Action<string, string> page = (route, html) => factory.RegisterHandler(SitePolicy.Origin + route, ResourceHandler.GetByteArray("<!doctype html><meta charset='utf-8'>" + html, Encoding.UTF8), "text/html", false);
            page(CourseRoute + "acpos/list/7101/2026/1/", CoursePlansFixture(7101));
            page(CourseRoute + "acpos/list/7103/2026/1/", CoursePlansFixture(7103));
            page(CourseRoute + "acpos/week/list/57101/7101/2026/1/", CourseWeeksFixture(7101, 1));
            page(CourseRoute + "acpos/week/list/57103/7103/2026/1/", CourseWeeksFixture(7103, 1));
            page(CourseRoute + "task/list/57101/7101/2026/1/9/", CourseTaskListFixture(7101, 9, "Синтетическое задание по истории", -3, 1));
            page(CourseRoute + "task/list/57101/7101/2026/1/7/", CourseTaskListFixture(7101, 7, "Прошлое синтетическое задание", -17, -12));
            page(CourseRoute + "task/list/57103/7103/2026/1/9/", CourseTaskListFixture(7103, 9, "Эссе о социальных институтах", -1, 6));
            page(CourseRoute + "task/preview/909/57101/7101/2026/1/9/0/0/0/", CourseTaskFixture());
            // Политология (7102) has no registered curriculum: its read fails and the panel says so.
        }
        private async Task<string> WaitForAsync(string script, int milliseconds = 8000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds); string value;
            do { value = await AutomationController.EvaluateAsync(main, script); if (!string.IsNullOrEmpty(value)) return value; await Task.Delay(100); } while (DateTime.UtcNow < until);
            return value;
        }
        // Native checks: background reads through the real request path, the course list panel, the curriculum skip,
        // the weeks and the task card, wide and narrow, and Original restoring the site's markup.
        private async Task CourseTasksQaAsync(string qa, List<string> results, string heading)
        {
            RegisterCoursePages();
            main.LoadHtml(heading + CourseIndexFixture(), SitePolicy.Origin + CourseRoute + "index/"); await WaitQaPresentationAsync();
            await AutomationController.EvaluateAsync(main, "sessionStorage.clear();'clear'");
            prefs.Readable = false; await ApplyPresentationAsync(); prefs.Readable = true; await ApplyPresentationAsync();
            var panel = await WaitForAsync("(function(){var b=document.querySelector('#l4 .univer-app-tasks');return b&&b.dataset.busy==='false'?'done':''})()");
            Check(panel == "done" && await AutomationController.EvaluateAsync(main, "(function(){var items=Array.from(document.querySelectorAll('#l4 .univer-app-task-item'));var first=items[0]&&items[0].querySelector('a');" +
                "var chip=document.getElementById('7101').querySelector('.univer-app-course-tasks'),note=document.querySelector('#l4 .univer-app-tasks-note');" +
                "return items.length===2&&items[0].dataset.state==='urgent'&&first.getAttribute('href')==='/student/discussion/task/preview/909/57101/7101/2026/1/9/0/0/0/'&&" +
                "chip&&chip.textContent==='1 задание'&&chip.getAttribute('href')==='/student/discussion/acpos/week/list/57101/7101/2026/1/'&&note&&/Политология/.test(note.textContent)&&" +
                "document.querySelectorAll('#l4 .univer-app-course-tasks').length===2?'tasks':JSON.stringify({items:items.length})})()") == "tasks",
                "course list shows current assignments read in the background, each linked to its task, and a failed course by name", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "course-tasks-index.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 900; await Task.Delay(250);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('#l4 .univer-app-tasks').getBoundingClientRect(),s=document.querySelector('#l4 .univer-app-task-status').getBoundingClientRect();" +
                "return document.documentElement.scrollWidth<=innerWidth+1&&s.right<=b.right&&s.width>40?'narrow':''})()") == "narrow", "narrow course list keeps assignments and their deadlines inside the window", results);
            Width = 1360;
            prefs.Readable = false; await ApplyPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('.univer-app-tasks,.univer-app-course-tasks,#univer-tasks-style')&&document.getElementById('7101').cells[1].textContent==='История Казахстана (0)'?'restored':''") == "restored",
                "Original view removes the assignments panel and course badges", results);
            prefs.Readable = true; await ApplyPresentationAsync();
            await WaitForAsync("document.querySelector('#l4 .univer-app-course-tasks')?'back':''", 3000);
            // Open on a course with a one-row curriculum lands on its weeks; Back would return to the list.
            await AutomationController.EvaluateAsync(main, "document.getElementById('7101').querySelector('.univer-app-course-open').click();'open'");
            var weeks = await WaitForAsync("/acpos\\/week\\/list\\/57101/.test(location.pathname)&&document.querySelector('#l4 .univer-app-tasks[data-busy=false]')?'weeks':''");
            Check(weeks == "weeks", "Open on a course skips its one-row curriculum page and shows its weeks", results);
            await WaitQaPresentationAsync(); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var box=document.querySelector('#l4 div.inner.scrool'),cur=document.querySelectorAll('#l4 .univer-app-week-current'),items=document.querySelectorAll('#l4 .univer-app-tasks .univer-app-task-item');" +
                "var buttons=Array.from(document.querySelectorAll('#l4 .univer-app-row-open')).map(function(b){return b.closest('tr').id}).join(',');" +
                "return box.scrollHeight<=box.clientHeight+1&&cur.length===1&&cur[0].closest('tr').id==='9'&&items.length===2&&buttons==='7,9'&&document.querySelector('#l4 a.univer-app-week-materials')?'weeks':JSON.stringify({buttons:buttons,items:items.length})})()") == "weeks",
                "weeks show the whole list, the current week, materials and the course's assignments above", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "course-tasks-weeks.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            main.Load(SitePolicy.Origin + CourseRoute + "task/preview/909/57101/7101/2026/1/9/0/0/0/"); await WaitQaPresentationAsync();
            var card = await WaitForAsync("document.querySelector('#l4 .univer-app-task-card')?'card':''");
            Check(card == "card" && await AutomationController.EvaluateAsync(main, "(function(){var c=document.querySelector('#l4 .univer-app-task-card'),x=document.querySelector('#l4 table.xp'),src=document.querySelector('#l4 .univer-app-task-source');" +
                "var items=c.querySelectorAll('ol li'),gap=items[1].getBoundingClientRect().top-items[0].getBoundingClientRect().bottom;" +
                "return c.getBoundingClientRect().bottom<=x.getBoundingClientRect().top&&items.length===5&&gap<16&&!src.getClientRects().length&&c.querySelector('h2').textContent==='Синтетическое задание по истории'&&" +
                "c.querySelector('.univer-app-task-status[data-state=urgent]')?'card':JSON.stringify({gap:gap,items:items.length})})()") == "card",
                "a task is one readable card above the course details, its plan a tight numbered list", results);
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('#l4 #group_list').getClientRects().length?'hidden':''") == "hidden", "an empty message history under the task is hidden", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "course-tasks-task.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "document.querySelector('#l4 .univer-app-task-reply').click();'reply'"); await Task.Delay(900);
            Check(await AutomationController.EvaluateAsync(main, "window.qaReply===1&&!window.qaSubmitted&&location.hash===''&&Math.abs(document.getElementById('send_message').getBoundingClientRect().top)<innerHeight?'reply':''") == "reply",
                "the reply button runs the site's reply link and brings the message form into view without sending anything", results);
            prefs.Readable = false; await ApplyPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('.univer-app-task-card,.univer-app-task-source')&&document.querySelector('#l4 tr.message').getClientRects().length?'restored':''") == "restored",
                "Original view shows the site's task table again", results);
            prefs.Readable = true; await ApplyPresentationAsync();
        }
    }
}
