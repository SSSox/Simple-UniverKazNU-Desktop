using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // Notifications from notices.js: the bell, the panel, a toast or tray balloon. Each shown page is read; sections and
    // counted lists at most every ten minutes. Only automatic sign-in reads them, and a session change empties the list.
    internal sealed partial class MainForm
    {
        private const long NoticeFetchInterval = 10 * 60 * 1000;
        private readonly ModernButton noticesButton = new ModernButton();
        private NoticesPanel noticesPanel;
        private List<SiteNotice> notices = new List<SiteNotice>();
        private readonly Stopwatch noticeClock = Stopwatch.StartNew();
        // A requested check that never reports back counts as failed.
        private readonly Timer noticeChecking = new Timer { Interval = 20000 };
        private long noticeFetchedAt = long.MinValue, noticeTriedAt = long.MinValue;
        private bool noticeScanDue = true, noticeBusy, noticeRefreshing, noticeRefreshQueued, noticeFailed, noticesClearPending, noticeBalloon;
        private string noticeAnnouncement = "";
        private int trayUnread = -1;
        private bool NoticesActive { get { return (credentials != null && !prefs.AutoLoginPaused && !guard.Stopped && !selfTest && !accountReview) || noticesQa; } }
        private bool NoticesShown { get { return noticesPanel != null && noticesPanel.Visible; } }
        private void BuildNotices()
        {
            noticesButton.Compact = true; noticesButton.Size = new Size(36, 36); noticesButton.Icon = UiIcon.Bell;
            toolbar.Controls.Add(noticesButton);
            noticesButton.Click += (s, e) => { if (NoticesShown) HideNotices(); else ShowNotices(); };
            noticesPanel = new NoticesPanel(layoutScale);
            workspace.Controls.Add(noticesPanel); noticesPanel.BringToFront();
            noticesPanel.OpenNotice += OpenNotice;
            noticesPanel.ReadNotice += key => QueueAsync(() => ChangeNoticesAsync(list => Notices.MarkRead(list, key)));
            noticesPanel.ReadAll += () => QueueAsync(() => ChangeNoticesAsync(Notices.MarkAllRead));
            noticesPanel.CheckNow += CheckNoticesNow;
            noticesPanel.CloseRequested += HideNotices;
            noticesPanel.VisibleChanged += (s, e) => noticesButton.Selected = noticesPanel.Visible;
            workspace.Resize += (s, e) => PlaceNotices(); canvas.LocationChanged += (s, e) => PlaceNotices();
            noticeChecking.Tick += (s, e) => EndNoticeCheck(true);
            if (credentials != null && !prefs.AutoLoginPaused && !selfTest && !accountReview) notices = storage.LoadNotices();
        }
        // The toolbar bell, the tray item and an open panel follow the list.
        private void RenderNotices()
        {
            if (closing || noticesPanel == null) return;
            var showing = NoticesActive ? Notices.Showing(notices) : new List<SiteNotice>();
            var unread = showing.Count(n => !n.Read);
            noticesButton.Badge = unread == 0 ? "" : unread > 99 ? "99+" : unread.ToString();
            var label = unread > 0 ? string.Format(Strings.Get("noticesBadge", prefs.Language), unread) : Strings.Get("notices", prefs.Language);
            noticesButton.AccessibleName = label; noticesButton.AccessibleDescription = Strings.Get("noticesHint", prefs.Language);
            tips.SetToolTip(noticesButton, label + "\n" + Strings.Get("noticesHint", prefs.Language));
            noticesPanel.Render(showing, prefs.Language, noticeRefreshing, !NoticesActive, noticeFailed);
            PlaceNotices();
            if (unread != trayUnread) { trayUnread = unread; LocalizeTray(); }
        }
        private void PlaceNotices()
        {
            if (noticesPanel == null || !noticesPanel.Visible) return;
            // Below the toolbar and any message banner: the canvas starts where both end.
            var top = canvas.Top + D(8);
            var width = Math.Min(D(440), workspace.ClientSize.Width - D(24));
            var height = Math.Max(D(120), Math.Min(noticesPanel.PreferredHeight(width), workspace.ClientSize.Height - top - D(12)));
            noticesPanel.SetBounds(workspace.ClientSize.Width - width - D(12), top, width, height);
        }
        private void ShowNotices()
        {
            if (closing || noticesPanel == null) return;
            findBar.Close();
            noticesPanel.Visible = true; RenderNotices(); noticesPanel.BringToFront();
            MotionSurface.Reveal(noticesPanel);
            noticesPanel.FocusFirst();
            // An announcement is answered by opening the list.
            if (messages.Shows("noticesNew")) { messages.ClearNotices(); RenderFeedback(); }
        }
        private void HideNotices()
        {
            if (!NoticesShown) return;
            var focused = noticesPanel.ContainsFocus; noticesPanel.Visible = false;
            if (focused) { if (Active != null && !Active.IsDisposed && Active.Visible) Active.Focus(); else noticesButton.Focus(); }
        }
        // Opens the page that resolves the notice (the questionnaire list, the section with the message) and marks it read.
        private void OpenNotice(SiteNotice notice)
        {
            if (closing || commandBusy || rotating || main == null || !SitePolicy.Trusted(notice.Url)) return;
            HideNotices();
            QueueAsync(() => ChangeNoticesAsync(list => Notices.MarkRead(list, notice.Key)));
            CloseDocument(); HideDownloads();
            if (!(main.IsLoading && SitePolicy.SamePage(lastGet, notice.Url))) main.Load(SitePolicy.NewsShortcut(notice.Url) ?? notice.Url);
        }
        private void CheckNoticesNow()
        {
            if (!NoticesActive || noticeRefreshing) return;
            noticeRefreshing = true; noticeFailed = false; noticeChecking.Stop(); noticeChecking.Start(); RenderNotices();
            QueueAsync(() => ScanNoticesAsync(true));
        }
        private void EndNoticeCheck(bool failed)
        {
            noticeChecking.Stop(); noticeRefreshing = false; noticeFailed = failed; RenderNotices();
        }
        // Called on every tick that shows a signed-in page: the site window's page is read once after it loads, and again
        // when the section fetch is due (at most once a minute while a page offers nothing to fetch).
        private void NoticeContent()
        {
            if (!NoticesActive || noticeBusy || document != null || Active != main) return;
            var now = noticeClock.ElapsedMilliseconds;
            var fetchDue = (noticeFetchedAt == long.MinValue || now - noticeFetchedAt >= NoticeFetchInterval - 30000) &&
                (noticeTriedAt == long.MinValue || now - noticeTriedAt >= 60000);
            if (!noticeScanDue && !fetchDue) return;
            QueueAsync(() => ScanNoticesAsync(false));
        }
        // Letters and digits only, so it is written into the script as it is.
        private string NoticeGeneration { get { return "n" + sessionEpoch; } }
        private async Task ScanNoticesAsync(bool refresh)
        {
            var browser = main;
            if (noticeBusy) { noticeRefreshQueued |= refresh; return; }
            // The login form and the read notice say nothing about the account's marks.
            if (closing || rotating || browser == null || document != null || !NoticesActive || !SitePolicy.Trusted(browser.Address) ||
                SitePolicy.LoginPage(browser.Address) || new Uri(browser.Address).AbsolutePath.StartsWith("/student/attention", StringComparison.OrdinalIgnoreCase))
            { if (refresh && noticeRefreshing) EndNoticeCheck(true); return; }
            noticeBusy = true; noticeScanDue = false;
            var outcome = refresh ? "failed" : null;
            try
            {
                var now = noticeClock.ElapsedMilliseconds; noticeTriedAt = now;
                var fetch = refresh || noticeFetchedAt == long.MinValue || now - noticeFetchedAt >= NoticeFetchInterval - 30000;
                var generation = NoticeGeneration; var address = browser.Address;
                string raw;
                try { raw = await AutomationController.EvaluateAsync(browser, "window.__univerNoticesV1?window.__univerNoticesV1.scan(" + (fetch ? "true" : "false") + ",'" + generation + "'):null"); }
                catch (Exception) { return; } // A navigation can end the frame; the next page is read again.
                if (closing || browser != main || browser.Address != address || generation != NoticeGeneration) return;
                var scan = Notices.Parse(raw, false, generation);
                if (scan == null) { noticeScanDue = true; return; }
                // The interval starts when the page really began fetching (it had sections or lists to read).
                if (fetch && scan.Fetching) noticeFetchedAt = now;
                // A requested check ends with the page read, or with the background read's last message.
                if (refresh) outcome = scan.Fetching ? "fetching" : "done";
                await ApplyNoticeScanAsync(scan, generation);
            }
            finally
            {
                noticeBusy = false;
                if (outcome != null && outcome != "fetching" && noticeRefreshing) EndNoticeCheck(outcome == "failed");
                if (noticeRefreshQueued) { noticeRefreshQueued = false; QueueAsync(() => ScanNoticesAsync(true)); }
            }
        }
        // A section or list read sent by the page while it fetched in the background.
        private void ReceiveNoticeMessage(CefSharp.WinForms.ChromiumWebBrowser browser, bool viewer, string value, bool trusted, bool mainFrame)
        {
            if (browser != main || viewer || !trusted || !mainFrame) return;
            var message = value.Substring(Notices.MessagePrefix.Length);
            var scan = Notices.Parse(message, true, NoticeGeneration);
            if (scan != null) { QueueAsync(() => ApplyNoticeScanAsync(scan, NoticeGeneration)); return; }
            int read, failed;
            if (!Notices.ParseDone(message, NoticeGeneration, out read, out failed)) return;
            // Any read of the sections, requested or periodic, says whether the list is current.
            if (noticeRefreshing) EndNoticeCheck(failed > 0); else if (noticeFailed != failed > 0) { noticeFailed = failed > 0; RenderNotices(); }
        }
        private async Task ApplyNoticeScanAsync(NoticeScan scan, string generation)
        {
            if (closing || generation != NoticeGeneration || !NoticesActive) return;
            var previous = notices;
            var next = Notices.Merge(previous, scan, Notices.Now());
            if (!Changed(previous, next) && !noticesClearPending) return;
            notices = next; RenderNotices();
            var fresh = Notices.NewlyUnread(previous, next);
            if (fresh.Count > 0) AnnounceNotices(fresh);
            await SaveNoticesAsync();
        }
        private async Task ChangeNoticesAsync(Func<List<SiteNotice>, List<SiteNotice>> change)
        {
            if (closing) return;
            var next = change(notices);
            if (!Changed(notices, next) && !noticesClearPending) return;
            notices = next; RenderNotices();
            await SaveNoticesAsync();
        }
        private async Task SaveNoticesAsync()
        {
            try { await storage.SaveNoticesAsync(notices); noticesClearPending = false; }
            // The list stays in memory and the next change writes it again; a failed write never blocks the site.
            catch (Exception) { }
        }
        private static bool Changed(List<SiteNotice> before, List<SiteNotice> after)
        {
            if (before.Count != after.Count) return true;
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            return serializer.Serialize(before) != serializer.Serialize(after);
        }
        // A session change: the list belonged to the account that was signed in. A failed delete is retried with the
        // next write, and the list is never shown without an automatic sign-in.
        private async Task ClearNoticesAsync()
        {
            notices = new List<SiteNotice>(); noticeFetchedAt = long.MinValue; noticeScanDue = true; noticeRefreshing = noticeRefreshQueued = noticeFailed = false; noticeChecking.Stop();
            HideNotices(); messages.ClearNotices();
            RenderNotices();
            try { await storage.SaveNoticesAsync(notices); noticesClearPending = false; }
            catch (Exception) { noticesClearPending = true; }
        }
        // New notifications: a toast while the window is in front, a tray balloon while it is hidden or minimized.
        private void AnnounceNotices(List<SiteNotice> fresh)
        {
            if (NoticesShown) return;
            var lang = prefs.Language;
            noticeAnnouncement = fresh.Count == 1 ? Heading(fresh[0]) : string.Format(Strings.Get("noticesNewMany", lang), fresh.Count);
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                if (trayIcon == null) return;
                noticeBalloon = true;
                trayIcon.ShowBalloonTip(8000, Strings.Get("noticesNew", lang), noticeAnnouncement, ToolTipIcon.Info);
                return;
            }
            SetStatus("noticesNew");
        }
        private static string Heading(SiteNotice notice) { return notice.Kind == SiteNotice.Blocked ? notice.Title : notice.Title + " (" + Notices.Shown(notice) + ")"; }
    }
}
