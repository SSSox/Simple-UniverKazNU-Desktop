using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // QA step "speed": how long the app keeps a page covered after the page itself is ready, how long the start takes and how
    // long the settings window takes to appear. A page of the site keeps loading images after DOMContentLoaded; the fixture
    // holds one picture back for SpeedQaSlowMs so the load event comes late, as it does on the real site.
    internal sealed partial class MainForm
    {
        private const int SpeedQaSlowMs = 1500, SpeedQaParserMs = 600, SpeedQaRuns = 7;
        private const string SpeedQaSlow = "/qa-slow/";
        private async Task SpeedQaAsync(List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            QaTrace("speed-startup process-to-main=" + StartupMs() + " main-to-cef=" + Program.CefReady + " main-to-window=" + Program.WindowShown + " main-to-first-page=" + Program.FirstReveal + " stages " + string.Join(" ", Program.Marks));
            await RestoreQaWindowAsync();
            prefs.Language = "ru"; prefs.Readable = true; await RefreshDocumentScriptsAsync();
            var original = main.ResourceRequestHandlerFactory;
            var pages = original as ResourceRequestHandlerFactory ?? new ResourceRequestHandlerFactory();
            var body = ReportQaJournal + PolishCourse("История Казахстана", "63", "12") + PolishCourse("Иностранный язык", "91", "45") + PolishCourse("Философия", "74", "50") + "</table>";
            var gaps = new List<double>(); var covered = new List<double>(); var loads = new List<double>(); var urls = new List<string>();
            main.ResourceRequestHandlerFactory = new SpeedQaFactory(pages, SitePolicy.Origin + SpeedQaSlow);
            // Self-test stops the 200 ms poll and ticks by hand; here the app runs as it does for a user.
            timer.Start();
            try
            {
                for (var run = 0; run < SpeedQaRuns; run++)
                {
                    var url = SitePolicy.Origin + "/student/attendance/full/?qa-speed=" + run; urls.Add(url);
                    // A script in the middle of the page holds the parser (the site's own scripts do) and a picture holds the load event.
                    var html = PolishShell(body + "<script src='" + SpeedQaSlow + "parser.js?run=" + run + "'></script><p>QA</p><img alt='' width='1' height='1' src='" + SpeedQaSlow + "pixel.gif?run=" + run + "'>", false);
                    pages.RegisterHandler(url, ResourceHandler.GetByteArray(html, Encoding.UTF8), "text/html", false);
                    var clock = Stopwatch.StartNew();
                    main.Load(url);
                    // Only the app's own machinery uncovers the page here: no QA tick is added.
                    while (clock.ElapsedMilliseconds < 10000 && !(await FixesEvalAsync("window.__univerPaintV1&&window.__univerPaintV1.shown>0&&location.search==='?qa-speed=" + run + "'?'shown':''") == "shown" && !loading.Visible)) await Task.Delay(20);
                    var shownMs = clock.ElapsedMilliseconds;
                    while (clock.ElapsedMilliseconds < 10000 && await FixesEvalAsync("document.readyState") != "complete") await Task.Delay(50);
                    var raw = await FixesEvalAsync("(function(){var n=performance.getEntriesByType('navigation')[0];return [n.domContentLoadedEventEnd,window.__univerPaintV1.shown,n.loadEventEnd].map(function(v){return v.toFixed(1)}).join('|')})()") ?? "";
                    var at = raw.Split('|').Select(v => { double d; return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.NaN; }).ToArray();
                    QaTrace("speed-page " + run + " dcl|shown|load=" + raw + " native-shown=" + shownMs + " bundle|view=" + await SpeedQaTimingAsync());
                    if (at.Length != 3 || at.Any(double.IsNaN)) continue;
                    // The first run warms the renderer and the script caches; the rest are the steady state.
                    if (run == 0) continue;
                    gaps.Add(at[1] - at[0]); covered.Add(at[1]); loads.Add(at[2] - at[0]);
                }
            }
            finally
            {
                timer.Stop();
                main.ResourceRequestHandlerFactory = original;
                foreach (var url in urls) pages.UnregisterHandler(url);
            }
            // A long table (the layout-load fixture): what the bundle and the reading view cost on a large page.
            for (var run = 0; run < 4; run++)
            {
                var rows = new StringBuilder();
                for (var i = 0; i < 1500; i++) rows.Append("<tr><td>").Append(i).Append("</td><td>Дисциплина ").Append(i).Append("</td><td>").Append(i % 100).Append("</td></tr>");
                main.LoadHtml("<table class='inner'><tr><th>№</th><th>Дисциплина</th><th>Балл</th></tr>" + rows + "</table>", SitePolicy.Origin + "/student/qa-load?run=" + run);
                await WaitQaPresentationAsync();
                QaTrace("speed-table " + run + " bundle|view=" + await SpeedQaTimingAsync());
            }
            var gap = Median(gaps); var load = Median(loads);
            QaTrace("speed-reveal median-gap-after-dcl=" + gap.ToString("0.0", CultureInfo.InvariantCulture) + " worst=" + (gaps.Count > 0 ? gaps.Max() : double.NaN).ToString("0.0", CultureInfo.InvariantCulture) +
                " median-shown=" + Median(covered).ToString("0.0", CultureInfo.InvariantCulture) + " median-load-after-dcl=" + load.ToString("0.0", CultureInfo.InvariantCulture));
            Check(gaps.Count == SpeedQaRuns - 1 && load >= SpeedQaSlowMs - SpeedQaParserMs - 200 && Median(covered) >= SpeedQaParserMs, "the speed fixture holds the parser and the load event back (" + load.ToString("0", CultureInfo.InvariantCulture) + " ms)", results);
            Check(gap < SpeedQaRevealLimit, "a prepared page is uncovered soon after DOMContentLoaded, not at the next poll or at the load event (median " + gap.ToString("0", CultureInfo.InvariantCulture) + " ms)", results);
            var worst = gaps.Count > 0 ? gaps.Max() : double.NaN;
            Check(worst < SpeedQaWorstLimit, "a read left unanswered by the page being replaced does not keep the next page covered (worst " + worst.ToString("0", CultureInfo.InvariantCulture) + " ms)", results);

            // The settings window: built and on screen. The first opening pays for JIT; the later ones are what a user sees again.
            var opened = new List<long>();
            for (var run = 0; run < 3; run++)
            {
                var clock = Stopwatch.StartNew(); long shown = 0;
                using (var form = new SettingsForm(null, prefs.Clone()))
                {
                    form.Shown += (s, e) => shown = clock.ElapsedMilliseconds;
                    form.Show(this);
                    while (shown == 0 && clock.ElapsedMilliseconds < 5000) await Task.Delay(10);
                    form.Close();
                }
                opened.Add(shown);
            }
            QaTrace("speed-settings first=" + opened[0] + " again=" + string.Join(",", opened.Skip(1)));
            Check(opened.All(x => x > 0), "the settings window opens", results);
        }
        // Before the page told the app it had parsed (18 Nov 2026, Modern x64): median 110–133 ms, worst 188 ms, the wait for
        // the next 200 ms poll.
        private const double SpeedQaRevealLimit = 60;
        // Before reads stopped waiting for the page being replaced (18 Nov 2026, Modern x64): 2.4 s in 3 of 5 runs, the
        // second page waiting out CEF's three-second deadline for the first page's probe.
        private const double SpeedQaWorstLimit = 1000;
        private async Task<string> SpeedQaTimingAsync()
        { return await FixesEvalAsync("window.__univerTimingV1?window.__univerTimingV1.bundle.toFixed(1)+'|'+window.__univerTimingV1.view.toFixed(1):''") ?? ""; }
        private static double Median(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = values.OrderBy(x => x).ToList();
            return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        }
        private static long StartupMs()
        {
            try { using (var process = Process.GetCurrentProcess()) return (long)(DateTime.Now - process.StartTime).TotalMilliseconds - Program.Clock.ElapsedMilliseconds; }
            catch (Exception) { return -1; }
        }
        // Everything as before, except the held-back picture.
        private sealed class SpeedQaFactory : IResourceRequestHandlerFactory
        {
            private readonly IResourceRequestHandlerFactory inner; private readonly string slow;
            public SpeedQaFactory(IResourceRequestHandlerFactory inner, string slow) { this.inner = inner; this.slow = slow; }
            public bool HasHandlers { get { return true; } }
            public IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
            {
                if (request.Url.StartsWith(slow, StringComparison.Ordinal)) return new SpeedQaRequest();
                return inner.HasHandlers ? inner.GetResourceRequestHandler(chromiumWebBrowser, browser, frame, request, isNavigation, isDownload, requestInitiator, ref disableDefaultHandling) : null;
            }
        }
        private sealed class SpeedQaRequest : CefSharp.Handler.ResourceRequestHandler
        {
            protected override IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) { return new SpeedQaPicture(); }
        }
        private sealed class SpeedQaPicture : ResourceHandler
        {
            private static readonly byte[] Pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            private static readonly byte[] Script = Encoding.UTF8.GetBytes("/* QA */");
            public override CefReturnValue ProcessRequestAsync(IRequest request, ICallback callback)
            {
                var script = request.Url.IndexOf(".js?", StringComparison.Ordinal) > 0;
                Task.Delay(script ? SpeedQaParserMs : SpeedQaSlowMs).ContinueWith(_ =>
                {
                    using (callback)
                    {
                        if (callback.IsDisposed) return;
                        var data = script ? Script : Pixel;
                        Stream = new MemoryStream(data); MimeType = script ? "text/javascript" : "image/gif"; StatusCode = 200; StatusText = "OK"; ResponseLength = data.Length;
                        callback.Continue();
                    }
                });
                return CefReturnValue.ContinueAsync;
            }
        }
    }
}
