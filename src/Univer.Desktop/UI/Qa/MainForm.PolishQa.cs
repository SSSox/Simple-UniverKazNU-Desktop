using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The owner's review of the restyled copy, point by point, on synthetic pages: one control size, round header tabs,
    // tiles that keep their words, grades that read alike on both pages, a journal as wide as its marks, and the chrome's zoom.
    internal sealed partial class MainForm
    {
        private const string PolishMenu = "<tr id='qa-menu-row'><td><table class='inner'><tr><td class='center'><div id='menu-home'><table id='tools'><tr><td class='center'><table class='tool'><tr><td><ul id='study'>" +
            "<li><a href='/student/umkd/'>УМКД</a></li><li><a href='/student/myschedule/'>Расписание</a></li><li><a href='/student/transcript/'>Транскрипт</a></li>" +
            "<li><a href='/student/attendance/full/'>Журнал посещаемости</a></li></ul></td></tr><tr><td class='bottom-center'>Главная</td></tr></table></td></tr></table></div></td></tr></table></td></tr>";
        // Every button and link that acts as one, on whatever page is open: heights outside one narrow band are named.
        private const string EvenControlsQa = "(function(){var list=Array.from(document.querySelectorAll('#l4 button,#l4 input[type=button],#l4 input[type=submit],#l4 .ts>a,#l4 a.univer-app-action-link,#l4 a.univer-app-back-link,#l4 .univer-app-news-pager>a,#l4 select')).filter(function(n){return n.offsetWidth>0&&!n.matches('.univer-app-attendance-overview-name,.univer-app-sort-toggle,.univer-app-pin,.univer-app-service-card,.univer-app-news-reset,.univer-app-news-all,.univer-app-news-preset,.univer-app-attendance-details-toggle,.univer-app-course-syllabus,.univer-app-course-open,.univer-app-add-tile');});var odd=list.filter(function(n){return n.offsetHeight<38||n.offsetHeight>46;}).map(function(n){return (n.className||n.tagName)+':'+n.offsetHeight;});return list.length&&!odd.length?'even':list.length+':'+odd.join()})()";
        private static string PolishShell(string body, bool bachelor)
        {
            return "<style>table{border-collapse:collapse;border-spacing:0}td{padding:0}</style><table class='wrappertable'><tr id='l1'><td class='header'>КазНУ<a href='/lang/change/kz/'><img src='/img/kz.png'></a><img src='/img/ru.png'><a href='/lang/change/en/'><img src='/img/en.png'></a></td></tr>" +
                "<tr id='l2'><td><div id='logo'></div><div class='links'><a href='/user/pass/'>Профиль</a><a href='/user/logout'>Выход</a></div></td></tr>" +
                "<tr id='l3'><td class='center'><a " + (bachelor ? "" : "class='active' ") + "href='/news/student'><div class='center'><span>Главная</span></div></a><a " + (bachelor ? "class='active' " : "") + "href='/student/bachelor/'><div class='center'>Бакалавр</div></a></td></tr>" +
                PolishMenu + "<tr id='l4'><td><table class='inner'><tr><td class='left'></td><td class='center'>" + body + "</td><td class='right'></td></tr></table></td></tr></table>";
        }
        private static string PolishCourse(string name, string first, string second)
        {
            return "<tr><td class='ct'>" + name + " ([РК1 + РК2 + Экз] (100)) 01.09.2026 - 12.12.2026</td></tr>" + AttendanceQaLesson("polish-" + Math.Abs(name.GetHashCode()), "семинар", first) +
                "<tr><td class='ct'><b>РК1 (100): </b>" + first + " <b>РК2 (100): </b>" + second + "</td></tr>";
        }
        private async Task PolishQaAsync(string qa, List<string> results)
        {
            while (!preparedBrowsers.Contains(main)) await Task.Delay(50);
            prefs.Language = "ru"; prefs.Readable = true; prefs.Animations = true; prefs.AutoLoginPaused = false;
            prefs.Pins = new List<PinnedLink> { new PinnedLink { Title = "Дистанционные курсы", Url = SitePolicy.Origin + "/my/distance/" }, new PinnedLink { Title = "Текущая аттестация", Url = SitePolicy.Origin + "/my/attestation/" } };
            await RefreshDocumentScriptsAsync();
            var started = zoomPercent; SetZoom(100);
            var json = new JavaScriptSerializer();
            var names = new[] { "Социология", "Психология", "Культурология", "Политология", "История Казахстана", "Казахский (русский) язык", "Введение в информационную безопасность", "Иностранный язык" };
            var marks = new[] { "87", "90", "96", "63", "89", "77", "95", "0" };
            var journal = "<table class='wrappertable'><tr id='l4'><td class='center'><table class='mt'><tr><td class='ct'>Журнал посещений и успеваемости</td></tr>" + string.Concat(names.Select((name, i) => PolishCourse(name, marks[i], i % 2 == 0 ? "0" : "15"))) + "</table></td></tr></table>";
            var stub = "<script>window.fetch=async function(path){return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return " + json.Serialize(journal) + ";}}};</script>";
            var bachelor = stub + PolishShell("<table class='mt' id='qa-gpa-source' style='height:70px'><tr class='top'><td class='ct message'><ul><li>Переводной GPA = 2.85</li><li>Общий GPA = 3.12</li></ul></td></tr></table>", true);
            SetPresentation(main, true); main.LoadHtml(bachelor, SitePolicy.Origin + "/student/bachelor/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync();
            Size = new Size(1360, 860); await Task.Delay(700);
            await AutomationController.EvaluateAsync(main, "window.__univerMotion=false;document.querySelector('.univer-app-overview-grades').scrollIntoView({block:'start'});window.scrollBy(0,-70);'top'"); await Task.Delay(150);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var a=Array.from(document.querySelectorAll('.univer-app-tabs>a')).filter(function(n){return n.offsetWidth>0;});return a.length>=2&&a.every(function(n){return n.offsetHeight===36;})?'tabs':a.map(function(n){return n.offsetHeight;}).join()})()") == "tabs",
                "the header tabs share one height", results);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var t=document.querySelector('.univer-app-sections').offsetHeight,u=document.querySelector('.univer-app-account>summary').offsetHeight,f=document.querySelector('.univer-app-flags').offsetHeight;return t===40&&u===40&&f===40?'even':[t,u,f].join()})()") == "even",
                "every control of the header has the same height", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-overview-more')?.click();'more'"); await Task.Delay(300);
            var overview = await AutomationController.EvaluateAsync(main, "(function(){var box=document.querySelector('.univer-app-overview-grades'),head=box.querySelector('.univer-app-overview-head'),h=head.querySelector('h2'),rows=Array.from(box.querySelectorAll('.univer-app-overview-courses>li')).filter(function(n){return !n.hidden;});" +
                "box.scrollTop=20;var gap=h.getBoundingClientRect().top-box.getBoundingClientRect().top,tall=rows.map(function(r){return r.getBoundingClientRect().height;}).sort(function(a,b){return a-b;})[rows.length>>1];return [box.scrollTop,Math.round(gap),rows.length,Math.round(tall)].join('|')})()");
            QaTrace("qa-polish-overview " + overview);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-overview.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var parts = (overview ?? "").Split('|'); int gapTop, rowsShown, rowHeight;
            Check(parts.Length == 4 && parts[0] == "0" && int.TryParse(parts[1], out gapTop) && gapTop >= 12, "the grades card cannot scroll its head away and keeps room above the title", results);
            Check(parts.Length == 4 && int.TryParse(parts[2], out rowsShown) && rowsShown >= 6 && int.TryParse(parts[3], out rowHeight) && rowHeight <= 48, "grade rows on the home screen are compact", results);
            await AutomationController.EvaluateAsync(main, "window.scrollTo(0,0);'top'"); await Task.Delay(120);
            var tileAt = (await AutomationController.EvaluateAsync(main, "(function(){var r=document.querySelector('.univer-app-quick .univer-app-tile>a').getBoundingClientRect();return Math.round(r.left+r.width/3)+'|'+Math.round(r.top+r.height/2)})()") ?? "0|0").Split('|');
            var tileRest = await AutomationController.EvaluateAsync(main, "(function(){var s=getComputedStyle(document.querySelector('.univer-app-quick .univer-app-tile>a'));return s.backgroundColor+s.borderTopColor+'/'+s.borderTopWidth})()");
            main.GetBrowser().GetHost().SendMouseMoveEvent(new MouseEvent(int.Parse(tileAt[0]), int.Parse(tileAt[1]), CefEventFlags.None), false); await Task.Delay(350);
            var tileHover = await AutomationController.EvaluateAsync(main, "(function(){var a=document.querySelector('.univer-app-quick .univer-app-tile>a'),s=getComputedStyle(a);return a.matches(':hover')+'|'+s.backgroundColor+s.borderTopColor})()");
            QaTrace("qa-polish-hover " + tileRest + " -> " + tileHover);
            Check((tileRest ?? "").EndsWith("/1px", StringComparison.Ordinal) && (tileHover ?? "").StartsWith("true|", StringComparison.Ordinal) && !(tileRest ?? "").StartsWith((tileHover ?? "").Substring(5), StringComparison.Ordinal), "a home tile has an outline at rest and a clear highlight under the pointer", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-tile-hover.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            main.GetBrowser().GetHost().SendMouseMoveEvent(new MouseEvent(2, 2, CefEventFlags.None), true);
            // Long pinned names keep their words, at the widths and zoom the owner uses.
            const string words = "(function(){var labels=Array.from(document.querySelectorAll('.univer-app-quick .univer-app-tile-label'));var probe=document.createElement('span');probe.style.cssText='position:absolute;visibility:hidden;white-space:nowrap';var broken=[];labels.forEach(function(l){var s=getComputedStyle(l);probe.style.font=s.font;l.parentNode.appendChild(probe);l.textContent.trim().split(/\\s+/).forEach(function(w){probe.textContent=w;if(probe.getBoundingClientRect().width>l.getBoundingClientRect().width+.5)broken.push(w);});probe.remove();});return labels.length>=8&&!broken.length?'whole':labels.length+':'+broken.join()})()";
            await AutomationController.EvaluateAsync(main, "window.scrollTo(0,0);'top'");
            foreach (var zoom in new[] { 100, 130 })
                foreach (var width in new[] { 1360, 1100 })
                {
                    SetZoom(zoom); Size = new Size(width, 860); await Task.Delay(450);
                    var whole = await AutomationController.EvaluateAsync(main, words); QaTrace("qa-polish-words " + zoom + " " + width + " " + whole);
                    Check(whole == "whole", "tile labels keep whole words at " + width + " px and " + zoom + "%", results);
                }
            SetZoom(130); Size = new Size(1360, 860); await Task.Delay(450);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-tiles-130.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            // The chrome grows with the page: the toolbar like the sidebar, and its icons with it.
            Check(toolbar.Height == SZ(44) && toolbar.Height >= (int)(44 * layoutScale * 1.29f) && menu.Height == SZ(28) && Math.Abs(pageTitle.Font.Size / (Theme.Body * 1f) - 1.3f) < .25f && menu.Zoom > 1.29f, "the toolbar, its buttons and its text follow the zoom", results);
            SaveBitmap(toolbar, Path.Combine(qa, "polish-toolbar-130.png")); SaveBitmap(sidebar, Path.Combine(qa, "polish-sidebar-130.png"));
            OpenFind(); await Task.Delay(80);
            Check(findBar.Height == SZ(48) && findBar.InputForQa.Font.Size > Theme.Body * 1.2f, "the find bar follows the zoom", results);
            SaveBitmap(findBar, Path.Combine(qa, "polish-find-130.png"));
            findBar.Close(); SetStatus("downloadComplete"); await Task.Delay(80);
            Check(toast.Shown && toast.Width >= (int)(380 * 1.29f * layoutScale) && toast.Primary.Font.Size > Theme.Meta * 1.2f, "a toast follows the zoom", results);
            SaveBitmap(toast, Path.Combine(qa, "polish-toast-130.png"));
            messages.Dismiss(); RenderFeedback();
            SetStatus("presentationSlow"); await Task.Delay(80);
            Check(feedback.Visible && feedback.Primary.Height >= (int)(28 * 1.29f * layoutScale), "the banner follows the zoom", results);
            SaveBitmap(feedback, Path.Combine(qa, "polish-banner-130.png"));
            messages.Dismiss(); RenderFeedback();
            ShowNotices(); await Task.Delay(120);
            Check(NoticesShown && noticesPanel.TitleSizeForQa > Theme.Emphasis * 1.2f, "the notifications panel follows the zoom", results);
            SaveBitmap(noticesPanel, Path.Combine(qa, "polish-notices-130.png"));
            HideNotices(); await Task.Delay(120);
            using (var form = new SettingsForm(new Credentials { Login = "QA student", Password = "synthetic fixture" }, prefs, false, null, SidebarZoom))
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); await Task.Delay(120);
                form.SelectAppearanceForQa(); await Task.Delay(120);
                Check(form.SaveButton.Height >= (int)(32 * 1.29f) && form.SaveButton.Font.Size > Theme.Body * 1.2f && form.ClientSize.Width >= (int)(552 * 1.29f * layoutScale), "Settings follows the zoom in size and type", results);
                SaveBitmap(form, Path.Combine(qa, "polish-settings-130.png")); form.Close();
            }
            SetZoom(100); await Task.Delay(200);
            SaveBitmap(sidebar, Path.Combine(qa, "polish-sidebar-100.png"));
            Check(sideHome.Height == SZ(36) && scheduleButton.Top - sideHome.Bottom == SZ(40) - SZ(36), "the sections read as separate buttons", results);
            using (var form = new SettingsForm(new Credentials { Login = "QA student", Password = "synthetic fixture" }, prefs))
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); await Task.Delay(120);
                form.SelectAppearanceForQa(); await Task.Delay(120);
                var row = form.TabsForQa;
                using (var shot = new Bitmap(row.Width, row.Height))
                {
                    row.DrawToBitmap(shot, new Rectangle(Point.Empty, row.Size));
                    shot.Save(Path.Combine(qa, "polish-settings-tabs.png"), ImageFormat.Png);
                    // The grey line runs on under every tab, and the chosen tab's bar lies over it.
                    var tabX = form.UnselectedTabCentreForQa; var wanted = Theme.Current.Line;
                    Check(shot.GetPixel(tabX, row.Height - 1).ToArgb() == wanted.ToArgb(), "the tab row's line runs on under an unselected tab", results);
                }
                SaveBitmap(form, Path.Combine(qa, "polish-settings-100.png"));
                form.Close();
            }
            // Nothing to save: the window stays quiet, and its second button simply closes it.
            var calm = prefs.Clone(); calm.AutoLoginPaused = false;
            using (var form = new SettingsForm(new Credentials { Login = "QA student", Password = "synthetic fixture" }, calm))
            {
                form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); await Task.Delay(120);
                Check(!form.SaveButton.Enabled && form.CancelForQa.Text == Strings.Get("closeWindow", calm.Language), "Settings offers Save only after a change", results);
                form.SelectAppearanceForQa(); form.DarkThemeForQa.PerformClick();
                Check(form.SelectedTheme == ThemeChoice.Dark && form.SaveButton.Enabled && form.CancelForQa.Text == Strings.Get("cancelChanges", calm.Language), "a theme picture chooses the theme and wakes Save", results);
                form.LanguageForQa(2).PerformClick();
                Check(form.Language == "en" && form.Text == Strings.Get("settings", "en"), "a language segment changes the window's language at once", results);
                form.Close();
            }
            // Toast: the theme's own surface, a short life, and a fading copy with the same round corners.
            using (var panel = new ToastPanel(layoutScale))
            {
                workspace.Controls.Add(panel); var status = new UiStatus(); status.Update("downloadComplete");
                panel.Render(status.Current, "Готово", "Файл сохранён в Загрузки", "Загрузки", "", "Закрыть"); panel.BringToFront(); await Task.Delay(400);
                Check(panel.BackColor.ToArgb() == Theme.Current.Raised.ToArgb() && panel.Lifetime <= 4000, "the toast uses the theme's surface and a short life", results);
                using (var picture = panel.RoundedPicture())
                {
                    Check(picture != null && picture.GetPixel(0, 0).A == 0 && picture.GetPixel(picture.Width - 1, picture.Height - 1).A == 0 && picture.GetPixel(picture.Width / 2, picture.Height / 2).A == 255, "the toast's fading copy keeps its round corners", results);
                    picture?.Save(Path.Combine(qa, "polish-toast.png"), ImageFormat.Png);
                }
                Motion.KeyboardInput = true; panel.Hide(); Motion.KeyboardInput = false; workspace.Controls.Remove(panel);
            }
            // The journal page: the home screen's rows, a plain caption, and a journal as wide as its marks.
            var attendance = "<script>window.fetch=async function(path){return {status:200,url:new URL(path,location.origin).href,headers:{get:function(){return null}},text:async function(){return '<div id=l4></div>';}}};sessionStorage.removeItem('univer.desktop.attendance-kind.v1');sessionStorage.removeItem('univer.desktop.attendance-course.v1:/student/attendance/full/2026/1');</script>" + journal;
            SetPresentation(main, true); main.LoadHtml(attendance, SitePolicy.Origin + "/student/attendance/full/2026/1"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(400);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var list=document.querySelector('.univer-app-attendance-overview');var bars=list.querySelectorAll('.univer-app-overview-score-bar').length,chips=list.querySelectorAll('.univer-app-overview-score').length,rows=Array.from(list.querySelectorAll('li'));var tall=rows.map(function(r){return r.getBoundingClientRect().height;}).sort(function(a,b){return a-b;})[rows.length>>1];return bars>=8&&chips===16&&tall<=48?'same':[bars,chips,tall].join()})()") == "same",
                "the summary on the grades page uses the home screen's chips and bars", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-grades.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            Check(await AutomationController.EvaluateAsync(main, "(function(){var c=document.querySelector('p.univer-app-attendance-filter-caption'),b=document.querySelector('.univer-app-attendance-filters');var s=getComputedStyle(c),r=s.color.match(/\\d+/g).map(Number);return c.textContent==='Тип занятий'&&!b.contains(c)&&b.getAttribute('aria-label')==='Тип занятий'&&r[0]<60&&r[1]<70&&r[2]<90?'plain':c.textContent+s.color})()") == "plain",
                "the class filter is named in plain words, in ink, outside its track", results);
            await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-course-list button:nth-child(2)').click();'course'"); await Task.Delay(350);
            await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-attendance-details-toggle[aria-expanded=false]').forEach(function(t){t.click();});'open'"); await Task.Delay(350);
            var matrix = await AutomationController.EvaluateAsync(main, "(function(){var t=Array.from(document.querySelectorAll('table.univer-app-matrix')).filter(function(n){return n.offsetWidth>0;})[0];if(!t)return 'none';var host=t.closest('.univer-app-table-scroll'),sum=t.querySelector('td.univer-app-sum'),cell=t.querySelector('td');var s=getComputedStyle(sum),line=getComputedStyle(cell).borderRightColor;return [Math.round(t.getBoundingClientRect().width),host.scrollWidth-host.clientWidth,s.fontWeight,s.backgroundColor,line].join('|')})()");
            QaTrace("qa-polish-matrix " + matrix);
            var cells = (matrix ?? "").Split('|'); int tableWidth, sideways;
            Check(cells.Length == 5 && int.TryParse(cells[0], out tableWidth) && int.TryParse(cells[1], out sideways) && tableWidth <= 420 && sideways <= 1, "a course journal keeps its marks compact, with no sideways scroll", results);
            Check(cells.Length == 5 && cells[2] == "700" && cells[3] != "rgba(0, 0, 0, 0)", "the journal's total stands out", results);
            using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-journal.png"), (await client.Page.CaptureScreenshotAsync()).Data);
            var sizes = await AutomationController.EvaluateAsync(main, EvenControlsQa);
            QaTrace("qa-polish-sizes " + sizes);
            Check(sizes == "even", "every button on the journal page has one size", results);
            prefs.Readable = false; await ApplyPresentationAsync(); await Task.Delay(300);
            Check(await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-sum').length===0?'gone':''") == "gone", "Original takes the total's mark back", results);
            prefs.Readable = true; await ApplyPresentationAsync();
            foreach (var route in new[] { "/news/student", "/student/myschedule/", "/student/attendance/full/" })
            {
                loading.Zoom = 1; loading.SetRoute(SitePolicy.Origin + route); loading.Visible = true; loading.BringToFront(); loading.Refresh();
                SaveBitmap(loading, Path.Combine(qa, "polish-skeleton-" + route.Trim('/').Replace('/', '-') + ".png"));
            }
            loading.Visible = false;
            // The same blocks in the dark theme: nothing new may fall back to light colours or lose its contrast.
            var themeBefore = prefs.Theme; prefs.Theme = ThemeChoice.Dark; ApplyTheme();
            try
            {
                using (var panel = new ToastPanel(layoutScale))
                {
                    workspace.Controls.Add(panel); var status = new UiStatus(); status.Update("downloadComplete");
                    panel.Render(status.Current, "Готово", "Файл сохранён в Загрузки", "Загрузки", "", "Закрыть"); panel.BringToFront(); await Task.Delay(400);
                    Check(panel.BackColor.ToArgb() == Theme.DarkPalette.Raised.ToArgb(), "the toast takes the dark theme's surface", results);
                    using (var picture = panel.RoundedPicture()) picture?.Save(Path.Combine(qa, "polish-dark-toast.png"), ImageFormat.Png);
                    Motion.KeyboardInput = true; panel.Hide(); Motion.KeyboardInput = false; workspace.Controls.Remove(panel);
                }
                loading.Zoom = 1; loading.SetRoute(SitePolicy.Origin + "/student/attendance/full/"); loading.Visible = true; loading.BringToFront(); loading.Refresh();
                SaveBitmap(loading, Path.Combine(qa, "polish-dark-skeleton.png")); loading.Visible = false;
                const string contrast = "var l=function(c){var v=(c.match(/\\d+(\\.\\d+)?/g)||[]).map(Number).slice(0,3).map(function(x){x/=255;return x<=0.04045?x/12.92:Math.pow((x+0.055)/1.055,2.4);});return .2126*v[0]+.7152*v[1]+.0722*v[2];},k=function(a,b){return (Math.max(a,b)+.05)/(Math.min(a,b)+.05);};";
                var tileState = "(function(){" + contrast + "var a=document.querySelector('.univer-app-quick .univer-app-tile>a'),t=a.querySelector('span:not(:empty)')||a,s=getComputedStyle(a),c=getComputedStyle(t).color;return [a.matches(':hover'),s.backgroundColor,s.borderTopColor,c,k(l(c),l(s.backgroundColor)).toFixed(2)].join('|');})()";
                SetPresentation(main, true); main.LoadHtml(bachelor, SitePolicy.Origin + "/student/bachelor/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(500);
                await AutomationController.EvaluateAsync(main, "window.__univerMotion=false;window.scrollTo(0,0);'top'");
                main.GetBrowser().GetHost().SendMouseMoveEvent(new MouseEvent(2, 2, CefEventFlags.None), false); await Task.Delay(300);
                var darkRest = (await AutomationController.EvaluateAsync(main, tileState) ?? "").Split('|');
                main.GetBrowser().GetHost().SendMouseMoveEvent(new MouseEvent(int.Parse(tileAt[0]), int.Parse(tileAt[1]), CefEventFlags.None), false); await Task.Delay(350);
                var darkHover = (await AutomationController.EvaluateAsync(main, tileState) ?? "").Split('|');
                QaTrace("qa-polish-dark-tile " + string.Join("|", darkRest) + " -> " + string.Join("|", darkHover));
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-dark-overview.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                Check(darkRest.Length == 5 && darkHover.Length == 5 && darkHover[0] == "true" && darkRest[1] != darkRest[2] && darkRest[1] != darkHover[1] &&
                    double.Parse(darkRest[4], System.Globalization.CultureInfo.InvariantCulture) >= 4.5 && double.Parse(darkHover[4], System.Globalization.CultureInfo.InvariantCulture) >= 4.5,
                    "a dark home tile has an outline, a highlight under the pointer and readable text (4.5:1)", results);
                main.GetBrowser().GetHost().SendMouseMoveEvent(new MouseEvent(2, 2, CefEventFlags.None), false);
                SetPresentation(main, true); main.LoadHtml(attendance, SitePolicy.Origin + "/student/attendance/full/2026/1"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(400);
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-course-list button:nth-child(1)').click();'all'"); await Task.Delay(350);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-dark-grades.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                await AutomationController.EvaluateAsync(main, "document.querySelector('.univer-app-attendance-course-list button:nth-child(2)').click();'course'"); await Task.Delay(350);
                await AutomationController.EvaluateAsync(main, "document.querySelectorAll('.univer-app-attendance-details-toggle[aria-expanded=false]').forEach(function(t){t.click();});'open'"); await Task.Delay(350);
                var darkSum = await AutomationController.EvaluateAsync(main, "(function(){" + contrast + "var t=Array.from(document.querySelectorAll('table.univer-app-matrix')).filter(function(n){return n.offsetWidth>0;})[0];if(!t)return 'none';var s=getComputedStyle(t.querySelector('td.univer-app-sum'));return [s.backgroundColor,s.color,k(l(s.color),l(s.backgroundColor)).toFixed(2)].join('|');})()");
                QaTrace("qa-polish-dark-sum " + darkSum);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-dark-journal.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                var sumParts = (darkSum ?? "").Split('|');
                Check(sumParts.Length == 3 && sumParts[0] != "rgba(0, 0, 0, 0)" && double.Parse(sumParts[2], System.Globalization.CultureInfo.InvariantCulture) >= 4.5, "the journal's total stays filled and readable in the dark theme (4.5:1)", results);
                // Letter grades: a soft dark tile near the card (luminance under 0.04, channels within 40 of each other) and light ink.
                SetPresentation(main, true); main.LoadHtml(AttestationFixture(false), SitePolicy.Origin + "/student/attestation/"); await WaitQaPresentationAsync(); await ApplyPresentationAsync(); await Task.Delay(400);
                var letters = await AutomationController.EvaluateAsync(main, "(function(){" + contrast + "return Array.from(document.querySelectorAll('#l4 .univer-app-grade')).map(function(b){var s=getComputedStyle(b),v=s.backgroundColor.match(/\\d+/g).slice(0,3).map(Number);" +
                    "return b.dataset.band+':'+(s.backgroundColor!=='rgba(0, 0, 0, 0)'&&l(s.backgroundColor)<0.04&&Math.max.apply(null,v)-Math.min.apply(null,v)<=40)+':'+k(l(s.color),l(s.backgroundColor)).toFixed(2);}).join('|');})()") ?? "";
                QaTrace("qa-polish-dark-letters " + letters);
                using (var client = main.GetDevToolsClient()) File.WriteAllBytes(Path.Combine(qa, "polish-dark-letters.png"), (await client.Page.CaptureScreenshotAsync()).Data);
                var letterParts = letters.Split('|').Select(x => x.Split(':')).ToList();
                Check(letterParts.Count == 5 && letterParts.All(x => x.Length == 3 && x[1] == "true" && double.Parse(x[2], System.Globalization.CultureInfo.InvariantCulture) >= 4.5),
                    "letter grades in the dark theme are soft dark tiles with readable ink (" + letters + ")", results);
            }
            finally { prefs.Theme = themeBefore; ApplyTheme(false); }
            prefs.Pins = new List<PinnedLink>(); SetZoom(started); await Task.Delay(150);
        }
    }
}
