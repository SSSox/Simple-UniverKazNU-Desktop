using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private async Task DocxTablesQaAsync(string qa, List<string> results, string folder)
        {
            var synthetic = Path.Combine(folder, "Критерии оценивания QA.docx");
            SyntheticDocx(synthetic, WideDocxBody());
            await CheckDocxLayoutAsync(synthetic, qa, "docx", results, true);
            // Optional, read-only reproduction of a user-supplied local document. The path is
            // never copied into history or packages; screenshots remain in the excluded QA folder.
            var supplied = Environment.GetEnvironmentVariable("UNIVER_QA_DOCX");
            if (!string.IsNullOrEmpty(supplied))
            {
                if (!Path.IsPathRooted(supplied) || !File.Exists(supplied) || !string.Equals(Path.GetExtension(supplied), ".docx", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("QA requires an existing absolute DOCX path");
                await CheckDocxLayoutAsync(supplied, qa, "docx-user", results, false);
            }
            // Optional Word 97-2003 file (for example tests/Univer.Desktop.Tests/Fixtures/synthetic.doc), shown by the
            // bundled renderer as on a PC without Office.
            var legacy = Environment.GetEnvironmentVariable("UNIVER_QA_DOC");
            if (!string.IsNullOrEmpty(legacy))
            {
                if (!Path.IsPathRooted(legacy) || !File.Exists(legacy) || !string.Equals(Path.GetExtension(legacy), ".doc", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("QA requires an existing absolute DOC path");
                var preferred = downloadsView.PreferBuiltIn; downloadsView.PreferBuiltIn = true;
                try
                {
                    downloadsView.Preview(new DownloadRecord { FileName = Path.GetFileName(legacy), Path = legacy, State = DownloadState.Completed, Total = new FileInfo(legacy).Length });
                    var title = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Path.GetFileName(legacy));
                    var ready = "";
                    for (var until = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < until && ready != "ready"; await Task.Delay(100))
                        ready = await EvaluateLocalPreviewAsync(downloadsView.ViewerForQa, "document.title === " + title + " && document.body.innerText.trim().length > 0 ? 'ready' : ''");
                    Check(ready == "ready" && !downloadsView.OfficeForQa, "DOC renders with the bundled renderer without an Office preview handler", results);
                    // Find in a DOC file is Chromium's find over the same page shell as DOCX.
                    await FindRoundQaAsync("DOC preview", "QA", 1, -1, results);
                    await Task.Delay(300);
                    using (var client = downloadsView.ViewerForQa.GetDevToolsClient())
                        File.WriteAllBytes(Path.Combine(qa, "doc-builtin.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                }
                finally { downloadsView.PreferBuiltIn = preferred; downloadsView.ClosePreview(); }
            }
        }
        private async Task CheckDocxLayoutAsync(string path, string qa, string prefix, List<string> results, bool synthetic)
        {
            var originalWidth = Width;
            try
            {
                Width = Math.Min(Screen.FromControl(this).WorkingArea.Width, D(1440));
                downloadsView.Preview(new DownloadRecord { FileName = Path.GetFileName(path), Path = path, State = DownloadState.Completed, Total = new FileInfo(path).Length });
                var browser = downloadsView.ViewerForQa;
                var until = DateTime.UtcNow.AddSeconds(10);
                var title = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(Path.GetFileName(path));
                while (DateTime.UtcNow < until && await EvaluateLocalPreviewAsync(browser, "document.title === " + title + " && document.querySelector('.table-scroll table') ? 'ready' : ''") != "ready")
                    await Task.Delay(100);
                await EvaluateLocalPreviewAsync(browser, "document.querySelector('.table-scroll').scrollIntoView({block:'start'}); 'positioned'");
                await Task.Delay(250);
                Check(await EvaluateLocalPreviewAsync(browser,
                    "document.documentElement.scrollWidth <= innerWidth + 1 && [...document.querySelectorAll('.table-scroll')].every(s => s.getBoundingClientRect().right <= innerWidth && s.clientWidth >= s.scrollWidth - 1) ? 'fit' : ''") == "fit",
                    prefix + ": all table columns fit the wide viewer without page overflow", results);
                Check(await EvaluateLocalPreviewAsync(browser,
                    "document.querySelectorAll('td[rowspan=\"3\"]').length === 2 && document.querySelector('td[colspan=\"5\"]') ? 'merged' : ''") == "merged",
                    prefix + ": three-row headers and five-column heading retain merged cells", results);
                using (var client = browser.GetDevToolsClient())
                    File.WriteAllBytes(Path.Combine(qa, prefix + "-wide.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                Width = MinimumSize.Width;
                await Task.Delay(350);
                await EvaluateLocalPreviewAsync(browser, "document.querySelector('.table-scroll').scrollIntoView({block:'start'}); 'positioned'");
                await Task.Delay(100);
                File.WriteAllText(Path.Combine(qa, prefix + "-layout.json"), await EvaluateLocalPreviewAsync(browser,
                    "JSON.stringify({viewport:innerWidth,viewportHeight:innerHeight,page:document.documentElement.scrollWidth,tables:[...document.querySelectorAll('.table-scroll')].map(s=>({width:s.clientWidth,content:s.scrollWidth,height:s.clientHeight,contentHeight:s.scrollHeight,right:s.getBoundingClientRect().right,bottom:s.getBoundingClientRect().bottom}))})"));
                Check(await EvaluateLocalPreviewAsync(browser,
                    "document.documentElement.scrollWidth <= innerWidth + 1 && [...document.querySelectorAll('.table-scroll')].some(s => s.scrollWidth > s.clientWidth && s.getBoundingClientRect().right <= innerWidth && s.getBoundingClientRect().bottom <= innerHeight + 1) ? 'scroll' : ''") == "scroll",
                    prefix + ": narrow viewer contains horizontal scrolling and keeps both scrollbars within the viewport", results);
                if (synthetic)
                    Check(await EvaluateLocalPreviewAsync(browser,
                        "document.querySelector('.table-scroll').scrollHeight > document.querySelector('.table-scroll').clientHeight ? 'tall' : ''") == "tall",
                        "tall DOCX table has accessible vertical and horizontal scrollbars", results);
                using (var client = browser.GetDevToolsClient())
                    File.WriteAllBytes(Path.Combine(qa, prefix + "-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await EvaluateLocalPreviewAsync(browser, "document.querySelector('.table-scroll').focus(); 'focused'");
                browser.Focus();
                browser.GetBrowser().GetHost().SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyDown, WindowsKeyCode = (int)Keys.Right });
                browser.GetBrowser().GetHost().SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = (int)Keys.Right });
                // Smooth scrolling may finish later under load; poll for the first movement within a bound.
                var moved = "";
                for (var scrollEnd = DateTime.UtcNow.AddMilliseconds(1500); moved != "moved" && DateTime.UtcNow < scrollEnd;)
                { await Task.Delay(100); moved = await EvaluateLocalPreviewAsync(browser, "document.querySelector('.table-scroll').scrollLeft > 0 ? 'moved' : ''"); }
                Check(moved == "moved", prefix + ": focused table scrolls horizontally with the keyboard", results);
                Check(await EvaluateLocalPreviewAsync(browser,
                    "(() => { let s=document.querySelector('.table-scroll'); s.scrollLeft=s.scrollWidth; s.scrollTop=s.scrollHeight; let c=s.querySelector('tr:last-child td:last-child').getBoundingClientRect(),r=s.getBoundingClientRect(); return c.right<=r.right && c.left>=r.left && c.bottom<=r.bottom ? 'visible' : ''; })()") == "visible",
                    prefix + ": last column and bottom row are fully reachable", results);
                await Task.Delay(150);
                using (var client = browser.GetDevToolsClient())
                {
                    File.WriteAllBytes(Path.Combine(qa, prefix + "-last-column.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                    await client.Emulation.SetEmulatedMediaAsync(media: "print");
                    Check(await EvaluateLocalPreviewAsync(browser,
                        "[...document.querySelectorAll('.table-scroll')].every(s => getComputedStyle(s).maxHeight==='none' && getComputedStyle(s).overflow==='visible' && getComputedStyle(s.querySelector('table')).minWidth==='0px') ? 'print' : ''") == "print",
                        prefix + ": printing removes scroll limits instead of clipping the table", results);
                    await client.Emulation.SetEmulatedMediaAsync(media: "screen");
                }
            }
            finally { Width = originalWidth; }
        }
        private static string WideDocxBody()
        {
            Func<string, string, string> cell = (text, properties) => "<w:tc><w:tcPr>" + properties + "</w:tcPr><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:tc>";
            var html = new StringBuilder("<w:p><w:pPr><w:pStyle w:val='Heading1'/></w:pPr><w:r><w:t>Критерии оценивания</w:t></w:r></w:p><w:tbl><w:tblGrid>");
            foreach (var width in new[] { 701, 993, 1842, 1701, 1701, 1418, 1418 }) html.Append("<w:gridCol w:w='").Append(width).Append("'/>");
            html.Append("</w:tblGrid><w:tr>").Append(cell("№ вопроса", "<w:vMerge w:val='restart'/>")).Append(cell("Критерий", "<w:vMerge w:val='restart'/>")).Append(cell("Дескрипторы", "<w:gridSpan w:val='5'/>")).Append("</w:tr>");
            html.Append("<w:tr>").Append(cell("", "<w:vMerge/>")).Append(cell("", "<w:vMerge/>"));
            foreach (var label in new[] { "Отлично", "Хорошо", "Удовлетворительно" }) html.Append(cell(label, ""));
            html.Append(cell("Неудовлетворительно", "<w:gridSpan w:val='2'/>")).Append("</w:tr><w:tr>").Append(cell("", "<w:vMerge/>")).Append(cell("", "<w:vMerge/>"));
            foreach (var label in new[] { "90–100 %", "70–89 %", "50–69 %", "25–49 %", "0–24 %" }) html.Append(cell(label, ""));
            html.Append("</w:tr>");
            for (var row = 0; row < 4; row++)
            {
                html.Append("<w:tr>").Append(cell((row + 1).ToString(), "")).Append(cell("Знание и понимание темы курса", ""));
                for (var column = 0; column < 5; column++)
                    html.Append(cell(string.Join(" ", Enumerable.Repeat("Ответ раскрывает содержание темы. Приведены примеры и объяснения.", 5)) + (column == 4 ? " Конец последнего столбца." : ""), ""));
                html.Append("</w:tr>");
            }
            return html.Append("</w:tbl>").ToString();
        }
    }
}
