using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private const string ScheduleExportQaButtons = "document.querySelectorAll('.univer-app-schedule-export button[data-univer-export]')";
        // The schedule page's two export buttons on the syllabus step's timetable (weeks 3–5, served without the network):
        // pressed with DevTools input as the student would, the calendar file and the picture in Downloads (a QA folder),
        // the picture in both themes, a neighbour week that cannot be read, and a scripted click that makes nothing.
        private async Task ScheduleExportQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            var folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", "schedule-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            var theme = prefs.Theme; var language = prefs.Language;
            scheduleExportFolder = folder;
            try
            {
                await RestoreQaWindowAsync();
                prefs.Language = "ru"; prefs.Readable = true; await RefreshDocumentScriptsAsync();
                // A run of this step alone has had no LoadHtml yet: the factory the fixture pages go into is made here.
                if (main.ResourceRequestHandlerFactory == null) main.ResourceRequestHandlerFactory = new ResourceRequestHandlerFactory();
                RegisterSyllabusPages();
                const string heading = "<script>function jQuery(){return{bind:function(){}}}</script>";
                main.LoadHtml(heading + SyllabusScheduleFixture(3), SitePolicy.Origin + "/student/myschedule/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync();
                await FixesEvalAsync("window.__univerMotion=false;'still'");
                var buttons = await WaitForAsync("(function(){var b=Array.from(" + ScheduleExportQaButtons + "),c=getComputedStyle(document.documentElement).getPropertyValue('--u-control').trim();" +
                    "if(b.length!==2||b.some(function(x){return !x.offsetWidth}))return '';return b.map(function(x){return x.textContent+'~'+Math.round(x.getBoundingClientRect().height)}).join('|')+'|'+c})()");
                QaTrace("schedule-export-buttons " + buttons);
                var parts = buttons.Split('|');
                Check(parts.Length == 3 && parts[0].StartsWith("В календарь~", StringComparison.Ordinal) && parts[1].StartsWith("Картинкой~", StringComparison.Ordinal) &&
                    parts[0].Split('~')[1] + "px" == parts[2] && parts[1].Split('~')[1] + "px" == parts[2],
                    "the schedule page has «В календарь» and «Картинкой» at the page's control height (" + buttons + ")", results);
                // Beside the mode switch when there is room; under it, inside the window, when there is not.
                const string ScheduleBarLook = "(function(){var m=document.querySelector('.univer-app-schedule-modes').getBoundingClientRect(),e=document.querySelector('.univer-app-schedule-export').getBoundingClientRect();" +
                    "var apart=e.left>=m.right||e.top>=m.bottom;return (Math.abs(e.top-m.top)<12?'row':'below')+'|'+(apart&&e.right<=innerWidth+0.5?'clear':'overlap')})()";
                var wide = await FixesEvalAsync(ScheduleBarLook); string narrow;
                using (var client = main.GetDevToolsClient())
                {
                    File.WriteAllBytes(Path.Combine(qa, "schedule-export-page.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                    await client.Emulation.SetDeviceMetricsOverrideAsync(400, 760, 1, false); await Task.Delay(400);
                    try { narrow = await FixesEvalAsync(ScheduleBarLook); File.WriteAllBytes(Path.Combine(qa, "schedule-export-narrow.png"), (await client.Page.CaptureScreenshotAsync()).Data); }
                    finally { await client.Emulation.ClearDeviceMetricsOverrideAsync(); await Task.Delay(300); }
                }
                Check(wide == "row|clear" && narrow.EndsWith("|clear", StringComparison.Ordinal), "the export buttons share the mode switch's row and stay clear of it in a narrow window (" + wide + ", " + narrow + ")", results);

                // A script's click makes nothing: no file, no busy buttons.
                await FixesEvalAsync(ScheduleExportQaButtons + "[0].click();'clicked'"); await Task.Delay(1500);
                Check(Directory.GetFiles(folder).Length == 0 && await FixesEvalAsync("Array.from(" + ScheduleExportQaButtons + ").some(function(b){return b.getAttribute('aria-busy')==='true'})?'busy':'free'") == "free",
                    "a scripted click on the export buttons makes no file", results);

                // The calendar: shown week 3 and its neighbour 4, each repeated every second week while the semester (week 5) lasts.
                var ics = await PressScheduleExportQaAsync(folder, "ics");
                var text = ics == null ? "" : File.ReadAllText(ics, Encoding.UTF8); var bytes = ics == null ? new byte[0] : File.ReadAllBytes(ics);
                var events = text.Split(new[] { "BEGIN:VEVENT" }, StringSplitOptions.None).Length - 1;
                var rules = text.Split(new[] { "RRULE:FREQ=WEEKLY;INTERVAL=2;UNTIL=" }, StringSplitOptions.None).Length - 1;
                QaTrace("schedule-export-ics " + Path.GetFileName(ics) + " events=" + events + " rules=" + rules + " status=" + messages.Current?.Key);
                Check(ics != null && Path.GetFileName(ics) == "Расписание, недели 3–4.ics" && bytes.Length > 3 && bytes[0] == (byte)'B' && text.StartsWith("BEGIN:VCALENDAR\r\n", StringComparison.Ordinal) &&
                    !text.Replace("\r\n", "").Contains("\n") && events == 13 && rules == 6 && text.Contains("SUMMARY:Политология") && messages.Current?.Key == "downloadComplete" &&
                    history.Items.Any(x => x.Path == ics && x.Source == Strings.Get("scheduleSource", "ru")),
                    "«В календарь» saves both weeks' classes as a calendar file in Downloads, week 3 repeated in week 5, and lists it (" + Path.GetFileName(ics) + ", " + events + " events, " + rules + " repeating)", results);

                // The picture, light and dark: the two weeks side by side at twice the size, on the theme's background.
                var light = await PressScheduleExportQaAsync(folder, "png");
                var lightLook = ScheduleExportQaPicture(light, qa, "schedule-export-light.png");
                prefs.Theme = ThemeChoice.Dark; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
                var dark = await PressScheduleExportQaAsync(folder, "png");
                var darkLook = ScheduleExportQaPicture(dark, qa, "schedule-export-dark.png");
                prefs.Theme = ThemeChoice.Light; ApplyTheme(); await RefreshDocumentScriptsAsync(); await ApplyPresentationAsync(); await Task.Delay(300);
                QaTrace("schedule-export-png light=" + lightLook + " dark=" + darkLook);
                Check(light != null && Path.GetFileName(light) == "Расписание, недели 3–4.png" && lightLook.StartsWith("1760x", StringComparison.Ordinal) && lightLook.EndsWith("|light", StringComparison.Ordinal) &&
                    dark != null && Path.GetFileName(dark) == "Расписание, недели 3–4 (1).png" && darkLook.EndsWith("|dark", StringComparison.Ordinal),
                    "«Картинкой» saves the two weeks side by side, in the light and the dark theme, never over the earlier file (" + lightLook + ", " + darkLook + ")", results);

                // The neighbour cannot be read (the site answers with its sign-in page): the calendar keeps week 3 alone and says so.
                var route4 = SyllabusWeekRoute(4, out _);
                var factory = main.ResourceRequestHandlerFactory as ResourceRequestHandlerFactory;
                factory?.RegisterHandler(SitePolicy.Origin + route4, ResourceHandler.GetByteArray("<!doctype html><meta charset='utf-8'><form id='login_frm'></form>", Encoding.UTF8), "text/html", false);
                try
                {
                    var part = await PressScheduleExportQaAsync(folder, "ics");
                    var partText = part == null ? "" : File.ReadAllText(part, Encoding.UTF8);
                    var partEvents = partText.Split(new[] { "BEGIN:VEVENT" }, StringSplitOptions.None).Length - 1;
                    var status = messages.Current?.Key;
                    var missing = await PressScheduleExportQaAsync(folder, "png");
                    var missingLook = ScheduleExportQaPicture(missing, qa, "schedule-export-missing.png");
                    QaTrace("schedule-export-missing " + Path.GetFileName(part) + " events=" + partEvents + " status=" + status + " png=" + missingLook);
                    Check(part != null && partEvents == 6 && status == "schedulePart" && missingLook.StartsWith("1760x", StringComparison.Ordinal),
                        "a neighbour week that cannot be read is left out of both files and the student is told (" + partEvents + " events, " + status + ")", results);
                }
                finally { factory?.RegisterHandler(SitePolicy.Origin + route4, ResourceHandler.GetByteArray("<!doctype html><meta charset='utf-8'>" + SyllabusScheduleFixture(4), Encoding.UTF8), "text/html", false); }
                // Edge weeks, drawn straight in Kazakh: an empty week and a class whose name runs over several lines.
                Func<DateTime, long> ms = d => (long)(d.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
                var longName = string.Concat(Enumerable.Repeat("Ақпараттық қауіпсіздіктің теориялық негіздері ", 6)).Trim();
                var edgeJson = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    { "term", "2026/1" }, { "until", ms(monday.AddDays(14)) }, { "missing", new object[0] },
                    { "weeks", new object[] {
                        new Dictionary<string, object> { { "number", 7 }, { "labelled", true }, { "start", ms(monday) }, { "end", ms(monday.AddDays(7)) }, { "days", new object[] { new object[0] } } },
                        new Dictionary<string, object> { { "number", 8 }, { "labelled", true }, { "start", ms(monday.AddDays(7)) }, { "end", ms(monday.AddDays(14)) },
                            { "days", new object[] { new object[] { new Dictionary<string, object> { { "start", 540 }, { "end", 590 }, { "name", longName }, { "type", "Дәріс" }, { "room", "ФИТ / Ауд.: 309" }, { "teacher", "Синтетический П. П." } } } } } } } }
                });
                var edge = SchedulePair.Parse(edgeJson, DateTime.Now);
                var edgePath = Path.Combine(folder, "edge.png");
                var edgePicture = edge == null ? null : SchedulePicture(edge, "kz");
                if (edgePicture != null) File.WriteAllBytes(edgePath, edgePicture);
                var edgeLook = ScheduleExportQaPicture(edgePicture == null ? null : edgePath, qa, "schedule-export-edge-kz.png");
                QaTrace("schedule-export-edge " + edgeLook);
                Check(edgeLook.StartsWith("1760x", StringComparison.Ordinal) && SchedulePair.Heading(edge.Weeks[0], "kz") == "7-апта, тақ",
                    "an empty week and a very long class name are drawn in Kazakh at the picture's width (" + edgeLook + ")", results);
            }
            finally
            {
                scheduleExportFolder = null;
                if (prefs.Theme != theme) { prefs.Theme = theme; ApplyTheme(); }
                prefs.Language = language; await RefreshDocumentScriptsAsync();
                foreach (var record in history.Items.Where(x => x.Path != null && x.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).ToList()) ForgetRecord(record.Id);
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (messages.Withdraw("downloadComplete") | messages.Withdraw("schedulePart") | messages.Withdraw("scheduleFailed")) RenderFeedback();
            }
        }
        // Presses one export button with DevTools input and waits for the page to be answered; the new file, or null.
        private async Task<string> PressScheduleExportQaAsync(string folder, string kind)
        {
            var before = new HashSet<string>(Directory.GetFiles(folder));
            await WaitQaScriptAsync("!Array.from(" + ScheduleExportQaButtons + ").some(function(b){return b.getAttribute('aria-busy')==='true'})", 5000);
            var at = await FixesEvalAsync("(function(){var b=document.querySelector('.univer-app-schedule-export button[data-univer-export=" + kind + "]'),h=document.documentElement;if(!b)return '';" +
                "h.style.scrollBehavior='auto';b.scrollIntoView({block:'center'});window.qaExportPresses=window.qaExportPresses||0;if(!window.qaExportCounted){window.qaExportCounted=true;" +
                "document.addEventListener('click',function(e){if(e.isTrusted&&e.target.closest&&e.target.closest('.univer-app-schedule-export'))window.qaExportPresses++},true)}" +
                "var r=b.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2)+','+window.qaExportPresses})()");
            var point = at.Split(',');
            if (point.Length != 3) return null;
            using (var client = main.GetDevToolsClient()) await PressQaPointAsync(client, int.Parse(point[0]), int.Parse(point[1]), "window.qaExportPresses>" + point[2]);
            for (var until = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < until; await Task.Delay(100))
            {
                var made = Directory.GetFiles(folder).Where(path => !before.Contains(path) && !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)).ToList();
                if (made.Count > 0 && await FixesEvalAsync("Array.from(" + ScheduleExportQaButtons + ").some(function(b){return b.getAttribute('aria-busy')==='true'})?'busy':'free'") == "free") return made[0];
            }
            return null;
        }
        // "<width>x<height>|light|dark" by the picture's corner, kept beside the self-test's screenshots.
        private static string ScheduleExportQaPicture(string path, string qa, string name)
        {
            if (path == null) return "";
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != (byte)'P') return "not png";
            File.Copy(path, Path.Combine(qa, name), true);
            using (var stream = new MemoryStream(bytes)) using (var image = new Bitmap(stream))
            {
                var c = image.GetPixel(2, 2); var sum = c.R + c.G + c.B;
                return image.Width + "x" + image.Height + "|" + (sum > 600 ? "light" : sum < 200 ? "dark" : "mid");
            }
        }
    }
}
