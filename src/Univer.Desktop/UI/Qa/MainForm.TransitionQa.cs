using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        // Developer check for dark flashes between pages: opens a few GET sections and samples the client area; the report
        // holds only timings and dark-pixel shares, the darkest frame stays in the private review captures.
        private async Task TransitionQaAsync()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Univer.Desktop", "Review");
            Directory.CreateDirectory(root);
            var report = Path.Combine(root, "transitions-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            var lines = new List<string>();
            var startup = Stopwatch.StartNew(); double startupWorst = 0; long startupDarkAt = -1;
            var until = DateTime.UtcNow.AddSeconds(40);
            while ((loading.Visible || main == null || !main.IsBrowserInitialized) && DateTime.UtcNow < until)
            {
                using (var shot = new Bitmap(ClientSize.Width, ClientSize.Height))
                {
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(PointToScreen(Point.Empty), Point.Empty, ClientSize);
                    var share = DarkShare(shot, new Rectangle(PointToClient(canvas.PointToScreen(Point.Empty)), canvas.ClientSize));
                    if (share > startupWorst) startupWorst = share; if (share > 0.05 && startupDarkAt < 0) startupDarkAt = startup.ElapsedMilliseconds;
                }
                await Task.Delay(15);
            }
            lines.Add("startup worst=" + startupWorst.ToString("0.000") + " firstDarkMs=" + startupDarkAt);
            await Task.Delay(1500);
            var routes = (Environment.GetEnvironmentVariable("UNIVER_QA_ROUTES") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (routes.Length == 0) routes = new[] { "/student/myschedule/", "/student/attendance/full/", "/student/bachelor/", "/ssc/", "/ssc/StatementList", "/news/student", "downloads", "/student/myschedule/", "click:/student/attendance/full/", "click:/news/student", "click:/student/umkd/" };
            foreach (var route in routes)
            {
                var frames = new List<Tuple<long, double, bool>>(); Bitmap worst = null; double worstShare = 0;
                var size = canvas.ClientSize; var origin = PointToClient(canvas.PointToScreen(Point.Empty));
                var handle = Handle; var full = ClientSize; var screenOrigin = PointToScreen(Point.Empty); var clock = Stopwatch.StartNew(); var stop = false;
                var sampler = Task.Run(() =>
                {
                    while (!Volatile.Read(ref stop))
                    {
                        using (var shot = new Bitmap(full.Width, full.Height, PixelFormat.Format32bppArgb))
                        {
                            // What the screen actually shows, including the compositor's own surfaces.
                            using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(screenOrigin, Point.Empty, full);
                            var share = DarkShare(shot, new Rectangle(origin, size));
                            frames.Add(Tuple.Create(clock.ElapsedMilliseconds, share, loading.Visible));
                            if (share > worstShare) { worstShare = share; worst?.Dispose(); worst = (Bitmap)shot.Clone(); }
                        }
                        Thread.Sleep(10);
                    }
                });
                if (route == "downloads") ShowDownloads();
                else if (route.StartsWith("click:", StringComparison.Ordinal))
                    // A link followed inside the page, as a student's click does (renderer-initiated navigation).
                    await Univer.Desktop.Web.AutomationController.EvaluateAsync(main, "(function(){var a=document.createElement('a');a.href='" + route.Substring(6) + "';document.body.appendChild(a);a.click();return 'ok';})()");
                else { HideDownloads(); main.Load(SitePolicy.Origin + route); }
                await Task.Delay(5000);
                Volatile.Write(ref stop, true); await sampler;
                var dark = frames.FindAll(f => f.Item2 > 0.05);
                // Counts only: whether the tab-page panels found their parts on the live site.
                var shape = await Univer.Desktop.Web.AutomationController.EvaluateAsync(main, "JSON.stringify({gpa:document.querySelectorAll('.univer-app-gpa>div').length,panels:document.querySelectorAll('.univer-app-panel').length," +
                    "overview:Array.from(document.querySelectorAll('.univer-app-overview')).map(function(o){return o.querySelector('.univer-app-pending')?'loading':o.querySelector('.univer-app-overview-empty')?'empty':'items'+o.querySelectorAll('li').length;})," +
                    "credentials:document.querySelectorAll('.univer-app-credential').length,cards:document.querySelectorAll('.univer-app-service-card').length,tabs:document.querySelectorAll('.univer-app-tab-icon').length,hiddenTabs:document.querySelectorAll('.univer-app-tab-hidden').length,selects:document.querySelectorAll('#l4 select').length,tiles:document.querySelectorAll('.univer-app-quick .univer-app-tile').length," +
                    // Site structure without personal text: link routes with numbers masked, and short headings that are site labels.
                    "links:Array.from(document.querySelectorAll('#l4 a[href]')).filter(function(a){return !a.closest('#tools,.univer-app-menu,.univer-app-overview,.univer-app-quick');}).map(function(a){try{return new URL(a.href).pathname.replace(/[0-9]+/g,'#');}catch(e){return '';}}).slice(0,20)," +
                    "heads:Array.from(document.querySelectorAll('#l4 td.ct,#l4 h1,#l4 h2,#l4 h3')).map(function(c){return (c.textContent||'').replace(/\\s+/g,' ').trim();}).filter(function(t){return t&&t.length<40&&!/[0-9@]/.test(t);}).slice(0,20)," +
                    // Site code and structure only (attributes with numbers masked, no cell text).
                    "week:((document.getElementById('weekSelect')||{}).outerHTML||'').replace(/>[^<]*</g,'><').replace(/[0-9]+/g,'#').slice(0,300)," +
                    "weekCode:Array.from(document.scripts).map(function(s){return s.textContent;}).filter(function(t){return /weekSelect|myschedule|statement|Statement/.test(t);}).map(function(t){var i=t.search(/weekSelect|myschedule|statement|Statement/);return t.slice(Math.max(0,i-200),i+400).replace(/[0-9]{3,}/g,'#');}).slice(0,3)," +
                    "rows:Array.from(document.querySelectorAll('#l4 table tr')).filter(function(r){return r.cells.length>4&&!r.closest('#tools');}).slice(0,2).map(function(r){return r.outerHTML.replace(/>[^<]+</g,'><').replace(/[0-9]+/g,'#').slice(0,700);})," +
                    "study:Array.from(document.querySelectorAll('td.univer-app-panel-body')).map(function(c){return c.innerHTML.replace(/>[^<]+</g,'><').replace(/[0-9]+/g,'#').slice(0,500);})," +
                    "buttons:Array.from(document.querySelectorAll('#l4 input[type=button],#l4 input[type=submit],#l4 button,#l4 .ts>*,#l4 [onclick]')).filter(function(e){return !e.closest('#tools,.univer-app-menu,.univer-app-overview,.univer-app-quick,.univer-app-credentials,.univer-app-ssc-nav');}).map(function(e){return e.tagName+'|'+(e.className||'')+'|'+(e.getAttribute('href')||'').replace(/[0-9]+/g,'#')+'|'+(e.getAttribute('onclick')||'').replace(/[0-9]+/g,'#').slice(0,90)+'|'+(e.value||e.textContent||'').replace(/\\s+/g,' ').trim().slice(0,24);}).slice(0,12)," +
                    "mt:Array.from(document.querySelectorAll('#l4 table.mt')).map(function(t){return (t.classList.contains('univer-app-panel')?'P':'-')+t.querySelectorAll('td.ct').length;})})");
                lines.Add(route + " frames=" + frames.Count + " worst=" + worstShare.ToString("0.000") + " darkFrames=" + dark.Count +
                    " shape=" + shape + (dark.Count > 0 ? " firstDarkMs=" + dark[0].Item1 + " lastDarkMs=" + dark[dark.Count - 1].Item1 + " loadingVisible=" + dark[0].Item3 : ""));
                if (worst != null && worstShare > 0.05) worst.Save(Path.Combine(root, "transition-dark-" + route.Trim('/').Replace('/', '-') + ".png"));
                worst?.Dispose();
            }
            File.WriteAllLines(report, lines);
            Close();
        }
        private static double DarkShare(Bitmap shot, Rectangle area)
        {
            area.Intersect(new Rectangle(Point.Empty, shot.Size)); if (area.Width <= 0 || area.Height <= 0) return 0;
            int dark = 0, total = 0;
            for (var y = area.Top; y < area.Bottom; y += 8)
                for (var x = area.Left; x < area.Right; x += 8)
                { var c = shot.GetPixel(x, y); total++; if (c.R < 40 && c.G < 40 && c.B < 40) dark++; }
            return total == 0 ? 0 : dark / (double)total;
        }
    }
}
