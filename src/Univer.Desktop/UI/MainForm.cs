using System;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private readonly Panel canvas = new BackdropPanel(), sidebar = new SmoothPanel(), toolbar = new SmoothPanel();
        private readonly FeedbackPanel feedback = new FeedbackPanel();
        private ToastPanel toast;
        private readonly Panel workspace = new SmoothPanel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        private readonly LoadingView loading = new LoadingView();
        private readonly Panel pageCover = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };
        private readonly Label brand = new Label(), university = new Label(), localAccount = new RollLabel(), pageTitle = new RollLabel(), pageState = new RollLabel(), zoomLabel = new RollLabel();
        private readonly Label portalGroup = new Label(), appGroup = new Label();
        private readonly ModernButton menu = new ModernButton(), back = new ModernButton(), forward = new ModernButton(), refresh = new ModernButton(), mode = new ModernButton(), zoomIn = new ModernButton(), zoomOut = new ModernButton(), find = new ModernButton();
        private FindBar findBar;
        private readonly ModernButton sideHome = new ModernButton(), downloadsButton = new ModernButton(), sideSettings = new ModernButton(), website = new ModernButton();
        private readonly ModernButton scheduleButton = new ModernButton(), materialsButton = new ModernButton(), journalButton = new ModernButton(), moodleButton = new ModernButton(), attestationButton = new ModernButton(), distanceButton = new ModernButton();
        // A sidebar row: its name (its hint is Name + "Hint"), the page it opens and the part of the site that counts as its own.
        private sealed class Section
        {
            public readonly ModernButton Button; public readonly UiIcon Icon; public readonly string Name, Target, Pages;
            public Section(ModernButton button, UiIcon icon, string name, string target = null, string pages = null) { Button = button; Icon = icon; Name = name; Target = target; Pages = pages; }
        }
        // The two groups of the sidebar, top to bottom.
        private Section[] portal, tools;
        private System.Collections.Generic.IEnumerable<Section> Sections { get { return portal.Concat(tools); } }
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
        private int zoomPercent = 100, appliedZoom = 100, sessionEpoch;
        private Rectangle zoomGroup;
        private string siteTitle = "", siteSection = "";
        private readonly float layoutScale;
        private DateTime ignoreCefCloseUntil;
        private DateTime presentationBegan = DateTime.UtcNow;
        private bool recoveringPresentation;
        private int D(int value) { return (int)Math.Round(value * layoutScale); }
        // The sidebar follows the page zoom within a range that keeps the workspace usable.
        private float SidebarZoom { get { return Math.Max(0.8f, Math.Min(1.6f, appliedZoom / 100f)); } }
        private int SZ(int value) { return (int)Math.Round(value * layoutScale * SidebarZoom); }
        private ChromiumWebBrowser Active { get { return document ?? main; } }
        public MainForm(Storage storage, bool selfTest, bool liveSmoke, bool accountReview = false, bool reviewAll = false)
        {
            this.storage = storage; this.selfTest = selfTest; this.liveSmoke = liveSmoke; this.accountReview = accountReview; this.reviewAll = reviewAll; stopping = lifetime.Token;
            ctrlWheel = new CtrlWheel((delta, time) => Dispatch(() => WheelZoom(delta, time)));
            var stored = storage.Load(); prefs = stored.Preferences; credentials = stored.Credentials; storageFailed = stored.Failed; credentialsUnreadable = stored.CredentialsUnreadable;
            plainPassword = stored.PlainFile; Program.Mark("stored");
            // A review that may only use the existing session: without credentials in memory an expired session stops at
            // the login page instead of signing in. Nothing is saved in review mode, so the stored record is untouched.
            if (accountReview && Array.IndexOf(Environment.GetCommandLineArgs(), "--review-no-login") >= 0) credentials = null;
            // The PIN file was deleted (a sealed login or the PIN mark lock.pin without lock.dpapi): the logout below replaces the
            // browser profiles and the login file goes, so removing the PIN file opens neither the password nor the old
            // session. The login file goes only after the mark is saved: a failed write keeps it for the next start.
            var orphan = stored.SealedOrphan && !storageFailed && !accountReview;
            if (orphan) prefs.SessionClearPending = true;
            // A logout that the previous run did not finish ends here, before any browser profile is opened.
            if (!storageFailed) { try { storage.FinishSessionClear(prefs, !accountReview); } catch (Exception) { storageFailed = true; } }
            if (orphan && !storageFailed)
            {
                try { storage.RemoveCredentials(); } catch (Exception) { }
                try { storage.RemoveLock(); } catch (Exception) { }
            }
            // Housekeeping of old profiles must never discard successfully loaded settings or credentials.
            if (!storageFailed && !accountReview) { try { storage.CleanOldProfiles(prefs.Profile); if (storage.WebProfileSaved(prefs.WebProfile)) storage.CleanOldWebProfiles(prefs.WebProfile); } catch (Exception) { } }
            Program.Mark("cleaned");
            LoadScheduleCopy(); LoadGradeSheet();
            // Automated checks read settled layouts, so they always run without motion.
            Motion.Enabled = prefs.Animations && !selfTest && !accountReview;
            Font = Theme.Font(10.5f); ForeColor = Theme.Ink; BackColor = Theme.Background;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            layoutScale = Theme.DpiScale;
            Size = new Size(1360, 860); MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterScreen;
            if (selfTest || accountReview) { Opacity = 0; ShowInTaskbar = false; }
            if (accountReview) prefs.Readable = true;
            if (!selfTest && !accountReview) zoomPercent = appliedZoom = prefs.Zoom;
            zoomSettle.Tick += ZoomSettled;
            Icon = LoadAppIcon() ?? Icon;
            BuildShell(); Program.Mark("shell"); BuildTray(); BuildReminders(); BuildOffline(); BuildShapes(); Program.Mark("shapes"); CreateSession(); Program.Mark("session"); Localize(); HookTheme(); BuildUpdates(); Program.Mark("built");
            timer.Tick += async (s, e) =>
            {
                if (rotating || closing || commandBusy) return;
                ExpireResumedDownload(); ExpireSignOn(); RunRecovery();
                if (error) return;
                if (loading.Visible && DateTime.UtcNow - presentationBegan > TimeSpan.FromSeconds(45))
                {
                    // The handler awaits, so the 200 ms timer fires again before the cover is removed.
                    if (recoveringPresentation) return;
                    recoveringPresentation = true;
                    try
                    {
                        var recoverable = guard.Suspend();
                        // A page that already has its content and waits for nothing is simply open: the notice would only worry.
                        var quiet = await AutomationController.EvaluateAsync(Active, "(function(){var p=window.__univerAutomationV1&&window.__univerAutomationV1.probe();return p&&p.domReady&&p.hasContent&&!p.hasPendingForm&&!p.loginPage&&!p.notice&&!p.agreement?'quiet':'busy'})()") == "quiet";
                        await ApplyPresentationAsync();
                        SetPresentation(Active, false, true);
                        if (!(quiet && recoverable)) SetStatus(recoverable ? "presentationSlow" : "automationFailed");
                    }
                    finally { recoveringPresentation = false; }
                    return;
                }
                // A quiet page is asked rarely (ProbePace); what the tick does for the app itself goes on every second.
                if (PageSettled && !automation.ProbeDue(Visible && WindowState != FormWindowState.Minimized)) { if (automation.SignedInQuiet) SignedInWork(); }
                else await automation.TickAsync(Active, credentials, prefs, document != null);
                UpdateHistoryButtons(); UpdatePollingRate();
            };
            Resize += (s, e) => UpdatePollingRate(); VisibleChanged += (s, e) => UpdatePollingRate(); Activated += (s, e) => { EnterUpdates(); PageWoken(Active); if (DownloadsVisible) QueueAsync(PruneDownloadsAsync); };
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
                zoomSettle.Dispose(); curtain?.Dispose(); ctrlWheel.Dispose(); DisposeLock(); DisposeReminders(); DisposeTray(); updates?.Dispose(); downloads?.Dispose(); DisposeWeb(); CloseBrowsers(); context?.Dispose(); Web.SingleSignOn.CleanUp(TimeSpan.Zero);
            };
            var restored = false;
            // Restore before the first paint so the window does not jump after appearing.
            Load += (s, e) => { if (!selfTest && !accountReview) restored = RestoreWindow(); };
            Shown += async (s, e) =>
            {
                if (Program.WindowShown == 0) Program.WindowShown = Program.Clock.ElapsedMilliseconds;
                if (!restored)
                {
                    var area = Screen.FromControl(this).WorkingArea;
                    MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 24), Math.Min(MinimumSize.Height, area.Height - 24));
                    Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                    Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2;
                }
                if (selfTest) { await SelfTestAsync(); return; }
                if (accountReview) { await ReviewAccountAsync(); return; }
