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
        // Study pages rebuilt from the observed structure (review captures of 8–9 October 2026) with synthetic values:
        // a shell table, an empty notice panel, the GPA line, then the page's own panels.
        private static string StudyPage(string panels)
        {
            return "<table class='wrappertable'><tr id='l4'><td><table class='inner'><tr><td class='left'></td><td class='center'>" +
                "<table class='mt'><tr class='top'><td class='lt'></td><td class='ct warning'></td><td class='rt'></td></tr><tr class='mid'><td class='lt'></td><td class='ct message'><li>Переводной GPA = 3.12</li><li>Общий GPA = 3.12</li></td><td class='rt'></td></tr><tr class='bot'><td class='lt'></td><td class='ct'></td><td class='rt'></td></tr></table>" +
                panels + "</td><td class='right'></td></tr></table></td></tr></table>";
        }
        private static string Panel(string rows) { return "<table class='mt'>" + rows + "<tr class='bot'><td class='lt'></td><td class='ct'></td><td class='rt'></td></tr></table>"; }
        private static string Row(string cell, string kind = "mid") { return "<tr class='" + kind + "'><td class='lt'></td>" + cell + "<td class='rt'></td></tr>"; }
        private static string SemesterLinks(string route, string previous, string next)
        {
            return "<td class='ts'><a class='lt' href='" + route + "2025/2/'><img src='/img/tb_left.png'>" + previous + "</a>" +
                (next == null ? "" : "<a class='rt' href='" + route + "2026/2/'>" + next + "<img src='/img/tb_right.png'></a>") + "</td>";
        }
        private const string AttestationNote = "<td class='ct'><b><font color='red'>Примечание:</font></b><br><br><b><font color='red'>* - оценка итогового контроля (экзамена) может быть аннулирована по итогам проверки видеозаписи экзамена в случае выявления академической нечестности обучающегося</font></b></td>";
        // The live page (29 October 2026): nine columns and a total row across several columns, which makes it a matrix.
        internal static string AttestationFixture(bool empty)
        {
            var rows = empty ? "<tr><td colspan='11'>Нет данных</td></tr>" :
                "<tr><td>Алгоритмы и структуры данных</td><td>5</td><td>98</td><td>96</td><td>92</td><td>94</td><td>4</td><td>A</td><td>отл.</td></tr>" +
                "<tr><td>Дискретная математика</td><td>5</td><td>88</td><td>84</td><td>86</td><td>86</td><td>3.33</td><td>B+</td><td>хор.</td></tr>" +
                "<tr><td>Иностранный язык (профессионально-ориентированный)</td><td>3</td><td>80</td><td>78</td><td>76*</td><td>78</td><td>2.67</td><td>B-</td><td>хор.</td></tr>" +
                "<tr><td>Физическая культура</td><td>2</td><td>70</td><td>64</td><td>65</td><td>66</td><td>1.67</td><td>C-</td><td>удовл.</td></tr>" +
                "<tr><td>Психология</td><td>3</td><td>60</td><td>0</td><td>0</td><td>18</td><td>0</td><td>F</td><td>неуд.</td></tr>" +
                "<tr><td>GPA(2026-2027 уч. год)- 2.71</td><td colspan='8'>Освоено кредитов (2026-2027 уч. год) - 15</td></tr>";
            return StudyPage(Panel(
                Row("<td class='ct'>Аттестация студента. Осенний Семестр, 2026-2027 уч. год (2 Курс, 3 Семестр)</td>", "top") +
                Row(SemesterLinks("/student/attestation/", "Перейти к 2025-2026 году Весенний семестр", "Перейти к 2026-2027 году Весенний семестр")) +
                Row("<td class='tt'><table class='inner'><tr><th width='30%'>Дисциплина</th><th width='5%'>Кред.</th><th width='5%'>РК1</th><th width='5%'>РК2</th><th width='5%'>ПА*</th><th width='5%'>Сумма</th><th width='5%'>GPA</th><th width='5%'>Букв.</th><th>Значение</th></tr>" + rows + "</table></td>") +
                Row("<td class='pd'></td>") + Row(AttestationNote)));
        }
        internal static string DiscussionFixture()
        {
            var names = new[] { "Без привязки к дисциплине (0)", "Введение в информационную безопасность (2)", "Иностранный язык (0)", "История Казахстана (1)", "Казахский (русский) язык (0)",
                "Культурология (0)", "Политология (0)", "Психология (3)", "Социология (0)", "Физическая культура (0)", "Обсуждения других пользователей" };
            var rows = "";
            for (var i = 0; i < names.Length; i++)
                rows += "<tr class='link' id='" + (i == names.Length - 1 ? "other" : (7100 + i).ToString()) + "' ondblclick='window.qaOpened=this.id'><td><img src='/img/folder.png'></td><td>" + names[i] + "</td></tr>";
            // As on the live page: two blocks of instruction files, "Назад" without an address next to the heading link.
            return StudyPage(
                Panel(Row("<td class='ct'>Инструкция</td>", "top") + Row("<td class='ct'><div><div><a href='/Content/instructions/1.pdf'><img src='/img/pdf.png' alt=''> Студенттерге арналған нұсқаулық</a><br><a href='/Content/instructions/2.pdf'><img src='/img/pdf.png' alt=''> Студенттерге арналған жазбаша емтихан тапсыру нұсқаулығы.pdf</a></div>" +
                    "<div><a href='/Content/instructions/3.pdf'><img src='/img/pdf.png' alt=''> Дистанционные курсы Инструкция для студентов</a><br><a href='/Content/instructions/4.pdf'><img src='/img/pdf.png' alt=''> Для студентов письменный экзамен ДО инструкция.pdf</a></div></div></td>")) +
                Panel(Row("<td class='ct'><span><a>Назад</a> / <a href='/student/discussion/index/'>Список дисциплин 2026-2027 год Осенний Семестр</a></span><span><b></b></span></td>", "top") +
                    Row("<td class='ts'><a class='lt' id='subjectShow_btn' href='#'><img src='/img/door-in-icon.png'>Открыть дисциплину</a><a class='lt' href='/student/discussion/index/2025/2/'><img src='/img/tb_left.png'>Перейти к 2025-2026 году Весенний Семестр</a></td>") +
                    Row("<td class='ct' style='text-align:center'><b>2026-2027 год Осенний Семестр</b></td>") +
                    Row("<td class='tt'><table class='inner' onselectstart='return false' onmousedown='return false'><tr><th width='10%'></th><th>Дисциплина</th></tr>" + rows + "</table></td>")));
        }
        // The live debts page opens with a long memo before its two tables.
        internal static string FailFixture()
        {
            var memo = "<td class='ct'><b><font color='red'>ВНИМАНИЕ!</font><br>ПАМЯТКА ПО ПОВТОРНОМУ ИЗУЧЕНИЮ ДИСЦИПЛИН<br>Обучающийся, не подавший заявку на повторное обучение через систему Univer, к обучению не допускается.<br><br><br></b>";
            for (var step = 1; step <= 8; step++) memo += step + ". Синтетический шаг памятки: заявка, рассмотрение в деканате, договор, оплата и проверка статуса в системе перед началом занятий.<br>";
            memo += "</td>";
            return StudyPage(
                Panel(Row("<td class='ct'>Информация</td>", "top") + Row("<td class='pd'></td>") + Row(memo)) +
                Panel(Row("<td class='ct'>Список задолженностей студента.</td>", "top") +
                    Row("<td class='tt'><table class='inner'><tr><th>Шифр</th><th>Дисциплина</th><th>Кред.</th><th>Сумма</th><th>GPA</th><th>Букв.</th><th>Семестр</th><th>Преподаватель</th><th>Состояние</th></tr><tr><td colspan='9'>Нет данных</td></tr></table></td>") +
                    Row("<td class='tt'><table class='inner'><tr><th colspan='8'>Мои заявки на повторное изучение</th></tr><tr><th>Дисциплина</th><th>Кредиты</th><th>Стоимость</th><th>Семестр обучения</th><th>Статус</th><th>Оплачено</th><th>Дата</th><th></th></tr><tr><td colspan='8'>Нет данных</td></tr></table></td>")));
        }
        // The live exam schedule: instruction files mixed with red proctoring lines in one block.
        internal static string ExamFixture()
        {
            return StudyPage(
                Panel(Row("<td class='ct'>Инструкция</td>", "top") + Row("<td class='ct'><div><a href='/Content/instructions/DistanceLearnExam/1.pdf'><img src='/img/pdf.png' alt=''> Студенттерге арналған жазбаша емтихан тапсыру нұсқаулығы.pdf</a><br><br>" +
                    "<a href='/Content/instructions/DistanceLearnExam/2.pdf'><img src='/img/pdf.png' alt=''>Инструкция для студентов (онлайн письменный экзамен)</a><br><br><a href='/Content/instructions/DistanceLearnExam/3.pdf'><img src='/img/pdf.png' alt=''> Устный экзамен инструкция для студентов.pdf</a><br><br>" +
                    "<center><font color='red'>Начните экзамен через кнопку экстеншна Farabi Proctoring, если вы еще не установили нажмите на </font><a href='https://example.org/extension'>ссылка для установки</a></center></div><div></div></td>")) +
                Panel(Row("<td class='ct'>Расписание экзаменов</td>", "top") + Row(SemesterLinks("/student/myexam/schedule/", "Расписание экзаменов на 2025-2026 год Весенний семестр", "Расписание экзаменов на 2026-2027 год Весенний семестр")) +
                    Row("<td class='tt'><table class='inner'><tr><th>Название дисциплины</th><th>Преподаватель</th><th>Тип</th><th>Аудитория факультета</th><th>Тип группы</th><th>Ссылки</th></tr><tr><td colspan='6'>Нет данных</td></tr></table></td>")));
        }
        // Native checks of the study-page layouts in real Chromium: geometry, colours and the site's own handler.
        private async Task StudyPagesQaAsync(string qa, List<string> results, string heading)
        {
            main.LoadHtml(heading + AttestationFixture(false), SitePolicy.Origin + "/student/attestation/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('#l4 .univer-app-grade[data-band=f]'),t=b&&b.closest('table'),n=document.querySelector('#l4 .univer-app-callout'),last=t&&t.rows[t.rows.length-1];" +
                "return document.querySelectorAll('#l4 .univer-app-grade').length===5&&t.classList.contains('univer-app-matrix')&&b.getBoundingClientRect().height>=20&&getComputedStyle(b).backgroundColor==='rgb(253, 236, 235)'&&" +
                "!last.querySelector('[data-univer-role],.univer-app-grade')&&getComputedStyle(b.closest('tr').cells[0]).backgroundColor!=='rgb(255, 247, 246)'&&" +
                "n&&getComputedStyle(n).backgroundColor==='rgb(255, 248, 234)'&&parseFloat(getComputedStyle(n.querySelector('b:last-of-type font')).fontSize)<=14.5?'grades':''})()") == "grades",
                "live-shaped attestation matrix shows grade badges without tinting rows, keeps its total row and a calm note", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "study-attestation-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 900; await Task.Delay(250);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('#l4 .univer-app-grade');return document.documentElement.scrollWidth<=innerWidth+1&&b&&b.getBoundingClientRect().height>=20?'narrow':''})()") == "narrow",
                "narrow attestation keeps the grade matrix inside the window with its badges", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "study-attestation-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 1360;
            main.LoadHtml(heading + DiscussionFixture(), SitePolicy.Origin + "/student/discussion/index/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var rows=Array.from(document.querySelectorAll('#l4 .univer-app-forum-courses tr.link'));var links=Array.from(document.querySelectorAll('#l4 .univer-app-doc-links a'));" +
                "return rows.length===11&&rows.every(function(r){var b=r.querySelector('.univer-app-course-open'),c=r.cells[1].getBoundingClientRect(),k=b.getBoundingClientRect();return k.width>40&&k.left>=c.left&&k.right<=c.right+1&&k.top>=c.top-1&&k.bottom<=c.bottom+1})&&" +
                "links.length===4&&links.every(function(a,i){var r=a.getBoundingClientRect();return getComputedStyle(a).display==='flex'&&links.every(function(o,j){var q=o.getBoundingClientRect();return i===j||r.bottom<=q.top-6||q.bottom<=r.top-6||r.right<=q.left-6||q.right<=r.left-6})})&&" +
                "!document.querySelector('#l4 .univer-app-back-link')?'courses':''})()") == "courses",
                "distance courses: separated document cards, an Open button inside every course row, and no dead back button", results);
            var opened = await AutomationController.EvaluateAsync(main, "(function(){document.querySelectorAll('#l4 .univer-app-course-open')[1].click();var i=document.querySelector('#l4 .univer-app-list-filter input');" +
                "i.value='истор';i.dispatchEvent(new Event('input'));var shown=Array.from(document.querySelectorAll('#l4 .univer-app-forum-courses tr.link')).filter(function(r){return r.getClientRects().length});" +
                "var ok=window.qaOpened==='7101'&&shown.length===1&&shown[0].id==='7103';i.value='';i.dispatchEvent(new Event('input'));return ok?'opened':JSON.stringify({opened:window.qaOpened,shown:shown.map(function(r){return r.id})})})()");
            QaTrace("study-open-result " + opened);
            Check(opened == "opened", "Open uses the site's double-click handler and the course search filters the list", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "study-discussion.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            prefs.Readable = false; await ApplyPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('.univer-app-course-open,.univer-app-list-filter,.univer-app-count,#univer-pages-style')&&document.getElementById('7101').cells[1].textContent==='Введение в информационную безопасность (2)'?'restored':''") == "restored",
                "Original view restores the distance-course list exactly", results);
            prefs.Readable = true; await ApplyPresentationAsync();
            main.LoadHtml(heading + FailFixture(), SitePolicy.Origin + "/student/fail/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var m=document.querySelector('#l4 .univer-app-memo'),b=m&&m.querySelector('.univer-app-memo-toggle');if(!b)return '';var folded=m.getBoundingClientRect().height,cap=document.querySelector('#l4 .univer-app-table-caption');" +
                "b.click();var open=m.getBoundingClientRect().height,expanded=b.getAttribute('aria-expanded');b.click();" +
                "return folded<330&&open>folded+80&&expanded==='true'&&b.getAttribute('aria-expanded')==='false'&&cap&&cap.getClientRects().length?'memo':''})()") == "memo",
                "the long debts memo opens folded, unfolds with its button and the empty table keeps its heading", results);
            main.LoadHtml(heading + ExamFixture(), SitePolicy.Origin + "/student/myexam/schedule/"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "(function(){var a=Array.from(document.querySelectorAll('#l4 a.univer-app-doc-link')),red=document.querySelector('#l4 center');" +
                "return a.length===3&&a.every(function(x){return getComputedStyle(x).display==='flex'})&&red&&red.getClientRects().length&&!red.querySelector('.univer-app-doc-link')?'exam':''})()") == "exam",
                "exam instruction files become cards while the proctoring lines stay as they are", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "study-exam.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await CourseTasksQaAsync(qa, results, heading);
        }
        // --qa-study: an isolated visible run that renders these fixtures wide and narrow and saves Chromium screenshots.
        private async Task StudyQaAsync()
        {
            var output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-study"); Directory.CreateDirectory(output);
            try
            {
                while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
                prefs.Language = "ru"; prefs.Readable = true; Localize();
                foreach (var page in new[] { Tuple.Create("attestation", "/student/attestation/", AttestationFixture(false)), Tuple.Create("attestation-empty", "/student/attestation/", AttestationFixture(true)),
                    Tuple.Create("discussion", "/student/discussion/index/", DiscussionFixture()), Tuple.Create("fail", "/student/fail/", FailFixture()), Tuple.Create("exam", "/student/myexam/schedule/", ExamFixture()),
                    Tuple.Create("course-index", "/student/discussion/index/", CourseIndexFixture()), Tuple.Create("course-weeks", "/student/discussion/acpos/week/list/57101/7101/2026/1/", CourseWeeksFixture(7101, 1)),
                    Tuple.Create("course-tasks", "/student/discussion/task/list/57101/7101/2026/1/9/", CourseTaskListFixture(7101, 9, "Синтетическое задание по истории", -3, 1)), Tuple.Create("course-task", "/student/discussion/task/preview/909/57101/7101/2026/1/9/0/0/0/", CourseTaskFixture()) })
                {
                    foreach (var width in new[] { 1360, 900 })
                    {
                        RegisterCoursePages();
                        Width = width; main.LoadHtml(page.Item3, SitePolicy.Origin + page.Item2); await WaitQaPresentationAsync(); await Task.Delay(400);
                        await WaitForAsync("document.querySelector('#l4 .univer-app-tasks[data-busy=true]')?'':'idle'", 5000);
                        using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(output, page.Item1 + "-" + width + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                    }
                }
                File.WriteAllText(Path.Combine(output, "done.txt"), "ok");
            }
            catch (Exception exception) { File.WriteAllText(Path.Combine(output, "done.txt"), exception.ToString()); }
            finally { ExitApplication(); }
        }
    }
}