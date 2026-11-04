using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm : Form
    {
        private readonly Storage storage;
        private Preferences prefs;
        private Credentials credentials;
        private readonly bool selfTest, liveSmoke, accountReview, reviewAll;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken stopping;
        private readonly UiStatus messages = new UiStatus();
        private readonly Panel canvas = new Panel(), sidebar = new SmoothPanel(), toolbar = new SmoothPanel();
        private readonly FeedbackPanel feedback = new FeedbackPanel();
        private ToastPanel toast;
        private readonly Panel workspace = new SmoothPanel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        private readonly LoadingView loading = new LoadingView();
        private readonly Panel pageCover = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };
        private readonly Label brand = new Label(), university = new Label(), localAccount = new Label(), pageTitle = new Label(), pageState = new Label(), zoomLabel = new Label();
        private readonly Label portalGroup = new Label(), appGroup = new Label();
        private int sidebarDivider;
        private readonly ModernButton menu = new ModernButton(), back = new ModernButton(), forward = new ModernButton(), refresh = new ModernButton(), mode = new ModernButton(), zoomIn = new ModernButton(), zoomOut = new ModernButton(), find = new ModernButton();
        private FindBar findBar;
        private readonly ModernButton sideHome = new ModernButton(), downloadsButton = new ModernButton(), sideSettings = new ModernButton(), website = new ModernButton();
        private readonly ModernButton scheduleButton = new ModernButton(), materialsButton = new ModernButton(), journalButton = new ModernButton(), moodleButton = new ModernButton();
        private readonly ToolTip tips = new ToolTip();
        private readonly ContextMenuStrip viewMenu = new ContextMenuStrip();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 };
        private ChromiumWebBrowser main, document;
        private RequestContext context;
        private DownloadService downloads;
        private AutomationController automation;
        private SessionGuard guard = new SessionGuard();
        private bool rotating, closing, error, storageFailed, credentialsUnreadable, settingsOpen, commandBusy, sidebarExpanded, manualExpanded;
        private string failureCode = "", lastGet = SitePolicy.Home, lastMethod = "GET";
        private string lastDownloadPath;
        private string sourceGet = SitePolicy.Home, sourceMethod = "GET";
        private int zoomPercent = 100, sessionEpoch;
        private Rectangle zoomGroup;
        private string siteTitle = "", siteSection = "";
        private readonly float layoutScale;
        private DateTime ignoreCefCloseUntil;
        private DateTime presentationBegan = DateTime.UtcNow;
        private bool recoveringPresentation;
        private int D(int value) { return (int)Math.Round(value * layoutScale); }
        // The sidebar follows the page zoom within a range that keeps the workspace usable.
        private float SidebarZoom { get { return Math.Max(0.8f, Math.Min(1.6f, zoomPercent / 100f)); } }
        private int SZ(int value) { return (int)Math.Round(value * layoutScale * SidebarZoom); }
        private ChromiumWebBrowser Active { get { return document ?? main; } }
        public MainForm(Storage storage, bool selfTest, bool liveSmoke, bool accountReview = false, bool reviewAll = false)
        {
            this.storage = storage; this.selfTest = selfTest; this.liveSmoke = liveSmoke; this.accountReview = accountReview; this.reviewAll = reviewAll; stopping = lifetime.Token;
            ctrlWheel = new CtrlWheel((delta, time) => Dispatch(() => WheelZoom(delta, time)));
            var stored = storage.Load(); prefs = stored.Preferences; credentials = stored.Credentials; storageFailed = stored.Failed; credentialsUnreadable = stored.CredentialsUnreadable;
            // A review that may only use the existing session: without credentials in memory an expired session stops at
            // the login page instead of signing in. Nothing is saved in review mode, so the stored record is untouched.
            if (accountReview && Array.IndexOf(Environment.GetCommandLineArgs(), "--review-no-login") >= 0) credentials = null;
            // Housekeeping of old profiles must never discard successfully loaded settings or credentials.
            if (!storageFailed && !accountReview) { try { storage.CleanOldProfiles(prefs.Profile); } catch (Exception) { } }
            // Automated checks read settled layouts, so they always run without motion.
            Motion.Enabled = prefs.Animations && !selfTest && !accountReview;
            Font = Theme.Font(10.5f); ForeColor = Theme.Ink; BackColor = Theme.Background;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) layoutScale = graphics.DpiX / 96f;
            Text = "Univer · КазНУ"; Size = new Size(1360, 860); MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterScreen;
#if LEGACY_ENGINE
            Text += " · Legacy";
