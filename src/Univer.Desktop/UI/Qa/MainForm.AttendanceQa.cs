using System;
using System.Collections.Generic;
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
        private static string AttendanceQaLesson(string id, string name, string total)
        {
            return "<tr><td class='lt'></td><td class='ts'><a class='lt' id='qa-" + id + "-label' href='/student/attendance/show/123/2026/1' onclick='event.preventDefault();window.qaAttendanceClicks++'>" + name + " — Преподаватель QA (Русский)</a><a class='rt syllabus-info' href='#'>Интерактивный силлабус</a></td><td class='rt'></td></tr>" +
                "<tr><td colspan='3'><div class='inner'><table class='inner' id='qa-" + id + "'><tr><th rowspan='2'>РК1 (100)</th><th colspan='2'>06.10</th><th></th><th>Сумма</th></tr><tr><td>2</td><td>3</td><td></td><td>" + total + "</td></tr></table></div></td></tr>";
        }
        private async Task AttendanceQaAsync(string qa, List<string> results, string heading)
        {
            var courses = "<div id='l4'><table class='inner'><tr class='link' id='321'><td>1</td><td>История Казахстана</td><td>Обязательная</td></tr><tr class='link' id='322'><td>2</td><td>Иностранный язык</td><td>Обязательная</td></tr></table></div>";
            var syllabi = "<div id='l4'><a href='/student/interactive/syllabus/print/22/2'>Силлабус QA</a></div>";
            var json = new JavaScriptSerializer();
            var fixture = "<script>window.qaAttendanceClicks=0;window.fetch=async function(path){return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return path.includes('/0/')?" + json.Serialize(courses) + ":" + json.Serialize(syllabi) + ";}}};sessionStorage.removeItem('univer.desktop.attendance-kind.v1');</script>" +
                "<table class='wrappertable'><tr id='l4'><td class='center'><table class='mt'><tr><td class='ct'>Журнал посещений и успеваемости</td></tr><tr><td class='ct'>История Казахстана ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>" +
                AttendanceQaLesson("lecture", "лекция", "10") + AttendanceQaLesson("seminar", "семинар", "26") + AttendanceQaLesson("sro", "СРО/СРОП", "15") +
                "<tr><td class='ct' id='qa-overall'><b>РК1 (100): </b>51 <span></span><b>РК2 (100): </b>0</td></tr><tr><td class='ct'>Иностранный язык ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>" + AttendanceQaLesson("second-seminar", "семинар", "8") +
                "<tr><td class='ct'><b>РК1 (100): </b>8 <b>РК2 (100): </b>0</td></tr></table></td></tr></table>";
            main.LoadHtml(heading + fixture, SitePolicy.Origin + "/student/attendance/full/2026/1"); await WaitQaPresentationAsync();
            Check(await AutomationController.EvaluateAsync(main, "document.getElementById('qa-seminar-label').click();Array.from(document.querySelectorAll('a[data-univer-lesson-label]')).every(a=>getComputedStyle(a).display==='none'&&a.tabIndex===-1)&&window.qaAttendanceClicks===0?'labels':''") == "labels", "attendance lesson labels cannot be clicked or keyboard-focused in Reading mode", results);
            Check(await AutomationController.EvaluateAsync(main, "var s=document.querySelector('.univer-app-attendance-summary'),t=Array.from(s.querySelectorAll('.univer-app-type-row')).map(function(r){return r.querySelector('.univer-app-type-label').textContent+' '+Array.from(r.querySelectorAll('.univer-app-score')).map(function(x){return x.getAttribute('aria-label');}).join(' · ');}).join('|');t.includes('Лекции РК1 (100): 10')&&t.includes('Семинары РК1 (100): 26')&&t.includes('СРО РК1 (100): 15')?'totals':''") == "totals", "attendance shows independent totals by lesson type for each course", results);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-course-syllabus').length===2&&Array.from(document.querySelectorAll('a.syllabus-info')).every(a=>getComputedStyle(a).display==='none')?'one':''") == "one", "attendance has exactly one syllabus button per subject instead of per lesson", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var v=n=>n.getBoundingClientRect().height>0,o=document.querySelector('.univer-app-attendance-overview');return o&&v(o)&&o.querySelectorAll('li').length===2&&o.querySelectorAll('.univer-app-score').length===4&&!v(document.getElementById('qa-lecture'))&&document.querySelectorAll('.univer-app-attendance-summary .univer-app-score-bar').length===4&&!v(document.querySelector('.univer-app-attendance-filter-note'))?'summary':''})()") == "summary", "all courses open as a summary with score tiles while class logs wait for a request", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "attendance-all-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            // Each toggle rebuilds the summaries, so open the remaining collapsed logs one by one.
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t,n=0;while(n<10&&(t=document.querySelector('.univer-app-attendance-details-toggle[aria-expanded=false]'))){t.click();n++;}return n===2&&document.getElementById('qa-lecture').getBoundingClientRect().height>60&&document.activeElement.matches('.univer-app-attendance-details-toggle[aria-expanded=true]')?'open':''})()") == "open", "class log opens on request and keeps keyboard focus on its toggle", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-filters [data-kind=seminar]').click();'filter'");
            Check(await AutomationController.EvaluateAsync(main, "var l=document.getElementById('qa-lecture'),s=document.getElementById('qa-seminar'),o=document.querySelector('.univer-app-attendance-overall-block'),r=document.querySelector('.univer-app-type-row[data-kind=seminar]');l.getBoundingClientRect().height===0&&s.getBoundingClientRect().height>60&&!o&&r&&r.getBoundingClientRect().height>20&&s.rows[0].cells[0].rowSpan===2?'filtered':''") == "filtered", "attendance filter shows each course's log of that type instead of the course total, keeping merged cells", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "attendance-seminars-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 900; await Task.Delay(180);
            Check(await AutomationController.EvaluateAsync(main, "document.documentElement.scrollWidth<=innerWidth+2&&Array.from(document.querySelectorAll('.univer-app-attendance-filters button')).every(b=>b.getBoundingClientRect().height>=36)?'fits':''") == "fits", "attendance filters and summaries fit a narrow window", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "attendance-seminars-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-course-syllabus').click();'open'");
            var syllabusUntil = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < syllabusUntil && await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-syllabi li')?'ready':''") != "ready") await Task.Delay(100);
            Check(await AutomationController.EvaluateAsync(main, "var p=document.querySelector('.univer-app-syllabi');p&&p.querySelector('li')&&p.getBoundingClientRect().height>40&&p.closest('.ct').querySelector('.univer-app-course-syllabus')?'list':''") == "list", "subject syllabus list stays available with a lesson filter in a narrow window", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "attendance-syllabus-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 1360;
            await AutomationController.EvaluateAsync(main, "document.querySelector('[data-kind=all]').click();document.querySelectorAll('.univer-app-attendance-courses button')[2].click();'subject'");
            Check(await AutomationController.EvaluateAsync(main, "(function(){var visible=n=>n.getBoundingClientRect().height>0;return document.querySelectorAll('.univer-app-attendance-courses button').length===3&&!visible(document.getElementById('qa-lecture'))&&visible(document.getElementById('qa-second-seminar'))&&Array.from(document.querySelectorAll('.univer-app-course-syllabus')).filter(visible).length===1&&Array.from(document.querySelectorAll('.univer-app-attendance-overall-block')).filter(visible)[0].querySelector('.univer-app-score-value').textContent==='8'?'subject':''})()") == "subject", "subject pager shows only the selected course, its total and one syllabus button", results);
            await Task.Delay(150);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "attendance-subject-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "document.querySelector('[data-kind=lecture]').click();'lecture'");
            Check(await AutomationController.EvaluateAsync(main, "(function(){var v=n=>n.getBoundingClientRect().height>0,s=Array.from(document.querySelectorAll('.univer-app-attendance-summary')).find(v);return document.getElementById('qa-lecture').getBoundingClientRect().height===0&&document.getElementById('qa-second-seminar').getBoundingClientRect().height===0&&s.textContent.includes('Нет занятий этого вида')&&!s.querySelector('.univer-app-attendance-overall-block')?'combined':''})()") == "combined", "subject and lesson filters combine and the course total steps aside for the class log", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('[data-kind=all]').click();'all types'");
            Width = 900; await Task.Delay(150);
            Check(await AutomationController.EvaluateAsync(main, "document.documentElement.scrollWidth<=innerWidth+2&&Array.from(document.querySelectorAll('.univer-app-attendance-courses button')).every(b=>b.getBoundingClientRect().right<=innerWidth&&b.getBoundingClientRect().height>=38)?'fits':''") == "fits", "subject pager remains readable in the narrow window", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "attendance-subject-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-courses button[aria-pressed=true]').dispatchEvent(new KeyboardEvent('keydown',{key:'Home',bubbles:true}));'all courses'");
            Check(await AutomationController.EvaluateAsync(main, "document.getElementById('qa-lecture').getBoundingClientRect().height>0&&document.getElementById('qa-second-seminar').getBoundingClientRect().height>0&&document.querySelector('.univer-app-attendance-courses button').getAttribute('aria-pressed')==='true'?'all':''") == "all", "All restores every course through the pager keyboard action", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-courses button').dispatchEvent(new KeyboardEvent('keydown',{key:'End',bubbles:true}));'last course'");
            Check(await AutomationController.EvaluateAsync(main, "document.getElementById('qa-lecture').getBoundingClientRect().height===0&&document.getElementById('qa-second-seminar').rows[0].cells[0].rowSpan===2?'last':''") == "last", "End selects the last course without altering merged grade cells", results);
            await AutomationController.EvaluateAsync(main, "window.__univerDesktop.apply(false,false,'ru');document.getElementById('qa-seminar-label').click();'original'");
            Check(await AutomationController.EvaluateAsync(main, "window.qaAttendanceClicks===1&&!document.querySelector('.univer-app-attendance-courses,.univer-app-attendance-filters,.univer-app-attendance-summary,.univer-app-lesson-label')&&!document.getElementById('qa-lecture').closest('[hidden]')?'original':''") == "original", "Original restores all attendance labels, original handlers and hidden sections", results);
            Width = 1360; await AutomationController.EvaluateAsync(main, "sessionStorage.removeItem('univer.desktop.attendance-kind.v1');sessionStorage.removeItem('univer.desktop.attendance-course.v1:'+location.pathname);'cleared'");
        }
    }
}
