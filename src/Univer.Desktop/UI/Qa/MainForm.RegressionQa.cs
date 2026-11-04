using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private async Task CookieSyncQaAsync(List<string> results)
        {
            var manager = context.GetCookieManager(null);
            var expiry = DateTime.UtcNow.AddDays(5);
            foreach (var path in new[] { "/", "/student" })
                await manager.SetCookieAsync(SitePolicy.Origin, new CefSharp.Cookie { Name="qa_response_cookie", Value="synthetic", Path=path, Secure=true, HttpOnly=true, Expires=expiry });
            using (var http = new SessionHttp(context, "QA", SitePolicy.Origin, TimeSpan.FromSeconds(5)))
            {
                // Only the transport is replaced; actual CEF cookies and response synchronization are exercised.
                var original = http.Client;
                typeof(SessionHttp).GetProperty("Client").SetValue(http, new HttpClient(new CookieQaTransport())); original.Dispose();
                using (var response = await http.GetAsync(SitePolicy.Origin + "/student/qa-response", CancellationToken.None)) { }
                var after = await manager.VisitUrlCookiesAsync(SitePolicy.Origin + "/student/qa-response", true);
                Check(after.FindAll(c => c.Name == "qa_response_cookie").Count == 2 && after.FindAll(c => c.Name == "qa_response_cookie").TrueForAll(c => c.Expires > DateTime.UtcNow.AddDays(4)),
                    "unrelated response cookie preserves persistent cookies", results);
                using (var response = await http.GetAsync(SitePolicy.Origin + "/student/qa-response", CancellationToken.None)) { }
                after = await manager.VisitUrlCookiesAsync(SitePolicy.Origin + "/student/qa-response", true);
                Check(after.FindAll(c => c.Name == "qa_response_cookie").Count == 1 && after.Exists(c => c.Name == "qa_response_cookie" && c.Path == "/student"),
                    "response expiry removes only the matching cookie path in Chromium", results);
                using (var response = await http.GetAsync(SitePolicy.Origin + "/student/qa-response", CancellationToken.None)) { }
                after = await manager.VisitUrlCookiesAsync(SitePolicy.Origin + "/student/qa-response", true);
                Check(!after.Exists(c => c.Name == "qa_response_cookie"), "response Max-Age deletion reaches Chromium", results);
            }
        }
        // Real Chromium download events: a file answering a submitted form and a file built by the page are both explained,
        // and neither becomes a native GET.
        private async Task FormDownloadQaAsync(List<string> results)
        {
            var export = SitePolicy.Origin + "/student/qa-form-export";
            main.RegisterResourceHandler(export, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("synthetic,export\n")), "application/octet-stream", true);
            main.LoadHtml("<form id='qa' method='post' action='/student/qa-form-export'><input name='synthetic' value='1'><button>Export</button></form><p>Form export fixture</p>", SitePolicy.Origin + "/student/qa-form");
            await WaitQaPresentationAsync();
            var before = history.Items.Count;
            await AutomationController.EvaluateAsync(main, "document.getElementById('qa').submit();'ok'");
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!messages.Shows("downloadFromForm") && DateTime.UtcNow < until) await Task.Delay(100);
            await Task.Delay(300);
            Check(messages.Shows("downloadFromForm") && !messages.Shows("downloading") && history.Items.Count == before, "a file answering a submitted form is explained and never fetched again", results);
            // A fresh page resets Chromium's limit on downloads started without a user gesture.
            main.LoadHtml("<p>Page-made file fixture</p>", SitePolicy.Origin + "/student/qa-blob");
            await WaitQaPresentationAsync();
            await AutomationController.EvaluateAsync(main, "var a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['synthetic'],{type:'text/plain'}));a.download='qa.txt';document.body.appendChild(a);a.click();'ok'");
            until = DateTime.UtcNow.AddSeconds(8);
            while (!messages.Shows("downloadUnsupported") && DateTime.UtcNow < until) await Task.Delay(100);
            Check(messages.Shows("downloadUnsupported") && history.Items.Count == before, "a file the page builds itself is explained instead of ignored", results);
            while (messages.Current != null && messages.Current.Action == MessageAction.None && (messages.Current.Key == "downloadFromForm" || messages.Current.Key == "downloadUnsupported")) messages.Dismiss();
            RenderFeedback();
        }
        // Cost of the reading-mode adapter while a site animation rewrites an inline style every frame on a large table page.
        // Each adapter pass disconnects its observer first and observes again last, which brackets the pass for timing.
        private async Task LayoutLoadQaAsync(List<string> results)
        {
            var rows = new System.Text.StringBuilder();
            for (var i = 0; i < 1500; i++) rows.Append("<tr><td>").Append(i).Append("</td><td>Дисциплина ").Append(i).Append("</td><td>").Append(i % 100).Append("</td></tr>");
            main.LoadHtml("<table class='inner'><tr><th>№</th><th>Дисциплина</th><th>Балл</th></tr>" + rows + "</table><div id='qa-ticker' style='position:relative;left:0'>QA</div>", SitePolicy.Origin + "/student/qa-load");
            await WaitQaPresentationAsync();
            var raw = await AutomationController.EvaluateAsync(main, "(function(){var s={runs:0,ms:0,at:0},o=MutationObserver.prototype.observe,d=MutationObserver.prototype.disconnect;" +
                "MutationObserver.prototype.disconnect=function(){s.at=performance.now();return d.apply(this,arguments);};" +
                "MutationObserver.prototype.observe=function(){if(s.at){s.runs++;s.ms+=performance.now()-s.at;s.at=0;}return o.apply(this,arguments);};" +
                "window.__qaLoad=s;var x=0,t=document.getElementById('qa-ticker');s.timer=setInterval(function(){t.style.left=(x++%100)+'px';},16);return 'ok';})()");
            await Task.Delay(2000);
            var load = await AutomationController.EvaluateAsync(main, "(function(){var s=window.__qaLoad;clearInterval(s.timer);return s.runs+'|'+s.ms.toFixed(1);})()") ?? "";
            QaTrace("qa-layout-load " + load);
            var parts = load.Split('|'); var runs = 0; var ms = 0.0;
            var measured = raw == "ok" && parts.Length == 2 && int.TryParse(parts[0], out runs) && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ms);
            // Before the style-only throttle: 123 passes and 1626 ms of 2000 ms (Modern x64), 78 passes and 1536 ms (Legacy x64).
            Check(measured && runs <= 20 && ms < 500, "a site style animation keeps the reading-mode adapter under a quarter of the page thread", results);
        }
        private sealed class CookieQaTransport : HttpMessageHandler
        {
            private int request;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Synthetic cookie fixture") };
                var headers = new[] { "qa_other=1; Path=/; Secure", "qa_response_cookie=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Secure; HttpOnly", "qa_response_cookie=; Path=/student; Max-Age=0; Secure; HttpOnly" };
                response.Headers.TryAddWithoutValidation("Set-Cookie", headers[request++]); return Task.FromResult(response);
            }
        }
        private async Task PreviewEscapeQaAsync(string extension, List<string> results)
        {
            var browser = downloadsView.ViewerForQa;
            var delivered = 0;
            Action<Keys> observe = key => { if (key == Keys.Escape) delivered++; };
            downloadsView.Shortcut += observe;
            try
            {
            browser.Focus(); browser.GetBrowser().GetHost().SetFocus(true);
            browser.GetBrowser().GetHost().SendKeyEvent(new KeyEvent { Type=KeyEventType.RawKeyDown, WindowsKeyCode=(int)Keys.Escape });
            browser.GetBrowser().GetHost().SendKeyEvent(new KeyEvent { Type=KeyEventType.KeyUp, WindowsKeyCode=(int)Keys.Escape });
            var until = DateTime.UtcNow.AddSeconds(3);
            while (downloadsView.Previewing && DateTime.UtcNow < until) await Task.Delay(50);
            if (downloadsView.Previewing) QaTrace("qa-preview-escape " + extension + " dispatch=" + delivered + " focused=" + browser.Focused + " busy=" + commandBusy);
            Check(!downloadsView.Previewing && DownloadsVisible, "CEF Escape returns " + extension + " to downloads", results);
            }
            finally { downloadsView.Shortcut -= observe; }
        }
        private void OtherDownloadsQa(List<string> results)
        {
            var chips = (FlowLayoutPanel)typeof(DownloadsView).GetField("chips", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(downloadsView);
            ((Button)chips.Controls[chips.Controls.Count - 1]).PerformClick();
            Check(downloadsView.RowsForQa == 1, "Other filter displays the TXT without a dedicated category", results);
            ((Button)chips.Controls[0]).PerformClick();
        }
        // Both PDF views: the bundled reader and Chromium's own viewer, which the reader hands a file it cannot open.
        private async Task PreviewShortcutsQaAsync(DownloadRecord pdf, List<string> results)
        {
            foreach (var chromium in new[] { false, true })
                foreach (var key in new[] { Keys.Control | Keys.J, Keys.Alt | Keys.Left })
                {
                    var view = chromium ? "Chromium's PDF viewer" : "the PDF reader";
                    CefSharp.WinForms.ChromiumWebBrowser browser;
                    if (chromium) browser = await ChromiumPdfQaAsync(pdf);
                    else { await OpenReaderQaAsync(pdf.Path, 1); browser = downloadsView.ViewerForQa; }
                    Check(!browser.IsLoading && Web.LocalFiles.Owns(browser.Address), "PDF reloads in " + view + " before testing " + key, results);
                    browser.GetBrowser().GetHost().SetFocus(true);
                    var modifiers = (key & Keys.Control) != 0 ? CefEventFlags.ControlDown : CefEventFlags.AltDown;
                    browser.GetBrowser().GetHost().SendKeyEvent(new KeyEvent { Type=KeyEventType.RawKeyDown, WindowsKeyCode=(int)(key & Keys.KeyCode), Modifiers=modifiers });
                    browser.GetBrowser().GetHost().SendKeyEvent(new KeyEvent { Type=KeyEventType.KeyUp, WindowsKeyCode=(int)(key & Keys.KeyCode), Modifiers=modifiers });
                    var until = DateTime.UtcNow.AddSeconds(3);
                    while (downloadsView.Previewing && DateTime.UtcNow < until) await Task.Delay(50);
                    Check(!downloadsView.Previewing && DownloadsVisible, "a shortcut in " + view + " returns to downloads exactly once: " + key, results);
                }
        }
        // In the PDF preview a handled press must not swallow a later release-only shortcut (the PDF guest path). Chromium drops
        // the release of a handled press until the next key-down, which is what the Shift press below provides.
        private async Task PdfReleaseQaAsync(DownloadRecord pdf, List<string> results)
        {
            // Ctrl+J is not a PDF viewer key, so it reaches the app whether focus is in the viewer frame or its PDF guest.
            var delivered = 0;
            Action<Keys> observe = key => { if (key == (Keys.Control | Keys.J)) delivered++; };
            downloadsView.Shortcut += observe;
            try
            {
                for (var step = 0; step < 2; step++)
                {
                    var browser = await ChromiumPdfQaAsync(pdf);
                    var host = browser.GetBrowser().GetHost(); host.SetFocus(true); await Task.Delay(300);
                    if (step == 0)
                    {
                        // Ctrl let go before J: one command, whichever path the key took.
                        host.SendKeyEvent(new KeyEvent { Type=KeyEventType.RawKeyDown, WindowsKeyCode=(int)Keys.J, Modifiers=CefEventFlags.ControlDown });
                        host.SendKeyEvent(new KeyEvent { Type=KeyEventType.KeyUp, WindowsKeyCode=(int)Keys.J });
                    }
                    else
                    {
                        // An ordinary key-down ends Chromium's suppression of releases, as the next real key press does;
                        // then the release-only Ctrl+J of the PDF guest path must not be swallowed by the earlier press.
                        host.SendKeyEvent(new KeyEvent { Type=KeyEventType.RawKeyDown, WindowsKeyCode=(int)Keys.ShiftKey, Modifiers=CefEventFlags.ShiftDown });
                        host.SendKeyEvent(new KeyEvent { Type=KeyEventType.KeyUp, WindowsKeyCode=(int)Keys.ShiftKey });
                        host.SendKeyEvent(new KeyEvent { Type=KeyEventType.KeyUp, WindowsKeyCode=(int)Keys.J, Modifiers=CefEventFlags.ControlDown });
                    }
                    for (var wait = DateTime.UtcNow.AddSeconds(3); delivered <= step && DateTime.UtcNow < wait;) await Task.Delay(50);
                    await Task.Delay(500);
                    QaTrace("qa-pdf-release step=" + step + " delivered=" + delivered + " previewing=" + downloadsView.Previewing);
                    Check(delivered == step + 1 && !downloadsView.Previewing, step == 0 ? "a handled PDF shortcut fires once when Ctrl is let go first" :
                        "a later release-only PDF shortcut is not swallowed by an earlier press", results);
                }
            }
            finally { downloadsView.Shortcut -= observe; downloadsView.ClosePreview(); }
        }
        // A Windows (Office) preview handler runs in prevhost.exe; the app's Escape still closes it.
        private async Task OfficeKeysQaAsync(string folder, List<string> results)
        {
            var rtf = Path.Combine(folder, "Office keys QA.rtf");
            File.WriteAllText(rtf, "{\\rtf1\\ansi Office keys QA\\par}");
            if (!PreviewHost.HandlerFor(rtf).HasValue) { QaTrace("qa-office-keys no RTF preview handler"); return; }
            await downloadsView.PreviewAsync(new DownloadRecord { FileName = Path.GetFileName(rtf), Path = rtf, State = DownloadState.Completed });
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!downloadsView.OfficeForQa && DateTime.UtcNow < until) await Task.Delay(100);
            QaTrace("qa-office-keys office=" + downloadsView.OfficeForQa);
            var escape = new Message(); ProcessCmdKey(ref escape, Keys.Escape);
            Check(!downloadsView.Previewing && DownloadsVisible, "Escape closes a Windows Office preview", results);
        }
        private async Task PreviewCancellationQaAsync(string folder, List<string> results)
        {
            var path=Path.Combine(folder,"cancel-preview.txt"); using(var stream=File.Create(path)) stream.SetLength(32L*1024*1024);
            var pending=downloadsView.PreviewAsync(new DownloadRecord { FileName=Path.GetFileName(path), Path=path, State=DownloadState.Completed });
            downloadsView.ClosePreview(); await pending;
            Check(!downloadsView.Previewing && downloadsView.PreviewName.Length==0, "closing a preparing file cannot reopen stale preview", results);
        }
        private async Task PreviewLimitsQaAsync(string folder, string qa, List<string> results)
        {
            var path = Path.Combine(folder, "Deep QA.docx");
            var body = new System.Text.StringBuilder();
            for (var i = 0; i < 1000; i++) body.Append("<w:sdt><w:sdtContent>");
            body.Append("<w:p><w:r><w:t>Nested fixture</w:t></w:r></w:p>");
            for (var i = 0; i < 1000; i++) body.Append("</w:sdtContent></w:sdt>");
            SyntheticDocx(path, body.ToString());
            await downloadsView.PreviewAsync(new DownloadRecord { FileName=Path.GetFileName(path), Path=path, State=DownloadState.Completed });
            Check(downloadsView.Previewing && downloadsView.MessageForQa == Strings.Get("previewLimit", prefs.Language) &&
                !downloadsView.OfficeForQa && (downloadsView.ViewerForQa == null || !downloadsView.ViewerForQa.Visible), "unsafe DOCX shows a readable limit message and hides old content", results);
            SaveDownloadsBitmap(Path.Combine(qa, "downloads-preview-limit.png"));
            downloadsView.ClosePreview();
            Check(!downloadsView.Previewing && DownloadsVisible, "a rejected DOCX returns to the working downloads list", results);
        }
    }
}
