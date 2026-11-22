using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // --qa-flash: a real window in an isolated profile, for about ten seconds. Only the screen shows what Windows composes,
    // so frames are read from there; they are counted and dropped, never saved, and a frame is skipped unless this window
    // is what lies under it. A child that never paints shows what is under the website, and the surfaces that open over
    // the website open and close while frames are counted.
    internal sealed partial class MainForm
    {
        private sealed class HoleProbe : Control
        {
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x000F) { ValidateRect(Handle, IntPtr.Zero); return; }
                if (m.Msg == 0x0014) { m.Result = (IntPtr)1; return; }
                base.WndProc(ref m);
            }
            [DllImport("user32.dll")] private static extern bool ValidateRect(IntPtr window, IntPtr rect);
        }
        [StructLayout(LayoutKind.Sequential)] private struct FlashPoint { public int X, Y; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(FlashPoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        // The check must not take the keyboard from whoever is working at this computer.
        protected override bool ShowWithoutActivation { get { return Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-flash") >= 0; } }
        private static string FlashReport { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-flash.txt"); } }
        private static bool OwnsPoint(IntPtr form, Point at) { return GetAncestor(WindowFromPoint(new FlashPoint { X = at.X, Y = at.Y }), 2) == form; }
        private async Task FlashQaAsync()
        {
            try { await FlashStepsAsync(); }
            catch (Exception error) { File.AppendAllText(FlashReport, error.GetType().Name + ": " + error.Message + "\r\n"); }
            noticesQa = false; TopMost = false; Close();
        }
        private async Task FlashStepsAsync()
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            Size = new Size(1000, 640); var area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right - Width - 8, area.Bottom - Height - 8); TopMost = true; await Task.Delay(300);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-flash-downloads") >= 0) { await FlashDownloadsAsync(); return; }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-flash-reveal") >= 0) { await FlashRevealAsync(); return; }
            noticesQa = true;
            notices = new List<SiteNotice>
            {
                new SiteNotice { Key = "a", Title = "Анкетирование студентов", Url = SitePolicy.Origin + "/student/rating/", Count = 2, Open = 2, Page = "/student/" },
                new SiteNotice { Key = "b", Kind = SiteNotice.Blocked, Title = "Журнал", Text = "Пройдите анкету, чтобы открыть раздел", Url = SitePolicy.Origin + "/student/rating/", Page = "/student/" }
            };
            main.LoadHtml(PolishShell("<div style='height:1800px;background:#fff'><table class='mt'><tr class='top'><td class='ct'>Страница QA</td></tr></table></div>", false), SitePolicy.Origin + "/news/student"); await Task.Delay(1500);
            RenderNotices();
            using (var probe = new HoleProbe())
            {
                workspace.Controls.Add(probe); probe.SetBounds(300, 200, 300, 200); probe.BringToFront(); await Task.Delay(350);
                var at = workspace.PointToScreen(new Point(450, 300));
                using (var pixel = new Bitmap(1, 1))
                {
                    using (var g = Graphics.FromImage(pixel)) g.CopyFromScreen(at, Point.Empty, new Size(1, 1));
                    File.AppendAllText(FlashReport, "unpainted child: " + (OwnsPoint(Handle, at) ? pixel.GetPixel(0, 0).ToString() : "covered") + " page colour: " + canvas.BackColor + "\r\n");
                }
                workspace.Controls.Remove(probe); await Task.Delay(200);
            }
            File.AppendAllText(FlashReport, await FlashRunAsync("notices", 3, async () => { ShowNotices(); await Task.Delay(420); HideNotices(); await Task.Delay(320); }) + "\r\n");
            File.AppendAllText(FlashReport, await FlashRunAsync("find", 3, async () => { OpenFind(); await Task.Delay(320); findBar.Close(); await Task.Delay(240); }) + "\r\n");
        }
        // The uncover after a page load: how soon the page is on screen after its navigation began, and whether a frame shows
        // neither the placeholder nor the page once the placeholder is gone. The page is one colour over the whole window, so
        // a single screen patch says which is shown. The old fixed 48 ms wait and the frame signal run in turns.
        private async Task FlashRevealAsync()
        {
            var original = main.ResourceRequestHandlerFactory; var pages = original as ResourceRequestHandlerFactory ?? new ResourceRequestHandlerFactory();
            main.ResourceRequestHandlerFactory = pages;
            var lines = new List<string>();
            for (var round = 0; round < 12; round++)
            {
                var fixedWait = round % 2 == 0; revealFixedForQa = fixedWait;
                var url = SitePolicy.Origin + "/news/student?qa-reveal=" + round;
                pages.RegisterHandler(url, ResourceHandler.GetByteArray(PolishShell("<div style='position:fixed;inset:0;background:#c2185b;z-index:2147483647'></div><p>QA</p>", false), System.Text.Encoding.UTF8), "text/html", true);
                var patch = canvas.PointToScreen(new Point(canvas.Width / 2, canvas.Height / 2));
                var frames = new List<Tuple<long, bool, bool>>(); var stop = false; var lastColour = Color.Empty; var clock = System.Diagnostics.Stopwatch.StartNew();
                var sampler = Task.Run(() =>
                {
                    using (var shot = new Bitmap(2, 2))
                    using (var g = Graphics.FromImage(shot))
                        while (!stop && clock.ElapsedMilliseconds < 4000)
                        {
                            g.CopyFromScreen(patch, Point.Empty, new Size(2, 2));
                            var c = shot.GetPixel(0, 0); lastColour = c;
                            frames.Add(Tuple.Create(clock.ElapsedMilliseconds, loading.Visible, Math.Abs(c.R - 0xc2) < 24 && Math.Abs(c.G - 0x18) < 24 && Math.Abs(c.B - 0x5b) < 24));
                            System.Threading.Thread.Sleep(2);
                        }
                });
                main.Load(url);
                while (clock.ElapsedMilliseconds < 4000 && !(frames.Count > 0 && !loading.Visible && frames[frames.Count - 1].Item3)) await Task.Delay(10);
                await Task.Delay(300); stop = true; await sampler;
                var uncover = frames.FirstOrDefault(f => !f.Item2 && f.Item1 > 50);
                var shown = frames.FirstOrDefault(f => f.Item3);
                var blank = uncover == null ? -1 : frames.Count(f => f.Item1 >= uncover.Item1 && !f.Item2 && !f.Item3 && shown != null && f.Item1 < shown.Item1 + 1000 && f.Item1 >= uncover.Item1);
                lines.Add((fixedWait ? "fixed48" : "frames ") + " round=" + round + " uncoverMs=" + (uncover?.Item1 ?? -1) + " pageOnScreenMs=" + (shown?.Item1 ?? -1) + " waitMs=" + revealWaitForQa + " blankSamplesAfterUncover=" + blank + " samples=" + frames.Count + " owned=" + OwnsPoint(Handle, patch) + " last=" + ColorTranslator.ToHtml(lastColour) + " url=" + main.Address);
                pages.UnregisterHandler(url);
                await Task.Delay(400);
            }
            revealFixedForQa = false; main.ResourceRequestHandlerFactory = original;
            File.AppendAllText(FlashReport, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
        // How long Downloads takes to follow the window, the zoom and a filter with its painting done, and whether every
        // control survives a repaint of the panel under it. Pixels are read from the screen and dropped.
        private async Task FlashDownloadsAsync()
        {
            var now = DateTime.Now; var kinds = new[] { ".pdf", ".docx", ".pptx", ".doc" };
            downloadsView.SetItems(Enumerable.Range(0, 70).Select(i => new DownloadRecord { Id = "qa" + i, FileName = "Лекция " + i + kinds[i % 4], State = DownloadState.Completed, Total = 40000 + i, Received = 40000 + i, Source = "Культурология", Started = now.AddHours(-5 * i), Finished = now.AddHours(-5 * i) }));
            ShowDownloads(); await Task.Delay(900);
            Width -= 56; Kit.PaintNow(this); Width += 56; Kit.PaintNow(this); await Task.Delay(200);
            var blank = await FlashBlankAsync();
            TopMost = false; Location = new Point(-4000, 200); await Task.Delay(200);
            var resize = FlashTime(12, i => Width += i % 2 == 0 ? -56 : 56);
            var zoom = FlashTime(6, i => { zoomPercent = i % 2 == 0 ? 120 : 100; ApplyZoom(); });
            var chip = downloadsView.ChipsForQa.Where(c => c.Visible).ToList();
            var filter = FlashTime(6, i => chip[i % 2 == 0 ? 1 : 0].PerformClick());
            File.AppendAllText(FlashReport, "downloads: rows=" + downloadsView.RowsForQa + " resize=" + resize.ToString("F1") + "ms zoom=" + zoom.ToString("F1") + "ms filter=" + filter.ToString("F1") + "ms settled=" + downloadsView.RowsSettledForQa + " " + blank + "\r\n");
        }
        private double FlashTime(int times, Action<int> step)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < times; i++) { step(i); Kit.PaintNow(this); }
            return watch.Elapsed.TotalMilliseconds / times;
        }
        private async Task<string> FlashBlankAsync()
        {
            canvas.Invalidate(false); canvas.Update(); await Task.Delay(200);
            var first = FlashBlankCount(); downloadsView.Refresh(); await Task.Delay(200);
            return "blank=" + first + " afterRepaint=" + FlashBlankCount();
        }
        private string FlashBlankCount()
        {
            var controls = downloadsView.ChipsForQa.Where(c => c.Visible).Cast<Control>().Concat(new Control[] { downloadsView.SearchForQa.Parent }).ToList();
            var blank = 0;
            foreach (var control in controls)
            {
                var box = control.RectangleToScreen(control.ClientRectangle);
                if (!OwnsPoint(Handle, new Point(box.Left + box.Width / 2, box.Top + box.Height / 2))) return "covered";
                using (var shot = new Bitmap(box.Width, box.Height))
                {
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(box.Location, Point.Empty, box.Size);
                    var colours = new HashSet<int>();
                    for (var y = 0; y < box.Height; y += 2) for (var x = 0; x < box.Width; x += 2) colours.Add(shot.GetPixel(x, y).ToArgb());
                    if (colours.Count <= 2) blank++;
                }
            }
            return blank + "/" + controls.Count;
        }
        private async Task<string> FlashRunAsync(string name, int trials, Func<Task> once)
        {
            var form = Handle; var origin = workspace.PointToScreen(Point.Empty); var size = workspace.ClientSize; var running = true; var skipped = 0;
            var marks = new List<double>();
            var sampling = Task.Run(async () =>
            {
                var pixels = new int[size.Width * size.Height];
                using (var shot = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb))
                using (var g = Graphics.FromImage(shot))
                    while (running)
                    {
                        var ours = new[] { origin, new Point(origin.X + size.Width - 2, origin.Y + 2), new Point(origin.X + size.Width / 2, origin.Y + size.Height / 2), new Point(origin.X + 2, origin.Y + size.Height - 2) }.All(p => OwnsPoint(form, p));
                        if (!ours) { skipped++; await Task.Delay(30); continue; }
                        g.CopyFromScreen(origin, Point.Empty, size);
                        var data = shot.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); shot.UnlockBits(data);
                        var dark = 0; var seen = 0;
                        for (var y = 4; y < size.Height; y += 6) for (var x = 4; x < size.Width; x += 6)
                        { var c = pixels[y * size.Width + x]; seen++; if (((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255) < 110) dark++; }
                        lock (marks) marks.Add(dark / (double)seen);
                        await Task.Delay(1);
                    }
            });
            await Task.Delay(200);
            for (var trial = 0; trial < trials; trial++) await once();
            await Task.Delay(200); running = false; await sampling;
            if (marks.Count == 0) return name + ": covered";
            var sorted = marks.OrderBy(v => v).ToList(); var usual = sorted[sorted.Count / 2];
            return name + ": frames=" + marks.Count + " skipped=" + skipped + " usualDark=" + usual.ToString("P2") + " worstDark=" + sorted.Last().ToString("P2") + " blackFrames=" + marks.Count(v => v > usual + .006);
        }
    }
}
