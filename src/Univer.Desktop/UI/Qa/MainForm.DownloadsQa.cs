using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Isolated QA: synthetic local files only; nothing is downloaded and the user's history is not touched.
        private async Task DownloadsQaAsync(string qa, List<string> results)
        {
            var folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", "files-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var pdf = Path.Combine(folder, "Силлабус QA.pdf"); File.WriteAllBytes(pdf, SyntheticPdf("Univer QA PDF"));
                var docx = Path.Combine(folder, "Методичка QA.docx"); SyntheticDocx(docx);
                var text = Path.Combine(folder, "Заметки QA.txt"); File.WriteAllText(text, "Проверка просмотра текста внутри приложения.\nUniver QA", new UTF8Encoding(false));
                var records = new List<DownloadRecord>
                {
                    new DownloadRecord { FileName = Path.GetFileName(pdf), Path = pdf, State = DownloadState.Completed, Total = new FileInfo(pdf).Length, Source = "Введение в QA", Started = DateTime.Now.AddMinutes(-3) },
                    new DownloadRecord { FileName = Path.GetFileName(docx), Path = docx, State = DownloadState.Completed, Total = new FileInfo(docx).Length, Source = "Методы QA", Started = DateTime.Now.AddMinutes(-2) },
                    new DownloadRecord { FileName = Path.GetFileName(text), Path = text, State = DownloadState.Completed, Total = new FileInfo(text).Length, Started = DateTime.Now.AddMinutes(-1) },
                    new DownloadRecord { FileName = "Лекция 5.pdf", State = DownloadState.Running, Received = 450000, Total = 1200000, Source = "Введение в QA" },
                    new DownloadRecord { FileName = "Старый файл.doc", Path = Path.Combine(folder, "missing.doc"), State = DownloadState.Completed, Total = 1000, Started = DateTime.Now.AddDays(-20) }
                };
                downloadsView.SetItems(records);
                ShowDownloads(); await Task.Delay(150);
                Check(DownloadsVisible && downloadsButton.Selected && pageTitle.Text == Strings.Get("downloads", prefs.Language), "downloads tab replaces the site view", results);
                downloadsView.SetLanguage("en");
                typeof(System.Windows.Forms.Control).GetMethod("RecreateHandle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(downloadsView.SearchForQa, null);
                var cueText = new System.Text.StringBuilder(256); CueMessage(downloadsView.SearchForQa.Handle, 0x1502, cueText, (IntPtr)cueText.Capacity);
                Check(cueText.ToString() == Strings.Get("downloadsSearch", "en"), "search hint keeps the current language after its window is recreated", results);
                downloadsView.SetLanguage(prefs.Language);
                Check(!mode.Visible && zoomIn.Visible && pageTitle.Visible, "downloads page hides the website-only reading menu and keeps zoom", results);
                downloadsView.SearchForQa.Text = "методичка";
                var found = downloadsView.RowsForQa;
                var escape = new System.Windows.Forms.Message(); ProcessCmdKey(ref escape, System.Windows.Forms.Keys.Escape);
                Check(found == 1 && downloadsView.SearchForQa.Text.Length == 0 && downloadsView.RowsForQa == records.Count && DownloadsVisible, "search filters downloads and Escape clears it first", results);
                downloadsView.SortForQa = DownloadSort.Name;
                Check(string.Join("|", downloadsView.RowOrderForQa) == "Заметки QA.txt|Лекция 5.pdf|Методичка QA.docx|Силлабус QA.pdf|Старый файл.doc", "downloads sort by name", results);
                downloadsView.SortForQa = DownloadSort.Size;
                Check(downloadsView.RowOrderForQa[0] == "Лекция 5.pdf", "downloads sort by size uses the expected total of a running file", results);
                downloadsView.SortForQa = DownloadSort.Newest;
                Check(downloadsView.RowOrderForQa[4] == "Старый файл.doc", "downloads return to newest first", results);
                // Course filter: the courses the files came from, with their counts; search works inside the chosen one.
                await RestoreQaWindowAsync();
                Check(string.Join("|", downloadsView.SubjectsForQa) == "Введение в QA=2|Методы QA=1" && downloadsView.SubjectButtonForQa.Visible && downloadsView.FilterRowFitsForQa, "downloads offer the courses their files came from, beside type and order", results);
                downloadsView.SubjectForQa = "Введение в QA";
                Check(downloadsView.RowsForQa == 2 && downloadsView.SubjectButtonForQa.Text == "Введение в QA" && downloadsView.SubjectButtonForQa.Selected &&
                    downloadsView.SubjectButtonForQa.AccessibleName == string.Format(Strings.Get("filterSubject", prefs.Language), "Введение в QA") && downloadsView.FilterRowFitsForQa, "choosing a course shows only its files and names it on the button", results);
                SaveDownloadsBitmap(Path.Combine(qa, "downloads-course.png"));
                downloadsView.SearchForQa.Text = "лекция";
                Check(downloadsView.RowsForQa == 1, "search works inside the chosen course", results);
                downloadsView.SearchForQa.Text = ""; downloadsView.SubjectForQa = null;
                Check(downloadsView.RowsForQa == records.Count && !downloadsView.SubjectButtonForQa.Selected, "all courses shows every file again", results);
                SaveDownloadsBitmap(Path.Combine(qa, "downloads-list.png"));
                OtherDownloadsQa(results);
                await RepeatDownloadQaAsync(qa, records[0], results);
                // The repeat notice moves focus, after which the desktop may minimize the hidden QA window: rows would lay out at width 0.
                await RestoreQaWindowAsync();
                // A file that finishes while the site is shown must have its actions placed without a window resize.
                HideDownloads();
                var background = records.Find(r => r.Path == pdf).Clone(); background.Id = Guid.NewGuid().ToString("N"); background.State = DownloadState.Running; background.Received = 1; downloadsView.Upsert(background);
                background = background.Clone(); background.State = DownloadState.Completed; background.Received = background.Total; downloadsView.Upsert(background);
                ShowDownloads(); await Task.Delay(100);
                Check(downloadsView.ActionsPlacedForQa(background.Id), "a download completed in the background shows its actions without resizing", results);
                downloadsView.Remove(background.Id);
                var wide = Width; Width = MinimumSize.Width; await Task.Delay(100);
                SaveDownloadsBitmap(Path.Combine(qa, "downloads-narrow.png"));
                Width = wide; await Task.Delay(50);
                downloadsView.PreferBuiltIn = true;
                foreach (var source in new[] { pdf, docx, text })
                {
                    var record = records.Find(r => r.Path == source);
                    downloadsView.Preview(record); await Task.Delay(100);
                    Check(downloadsView.Previewing && pageTitle.Text == record.FileName, "in-app preview opens " + Path.GetExtension(source), results);
                    var browser = downloadsView.ViewerForQa;
                    if (source == pdf && browser != null) browser.ConsoleMessage += (s, e) => QaTrace("viewer-console " + e.Level + " " + e.Message);
                    var until = DateTime.UtcNow.AddSeconds(15);
                    var ready = false;
                    while (DateTime.UtcNow < until && !ready)
                    {
                        await Task.Delay(200);
                        if (downloadsView.OfficeForQa) { ready = true; break; }
                        ready = browser != null && browser.IsBrowserInitialized && !browser.IsLoading && (browser.Address ?? "").Contains(Web.LocalFiles.Host);
                    }
                    Check(ready, "preview content loads for " + Path.GetExtension(source), results);
                    await Task.Delay(700);
                    if (browser != null && browser.Visible && !downloadsView.OfficeForQa)
                        using (var client = browser.GetDevToolsClient())
                            File.WriteAllBytes(Path.Combine(qa, "downloads-preview" + Path.GetExtension(source) + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                    if (source == docx)
                        Check(!downloadsView.OfficeForQa && await EvaluateLocalPreviewAsync(browser,
                            "document.body.textContent.includes('Методические указания QA') && document.querySelector('table') ? 'docx' : ''") == "docx",
                            "DOCX renders its heading and table without any installed Office preview", results);
                    if (source == pdf)
                    {
                        var state = await ReaderStateQaAsync(pdf, 1);
                        Check(!downloadsView.OfficeForQa && downloadsView.ReaderForQa && state != null && state.Text == 1 && state.Chars > 0,
                            "PDF opens in the bundled reader with searchable text, without an external preview handler", results);
                    }
                    if (!downloadsView.OfficeForQa) await FindRoundQaAsync("preview " + Path.GetExtension(source), "QA", 1, source == pdf ? 1 : -1, results);
                    if (source == pdf && browser != null) await PdfFindKeyQaAsync(browser, results);
                    await PreviewEscapeQaAsync(Path.GetExtension(source), results);
                }
                await PreviewShortcutsQaAsync(records.Find(r => r.Path == pdf), results);
                await PdfReleaseQaAsync(records.Find(r => r.Path == pdf), results);
                await PdfReaderQaAsync(folder, qa, results);
                downloadsView.PreferBuiltIn = false;
                downloadsView.Preview(records.Find(r => r.Path == docx)); await Task.Delay(300);
                Check(!downloadsView.OfficeForQa, "default DOCX preview always uses the bundled renderer", results);
                await DocxTablesQaAsync(qa, results, folder);
                await PreviewCancellationQaAsync(folder, results);
                await PreviewLimitsQaAsync(folder, qa, results);
                // Last: the Word handler runs in its own process and should not overlap the bundled-viewer geometry checks.
                await OfficeKeysQaAsync(folder, results);
                downloadsView.ClosePreview();
                Check(!downloadsView.Previewing && DownloadsVisible, "preview returns to the downloads list", results);
                HideDownloads();
                Check(!DownloadsVisible && !downloadsButton.Selected && mode.Visible && zoomIn.Visible, "site view and its controls return after downloads", results);
                Check(downloadsView.ViewerForQa == null, "closing downloads releases the preview browser and its renderer", results);
            }
            finally
            {
                downloadsView.SetItems(history.Items);
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        private static async Task<string> EvaluateLocalPreviewAsync(CefSharp.WinForms.ChromiumWebBrowser browser, string script)
        {
            if (browser == null || !browser.IsBrowserInitialized || !Web.LocalFiles.Owns(browser.Address)) return null;
            using (var frame = browser.GetMainFrame())
            {
                if (!frame.IsValid || !Web.LocalFiles.Owns(frame.Url)) return null;
                // A busy renderer (a large file being opened) misses the deadline; polling callers simply ask again.
                try
                {
                    var result = await frame.EvaluateScriptAsync(script, timeout: TimeSpan.FromSeconds(3));
                    return result.Success ? result.Result as string : null;
                }
                catch (TaskCanceledException) { return null; }
            }
        }
        private static async Task<bool> PdfViewerQaAsync(CefSharp.WinForms.ChromiumWebBrowser browser)
        {
            // Current Chromium runs the bundled PDF extension in a separate frame/process.
            foreach (var id in browser.GetBrowser().GetFrameIdentifiers())
#if LEGACY_ENGINE
                using (var frame = browser.GetBrowser().GetFrame(id))
#else
                using (var frame = browser.GetBrowser().GetFrameByIdentifier(id))
#endif
                {
                    if (frame == null || !frame.IsValid || !(Web.LocalFiles.Owns(frame.Url) || frame.Url.StartsWith("chrome-extension://", StringComparison.Ordinal))) continue;
                    var result = await frame.EvaluateScriptAsync("document.querySelector('pdf-viewer,embed[type=\"application/pdf\"]') ? 'pdf' : ''");
                    if (result.Success && result.Result as string == "pdf") return true;
                }
            return false;
        }
        // Opens the shown PDF in Chromium's own viewer, the reader's fallback, so its keyboard path is checked too.
        private async Task<CefSharp.WinForms.ChromiumWebBrowser> ChromiumPdfQaAsync(DownloadRecord pdf)
        {
            await OpenReaderQaAsync(pdf.Path, 1);
            var browser = downloadsView.ViewerForQa;
            await EvaluateLocalPreviewAsync(browser, "location.replace(JSON.parse(document.getElementById('reader-config').textContent).file);'moved'");
            var shown = false;
            for (var until = DateTime.UtcNow.AddSeconds(10); !shown && DateTime.UtcNow < until; await Task.Delay(150))
                shown = !browser.IsLoading && (browser.Address ?? "").Contains("/file/") && await PdfViewerQaAsync(browser);
            QaTrace("qa-chromium-pdf " + shown);
            return browser;
        }
        // SaveBitmap hides the canvas to keep WM_PRINT away from CEF; the list page has no browser, so draw it on top.
        private void SaveDownloadsBitmap(string path)
        {
            // The hidden QA window can be minimized from the desktop (Show desktop); its docked pages are then 0×0.
            if (WindowState == System.Windows.Forms.FormWindowState.Minimized) WindowState = System.Windows.Forms.FormWindowState.Normal;
            if (downloadsView.Width <= 0 || downloadsView.Height <= 0)
            { QaTrace("qa-bitmap-skipped " + Path.GetFileName(path)); return; }
            QaSnapshot(path, Width, Height, bitmap =>
            {
                var visibleCanvas = canvas.Visible; canvas.Visible = false;
                try { DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, Size)); }
                finally { canvas.Visible = visibleCanvas; }
                using (var view = new System.Drawing.Bitmap(downloadsView.Width, downloadsView.Height))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    downloadsView.DrawToBitmap(view, new System.Drawing.Rectangle(System.Drawing.Point.Empty, downloadsView.Size));
                    var origin = downloadsView.PointToScreen(System.Drawing.Point.Empty); // Form bitmaps start at the window frame.
                    graphics.DrawImage(view, origin.X - Left, origin.Y - Top);
                }
            });
        }
        private static void SyntheticDocx(string path, string body = null)
        {
            using (var file = File.Create(path)) using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Action<string, string> write = (name, content) => { using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(content); };
                write("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                write("_rels/.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                write("word/document.xml", "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                    (body ?? ("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Методические указания QA</w:t></w:r></w:p>" +
                    "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Цель:</w:t></w:r><w:r><w:t xml:space=\"preserve\"> проверить просмотр документа Word внутри приложения.</w:t></w:r></w:p>" +
                    "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Неделя</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Тема</w:t></w:r></w:p></w:tc></w:tr>" +
                    "<w:tr><w:tc><w:p><w:r><w:t>1</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Введение</w:t></w:r></w:p></w:tc></w:tr></w:tbl>")) + "</w:body></w:document>");
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")]
        // EM_GETCUEBANNER: wParam is the buffer, lParam its length in characters.
        private static extern IntPtr CueMessage(IntPtr window, int message, System.Text.StringBuilder text, IntPtr length);
    }
}
