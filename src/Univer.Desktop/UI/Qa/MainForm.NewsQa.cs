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
        private static string NewsQaRow(string day, string title, string body = "")
        { return "<tr class='news'><td><div class='date'><span class='day'>" + day + "</span> 10.2026</div></td><td><p>" + title + "</p>" + body + "</td></tr>"; }
        private static string NewsQaPage(string rows, bool next, int page = 1)
        { return "<table class='wrappertable'><tr id='l4'><td><table class='inner'><tr><th>Дата</th><th>Новость</th></tr>" + rows + "</table><table><tr><td class='tb'>Страница " + page + (next ? "<a href='/my/news/" + (page + 1) + "'>" + (page + 1) + "</a>" : "") + "</td></tr></table></td></tr></table>"; }
        private async Task NewsQaAsync(string qa, List<string> results, string heading)
        {
            var json = new JavaScriptSerializer();
            var article = "";
            for (var part = 0; part < 8; part++) article += "<p>Студенты могут ознакомиться с программой встречи, выбрать интересующее направление и узнать об условиях участия. Подробности и расписание приведены в объявлении.</p>";
            article += "<a id='qa-news-tail' href='#qa-news-short'>Последняя строка объявления</a>";
            main.LoadHtml(heading + "<script>sessionStorage.removeItem('univer.desktop.news-filter.v1')</script>" + NewsQaPage(NewsQaRow("09", "<strong>Встреча с участниками студенческих проектов</strong>", article) + NewsQaRow("08", "Короткое объявление: встреча начнётся в 15:00."), false), SitePolicy.Origin + "/news/student");
            await WaitQaPresentationAsync(); await Task.Delay(100);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-news-body'),t=document.querySelector('.univer-app-news-toggle');window.qaNewsTail=document.getElementById('qa-news-tail');return b.clientHeight<=250&&b.scrollHeight>b.clientHeight&&t.getAttribute('aria-expanded')==='false'&&!t.hidden&&document.querySelectorAll('.univer-app-news-toggle')[1].hidden?'collapsed':''})()") == "collapsed", "long news show a compact preview while short news remain complete", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "news-collapsed-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "window.qaNewsScroll=scrollY;document.querySelector('.univer-app-news-toggle').click();'expanded'");
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-news-body');return b.clientHeight>=b.scrollHeight&&document.querySelector('.univer-app-news-toggle').textContent==='Свернуть'&&Math.abs(scrollY-window.qaNewsScroll)<=1?'full':''})()") == "full", "Show full news reveals the entire article without navigation or a scroll jump", results);
            await Task.Delay(100);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "news-expanded-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var savedAnimations = prefs.Animations;
            prefs.Animations = true; await ApplyPresentationAsync();
            using (var client = main.GetDevToolsClient())
            {
                await client.Emulation.SetEmulatedMediaAsync(features: new List<CefSharp.DevTools.Emulation.MediaFeature> { new CefSharp.DevTools.Emulation.MediaFeature { Name = "prefers-reduced-motion", Value = "no-preference" } });
                await Task.Delay(50);
                await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('.univer-app-news-toggle');t.click();document.documentElement.classList.remove('univer-app-keyboard');t.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:1}));var b=document.querySelector('.univer-app-news-body');window.qaNewsAnimations=b.getAnimations();window.qaNewsAnimations.forEach(function(a){a.pause();a.currentTime=0;});window.qaNewsStartHeight=b.clientHeight;return 'start';})()");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-news-body'),s=getComputedStyle(b);return b.clientHeight<b.scrollHeight&&+s.opacity<.4&&s.transform!=='none'?'ready':''})()") == "ready", "pointer news disclosure starts at preview height with translation and scale", results);
                File.WriteAllBytes(Path.Combine(qa, "motion-news-start.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "window.qaNewsAnimations.forEach(function(a){a.currentTime=80;});'middle'");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-news-body'),s=getComputedStyle(b);return b.clientHeight>window.qaNewsStartHeight&&b.clientHeight<b.scrollHeight&&+s.opacity>.4&&+s.opacity<1&&s.transform!=='none'?'middle':''})()") == "middle", "news disclosure interpolates height, translation, scale and opacity in Chromium", results);
                File.WriteAllBytes(Path.Combine(qa, "motion-news-middle.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "window.qaNewsAnimations.forEach(function(a){a.currentTime=220;});'end'");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-news-body');return b.clientHeight>=b.scrollHeight&&getComputedStyle(b).transform==='none'?'full':''})()") == "full", "news expansion finishes at full height without a residual transform", results);
                File.WriteAllBytes(Path.Combine(qa, "motion-news-end.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-toggle').click();'collapsed'");
                Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-news-body');return b.getAttribute('data-collapsed')==='true'&&b.getAnimations().length===0?'cancelled':''})()") == "cancelled", "keyboard news collapse cancels movement immediately with no delayed reopening", results);
                await client.Emulation.SetEmulatedMediaAsync(features: new List<CefSharp.DevTools.Emulation.MediaFeature> { new CefSharp.DevTools.Emulation.MediaFeature { Name = "prefers-reduced-motion", Value = "reduce" } });
                await Task.Delay(50);
                Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('.univer-app-news-toggle'),b=document.querySelector('.univer-app-news-body');t.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:1}));var a=b.getAnimations()[0];a.pause();a.currentTime=0;return a.effect.getTiming().duration===180&&getComputedStyle(b).transform==='none'&&+getComputedStyle(b).opacity<1?'gentle':''})()") == "gentle", "reduced motion retains a gentle news fade", results);
                await AutomationController.EvaluateAsync(main, "window.__univerMotionUI.stop();'stopped'");
                await client.Emulation.SetEmulatedMediaAsync(features: new List<CefSharp.DevTools.Emulation.MediaFeature>());
            }
            prefs.Animations = savedAnimations; await ApplyPresentationAsync();
            await AutomationController.EvaluateAsync(main, "var b=document.querySelector('.univer-app-news-toggle');b.scrollIntoView({block:'center'});b.click();'collapsed'");
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-body').getAttribute('data-collapsed')==='true'&&document.querySelector('tr.news').getBoundingClientRect().top>=0?'returned':''") == "returned", "collapsing a long article returns to its start", results);
            Width = 900; await Task.Delay(150);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-news-toggle'),r=b.closest('tr').getBoundingClientRect(),t=b.getBoundingClientRect();return t.left>=r.left&&t.right<=r.right&&document.documentElement.scrollWidth<=innerWidth?'fits':''})()") == "fits", "news disclosure fits the narrow window without horizontal overflow", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "news-collapsed-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            using (var client = main.GetDevToolsClient())
            {
                // The isolated test window is hidden; emulate an active page for real DOM focus events.
                await client.Emulation.SetFocusEmulationEnabledAsync(true);
                try
                {
                    await AutomationController.EvaluateAsync(main, "document.getElementById('qa-news-tail').focus();'focused'");
                    Check(await AutomationController.EvaluateAsync(main, "document.activeElement.id==='qa-news-tail'&&document.querySelector('.univer-app-news-toggle').getAttribute('aria-expanded')==='true'?'accessible':''") == "accessible", "keyboard focus reveals links below the news preview", results);
                }
                finally { await client.Emulation.SetFocusEmulationEnabledAsync(false); }
            }
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-toggle').click();var q=document.querySelector('.univer-app-news-query');q.value='Последняя строка';q.dispatchEvent(new Event('input'));'filtered'");
            await Task.Delay(350);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('[data-univer-results] tr.news').length===1&&document.querySelector('[data-univer-results] .univer-app-news-toggle').getAttribute('aria-expanded')==='false'?'searchable':''") == "searchable", "search finds hidden news text and filtered articles also expand", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('[data-univer-results] .univer-app-news-toggle').click();window.__univerDesktop.apply(false,false,'ru');'original'");
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('.univer-app-news-body,.univer-app-news-toggle')&&document.getElementById('qa-news-tail')===window.qaNewsTail?'restored':''") == "restored", "Original unwraps news and preserves the original article link", results);
            Width = 1360;
            await AutomationController.EvaluateAsync(main, "sessionStorage.removeItem('univer.desktop.news-filter.v1');'cleared'");
            var many = NewsQaPage(NewsQaRow("08", "Синтетическая новость"), true);
            var fetch = "<script>sessionStorage.removeItem('univer.desktop.news-page-count.v1');window.fetch=async function(path){var p=+path.split('/').pop(),body=" + json.Serialize(many) + ";body=body.replace('Страница 1','Страница '+p).replace('/my/news/2','/my/news/'+(p+1));if(p>=20)body=body.replace(/<a href='[^']+'>2<\\/a>/,'');return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return body;}}};</script>";
            main.LoadHtml(heading + fetch + many, SitePolicy.Origin + "/news/student"); await WaitQaPresentationAsync();
            var countUntil = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < countUntil && await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-jump input')?.max==='20'?'ready':''") != "ready") await Task.Delay(100);
            Check(await AutomationController.EvaluateAsync(main, "Array.from(document.querySelectorAll('.univer-app-news-pager-top>a')).map(a=>a.textContent).join(',')==='1,2,3,4,5,20'&&document.querySelector('.univer-app-news-gap')&&document.querySelector('.univer-app-news-pager-top a[href=\"/news/student/3\"]')?'direct':''") == "direct", "unfiltered news offer direct first-five and last-page links with an ellipsis", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "news-unfiltered.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var second = NewsQaPage(NewsQaRow("07", "Новость QA 3") + NewsQaRow("06", "Новость QA 4"), false, 2);
            var empty = NewsQaPage("", false);
            fetch = "<script>sessionStorage.removeItem('univer.desktop.news-page-count.v1');window.fetch=async function(path){var p=+path.split('/').pop();return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return p===2?" + json.Serialize(second) + ":" + json.Serialize(empty) + ".replace('Страница 1','Страница '+p);}}};</script>";
            main.LoadHtml(heading + fetch + NewsQaPage(NewsQaRow("09", "Новость QA 1") + NewsQaRow("08", "Новость QA 2"), true), SitePolicy.Origin + "/news/student");
            await WaitQaPresentationAsync();
            await AutomationController.EvaluateAsync(main, "var from=document.querySelector('.univer-app-news-from');from.value='2026-10-06';from.dispatchEvent(new Event('input'));'filtered'");
            var until = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-count').textContent.endsWith('из 4')?'ready':''") != "ready") await Task.Delay(100);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-news-pager-top a').length===2?'two':''") == "two", "filtered news have exactly two result pages in Chromium", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-pager-top a:last-of-type').click();'second'");
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('[data-univer-results]').textContent.includes('Новость QA 3')&&document.querySelector('.univer-app-news-from').value==='2026-10-06'?'retained':''") == "retained", "second news page retains the selected dates", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "news-two-pages.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "var from=document.querySelector('.univer-app-news-from');from.value='2026-10-08';from.dispatchEvent(new Event('input'));'narrowed'");
            await Task.Delay(400);
            Check(await AutomationController.EvaluateAsync(main, "Array.from(document.querySelectorAll('.univer-app-news-pager')).every(n=>getComputedStyle(n).display==='none')&&getComputedStyle(document.querySelector('.univer-app-news-all')).display==='none'?'hidden':''") == "hidden", "single-page filtered news hide both pagers and the all-pages button", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "news-one-page.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await AutomationController.EvaluateAsync(main, "window.__univerDesktop.apply(false,false,'ru');'original'");
            Check(await AutomationController.EvaluateAsync(main, "!document.querySelector('.univer-app-news-tools')&&document.querySelectorAll('tr.news').length===2&&Array.from(document.querySelectorAll('tr.news')).every(n=>!n.hidden)?'original':''") == "original", "Original restores the source news nodes and their visibility", results);
            await AutomationController.EvaluateAsync(main, "sessionStorage.removeItem('univer.desktop.news-filter.v1');'cleared'");
        }
    }
}