#endif
            if (selfTest || accountReview) { Opacity = 0; ShowInTaskbar = false; }
            if (accountReview) prefs.Readable = true;
            if (!selfTest && !accountReview) zoomPercent = prefs.Zoom;
            Icon = LoadAppIcon() ?? Icon;
            BuildShell(); BuildTray(); CreateSession(); Localize(); HookTheme(); BuildUpdates();
            timer.Tick += async (s, e) =>
            {
                if (rotating || closing || commandBusy) return;
                ExpireResumedDownload(); RunRecovery();
                if (error) return;
                if (loading.Visible && DateTime.UtcNow - presentationBegan > TimeSpan.FromSeconds(45))
                {
                    // The handler awaits, so the 200 ms timer fires again before the cover is removed.
                    if (recoveringPresentation) return;
                    recoveringPresentation = true;
                    try
                    {
                        var recoverable = guard.Suspend();
                        await ApplyPresentationAsync();
                        SetPresentation(Active, false, true);
                        SetStatus(recoverable ? "presentationSlow" : "automationFailed");
                    }
                    finally { recoveringPresentation = false; }
                    return;
                }
                await automation.TickAsync(Active, credentials, prefs, document != null);
                UpdateHistoryButtons();
            };
            Resize += (s, e) => UpdatePollingRate(); VisibleChanged += (s, e) => UpdatePollingRate(); Activated += (s, e) => EnterUpdates();
            FormClosing += (s, e) =>
            {
                // Chromium 109 may post a delayed WM_CLOSE while a child browser is disposed.
                // User Close/Alt+F4 and Windows shutdown keep their normal close reasons.
                if (e.CloseReason == CloseReason.TaskManagerClosing && (rotating || DateTime.UtcNow < ignoreCefCloseUntil)) { e.Cancel = true; return; }
                if (HideToTray(e)) return;
                closing = true; lifetime.Cancel(); timer.Stop(); automation?.Navigating();
                RememberWindow();
            };
            // The network coming back or Windows waking up runs a pending automatic recovery at once.
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged += PowerModeChanged;
            // A Moodle hand-off page must not outlive the app, even when it exits before the page's own 30-second cleanup.
            FormClosed += (s, e) =>
            {
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
                Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerModeChanged;
                ctrlWheel.Dispose(); DisposeTray(); updates?.Dispose(); downloads?.Dispose(); CloseBrowsers(); context?.Dispose(); Web.SingleSignOn.CleanUp(TimeSpan.Zero);
            };
            var restored = false;
            // Restore before the first paint so the window does not jump after appearing.
            Load += (s, e) => { if (!selfTest && !accountReview) restored = RestoreWindow(); };
            Shown += async (s, e) =>
            {
                if (!restored)
                {
                    var area = Screen.FromControl(this).WorkingArea;
                    MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 24), Math.Min(MinimumSize.Height, area.Height - 24));
                    Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                    Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2;
                }
                if (selfTest) { await SelfTestAsync(); return; }
                if (accountReview) { await ReviewAccountAsync(); return; }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-transitions") >= 0) { QueueAsync(TransitionQaAsync); return; }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-keys") >= 0) { QueueAsync(KeysQaAsync); return; }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-study") >= 0) { QueueAsync(StudyQaAsync); return; }
#if LEGACY_ENGINE
                ShowLegacyWarning();
                if (closing) return;