#if QA_REVIEW
                // Walks the real profile's pages with the saved sign-in and writes captures: a QA build only, never past the PIN.
                if (!locked && Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-transitions") >= 0) { QueueAsync(TransitionQaAsync); return; }
#endif
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-keys") >= 0) { QueueAsync(KeysQaAsync); return; }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-study") >= 0) { QueueAsync(StudyQaAsync); return; }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-zoom") >= 0) { QueueAsync(ZoomFeelQaAsync); return; }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-flash") >= 0) { QueueAsync(FlashQaAsync); return; }
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-offline") >= 0) { QueueAsync(OfflineRunQaAsync); return; }
                if (startHidden) { EnterTrayAtStart(); return; }
                // Locked at start: the first sign-in window, the notices and the update offer wait for the PIN.
                if (locked) { startPromptsPending = true; return; }
                await StartPromptsAsync();
            };
        }
        private SelectionPlate sectionPlate;
        private void BuildShell()
        {
            sectionPlate = new SelectionPlate(sidebar, () => SZ(Kit.RadiusSmall));
            sidebar.Dock = DockStyle.Left; sidebar.Width = 240; sidebar.BackColor = Theme.Surface;
            brand.Text = "Univer"; brand.Font = Theme.Display(Theme.Title); brand.ForeColor = Theme.Ink; brand.AutoEllipsis = true;
            university.Font = Theme.Font(Theme.Caption); university.ForeColor = Theme.Muted; university.AutoEllipsis = true;
            localAccount.ForeColor = Theme.Muted; localAccount.Font = Theme.Font(Theme.Caption); localAccount.TextAlign = ContentAlignment.MiddleLeft; localAccount.AutoEllipsis = true;
            // Group captions separate the portal shortcuts from local app tools when the labels are shown.
            foreach (var group in new[] { portalGroup, appGroup }) { group.Font = Theme.Font(Theme.Caption); group.ForeColor = Theme.Muted; group.AutoEllipsis = true; group.UseMnemonic = false; sidebar.Controls.Add(group); }
            portal = new[]
            {
                // The read notice is confirmed once at startup; home goes straight to the news list afterwards.
                new Section(sideHome, UiIcon.Home, "home", "/news/student"),
                new Section(scheduleButton, UiIcon.Calendar, "schedule", "/student/myschedule/", "/student/myschedule"),
                new Section(journalButton, UiIcon.Journal, "journal", "/student/attendance/full/", "/student/attendance"),
                new Section(attestationButton, UiIcon.Award, "attestation", "/student/attestation/", "/student/attestation"),
                new Section(materialsButton, UiIcon.Book, "materials", "/student/umkd/", "/student/umkd"),
                new Section(distanceButton, UiIcon.Laptop, "distance", "/student/discussion/index/", "/student/discussion"),
                // Univer's own Moodle sign-in page; the hand-off to Moodle then opens in the user's browser.
                new Section(moodleButton, UiIcon.Cap, "moodle", "/user/loginMoodle")
            };
            tools = new[] { new Section(downloadsButton, UiIcon.Downloads, "downloads"), new Section(sideSettings, UiIcon.Settings, "settings"), new Section(website, UiIcon.External, "website") };
            foreach (var section in Sections)
            {
                var button = section.Button; var target = section.Target;
                button.Look = ButtonLook.Navigation; button.Height = 32; button.Plate = sectionPlate; button.Icon = section.Icon; sidebar.Controls.Add(button);
                if (target != null) button.Click += (s, e) => NavigateSection(target);
            }
            sideHome.Selected = true;
            sidebar.Controls.Add(brand); sidebar.Controls.Add(university); sidebar.Controls.Add(localAccount);
            sidebar.Paint += (s, e) => PaintSidebar(e.Graphics);
            tips.ShowAlways = true; Theme.Tips(tips);
            toolbar.Dock = DockStyle.Top; toolbar.Height = 44; toolbar.BackColor = Theme.Surface;
            toolbar.Paint += (s, e) => PaintToolbar(e.Graphics);
            canvas.Dock = DockStyle.Fill; canvas.BackColor = Theme.Background;
            canvas.Controls.Add(loading); canvas.Controls.Add(pageCover);
            BuildDownloads();
            foreach (var button in new[] { menu, back, forward, refresh, find, mode, zoomIn, zoomOut }) { button.Compact = true; button.Size = new Size(28, 28); toolbar.Controls.Add(button); }
            menu.Icon = UiIcon.Menu; back.Icon = UiIcon.Back; forward.Icon = UiIcon.Forward; refresh.Icon = UiIcon.Reload; find.Icon = UiIcon.Search;
            mode.Icon = UiIcon.Eye; mode.Compact = false; mode.Look = ButtonLook.Outline; mode.Font = Theme.Font(Theme.Body); zoomIn.Icon = UiIcon.Plus; zoomOut.Icon = UiIcon.Minus;
            pageTitle.Font = Theme.Display(Theme.Body); pageTitle.AutoEllipsis = true; pageTitle.UseMnemonic = false;
            pageState.Font = Theme.Font(Theme.Meta); pageState.ForeColor = Theme.Muted; pageState.AutoEllipsis = true; pageState.UseMnemonic = false;
            zoomLabel.Font = Theme.Font(Theme.Meta); zoomLabel.TextAlign = ContentAlignment.MiddleCenter; zoomLabel.ForeColor = Theme.Ink;
            zoomLabel.Cursor = Cursors.Hand; zoomLabel.Click += (s, e) => SetZoom(100);
            toolbar.Controls.Add(pageTitle); toolbar.Controls.Add(pageState); toolbar.Controls.Add(zoomLabel);
            Motion.FadeIn(viewMenu); viewMenu.Opened += (s, e) => mode.Open = true; viewMenu.Closed += (s, e) => mode.Open = false;
            viewMenu.Font = Theme.Font(Theme.Body); viewMenu.ShowImageMargin = false; viewMenu.ShowCheckMargin = true; Theme.Style(viewMenu);
            workspace.Controls.Add(canvas); workspace.Controls.Add(feedback); workspace.Controls.Add(toolbar);
            toast = new ToastPanel(layoutScale); workspace.Controls.Add(toast); toast.BringToFront();
            findBar = new FindBar(layoutScale) { Placing = PlaceFind }; workspace.Controls.Add(findBar); findBar.BringToFront();
            BuildWeb(); toast.BringToFront();
            workspace.Resize += (s, e) => { toast.Place(); PlaceFind(); }; canvas.LocationChanged += (s, e) => PlaceFind();
            findBar.VisibleChanged += (s, e) => { find.Selected = findBar.Visible; PlaceFind(); };
            BuildNotices();
            find.Click += (s, e) => OpenFind();
            toast.Expired += () => { if (messages.Current?.Key == toast.Key) { messages.Dismiss(); RenderFeedback(); } };
            Controls.Add(workspace); Controls.Add(sidebar);
            BuildLock();
            Resize += (s, e) => ArrangeShell(); toolbar.Resize += (s, e) => ArrangeToolbar(); sidebar.Resize += (s, e) => ArrangeSidebar();
            menu.Click += async (s, e) => await ToggleSidebarAsync();
            back.Click += (s, e) => Back(); forward.Click += (s, e) => { if (Active?.CanGoForward == true) Active.Forward(); };
            refresh.Click += (s, e) => RefreshCurrent();
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
            var target = SZ(expand ? 240 : 64); toolbar.Height = SZ(44);
            // Resize Chromium once; animating width forces a complete website reflow on every frame.
            sidebarExpanded = expand; sidebar.Width = target;
            ArrangeSidebar(); ArrangeToolbar(); sidebar.Invalidate();
            // The toolbar button names the action it performs now: show the names or fold the panel back to icons.
            SetButtonLabel(menu, expand ? "collapseSidebar" : "expandSidebar"); UpdateSidebarTips();
        }
        private void ArrangeSidebar()
        {
            // Expanded labels and the compact rail share the same row positions.
            var full = SZ(sidebarExpanded ? 240 : 64);
            brand.SetBounds(SZ(54), SZ(13), full - SZ(62), SZ(20)); brand.Visible = sidebarExpanded;
            university.SetBounds(SZ(54), SZ(32), full - SZ(62), SZ(18)); university.Visible = sidebarExpanded;
            portalGroup.Visible = appGroup.Visible = sidebarExpanded;
            // Group captions keep their rows in the icon rail too; a caption sits closer to its rows than to the group above.
            var width = full - SZ(24); var y = SZ(68);
            // Every section stays reachable: a short window at a large zoom tightens the rows instead of losing the last ones.
            var rows = portal.Length + tools.Length;
            var step = Math.Max(SZ(26), Math.Min(SZ(40), (sidebar.Height - y - SZ(56) - SZ(44)) / rows)); var height = step - Math.Max(SZ(2), Math.Min(SZ(4), step / 10));
            portalGroup.SetBounds(SZ(22), y, width - SZ(20), SZ(18)); y += SZ(22);
            foreach (var section in portal) { section.Button.SetBounds(SZ(12), y, width, height); y += step; }
            y += SZ(12);
            appGroup.SetBounds(SZ(22), y, width - SZ(20), SZ(18)); y += SZ(22);
            foreach (var section in tools) { section.Button.SetBounds(SZ(12), y, width, height); y += step; }
            localAccount.SetBounds(SZ(38), sidebar.Height - SZ(40), full - SZ(48), SZ(24)); localAccount.Visible = sidebarExpanded;
            foreach (var section in Sections) { section.Button.Compact = !sidebarExpanded; section.Button.Invalidate(); }
            sectionPlate.Snap();
        }
        private void UpdateSidebarTips()
        {
            // Icons alone need the section name; with labels shown the tooltip adds what the section contains.
            var lang = prefs.Language;
            foreach (var section in Sections)
            {
                var button = section.Button; var hint = Strings.Get(section.Name + "Hint", lang);
                button.AccessibleName = button.Text; button.AccessibleDescription = hint;
                tips.SetToolTip(button, sidebarExpanded ? hint : button.Text + "\n" + hint);
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
            using (var pen = new Pen(Theme.Current.Line)) g.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
            sectionPlate.Draw(g, 0, 0);
            // A drawn monogram stays legible in the compact rail, where the wordmark is hidden.
            var mark = new RectangleF(sidebarExpanded ? SZ(16) : (sidebar.Width - SZ(28)) / 2f, SZ(18), SZ(28), SZ(28));
            using (var path = Theme.Rounded(mark, SZ(Kit.RadiusSmall + 1))) using (var fill = new SolidBrush(Theme.Accent)) g.FillPath(fill, path);
            var monogramSize = Theme.Emphasis * SidebarZoom;
            if (monogramFont == null || monogramFont.Size != monogramSize) { monogramFont?.Dispose(); monogramFont = Theme.Display(monogramSize); }
            TextRenderer.DrawText(g, "U", monogramFont, Rectangle.Round(mark), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (sidebarExpanded)
            {
                var dot = credentials == null ? Theme.Muted : prefs.AutoLoginPaused ? Theme.Current.Pending : Theme.Success;
                using (var brush = new SolidBrush(dot)) g.FillEllipse(brush, SZ(23), localAccount.Top + localAccount.Height / 2f - SZ(3), SZ(6), SZ(6));
            }
        }
        private void PaintToolbar(Graphics g)
        {
            using (var pen = new Pen(Theme.Current.Line)) g.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            // The zoom controls read as one segmented group.
            if (zoomGroup.IsEmpty) return;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var group = new RectangleF(zoomGroup.X + 0.5f, zoomGroup.Y + 0.5f, zoomGroup.Width - 1, zoomGroup.Height - 1);
            using (var path = Theme.Rounded(group, SZ(Kit.RadiusSmall))) using (var pen = new Pen(Theme.Current.Control)) g.DrawPath(pen, path);
        }
        private void ArrangeToolbar()
        {
            var viewer = document != null; var x = SZ(12); var top = SZ(8);
            // Every view change passes here: the find button names what it searches, and a bar for a page that is gone closes.
            SetButtonLabel(find, viewer || DownloadsVisible ? "findInDocument" : "findOnPage"); CheckFindTarget();
            menu.SetBounds(x, top, SZ(28), SZ(28)); x += SZ(32);
            back.Compact = !viewer; back.Look = viewer ? ButtonLook.Outline : ButtonLook.Quiet;
            back.Width = viewer ? Math.Max(SZ(120), TextRenderer.MeasureText(back.Text, back.Font).Width + SZ(44)) : SZ(28); back.SetBounds(x, top, back.Width, SZ(28)); x += back.Width + SZ(4);
            forward.Visible = !viewer; if (!viewer) { forward.SetBounds(x, top, SZ(28), SZ(28)); x += SZ(32); }
            refresh.SetBounds(x, top, SZ(28), SZ(28)); x += SZ(28) + SZ(16);
            // Zoom applies to the website and to Downloads alike and keeps the right edge; the reading mode menu belongs
            // to the website only.
            zoomGroup = new Rectangle(toolbar.Width - SZ(12) - SZ(104), top, SZ(104), SZ(28));
            zoomOut.SetBounds(zoomGroup.Left + 1, top + 1, SZ(26), SZ(28) - 2); zoomLabel.SetBounds(zoomOut.Right, top + 1, zoomGroup.Width - SZ(52) - 2, SZ(28) - 2);
            zoomIn.SetBounds(zoomGroup.Right - 1 - SZ(26), top + 1, SZ(26), SZ(28) - 2);
            zoomOut.Visible = zoomIn.Visible = zoomLabel.Visible = true;
            mode.Visible = !DownloadsVisible;
            int titleWidth;
            if (DownloadsVisible)
            {
                // Find is offered while a file is shown in Chromium (PDF, Word as a page, text), which is what it can search.
                var findShown = downloadsView.FindTarget != null;
                find.Visible = findShown; find.SetBounds(zoomGroup.Left - SZ(10) - SZ(28), top, SZ(28), SZ(28));
                noticesButton.SetBounds((findShown ? find.Left - SZ(4) : zoomGroup.Left - SZ(10)) - SZ(28), top, SZ(28), SZ(28));
                titleWidth = noticesButton.Left - SZ(16) - x;
                pageTitle.Visible = true;
            }
            else
            {
                var modeWidth = Math.Max(SZ(72), TextRenderer.MeasureText(mode.Text, mode.Font).Width + SZ(40));
                mode.SetBounds(zoomGroup.Left - SZ(10) - modeWidth, top, modeWidth, SZ(28));
                find.Visible = true; find.SetBounds(mode.Left - SZ(8) - SZ(28), top, SZ(28), SZ(28));
                noticesButton.SetBounds(find.Left - SZ(4) - SZ(28), top, SZ(28), SZ(28));
                titleWidth = noticesButton.Left - SZ(16) - x;
                pageTitle.Visible = titleWidth >= SZ(60);
            }
            pageState.Visible = pageTitle.Visible;
            pageTitle.SetBounds(x, SZ(4), Math.Max(SZ(60), titleWidth), SZ(20));
            pageState.SetBounds(x, SZ(23), pageTitle.Width, SZ(17)); AlignHeading(); back.Invalidate(); toolbar.Invalidate();
        }
        // A title without a subtitle sits on the toolbar's centre line instead of leaving an empty second row.
        private void AlignHeading()
        {
            var single = string.IsNullOrEmpty(pageState.Text);
            // The empty subtitle label would otherwise paint over the lower half of the centred title.
            pageState.Visible = pageTitle.Visible && !single;
            pageTitle.Top = single ? (toolbar.Height - pageTitle.Height) / 2 : SZ(4);
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
            if (closing) return; NoteStatus(key); messages.Update(key); RenderFeedback();
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
            { text = Strings.Get("fileSaved", lang) + " " + Path.GetFileName(lastDownloadPath) + StudyFolderNote(lastDownloadPath); action = "openFile"; secondary = Strings.Get("allDownloads", lang); }
            else if (message.Key == "downloading") { text = DownloadProgressText() ?? text; if (sectionRun != null) text = SectionText("downloadAllProgress", lang) + " · " + text; action = "allDownloads"; }
            else if (message.Key == "downloadAllProgress") { text = SectionText(message.Key, lang) ?? text; action = "allDownloads"; }
            else if (message.Key == "downloadAllDone" || message.Key == "downloadAllStopped") text = SectionText(message.Key, lang) ?? text;
            else if (message.Key == "externalOpened") text = string.Format(text, openedHost);
            else if (message.Key == "noticesNew") { heading = Strings.Get("noticesNew", lang); text = NoticeAnnouncement(); }
            else if (message.Key == "downloadDuplicate" && duplicate != null)
            {
                var earlier = duplicate.Earlier; var when = earlier.Finished ?? earlier.Started;
                heading = Strings.Get("downloadDuplicate", lang); action = "openExisting"; secondary = Strings.Get("downloadAgain", lang);
                text = string.Format(Strings.Get("downloadDuplicateText", lang), earlier.FileName, when.ToString("dd.MM.yyyy HH:mm"));
            }
            // Progress, successes and the repeated-download question leave on their own; recovery actions remain in the banner.
            if ((message.Tone == MessageTone.Success || message.Key == "downloading" || message.Key == "downloadAllProgress" || message.Key == "downloadAllStopping" || message.Key == "downloadDuplicate" || message.Key == "noticesNew") && message.CanDismiss)
            {
                feedback.Render(null, "", "", "", "", "");
                // A toast enters and leaves as a window of its own, which would pass over the lock; it waits for the unlock.
                if (LockShown) { toast.Render(null, "", "", "", "", ""); return; }
                toast.Render(message, heading, text, Strings.Get(action, lang), secondary, Strings.Get("dismiss", lang));
                return;
            }
            toast.Render(null, "", "", "", "", "");
            feedback.Render(message, heading, text, Strings.Get(action, lang), secondary, Strings.Get("dismiss", lang));
        }
        // The sidebar's account line and its dot: saved sign-in, paused, or sign-in on the website.
        private void UpdateAccountLine()
        {
            localAccount.Text = Strings.Get(credentials == null ? "localManual" : prefs.AutoLoginPaused ? "localPaused" : "localSaved", prefs.Language);
            sidebar.Invalidate();
        }
        private void Localize()
        {
            var lang = prefs.Language;
            UpdateOffline();
            var title = Strings.Get("appTitle", lang);
#if LEGACY_ENGINE
            title += " · Legacy";
#endif
            Text = title;
            loading.SetLanguage(lang); webPanel?.SetLanguage(lang);
            university.Text = Strings.Get("university", lang);
            foreach (var section in Sections) section.Button.Text = Strings.Get(section.Name, lang);
            portalGroup.Text = Strings.Get("groupPortal", lang); appGroup.Text = Strings.Get("groupApp", lang);
            back.Text = Strings.Get("backToSite", lang); mode.Text = Strings.Get("view", lang);
            UpdateAccountLine();
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
            // The chosen row goes first: the plate then slides to it instead of fading out with the row it leaves.
            var chosen = !site ? null : portal.FirstOrDefault(s => s.Pages != null && path.StartsWith(s.Pages, StringComparison.OrdinalIgnoreCase)) ?? portal[0];
            if (chosen != null) chosen.Button.Selected = true;
            foreach (var section in portal) if (section != chosen) section.Button.Selected = false;
            downloadsButton.Selected = DownloadsVisible;
            UpdateHeading();
            foreach (var section in Sections) section.Button.Invalidate();
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
            var key = document != null ? "document" : portal.FirstOrDefault(s => s.Pages != null && s.Button.Selected)?.Name ?? "page";
            pageTitle.Text = siteTitle.Length > 0 ? siteTitle : Strings.Get(key, lang);
            pageState.Text = messages.PageKey == "loading" ? Strings.Get("loadingProgress", lang) : document != null ? Strings.Get("documentHint", lang) : siteSection;
            tips.SetToolTip(pageTitle, pageTitle.Text); AlignHeading();
        }
        private async Task UpdateSiteTitleAsync()
        {
            // Under the loading cover the page may still be the one before a language switch or a sign-in: its
            // heading would flash in the toolbar. The reveal reads the heading again once the cover is gone.
            if (loading.Visible) return;
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
            // The first heading on Бакалавр belongs to the "Today" panel, not to the page.
            if (SitePolicy.Trusted(browser.Address) && new Uri(browser.Address).AbsolutePath.TrimEnd('/').Equals("/student/bachelor", StringComparison.OrdinalIgnoreCase)) siteTitle = Strings.Get("cabinet", prefs.Language);
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
        // Each poll is round trips into Chromium. While nobody can see the window (tray or minimized), and once a shown page
        // has settled, the timer runs at a calmer 1 s pace; deadlines and attempt budgets use real time. A navigation goes
        // back to the fast pace at once. A settled page that is also quiet (AutomationController.Quiet) is asked more rarely
        // still (ProbePace), while the timer keeps doing the app's own work.
        private bool PageSettled { get { return (settledForQa || !selfTest && !accountReview && DateTime.UtcNow - presentationBegan > TimeSpan.FromSeconds(10)) && !loading.Visible && document == null && guard.ReauthChallenge == null; } }
        // Self-test's own quiet stretch (QA step "idle"); otherwise a self-test page never counts as settled.
        private bool settledForQa;
        // What every tick on a signed-in page does for the app; a quiet page's skipped ticks do it too.
        private void SignedInWork() { RecoverySucceeded(); ResumeDownload(); NoticeContent(); ScheduleContent(); GradeContent(); MaybeOfferPin(); }
        // The user pressed something on the shown page or came back to the window: a page that was being asked rarely is
        // asked now, and every second for a while (ProbePace.Active).
        private void PageWoken(ChromiumWebBrowser browser)
        {
            if (automation == null || browser == null || browser != Active) return;
            var rested = !automation.RecentInput; automation.Woken();
            if (rested && timer.Enabled && !closing && !rotating && !commandBusy && !error) QueueAsync(() => automation.TickAsync(browser, credentials, prefs, document != null));
        }
        private void UpdatePollingRate()
        {
            var interval = Visible && WindowState != FormWindowState.Minimized && !PageSettled ? 200 : 1000;
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
        // Ctrl+K while the keyboard is in the app's own shell (sidebar, toolbar): the page opens «Все разделы» with the
        // cursor in its search. With the keyboard in the page, the page answers the key itself (univer-layout.js).
        private void OpenSections()
        {
            if (document != null || DownloadsVisible || WebShown || NoticesShown || loading.Visible || main == null || !main.IsBrowserInitialized || !SitePolicy.Trusted(main.Address)) return;
            main.Focus();
            QueueAsync(() => AutomationController.EvaluateAsync(main, "(function(){var b=document.querySelector('.univer-app-sections'),m=document.querySelector('.univer-app-menu');if(!b||b.hidden||!m)return 'none';if(!m.open)b.click();var s=m.querySelector('input[type=search]');if(s)s.focus({preventScroll:true});return 'open'})()"));
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Motion.KeyboardInput = true;
            MotionSurface.StopTree(this);
            if (LockShown && (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift))) { LockTab(keyData != Keys.Tab); return true; }
            if (LockShown) return LockKey(keyData) ? base.ProcessCmdKey(ref msg, keyData) : true;
            if (commandBusy) return base.ProcessCmdKey(ref msg, keyData);
            // The other-site window answers its own keys; nothing reaches the Univer page hidden behind it.
            if (WebShown)
            {
                if (keyData == Keys.Escape) { CloseWeb(); return true; }
                if (Web.WebKeys.Accepted(keyData)) { WebKey(keyData); return true; }
                if (keyData == (Keys.Control | Keys.F) || keyData == Keys.F3 || keyData == (Keys.Shift | Keys.F3)) return true;
                if (keyData == (Keys.Control | Keys.J)) { CloseWeb(); ShowDownloads(); return true; }
            }
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
            if (keyData == (Keys.Control | Keys.K)) { OpenSections(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { PrintPage(); return true; }
            if (keyData == (Keys.Control | Keys.Oemcomma)) { QueueAsync(() => OpenSettingsAsync()); return true; }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { SetZoom(100); return true; }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add)) { SetZoom(zoomPercent + 10); return true; }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract)) { SetZoom(zoomPercent - 10); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
