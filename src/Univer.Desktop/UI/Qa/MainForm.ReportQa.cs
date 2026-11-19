using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // Score bands and the grade summary in real Chromium, on a synthetic journal: the band colours on the page, the sheet,
    // its four buttons (the first pressed with DevTools input, as the student would), the files' size at 100% and 200%,
    // printing the sheet alone and Original. Files go to a QA folder, the clipboard and the print dialog are stood in for.
    internal sealed partial class MainForm
    {
        private const string ReportQaJournal = "<table class='mt'><tr><td class='ct'>Журнал посещений и успеваемости</td></tr>";
        // Each chip: its band, background luminance ×1000, and the WCAG contrast of its number and of its note.
        private const string ReportQaContrast = "(function(){function L(c){var m=c.match(/\\d+(\\.\\d+)?/g).map(Number).slice(0,3).map(function(v){v/=255;return v<=.04045?v/12.92:Math.pow((v+.055)/1.055,2.4)});return .2126*m[0]+.7152*m[1]+.0722*m[2]}" +
                    "function k(a,b){var x=L(a),y=L(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05)}var c=Array.from(document.querySelectorAll('.univer-app-attendance-overview .univer-app-overview-score'));" +
                    "return c.map(function(n){var bg=getComputedStyle(n).backgroundColor;return n.dataset.univerBand+':'+Math.round(L(bg)*1000)+':'+k(getComputedStyle(n.querySelector('.univer-app-overview-score-value')).color,bg).toFixed(1)+':'+k(getComputedStyle(n.querySelector('.univer-app-overview-score-label')).color,bg).toFixed(1)}).join('|')})()";
        private const string ReportQaHost = "document.getElementById('univer-app-report')";
        // The goal level and the absences, on the page (both themes, wide and narrow) and on the sheet and its picture.
        // The goal is set back to none at the end: the long journal below is checked without one.
        private async Task ReportQaGoalAsync(string qa, string folder, List<string> results)
        {
            var absences = await FixesEvalAsync("(function(){var a=document.querySelectorAll('.univer-app-attendance-absences');return a.length+'|'+(a[0]?a[0].textContent:'')})()");
            QaTrace("report-absences " + absences);
            Check(absences == "1|Пропуски2", "a course card counts the journal's «н» marks, a course without one shows none (" + absences + ")", results);
            await FixesEvalAsync("document.querySelector('.univer-app-attendance-actions .univer-app-goal button[data-goal=top]').click();'goal'"); await Task.Delay(300);
            // The goal's buttons and the summary's button share the filter row: none overlaps another or runs out of the window.
            const string row = "(function(){var b=Array.from(document.querySelectorAll('.univer-app-attendance-filter-row button')).filter(function(n){return n.offsetWidth}).map(function(n){return n.getBoundingClientRect()});" +
                "var clash=b.some(function(x,i){return b.some(function(y,j){return i<j&&x.left<y.right-1&&y.left<x.right-1&&x.top<y.bottom-1&&y.top<x.bottom-1})});" +
                "var out=b.some(function(x){return x.right>innerWidth||x.left<0});var n=document.querySelector('.univer-app-score-total [data-univer-left]');var g=document.querySelector('.univer-app-goal button[aria-pressed=true]');" +
                "return b.length+'|'+clash+'|'+out+'|'+(document.documentElement.scrollWidth<=innerWidth+2)+'|'+(n?n.textContent+'|'+getComputedStyle(n).backgroundColor:'none')+'|'+(g?g.textContent+':'+Math.round(g.getBoundingClientRect().height):'')})()";
            var wide = await FixesEvalAsync(row); QaTrace("report-goal-wide " + wide);
            Check(wide.StartsWith("9|false|false|true|ещё 27 до 90|rgb(", StringComparison.Ordinal) && wide.EndsWith("|Отлично:40", StringComparison.Ordinal), "the goal switch sits beside the summary button and the totals count to it, highlighted (" + wide + ")", results);
            await FixesEvalAsync("scrollTo(0,0);'ok'"); await Task.Delay(150);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-goal.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await FixesEvalAsync("document.querySelector('.univer-app-score-total').scrollIntoView({block:'center'});'ok'"); await Task.Delay(200);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-goal-total.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            // Kazakh and English say what is left in longer words: the notes of one totals row neither overlap nor leave the card.
            const string notes = "(function(){var bad=0,n=0;Array.from(document.querySelectorAll('.univer-app-score-total')).forEach(function(row){var c=row.closest('td').getBoundingClientRect();" +
                "var b=Array.from(row.querySelectorAll('.univer-app-score-next')).map(function(x){return x.getBoundingClientRect()});n+=b.length;" +
                "b.forEach(function(x,i){if(x.left<c.left-1||x.right>c.right+1)bad++;b.forEach(function(y,j){if(i<j&&x.left<y.right-1&&y.left<x.right-1&&x.top<y.bottom-1&&y.top<x.bottom-1)bad++})})});" +
                "return n+':'+bad+':'+(document.querySelector('.univer-app-score-next')||{}).textContent})()";
            await FixesEvalAsync("scrollTo(0,0);'ok'");
            Width = 900; await Task.Delay(300);
            var narrow = await FixesEvalAsync(row); QaTrace("report-goal-narrow " + narrow);
            Check(narrow.StartsWith("9|false|false|true|", StringComparison.Ordinal), "in a narrow window the goal switch wraps without covering anything (" + narrow + ")", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-goal-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Width = 1360; await Task.Delay(200);
            prefs.Theme = ThemeChoice.Dark; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(700);
            const string plate = "(function(){function L(c){var m=c.match(/\\d+(\\.\\d+)?/g).map(Number).slice(0,3).map(function(v){v/=255;return v<=.04045?v/12.92:Math.pow((v+.055)/1.055,2.4)});return .2126*m[0]+.7152*m[1]+.0722*m[2]}" +
                "return Array.from(document.querySelectorAll('.univer-app-score-total [data-univer-left]')).map(function(n){var s=getComputedStyle(n),x=L(s.color),y=L(s.backgroundColor);return ((Math.max(x,y)+.05)/(Math.min(x,y)+.05)).toFixed(1)+':'+Math.round(y*1000)}).join('|')})()";
            var dark = await FixesEvalAsync(plate); QaTrace("report-goal-dark " + dark);
            var darkParts = dark.Split('|').Select(x => x.Split(':')).Where(x => x.Length == 2).ToList();
            Check(darkParts.Count > 0 && darkParts.All(x => double.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture) >= 4.5 && double.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture) < 60), "in the dark theme the «left» plate is dark and keeps 4.5:1 (" + dark + ")", results);
            await FixesEvalAsync("document.querySelector('.univer-app-score-total').scrollIntoView({block:'center'});'ok'"); await Task.Delay(200);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-goal-dark.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            prefs.Theme = ThemeChoice.Light; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
            await FixesEvalAsync("scrollTo(0,0);document.querySelector('.univer-app-attendance-report').click();'open'"); await Task.Delay(300);
            var sheet = await FixesEvalAsync("(function(){var h=" + ReportQaHost + ",r=h.shadowRoot,a=r.querySelector('.aim'),g=r.querySelector('.goal-row');h.setAttribute('data-mode','capture');var off=getComputedStyle(g).display;h.removeAttribute('data-mode');" +
                "return (a?a.textContent:'')+'|'+r.querySelectorAll('.note .left').length+'|'+off+'|'+r.querySelectorAll('.missed').length+'|'+(r.querySelector('.goal-row button[aria-pressed=true]')||{}).textContent})()");
            QaTrace("report-goal-sheet " + sheet);
            Check(sheet.StartsWith("Цель: «Отлично» — от 90 баллов.", StringComparison.Ordinal) && sheet.EndsWith("|5|none|1|Отлично", StringComparison.Ordinal), "the sheet names the goal, highlights what is left, counts absences and keeps its switch off the picture (" + sheet + ")", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-goal-sheet.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            await ReportAsync(main, GradeReport.Png);
            var picture = Directory.GetFiles(folder, "*.png").FirstOrDefault();
            Check(picture != null && ReportQaWhole(picture, true), "the picture with a goal is one whole page (" + (picture == null ? "none" : ReportQaSize(picture)) + ")", results);
            if (picture != null) File.Copy(picture, Path.Combine(qa, "report-picture-goal.png"), true);
            await FixesEvalAsync("window.__univerReportV1.close();'closed'");
            // The notes get the Kazakh and the English wording page-common.js writes for the same numbers (a language switch
            // here would wait for the site's own language); the next render puts the Russian back.
            foreach (var lang in new[] { "kz", "en" })
            {
                var swap = lang == "kz" ? "function(t){var m=t.match(/ещё (\\S+) до (\\d+)/);return m?m[2]+({50:'-ге',70:'-ке',90:'-ға'}[m[2]]||'')+' дейін '+m[1]+' балл':t}" : "function(t){var m=t.match(/ещё (\\S+) до (\\d+)/);return m?m[1].replace(',','.')+(m[1]==='1'?' point':' points')+' to '+m[2]:t}";
                await FixesEvalAsync("(function(){var f=" + swap + ";document.querySelectorAll('.univer-app-score-next').forEach(function(n){n.textContent=f(n.textContent)});return 'ok'})()"); await Task.Delay(150);
                var spoken = await FixesEvalAsync(notes); QaTrace("report-goal-" + lang + " " + spoken);
                await FixesEvalAsync("document.querySelector('.univer-app-score-total').scrollIntoView({block:'center'});'ok'"); await Task.Delay(200);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-goal-" + lang + ".png"), (await client.Page.CaptureScreenshotAsync()).Data);
                Check(spoken.Split(':').Length == 3 && spoken.Split(':')[1] == "0" && spoken.Split(':')[0] != "0", "in " + lang + " what is left fits beside the other notes of its row (" + spoken + ")", results);
                await FixesEvalAsync("document.querySelector('.univer-app-attendance-actions .univer-app-goal button[data-goal=good]').click();document.querySelector('.univer-app-attendance-actions .univer-app-goal button[data-goal=top]').click();'ok'"); await Task.Delay(150);
            }
            await FixesEvalAsync("document.querySelector('.univer-app-attendance-actions .univer-app-goal button[data-goal=none]').click();'none'");
            foreach (var record in history.Items.Where(x => x.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList()) ForgetRecord(record.Id);
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            messages.Withdraw("downloadComplete");
        }
        private async Task ReportQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            var folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", "report-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            byte[] copied = null; var printed = 0; var before = zoomPercent;
            reportFolder = folder; reportClipboard = data => copied = data; reportPrint = () => printed++;
            try
            {
                await RestoreQaWindowAsync();
                prefs.Language = "ru"; prefs.Readable = true; await RefreshDocumentScriptsAsync();
                // The first course has two «н» in its journal: its card counts them.
                var absent = PolishCourse("История Казахстана", "63", "12").Replace("<td>2</td><td>3</td><td></td>", "<td>н</td><td>3</td><td>н</td>");
                var body = ReportQaJournal + absent + PolishCourse("Иностранный язык", "91", "45") + PolishCourse("Философия", "74", "50") + "</table>";
                SetPresentation(main, true); main.LoadHtml(PolishShell(body, false), SitePolicy.Origin + "/student/attendance/full/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
                await FixesEvalAsync("window.__univerMotion=false;'still'");
                // Bands on the page: each chip's band and its tint, low and good apart, and the words in its tooltip (the summary
                // chips are compact; the course totals below say it on the page).
                var bands = await FixesEvalAsync("(function(){var c=Array.from(document.querySelectorAll('.univer-app-attendance-overview .univer-app-overview-score'));return c.map(function(n){return (n.dataset.univerBand||'-')+'~'+getComputedStyle(n).backgroundColor+'~'+n.title}).join('|')})()");
                QaTrace("report-bands " + bands);
                var parts = bands.Split('|').Select(x => x.Split('~')).ToList();
                Check(parts.Count == 6 && parts.All(x => x.Length == 3) && string.Join(",", parts.Select(x => x[0])) == "fair,low,top,low,good,fair" && parts[0][1] != parts[1][1] && parts[1][1] != parts[4][1] && parts[2][2].StartsWith("Отлично", StringComparison.Ordinal) && parts[0][2].EndsWith("Ещё 7 до 70", StringComparison.Ordinal),
                    "score chips take their band's tint and say what is left to the next mark (" + bands + ")", results);
                var total = await FixesEvalAsync("(function(){var s=document.querySelector('.univer-app-score-total [data-univer-band]');if(!s)return '';var b=s.querySelector('.univer-app-score-bar'),f=getComputedStyle(b,'::after').backgroundColor;return s.dataset.univerBand+'|'+f+'|'+s.querySelector('.univer-app-score-next').textContent+'|'+Math.round(b.getBoundingClientRect().width)})()");
                QaTrace("report-total " + total);
                Check(total.StartsWith("fair|rgb(229, 184, 98)|ещё 7 до 70|72", StringComparison.Ordinal), "a course total shows its band's fill, the marks and what is left (" + total + ")", results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-bands.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                var light = await FixesEvalAsync(ReportQaContrast); QaTrace("report-light " + light);
                Check(light.Split('|').Select(x => x.Split(':')).Count(x => x.Length == 4 && double.Parse(x[2], System.Globalization.CultureInfo.InvariantCulture) >= 4.5 && double.Parse(x[3], System.Globalization.CultureInfo.InvariantCulture) >= 4.5) == 6, "every band's number and label keep 4.5:1 on its tint (" + light + ")", results);
                await FixesEvalAsync("document.querySelector('.univer-app-score-total').scrollIntoView({block:'center'});'ok'"); await Task.Delay(200);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-total.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await FixesEvalAsync("scrollTo(0,0);'ok'");
                // A narrow window: the filter row wraps, nothing runs past the page, the button stays whole.
                Width = 900; await Task.Delay(300);
                var narrow = await FixesEvalAsync("(function(){var b=document.querySelector('.univer-app-attendance-report').getBoundingClientRect();return (document.documentElement.scrollWidth<=innerWidth+2)+'|'+(b.right<=innerWidth&&b.height>=36)})()");
                Check(narrow == "true|true", "in a narrow window the chips and the summary button fit (" + narrow + ")", results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-bands-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                Width = 1360; await Task.Delay(200);
                // Dark theme: the tints turn dark and the ink light, still apart from each other.
                prefs.Theme = ThemeChoice.Dark; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(700);
                var dark = await FixesEvalAsync(ReportQaContrast);
                QaTrace("report-dark " + dark);
                var darkParts = dark.Split('|').Select(x => x.Split(':')).Where(x => x.Length == 4).ToList();
                Func<string, double> number = x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture);
                Check(darkParts.Count == 6 && darkParts.All(x => number(x[1]) < 60 && number(x[2]) >= 4.5 && number(x[3]) >= 4.5) && darkParts[0][1] + darkParts[0][2] != darkParts[1][1] + darkParts[1][2],
                    "in the dark theme the band tints are dark and their words keep 4.5:1 (" + dark + ")", results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-bands-dark.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                prefs.Theme = ThemeChoice.Light; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
                // A short summary is one picture with the plain name, whole: the legend box ends at 1440 px, the source just above the bottom.
                await FixesEvalAsync("document.querySelector('.univer-app-attendance-report').click();'open'");
                var one = await FixesEvalAsync("(function(){var r=" + ReportQaHost + ".shadowRoot;return r.querySelectorAll('.sheet').length+'|'+r.querySelectorAll('.page-bar').length+'|'+getComputedStyle(r.querySelectorAll('.actions button')[3]).display})()");
                await ReportAsync(main, GradeReport.Png);
                var single = Directory.GetFiles(folder, "*.png").FirstOrDefault();
                QaTrace("report-one " + one + " " + (single == null ? "none" : Path.GetFileName(single)) + " " + ReportQaSize(single) + " edge " + ReportQaEdge(single));
                Check(one == "1|0|flex" && single != null && Path.GetFileName(single) == GradeReport.FileName("ru", DateTime.Now, "png") && ReportQaWhole(single, true), "a short summary is one whole picture with the plain name (" + one + ")", results);
                if (single != null) File.Copy(single, Path.Combine(qa, "report-picture-short.png"), true);
                await FixesEvalAsync("window.__univerReportV1.close();'closed'");
                foreach (var record in history.Items.Where(x => x.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList()) ForgetRecord(record.Id);
                foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
                messages.Withdraw("downloadComplete");
                await ReportQaGoalAsync(qa, folder, results);
                // A long journal for the summary: fourteen courses, two pictures.
                body = ReportQaJournal + PolishCourse("История Казахстана", "63", "12") + PolishCourse("Иностранный язык", "91", "45") + PolishCourse("Философия", "74", "50") +
                    string.Concat(Enumerable.Range(1, 11).Select(i => PolishCourse("Предмет " + i, (40 + i * 5).ToString(), (30 + i * 4).ToString()))) + "</table>";
                SetPresentation(main, true); main.LoadHtml(PolishShell(body, false), SitePolicy.Origin + "/student/attendance/full/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
                await FixesEvalAsync("window.__univerMotion=false;'still'");

                // The summary opens from its button and reads every course.
                // Brought to the middle at once: smooth scrolling (Chromium 109) can leave it under the sticky header.
                await FixesEvalAsync("(function(){var b=document.querySelector('.univer-app-attendance-report'),h=document.documentElement,s=h.style.scrollBehavior;if(!b)return '';h.style.scrollBehavior='auto';b.scrollIntoView({block:'center'});h.style.scrollBehavior=s;return 'ok'})()"); await Task.Delay(300);
                var open = await FixesEvalAsync("(function(){var b=document.querySelector('.univer-app-attendance-report');if(!b||!b.offsetWidth)return '';var r=b.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)})()");
                QaTrace("report-open " + open);
                using (var client = main.GetDevToolsClient())
                {
                    var at = open.Split(','); if (at.Length == 2) await PressQaPointAsync(client, int.Parse(at[0]), int.Parse(at[1]), ReportQaHost + "?'open':''");
                }
                var sheet = await FixesEvalAsync("(function(){var h=" + ReportQaHost + ";if(!h)return '';var r=h.shadowRoot;return r.querySelectorAll('.course').length+'|'+r.querySelectorAll('.row[data-band]').length+'|'+r.querySelector('.sheet').getBoundingClientRect().width+'|'+(r.activeElement?r.activeElement.textContent:'')+'|'+r.querySelectorAll('.sheet').length+'|'+r.querySelectorAll('.page-bar button').length+'|'+getComputedStyle(r.querySelectorAll('.actions button')[3]).display})()");
                QaTrace("report-sheet " + sheet);
                Check(sheet == "14|28|760|Печать|2|2|none", "the summary opens from its button with every course, banded, on pages of its own width with their own Copy instead of the top one, focus on Print (" + sheet + ")", results);
                await Task.Delay(400);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-sheet.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                // The owner's picture at 110% was cut at the right and the bottom (DevTools takes the clip in zoomed pixels): at
                // any zoom each picture holds its whole page.
                var zooms = new List<string>();
                foreach (var z in new[] { 110, 125, 90, 200 })
                {
                    zoomPercent = z; ApplyZoom(); await Task.Delay(700); copied = null;
                    await ReportAsync(main, GradeReport.Copy, 1);
                    var probe = Path.Combine(folder, "zoom.bin"); if (copied != null) File.WriteAllBytes(probe, copied);
                    var whole = copied != null && ReportQaWhole(probe, true);
                    zooms.Add(z + ":" + ReportQaSize(probe) + ":" + ReportQaEdge(probe) + ":" + whole);
                    if (!whole && copied != null) File.Copy(probe, Path.Combine(qa, "report-zoom-" + z + ".png"), true);
                    if (File.Exists(probe)) File.Delete(probe);
                }
                zoomPercent = before; ApplyZoom(); await Task.Delay(400);
                QaTrace("report-zoom " + string.Join(" ", zooms));
                Check(zooms.Count == 4 && zooms.All(x => x.EndsWith(":True", StringComparison.Ordinal)), "at 90, 110, 125 and 200% a picture holds its whole page, not cut at the right or the bottom (" + string.Join(" ", zooms) + ")", results);
                messages.Withdraw("reportCopied");
                // A zoom applied between two pages moves the second under its clip (right size, wrong part): the set is dropped.
                reportCaptured = n => { if (n == 1) { zoomPercent = 125; ApplyZoom(); } };
                try { await ReportAsync(main, GradeReport.Png); } finally { reportCaptured = null; }
                var across = Directory.GetFiles(folder, "*.png").Length;
                QaTrace("report-zoom-across " + across + " " + messages.Shows("reportFailed"));
                Check(across == 0 && messages.Shows("reportFailed"), "a zoom applied while the pictures are taken saves none of them and says so (" + across + " files)", results);
                zoomPercent = before; ApplyZoom(); await Task.Delay(400); messages.Withdraw("reportFailed");

                // PNG from a real press of its button: a file for each page, the sheet alone at twice its width.
                // An earlier failure (a copy another program blocked) does not stay over the saved file's answer.
                SetStatus("reportCopyFailed");
                var png = await FixesEvalAsync("(function(){var r=" + ReportQaHost + ".shadowRoot.querySelectorAll('.actions button')[2].getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)})()");
                using (var client = main.GetDevToolsClient())
                {
                    var at = png.Split(','); await PressQaPointAsync(client, int.Parse(at[0]), int.Parse(at[1]), ReportQaHost + ".shadowRoot.querySelector('.status:not([hidden])')?'done':''");
                }
                await WaitQaScriptAsync("/Сохранено/.test(" + ReportQaHost + ".shadowRoot.querySelector('.status').textContent)", 20000);
                var set = Directory.GetFiles(folder, "*.png").OrderBy(x => x, StringComparer.Ordinal).ToList(); var sizes = set.Select(ReportQaSize).ToList();
                var status = await FixesEvalAsync(ReportQaHost + ".shadowRoot.querySelector('.status').textContent");
                QaTrace("report-png " + string.Join(" | ", set.Select(Path.GetFileName)) + " " + string.Join(" ", sizes) + " edge " + string.Join(" ", set.Select(ReportQaEdge)) + " " + status);
                var names = new[] { GradeReport.FileName("ru", DateTime.Now, "png", 1), GradeReport.FileName("ru", DateTime.Now, "png", 2) };
                Check(set.Count == 2 && set.Select(Path.GetFileName).SequenceEqual(names) && ReportQaWhole(set[0], true) && ReportQaWhole(set[1], false) && status.StartsWith("Сохранено в «Загрузки»: 2 картинки", StringComparison.Ordinal) &&
                    set.All(x => SavedFile.Zone(x) == null && history.Items.Any(r => r.Path == x)), "Save PNG from a real click makes a whole picture of each page, named by page, listed in Downloads (" + string.Join(" ", sizes) + ")", results);
                Check(messages.Current?.Key == "downloadComplete" && !messages.Shows("reportCopyFailed"), "a saved summary is told even after an earlier failure (" + messages.Current?.Key + ")", results);
                var newest = history.Items.FirstOrDefault(x => set.Contains(x.Path))?.Path;
                Check(set.Count == 2 && newest == set[0] && lastDownloadPath == set[0], "the downloads list and Open start at page 1, the page the sheet's «Показать в папке» selects (" + Path.GetFileName(newest ?? "") + ")", results);
                for (var i = 0; i < set.Count; i++) File.Copy(set[i], Path.Combine(qa, "report-picture-" + (i + 1) + ".png"), true);
                // Zoomed in, the pictures keep their size; a second set takes one number for all its pages.
                zoomPercent = 200; ApplyZoom(); await Task.Delay(700);
                await ReportAsync(main, GradeReport.Png);
                var replayed = await FixesEvalAsync("String(" + ReportQaHost + ".shadowRoot.getAnimations().length)");
                Check(replayed == "0", "back from the picture the sheet does not fade in again (" + replayed + " animations)", results);
                var again = Directory.GetFiles(folder, "*.png").Except(set).OrderBy(x => x, StringComparer.Ordinal).ToList(); var zoomed = string.Join(" ", again.Select(ReportQaSize));
                QaTrace("report-png-200 " + zoomed + " " + string.Join(" | ", again.Select(Path.GetFileName)));
                Check(ReportQaSame(zoomed, sizes) && again.Select(Path.GetFileName).SequenceEqual(new[] { GradeReport.FileName("ru", DateTime.Now, "png", 1, 1), GradeReport.FileName("ru", DateTime.Now, "png", 2, 1) }),
                    "at 200% the pictures have the same sizes and a second set never overwrites the first (" + zoomed + ")", results);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "report-sheet-200.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                // At 200% in a small window the buttons wrap, and the bar grows with them instead of running over the status.
                var bar = await FixesEvalAsync("(function(){var r=" + ReportQaHost + ".shadowRoot,b=r.querySelector('.bar').getBoundingClientRect(),a=r.querySelector('.actions').getBoundingClientRect(),c=r.querySelector('button.close').getBoundingClientRect(),s=r.querySelector('.bar').nextElementSibling.getBoundingClientRect();" +
                    "return [a.bottom<=b.bottom+.5,b.bottom<=s.top+.5,c.top<a.top,c.right>a.right].join('|')})()");
                Check(bar == "true|true|true|true", "at 200% the buttons stay inside the bar, above the status, with Close at the top right (" + bar + ")", results);
                zoomPercent = before; ApplyZoom(); await Task.Delay(400);
                // A 150% screen: the raw picture at the computed scale and the saved file both keep the size.
                string raw, scaled;
                using (var client = main.GetDevToolsClient())
                {
                    await client.Emulation.SetDeviceMetricsOverrideAsync(0, 0, 1.5, false); await Task.Delay(400);
                    try
                    {
                        var box = (await FixesEvalAsync("(function(){var b=window.__univerReportV1.capture(true),p=b.pages[0];return [p.x,p.y,p.width,p.height,b.ratio].join(',')})()")).Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        var shot = await client.Page.CaptureScreenshotAsync(CefSharp.DevTools.Page.CaptureScreenshotFormat.Png, null, new CefSharp.DevTools.Page.Viewport { X = box[0], Y = box[1], Width = box[2], Height = box[3], Scale = GradeReport.CaptureScale(box[4], zoomPercent / 100.0) }, null, true, null);
                        await FixesEvalAsync("window.__univerReportV1.capture(false);'ok'");
                        var probe = Path.Combine(folder, "probe.bin"); File.WriteAllBytes(probe, shot.Data); raw = "ratio " + box[4] + " " + ReportQaSize(probe); File.Delete(probe);
                        var known = Directory.GetFiles(folder, "*.png");
                        await ReportAsync(main, GradeReport.Png);
                        scaled = string.Join(" ", Directory.GetFiles(folder, "*.png").Except(known).OrderBy(x => x, StringComparer.Ordinal).Select(ReportQaSize));
                    }
                    finally { await client.Emulation.ClearDeviceMetricsOverrideAsync(); }
                }
                QaTrace("report-png-dsf " + raw + " / " + scaled);
                Check(ReportQaSame(scaled, sizes), "on a 150% screen the saved pictures keep their sizes (" + raw + " / " + scaled + ")", results);

                // PDF, Copy and Print. A press that comes while the PDF is made makes nothing of its own.
                var making = ReportAsync(main, GradeReport.Pdf); var pictures = Directory.GetFiles(folder, "*.png").Length;
                await ReportAsync(main, GradeReport.Png);
                // The sheet is told the app was busy: it shows no failure for that press (read before the PDF's own answer).
                var refused = await FixesEvalAsync("(function(){var s=" + ReportQaHost + ".shadowRoot.querySelector('.status');return s.classList.contains('bad')?'bad:'+s.textContent:'ok'})()");
                await making;
                Check(refused == "ok" && Directory.GetFiles(folder, "*.png").Length == pictures && Directory.GetFiles(folder, "*.pdf").Length == 1 && !messages.Shows("reportFailed"), "a press while the PDF is made makes no second file and no failure (" + refused + ")", results);
                var pdf = Directory.GetFiles(folder, "*.pdf").FirstOrDefault(); var head = pdf == null ? "" : new string(File.ReadAllBytes(pdf).Take(5).Select(b => (char)b).ToArray());
                // Fourteen courses do not fit one A4 sheet: the PDF flows on to more pages instead of cutting the list.
                var sheets = pdf == null ? 0 : Regex.Matches(Encoding.ASCII.GetString(File.ReadAllBytes(pdf)), @"/Type\s*/Page[^s]").Count;
                QaTrace("report-pdf " + (pdf == null ? "none" : new FileInfo(pdf).Length.ToString()) + " pages " + sheets);
                Check(pdf != null && head == "%PDF-" && new FileInfo(pdf).Length > 4000 && sheets >= 2 && !Directory.GetFiles(folder, "*.part").Any(), "Save PDF makes a PDF of every page in the folder and leaves no partial file (" + sheets + " pages)", results);
                if (pdf != null) File.Copy(pdf, Path.Combine(qa, "report.pdf"), true);
                await ReportAsync(main, GradeReport.Copy);
                Check(copied != null && copied.Length > 1000 && copied[1] == 'P' && copied[2] == 'N' && copied[3] == 'G', "Copy picture puts the PNG on the clipboard", results);
                await ReportAsync(main, GradeReport.Print);
                Check(printed == 1, "Print opens the print dialog once", results);
                // The second page's own «Копировать», clicked: that page alone goes to the clipboard.
                copied = null; messages.Withdraw("reportCopied");
                var copyAt = (await FixesEvalAsync("(function(){var b=" + ReportQaHost + ".shadowRoot.querySelectorAll('.page-bar button')[1];if(!b)return '';b.scrollIntoView({block:'center'});var r=b.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)})()") ?? "").Split(',');
                var copiedPage = false;
                if (copyAt.Length == 2)
                {
                    await Task.Delay(300);
                    using (var client = main.GetDevToolsClient()) await PressQaPointAsync(client, int.Parse(copyAt[0]), int.Parse(copyAt[1]), ReportQaHost + ".shadowRoot.querySelector('.status:not([hidden])')?'done':''");
                    copiedPage = await WaitQaScriptAsync("/Страница 2 скопирована/.test(" + ReportQaHost + ".shadowRoot.querySelector('.status').textContent)", 15000);
                }
                var copiedSize = "";
                if (copied != null) { var probe = Path.Combine(folder, "copied.bin"); File.WriteAllBytes(probe, copied); copiedSize = ReportQaSize(probe); File.Delete(probe); }
                QaTrace("report-copy-page " + string.Join(",", copyAt) + " " + copiedPage + " " + copiedSize);
                Check(copiedPage && sizes.Count == 2 && ReportQaSame(copiedSize, sizes.Skip(1).ToList()),"a real click on the second page's Copy puts that page alone on the clipboard (" + copiedSize + ")", results);
                // The student's own click on «Сохранить PNG» goes the whole way: page, __univerNative, the app, a file.
                var pngAt = (await FixesEvalAsync("(function(){var b=Array.from(" + ReportQaHost + ".shadowRoot.querySelectorAll('.actions button')).find(function(x){return x.textContent==='Сохранить PNG'});if(!b||!b.offsetWidth)return '';var r=b.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)})()") ?? "").Split(',');
                var clicked = Directory.GetFiles(folder, "*.png").Length; var savedByClick = false;
                if (pngAt.Length == 2)
                {
                    using (var client = main.GetDevToolsClient()) await PressQaPointAsync(client, int.Parse(pngAt[0]), int.Parse(pngAt[1]), "!/скопирована/.test(" + ReportQaHost + ".shadowRoot.querySelector('.status').textContent)");
                    savedByClick = await WaitQaScriptAsync("/Сохранено/.test(" + ReportQaHost + ".shadowRoot.querySelector('.status').textContent)", 15000);
                }
                QaTrace("report-click " + string.Join(",", pngAt) + " " + savedByClick);
                Check(savedByClick && Directory.GetFiles(folder, "*.png").Length == clicked + 2, "a real click on Save PNG reaches the app and saves both pictures", results);
                // Printed, the page is the sheet alone.
                // The paper is white: a transparent root would print the browser's own background, the theme colour.
                const string printState = "(function(){var h=" + ReportQaHost + ",o=Array.from(document.body.children).filter(function(n){return n!==h&&getComputedStyle(n).display!=='none'}).length;return getComputedStyle(h).display+'|'+o+'|'+getComputedStyle(h.shadowRoot.querySelector('.bar')).display+'|'+getComputedStyle(document.documentElement).backgroundColor})()";
                string printedState;
                using (var client = main.GetDevToolsClient())
                {
                    await client.Emulation.SetEmulatedMediaAsync(media: "print");
                    try { printedState = await FixesEvalAsync(printState); }
                    finally { await client.Emulation.SetEmulatedMediaAsync(media: "screen"); }
                }
                QaTrace("report-print " + printedState);
                Check(printedState == "block|0|none|rgb(255, 255, 255)", "printing with the summary open prints the sheet alone on white paper (" + printedState + ")", results);
                // A request after the sheet has closed makes nothing.
                await FixesEvalAsync("window.__univerReportV1.close();'closed'");
                var count = Directory.GetFiles(folder).Length; await ReportAsync(main, GradeReport.Png);
                Check(Directory.GetFiles(folder).Length == count && await FixesEvalAsync("document.activeElement&&document.activeElement.matches('.univer-app-attendance-report')?'back':''") == "back",
                    "a closed summary makes no file and focus returns to its button", results);
                prefs.Readable = false; await ApplyPresentationAsync(); await Task.Delay(300);
                Check(await FixesEvalAsync("!document.querySelector('.univer-app-attendance-report,[data-univer-band]')&&!" + ReportQaHost + "?'original':''") == "original", "Original has no summary button and no bands", results);
                prefs.Readable = true; await ApplyPresentationAsync();
            }
            finally
            {
                reportFolder = null; reportClipboard = null; reportPrint = null; reportCaptured = null; reportPath = null;
                // The step's answers («Сохранено…», «Картинка скопирована») go with it: later steps read the status line.
                foreach (var key in new[] { "reportCopied", "reportFailed", "reportCopyFailed", "printFailed", "downloadComplete" }) messages.Withdraw(key);
                RenderFeedback();
                if (zoomPercent != before) { zoomPercent = before; ApplyZoom(); }
                if (history != null) foreach (var record in history.Items.Where(x => x.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList()) ForgetRecord(record.Id);
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        // Where the sheet's content ends in a picture: the right edge of the legend box (1440 px when the sheet is drawn at
        // twice its size) and the last row that is not white.
        private static string ReportQaEdge(string path)
        {
            if (path == null || !File.Exists(path)) return "";
            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
            using (var image = Image.FromStream(stream))
            using (var bitmap = new Bitmap(image))
            {
                Func<Color, bool> paper = c => c.R > 250 && c.G > 250 && c.B > 250;
                var right = -1; var y = Math.Min(bitmap.Height - 1, 340);
                for (var x = bitmap.Width - 1; x >= 0; x--) if (!paper(bitmap.GetPixel(x, y))) { right = x; break; }
                var bottom = -1;
                for (var row = bitmap.Height - 1; row >= 0 && bottom < 0; row -= 2)
                    for (var x = 0; x < bitmap.Width; x += 6) if (!paper(bitmap.GetPixel(x, row))) { bottom = row; break; }
                return right + "," + bottom;
            }
        }
        // A picture of a whole page: 1520 px wide with the sheet's white corner, the content ending 20–160 px above the
        // bottom (a cut picture runs to its edge), and on the first page the legend box ending at 1440 px (a clip taken in
        // unzoomed pixels at 110% ran to 1519).
        private static bool ReportQaWhole(string path, bool legend)
        {
            var size = ReportQaSize(path);
            if (!size.StartsWith("1520x", StringComparison.Ordinal) || size.Contains(" ")) return false;
            var height = int.Parse(size.Substring(5)); var edge = ReportQaEdge(path).Split(',');
            int right = int.Parse(edge[0]), bottom = int.Parse(edge[1]);
            return bottom >= height - 160 && bottom <= height - 20 && (!legend || right >= 1436 && right <= 1442);
        }
        // The same pictures: equal widths, heights within 4 px (the sheet's fractional height rounds differently per zoom).
        private static bool ReportQaSame(string found, List<string> expected)
        {
            var a = (found ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length != expected.Count) return false;
            for (var i = 0; i < a.Length; i++)
            {
                var x = a[i].Split('x'); var y = expected[i].Split('x');
                int h1, h2;
                if (x.Length != 2 || y.Length != 2 || x[0] != y[0] || !int.TryParse(x[1], out h1) || !int.TryParse(y[1], out h2) || Math.Abs(h1 - h2) > 4) return false;
            }
            return true;
        }
        private static string ReportQaSize(string path)
        {
            if (path == null || !File.Exists(path)) return "";
            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
            using (var image = Image.FromStream(stream))
            using (var bitmap = new Bitmap(image))
            {
                // The sheet's own white corner, never the page or the dim backdrop.
                var corner = bitmap.GetPixel(4, 4);
                return image.Width + "x" + image.Height + (corner.R > 245 && corner.G > 245 && corner.B > 245 ? "" : " corner " + corner);
            }
        }
    }
}
