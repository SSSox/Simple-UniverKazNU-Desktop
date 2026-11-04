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
        // Lets the self-test read notifications without a saved sign-in; nothing else turns it on.
        private bool noticesQa;
        // The menu as observed on 2 November 2026: the count in a red <b> after the link, the list in table.inner rows.
        private const string NoticeQaCount = "<b style='color:Red; font-size:10px;'>&nbsp;(2)</b>";
        private static string NoticeQaPage(string content, string count)
        {
            return "<table class='wrappertable'><tr id='l1'><td class='header'>КазНУ</td></tr><tr id='l2'><td></td></tr>" +
                "<tr id='l3'><td class='center'><a href='/student/bachelor/'><span class='center'>Бакалавр</span></a><a href='/ssc/'><span class='center'>Е-услуги</span></a></td></tr>" +
                "<tr id='l4'><td class='center'><table id='tools'><tr><td class='center'><table class='tool'><tr><td><ul>" +
                "<li><a href='/student/rating/'>Анкетирование студентов</a>" + count + "</li><li><a href='/student/umkd/'>УМКД</a></li></ul></td></tr></table></td></tr></table>" +
                content + "</td></tr></table>";
        }
        private static string NoticeQaList(string first, string second)
        {
            return "<table class='inner'><tr><th>Название</th><th>Тип</th><th>Начало</th><th>Окончание</th><th>Статус</th></tr>" +
                "<tr class='link' id='208'><td>Удовлетворенность обучающихся качеством преподавания</td><td>Анкетирование</td><td>30.10.2026</td><td>30.11.2026</td><td>" + first + "</td></tr>" +
                "<tr class='link' id='207'><td>Восприятие коррупции - 2026, осенний сем.</td><td>Анкетирование</td><td>29.10.2026</td><td>11.11.2026</td><td>" + second + "</td></tr></table>";
        }
        // Native checks: real script and message channel, answered questionnaires hidden, badge, toast, panel and its buttons,
        // a failed check, read marks that survive, the dark theme at the smallest window, three languages and the saved list.
        private async Task NoticesQaAsync(string qa, List<string> results)
        {
            ResourceRequestHandlerFactory factory = null;
            var routes = new[] { "/ssc/", "/student/rating/" };
            noticesQa = true; await ClearNoticesAsync();
            try
            {
                // LoadHtml creates the browser's handler factory; the background reads below are served by it, never by the site.
                main.LoadHtml(NoticeQaPage("<p>Бакалавр</p>", NoticeQaCount), SitePolicy.Origin + "/student/bachelor/"); await WaitQaPresentationAsync();
                factory = main.ResourceRequestHandlerFactory as ResourceRequestHandlerFactory;
                if (factory == null) throw new InvalidOperationException("QA pages cannot be served");
                Action<string, string> page = (route, html) => factory.RegisterHandler(SitePolicy.Origin + route, ResourceHandler.GetByteArray("<!doctype html><meta charset='utf-8'>" + html, Encoding.UTF8), "text/html", false);
                page("/ssc/", NoticeQaPage("<p>Е-услуги</p>", ""));
                page("/student/rating/", NoticeQaPage(NoticeQaList("+", "+"), NoticeQaCount));
                // The page alone first: only the menu count is known.
                noticeFetchedAt = noticeClock.ElapsedMilliseconds; await ScanNoticesAsync(false);
                Check(Notices.Showing(notices).Count == 1 && Notices.Unread(notices) == 1 && noticesButton.Badge == "1" &&
                    noticesButton.Right <= find.Left && pageTitle.Right <= noticesButton.Left, "the menu count is read from the page and shown on the bell, beside find and the title", results);
                await ScanNoticesAsync(true);
                var until = DateTime.UtcNow.AddSeconds(10);
                while (Notices.Showing(notices).Count > 0 && DateTime.UtcNow < until) await Task.Delay(100);
                Check(Notices.Showing(notices).Count == 0 && noticesButton.Badge == "" && notices.Any(n => n.Open == 0 && n.Count == 2),
                    "the background read of the questionnaire list sees both rows answered with + and hides the site's (2)", results);
                messages.ClearNotices(); RenderFeedback();
                await ScanNoticesAsync(false);
                Check(Notices.Showing(notices).Count == 0 && !messages.Shows("noticesNew"), "a later page with the same (2) keeps the answered questionnaires hidden and silent", results);
                var scan = new NoticeScan { Page = "/student/bachelor/", Menu = true };
                scan.Links.Add(new NoticeLink { Url = SitePolicy.Origin + "/student/rating/", Title = "Анкетирование студентов", Count = 3, Open = 1, Menu = true,
                    Details = new List<NoticeDetail> { new NoticeDetail { Title = "Новая синтетическая анкета", Until = "20.11.2026" } } });
                await ApplyNoticeScanAsync(scan, NoticeGeneration);
                Check(messages.Current?.Key == "noticesNew" && toast.Shown && noticeAnnouncement == "Анкетирование студентов (1)" && noticesButton.Badge == "1",
                    "a new questionnaire is announced in a toast and counted on the bell by its open rows", results);
                QaSnapshot(Path.Combine(qa, "notices-toolbar.png"), toolbar.Width, toolbar.Height, bitmap => toolbar.DrawToBitmap(bitmap, new Rectangle(Point.Empty, toolbar.Size)));
                QaSnapshot(Path.Combine(qa, "notices-toast.png"), toast.Width, toast.Height, bitmap => toast.DrawToBitmap(bitmap, new Rectangle(Point.Empty, toast.Size)));
                toast.Primary.PerformClick(); await Task.Delay(100);
                Check(NoticesShown && noticesPanel.RowsForQa == 1 && !messages.Shows("noticesNew") && workspace.ClientRectangle.Contains(noticesPanel.Bounds) && noticesPanel.Top >= canvas.Top &&
                    noticesPanel.RowNamesForQa.Single() == "Анкетирование студентов (1)", "Show opens the panel below the toolbar with the open questionnaire", results);
                QaSnapshot(Path.Combine(qa, "notices-panel.png"), noticesPanel.Width, noticesPanel.Height, bitmap => noticesPanel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, noticesPanel.Size)));
                noticesPanel.ReadFirstForQa(); await Task.Delay(200);
                Check(Notices.Unread(notices) == 0 && noticesButton.Badge == "" && NoticesShown, "the check mark marks it read and the badge goes away", results);
                var empty = new NoticeScan { Page = "/student/umkd/", Menu = true };
                empty.Links.Add(new NoticeLink { Url = SitePolicy.Origin + "/student/rating/", Title = "Анкетирование студентов", Menu = true });
                await ApplyNoticeScanAsync(empty, NoticeGeneration);
                var hidden = Notices.Showing(notices).Count == 0;
                await ApplyNoticeScanAsync(scan, NoticeGeneration);
                Check(hidden && Notices.Showing(notices).Single().Read && !messages.Shows("noticesNew"), "a read notification that goes and comes back stays read and is not announced again", results);
                factory.RegisterHandler(SitePolicy.Origin + "/ssc/", ResourceHandler.GetByteArray("not a page", Encoding.UTF8), "text/plain", false);
                CheckNoticesNow();
                until = DateTime.UtcNow.AddSeconds(25);
                while (noticeRefreshing && DateTime.UtcNow < until) await Task.Delay(100);
                Check(!noticeRefreshing && noticeFailed && noticesPanel.StatusForQa == Strings.Get("noticesCheckFailed", "ru"),
                    "a check that cannot read a section says so in the panel instead of silently ending", results);
                QaSnapshot(Path.Combine(qa, "notices-failed.png"), noticesPanel.Width, noticesPanel.Height, bitmap => noticesPanel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, noticesPanel.Size)));
                page("/ssc/", NoticeQaPage("<p>Е-услуги</p>", ""));
                CheckNoticesNow();
                until = DateTime.UtcNow.AddSeconds(25);
                while (noticeRefreshing && DateTime.UtcNow < until) await Task.Delay(100);
                Check(!noticeRefreshing && !noticeFailed && noticesPanel.StatusForQa == "", "the next complete check clears the failure", results);
                var more = new NoticeScan { Page = "/student/bachelor/", Menu = true };
                more.Links.Add(new NoticeLink { Url = SitePolicy.Origin + "/student/rating/", Title = "Анкетирование студентов", Count = 4, Open = 2, Menu = true });
                await ApplyNoticeScanAsync(more, NoticeGeneration); messages.ClearNotices(); RenderFeedback();
                var unreadBefore = Notices.Unread(notices);
                noticesPanel.ReadAllForQa(); await Task.Delay(200);
                Check(unreadBefore == 1 && Notices.Unread(notices) == 0 && Notices.Showing(notices).Count == 1, "Mark all as read marks every shown notification read", results);
                var size = Size;
                prefs.Theme = ThemeChoice.Dark; ApplyTheme(); Size = MinimumSize; await Task.Delay(300); PlaceNotices();
                var rowColors = noticesPanel.RowColorsForQa;
                Check(Theme.Dark && NoticesShown && noticesPanel.BackColor == Theme.Surface && rowColors.Length == 1 && rowColors.All(c => c == Theme.Surface || c == Theme.Current.InfoFill) &&
                    workspace.ClientRectangle.Contains(noticesPanel.Bounds) && noticesPanel.Top >= canvas.Top && pageTitle.Right <= noticesButton.Left && noticesButton.Right <= find.Left,
                    "dark theme at the smallest window: the panel takes the theme's colours and fits below the toolbar", results);
                QaSnapshot(Path.Combine(qa, "notices-panel-dark.png"), noticesPanel.Width, noticesPanel.Height, bitmap => noticesPanel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, noticesPanel.Size)));
                QaSnapshot(Path.Combine(qa, "notices-toolbar-dark.png"), toolbar.Width, toolbar.Height, bitmap => toolbar.DrawToBitmap(bitmap, new Rectangle(Point.Empty, toolbar.Size)));
                noticesPanel.CloseForQa(); await Task.Delay(100);
                Check(!NoticesShown, "the close button hides the panel", results);
                var newer = new NoticeScan { Page = "/student/bachelor/", Menu = true };
                newer.Links.Add(new NoticeLink { Url = SitePolicy.Origin + "/student/rating/", Title = "Анкетирование студентов", Count = 5, Open = 3, Menu = true });
                await ApplyNoticeScanAsync(newer, NoticeGeneration);
                Check(toast.Shown && workspace.ClientRectangle.Contains(toast.Bounds) && toast.Primary.Right <= toast.Width, "the toast fits the smallest window in the dark theme", results);
                QaSnapshot(Path.Combine(qa, "notices-toast-dark.png"), toast.Width, toast.Height, bitmap => toast.DrawToBitmap(bitmap, new Rectangle(Point.Empty, toast.Size)));
                ShowNotices(); await Task.Delay(100);
                var escape = new Message(); ProcessCmdKey(ref escape, Keys.Escape);
                Check(!NoticesShown, "Escape closes the panel", results);
                prefs.Theme = ThemeChoice.Light; ApplyTheme(false); Size = size; await Task.Delay(150);
                var names = new List<string>();
                foreach (var language in new[] { "kz", "en", "ru" }) { prefs.Language = language; Localize(); names.Add(noticesButton.AccessibleName); }
                Check(names.SequenceEqual(new[] { "Хабарландырулар, оқылмаған: 1", "Notifications, unread: 1", "Уведомления, непрочитанных: 1" }), "the bell is named with its unread count in Kazakh, English and Russian", results);
                Check(storage.LoadNotices().Count == notices.Count, "the list is saved with the app's local data", results);
                await ClearNoticesAsync();
                Check(notices.Count == 0 && !File.Exists(Path.Combine(storage.Root, "notices.json")), "a session change empties the list and deletes its file", results);
            }
            finally
            {
                noticesQa = false; await ClearNoticesAsync(); messages.ClearNotices(); RenderFeedback();
                foreach (var route in routes) factory?.UnregisterHandler(SitePolicy.Origin + route);
            }
        }
    }
}
