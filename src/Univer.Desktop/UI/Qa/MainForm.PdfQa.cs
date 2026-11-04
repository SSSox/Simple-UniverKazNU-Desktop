using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // The bundled PDF reader: text over every page for find, the app zoom, print pictures, links, a scanned file's
        // hint and the hand-over of a file PDF.js cannot open to Chromium's own viewer. Synthetic files only.
        private async Task PdfReaderQaAsync(string folder, string qa, List<string> results)
        {
            var book = Path.Combine(folder, "Курс QA.pdf");
            File.WriteAllBytes(book, SyntheticPdf(new[] { "Univer reader page one", "Univer reader page two", "Final page QA-omega and QA-omega" }, "https://univer.kaznu.kz/"));
            var state = await OpenReaderQaAsync(book, 3);
            Check(state != null && state.Pages == 3 && state.Text == 3 && state.Drawn >= 1 && !state.Hint, "the PDF reader draws the pages and lays text over all three", results);
            var browser = downloadsView.ViewerForQa;
            Check(await EvaluateLocalPreviewAsync(browser, "document.querySelectorAll('.page .links a[href=\"https://univer.kaznu.kz/\"]').length===1?'link':''") == "link",
                "a web link in the PDF becomes a link over its page", results);
            await FindRoundQaAsync("PDF reader", "QA-omega", 2, 2, results);
            var open = new System.Windows.Forms.Message(); ProcessCmdKey(ref open, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F);
            findBar.InputForQa.Text = "QA-omega"; await FindCountQaAsync(0);
            await Task.Delay(400);
            Check(await EvaluateLocalPreviewAsync(browser, "(()=>{const p=document.querySelector('.page[data-page=\"3\"]').getBoundingClientRect();return p.top<innerHeight&&p.bottom>0?'shown':'';})()") == "shown",
                "find scrolls the reader to the page with the match", results);
            using (var client = browser.GetDevToolsClient())
                File.WriteAllBytes(Path.Combine(qa, "pdf-reader-find.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var close = new System.Windows.Forms.Message(); ProcessCmdKey(ref close, System.Windows.Forms.Keys.Escape);
            // Zoom: the page keeps its CSS width, so it grows on screen with the app zoom.
            var before = await ReaderScreenWidthQaAsync(browser);
            SetZoom(150); await Task.Delay(900);
            var after = await ReaderScreenWidthQaAsync(browser);
            ProcessCmdKey(ref open, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F);
            Check(findBar.Searching && findBar.Top >= canvas.Top + downloadsView.PreviewBarHeight && downloadsView.PreviewBarHeight > D(56),
                "at 150% the find bar opens below the file's taller bar", results);
            ProcessCmdKey(ref close, System.Windows.Forms.Keys.Escape);
            SetZoom(100); await Task.Delay(600);
            QaTrace("qa-reader-zoom " + before + " -> " + after);
            Check(before > 0 && after / before > 1.35 && after / before < 1.65, "the app zoom enlarges PDF pages like other files", results);
            await EvaluateLocalPreviewAsync(browser, "window.__qaPrint='';window.__univerReader.print(false).then(n=>{window.__qaPrint=String(n);});'started'");
            var printed = "";
            for (var until = DateTime.UtcNow.AddSeconds(15); printed == "" && DateTime.UtcNow < until; await Task.Delay(150))
                printed = await EvaluateLocalPreviewAsync(browser, "window.__qaPrint") ?? "";
            Check(printed == "3" && await EvaluateLocalPreviewAsync(browser, "document.getElementById('print')?'left':'gone'") == "gone",
                "printing draws every page at print resolution and cleans up afterwards", results);
            downloadsView.ClosePreview();
            // A page without text (a scan): the reader says that find has nothing to match.
            var scan = Path.Combine(folder, "Скан QA.pdf");
            File.WriteAllBytes(scan, SyntheticPdf(new string[] { null }));
            state = await OpenReaderQaAsync(scan, 1);
            Check(state != null && state.Hint && state.Chars == 0, "a PDF without text explains that find cannot match words in it", results);
            using (var client = downloadsView.ViewerForQa.GetDevToolsClient())
                File.WriteAllBytes(Path.Combine(qa, "pdf-reader-scan.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            downloadsView.ClosePreview();
            // A file PDF.js rejects goes to Chromium's own viewer at the same file.
            var broken = Path.Combine(folder, "Повреждён QA.pdf");
            File.WriteAllBytes(broken, Encoding.ASCII.GetBytes("%PDF-1.4\nnot a document\n%%EOF\n"));
            await downloadsView.PreviewAsync(Record(broken));
            browser = downloadsView.ViewerForQa;
            var fallback = false;
            for (var until = DateTime.UtcNow.AddSeconds(12); !fallback && DateTime.UtcNow < until; await Task.Delay(200))
                fallback = browser != null && !browser.IsLoading && (browser.Address ?? "").Contains("/file/");
            Check(fallback && downloadsView.FindTarget == browser, "a PDF the reader cannot open moves to Chromium's own viewer and keeps find", results);
            downloadsView.ClosePreview();
            await LocalPdfQaAsync(results);
        }
        // Optional, read-only timing of the user's own PDF files (UNIVER_QA_PDF: absolute paths separated by ';'). Only
        // counts and timings reach the trace; nothing is copied, captured or added to the downloads list.
        private async Task LocalPdfQaAsync(List<string> results)
        {
            var supplied = Environment.GetEnvironmentVariable("UNIVER_QA_PDF");
            if (string.IsNullOrEmpty(supplied)) return;
            var number = 0;
            foreach (var path in supplied.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                number++;
                if (!Path.IsPathRooted(path) || !File.Exists(path) || !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("QA requires existing absolute PDF paths");
                await downloadsView.PreviewAsync(Record(path));
                string raw = null; ReaderState state = null; var started = DateTime.UtcNow;
                for (var until = DateTime.UtcNow.AddSeconds(180); DateTime.UtcNow < until; await Task.Delay(250))
                {
                    raw = await EvaluateLocalPreviewAsync(downloadsView.ViewerForQa, "window.__univerReader?window.__univerReader.state():''");
                    if (string.IsNullOrEmpty(raw)) continue;
                    state = new JavaScriptSerializer().Deserialize<ReaderState>(raw);
                    if (state.Pages > 0 && state.Text == state.Pages && state.Drawn >= 1) break;
                }
                var open = new System.Windows.Forms.Message(); ProcessCmdKey(ref open, System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.F);
                findBar.InputForQa.Text = "а"; var matches = await FindCountQaAsync(0);
                var close = new System.Windows.Forms.Message(); ProcessCmdKey(ref close, System.Windows.Forms.Keys.Escape);
                QaTrace("qa-local-pdf " + number + " bytes=" + new FileInfo(path).Length + " seconds=" + (DateTime.UtcNow - started).TotalSeconds.ToString("0.0") +
                    " state=" + (raw ?? "none") + " find-a=" + matches + " address-file=" + (downloadsView.ViewerForQa?.Address ?? "").Contains("/file/"));
                Check(state != null && state.Pages > 0 && state.Text == state.Pages, "local PDF " + number + " opens in the reader with text for every page", results);
                downloadsView.ClosePreview();
            }
        }
        private sealed class ReaderState { public int Pages = 0, Text = 0, Chars = 0, Drawn = 0; public double Width = 0; public bool Hint = false, Failed = false; public Dictionary<string, int> Timing = null; }
        private async Task<ReaderState> OpenReaderQaAsync(string path, int pages)
        {
            await downloadsView.PreviewAsync(Record(path));
            return await ReaderStateQaAsync(path, pages);
        }
        // Waits until the shown PDF has all its pages with text and at least one drawn page.
        private async Task<ReaderState> ReaderStateQaAsync(string path, int pages)
        {
            ReaderState state = null;
            for (var until = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < until; await Task.Delay(150))
            {
                // PDF.js draws only visible pages: a hidden QA window minimized from the desktop would never draw one.
                await RestoreQaWindowAsync();
                var raw = await EvaluateLocalPreviewAsync(downloadsView.ViewerForQa, "window.__univerReader?window.__univerReader.state():''");
                if (string.IsNullOrEmpty(raw)) continue;
                state = new JavaScriptSerializer().Deserialize<ReaderState>(raw);
                if (state.Pages == pages && state.Text == pages && state.Drawn >= 1) break;
            }
            var browser = downloadsView.ViewerForQa;
            QaTrace("qa-reader " + Path.GetFileName(path) + " " +
                (state == null ? "none at " + browser?.Address + " " + await EvaluateLocalPreviewAsync(browser, "document.readyState+' '+typeof window.__univerReader+' '+document.body.innerText.slice(0,80)")
                : state.Pages + "/" + state.Text + "/" + state.Drawn + "/" + state.Chars + (state.Hint ? "/hint" : "")));
            return state;
        }
        private static async Task<double> ReaderScreenWidthQaAsync(ChromiumWebBrowser browser)
        {
            double width;
            var raw = await EvaluateLocalPreviewAsync(browser, "String(document.querySelector('.page').getBoundingClientRect().width*devicePixelRatio)");
            return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out width) ? width : 0;
        }
        private static DownloadRecord Record(string path)
        { return new DownloadRecord { FileName = Path.GetFileName(path), Path = path, State = DownloadState.Completed, Total = new FileInfo(path).Length }; }
        private static byte[] SyntheticPdf(string text) { return SyntheticPdf(new[] { text }); }
        // A minimal PDF: one Helvetica line per page (null draws a grey box and no text, like a scan) and an optional web
        // link over the first page's line.
        private static byte[] SyntheticPdf(string[] pages, string link = null)
        {
            var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", null, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
            var kids = new List<int>();
            for (var i = 0; i < pages.Length; i++)
            {
                var stream = pages[i] == null ? "0.85 g 72 560 451 200 re f" : "BT /F1 28 Tf 72 760 Td (" + pages[i] + ") Tj ET";
                objects.Add("<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream");
                var content = objects.Count; var annotations = "";
                if (link != null && i == 0)
                {
                    objects.Add("<< /Type /Annot /Subtype /Link /Rect [72 750 400 790] /Border [0 0 0] /A << /S /URI /URI (" + link + ") >> >>");
                    annotations = " /Annots [" + objects.Count + " 0 R]";
                }
                objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents " + content + " 0 R /Resources << /Font << /F1 3 0 R >> >>" + annotations + " >>");
                kids.Add(objects.Count);
            }
            objects[1] = "<< /Type /Pages /Kids [" + string.Join(" ", kids.Select(k => k + " 0 R")) + "] /Count " + kids.Count + " >>";
            var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
            for (var i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); }
            var xref = pdf.Length;
            pdf.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (var offset in offsets) pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
            pdf.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }
    }
}
