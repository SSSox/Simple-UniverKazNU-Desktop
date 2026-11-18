using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The owner's second review of the copy, on synthetic pages: arranging the home screen with the real mouse, the sidebar,
    // the page title, Downloads, the skeleton's logo, and how a zoom step looks (--qa-zoom, on a visible window).
    internal sealed partial class MainForm
    {
        private const string TileBox = "(function(s){var r=document.querySelector(s).getBoundingClientRect();return Math.round(r.left+r.width/2)+'|'+Math.round(r.top+r.height/2);})";
        private async Task<Point> PointOfAsync(string selector)
        {
            var at = (await AutomationController.EvaluateAsync(main, TileBox + "('" + selector + "')") ?? "0|0").Split('|');
            return new Point(int.Parse(at[0]), int.Parse(at[1]));
        }
        private void MouseAt(Point at, CefEventFlags flags = CefEventFlags.None) { main.GetBrowser().GetHost().SendMouseMoveEvent(new MouseEvent(at.X, at.Y, flags), false); }
        private void Press(Point at, bool up) { main.GetBrowser().GetHost().SendMouseClickEvent(new MouseEvent(at.X, at.Y, up ? CefEventFlags.None : CefEventFlags.LeftMouseButton), MouseButtonType.Left, up, 1); }
        // A long press lifts a tile: the pointer goes down, stays, and the tile answers.
        private async Task LiftAsync(string selector)
        {
            var at = await PointOfAsync(selector); MouseAt(at); await Task.Delay(60);
            Press(at, false);
            // A hidden QA window gets throttled timers, so the press lasts until the page has answered.
            for (var wait = 0; wait < 40; wait++) { await Task.Delay(100); if (await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-lifted')?'up':''") == "up") break; }
        }
        private async Task DragToAsync(Point target)
        {
            MouseAt(target, CefEventFlags.LeftMouseButton); await Task.Delay(120);
            MouseAt(new Point(target.X + 1, target.Y), CefEventFlags.LeftMouseButton); await Task.Delay(220);
        }
        // The hidden QA window delivers moves when it gets to them, so the tile keeps moving until the bin has answered.
        private async Task DragToBinAsync()
        {
            var bin = await PointOfAsync(".univer-app-trash");
            for (var wait = 0; wait < 30; wait++)
            {
                MouseAt(new Point(bin.X + wait % 3, bin.Y), CefEventFlags.LeftMouseButton); await Task.Delay(100);
                if (await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-trash-hot')?'hot':''") == "hot") break;
            }
        }
        private async Task ProductQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            prefs.Language = "ru"; prefs.Readable = true; prefs.Animations = false; prefs.AutoLoginPaused = false; prefs.TileOrder = new List<string>(); prefs.HiddenTiles = new List<string>();
            prefs.Pins = new List<PinnedLink> { new PinnedLink { Title = "Дистанционные курсы", Url = SitePolicy.Origin + "/student/discussion/index/" }, new PinnedLink { Title = "Текущая аттестация", Url = SitePolicy.Origin + "/student/attestation/" } };
            await RefreshDocumentScriptsAsync();
            var started = zoomPercent; SetZoom(100); Size = new Size(1360, 860); await Task.Delay(200);
            var bachelor = PolishShell("<table class='mt' style='height:70px'><tr class='top'><td class='ct message'><ul><li>Переводной GPA = 2.85</li><li>Общий GPA = 3.12</li></ul></td></tr></table>", true);
            SetPresentation(main, true); main.LoadHtml(bachelor, SitePolicy.Origin + "/student/bachelor/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
            await AutomationController.EvaluateAsync(main, "window.__univerMotion=false;window.scrollTo(0,0);'top'");

            // The page's name in the toolbar is the page's, not the first panel's.
            await UpdateSiteTitleAsync();
            Check(pageTitle.Text == Strings.Get("cabinet", "ru"), "Бакалавр is named as a page, not after its Today panel", results);

            // The header: two catalogues in one tray, less round and larger than the controls beside them.
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('.univer-app-tabs'),a=Array.from(t.querySelectorAll('a')).filter(function(n){return n.offsetWidth>0;});return a.length===2&&t.offsetHeight===44&&a.every(function(n){var s=getComputedStyle(n);return n.offsetHeight===36&&s.borderTopLeftRadius==='10px'&&s.fontSize==='15px'&&+s.fontWeight>=600;})?'tray':t.offsetHeight+'/'+a.map(function(n){return n.offsetHeight+getComputedStyle(n).borderTopLeftRadius;}).join()})()") == "tray",
                "the two catalogues sit in one tray with square-ish, heavier tabs", results);

            // A tile is only a link now: no handle, no cross, and New section closes the grid.
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-quick .univer-app-reorder,.univer-app-quick .univer-app-unpin').length===0&&document.querySelector('.univer-app-quick').lastElementChild.matches('.univer-app-add-tile')?'plain':''") == "plain",
                "tiles carry no handle or cross and New section closes the grid", results);

            // Long press and drag with the real mouse: the tile lifts, the bin appears, the others make room, the order is stored.
            var first = await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-quick .univer-app-tile>a').pathname");
            await LiftAsync(".univer-app-quick .univer-app-tile:nth-child(1)>a");
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-tile.univer-app-lifted')&&document.querySelector('.univer-app-trash.univer-app-trash-on')?'lifted':''") == "lifted", "a long press lifts the tile and shows the bin", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "product-lifted.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await DragToAsync(await PointOfAsync(".univer-app-quick .univer-app-tile:nth-child(3)>a"));
            var drop = await PointOfAsync(".univer-app-tile.univer-app-lifted>a"); Press(drop, true); await Task.Delay(500);
            Check(prefs.TileOrder.Count >= 6 && new Uri(prefs.TileOrder[0]).AbsolutePath != first && new Uri(prefs.TileOrder[2]).AbsolutePath == first &&
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-lifted,.univer-app-trash-on')?'left':'clear'") == "clear", "dragging a lifted tile reorders the grid and stores the order", results);
            var order = prefs.TileOrder.ToList();

            // A quick click still follows the link, and a click that ended a long press does not.
            await LiftAsync(".univer-app-quick .univer-app-tile:nth-child(2)>a");
            Press(await PointOfAsync(".univer-app-tile.univer-app-lifted>a"), true); await Task.Delay(300);
            Check(main.Address.EndsWith("/student/bachelor/", StringComparison.Ordinal) && prefs.TileOrder.SequenceEqual(order), "letting go in place neither opens the tile nor changes the order", results);

            // Into the bin: a pinned tile is unpinned, a fixed tile leaves the screen.
            await LiftAsync(".univer-app-quick .univer-app-tile.univer-app-pinned>a");
            await DragToBinAsync();
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-trash.univer-app-trash-hot')?'hot':''") == "hot", "the bin lights up under a tile", results);
            await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var a=document.querySelector('.univer-app-lifted>a').getBoundingClientRect(),b=document.querySelector('.univer-app-trash').getBoundingClientRect();return a.height<b.height?'in':a.height+'/'+b.height;})()") == "in", "a tile over the bin shrinks to fit behind it", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "product-bin.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Press(await PointOfAsync(".univer-app-trash"), true); await Task.Delay(500);
            Check(prefs.Pins.Count == 1, "a pinned tile dropped into the bin is unpinned", results);
            await LiftAsync(".univer-app-quick .univer-app-tile:not(.univer-app-pinned)>a");
            await DragToBinAsync(); Press(await PointOfAsync(".univer-app-trash"), true); await Task.Delay(500);
            Check(prefs.HiddenTiles.Count == 1 && Shortcuts.FixedHomeLink(prefs.HiddenTiles[0]), "a fixed tile dropped into the bin leaves the home screen", results);

            // New section offers what is not on the screen, and choosing brings it back.
            var add = await PointOfAsync(".univer-app-add-tile"); Press(add, false); await Task.Delay(40); Press(add, true); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "document.activeElement===document.querySelector('.univer-app-add-search')&&document.querySelector('.univer-app-add-panel').getAttribute('role')==='dialog'&&document.querySelectorAll('.univer-app-add-row').length>=1?'open':''") == "open", "New section opens as a dialog with its search in focus", results);
            // The list of sections is the panel's last part: no hint about tiles under it, and it runs to the panel's own padding.
            Check(await AutomationController.EvaluateAsync(main, "(function(){var panel=document.querySelector('.univer-app-add-panel'),list=panel.lastElementChild,gap=panel.getBoundingClientRect().bottom-list.getBoundingClientRect().bottom;" +
                "return !panel.querySelector('.univer-app-add-hint')&&list.classList.contains('univer-app-add-list')&&Math.abs(gap-parseFloat(getComputedStyle(panel).paddingBottom)-parseFloat(getComputedStyle(panel).borderBottomWidth))<1.5?'list':gap})()") == "list",
                "New section ends with its list of sections, without the tile hint", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "product-new-section.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Press(await PointOfAsync(".univer-app-add-row"), false); await Task.Delay(40); Press(await PointOfAsync(".univer-app-add-row"), true); await Task.Delay(400);
            Check(prefs.HiddenTiles.Count == 0, "choosing a removed tile in New section brings it back", results);
            var host = main.GetBrowser().GetHost();
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = 27 }); host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = 27 }); await Task.Delay(200);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-add-layer')?'open':'closed'") == "closed", "Escape closes New section", results);

            // From the keyboard: Delete removes the focused tile.
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-quick .univer-app-tile.univer-app-pinned>a').focus();'focus'");
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = 46 }); host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = 46 }); await Task.Delay(300);
            Check(prefs.Pins.Count == 0, "Delete removes a tile from the keyboard", results);

            // The skeleton keeps the logo's place: the tabs start where the page will put them.
            var tray = await AutomationController.EvaluateAsync(main, "Math.round(document.querySelector('.univer-app-tabs').getBoundingClientRect().left)+'|'+Math.round(document.querySelector('.univer-app-tabs').offsetWidth)");
            QaTrace("qa-product-tray " + tray);
            loading.Zoom = 1; loading.SetRoute(SitePolicy.Origin + "/student/bachelor/"); loading.Visible = true; loading.BringToFront(); loading.Refresh();
            SaveBitmap(loading, Path.Combine(qa, "product-skeleton.png"));
            using (var shot = new Bitmap(loading.Width, loading.Height))
            {
                loading.DrawToBitmap(shot, new Rectangle(Point.Empty, loading.Size));
                var left = (int)Math.Round(24 * layoutScale);
                Check(shot.Width >= D(1040) && shot.GetPixel(left + D(12), D(28)).ToArgb() != Theme.Current.Surface.ToArgb(), "the skeleton draws the logo's place", results);
            }
            loading.Visible = false;

            // The sidebar: every study section, the app's three together, none stranded at the bottom.
            Check(attestationButton.Top > journalButton.Top && distanceButton.Top > materialsButton.Top && sideSettings.Top - downloadsButton.Bottom <= SZ(8) && website.Top - sideSettings.Bottom <= SZ(8) && website.Bottom <= localAccount.Top,
                "Settings and Open in browser follow Downloads, and the two new sections are in the sidebar", results);
            SaveBitmap(sidebar, Path.Combine(qa, "product-sidebar.png"));

            await OwnerFixesQaAsync(qa, results);

            // Downloads: a row is one painted control, and a zoom step keeps it.
            await DownloadsProductQaAsync(qa, results);
            prefs.Pins = new List<PinnedLink>(); prefs.TileOrder = new List<string>(); prefs.HiddenTiles = new List<string>(); SetZoom(started); await Task.Delay(150);
        }
        private async Task DownloadsProductQaAsync(string qa, List<string> results)
        {
            var folder = Path.Combine(storage.Root, "qa-files"); Directory.CreateDirectory(folder);
            var records = new List<DownloadRecord>();
            var names = new[] { "Лекция 1.pdf", "Семинар 2.docx", "Таблица 3.xlsx", "Слайды 4.pptx", "Рисунок 5.png", "Заметка 6.txt" };
            for (var i = 0; i < 24; i++)
            {
                var path = Path.Combine(folder, i + " " + names[i % names.Length]); File.WriteAllText(path, "synthetic");
                records.Add(new DownloadRecord { Id = Guid.NewGuid().ToString("N"), FileName = Path.GetFileName(path), Path = path, Source = i % 5 == 4 ? "Новости" : "Предмет QA " + i % 3, State = DownloadState.Completed, Total = 2048 * (i + 1), Received = 2048 * (i + 1), Started = DateTime.Now.AddDays(-i * 2), Finished = DateTime.Now.AddDays(-i * 2) });
            }
            downloadsView.SetItems(records); ShowDownloads(); await Task.Delay(300);
            var rows = Descendants(downloadsView).OfType<DownloadRow>().Where(x => x.Width > 0).ToList();
            Check(rows.Count == 24 && rows.All(x => x.Controls.Count == 0), "a Downloads row is one painted control", results);
            Check(rows.Count(x => x.Heading != null) >= 3, "the newest-first list is grouped by when files arrived", results);
            Check(downloadsView.ChipsForQa.Where(x => x.Visible).Select(x => x.Note).First() == "24" && downloadsView.ChipsForQa.All(x => x.Height >= D(34)), "file type filters are large chips with their counts", results);
            SaveBitmap(downloadsView, Path.Combine(qa, "product-downloads.png"));
            var before = rows[0]; var clock = System.Diagnostics.Stopwatch.StartNew();
            SetZoom(130); clock.Stop(); await Task.Delay(120);
            Check(Descendants(downloadsView).OfType<DownloadRow>().First(x => x.Width > 0) == before, "a zoom step keeps the rows instead of building them again", results);
            QaTrace("qa-product-zoom-ms " + clock.ElapsedMilliseconds);
            // Behind the curtain this cost is not seen; the bound only catches rows being built again (it was seconds with 60 files).
            Check(clock.ElapsedMilliseconds < 1000, "a zoom step on Downloads stays well under a second", results);
            SaveBitmap(downloadsView, Path.Combine(qa, "product-downloads-130.png"));
            SetZoom(100); await Task.Delay(120);
            var list = downloadsView.OpenSubjectsForQa(); await Task.Delay(300);
            using (var shot = new Bitmap(list.Width, list.Height)) { list.DrawToBitmap(shot, new Rectangle(Point.Empty, list.Size)); shot.Save(Path.Combine(qa, "product-subject-list.png"), ImageFormat.Png); }
            Check(list.Width >= D(320) && list.Height >= D(120), "the course filter opens a large list", results);
            downloadsView.CloseMenusForQa(); await Task.Delay(120);
            Check(string.Join("|", downloadsView.SubjectGroupsForQa) == Strings.Get("subjectCourses", "ru") + "|" + Strings.Get("subjectPages", "ru"), "site pages are listed apart from courses", results);
            // The same screens in the dark theme.
            var themeBefore = prefs.Theme; prefs.Theme = ThemeChoice.Dark; ApplyTheme();
            try
            {
                await Task.Delay(200);
                SaveBitmap(downloadsView, Path.Combine(qa, "product-dark-downloads.png")); SaveBitmap(sidebar, Path.Combine(qa, "product-dark-sidebar.png"));
                var dark = Descendants(downloadsView).OfType<DownloadRow>().First(x => x.Width > 0);
                using (var shot = new Bitmap(dark.Width, dark.Height)) { dark.DrawToBitmap(shot, new Rectangle(Point.Empty, dark.Size)); Check(shot.GetPixel(dark.Width / 2, dark.Height - D(20)).GetBrightness() < .3f, "a Downloads row follows the dark theme", results); }
                var list2 = downloadsView.OpenSubjectsForQa(); await Task.Delay(200);
                using (var shot = new Bitmap(list2.Width, list2.Height)) { list2.DrawToBitmap(shot, new Rectangle(Point.Empty, list2.Size)); shot.Save(Path.Combine(qa, "product-dark-subject-list.png"), ImageFormat.Png); Check(shot.GetPixel(list2.Width / 2, list2.Height - D(10)).GetBrightness() < .3f, "the course list follows the dark theme", results); }
                downloadsView.CloseMenusForQa(); await Task.Delay(100);
                HideDownloads();
                loading.Zoom = 1; loading.SetRoute(SitePolicy.Origin + "/student/umkd/"); loading.Visible = true; loading.BringToFront(); loading.Refresh();
                SaveBitmap(loading, Path.Combine(qa, "product-dark-skeleton-umkd.png")); loading.Visible = false;
            }
            finally { prefs.Theme = themeBefore; ApplyTheme(false); }
            loading.Zoom = 1; loading.SetRoute(SitePolicy.Origin + "/student/umkd/123/2026/1"); loading.Visible = true; loading.BringToFront(); loading.Refresh();
            SaveBitmap(loading, Path.Combine(qa, "product-skeleton-files.png")); loading.Visible = false;
            downloadsView.SetItems(new List<DownloadRecord>());
        }
        // --qa-zoom: a real, visible window in an isolated profile. Zoom steps run the way Ctrl+wheel sends them while frames are
        // copied from the screen; the report says how long the window showed black and how far frames lay apart.
        private async Task ZoomFeelQaAsync()
        {
            var report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-zoom.txt"); var lines = new List<string>();
            var folder = Path.Combine(storage.Root, "qa-files"); Directory.CreateDirectory(folder); var records = new List<DownloadRecord>();
            for (var i = 0; i < 60; i++)
            {
                var path = Path.Combine(folder, "Файл " + i + ".pdf"); File.WriteAllText(path, "synthetic");
                records.Add(new DownloadRecord { Id = Guid.NewGuid().ToString("N"), FileName = Path.GetFileName(path), Path = path, Source = "Предмет QA", State = DownloadState.Completed, Total = 4096, Received = 4096, Started = DateTime.Now.AddHours(-i * 7), Finished = DateTime.Now.AddHours(-i * 7) });
            }
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            await Task.Delay(800);
            main.LoadHtml(PolishShell("<div style='height:1400px'><table class='mt'><tr class='top'><td class='ct'>Страница QA</td></tr></table></div>", false), SitePolicy.Origin + "/news/student"); await Task.Delay(1500);
            lines.Add(await ZoomRunAsync("site", new[] { 110, 120, 130, 120, 110, 100 }));
            downloadsView.SetItems(records); ShowDownloads(); await Task.Delay(700);
            lines.Add(await ZoomRunAsync("downloads", new[] { 110, 120, 130, 120, 110, 100 }));
            File.WriteAllLines(report, lines); Close();
        }
        private async Task<string> ZoomRunAsync(string name, int[] steps)
        {
            var origin = PointToScreen(Point.Empty); var size = ClientSize; var clock = System.Diagnostics.Stopwatch.StartNew();
            var frames = new List<KeyValuePair<long, double>>(); var running = true; var sheet = new List<Bitmap>();
            var sampling = Task.Run(async () =>
            {
                using (var shot = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb))
                using (var g = Graphics.FromImage(shot))
                    while (running)
                    {
                        g.CopyFromScreen(origin, Point.Empty, size);
                        var dark = 0; var seen = 0;
                        for (var y = 8; y < size.Height; y += 24) for (var x = 8; x < size.Width; x += 24) { var c = shot.GetPixel(x, y); seen++; if (c.R + c.G + c.B < 45) dark++; }
                        lock (frames) { frames.Add(new KeyValuePair<long, double>(clock.ElapsedMilliseconds, dark / (double)seen)); if (frames.Count % 6 == 1 && sheet.Count < 12) sheet.Add(new Bitmap(shot, size.Width / 3, size.Height / 3)); }
                        await Task.Delay(8);
                    }
            });
            foreach (var step in steps) { SetZoom(step); await Task.Delay(320); }
            await Task.Delay(900); running = false; await sampling;
            using (var contact = new Bitmap(size.Width / 3 * 4, size.Height / 3 * 3))
            using (var g = Graphics.FromImage(contact))
            {
                for (var i = 0; i < sheet.Count; i++) g.DrawImage(sheet[i], i % 4 * (size.Width / 3), i / 4 * (size.Height / 3));
                contact.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-zoom-" + name + ".png"), ImageFormat.Png);
            }
            foreach (var picture in sheet) picture.Dispose();
            var worst = frames.Max(f => f.Value); var dropped = frames.Count(f => f.Value > .05);
            var gaps = frames.Zip(frames.Skip(1), (a, b) => b.Key - a.Key).OrderBy(x => x).ToList();
            return name + ": frames=" + frames.Count + " worstDark=" + worst.ToString("P1") + " darkFrames=" + dropped + " medianGapMs=" + gaps[gaps.Count / 2] + " maxGapMs=" + gaps.Last();
        }
    }
}
