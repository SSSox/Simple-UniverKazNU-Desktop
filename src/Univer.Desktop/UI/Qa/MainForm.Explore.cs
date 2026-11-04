#if QA_REVIEW
using System;
using System.IO;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // --review-explore runs steps from Review\explore.txt, one per line: goto <route>, dblclick|select <row text>, back,
        // click <exact text>, capture <name>. Form submits are never clicked; the session is the one already signed in.
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
    }
}
#endif
