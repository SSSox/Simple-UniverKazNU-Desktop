using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Workbooks, presentations and web pages in the bundled viewer, from the synthetic files smoke.ps1 points
        // UNIVER_QA_FIXTURES at; nothing personal is read.
        private async Task OfficeQaAsync(string qa, List<string> results)
        {
            var folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", "office-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var page = Path.Combine(folder, "Страница QA.html");
                File.WriteAllText(page, "<html><head><title>Чужой заголовок</title><script>document.title='script ran'</script><style>body{background:red}</style></head>" +
                    "<body onload=\"document.title='handler ran'\"><h1>Страница QA</h1><p>Univer QA текст <a href=\"javascript:alert(1)\">ссылка</a></p>" +
                    "<img src=\"https://example.com/qa.png\" alt=\"Удалённый рисунок\"><table><tr><td bgcolor=yellow>QA ячейка</td></tr></table></body></html>", new UTF8Encoding(false));
                var files = new List<string> { page };
                var fixtures = Environment.GetEnvironmentVariable("UNIVER_QA_FIXTURES");
                if (!string.IsNullOrEmpty(fixtures))
                    foreach (var name in new[] { "synthetic.xlsx", "synthetic.xls", "synthetic.pptx", "synthetic.ppt" })
                    {
                        var source = Path.Combine(fixtures, name);
                        if (!File.Exists(source)) throw new FileNotFoundException("QA fixture missing", source);
                        var copy = Path.Combine(folder, "QA " + name); File.Copy(source, copy); files.Add(copy);
                    }
                else QaTrace("qa-office-fixtures-unavailable");
                var records = files.Select((path, i) => new DownloadRecord { FileName = Path.GetFileName(path), Path = path, State = DownloadState.Completed, Total = new FileInfo(path).Length, Started = DateTime.Now.AddMinutes(-i) }).ToList();
                downloadsView.SetItems(records);
                ShowDownloads(); await Task.Delay(150);
                var texts = new Dictionary<string, string>();
                foreach (var record in records)
                {
                    var extension = Path.GetExtension(record.Path).ToLowerInvariant();
                    var browser = await OpenPreviewQaAsync(record);
                    Check(browser != null && !downloadsView.OfficeForQa && downloadsView.MessageForQa == "", extension + " opens in the bundled viewer without an Office preview handler", results);
                    texts[extension] = await EvaluateLocalPreviewAsync(browser, "document.body.innerText");
                    if (extension == ".html")
                        Check(await EvaluateLocalPreviewAsync(browser, "document.title===" + Json(record.FileName) + "&&!document.querySelector('script,img[src^=\"http\"],a[href^=\"javascript\"],[onload]')&&" +
                            "document.querySelector('.omitted').textContent==='Удалённый рисунок'&&getComputedStyle(document.body).backgroundColor!=='rgb(255, 0, 0)'?'safe':''") == "safe",
                            "a web page shows without its scripts, handlers, styles or remote pictures", results);
                    else if (extension == ".xlsx" || extension == ".xls")
                        Check(await EvaluateLocalPreviewAsync(browser, "document.querySelectorAll('table.grid').length===2&&document.querySelectorAll('.sheets a').length===2&&document.querySelector('td[colspan=\"4\"]')?'sheets':''") == "sheets",
                            extension + " shows both visible sheets with their merged title", results);
                    else
                        Check(await EvaluateLocalPreviewAsync(browser, "document.querySelectorAll('section.slide').length===4&&document.querySelector('.slide h2')&&document.querySelector('.slide img')&&document.querySelector('.slide td[colspan=\"2\"]')?'slides':''") == "slides",
                            extension + " shows four slides with titles, the picture and the merged table cell", results);
                    var needle = extension == ".html" ? "QA" : extension.StartsWith(".xl", StringComparison.Ordinal) ? "Студент" : "пункт";
                    await FindRoundQaAsync("preview " + extension, needle, 2, -1, results);
                    using (var client = browser.GetDevToolsClient())
                        File.WriteAllBytes(Path.Combine(qa, "preview" + extension.Replace('.', '-') + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                }
                if (texts.ContainsKey(".xls")) Check(texts[".xls"] == texts[".xlsx"], "XLS and XLSX show the same text in the same layout", results);
                if (texts.ContainsKey(".ppt")) Check(texts[".ppt"] == texts[".pptx"], "PPT and PPTX show the same text in the same layout", results);
                downloadsView.ClosePreview();
            }
            finally
            {
                downloadsView.ClosePreview(); HideDownloads(); downloadsView.SetItems(history.Items);
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        private static string Json(string value) { return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value); }
        private async Task<CefSharp.WinForms.ChromiumWebBrowser> OpenPreviewQaAsync(DownloadRecord record)
        {
            downloadsView.Preview(record);
            var title = Json(record.FileName);
            for (var until = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < until; await Task.Delay(100))
                if (await EvaluateLocalPreviewAsync(downloadsView.ViewerForQa, "document.title===" + title + "&&document.readyState==='complete'&&document.body.innerText.trim().length>0?'ready':''") == "ready")
                    return downloadsView.ViewerForQa;
            QaTrace("qa-preview-timeout " + Path.GetExtension(record.Path) + " message=" + downloadsView.MessageForQa);
            return null;
        }
        // Both themes in every window: the shell, Settings, Downloads with a converted document and the PDF reader, and the
        // website's reading layout. Native text keeps at least 4.5:1 against its background; pages switch without reopening.
        private async Task ThemeQaAsync(string qa, List<string> results)
        {
            var saved = prefs.Theme; var readable = prefs.Readable;
            var folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", "theme-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                prefs.Theme = ThemeChoice.Dark; ApplyTheme();
                Check(Theme.Dark && sidebar.BackColor.ToArgb() == Theme.DarkPalette.Surface.ToArgb() && workspace.BackColor.ToArgb() == Theme.DarkPalette.Background.ToArgb() && canvas.BackColor.ToArgb() == Theme.DarkPalette.Background.ToArgb(),
                    "dark theme recolours the open window", results);
                Check(ReadableQa(this, "shell"), "every label in the dark shell keeps 4.5:1 contrast", results);
                SaveBitmap(this, Path.Combine(qa, "theme-dark-shell.png"));
                using (var form = new SettingsForm(new Credentials { Login = "QA student", Password = "synthetic fixture" }, prefs))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); await Task.Delay(60);
                    form.SelectAppearanceForQa();
                    Check(form.BackColor.ToArgb() == Theme.DarkPalette.Surface.ToArgb() && form.SelectedTheme == ThemeChoice.Dark && ReadableQa(form, "settings"), "Settings opens dark with the chosen theme and readable text", results);
                    var combo = Descendants(form).OfType<FadeComboBox>().FirstOrDefault();
                    Check(combo != null && PixelQa(combo, 4, combo.Height / 2, Theme.DarkPalette.Surface, "language list"), "the language list is drawn dark, not by Windows' light box", results);
                    SaveBitmap(form, Path.Combine(qa, "theme-dark-settings.png")); form.Close();
                }
                var pdf = Path.Combine(folder, "Тема QA.pdf"); File.WriteAllBytes(pdf, SyntheticPdf("Univer QA PDF"));
                var docx = Path.Combine(folder, "Тема QA.docx"); SyntheticDocx(docx);
                var records = new List<DownloadRecord>
                {
                    new DownloadRecord { FileName = Path.GetFileName(docx), Path = docx, State = DownloadState.Completed, Total = new FileInfo(docx).Length, Started = DateTime.Now },
                    new DownloadRecord { FileName = Path.GetFileName(pdf), Path = pdf, State = DownloadState.Completed, Total = new FileInfo(pdf).Length, Started = DateTime.Now.AddMinutes(-1) }
                };
                downloadsView.SetItems(records); ShowDownloads(); await Task.Delay(200);
                Check(ReadableQa(downloadsView, "downloads"), "the dark downloads list keeps readable text", results);
                // Rounded corners show what is behind them: the page around the search field and a file's card, the card
                // around that file's buttons.
                await RestoreQaWindowAsync();
                var row = Descendants(downloadsView).OfType<DownloadRow>().FirstOrDefault(x => x.Width > 0);
                var button = row?.Controls.OfType<ModernButton>().FirstOrDefault(x => x.Visible && x.Look == ButtonLook.Outline && x.Width > 0);
                Check(PixelQa(downloadsView.SearchForQa.Parent, 1, 1, Theme.DarkPalette.Background, "search corner") && row != null && PixelQa(row, 1, 1, Theme.DarkPalette.Background, "row corner") &&
                    button != null && PixelQa(button, 1, 1, Theme.DarkPalette.Surface, "button corner"), "dark Downloads corners blend into the page and the file card", results);
                SaveBitmap(this, Path.Combine(qa, "theme-dark-downloads.png"));
                SaveBitmap(downloadsView, Path.Combine(qa, "theme-dark-downloads-list.png"));
                const string dark = "(()=>{const l=c=>{const m=c.match(/\\d+/g).map(Number).map(v=>{v/=255;return v<=0.04045?v/12.92:Math.pow((v+0.055)/1.055,2.4)});return .2126*m[0]+.7152*m[1]+.0722*m[2]};" +
                    "const b=getComputedStyle(document.body),h=getComputedStyle(document.documentElement);return document.documentElement.classList.contains('dark')+'|'+l(h.backgroundColor).toFixed(3)+'|'+l(b.color).toFixed(3)})()";
                var browser = await OpenPreviewQaAsync(records[0]);
                var state = (await EvaluateLocalPreviewAsync(browser, dark) ?? "").Split('|');
                Check(state.Length == 3 && state[0] == "true" && double.Parse(state[1], System.Globalization.CultureInfo.InvariantCulture) < 0.03 && double.Parse(state[2], System.Globalization.CultureInfo.InvariantCulture) > 0.6,
                    "a converted document opens on the dark page with light text", results);
                using (var client = browser.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "theme-dark-docx.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                // Switching while the file is shown serves it again in the new colours.
                prefs.Theme = ThemeChoice.Light; ApplyTheme();
                var light = "";
                for (var until = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < until && light != "light"; await Task.Delay(100))
                    light = await EvaluateLocalPreviewAsync(downloadsView.ViewerForQa, "document.readyState==='complete'&&!document.documentElement.classList.contains('dark')&&getComputedStyle(document.documentElement).backgroundColor==='rgb(238, 242, 246)'?'light':''");
                Check(light == "light" && !Theme.Dark && sidebar.BackColor.ToArgb() == Theme.Light.Surface.ToArgb(), "switching back to light recolours the window and the shown document", results);
                Check(ReadableQa(this, "light shell"), "every label in the light shell keeps 4.5:1 contrast", results);
                prefs.Theme = ThemeChoice.Dark; ApplyTheme();
                var reader = await OpenPreviewQaAsync(records[1]);
                await Task.Delay(400);
                Check(await EvaluateLocalPreviewAsync(reader, "document.documentElement.classList.contains('dark')&&getComputedStyle(document.documentElement).backgroundColor==='rgb(21, 25, 30)'?'dark':''") == "dark",
                    "the PDF reader opens in the dark theme", results);
                using (var client = reader.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "theme-dark-pdf.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                downloadsView.ClosePreview(); HideDownloads();
                // The website's reading layout follows; Original keeps the site's own colours.
                prefs.Readable = true;
                // An app icon as the home shortcuts draw it: a black SVG mask in a custom property of an app stylesheet in the head.
                main.LoadHtml("<style id='univer-qa-icons'>:root{--qa-icon:url(\"data:image/svg+xml," + Uri.EscapeDataString("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='black' stroke-width='3'><path d='M6 6l12 12M18 6L6 18'/></svg>").Replace("%27", "'") +
                    "\")}.qa-icon{display:inline-block;width:48px;height:48px;background:#000;-webkit-mask:var(--qa-icon) center/contain no-repeat}</style>" +
                    "<table class='wrappertable'><tr id='l4'><td class='center'><table class='inner'><tr><th>Дисциплина</th><th>Балл</th></tr>" +
                    "<tr><td style='color:#1f3864'>Тёмно-синий текст</td><td bgcolor='#ffff99'>95</td></tr></table><p><font color='#800000'>Тёмно-красный</font></p>" +
                    "<span id='qa-icon' class='qa-icon'></span></td></tr></table>", SitePolicy.Origin + "/student/qa-theme");
                await WaitQaPresentationAsync();
                const string site = "(()=>{const l=c=>{const m=(c.match(/\\d+(\\.\\d+)?/g)||[]).map(Number);const v=m.slice(0,3).map(x=>{x/=255;return x<=0.04045?x/12.92:Math.pow((x+0.055)/1.055,2.4)});return .2126*v[0]+.7152*v[1]+.0722*v[2]};" +
                    "const k=(a,b)=>(Math.max(a,b)+.05)/(Math.min(a,b)+.05);const bg=e=>{for(;e;e=e.parentElement){const c=getComputedStyle(e).backgroundColor;if(c&&!/rgba\\(.*, 0\\)$/.test(c)&&c!=='transparent')return c}return getComputedStyle(document.documentElement).backgroundColor};" +
                    "const r=document.documentElement,cells=[...document.querySelectorAll('td,th,font,p')].filter(e=>e.textContent.trim());" +
                    "const worst=Math.min(...cells.map(e=>k(l(getComputedStyle(e).color),l(bg(e)))));return r.classList.contains('univer-app-dark')+'|'+l(getComputedStyle(document.body).backgroundColor).toFixed(3)+'|'+worst.toFixed(2)})()";
                var siteState = (await AutomationController.EvaluateAsync(main, site) ?? "").Split('|');
                QaTrace("qa-theme-site " + string.Join("|", siteState));
                Check(siteState.Length == 3 && siteState[0] == "true" && double.Parse(siteState[1], System.Globalization.CultureInfo.InvariantCulture) < 0.03 &&
                    double.Parse(siteState[2], System.Globalization.CultureInfo.InvariantCulture) >= 4.5, "the dark reading layout keeps the site's own colours readable (4.5:1)", results);
                await RestoreQaWindowAsync();
                var iconArea = (await AutomationController.EvaluateAsync(main, "(()=>{const r=document.getElementById('qa-icon').getBoundingClientRect(),k=devicePixelRatio;return [r.left*k,r.top*k,r.width*k,r.height*k].map(Math.round).join(',')})()") ?? "")
                    .Split(',').Select(x => { int v; return int.TryParse(x, out v) ? v : 0; }).ToArray();
                byte[] sitePng; using (var client = main.GetDevToolsClient()) sitePng = (await client.Page.CaptureScreenshotAsync()).Data;
                File.WriteAllBytes(Path.Combine(qa, "theme-dark-site.png"), sitePng);
                double iconLight = 0;
                if (iconArea.Length == 4 && iconArea[2] > 0 && iconArea[3] > 0)
                    using (var stream = new MemoryStream(sitePng)) using (var shot = new Bitmap(stream)) iconLight = LightShare(shot, new Rectangle(iconArea[0], iconArea[1], iconArea[2], iconArea[3]));
                QaTrace("qa-theme-icon " + string.Join(",", iconArea) + " light " + iconLight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                Check(iconLight > 0.08, "an app icon drawn through an SVG mask stays visible in the dark reading layout", results);
                // A tick still running from the page load skips this one; the view applies on the next tick.
                prefs.Readable = false; var original = "";
                for (var until = DateTime.UtcNow.AddSeconds(5); DateTime.UtcNow < until && original != "original"; await Task.Delay(100))
                {
                    await automation.TickAsync(main, credentials, prefs, false);
                    original = await AutomationController.EvaluateAsync(main, "!document.documentElement.classList.contains('univer-app-dark')&&!document.querySelector('[id^=\"univer-app-dark-\"]')&&getComputedStyle(document.querySelector('td[style]')).color==='rgb(31, 56, 100)'?'original':''");
                }
                if (original != "original") QaTrace("qa-theme-original " + await AutomationController.EvaluateAsync(main, "document.documentElement.className+'|'+[...document.querySelectorAll('[id^=\"univer-app-dark-\"]')].map(n=>n.id).join(',')+'|'+getComputedStyle(document.querySelector('td[style]')).color"));
                Check(original == "original", "Original layout keeps the site's own colours in the dark theme", results);
                prefs.Readable = true; await automation.TickAsync(main, credentials, prefs, false);
                // "As in Windows" follows the app mode Windows reports.
                qaFollowsWindows = true; prefs.Theme = ThemeChoice.System; ApplyTheme();
                Check(Theme.Dark == Theme.SystemDark(), "Same as Windows follows the Windows app mode", results);
                qaFollowsWindows = false;
            }
            finally
            {
                qaFollowsWindows = false; prefs.Theme = saved; prefs.Readable = readable; ApplyTheme(false);
                downloadsView.ClosePreview(); HideDownloads(); downloadsView.SetItems(history.Items);
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls) { yield return child; foreach (var inner in Descendants(child)) yield return inner; }
        }
        // One pixel of a control as it draws itself.
        private static bool PixelQa(Control control, int x, int y, Color expected, string what)
        {
            if (control == null || control.Width <= x || control.Height <= y) { QaTrace("qa-pixel " + what + " no control"); return false; }
            using (var bitmap = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.ToArgb() == expected.ToArgb()) return true;
                QaTrace("qa-pixel " + what + " " + pixel.R + "," + pixel.G + "," + pixel.B + " expected " + expected.R + "," + expected.G + "," + expected.B);
                return false;
            }
        }
        private static double LightShare(Bitmap shot, Rectangle area)
        {
            area.Intersect(new Rectangle(Point.Empty, shot.Size)); if (area.Width <= 0 || area.Height <= 0) return 0;
            int light = 0, total = 0;
            for (var y = area.Top; y < area.Bottom; y++)
                for (var x = area.Left; x < area.Right; x++)
                { var c = shot.GetPixel(x, y); total++; if (c.R + c.G + c.B > 450) light++; }
            return light / (double)total;
        }
        // Labels, check boxes and text boxes against the colour behind them.
        private static bool ReadableQa(Control root, string what)
        {
            var failures = new List<string>();
            Action<Control> visit = null;
            visit = control =>
            {
                if (!control.Visible) return;
                if ((control is Label || control is CheckBox || control is TextBox) && !string.IsNullOrWhiteSpace(control.Text))
                {
                    var back = control.BackColor; for (var parent = control.Parent; back.A < 255 && parent != null; parent = parent.Parent) back = parent.BackColor;
                    var ratio = PreviewTheme.Contrast(PreviewTheme.Luminance(control.ForeColor.R, control.ForeColor.G, control.ForeColor.B), PreviewTheme.Luminance(back.R, back.G, back.B));
                    if (ratio < 4.5) failures.Add(control.GetType().Name + " '" + control.Text.Substring(0, Math.Min(30, control.Text.Length)) + "' " + ratio.ToString("0.0"));
                }
                foreach (Control child in control.Controls) visit(child);
            };
            visit(root);
            if (failures.Count > 0) QaTrace("qa-contrast " + what + ": " + string.Join("; ", failures));
            return failures.Count == 0;
        }
    }
}