#endif
                if (storageFailed) SetStatus("storageError");
                else if (credentialsUnreadable) SetStatus("credentialsUnreadable");
                else if (credentials == null && !prefs.AutoLoginPaused) await OpenSettingsAsync(true);
                updatesReady = true; EnterUpdates();
            };
        }
        private void BuildShell()
        {
            sidebar.Dock = DockStyle.Left; sidebar.Width = 232; sidebar.BackColor = Theme.Surface;
            brand.Text = "Univer"; brand.Font = Theme.Strong(16); brand.ForeColor = Theme.Ink; brand.AutoEllipsis = true;
            university.Font = Theme.Font(Theme.Caption); university.ForeColor = Theme.Muted; university.AutoEllipsis = false;
            localAccount.ForeColor = Theme.Muted; localAccount.Font = Theme.Font(Theme.Caption); localAccount.TextAlign = ContentAlignment.MiddleLeft; localAccount.AutoEllipsis = true;
            // Group captions separate the portal shortcuts from local app tools when the labels are shown.
            foreach (var group in new[] { portalGroup, appGroup }) { group.Font = Theme.Strong(Theme.Caption); group.ForeColor = Theme.Muted; group.AutoEllipsis = true; group.UseMnemonic = false; sidebar.Controls.Add(group); }
            foreach (var button in new[] { sideHome, scheduleButton, journalButton, materialsButton, moodleButton, downloadsButton, sideSettings, website }) { button.Look = ButtonLook.Navigation; button.Height = 40; sidebar.Controls.Add(button); }
            sideHome.Icon = UiIcon.Home; sideHome.Selected = true; downloadsButton.Icon = UiIcon.Downloads; sideSettings.Icon = UiIcon.Settings; website.Icon = UiIcon.External;
            scheduleButton.Icon = UiIcon.Calendar; materialsButton.Icon = UiIcon.Book; journalButton.Icon = UiIcon.Journal; moodleButton.Icon = UiIcon.Cap;
            sidebar.Controls.Add(brand); sidebar.Controls.Add(university); sidebar.Controls.Add(localAccount);
            sidebar.Paint += (s, e) => PaintSidebar(e.Graphics);
            tips.ShowAlways = true; Theme.Tips(tips);
            toolbar.Dock = DockStyle.Top; toolbar.Height = 52; toolbar.BackColor = Theme.Surface;
            toolbar.Paint += (s, e) => PaintToolbar(e.Graphics);
            canvas.Dock = DockStyle.Fill; canvas.BackColor = Theme.Background;
            canvas.Controls.Add(loading); canvas.Controls.Add(pageCover);
            BuildDownloads();
            foreach (var button in new[] { menu, back, forward, refresh, find, mode, zoomIn, zoomOut }) { button.Compact = true; button.Size = new Size(36, 36); toolbar.Controls.Add(button); }
            menu.Icon = UiIcon.Menu; back.Icon = UiIcon.Back; forward.Icon = UiIcon.Forward; refresh.Icon = UiIcon.Reload; find.Icon = UiIcon.Search;
            mode.Icon = UiIcon.Eye; mode.Compact = false; mode.Look = ButtonLook.Outline; mode.Font = Theme.Font(Theme.Body); zoomIn.Icon = UiIcon.Plus; zoomOut.Icon = UiIcon.Minus;
            pageTitle.Font = Theme.Strong(Theme.Title); pageTitle.AutoEllipsis = true; pageTitle.UseMnemonic = false;
            pageState.Font = Theme.Font(Theme.Meta); pageState.ForeColor = Theme.Muted; pageState.AutoEllipsis = true; pageState.UseMnemonic = false;
            zoomLabel.Font = Theme.Font(Theme.Meta); zoomLabel.TextAlign = ContentAlignment.MiddleCenter; zoomLabel.ForeColor = Theme.Ink;
            zoomLabel.Cursor = Cursors.Hand; zoomLabel.Click += (s, e) => SetZoom(100);
            toolbar.Controls.Add(pageTitle); toolbar.Controls.Add(pageState); toolbar.Controls.Add(zoomLabel);
            Motion.FadeIn(viewMenu);
            viewMenu.Font = Theme.Font(Theme.Body); viewMenu.ShowImageMargin = false; viewMenu.ShowCheckMargin = true; Theme.Style(viewMenu);
            workspace.Controls.Add(canvas); workspace.Controls.Add(feedback); workspace.Controls.Add(toolbar);
            toast = new ToastPanel(layoutScale); workspace.Controls.Add(toast); toast.BringToFront();
            findBar = new FindBar(layoutScale); workspace.Controls.Add(findBar); findBar.BringToFront();
            workspace.Resize += (s, e) => { toast.Place(); PlaceFind(); }; canvas.LocationChanged += (s, e) => PlaceFind();
            findBar.VisibleChanged += (s, e) => { find.Selected = findBar.Visible; PlaceFind(); };
            BuildNotices();
            find.Click += (s, e) => OpenFind();
            toast.Expired += () => { if (messages.Current?.Key == toast.Key) { messages.Dismiss(); RenderFeedback(); } };
            Controls.Add(workspace); Controls.Add(sidebar);
            Resize += (s, e) => ArrangeShell(); toolbar.Resize += (s, e) => ArrangeToolbar(); sidebar.Resize += (s, e) => ArrangeSidebar();
            menu.Click += async (s, e) => await ToggleSidebarAsync();
            back.Click += (s, e) => Back(); forward.Click += (s, e) => { if (Active?.CanGoForward == true) Active.Forward(); };
            refresh.Click += (s, e) => RefreshCurrent(); sideHome.Click += (s, e) => GoHome();
            scheduleButton.Click += (s, e) => NavigateSection("/student/myschedule/");
            materialsButton.Click += (s, e) => NavigateSection("/student/umkd/");
            journalButton.Click += (s, e) => NavigateSection("/student/attendance/full/");
            // Univer's own Moodle sign-in page; the hand-off to Moodle then opens in the user's browser.
            moodleButton.Click += (s, e) => NavigateSection("/user/loginMoodle");
            downloadsButton.Click += (s, e) => ShowDownloads(); website.Click += (s, e) => External(SitePolicy.Trusted(Active?.Address) ? Active.Address : SitePolicy.Home);
            sideSettings.Click += async (s, e) => await OpenSettingsAsync();
            mode.Click += (s, e) => viewMenu.Show(mode, new Point(0, mode.Height));
            zoomIn.Click += (s, e) => SetZoom(zoomPercent + 10); zoomOut.Click += (s, e) => SetZoom(zoomPercent - 10);
            feedback.Primary.Click += async (s, e) => await FeedbackPrimaryAsync(); toast.Primary.Click += async (s, e) => await FeedbackPrimaryAsync();
            feedback.Secondary.Click += (s, e) => FeedbackSecondary(); toast.Secondary.Click += (s, e) => FeedbackSecondary();
            feedback.Dismiss.Click += (s, e) => DismissFeedback(); toast.Dismiss.Click += (s, e) => DismissFeedback();
        }
        private async Task FeedbackPrimaryAsync()
        {
            var message = messages.Current; if (message == null) return;
            if (message.Action == MessageAction.Settings) await OpenSettingsAsync();
            else if (message.Action == MessageAction.Retry) Retry();
            else if (message.Key == "downloadDuplicate") OpenDuplicate();
            else if (message.Action == MessageAction.Downloads) ShowDownloads(message.Key == "downloadComplete" ? LastCompletedDownload() : null);
            else if (message.Action == MessageAction.Notices) ShowNotices();
        }
        private void FeedbackSecondary()
        {
            var key = messages.Current?.Key;
            if (key == "downloadComplete") ShowDownloads(); else if (key == "downloadDuplicate") DownloadAgain(); else Retry();
        }
        private void DismissFeedback() { if (messages.Current?.Key == "downloadDuplicate") duplicate = null; messages.Dismiss(); RenderFeedback(); }
        private void ArrangeShell()
        {
            var expand = prefs.Sidebar && (ClientSize.Width >= D(1060) || manualExpanded);
            var target = SZ(expand ? 232 : 68); toolbar.Height = D(52);
            // Resize Chromium once; animating width forces a complete website reflow on every frame.
            sidebarExpanded = expand; sidebar.Width = target;
            ArrangeSidebar(); ArrangeToolbar(); sidebar.Invalidate();
            // The toolbar button names the action it performs now: show the names or fold the panel back to icons.
            SetButtonLabel(menu, expand ? "collapseSidebar" : "expandSidebar"); UpdateSidebarTips();
        }
        private void ArrangeSidebar()
        {
            // Expanded labels and the compact rail share the same row positions.
            var full = SZ(sidebarExpanded ? 232 : 68);
            brand.SetBounds(SZ(62), SZ(16), full - SZ(70), SZ(26)); brand.Visible = sidebarExpanded;
            university.SetBounds(SZ(62), SZ(42), full - SZ(70), SZ(36)); university.Visible = sidebarExpanded;
            portalGroup.Visible = appGroup.Visible = sidebarExpanded;
            // Group captions keep their rows in the icon rail too.
            var width = full - SZ(24); var y = SZ(84);
            portalGroup.SetBounds(SZ(24), y, width - SZ(12), SZ(20)); y += SZ(24);
            foreach (var button in new[] { sideHome, scheduleButton, journalButton, materialsButton, moodleButton })
            { button.SetBounds(SZ(12), y, width, SZ(40)); y += SZ(44); }
            sidebarDivider = y + SZ(8); y = sidebarDivider + SZ(9);
            appGroup.SetBounds(SZ(24), y, width - SZ(12), SZ(20)); y += SZ(24);
            downloadsButton.SetBounds(SZ(12), y, width, SZ(40));
            sideSettings.SetBounds(SZ(12), Math.Max(downloadsButton.Bottom + SZ(15), sidebar.Height - SZ(140)), width, SZ(40));
            website.SetBounds(SZ(12), Math.Max(sideSettings.Bottom + SZ(4), sidebar.Height - SZ(96)), width, SZ(40));
            localAccount.SetBounds(SZ(34), sidebar.Height - SZ(44), full - SZ(44), SZ(28)); localAccount.Visible = sidebarExpanded;
            foreach (var button in new[] { sideHome, scheduleButton, materialsButton, journalButton, moodleButton, downloadsButton, sideSettings, website }) { button.Compact = !sidebarExpanded; button.Invalidate(); }
        }
        private void UpdateSidebarTips()
        {
            // Icons alone need the section name; with labels shown the tooltip adds what the section contains.
            var lang = prefs.Language;
            foreach (var pair in new[] { Tuple.Create(sideHome, "homeHint"), Tuple.Create(scheduleButton, "scheduleHint"), Tuple.Create(journalButton, "journalHint"),
                Tuple.Create(materialsButton, "materialsHint"), Tuple.Create(moodleButton, "moodleHint"), Tuple.Create(downloadsButton, "downloadsHint"), Tuple.Create(sideSettings, "settingsHint"), Tuple.Create(website, "websiteHint") })
            {
                var hint = Strings.Get(pair.Item2, lang);
                pair.Item1.AccessibleName = pair.Item1.Text; pair.Item1.AccessibleDescription = hint;
                tips.SetToolTip(pair.Item1, sidebarExpanded ? hint : pair.Item1.Text + "\n" + hint);
            }
        }
        private async Task ToggleSidebarAsync()
        {
            if (commandBusy || closing) return;
            // An explicit choice applies at every window width and is remembered for the next launch.
            prefs.Sidebar = manualExpanded = !sidebarExpanded;
            ArrangeShell(); MotionSurface.Reveal(sidebar, sidebarExpanded ? 1 : -1);
            if (!Motion.KeyboardInput && main != null)
                await AutomationController.EvaluateAsync(main, "if(window.__univerMotionUI){document.documentElement.classList.remove('univer-app-keyboard');window.__univerMotionUI.reveal(document.getElementById('l4'),'content');} 'motion'");
            await SavePreferencesAsync();
        }
        private void PaintSidebar(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            // A drawn monogram stays legible in the compact rail, where the wordmark is hidden.
            var mark = new RectangleF(sidebarExpanded ? SZ(16) : (sidebar.Width - SZ(36)) / 2f, SZ(20), SZ(36), SZ(36));
            using (var path = Theme.Rounded(mark, SZ(9))) using (var fill = new SolidBrush(Theme.Accent)) g.FillPath(fill, path);
            var monogramSize = 14 * SidebarZoom;
            if (monogramFont == null || monogramFont.Size != monogramSize) { monogramFont?.Dispose(); monogramFont = Theme.Strong(monogramSize); }
            TextRenderer.DrawText(g, "U", monogramFont, Rectangle.Round(mark), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, SZ(16), sidebarDivider, SZ(sidebarExpanded ? 232 : 68) - SZ(16), sidebarDivider);
            if (sidebarExpanded)
            {
                var dot = credentials == null ? Theme.Muted : prefs.AutoLoginPaused ? Theme.Current.Pending : Theme.Success;
                using (var brush = new SolidBrush(dot)) g.FillEllipse(brush, SZ(20), localAccount.Top + localAccount.Height / 2f - SZ(4), SZ(8), SZ(8));
            }
        }
        private void PaintToolbar(Graphics g)
        {
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            if (refresh.Visible) using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, refresh.Right + D(10), D(14), refresh.Right + D(10), toolbar.Height - D(14));
            // The zoom controls read as one segmented group.
            if (zoomGroup.IsEmpty) return;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var group = new RectangleF(zoomGroup.X + 0.5f, zoomGroup.Y + 0.5f, zoomGroup.Width - 1, zoomGroup.Height - 1);
            using (var path = Theme.Rounded(group, D(8))) using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
        }
        private void ArrangeToolbar()
        {
            var viewer = document != null; var x = D(12); var top = D(8);
            // Every view change passes here: the find button names what it searches, and a bar for a page that is gone closes.
            SetButtonLabel(find, viewer || DownloadsVisible ? "findInDocument" : "findOnPage"); CheckFindTarget();
            menu.SetBounds(x, top, D(36), D(36)); x += D(40);
            back.Compact = !viewer; back.Look = viewer ? ButtonLook.Outline : ButtonLook.Quiet;
            back.Width = viewer ? Math.Max(D(150), TextRenderer.MeasureText(back.Text, back.Font).Width + D(56)) : D(36); back.SetBounds(x, top, back.Width, D(36)); x += back.Width + D(4);
            forward.Visible = !viewer; if (!viewer) { forward.SetBounds(x, top, D(36), D(36)); x += D(40); }
            refresh.SetBounds(x, top, D(36), D(36)); x += D(36) + D(21);
            // Zoom applies to the website and to Downloads alike and keeps the right edge; the reading mode menu belongs
            // to the website only.
            zoomGroup = new Rectangle(toolbar.Width - D(12) - D(122), top, D(122), D(36));
            zoomOut.SetBounds(zoomGroup.Left + 1, top + 1, D(34), D(36) - 2); zoomLabel.SetBounds(zoomOut.Right, top + 1, zoomGroup.Width - D(68) - 2, D(36) - 2);
            zoomIn.SetBounds(zoomGroup.Right - 1 - D(34), top + 1, D(34), D(36) - 2);
            zoomOut.Visible = zoomIn.Visible = zoomLabel.Visible = true;
            mode.Visible = !DownloadsVisible;
            int titleWidth;
            if (DownloadsVisible)
            {
                // Find is offered while a file is shown in Chromium (PDF, Word as a page, text), which is what it can search.
                var findShown = downloadsView.FindTarget != null;
                find.Visible = findShown; find.SetBounds(zoomGroup.Left - D(10) - D(36), top, D(36), D(36));
                noticesButton.SetBounds((findShown ? find.Left - D(4) : zoomGroup.Left - D(10)) - D(36), top, D(36), D(36));
                titleWidth = noticesButton.Left - D(16) - x;
                pageTitle.Visible = true;
            }
            else
            {
                var modeWidth = Math.Max(D(84), TextRenderer.MeasureText(mode.Text, mode.Font).Width + D(52));
                mode.SetBounds(zoomGroup.Left - D(10) - modeWidth, top, modeWidth, D(36));
                find.Visible = true; find.SetBounds(mode.Left - D(8) - D(36), top, D(36), D(36));
                noticesButton.SetBounds(find.Left - D(4) - D(36), top, D(36), D(36));
                titleWidth = noticesButton.Left - D(16) - x;
                pageTitle.Visible = titleWidth >= D(60);
            }
            pageState.Visible = pageTitle.Visible;
            pageTitle.SetBounds(x, D(6), Math.Max(D(60), titleWidth), D(22));
            pageState.SetBounds(x, D(27), pageTitle.Width, D(19)); AlignHeading(); back.Invalidate(); toolbar.Invalidate();
        }
        // A title without a subtitle sits on the toolbar's centre line instead of leaving an empty second row.
        private void AlignHeading()
        {
            var single = string.IsNullOrEmpty(pageState.Text);
            // The empty subtitle label would otherwise paint over the lower half of the centred title.
            pageState.Visible = pageTitle.Visible && !single;
            pageTitle.Top = single ? (toolbar.Height - pageTitle.Height) / 2 : D(6);
        }
        // An exception that escaped a UI event handler: the window stays usable and offers Retry instead of a crash dialog.
        // A fault that keeps coming back (for example while painting) cannot be recovered, so the app closes instead of spinning.
        private readonly System.Collections.Generic.Queue<DateTime> unexpectedErrors = new System.Collections.Generic.Queue<DateTime>();
        private bool exitingOnError;
        internal void ReportUnexpectedError()
        {
            if (closing || IsDisposed || exitingOnError) return;
            var now = DateTime.UtcNow; unexpectedErrors.Enqueue(now);
            while (now - unexpectedErrors.Peek() > TimeSpan.FromSeconds(10)) unexpectedErrors.Dequeue();
            if (unexpectedErrors.Count >= 5)
            {
                exitingOnError = true;
                MessageBox.Show(Strings.Get("unexpectedErrorExit", prefs.Language), "Univer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitApplication(); return;
            }
            try { SetStatus("unexpectedError"); } catch (Exception) { }
        }
        private void SetStatus(string key)
        {
            if (closing) return; messages.Update(key); RenderFeedback();
        }
        private void RenderFeedback()
        {
            var message = messages.Current; var lang = prefs.Language;
            UpdateHeading();
            if (message == null) { feedback.Render(null, "", "", "", "", ""); toast.Render(null, "", "", "", "", ""); return; }
            var heading = Strings.Get("message" + message.Tone.ToString(), lang);
            var text = Strings.Get(message.Key, lang);
            if (message.Key == "httpError" || message.Key == "networkError") text += " (" + failureCode + ")";
            if (recoverAt.HasValue && (message.Action == MessageAction.Retry || message.Key == "elementsMissing")) text += " " + Strings.Get("autoRetry", lang);
            var action = message.Action == MessageAction.Settings ? "checkCredentials" : message.Action == MessageAction.Downloads ? "openDownloads" : message.Action == MessageAction.Notices ? "noticesShow" : "retry";
            var secondary = message.Action == MessageAction.Settings && message.Tone == MessageTone.Warning ? Strings.Get("retry", lang) : "";
            if (message.Key == "downloadComplete" && lastDownloadPath != null)
            { text = Strings.Get("fileSaved", lang) + " " + Path.GetFileName(lastDownloadPath); action = "openFile"; secondary = Strings.Get("allDownloads", lang); }
            else if (message.Key == "downloading") { text = DownloadProgressText() ?? text; action = "allDownloads"; }
            else if (message.Key == "noticesNew") { heading = Strings.Get("noticesNew", lang); text = noticeAnnouncement; }
            else if (message.Key == "downloadDuplicate" && duplicate != null)
            {
                var earlier = duplicate.Earlier; var when = earlier.Finished ?? earlier.Started;
                heading = Strings.Get("downloadDuplicate", lang); action = "openExisting"; secondary = Strings.Get("downloadAgain", lang);
                text = string.Format(Strings.Get("downloadDuplicateText", lang), earlier.FileName, when.ToString("dd.MM.yyyy HH:mm"));
            }
            // Progress, successes and the repeated-download question leave on their own; recovery actions remain in the banner.
            if ((message.Tone == MessageTone.Success || message.Key == "downloading" || message.Key == "downloadDuplicate" || message.Key == "noticesNew") && message.CanDismiss)
            {
                feedback.Render(null, "", "", "", "", "");
                toast.Render(message, heading, text, Strings.Get(action, lang), secondary, Strings.Get("dismiss", lang));
                return;
            }
            toast.Render(null, "", "", "", "", "");
            feedback.Render(message, heading, text, Strings.Get(action, lang), secondary, Strings.Get("dismiss", lang));
        }
        private void Localize()
        {
            var lang = prefs.Language;
            loading.SetLanguage(lang);
            university.Text = Strings.Get("university", lang); sideHome.Text = Strings.Get("home", lang); downloadsButton.Text = Strings.Get("downloads", lang);
            scheduleButton.Text = Strings.Get("schedule", lang); materialsButton.Text = Strings.Get("materials", lang); journalButton.Text = Strings.Get("journal", lang); moodleButton.Text = Strings.Get("moodle", lang);
            sideSettings.Text = Strings.Get("settings", lang); website.Text = Strings.Get("website", lang);
            portalGroup.Text = Strings.Get("groupPortal", lang); appGroup.Text = Strings.Get("groupApp", lang);
            back.Text = Strings.Get("backToSite", lang); mode.Text = Strings.Get("view", lang);
            localAccount.Text = Strings.Get(credentials == null ? "localManual" : prefs.AutoLoginPaused ? "localPaused" : "localSaved", lang);
            SetButtonLabel(back, document != null ? "backToSite" : "back"); SetButtonLabel(forward, "forward"); SetButtonLabel(refresh, "refresh");
            SetButtonLabel(mode, "view"); SetButtonLabel(zoomIn, "zoomIn"); SetButtonLabel(zoomOut, "zoomOut");
            SetButtonLabel(find, document != null || DownloadsVisible ? "findInDocument" : "findOnPage"); findBar.SetLanguage(lang);
            // Section names, the toolbar sidebar button and their tooltips are set by ArrangeShell below.
            viewMenu.Items.Clear();
            var readable = new ToolStripMenuItem(Strings.Get("reading", lang)) { Checked = prefs.Readable };
            var original = new ToolStripMenuItem(Strings.Get("original", lang)) { Checked = !prefs.Readable };
            readable.Click += async (s, e) => await SetReadableAsync(true); original.Click += async (s, e) => await SetReadableAsync(false);
            viewMenu.Items.Add(readable); viewMenu.Items.Add(original);
            var print = new ToolStripMenuItem(Strings.Get("printPage", lang)) { ShortcutKeyDisplayString = "Ctrl+P" };
            print.Click += (s, e) => PrintPage(); viewMenu.Items.Add(new ToolStripSeparator()); viewMenu.Items.Add(print);
            zoomLabel.Text = zoomPercent + "%"; LocalizeTray(); downloadsView?.SetLanguage(lang); UpdateLocation(); ArrangeShell(); RenderFeedback(); RenderNotices();
        }
        private void UpdateLocation()
        {
            var address = Active?.Address;
            var path = SitePolicy.Trusted(address) ? new Uri(address).AbsolutePath.TrimEnd('/') : "";
            var site = document == null && !DownloadsVisible;
            scheduleButton.Selected = site && path.StartsWith("/student/myschedule", StringComparison.OrdinalIgnoreCase);
            materialsButton.Selected = site && path.StartsWith("/student/umkd", StringComparison.OrdinalIgnoreCase);
            journalButton.Selected = site && path.StartsWith("/student/attendance", StringComparison.OrdinalIgnoreCase);
            sideHome.Selected = site && !scheduleButton.Selected && !materialsButton.Selected && !journalButton.Selected;
            downloadsButton.Selected = DownloadsVisible;
            UpdateHeading();
            foreach (var button in new[] { sideHome, scheduleButton, materialsButton, journalButton, downloadsButton }) button.Invalidate();
        }
        private void UpdateHeading()
        {
            var lang = prefs.Language;
            if (DownloadsVisible)
            {
                pageTitle.Text = downloadsView.Previewing ? downloadsView.PreviewName : Strings.Get("downloads", lang);
                pageState.Text = downloadsView.Previewing ? Strings.Get("previewHint", lang) : string.Format(Strings.Get("downloadsCount", lang), downloadsView.Count);
                tips.SetToolTip(pageTitle, pageTitle.Text); AlignHeading(); return;
            }
            var key = document != null ? "document" : scheduleButton.Selected ? "schedule" : materialsButton.Selected ? "materials" : journalButton.Selected ? "journal" : "page";
            pageTitle.Text = siteTitle.Length > 0 ? siteTitle : Strings.Get(key, lang);
            pageState.Text = messages.PageKey == "loading" ? Strings.Get("loadingProgress", lang) : document != null ? Strings.Get("documentHint", lang) : siteSection;
            tips.SetToolTip(pageTitle, pageTitle.Text); AlignHeading();
        }
        private async Task UpdateSiteTitleAsync()
        {
            var browser = Active; string raw;
            try
            {
                raw = await AutomationController.EvaluateAsync(browser,"(function(){function t(e){return e?(e.textContent||'').replace(/\\s+/g,' ').trim():'';}" +
                "var current=document.querySelector('#tools a[aria-current=page]'),tab=document.querySelector('.univer-app-tabs a.active,#l3 a.active');" +
                "function head(c){if(c.matches('.univer-app-breadcrumb')){var l=c.querySelectorAll('a');return t(l[l.length-1]);}var k=c.cloneNode(true);k.querySelectorAll('.univer-app-print-action,script,style').forEach(function(n){n.remove();});return t(k);}" +
                "var heading=Array.from(document.querySelectorAll('#l4 tr.top>td.ct,h1,h2')).filter(function(e){return e.getClientRects().length;}).map(head).find(function(x){return x.length>1&&x.length<140;})||'';" +
                "var title=t(current)||heading||document.title.replace(/[\\s|]+$/,'');return title.slice(0,120)+'\\n'+t(tab).slice(0,60);})()");
            }
            catch (Exception) { return; } // The heading is cosmetic; a navigation can invalidate the frame.
            if (closing || browser != Active || raw == null) return;
            var parts = raw.Split('\n');
            siteTitle = parts[0].Trim(); siteSection = parts.Length > 1 ? parts[1].Trim() : "";
            UpdateHeading();
        }
        private bool RestoreWindow()
        {
            var parts = (prefs.Window ?? "").Split(',');
            int x, y, w, h;
            if (parts.Length != 5 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y) || !int.TryParse(parts[2], out w) || !int.TryParse(parts[3], out h)) return false;
            // Only reuse a position whose title bar is still on a connected monitor, and never exceed it.
            var caption = new Rectangle(x + 40, y, Math.Max(1, w - 80), 32);
            foreach (var screen in Screen.AllScreens)
            {
                var visible = Rectangle.Intersect(screen.WorkingArea, caption);
                if (visible.Width < 120 || visible.Height < 16) continue;
                var area = screen.WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 24), Math.Min(MinimumSize.Height, area.Height - 24));
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(x, y, Math.Min(area.Width, Math.Max(MinimumSize.Width, w)), Math.Min(area.Height, Math.Max(MinimumSize.Height, h)));
                if (parts[4] == "1") WindowState = FormWindowState.Maximized;
                return true;
            }
            return false;
        }
        private void RememberWindow()
        {
            if (selfTest || accountReview || storageFailed) return;
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var snapshot = prefs.Clone(); snapshot.Zoom = zoomPercent;
            if (bounds.Width >= 100 && bounds.Height >= 100)
                snapshot.Window = string.Join(",", bounds.X, bounds.Y, bounds.Width, bounds.Height, WindowState == FormWindowState.Maximized ? "1" : "0");
            // Serialized behind any pending settings write; a failure only loses the window position.
            try { storage.SavePreferencesAsync(snapshot).Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        }
        // Each poll is several round trips into Chromium. While nobody can see the window (tray or minimized), sign-in,
        // notices and agreements keep running at a calmer 1 s pace; their deadlines and attempt budgets use real time.
        private void UpdatePollingRate()
        {
            var interval = Visible && WindowState != FormWindowState.Minimized ? 200 : 1000;
            if (timer.Interval != interval) timer.Interval = interval;
        }
        // Back closes Downloads or the viewer before it goes back in the site; Forward belongs to the site only.
        private void UpdateHistoryButtons()
        {
            if (closing || Active == null || Active.IsDisposed) return;
            back.Enabled = DownloadsVisible || document != null || Active.CanGoBack; forward.Enabled = !DownloadsVisible && document == null && Active.CanGoForward;
        }
        private void SetButtonLabel(ModernButton button, string key)
        { button.AccessibleName = Strings.Get(key, prefs.Language); tips.SetToolTip(button, button.AccessibleName); }
        protected override void Dispose(bool disposing)
        { if (disposing) { timer.Dispose(); noticeChecking.Dispose(); tips.Dispose(); viewMenu.Dispose(); lifetime.Dispose(); monogramFont?.Dispose(); } base.Dispose(disposing); }
        private Font monogramFont;
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Motion.KeyboardInput = true;
            MotionSurface.StopTree(this);
            if (commandBusy) return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == (Keys.Alt | Keys.Left)) { Back(); return true; }
            // An open find bar takes Escape first; the next Escape closes the document or preview as before.
            if (keyData == Keys.Escape && findBar.Searching) { findBar.Close(); return true; }
            if (keyData == Keys.Escape && NoticesShown) { HideNotices(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { OpenFind(); return true; }
            if (keyData == Keys.F3 || keyData == (Keys.Shift | Keys.F3)) { if (findBar.Searching) findBar.Step(keyData == Keys.F3); else OpenFind(); return true; }
            if (keyData == Keys.Escape && DownloadsVisible) { if (downloadsView.Previewing) downloadsView.ClosePreview(); else if (!downloadsView.ClearSearch()) HideDownloads(); return true; }
            if (keyData == Keys.Escape && document != null) { CloseDocument(); return true; }
            if (keyData == (Keys.Control | Keys.J)) { ShowDownloads(); return true; }
            if (keyData == Keys.F5) { RefreshCurrent(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { PrintPage(); return true; }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { QueueAsync(() => OpenSettingsAsync()); return true; }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { SetZoom(100); return true; }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add)) { SetZoom(zoomPercent + 10); return true; }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract)) { SetZoom(zoomPercent - 10); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
