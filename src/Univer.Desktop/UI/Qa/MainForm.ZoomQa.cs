using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Zoom in every file the viewer shows itself (PDF, Office, HTML, text, images) with the toolbar, app shortcuts and keys
        // inside the file, down to 50% and back with Ctrl+0; content must scale by the same factor. Synthetic files only.
        private async Task ZoomQaAsync(string qa, List<string> results)
        {
            var folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", "zoom-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var started = zoomPercent;
            try
            {
                var files = new List<string>();
                Func<string, string> add = name => { var path = Path.Combine(folder, name); files.Add(path); return path; };
                File.WriteAllBytes(add("Масштаб QA.pdf"), SyntheticPdf("Univer QA PDF"));
                SyntheticDocx(add("Масштаб QA.docx"));
                File.WriteAllText(add("Масштаб QA.txt"), "Univer QA масштаб\nвторая строка", new UTF8Encoding(false));
                File.WriteAllText(add("Масштаб QA.csv"), "Дисциплина;Балл\nМатематика;95", new UTF8Encoding(false));
                File.WriteAllText(add("Масштаб QA.html"), "<h1>Страница QA</h1><p>Univer QA масштаб</p>", new UTF8Encoding(false));
                foreach (var format in new[] { ImageFormat.Png, ImageFormat.Jpeg })
                    using (var bitmap = new Bitmap(160, 80))
                    {
                        using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.SteelBlue); g.FillRectangle(Brushes.Orange, 90, 20, 60, 40); }
                        bitmap.Save(add("Масштаб QA." + (format == ImageFormat.Png ? "png" : "jpg")), format);
                    }
                var fixtures = Environment.GetEnvironmentVariable("UNIVER_QA_FIXTURES");
                if (!string.IsNullOrEmpty(fixtures))
                    foreach (var name in new[] { "synthetic.doc", "synthetic.xlsx", "synthetic.xls", "synthetic.pptx", "synthetic.ppt" })
                    {
                        var source = Path.Combine(fixtures, name);
                        if (!File.Exists(source)) throw new FileNotFoundException("QA fixture missing", source);
                        File.Copy(source, add("Масштаб " + name));
                    }
                else QaTrace("qa-zoom-fixtures-unavailable");
                var records = files.ConvertAll(Record);
                downloadsView.SetItems(records); ShowDownloads(); SetZoom(100); await Task.Delay(150);
                foreach (var record in records)
                {
                    var extension = Path.GetExtension(record.Path).ToLowerInvariant();
                    var browser = await OpenZoomQaAsync(record, extension);
                    Check(browser != null && !downloadsView.OfficeForQa && zoomIn.Visible && zoomIn.Enabled && zoomOut.Visible && zoomOut.Enabled,
                        extension + " opens in the viewer with the zoom buttons available", results);
                    var size = new List<double> { await SettledMetricQaAsync(browser, extension, 100) }; var percents = new List<int> { zoomPercent };
                    for (var i = 0; i < 3; i++) zoomIn.PerformClick();
                    size.Add(await SettledMetricQaAsync(browser, extension, 130)); percents.Add(zoomPercent);
                    var key = new Message();
                    for (var i = 0; i < 6; i++) ProcessCmdKey(ref key, Keys.Control | Keys.OemMinus);
                    size.Add(await SettledMetricQaAsync(browser, extension, 70)); percents.Add(zoomPercent);
                    // A key pressed inside the focused file reaches the app (ShortcutHandler); Chromium never delivers the release of a
                    // press the app handled, so only the press is sent, then an ordinary key as the next real key-down would be.
                    var host = browser.GetBrowser().GetHost(); host.SetFocus(true); await Task.Delay(100);
                    host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = (int)Keys.Oemplus, Modifiers = CefEventFlags.ControlDown });
                    host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = (int)Keys.ShiftKey, Modifiers = CefEventFlags.ShiftDown });
                    host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = (int)Keys.ShiftKey });
                    for (var until = DateTime.UtcNow.AddSeconds(3); zoomPercent != 80 && DateTime.UtcNow < until; ) await Task.Delay(50);
                    size.Add(await SettledMetricQaAsync(browser, extension, 80)); percents.Add(zoomPercent);
                    for (var i = 0; i < 5; i++) zoomOut.PerformClick();
                    size.Add(await SettledMetricQaAsync(browser, extension, 50)); percents.Add(zoomPercent);
                    ProcessCmdKey(ref key, Keys.Control | Keys.D0);
                    size.Add(await SettledMetricQaAsync(browser, extension, 100)); percents.Add(zoomPercent);
                    QaTrace("qa-zoom " + extension + " " + string.Join(" ", size.ConvertAll(v => v.ToString("0.#", CultureInfo.InvariantCulture))) + " at " + string.Join("/", percents));
                    var steps = new[] { 100, 130, 70, 80, 50, 100 };
                    Func<int, double, bool> near = (index, factor) => size[0] > 0 && percents[index] == steps[index] && Math.Abs(size[index] / size[0] - factor) <= factor * 0.12;
                    Check(near(1, 1.3), extension + ": the + button enlarges the file (100% → 130%)", results);
                    Check(near(2, 0.7), extension + ": Ctrl+minus makes the file smaller (130% → 70%)", results);
                    Check(near(3, 0.8), extension + ": Ctrl+plus pressed inside the file zooms it (70% → 80%)", results);
                    Check(near(4, 0.5), extension + ": the − button zooms out to the 50% floor and stops there", results);
                    Check(near(5, 1), extension + ": Ctrl+0 returns the file to 100%", results);
                    // Ctrl+wheel over the file is the app zoom on both engines: one notch +10%, smooth parts add up, back down.
                    var wheel = new List<double> { size[5] };
                    CtrlWheelQa(browser, 120);
                    wheel.Add(await SettledMetricQaAsync(browser, extension, 110));
                    for (var i = 0; i < 3; i++) CtrlWheelQa(browser, 40);
                    wheel.Add(await SettledMetricQaAsync(browser, extension, 120));
                    CtrlWheelQa(browser, -120); CtrlWheelQa(browser, -120);
                    wheel.Add(await SettledMetricQaAsync(browser, extension, 100));
                    QaTrace("qa-zoom-wheel " + extension + " " + string.Join(" ", wheel.ConvertAll(v => v.ToString("0.#", CultureInfo.InvariantCulture))) + " at " + zoomPercent);
                    Check(wheel[0] > 0 && Math.Abs(wheel[1] / wheel[0] - 1.1) <= 0.11 && Math.Abs(wheel[2] / wheel[0] - 1.2) <= 0.12 && Math.Abs(wheel[3] / wheel[0] - 1) <= 0.1 && zoomPercent == 100 &&
                        Math.Abs(await browser.GetZoomLevelAsync()) < 0.01, extension + ": Ctrl+wheel zooms the file in steps of the app zoom and back", results);
                    using (var client = browser.GetDevToolsClient())
                        File.WriteAllBytes(Path.Combine(qa, "zoom" + extension.Replace('.', '-') + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                }
                // Ctrl+wheel over the website zooms it like the files.
                downloadsView.ClosePreview(); HideDownloads(); await Task.Delay(200);
                CtrlWheelQa(main, 120); CtrlWheelQa(main, 120);
                for (var until = DateTime.UtcNow.AddSeconds(3); DateTime.UtcNow < until && zoomPercent != 120; ) await Task.Delay(50);
                await Task.Delay(200);
                Check(zoomPercent == 120 && Math.Abs(await main.GetZoomLevelAsync() - Math.Log(1.2, 1.2)) < 0.01, "Ctrl+wheel over the website zooms it by the app's steps", results);
                SetZoom(100); ShowDownloads(); await Task.Delay(150);
                // The ceiling: 200%, and the next step stays there.
                for (var i = 0; i < 12; i++) zoomIn.PerformClick();
                Check(zoomPercent == 200 && zoomLabel.Text == "200%", "the + button stops at the 200% ceiling", results);
                SetZoom(100);
            }
            finally
            {
                SetZoom(started);
                downloadsView.ClosePreview(); HideDownloads(); downloadsView.SetItems(history.Items);
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        // A Ctrl+wheel message as Windows posts it to the browser's window (on CEF's UI thread), with the pointer over it.
        private static void CtrlWheelQa(ChromiumWebBrowser browser, int delta)
        {
            var center = browser.PointToScreen(new Point(browser.Width / 2, browser.Height / 2));
            var wParam = (IntPtr)(((delta & 0xFFFF) << 16) | 0x0008);
            var lParam = (IntPtr)((center.Y & 0xFFFF) << 16 | (center.X & 0xFFFF));
            PostMessage(browser.GetBrowser().GetHost().GetWindowHandle(), 0x020A, wParam, lParam);
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        private async Task<ChromiumWebBrowser> OpenZoomQaAsync(DownloadRecord record, string extension)
        {
            if (extension == ".pdf") { await OpenReaderQaAsync(record.Path, 1); return downloadsView.ViewerForQa; }
            if (extension != ".png" && extension != ".jpg") return await OpenPreviewQaAsync(record);
            downloadsView.Preview(record);
            for (var until = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < until; await Task.Delay(100))
                if (await EvaluateLocalPreviewAsync(downloadsView.ViewerForQa, "(()=>{const i=document.querySelector('img');return i&&i.complete&&i.naturalWidth>0&&i.getBoundingClientRect().width>0?'ready':''})()") == "ready")
                    return downloadsView.ViewerForQa;
            return null;
        }
        // The size of the file's own content on screen, in device pixels: a PDF page, a picture, or a line of text.
        private static async Task<double> ZoomMetricQaAsync(ChromiumWebBrowser browser, string extension)
        {
            var probe = extension == ".pdf" ? "document.querySelector('.page').getBoundingClientRect().width"
                : extension == ".png" || extension == ".jpg" ? "document.querySelector('img').getBoundingClientRect().width"
                // The first character's line box: independent of how a long line wraps in the current width.
                : "(()=>{const e=document.querySelector('h1,h2,.grid td:not(:empty),pre,p:not(.note):not(.slide-no):not(.table-help)');" +
                  "const t=document.createTreeWalker(e,NodeFilter.SHOW_TEXT,{acceptNode:n=>n.data.trim()?1:3}).nextNode();const r=document.createRange();" +
                  "const i=t.data.search(/\\S/);r.setStart(t,i);r.setEnd(t,i+1);return r.getBoundingClientRect().height})()";
            double value;
            var raw = await EvaluateLocalPreviewAsync(browser, "String(" + probe + "*devicePixelRatio)");
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
        // Waits until the viewer has the expected zoom and its content has been laid out (and, for PDF, drawn) again.
        private async Task<double> SettledMetricQaAsync(ChromiumWebBrowser browser, string extension, int percent)
        {
            var expected = Math.Log(percent / 100.0, 1.2);
            // Focusing a browser of the hidden QA window can make the desktop minimize it, which leaves the file 0 px wide.
            if (WindowState == FormWindowState.Minimized) { QaTrace("qa-zoom-restore " + extension + " " + percent); await RestoreQaWindowAsync(); }
            for (var until = DateTime.UtcNow.AddSeconds(4); DateTime.UtcNow < until; await Task.Delay(100))
                if (zoomPercent == percent && Math.Abs(await browser.GetZoomLevelAsync() - expected) < 0.01) break;
            await Task.Delay(extension == ".pdf" ? 700 : 250);
            // Two equal readings in a row: the downloads page re-lays out after a zoom change as well.
            var value = await ZoomMetricQaAsync(browser, extension);
            for (var until = DateTime.UtcNow.AddSeconds(3); DateTime.UtcNow < until; )
            {
                await Task.Delay(150);
                var again = await ZoomMetricQaAsync(browser, extension);
                if (Math.Abs(again - value) < 0.5) break;
                value = again;
            }
            return value;
        }
    }
}
