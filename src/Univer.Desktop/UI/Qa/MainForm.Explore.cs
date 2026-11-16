#if QA_REVIEW
using System;
using System.IO;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // --review-explore runs steps from Review\explore.txt, one per line: goto <route>, dblclick|select <row text>, back,
        // click <exact text>, capture <name>, shot <name>, probe <script file in Review> (saves what the script returns, for layout
        // measurements). Form submits are never clicked; the session is the one already signed in.
        private readonly System.Collections.Generic.Dictionary<string, string> exploreIds = new System.Collections.Generic.Dictionary<string, string>();

        private async Task ExploreReviewAsync(string root, string output, string report)
        {
            var outline = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "review-outline.js"));
            var step = 0; exploreIds.Clear();
            foreach (var raw in File.ReadAllLines(Path.Combine(root, "explore.txt")))
            {
                var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var space = line.IndexOf(' '); var verb = space < 0 ? line : line.Substring(0, space); var argument = space < 0 ? "" : line.Substring(space + 1).Trim();
                var result = "ok"; step++;
                if (verb == "goto")
                {
                    var url = SitePolicy.Origin + argument;
                    if (ReviewRoute(url)) main.Load(url); else result = "refused";
                }
                else if (verb == "wait")
                {
                    int milliseconds; await Task.Delay(int.TryParse(argument, out milliseconds) ? Math.Min(milliseconds, 30000) : 1000);
                }
                // size <width> <height>: the window's outer size, for narrow-window checks.
                else if (verb == "size")
                {
                    var parts = argument.Split(' '); int wide, tall;
                    if (parts.Length == 2 && int.TryParse(parts[0], out wide) && int.TryParse(parts[1], out tall))
                    { WindowState = System.Windows.Forms.FormWindowState.Normal; Size = new System.Drawing.Size(wide, tall); await Task.Delay(1200); result = "client " + ClientSize.Width + "x" + ClientSize.Height; }
                    else result = "refused";
                }
                // theme dark|light: the window and the shown page in that theme for this run only; preferences stay as saved.
                else if (verb == "theme")
                {
                    ApplyTheme(argument == "dark"); await Task.Delay(900); result = "dark " + Theme.Dark;
                }
                else if (verb == "back")
                {
                    result = await AutomationController.EvaluateAsync(main, "history.back();'back'");
                }
                else if (verb == "routes")
                {
                    // Routes and script URL templates with every number replaced by a letter shared across the whole run, so
                    // the report shows which page passes which id to the next without recording the ids themselves.
                    var found = await AutomationController.EvaluateAsync(main, "(function(){var out=[location.pathname];Array.from(document.querySelectorAll('#l4 a[href^=\"/student/discussion\"],#l4 form[action]')).forEach(function(a){out.push((a.tagName==='FORM'?'form ':'a ')+(a.getAttribute('href')||a.getAttribute('action')));});" +
                        "Array.from(document.querySelectorAll('#l4 tr.link')).slice(0,3).forEach(function(r){out.push('row '+r.id);});" +
                        "Array.from(document.scripts).forEach(function(s){(s.text.match(/[\"']\\/student\\/discussion\\/[^\"']*[\"'][^;\\n]*/g)||[]).forEach(function(m){out.push('js '+m.slice(0,200));});});return out.join('\\n');})()") ?? "";
                    var masked = System.Text.RegularExpressions.Regex.Replace(found, @"\d+", m =>
                    {
                        string letter; if (!exploreIds.TryGetValue(m.Value, out letter)) { letter = "<" + (char)('A' + exploreIds.Count % 26) + (exploreIds.Count >= 26 ? (exploreIds.Count / 26).ToString() : "") + ">"; exploreIds[m.Value] = letter; }
                        return letter;
                    });
                    File.AppendAllText(Path.Combine(output, "routes.txt"), "### " + step.ToString("D3") + "\n" + masked + "\n\n");
                    result = "routes " + masked.Split('\n').Length;
                }
                else if (verb == "inspect")
                {
                    // Site handler code and a markup skeleton (digits masked, long text replaced by its length, no field values).
                    var dump = await AutomationController.EvaluateAsync(main, "(function(){var jq=window.jQuery,out=[],seen={};var mask=function(v){return String(v||'').replace(/\\d+/g,'{id}');};" +
                        "var handlers=function(e,name){var d;try{d=jq&&(jq._data?jq._data(e,'events'):jq.data(e,'events'));}catch(_){}if(!d)return;Object.keys(d).forEach(function(k){if(/^mouse/.test(k))return;var hs=d[k];Object.keys(hs).forEach(function(g){var h=hs[g];var f=typeof h==='function'?h:h&&h.handler;if(!f)return;var s=String(f);if(seen[s])return;seen[s]=1;out.push(name+' '+k+': '+mask(s).slice(0,4000));});});};" +
                        "handlers(document,'document');Array.from(document.querySelectorAll('#l4 *')).forEach(function(e){handlers(e,e.tagName+(e.id?'#'+mask(e.id):'')+(e.className?'.'+String(e.className).split(/\\s+/)[0]:''));});" +
                        "Array.from(document.scripts).forEach(function(s){if(!s.src&&s.text.trim())out.push('SCRIPT: '+mask(s.text).slice(0,12000));else if(s.src)out.push('SCRIPT-SRC: '+mask(s.src));});" +
                        "var skel=function(n,d){if(n.nodeType===3){var t=n.nodeValue.replace(/\\s+/g,' ').trim();return !t?'':t.length<=40?mask(t):'[text '+t.length+']';}if(n.nodeType!==1||/^(SCRIPT|STYLE)$/.test(n.tagName))return '';" +
                        "var a=Array.from(n.attributes).filter(function(x){return !/^(style|value|data-univer.*)$/.test(x.name);}).map(function(x){return ' '+x.name+'=\"'+mask(x.value).slice(0,80)+'\"';}).join('');" +
                        "var inner=d>0?Array.from(n.childNodes).map(function(c){return skel(c,d-1);}).join(''):'…';return '<'+n.tagName.toLowerCase()+a+'>'+inner+'</'+n.tagName.toLowerCase()+'>';};" +
                        "var root=document.querySelector('#l4 td.center')||document.querySelector('#l4');out.push('SKELETON: '+(root?skel(root,40):'none'));return out.join('\\n\\n----\\n\\n');})()");
                    File.WriteAllText(Path.Combine(output, step.ToString("D3") + "-inspect.txt"), dump ?? "");
                    result = dump == null ? "empty" : "saved " + dump.Length;
                }
                // skeleton <name>: the page as shown, then the loading placeholder drawn from this page's measured layout, for
                // comparing the two side by side.
                else if (verb == "skeleton")
                {
                    var shotName = step.ToString("D3") + "-" + System.Text.RegularExpressions.Regex.Replace(argument, @"[^\w-]", "_");
                    var shape = PageShape.Parse(await AutomationController.EvaluateAsync(main, File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "page-shape.js"))));
                    await SaveReviewCaptureAsync(Path.Combine(output, shotName + "-page.png"));
                    if (shape == null) result = "no shape";
                    else
                    {
                        loading.SetRoute(main.Address); loading.Captured = shape;
                        SaveBitmap(loading, Path.Combine(output, shotName + "-skeleton.png"));
                        result = "items " + shape.Items.Count + " css " + shape.Outer + "x" + shape.Height + " view " + loading.Width + "x" + loading.Height;
                        loading.Captured = null;
                    }
                }
                // link <address part>: the material row whose address contains it, clicked through the app's own download path,
                // only when the page marks the row as a link (so no file is saved); the user's browser is replaced by a note of
                // the host that would open.
                else if (verb == "link")
                {
                    var href = await AutomationController.EvaluateAsync(main, "(function(){var a=Array.from(document.querySelectorAll('#l4 tr.file a.downLoad')).find(function(a){return a.getAttribute('href').indexOf(" +
                        new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(argument) + ")>=0;});return a&&a.closest('tr').querySelector('td[data-univer-kind=link]')?a.href:'';})()");
                    if (string.IsNullOrEmpty(href) || !SitePolicy.ProtectedDownload(href)) result = "refused (no link row)";
                    else
                    {
                        var before = history == null ? -1 : history.Items.Count; string opened = null;
                        outsideProbe = target => { opened = SitePolicy.Host(target); return true; };
                        try { await DownloadAsync(href); }
                        finally { outsideProbe = null; }
                        result = "opened " + (opened ?? "nothing") + " externalOpened " + messages.Shows("externalOpened") + " records " + before + "->" + (history == null ? -1 : history.Items.Count);
                    }
                }
                // web <address part>: the same material row opened as the student would, so a link lands in the app's own browser
                // window; webshot <name>: that window as shown (its bar, then the page through DevTools); webback: its Back;
                // webclose: its Close; handoff: the Moodle hand-off mechanism exercised against example.com with a dummy field.
                else if (verb == "web")
                {
                    var href = await AutomationController.EvaluateAsync(main, "(function(){var a=Array.from(document.querySelectorAll('#l4 tr.file a.downLoad')).find(function(a){return a.getAttribute('href').indexOf(" +
                        new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(argument) + ")>=0;});return a&&a.closest('tr').querySelector('td[data-univer-kind=link]')?a.href:'';})()");
                    if (string.IsNullOrEmpty(href) || !SitePolicy.ProtectedDownload(href)) result = "refused (no link row)";
                    else { await DownloadAsync(href); result = await WebSettledAsync(); }
                }
                else if (verb == "webshot")
                {
                    if (!WebShown || webPanel.Browser == null) result = "no window";
                    else { await SaveWebCaptureAsync(Path.Combine(output, step.ToString("D3") + "-" + System.Text.RegularExpressions.Regex.Replace(argument, @"[^\w-]", "_") + ".png")); result = "saved"; }
                }
                else if (verb == "webback") { if (WebShown) webPanel.GoBack(); await Task.Delay(2500); result = WebShown ? await WebSettledAsync() : "closed, main visible " + (main != null && main.Visible); }
                else if (verb == "webclose") { CloseWeb(); result = "shown " + WebShown + " browser " + (webPanel.Browser != null); }
                else if (verb == "handoff")
                {
                    var handed = HandOffInside("https://example.com/", "POST", new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> { new System.Collections.Generic.KeyValuePair<string, string>("probe", "1") });
                    result = "handed " + handed + " " + (handed ? await WebSettledAsync() : "");
                }
                // placeholder <route>: the loading placeholder the next navigation to that page would show, from what this run
                // has measured so far.
                else if (verb == "placeholder")
                {
                    ShowRoute(SitePolicy.Origin + argument);
                    SaveBitmap(loading, Path.Combine(output, step.ToString("D3") + "-" + System.Text.RegularExpressions.Regex.Replace(argument, @"[^\w-]", "_") + "-placeholder.png"));
                    result = (loading.Captured == null ? "drawn" : "captured " + loading.Captured.Items.Count) + " known " + pageShapes.Count;
                    loading.Captured = null;
                }
                // shot saves the window as it is, without the Original round trip that capture makes.
                else if (verb == "shot") { await SaveReviewCaptureAsync(Path.Combine(output, step.ToString("D3") + "-" + System.Text.RegularExpressions.Regex.Replace(argument, @"[^\w-]", "_") + ".png")); result = "saved"; }
                // hop <material address>: one session GET of a UMKD file address, redirects not followed; only the status and
                // the target's scheme and host are kept, never the path, the body or a cookie.
                else if (verb == "hop")
                {
                    var url = SitePolicy.Origin + argument;
                    if (!SitePolicy.ProtectedDownload(url)) result = "refused";
                    else using (var http = new SessionHttp(context, null, main.Address, TimeSpan.FromSeconds(20)))
                    using (var response = await http.GetAsync(url, System.Threading.CancellationToken.None))
                    {
                        var location = response.Headers.Location; var target = location == null ? null : new Uri(new Uri(url), location);
                        result = (int)response.StatusCode + " " + (target == null ? "-" : target.Scheme + "://" + target.Host + (target.IsDefaultPort ? "" : ":" + target.Port) + " path " + target.AbsolutePath.Length + " query " + target.Query.Length);
                    }
                }
                // peek <site path>: one session GET of a site page, redirects not followed; only the status, the content type,
                // the redirect target's scheme and host, and the hosts of the page's forms and other-site addresses are kept,
                // never a path, a query, a field or a cookie.
                else if (verb == "peek")
                {
                    var url = SitePolicy.Origin + argument;
                    if (!ReviewRoute(url)) result = "refused";
                    else using (var http = new SessionHttp(context, null, main.Address, TimeSpan.FromSeconds(20)))
                    using (var response = await http.GetAsync(url, System.Threading.CancellationToken.None))
                    {
                        var location = response.Headers.Location; var target = location == null ? null : new Uri(new Uri(url), location);
                        var hosts = new System.Collections.Generic.SortedSet<string>(); var forms = new System.Collections.Generic.List<string>();
                        if (target == null && response.Content != null)
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            foreach (System.Text.RegularExpressions.Match form in System.Text.RegularExpressions.Regex.Matches(body, "<form[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            {
                                var action = System.Text.RegularExpressions.Regex.Match(form.Value, "action\\s*=\\s*[\"']([^\"']*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value; Uri resolved;
                                forms.Add((System.Text.RegularExpressions.Regex.IsMatch(form.Value, "method\\s*=\\s*[\"']?post", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "POST " : "GET ") +
                                    (Uri.TryCreate(new Uri(url), System.Net.WebUtility.HtmlDecode(action), out resolved) ? resolved.Scheme + "://" + resolved.Host : "?"));
                            }
                            foreach (System.Text.RegularExpressions.Match address in System.Text.RegularExpressions.Regex.Matches(body, "https?://([A-Za-z0-9.-]+)"))
                                if (!string.Equals(address.Groups[1].Value, "univer.kaznu.kz", StringComparison.OrdinalIgnoreCase)) hosts.Add(address.Value.ToLowerInvariant());
                        }
                        result = (int)response.StatusCode + " " + (response.Content?.Headers.ContentType?.MediaType ?? "-") + " to " +
                            (target == null ? "-" : target.Scheme + "://" + target.Host + (target.IsDefaultPort ? "" : ":" + target.Port) + " path " + target.AbsolutePath.Length + " query " + target.Query.Length) +
                            " forms [" + string.Join(", ", forms) + "] hosts [" + string.Join(", ", hosts) + "]";
                    }
                }
                // tap <link text>: a real mouse click (through Chromium's input, so it carries a user gesture) on the visible link
                // of "Все разделы" with that text; refused while links open in the user's browser, so nothing leaves the app.
                else if (verb == "tap")
                {
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var point = prefs.ExternalLinks ? null : await AutomationController.EvaluateAsync(main, "(function(text){var a=Array.from(document.querySelectorAll('#tools a[href]')).find(function(a){return a.textContent.replace(/\\s+/g,' ').trim()===text&&a.getClientRects().length;});" +
                        "if(!a)return '';a.scrollIntoView({block:'center'});var r=a.getBoundingClientRect();return Math.round(r.left+Math.min(24,r.width/2))+','+Math.round(r.top+r.height/2);})(" + serializer.Serialize(argument) + ")");
                    var xy = (point ?? "").Split(','); int x, y;
                    if (xy.Length != 2 || !int.TryParse(xy[0], out x) || !int.TryParse(xy[1], out y)) result = prefs.ExternalLinks ? "refused (links open outside)" : "missing";
                    else
                    {
                        var host = main.GetBrowser().GetHost();
                        host.SendMouseMoveEvent(x, y, false, CefEventFlags.None);
                        host.SendMouseClickEvent(x, y, MouseButtonType.Left, false, 1, CefEventFlags.None);
                        host.SendMouseClickEvent(x, y, MouseButtonType.Left, true, 1, CefEventFlags.None);
                        await Task.Delay(4000);
                        result = "tapped; window " + (WebShown ? SitePolicy.Host(webPanel.Browser?.Address) : "closed");
                    }
                }
                else if (verb == "probe") { var probe = Path.Combine(root, Path.GetFileName(argument)); var measured = File.Exists(probe) ? await AutomationController.EvaluateAsync(main, File.ReadAllText(probe)) : null; File.WriteAllText(Path.Combine(output, step.ToString("D3") + "-probe.txt"), measured ?? ""); result = measured == null ? "empty" : "saved " + measured.Length; }
                else if (verb == "dblclick" || verb == "select" || verb == "click")
                {
                    // select clicks a course row without the double click that opens it.
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    result = await AutomationController.EvaluateAsync(main, "(function(text,double,row){var fold=function(v){return (v||'').replace(/\\s+/g,' ').trim();};" +
                        "var nodes=row?Array.from(document.querySelectorAll('#l4 tr.link')).filter(function(r){return fold(r.textContent).indexOf(text)>=0;})" +
                        ":Array.from(document.querySelectorAll('#l4 a,#l4 button,#l4 input[type=button],#l4 span,#l4 div,#l4 td')).filter(function(e){return fold(e.value||e.textContent)===text&&!e.querySelector('a,button,input');});" +
                        "var node=nodes[0];if(!node)return 'missing '+nodes.length;" +
                        "if(node.matches('input[type=submit],button[type=submit],button:not([type])')&&node.form)return 'refused submit';" +
                        "node.scrollIntoView({block:'center'});node.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,cancelable:true,view:window}));" +
                        "node.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,cancelable:true,view:window}));node.click();" +
                        "if(double)node.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,cancelable:true,view:window}));return 'done '+node.tagName+' of '+nodes.length;})(" +
                        serializer.Serialize(argument) + "," + (verb == "dblclick" ? "true" : "false") + "," + (verb == "click" ? "false" : "true") + ")");
                }
                if (verb != "capture")
                {
                    await Task.Delay(700);
                    if (!await WaitReviewReadyAsync()) result += "; not ready";
                    await ApplyPresentationAsync(); await Task.Delay(400);
                    File.AppendAllText(report, step.ToString("D3") + " " + verb + " " + SafeReviewTitle(argument) + " -> " + result + " @ " + SafeReviewRoute(Active.Address) + "\n");
                    continue;
                }
                var name = step.ToString("D3") + "-" + System.Text.RegularExpressions.Regex.Replace(argument, @"[^\w-]", "_");
                prefs.Readable = false; await ApplyPresentationAsync(); await Task.Delay(300);
                var structure = await AutomationController.EvaluateAsync(Active, outline);
                if (structure != null) File.WriteAllText(Path.Combine(output, name + "-outline.json"), structure);
                prefs.Readable = true; await ApplyPresentationAsync(); await Task.Delay(400);
                await SaveReviewCaptureAsync(Path.Combine(output, name + "-wide.png"));
                var heightText = await AutomationController.EvaluateAsync(Active, "String(document.documentElement.scrollHeight)");
                int height; if (int.TryParse(heightText, out height) && height > 1500)
                    for (var part = 1; part <= Math.Min(5, height / 650); part++)
                    {
                        await AutomationController.EvaluateAsync(Active, "window.scrollTo(0," + (part * 650) + ");'part'"); await Task.Delay(120);
                        await SaveReviewCaptureAsync(Path.Combine(output, name + "-part-" + part.ToString("D2") + ".png"));
                    }
                await AutomationController.EvaluateAsync(Active, "window.scrollTo(0,0);'top'");
                File.AppendAllText(report, step.ToString("D3") + " capture " + name + " @ " + SafeReviewRoute(Active.Address) + " " + await ReviewGeometryAsync() + "\n");
            }
            File.AppendAllText(report, "DONE explore steps=" + step + "\n");
        }
        // The app's own browser window once its page has settled: the host and the window's state, never the path or the page.
        private async Task<string> WebSettledAsync()
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            await Task.Delay(800);
            while (DateTime.UtcNow < until && WebShown && (webPanel.Browser == null || webPanel.Browser.IsLoading || webPanel.StateKey == "webLoading")) await Task.Delay(250);
            if (!WebShown) return "window closed";
            Uri uri; var address = webPanel.Address;
            var place = Uri.TryCreate(address, UriKind.Absolute, out uri) ? uri.Scheme + "://" + uri.Host + " pathLength " + uri.AbsolutePath.Length : address;
            return "window " + place + " state '" + webPanel.StateKey + "' back " + webPanel.Browser.CanGoBack;
        }
        // The window's bar drawn by WinForms, the page under it from Chromium itself (a CEF window cannot be printed by WinForms).
        private async Task SaveWebCaptureAsync(string path)
        {
            byte[] page; using (var client = webPanel.Browser.GetDevToolsClient()) page = (await client.Page.CaptureScreenshotAsync()).Data;
            using (var picture = new System.Drawing.Bitmap(webPanel.Width, webPanel.Height))
            {
                webPanel.Browser.Visible = false;
                try { webPanel.DrawToBitmap(picture, new System.Drawing.Rectangle(System.Drawing.Point.Empty, webPanel.Size)); }
                finally { webPanel.Browser.Visible = true; }
                using (var stream = new MemoryStream(page)) using (var shot = System.Drawing.Image.FromStream(stream)) using (var g = System.Drawing.Graphics.FromImage(picture))
                    g.DrawImage(shot, 0, webPanel.BarHeight, webPanel.Width, webPanel.Height - webPanel.BarHeight);
                picture.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
#endif
