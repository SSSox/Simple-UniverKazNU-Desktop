using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // QA step "online-test": «Онлайн тест» in real Chromium. The student's own click on the site's route and the site's GET
    // redirect to online-test.kaznu.kz open the portal, in the app's link window or by the road of outside links (here a
    // probe, never the user's browser), and the site window loads the page it came from again. A redirect the student did not click, and one to another
    // host, open nothing. The site page, its redirect and the portal are fixtures: no request leaves the machine.
    internal sealed partial class MainForm
    {
        private const string OnlineTestQaPortal = "https://online-test.kaznu.kz/qa-fixture/start";
        // QA: the link-window road, which self-test otherwise sends to the user's browser.
        private bool webInsideForQa;
        // QA: runs on the link window's new browser before Chromium creates it.
        private Action<ChromiumWebBrowser> webBrowserForQa;
        private async Task OnlineTestQaAsync(List<string> results)
        {
            var page = SitePolicy.Origin + "/student/TestList/";
            var route = SitePolicy.Origin + "/student/AuthorizationOnOnlineTestForm";
            var original = main.ResourceRequestHandlerFactory;
            var fixtures = new OnlineTestQaFactory(original);
            fixtures.Pages[page] = "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Тестирование</title></head><body><div id='l4'>" +
                "<a id='qa-online' href='/student/AuthorizationOnOnlineTestForm' style='display:inline-block;padding:16px 32px'>Онлайн тест</a></div></body></html>";
            fixtures.Pages[OnlineTestQaPortal] = "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Online test</title></head><body>QA portal</body></html>";
            fixtures.Redirects[route] = OnlineTestQaPortal;
            string opened = null;
            // Set throughout: whatever road the app takes, nothing reaches the user's browser.
            outsideProbe = url => { opened = url; return true; };
            main.ResourceRequestHandlerFactory = fixtures;
            for (var wait = DateTime.UtcNow.AddSeconds(15); !preparedBrowsers.Contains(main) && DateTime.UtcNow < wait;) await Task.Delay(50);
            try
            {
                // Self-test takes the road of outside links (to the user's browser), which here ends at the probe.
                main.Load(page); await WaitQaPresentationAsync();
                var loads = fixtures.Served(page);
                await PressOnlineTestQaAsync(fixtures, route);
                await WaitOnlineTestQaAsync(() => opened != null && fixtures.Served(page) > loads && !main.IsLoading, 5000);
                Check(opened == OnlineTestQaPortal && !WebShown, "the student's click on «Онлайн тест» and the site's redirect hand online-test.kaznu.kz to the road of outside links", results);
                Check(fixtures.Served(page) == loads + 1 && SitePolicy.SamePage(main.Address, page), "after «Онлайн тест» the site window loads the page it came from again", results);

                // The student's click, answered by a redirect to another site: nothing opens and the site window stays.
                // Served if it were ever followed, so not even a failure reaches the network.
                opened = null; await WaitQaPresentationAsync();
                fixtures.Pages["https://example.org/qa-elsewhere"] = "<!DOCTYPE html><title>Elsewhere</title>";
                fixtures.Redirects[route] = "https://example.org/qa-elsewhere";
                await PressOnlineTestQaAsync(fixtures, route); await Task.Delay(1500);
                Check(opened == null && !WebShown && SitePolicy.SamePage(main.Address, page), "after a click on «Онлайн тест» a redirect to another site opens nothing", results);

                // The link window, the road a student gets by default; the portal takes the exit (arming is proved by the first
                // sub-step, this one may still be armed by the click above).
                fixtures.Redirects[route] = OnlineTestQaPortal;
                webInsideForQa = true; webBrowserForQa = browser => browser.ResourceRequestHandlerFactory = fixtures;
                main.Load(page); await WaitQaPresentationAsync();
                loads = fixtures.Served(page);
                await PressOnlineTestQaAsync(fixtures, route);
                await WaitOnlineTestQaAsync(() => WebShown && webPanel.Browser != null && webPanel.Browser.IsBrowserInitialized && fixtures.Served(OnlineTestQaPortal) > 0 &&
                    !webPanel.Browser.IsLoading && webPanel.StateKey != "webLoading" && fixtures.Served(page) > loads && !main.IsLoading, 10000);
                var shown = WebShown && webPanel.Browser != null && webPanel.Browser.IsBrowserInitialized ? await webPanel.Browser.EvaluateScriptAsync("document.title+'|'+location.href") : null;
                var portal = shown != null && shown.Success ? shown.Result as string : null;
                Check(opened == null && WebShown && portal == "Online test|" + OnlineTestQaPortal && fixtures.Served(OnlineTestQaPortal) == 1,
                    "the student's click on «Онлайн тест» shows online-test.kaznu.kz in the app's link window (" + (portal ?? "nothing") + ")", results);
                Check(fixtures.Served(page) == loads + 1 && SitePolicy.SamePage(main.Address, page), "behind the link window the site window is back on the page it came from", results);
                CloseWeb();

                // A page script, not the student, asks for the route: the redirect to the portal opens nothing.
                await WaitQaPresentationAsync();
                var asked = fixtures.Served(route);
                await AutomationController.EvaluateAsync(main, "location.href='/student/AuthorizationOnOnlineTestForm';'ok'");
                await WaitOnlineTestQaAsync(() => fixtures.Served(route) > asked, 3000); await Task.Delay(1500);
                Check(fixtures.Served(route) > asked && opened == null && !WebShown && SitePolicy.SamePage(main.Address, page), "a redirect to online-test.kaznu.kz the student did not click opens nothing", results);
                // The refused navigation left the window covered: the page is shown again for the steps after this one.
                main.Load(page); await WaitQaPresentationAsync();
            }
            finally
            {
                main.ResourceRequestHandlerFactory = original;
                outsideProbe = null; webInsideForQa = false; webBrowserForQa = null; onlineTestReturn = null;
                // A browser Chromium is still creating is given up to 10 s before it is disposed (disposing it mid-creation can end the process).
                for (var wait = DateTime.UtcNow.AddSeconds(10); webPanel.Browser != null && !webPanel.Browser.IsBrowserInitialized && DateTime.UtcNow < wait;) await Task.Delay(50);
                CloseWeb();
            }
        }
        // A trusted press on the fixture's link, seen by the request it makes. DevTools input first; Chromium 109 may not route
        // it to the hidden test window, so the host's own click follows only when the route was not asked for in time.
        private async Task PressOnlineTestQaAsync(OnlineTestQaFactory fixtures, string route)
        {
            var at = (await AutomationController.EvaluateAsync(main, "(function(){var a=document.getElementById('qa-online');if(!a)return '';" +
                "document.documentElement.style.scrollBehavior='auto';a.scrollIntoView({block:'center'});var r=a.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)})()") ?? "").Split(',');
            if (at.Length != 2) throw new InvalidOperationException("The «Онлайн тест» fixture link is missing: " + main.Address);
            int x = int.Parse(at[0]), y = int.Parse(at[1]); var before = fixtures.Served(route);
            using (var client = main.GetDevToolsClient())
                foreach (var type in new[] { CefSharp.DevTools.Input.DispatchMouseEventType.MousePressed, CefSharp.DevTools.Input.DispatchMouseEventType.MouseReleased })
                    await client.Input.DispatchMouseEventAsync(type, x, y, button: CefSharp.DevTools.Input.MouseButton.Left, clickCount: 1);
            if (await WaitOnlineTestQaAsync(() => fixtures.Served(route) > before, 1500)) return;
            QaTrace("online-test host click");
            var host = main.GetBrowser().GetHost();
            host.SendMouseMoveEvent(new MouseEvent(x, y, CefEventFlags.None), false);
            host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.LeftMouseButton), MouseButtonType.Left, false, 1);
            host.SendMouseClickEvent(new MouseEvent(x, y, CefEventFlags.None), MouseButtonType.Left, true, 1);
            if (!await WaitOnlineTestQaAsync(() => fixtures.Served(route) > before, 3000)) throw new InvalidOperationException("The press on «Онлайн тест» did not reach the page");
        }
        private static async Task<bool> WaitOnlineTestQaAsync(Func<bool> reached, int milliseconds)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!reached()) { if (DateTime.UtcNow > until) return false; await Task.Delay(50); }
            return true;
        }
        // Fixture pages and redirects by exact address; every other request goes on as before.
        private sealed class OnlineTestQaFactory : IResourceRequestHandlerFactory
        {
            private readonly IResourceRequestHandlerFactory inner;
            private readonly ConcurrentDictionary<string, int> served = new ConcurrentDictionary<string, int>();
            public readonly ConcurrentDictionary<string, string> Pages = new ConcurrentDictionary<string, string>();
            public readonly ConcurrentDictionary<string, string> Redirects = new ConcurrentDictionary<string, string>();
            public OnlineTestQaFactory(IResourceRequestHandlerFactory inner) { this.inner = inner; }
            public int Served(string url) { int count; return served.TryGetValue(url, out count) ? count : 0; }
            public bool HasHandlers { get { return true; } }
            public IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
            {
                var url = request.Url; string body, target;
                if (Pages.TryGetValue(url, out body)) { served.AddOrUpdate(url, 1, (key, count) => count + 1); return new OnlineTestQaRequest(() => ResourceHandler.FromString(body, Encoding.UTF8, false, "text/html")); }
                if (Redirects.TryGetValue(url, out target))
                {
                    served.AddOrUpdate(url, 1, (key, count) => count + 1);
                    return new OnlineTestQaRequest(() =>
                    {
                        var answer = new ResourceHandler("text/html", new MemoryStream(new byte[0])) { StatusCode = 302, StatusText = "Found", ResponseLength = 0 };
                        answer.Headers.Add("Location", target);
                        return answer;
                    });
                }
                return inner != null && inner.HasHandlers ? inner.GetResourceRequestHandler(chromiumWebBrowser, browser, frame, request, isNavigation, isDownload, requestInitiator, ref disableDefaultHandling) : null;
            }
        }
        private sealed class OnlineTestQaRequest : CefSharp.Handler.ResourceRequestHandler
        {
            private readonly Func<IResourceHandler> answer;
            public OnlineTestQaRequest(Func<IResourceHandler> answer) { this.answer = answer; }
            protected override IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return answer(); }
        }
    }
}
