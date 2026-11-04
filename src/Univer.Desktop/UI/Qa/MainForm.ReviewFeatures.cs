#if QA_REVIEW
using System;
using System.IO;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Explicit --review-account only: passive course/print navigation and local news controls.
        // Reports contain counts and booleans, never document bodies or account fields.
        private async Task ReviewFeatureChecksAsync(string output, string prefix, string report)
        {
            var path = new Uri(main.Address).AbsolutePath;
            if (path.StartsWith("/student/attendance/full", StringComparison.Ordinal))
            {
                var source = main.Address;
                if (await WaitReviewFeatureAsync("document.querySelector('.univer-app-attendance-filters')?'ready':''"))
                {
                    await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-filters [data-kind=all]').click();'all'");
                    await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-courses button')?.click();'all courses'");
                    var labels = await AutomationController.EvaluateAsync(main, "JSON.stringify({labels:document.querySelectorAll('.univer-app-lesson-label').length,summaries:document.querySelectorAll('.univer-app-attendance-summary').length,sourceLabelsHidden:Array.from(document.querySelectorAll('a[data-univer-lesson-label]')).every(a=>getComputedStyle(a).display==='none'&&a.tabIndex===-1),overallVisible:Array.from(document.querySelectorAll('.univer-app-attendance-overall')).every(n=>n.getBoundingClientRect().height>0)})");
                    File.AppendAllText(report, "FEATURE lesson labels " + labels + "\n");
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-attendance-all-wide.png"));
                    await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-courses button:last-child')?.click();'last course'");
                    var subjects = await AutomationController.EvaluateAsync(main, "JSON.stringify({pages:document.querySelectorAll('.univer-app-attendance-courses button').length,visibleSubjects:Array.from(document.querySelectorAll('.univer-app-course-heading')).filter(n=>n.getBoundingClientRect().height>0).length,visibleTotals:Array.from(document.querySelectorAll('.univer-app-attendance-overall-value')).filter(n=>n.getBoundingClientRect().height>0).length,visibleSyllabi:Array.from(document.querySelectorAll('.univer-app-course-syllabus')).filter(n=>n.getBoundingClientRect().height>0).length})");
                    File.AppendAllText(report, "FEATURE subject pages " + subjects + "\n");
                    await Task.Delay(150); await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-attendance-subject-wide.png"));
                    var courseWidth = Width; Width = 900; await Task.Delay(150);
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-attendance-subject-narrow.png")); Width = courseWidth;
                    await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-courses button').click();'all courses'");
                    await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-filters [data-kind=seminar]').click();'seminars'");
                    var filtering = await AutomationController.EvaluateAsync(main, "JSON.stringify({otherSectionsHidden:Array.from(document.querySelectorAll('.univer-app-attendance-part[hidden]')).every(n=>n.getBoundingClientRect().height===0),overallVisible:Array.from(document.querySelectorAll('.univer-app-attendance-overall')).every(n=>n.getBoundingClientRect().height>0),summariesOnlySeminars:Array.from(document.querySelectorAll('.univer-app-attendance-summary strong')).every(n=>n.textContent==='Семинары'),hiddenSections:document.querySelectorAll('.univer-app-attendance-part[hidden]').length})");
                    File.AppendAllText(report, "FEATURE lesson filter " + filtering + "\n");
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-attendance-seminars-wide.png"));
                    var filterWidth = Width; Width = 900; await Task.Delay(200);
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-attendance-seminars-narrow.png")); Width = filterWidth;
                    await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-filters [data-kind=all]').click();'all'");
                }
                else File.AppendAllText(report, "FEATURE lesson filter BLOCKED controls\n");
                if (!await WaitReviewFeatureAsync("document.querySelector('.univer-app-attendance-course') ? 'ready' : ''"))
                { File.AppendAllText(report, "FEATURE attendance BLOCKED course links\n"); return; }
                var syllabusButtons = await AutomationController.EvaluateAsync(main, "JSON.stringify({subjects:document.querySelectorAll('.univer-app-course-heading').length,buttons:document.querySelectorAll('.univer-app-course-syllabus').length,sourceButtonsHidden:Array.from(document.querySelectorAll('a.syllabus-info')).every(a=>getComputedStyle(a).display==='none'),overallAtTop:Array.from(document.querySelectorAll('.univer-app-attendance-overall-value')).every(n=>n.getBoundingClientRect().height>0)})");
                File.AppendAllText(report, "FEATURE subject controls " + syllabusButtons + "\n");
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-course-syllabus')?.click();'clicked'");
                if (!await WaitReviewFeatureAsync("document.querySelector('.univer-app-syllabi li a') ? 'ready' : ''"))
                { File.AppendAllText(report, "FEATURE syllabus BLOCKED list\n"); return; }
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-syllabi').scrollIntoView({block:'center'});'scrolled'");
                await Task.Delay(200);
                await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-syllabi-wide.png"));
                var width = Width; Width = 900; await Task.Delay(200);
                await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-syllabi-narrow.png")); Width = width;
                var target = await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-course').href");
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-syllabi a[href*=\"/interactive/syllabus/print/\"]')?.click();'opened'");
                var until = DateTime.UtcNow.AddSeconds(15);
                while (document == null && DateTime.UtcNow < until) await Task.Delay(100);
                var viewerOpened = document != null && main.Address == source;
                if (viewerOpened) { await WaitReviewReadyAsync(); CloseDocument(); }
                var returned = document == null && main.Address == source;
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-course').click();'opened'");
                await Task.Delay(300); await WaitReviewReadyAsync();
                var courseOpened = main.Address == target;
                File.AppendAllText(report, "FEATURE attendance course=" + courseOpened + " syllabusViewer=" + viewerOpened + " return=" + returned + "\n");
                main.Load(source); await Task.Delay(300); await WaitReviewReadyAsync();
            }
            if (path.TrimEnd('/') == "/news/student")
            {
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-reset')?.click();'reset'");
                await Task.Delay(150);
                var folds = await AutomationController.EvaluateAsync(main, "JSON.stringify({articles:document.querySelectorAll('table.univer-app-news:not([data-univer-results]) tr.news').length,collapsible:Array.from(document.querySelectorAll('table.univer-app-news:not([data-univer-results]) .univer-app-news-toggle')).filter(b=>!b.hidden).length})");
                File.AppendAllText(report, "FEATURE news disclosure " + folds + "\n");
                var disclosure = await AutomationController.EvaluateAsync(main, "(function(){window.qaReviewNewsButton=Array.from(document.querySelectorAll('.univer-app-news-toggle')).find(b=>!b.hidden);return window.qaReviewNewsButton?'ready':''})()");
                if (disclosure == "ready")
                {
                    var closed = await AutomationController.EvaluateAsync(main, "(function(){var b=window.qaReviewNewsButton,n=document.getElementById(b.getAttribute('aria-controls'));n.closest('tr.news').scrollIntoView({block:'start'});return b.getAttribute('aria-expanded')==='false'&&n.clientHeight<n.scrollHeight?'true':'false'})()");
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-news-collapsed-wide.png"));
                    await AutomationController.EvaluateAsync(main, "window.qaReviewNewsButton.click();'expanded'");
                    var opened = await AutomationController.EvaluateAsync(main, "(function(){var b=window.qaReviewNewsButton,n=document.getElementById(b.getAttribute('aria-controls'));return b.getAttribute('aria-expanded')==='true'&&n.clientHeight>=n.scrollHeight?'true':'false'})()");
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-news-expanded-wide.png"));
                    await AutomationController.EvaluateAsync(main, "window.qaReviewNewsButton.click();'collapsed'");
                    var newsWidth = Width; Width = 900; await Task.Delay(150);
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-news-collapsed-narrow.png")); Width = newsWidth;
                    File.AppendAllText(report, "FEATURE news disclosure collapsed=" + closed + " expanded=" + opened + "\n");
                }
                if (await WaitReviewFeatureAsync("document.querySelector('.univer-app-news-jump input')?.max?'ready':''"))
                {
                    var unfiltered = await AutomationController.EvaluateAsync(main, "JSON.stringify({pages:Array.from(document.querySelectorAll('.univer-app-news-pager-top>a')).map(a=>+a.textContent),lastPage:+document.querySelector('.univer-app-news-jump input').max,ellipsis:!!document.querySelector('.univer-app-news-gap')})");
                    File.AppendAllText(report, "FEATURE unfiltered news " + unfiltered + "\n");
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-news-unfiltered.png"));
                    var third = await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-pager-top a[href=\"/news/student/3\"]')?'ready':''");
                    if (third == "ready")
                    {
                        var newsSource = main.Address;
                        await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-pager-top a[href=\"/news/student/3\"]').click();'third'");
                        await Task.Delay(300); await WaitReviewReadyAsync();
                        File.AppendAllText(report, "FEATURE direct page three " + (new Uri(main.Address).AbsolutePath.TrimEnd('/') == "/news/student/3") + "\n");
                        main.Load(newsSource); await Task.Delay(300); await WaitReviewReadyAsync();
                    }
                }
                else File.AppendAllText(report, "FEATURE unfiltered news BLOCKED count\n");
                await AutomationController.EvaluateAsync(main,
                    "(function(){var box=document.querySelector('table.univer-app-news .date'),parts=box.textContent.match(/(\\d{1,2})\\.(\\d{4})\\s*$/),day=box.querySelector('.day').textContent.trim();" +
                    "var value=parts[2]+'-'+parts[1].padStart(2,'0')+'-'+day.padStart(2,'0');" +
                    "var from=document.querySelector('.univer-app-news-from'),to=document.querySelector('.univer-app-news-to');from.value=to.value=value;from.dispatchEvent(new Event('input'));return 'filtered';})()");
                if (!await WaitReviewFeatureAsync("(function(){var n=document.querySelector('.univer-app-news-count');return n&&!n.textContent.includes('+')?'ready':''})()"))
                { File.AppendAllText(report, "FEATURE news BLOCKED search\n"); return; }
                var news = await AutomationController.EvaluateAsync(main,
                    "JSON.stringify({rows:document.querySelectorAll('[data-univer-results] tr.news').length,pagerHidden:Array.from(document.querySelectorAll('.univer-app-news-pager')).every(n=>n.hidden),searchHidden:document.querySelector('.univer-app-news-all').hidden,filterRetained:!!sessionStorage.getItem('univer.desktop.news-filter.v1')})");
                File.AppendAllText(report, "FEATURE news " + news + "\n");
                await AutomationController.EvaluateAsync(main, "window.scrollTo(0,0);'top'");
                await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-news-filtered.png"));
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-news-reset').click();'reset'");
            }
        }
        private async Task<bool> WaitReviewFeatureAsync(string script)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!closing && DateTime.UtcNow < until)
            {
                if (await AutomationController.EvaluateAsync(main, script) == "ready") return true;
                await Task.Delay(100);
            }
            return false;
        }
    }
}
#endif
