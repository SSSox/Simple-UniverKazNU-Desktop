using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The schedule page's «В календарь» and «Картинкой» (overview.js): on the student's press the page reads the shown week
    // and its neighbour, keeps them and signals; the app takes the reading once, checks every field (Core/ScheduleExport.cs)
    // and writes a calendar file or a picture to Downloads beside the site's downloads, never over a file. Nothing is kept.
    internal sealed partial class MainForm
    {
        private bool scheduleExportBusy;
        // QA: the folder files go to, in place of Downloads.
        private string scheduleExportFolder;
        private const string ScheduleExportRead = "window.__univerOverviewV1&&window.__univerOverviewV1.exportData?String(window.__univerOverviewV1.exportData()):''";
        private const float PictureScale = 2, PictureColumn = 440, PictureMaxHeight = 12000;

        private async Task ScheduleExportAsync(ChromiumWebBrowser browser, string kind)
        {
            if (closing || browser == null || browser.IsDisposed) return;
            var address = browser.Address; var epoch = sessionEpoch;
            // A press the app cannot take now (another export, the window busy or locked, not the shown schedule page) only
            // frees the page's buttons.
            if (kind == null || scheduleExportBusy || rotating || commandBusy || LockShown || browser != Active || !SitePolicy.SchedulePage(address)) { AnswerScheduleExport(browser, false); return; }
            scheduleExportBusy = true; string saved = null; SchedulePair pair = null; var cancelled = false;
            try
            {
                if (messages.Withdraw("scheduleFailed") | messages.Withdraw("schedulePart")) RenderFeedback();
                var raw = await AutomationController.EvaluateAsync(browser, ScheduleExportRead);
                // Left, reloaded or signed out while the reading came, or nothing to fetch (a reload at the same address, the
                // page's own minute ran out): nothing is written and nothing is told.
                if (string.IsNullOrEmpty(raw) || !ScheduleExportStill(browser, address, epoch)) cancelled = true;
                else
                {
                    pair = SchedulePair.Parse(raw, DateTime.Now);
                    if (pair != null) saved = SaveScheduleExport(pair, kind);
                }
            }
            catch (Exception) { saved = null; }
            finally { scheduleExportBusy = false; }
            if (closing) return;
            if (!cancelled)
            {
                if (saved == null) SetStatus("scheduleFailed");
                else
                {
                    var size = 0L; try { size = new FileInfo(saved).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    OnDownloadChanged(new DownloadRecord { FileName = Path.GetFileName(saved), Path = saved, State = DownloadState.Completed, Received = size, Total = size, Finished = DateTime.Now, Source = Strings.Get("scheduleSource", prefs.Language) });
                    SetStatus(pair.Missing.Count > 0 ? "schedulePart" : "downloadComplete");
                }
            }
            AnswerScheduleExport(browser, saved != null);
        }
        private bool ScheduleExportStill(ChromiumWebBrowser browser, string address, int epoch)
        {
            if (closing || browser.IsDisposed || browser != Active || epoch != sessionEpoch || browser.Address != address) return false;
            return !(awaitingCommit == browser && DateTime.UtcNow - awaitingSince < TimeSpan.FromSeconds(20));
        }
        private static void AnswerScheduleExport(ChromiumWebBrowser browser, bool ok)
        {
            if (browser.IsDisposed) return;
            _ = AutomationController.EvaluateAsync(browser, "window.__univerOverviewV1&&window.__univerOverviewV1.exported&&window.__univerOverviewV1.exported(" + (ok ? "true" : "false") + ");'ok'");
        }
        // The file's full path, written under a temporary name first; null when it could not be made.
        private string SaveScheduleExport(SchedulePair pair, string kind)
        {
            var lang = prefs.Language;
            var data = kind == ScheduleExport.Calendar ? new UTF8Encoding(false).GetBytes(ScheduleCalendar.Build(pair, lang, DateTime.Now)) : SchedulePicture(pair, lang);
            if (data == null) return null;
            var folder = scheduleExportFolder ?? DownloadService.DownloadsFolder();
            Directory.CreateDirectory(folder);
            var temp = Path.Combine(folder, PartialDownloads.NewName());
            try
            {
                File.WriteAllBytes(temp, data);
                return SavedFile.Keep(temp, folder, SchedulePair.FileName(pair, lang, "." + kind), null);
            }
            catch (Exception) { return null; }
            finally { DeleteReportPart(temp); }
        }
        // The weeks side by side in the order of their numbers, each the offline screen's day cards, at twice the CSS size
        // (a very long week is drawn smaller, never under 1); the current theme's colours.
        internal static byte[] SchedulePicture(SchedulePair pair, string lang)
        {
            var columns = pair.Weeks.Select(w => new { w.Number, w.Week, Heading = SchedulePair.Heading(w, lang), Note = (string)null })
                .Concat(pair.Missing.Select(n => new { Number = n, Week = (ScheduleWeek)null, Heading = pair.MissingHeading(n, lang), Note = Strings.Get("exportMissing", lang) }))
                .OrderBy(c => c.Number).ToList();
            var sheets = new List<OfflineWeekView.WeekSheet>();
            try
            {
                foreach (var column in columns) { var sheet = new OfflineWeekView.WeekSheet(); sheets.Add(sheet); sheet.SetExport(column.Week, column.Heading, column.Note, lang); }
                var scale = PictureScale;
                foreach (var sheet in sheets) sheet.Arrange((int)Math.Round(PictureColumn * scale), scale);
                var height = sheets.Max(s => s.Size.Height);
                if (height > PictureMaxHeight)
                {
                    scale = Math.Max(1, scale * PictureMaxHeight / height);
                    foreach (var sheet in sheets) sheet.Arrange((int)Math.Round(PictureColumn * scale), scale);
                    height = sheets.Max(s => s.Size.Height);
                }
                using (var bitmap = new Bitmap(sheets.Sum(s => s.Size.Width), height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Theme.Background);
                        var x = 0;
                        foreach (var sheet in sheets)
                        {
                            var state = g.Save(); g.TranslateTransform(x, 0); sheet.Paint(g); g.Restore(state);
                            x += sheet.Size.Width;
                        }
                    }
                    using (var stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
                }
            }
            catch (Exception) { return null; }
            finally { foreach (var sheet in sheets) sheet.Dispose(); }
        }
    }
}
