#if QA_REVIEW
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private sealed class ReviewLink { public string Url; public string Title; public bool Tabs; public int Course = -1; }
        // Main-frame navigation chain (paths only, numbers masked) shows server redirects in the report.
        private readonly List<string> reviewTrail = new List<string>();
        private void ReviewNavigated(string url, string method) { if (reviewTrail.Count < 20 && SitePolicy.Trusted(url)) reviewTrail.Add(method + " " + SafeReviewRoute(url)); }
        private async Task ReviewAccountAsync()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Univer.Desktop", "Review");
            var output = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(root, "latest-path.txt"), output);
            var report = Path.Combine(output, "report.txt");
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--review-credentials") >= 0)
                {
                    if (!await WaitReviewReadyAsync()) { File.AppendAllText(report, "BLOCKED credentials structure: session unavailable\n"); return; }
                    main.Load(SitePolicy.Origin + "/student/bachelor/"); await Task.Delay(300);
                    if (!await WaitReviewReadyAsync()) { File.AppendAllText(report, "BLOCKED credentials structure: page unavailable\n"); return; }
                    await ApplyPresentationAsync(); await Task.Delay(500);
                    var script = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "review-credentials.js"));
                    var diagnostics = await AutomationController.EvaluateAsync(main, script);
                    if (diagnostics != null) File.WriteAllText(Path.Combine(output, "credentials-structure.json"), diagnostics);
                    File.AppendAllText(report, "DONE credentials structure only; no fields or captures\n"); return;
                }
                var targetsFile = Path.Combine(root, "targets.txt"); var targeted = !reviewAll && File.Exists(targetsFile);
                var explore = Array.IndexOf(Environment.GetCommandLineArgs(), "--review-explore") >= 0 && File.Exists(Path.Combine(root, "explore.txt"));
                if (!await WaitReviewReadyAsync())
                {
                    // Diagnostic state only: never record fields, credentials, cookies or response bodies.
                    var state = await AutomationController.EvaluateAsync(Active, "JSON.stringify({ready:document.readyState,login:!!document.querySelector('#login_frm'),reauth:!!document.querySelector('#auth_modal'),adapter:!!window.__univerAutomationV1})");
                    // Without credentials in memory the site's read notice on the home page stays unconfirmed; a live
                    // session can still open the listed pages directly, and each one is checked on its own below.
                    var direct = (targeted || explore) && credentials == null && Array.IndexOf(Environment.GetCommandLineArgs(), "--review-no-login") >= 0 && state != null && state.Contains("\"login\":false");
                    File.AppendAllText(report, (direct ? "INITIAL not ready, opening listed pages directly; state=" : "BLOCKED initial authenticated page; state=") + state + "; saved=" + (credentials != null) +
                        "; paused=" + prefs.AutoLoginPaused + "; guard=" + guard.Blocked + "; error=" + error + "; code=" + failureCode + "; message=" + messages.Current?.Key + "\n");
                    if (!direct)
                    {
                        if (Active != null && Active.IsBrowserInitialized) await SaveReviewCaptureAsync(Path.Combine(output, "blocked-initial.png"));
                        return;
                    }
                }
                if (explore) { await ExploreReviewAsync(root, output, report); return; }
                var queue = new Queue<ReviewLink>(); var seen = new HashSet<string>(StringComparer.Ordinal); var queued = new HashSet<string>(StringComparer.Ordinal);
                if (targeted) foreach (var target in File.ReadAllLines(targetsFile))
                {
                    var url = SitePolicy.Origin + target.Trim(); if (ReviewRoute(url)) queue.Enqueue(new ReviewLink { Url = url, Title = "Повторная проверка", Tabs = false });
                }
                else queue.Enqueue(new ReviewLink { Url = main.Address, Title = "Главная страница", Tabs = true });
                var menus = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; queue.Count > 0 && index < 200; index++)
                {
                    var item = queue.Dequeue(); var identity = item.Url + (item.Course >= 0 ? "#course-" + item.Course : "");
                    if (!seen.Add(identity)) { index--; continue; }
                    CloseDocument();
                    if (SitePolicy.PrintDocument(item.Url)) OpenDocument(item.Url);
                    else if (main.Address != item.Url || item.Course >= 0) main.Load(item.Url);
                    await Task.Delay(300);
                    if (!await WaitReviewReadyAsync()) { File.AppendAllText(report, "BLOCKED " + SafeReviewTitle(item.Title) + "\n"); continue; }
                    if (item.Course >= 0)
                    {
                        await AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelectorAll('.univer-app-course-open')[" + item.Course + "];if(b)b.click();return 'opened';})()");
                        await Task.Delay(350);
                        if (!await WaitReviewReadyAsync()) { File.AppendAllText(report, "BLOCKED course\n"); continue; }
                    }
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "--review-outline") >= 0)
                    {
                        // The outline describes the site's own markup, so it is read in Original mode before the adapter runs.
                        prefs.Readable = false; await ApplyPresentationAsync(); await Task.Delay(300);
                        var outline = await AutomationController.EvaluateAsync(Active, File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "review-outline.js")));
                        if (outline != null) File.WriteAllText(Path.Combine(output, index.ToString("D3") + "-outline.json"), outline);
                        prefs.Readable = true;
                    }
                    await ApplyPresentationAsync(); await Task.Delay(200);
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "--review-feature-fixes") >= 0)
                        await ReviewFeatureChecksAsync(output, index.ToString("D3"), report);
                    var links = await AutomationController.EvaluateAsync(Active, "JSON.stringify(Array.from(document.querySelectorAll('#tools a[href],#l3 a[href],.univer-app-quick a[href]" + (item.Tabs ? ",#l4 .ts a[href],#l4 a[href]" : "") + "')).filter(function(a){return a.closest('#tools,#l3,.univer-app-quick,.ts') || /^\\/student\\/(iup|transcript|recbook|interactive\\/syllabus\\/print)(\\/|$)/.test(new URL(a.href).pathname);}).map(function(a){return {url:a.href,title:a.textContent.trim(),menu:a.closest('#tools,#l3,.univer-app-quick')?'yes':''};}))");
                    if (links != null)
                        foreach (var value in new JavaScriptSerializer().Deserialize<List<Dictionary<string, string>>>(links))
                        {
                            var url = value["url"];
                            var menuLink = value["menu"] == "yes";
                            if (menuLink) menus.Add(url);
                            if (!targeted && ReviewRoute(url) && !seen.Contains(url) && queued.Add(url)) queue.Enqueue(new ReviewLink { Url = url, Title = value["title"], Tabs = menuLink });
                        }
                    if (item.Course < 0 && new Uri(Active.Address).AbsolutePath.TrimEnd('/') == "/student/umkd")
                    {
                        var count = await AutomationController.EvaluateAsync(Active, "String(document.querySelectorAll('.univer-app-course-open').length)");
                        int courseCount; if (int.TryParse(count, out courseCount)) for (var course = 0; course < courseCount; course++)
                            queue.Enqueue(new ReviewLink { Url = Active.Address, Title = "Учебные материалы, дисциплина " + (course + 1), Course = course, Tabs = true });
                    }
                    // Open read-only content; never submit forms, start tests or request services.
                    await AutomationController.EvaluateAsync(Active, "document.querySelectorAll('.univer-app-faq,.univer-app-catalog').forEach(function(d){d.open=true});'expanded'");
                    await Task.Delay(150);
                    var prefix = index.ToString("D3");
                    var probe = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "review-probe.js"));
                    var diagnostics = await AutomationController.EvaluateAsync(Active, probe);
                    if (diagnostics != null) File.WriteAllText(Path.Combine(output, prefix + "-interface.json"), diagnostics);
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-wide.png"));
                    var structure = await AutomationController.EvaluateAsync(Active, "JSON.stringify(Array.from(document.querySelectorAll('#l4 table,html.univer-app-document table')).slice(0,70).map(function(t){return {cls:t.className,width:t.getBoundingClientRect().width,layout:getComputedStyle(t).tableLayout,display:getComputedStyle(t).display,rows:Array.from(t.rows).filter(function(r){return r.closest('table')===t}).slice(0,4).map(function(r){return Array.from(r.cells).map(function(c){var s=getComputedStyle(c);return {tag:c.tagName,cls:c.className,width:c.getBoundingClientRect().width,height:c.getBoundingClientRect().height,display:s.display,minWidth:s.minWidth,span:c.colSpan,chars:c.textContent.trim().length};});})};}))");
                    if (structure != null) File.WriteAllText(Path.Combine(output, prefix + "-structure.json"), structure);
                    var frames = await AutomationController.EvaluateAsync(Active, "JSON.stringify(Array.from(document.querySelectorAll('iframe,frame')).map(function(f){try{var d=f.contentDocument;if(f.contentWindow.location.origin!==location.origin)return {external:true};return {path:f.contentWindow.location.pathname.replace(/\\d+/g,'{id}'),tables:Array.from(d.querySelectorAll('table')).map(function(t){return {cls:t.className,width:t.getBoundingClientRect().width,rows:Array.from(t.rows).filter(function(r){return r.closest('table')===t}).slice(0,3).map(function(r){return Array.from(r.cells).map(function(c){return {tag:c.tagName,cls:c.className,span:c.colSpan,chars:c.textContent.trim().length};});})};})};}catch(e){return {external:true}}}))");
                    if (frames != null) File.WriteAllText(Path.Combine(output, prefix + "-frames.json"), frames);
                    var blocks = await AutomationController.EvaluateAsync(Active, "JSON.stringify(Array.from(document.querySelectorAll('#l4 th,#l4 div')).filter(function(e){return e.tagName==='TH' || getComputedStyle(e).cssFloat!=='none'}).slice(0,60).map(function(e){function node(n,depth){var s=getComputedStyle(n);return {tag:n.tagName,cls:n.className,display:s.display,float:s.cssFloat,width:n.getBoundingClientRect().width,children:depth?Array.from(n.children).filter(function(c){return !/SCRIPT|STYLE/.test(c.tagName)}).slice(0,12).map(function(c){return node(c,depth-1)}):[]};}return node(e,3);}));");
                    if (blocks != null) File.WriteAllText(Path.Combine(output, prefix + "-blocks.json"), blocks);
                    var heightText = await AutomationController.EvaluateAsync(Active, "String(document.documentElement.scrollHeight)");
                    int height; if (int.TryParse(heightText, out height) && height > 1500)
                        for (var part = 1; part <= Math.Min(4, height / 650); part++)
                        {
                            await AutomationController.EvaluateAsync(Active, "window.scrollTo(0," + (part * 650) + ");'part'"); await Task.Delay(80);
                            await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-part-" + part.ToString("D2") + ".png"));
                        }
                    await AutomationController.EvaluateAsync(Active, "window.scrollTo(0,document.documentElement.scrollHeight);'bottom'"); await Task.Delay(150);
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-bottom.png"));
                    await AutomationController.EvaluateAsync(Active, "window.scrollTo(0,0);'top'");
                    File.AppendAllText(report, prefix + " " + SafeReviewTitle(item.Title) + " " + SafeReviewRoute(Active.Address) + " " + await ReviewGeometryAsync() + "\n");
                    if (reviewTrail.Count > 0) { File.AppendAllText(report, prefix + " trail " + string.Join(" -> ", reviewTrail) + "\n"); reviewTrail.Clear(); }
                    Width = 900; await Task.Delay(250);
                    diagnostics = await AutomationController.EvaluateAsync(Active, probe);
                    if (diagnostics != null) File.WriteAllText(Path.Combine(output, prefix + "-interface-narrow.json"), diagnostics);
                    await SaveReviewCaptureAsync(Path.Combine(output, prefix + "-narrow.png"));
                    File.AppendAllText(report, prefix + " narrow " + await ReviewGeometryAsync() + "\n");
                    Width = 1360; await Task.Delay(200);
                }
                File.AppendAllText(report, "DONE visited=" + seen.Count + " remaining=" + queue.Count + " menuRoutes=" + menus.Count + "\n");
            }
            catch (Exception exception) { File.AppendAllText(report, "ERROR " + exception.GetType().Name + "\n"); }
            finally { Close(); }
        }
        private async Task<bool> WaitReviewReadyAsync()
        {
            var until = DateTime.UtcNow.AddSeconds(50);
            while (!closing && DateTime.UtcNow < until)
            {
                if (guard.Blocked || error) return false;
                var ready = await AutomationController.EvaluateAsync(Active, "window.__univerAutomationV1 && document.readyState==='complete' && !window.__univerAutomationV1.probe().hasPendingForm && !window.__univerAutomationV1.probe().loginPage && window.__univerAutomationV1.probe().hasContent ? 'ready' : ''");
                if (ready == "ready" && !Active.IsLoading && !loading.Visible) return true;
                await Task.Delay(150);
            }
            return false;
        }
        private async Task SaveReviewCaptureAsync(string path)
        { using (var client = Active.GetDevToolsClient()) File.WriteAllBytes(path, (await client.Page.CaptureScreenshotAsync()).Data); }
        private Task<string> ReviewGeometryAsync()
        {
            return AutomationController.EvaluateAsync(Active, "JSON.stringify({width:innerWidth,scroll:document.documentElement.scrollWidth,inputs:Array.from(document.querySelectorAll('input:not([type=hidden]),select,textarea,button')).filter(function(e){var r=e.getBoundingClientRect();return r.width>0 && (r.left<0 || r.right>innerWidth+2);}).length,tables:document.querySelectorAll('.univer-app-data').length,matrices:document.querySelectorAll('.univer-app-matrix').length,menu:!!document.querySelector('.univer-app-menu'),tabs:document.querySelectorAll('#l3 a').length,details:document.querySelectorAll('.univer-app-faq[open],.univer-app-catalog[open]').length,lessons:document.querySelectorAll('.univer-app-lesson').length})");
        }
        private static bool ReviewRoute(string url)
        {
            if (!SitePolicy.Trusted(url) || SitePolicy.Logout(url) || SitePolicy.ProtectedDownload(url) || SitePolicy.Language(url) != null) return false;
            if (SitePolicy.PrintDocument(url)) return true;
            var path = new Uri(url).AbsolutePath;
            return Regex.IsMatch(path, @"^/(student|news|ssc|my)(/|$)", RegexOptions.IgnoreCase) && !Regex.IsMatch(path, @"/(delete|remove|save|set|confirm|submit|cancel|logout|download|send|docsget|get|start|begin|vote|pay|accept|activate)(/|$)", RegexOptions.IgnoreCase);
        }
        private static string SafeReviewTitle(string value) { return Regex.Replace(value ?? "", @"\s+", " ").Trim(); }
        private static string SafeReviewRoute(string value) { return Regex.Replace(new Uri(value).AbsolutePath, @"\d+", "{id}"); }
    }
}
#endif
