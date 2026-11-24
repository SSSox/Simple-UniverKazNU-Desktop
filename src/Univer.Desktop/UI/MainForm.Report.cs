using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CefSharp;
using CefSharp.DevTools.Page;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // «Сводка успеваемости» (grade-report.js): the page says which button the student pressed, the app does the rest. PDF
    // is Chromium's print of the sheet (its print rules leave only the sheet), PNG a DevTools picture of the sheet alone,
    // Copy the same picture on the clipboard for a message, Print the usual print dialog. Files go to Downloads beside the
    // site's downloads, never over an existing file, and into the downloads list; nothing opens by itself.
    internal sealed partial class MainForm
    {
        private bool reportBusy; private string reportPath; private List<string> reportPaths = new List<string>();
        // QA: the folder files go to, the clipboard and the print dialog, in place of the user's own.
        private string reportFolder; private Action<byte[]> reportClipboard; private Action reportPrint; private Action<int> reportCaptured;
        private static readonly TimeSpan ReportDeadline = TimeSpan.FromSeconds(30), DevToolsDeadline = TimeSpan.FromSeconds(5);

        // page: the one page «Копировать» beside it asks for (GradeReport.Page), 0 for the whole summary.
        private async Task ReportAsync(ChromiumWebBrowser browser, string action, int page = 0)
        {
            if (action == null || closing || browser == null || browser.IsDisposed || browser != Active || !SitePolicy.Grades(browser.Address)) return;
            if (action == GradeReport.Reveal) { RevealReport(); return; }
            // A press while an earlier one is still being made (a sheet closed and opened again meanwhile) only frees the
            // sheet's buttons: nothing is made and nothing is told, the student presses again. A press the app cannot take now
            // (the window is busy or locked) is answered as failed.
            if (reportBusy) { AnswerReport(browser, action, false, null, null, true); return; }
            if (rotating || commandBusy || LockShown) { AnswerReport(browser, action, false, null, null); return; }
            reportBusy = true; string result = null; GradeReport.Source source = null; var cancelled = false; var address = browser.Address; var epoch = sessionEpoch;
            try
            {
                // A new press starts afresh: an earlier failure of the summary does not stay over this one's answer.
                if (messages.Withdraw("reportFailed") | messages.Withdraw("reportCopyFailed")) RenderFeedback();
                try
                {
                    // The opening of the sheet the press belongs to: closing it, or opening it again, drops the work.
                    source = ReadReportSource(await AutomationController.EvaluateAsync(browser, ReportOpened), epoch);
                    result = await MakeReportAsync(browser, action, page, address, source);
                }
                catch (Exception) { result = null; }
                // Closed or left while it was made: nothing is told, a failure included (the student has moved on).
                try { cancelled = result == ReportCancelled || (result == null && !closing && !await ReportStillAsync(browser, address, source)); }
                catch (Exception) { cancelled = true; }
            }
            finally { reportBusy = false; }
            if (!closing && !cancelled && epoch == sessionEpoch) FinishReport(browser, action, page, result != null, string.IsNullOrEmpty(result) ? null : result, source.Opening);
        }
        // The file's name (the first one when a long summary made several pictures), "" for a print or a copy, null for a
        // failure, ReportCancelled when the sheet closed meanwhile.
        private async Task<string> MakeReportAsync(ChromiumWebBrowser browser, string action, int page, string address, GradeReport.Source source)
        {
            // A click that arrives after the sheet closed, after Original or on another page makes nothing.
            if (!await ReportStillAsync(browser, address, source)) return ReportCancelled;
            if (action == GradeReport.Print) { if (reportPrint != null) reportPrint(); else browser.Print(); return ""; }
            if (action == GradeReport.Pdf) return await SaveReportPdfAsync(browser, address, source);
            // Copy takes one page (the first, or the one pressed beside); PNG every page, each its own file.
            var pictures = await CaptureReportAsync(browser, action == GradeReport.Copy ? Math.Max(1, page) : 0);
            if (pictures == null) return null;
            // Closed or left while the pictures were taken: what was drawn is not the summary, and nothing is written yet.
            if (!await ReportStillAsync(browser, address, source)) return ReportCancelled;
            if (action == GradeReport.Copy) return await CopyReportPictureAsync(browser, address, source, pictures[0]);
            return await SaveReportPicturesAsync(browser, address, source, pictures);
        }
        // Only a positive reply from the originating document and sheet opening lets the export continue.
        private async Task<bool> ReportStillAsync(ChromiumWebBrowser browser, string address, GradeReport.Source source)
        {
            if (source == null || !ReportCurrent(browser, address, source.Generation)) return false;
            try
            {
                var now = await AutomationController.EvaluateAsync(browser, ReportOpened);
                return ReportCurrent(browser, address, source.Generation) && source.StillOpen(ReadReportSource(now, sessionEpoch));
            }
            catch (Exception) { return false; }
        }
        private bool ReportCurrent(ChromiumWebBrowser browser, string address, int epoch)
        {
            return !closing && !rotating && !browser.IsDisposed && browser.Address == address && epoch == sessionEpoch &&
                !(awaitingCommit == browser && DateTime.UtcNow - awaitingSince < TimeSpan.FromSeconds(20));
        }
        private static GradeReport.Source ReadReportSource(string json, int epoch)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var parts = new JavaScriptSerializer().Deserialize<string[]>(json);
            return parts != null && parts.Length == 2 ? new GradeReport.Source(parts[0], parts[1], epoch) : null;
        }
        private const string ReportOpened = "window.__univerAutomationV1&&window.__univerReportV1?JSON.stringify([window.__univerAutomationV1.probe().document,String(window.__univerReportV1.opened())]):''";
        // What MakeReportAsync returns when the sheet closed meanwhile: no file, and no failure to report.
        private const string ReportCancelled = "|cancelled|";
        private void FinishReport(ChromiumWebBrowser browser, string action, int page, bool ok, string name, string opening)
        {
            var count = 0;
            if (action == GradeReport.Print) { if (!ok) SetStatus("printFailed"); }
            else if (!ok) SetStatus(action == GradeReport.Copy ? "reportCopyFailed" : "reportFailed");
            else if (action == GradeReport.Copy) SetStatus("reportCopied");
            else
            {
                // Each saved file is told like any finished download: its name, Open and the downloads list. The last told is the
                // newest: page 1, the one the sheet's «Показать в папке» selects, stands on top.
                for (var i = reportPaths.Count - 1; i >= 0; i--)
                {
                    var path = reportPaths[i]; var size = 0L; try { size = new FileInfo(path).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    OnDownloadChanged(new DownloadRecord { FileName = Path.GetFileName(path), Path = path, State = DownloadState.Completed, Received = size, Total = size, Finished = DateTime.Now, Source = Strings.Get("reportSource", prefs.Language) });
                }
                count = reportPaths.Count; SetStatus("downloadComplete");
            }
            // The sheet gets its answer even when another window is in front now.
            AnswerReport(browser, action, ok, name, opening, false, page, count);
        }
        private static void AnswerReport(ChromiumWebBrowser browser, string action, bool ok, string name, string opening, bool busy = false, int page = 0, int count = 0)
        {
            if (browser.IsDisposed) return;
            var answer = new JavaScriptSerializer().Serialize(new { action, ok, name = name ?? "", opening, busy, page, count });
            _ = AutomationController.EvaluateAsync(browser, "window.__univerReportV1&&window.__univerReportV1.done(" + answer + ");'ok'");
        }
        private string ReportFolder()
        {
            var folder = reportFolder ?? DownloadService.DownloadsFolder();
            Directory.CreateDirectory(folder);
            return folder;
        }
        private async Task<string> SaveReportPdfAsync(ChromiumWebBrowser browser, string address, GradeReport.Source source)
        {
            var temp = Path.Combine(ReportFolder(), PartialDownloads.NewName());
            Task<bool> print = null;
            try
            {
                var settings = new PdfPrintSettings
                {
                    PrintBackground = true, PreferCssPageSize = true, PaperWidth = GradeReport.PaperWidth, PaperHeight = GradeReport.PaperHeight,
                    MarginType = CefPdfPrintMarginType.Custom, MarginTop = GradeReport.Margin, MarginBottom = GradeReport.Margin, MarginLeft = GradeReport.Margin, MarginRight = GradeReport.Margin
                };
                // Chromium fills the page margins with the browser's own background, the theme colour; paper is white.
                using (var client = browser.GetDevToolsClient())
                {
                    try
                    {
                        // Inside the try: an override that answers past its deadline is still taken back below.
                        await TaskDeadline.AwaitAsync(client.Emulation.SetDefaultBackgroundColorOverrideAsync(new CefSharp.DevTools.DOM.RGBA { R = 255, G = 255, B = 255, A = 1 }), DevToolsDeadline, stopping);
                        print = browser.PrintToPdfAsync(temp, settings);
                        await TaskDeadline.AwaitAsync(print, ReportDeadline, stopping);
                        if (!await print || !File.Exists(temp) || new FileInfo(temp).Length < 64) return null;
                    }
                    finally
                    {
                        try { if (!browser.IsDisposed) await TaskDeadline.AwaitAsync(client.Emulation.SetDefaultBackgroundColorOverrideAsync(), DevToolsDeadline, stopping); }
                        catch (Exception) { }
                    }
                }
                // Closed or left while Chromium printed: the printout is not the summary.
                if (!await ReportStillAsync(browser, address, source)) return ReportCancelled;
                return SaveReport(temp, "pdf", null);
            }
            finally
            {
                // Past the deadline Chromium may still write the file: it is removed once it has (at once if it already has).
                if (print != null) _ = print.ContinueWith(done => GradeReport.DeletePart(temp), TaskScheduler.Default);
                GradeReport.DeletePart(temp);
            }
        }
        // The summary's pages alone (all of them, or only page `only`), each at twice its CSS size: the page lays them out flat
        // for the pictures and back afterwards. Null when any picture fails: a summary is saved whole or not at all.
        private async Task<List<byte[]>> CaptureReportAsync(ChromiumWebBrowser browser, int only)
        {
            // A zoom the student applies while the pages are measured or taken moves them under the clip: the picture would have
            // the right size and the wrong part of the page, so the set is dropped.
            var zoomAt = zoomChanges;
            var box = await AutomationController.EvaluateAsync(browser, "window.__univerReportV1?JSON.stringify(window.__univerReportV1.capture(true)):''");
            try
            {
                double ratio; var pages = ReportPages(box, out ratio);
                if (pages == null || only > pages.Count) return null;
                // The browser's own zoom, not the page's word: DevTools takes the clip in zoomed pixels.
                var zoom = appliedZoom / 100.0;
                var level = browser.GetZoomLevelAsync();
                try { await TaskDeadline.AwaitAsync(level, DevToolsDeadline, stopping); zoom = Math.Pow(1.2, await level); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { }
                var clipZoom = GradeReport.ClipZoom(zoom); var scale = GradeReport.CaptureScale(ratio, zoom);
                // The screen's real scale, not the page's word, bounds the bitmaps DevTools would draw.
                var drawn = GradeReport.DrawnScale(scale, Native.WindowScale(browser.Handle), zoom);
                // Only the pages taken: one oversized page elsewhere does not stop another page's Copy.
                for (var i = 0; i < pages.Count; i++) if ((only == 0 || i == only - 1) && !GradeReport.Fits(pages[i][2], pages[i][3], drawn)) return null;
                var pictures = new List<byte[]>();
                using (var client = browser.GetDevToolsClient())
                {
                    for (var i = 0; i < pages.Count; i++)
                    {
                        if (only > 0 && i != only - 1) continue;
                        var page = pages[i];
                        var clip = new Viewport { X = page[0] * clipZoom, Y = page[1] * clipZoom, Width = page[2] * clipZoom, Height = page[3] * clipZoom, Scale = scale };
                        var capture = client.Page.CaptureScreenshotAsync(CaptureScreenshotFormat.Png, null, clip, null, true, null);
                        await TaskDeadline.AwaitAsync(capture, ReportDeadline, stopping);
                        var data = (await capture).Data;
                        var picture = data != null && data.Length > 0 ? await Task.Run(() => FitPicture(data, page[2], page[3])) : null;
                        if (picture == null) return null;
                        pictures.Add(picture);
                        reportCaptured?.Invoke(i + 1);
                        if (zoomChanges != zoomAt) return null;
                    }
                }
                return pictures;
            }
            finally { if (!browser.IsDisposed) await AutomationController.EvaluateAsync(browser, "window.__univerReportV1&&window.__univerReportV1.capture(false);'ok'"); }
        }
        // A screen whose scale the page reported wrongly still gets a picture of the promised width; one that came back
        // shorter than the page (cut by the renderer) is no picture of it.
        private static byte[] FitPicture(byte[] png, double cssWidth, double cssHeight)
        {
            using (var stream = new MemoryStream(png))
            using (var image = Image.FromStream(stream))
            {
                var plan = GradeReport.PlanFit(image.Width, image.Height, cssWidth, cssHeight);
                if (plan.Kind == GradeReport.PictureFitKind.Reject) return null;
                if (plan.Kind == GradeReport.PictureFitKind.Keep) return png;
                using (var bitmap = new Bitmap(plan.Width, plan.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                using (var output = new MemoryStream())
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.DrawImage(image, 0, 0, plan.Width, plan.Height);
                    bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                    return output.ToArray();
                }
            }
        }
        // Each page's x, y, width and height in CSS pixels of the document, and the device pixel ratio; at most
        // GradeReport.MaxPages, each judged by GradeReport.Fits.
        private static List<double[]> ReportPages(string json, out double ratio)
        {
            ratio = 1;
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object list, value;
                if (!values.TryGetValue("pages", out list) || !(list is System.Collections.ArrayList) || !values.TryGetValue("ratio", out value) || value == null) return null;
                ratio = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                var items = (System.Collections.ArrayList)list;
                if (items.Count < 1 || items.Count > GradeReport.MaxPages || double.IsNaN(ratio) || double.IsInfinity(ratio)) return null;
                var pages = new List<double[]>();
                foreach (var item in items)
                {
                    var box = item as Dictionary<string, object>; if (box == null) return null;
                    var keys = new[] { "x", "y", "width", "height" }; var rect = new double[4];
                    for (var i = 0; i < keys.Length; i++)
                    {
                        if (!box.TryGetValue(keys[i], out value) || value == null) return null;
                        rect[i] = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        if (double.IsNaN(rect[i]) || double.IsInfinity(rect[i])) return null;
                    }
                    if (rect[0] < 0 || rect[1] < 0 || rect[0] > 100000 || rect[1] > 100000) return null;
                    pages.Add(rect);
                }
                return pages;
            }
            catch (Exception) { return null; }
        }
        // Writes a picture (or takes the finished PDF) under a temporary name, then gives it its own: never over a file.
        private string SaveReport(string temp, string extension, byte[] data)
        {
            try
            {
                if (data != null) File.WriteAllBytes(temp, data);
                var name = GradeReport.FileName(prefs.Language, DateTime.Now, extension);
                reportPath = SavedFile.Keep(temp, Path.GetDirectoryName(temp), name, null);
                reportPaths = new List<string> { reportPath };
                return Path.GetFileName(reportPath);
            }
            finally { GradeReport.DeletePart(temp); }
        }
        // One picture keeps the plain name. Several are named by page, with one number for the whole set when an earlier set
        // of the same day is there («Успеваемость 2026-11-18 (1) стр. 2.png»); a failure removes the pages already written.
        private async Task<string> SaveReportPicturesAsync(ChromiumWebBrowser browser, string address, GradeReport.Source source, List<byte[]> pictures)
        {
            var folder = reportFolder ?? DownloadService.DownloadsFolder();
            var language = prefs.Language;
            var made = DateTime.Now;
            var saved = await Task.Run(() => GradeReport.SavePictures(folder, language, made, pictures));
            if (!await ReportStillAsync(browser, address, source))
            {
                await Task.Run(() => GradeReport.DeleteParts(saved));
                return ReportCancelled;
            }
            reportPaths = saved; reportPath = saved[0];
            return Path.GetFileName(reportPath);
        }
        private static void DeleteReportPart(string temp) { GradeReport.DeletePart(temp); }
        // The picture as an image (messengers paste it) and as PNG for programs that take that format.
        private async Task<string> CopyReportPictureAsync(ChromiumWebBrowser browser, string address, GradeReport.Source source, byte[] png)
        {
            if (reportClipboard != null) { reportClipboard(png); return ""; }
            try
            {
                using (var bitmap = await Task.Run(() => DecodeBitmap(png)))
                {
                    if (!await ReportStillAsync(browser, address, source)) return ReportCancelled;
                    var data = new DataObject();
                    data.SetData(DataFormats.Bitmap, true, bitmap);
                    data.SetData("PNG", false, new MemoryStream(png));
                    Clipboard.SetDataObject(data, true, 5, 100);
                }
                return "";
            }
            catch (Exception) { return null; }
        }
        private static Bitmap DecodeBitmap(byte[] png)
        {
            using (var stream = new MemoryStream(png))
            using (var image = Image.FromStream(stream))
                return new Bitmap(image);
        }
        // «Показать в папке»: Explorer with the summary just saved selected; its folder when the file has gone.
        private void RevealReport()
        {
            try
            {
                if (reportPath != null && File.Exists(reportPath)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + reportPath + "\"") { UseShellExecute = true });
                else OpenDownloads();
            }
            catch (Exception) { SetStatus("downloadFailed"); }
        }
    }
}
