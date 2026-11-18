using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal enum SettingsAction { None, Save, Remove, Manual }
    // Settings in the look of the app's pages: sheets on the page colour, one row per setting, a short line under its name.
    // Positions are design sizes at 96 DPI; the form scales them with the DPI and the app's zoom.
    internal sealed partial class SettingsForm : Form
    {
        private const int Side = 24, Inner = 504, Pad = 16, RowHeight = 60;
        private static readonly string[] LanguageCodes = { "ru", "kz", "en" }, LanguageNames = { "Русский", "Қазақша", "English" };
        private readonly Credentials original;
        private readonly bool firstLaunch, paused;
        private bool busy, laidOut;
        private bool closingAnimation, animationClosed, fadeIn;
        private DialogResult closeResult;
        private Tween appearance;
        private readonly StageView stage = new StageView(StageScene.Welcome);
        private SelectionPlate tabPlate, languagePlate;
        private readonly TextInput login = new TextInput(false), password = new TextInput(true);
        private readonly Toggle readable = new Toggle(), tray = new Toggle(), animations = new Toggle(), externalLinks = new Toggle();
        // Links: where they open, and the in-app browser's data, cleared at once (not on Save) like Logout.
        private readonly SettingsSheet linksSheet = new SettingsSheet();
        private readonly Label webDataTitle = new Label(), webDataHint = new Label();
        private readonly ModernButton webDataClear = new ModernButton();
        private string webDataState = "";
        // Sign-in protection: the app PIN is set, changed or turned off at once, each in its own window.
        private readonly SettingsSheet securitySheet = new SettingsSheet();
        private readonly Label securityTitle = new Label(), securityHint = new Label();
        private readonly ModernButton pinSet = new ModernButton(), pinChange = new ModernButton(), pinDisable = new ModernButton();
        private readonly Panel pinSlot = new Panel(), pinGap = new Panel { Dock = DockStyle.Right, Width = 8, TabStop = false };
        private bool pinBusy;
        private readonly Label heading = new Label(), description = new Label(), error = new Label();
        private readonly Label loginLabel = new Label(), passwordLabel = new Label(), privacy = new Label();
        private readonly Label logoutTitle = new Label(), logoutHint = new Label();
        private readonly Label languageLabel = new Label(), languageHint = new Label(), themeLabel = new Label(), about = new Label();
        // Three languages and three themes are short choices: all of them stay in view.
        private readonly ModernButton[] languages = { new ModernButton(), new ModernButton(), new ModernButton() };
        private int languageIndex;
        private readonly ModernButton[] themes = { new ModernButton(), new ModernButton(), new ModernButton() };
        private static readonly string[] ThemeValues = { ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark }, ThemeNames = { "themeSystem", "themeLight", "themeDark" };
        private string theme;
        private readonly ModernButton accountTab = new ModernButton(), viewTab = new ModernButton(), appTab = new ModernButton();
        private readonly UpdateService updates;
        private readonly Label updateTitle = new Label(), updateStatus = new Label();
        private readonly ModernButton updateCheck = new ModernButton(), updateInstall = new ModernButton();
        private readonly Panel updateGap = new Panel { Dock = DockStyle.Right, Width = 8, TabStop = false };
        private readonly ModernButton save = new ModernButton(), logout = new ModernButton(), cancel = new ModernButton();
        private readonly Panel accountPanel = new Panel(), viewPanel = new Panel(), appPanel = new Panel(), content = new Panel(), updateSlot = new Panel();
        private readonly SettingsSheet localSheet = new SettingsSheet(), viewSheet = new SettingsSheet(), appSheet = new SettingsSheet();
        // What the window opened with: Save wakes up only when something differs.
        private readonly string startLanguage, startTheme;
        private readonly bool startReadable, startTray, startAnimations, startExternalLinks;
#if LEGACY_ENGINE
        private readonly LegacyWarningPanel legacyWarning = new LegacyWarningPanel();
#endif
        internal bool LegacyWarningVisible
        {
            get
            {
#if LEGACY_ENGINE
                return legacyWarning.Visible && legacyWarning.Parent == this;
#else
                return false;
#endif
            }
        }
        public SettingsAction Action { get; private set; }
        // Install was pressed: the window closes and the update window takes over.
        public bool UpdateRequested { get; private set; }
        public Action OpenReleasePage { get; set; }
        public Func<SettingsAction, Credentials, string, bool, Task<bool>> ApplyAsync { get; set; }
        public Credentials Credentials { get { return login.Input.Text.Length == 0 && password.Input.Text.Length == 0 ? null : new Credentials { Login = login.Input.Text.Trim(), Password = password.Input.Text }; } }
        public string Language { get { return LanguageCodes[languageIndex]; } }
        public bool Readable { get { return readable.Checked; } }
        public bool Tray { get { return tray.Checked; } }
        public bool Animations { get { return animations.Checked; } }
        public bool ExternalLinks { get { return externalLinks.Checked; } }
        public Func<Task<bool>> ClearWebData { get; set; }
        // Given after the window is built: the row is read again then.
        public Func<bool> PinSet { get { return pinSetState; } set { pinSetState = value; securityHint.Text = Strings.Get(PinOn ? "securityOn" : "securityOff", Language); ArrangeApp(); } }
        private Func<bool> pinSetState;
        public Func<IWin32Window, Task<bool>> SetPin { get; set; }
        public Func<IWin32Window, Task<bool>> ChangePin { get; set; }
        public Func<IWin32Window, Task<bool>> DisablePin { get; set; }
        private bool PinOn { get { return PinSet != null && PinSet(); } }
        public string SelectedTheme { get { return theme; } }
        public SettingsForm(Credentials credentials, Preferences prefs, bool firstLaunch = false, UpdateService updates = null, float zoom = 1f)
        {
            this.zoom = zoom; original = credentials; this.firstLaunch = firstLaunch; paused = prefs.AutoLoginPaused; this.updates = updates;
            Font = Theme.Font(Theme.Body); BackColor = firstLaunch ? Theme.Surface : Theme.Background; ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(firstLaunch ? 552 + 400 : 552, firstLaunch ? 470 : 548); MinimumSize = Size;
#if LEGACY_ENGINE
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 112); MinimumSize = Size;
#endif
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            Theme.Window(this);
            BuildFrame();
            BuildAccount(credentials); BuildView(prefs); BuildApp(prefs);
            startLanguage = Language; startTheme = theme; startReadable = readable.Checked; startTray = tray.Checked; startAnimations = animations.Checked; startExternalLinks = externalLinks.Checked;
            Wire();
            AcceptButton = save; if (!firstLaunch) CancelButton = cancel;
            fadeIn = Motion.Enabled && !Motion.KeyboardInput;
            if (fadeIn) Opacity = .25;
            // The window follows the app's zoom like the rest of the chrome: every bound, then every font.
            if (Math.Abs(zoom - 1) > .01f)
            {
                var wanted = new Size((int)Math.Round(ClientSize.Width * zoom), (int)Math.Round(ClientSize.Height * zoom));
                MinimumSize = Size.Empty; Scale(new SizeF(zoom, zoom)); ClientSize = wanted; MinimumSize = Size;
                UiZoom.ApplyFonts(this, zoom); languagePlate.Snap();
            }
            ShowTab(false); Localize();
        }
        // The window's frame: the heading of first sign-in, the tabs, the page they show and the two actions.
        private void BuildFrame()
        {
            // The window's title already says Settings; only first sign-in needs a heading of its own.
            var header = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(Side, 20, Side, 6), Visible = firstLaunch };
            heading.Font = Theme.Display(12.75f); heading.Dock = DockStyle.Top; heading.Height = 26; heading.UseMnemonic = false;
            description.Dock = DockStyle.Fill; description.ForeColor = Theme.Muted; description.Font = Theme.Font(Theme.Meta); header.Controls.Add(description); header.Controls.Add(heading);
            var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(Side, 12, Side, 0), WrapContents = false };
            tabPlate = new SelectionPlate(tabs, () => Px(6)) { Style = PlateStyle.Underline };
            tabs.Paint += (s, e) => { using (var pen = new Pen(Theme.Current.Line)) e.Graphics.DrawLine(pen, tabs.Padding.Left, tabs.Height - 1, tabs.Width - tabs.Padding.Right, tabs.Height - 1); tabPlate.Draw(e.Graphics, 0, 0); };
            foreach (var tab in new[] { accountTab, viewTab, appTab })
            {
                tab.Height = 34; tab.Margin = new Padding(0, 0, 14, 0); tab.Look = ButtonLook.Quiet; tab.Plate = tabPlate; tabs.Controls.Add(tab);
            }
            tabs.Visible = !firstLaunch;

            // The two actions sit together, where the pointer finishes; Save leads.
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(Side, 12, Side, 12) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.Current.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            save.Look = ButtonLook.Primary; save.Hug = true; save.Height = 32; save.Dock = DockStyle.Right; save.TabIndex = 0;
            stage.Dock = DockStyle.Left; stage.Width = 400; stage.Visible = firstLaunch;
            cancel.Look = ButtonLook.Outline; cancel.Hug = true; cancel.Height = 32; cancel.Dock = firstLaunch ? DockStyle.Left : DockStyle.Right; cancel.BackColor = Theme.Surface; cancel.TabIndex = 1;
            footer.Controls.Add(cancel); footer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, TabStop = false }); footer.Controls.Add(save);
            content.Dock = DockStyle.Fill; content.Padding = new Padding(Side - TextInput.Ring, 16, Side - TextInput.Ring, 12); content.AutoScroll = true;
            accountPanel.Dock = viewPanel.Dock = appPanel.Dock = DockStyle.Top;
            content.Controls.Add(accountPanel); content.Controls.Add(viewPanel); content.Controls.Add(appPanel);
            Controls.Add(content);
#if LEGACY_ENGINE
            // Outside the tab content and its scroll area: never hidden by tab switching.
            legacyWarning.Dock = DockStyle.Bottom; legacyWarning.Height = 112;
            legacyWarning.Padding = new Padding(Side, 12, Side, 12); Controls.Add(legacyWarning); legacyWarning.TabIndex = 2;
#endif
            Controls.Add(footer); Controls.Add(tabs); Controls.Add(header); if (firstLaunch) Controls.Add(stage);
            // Tab reads the window from the top: its tabs, what they show, then the actions.
            tabs.TabIndex = 0; content.TabIndex = 1; footer.TabIndex = 3;
        }
        private void Wire()
        {
            foreach (var toggle in new[] { readable, tray, animations, externalLinks }) toggle.CheckedChanged += (s, e) => UpdateFooter();
            webDataClear.Click += async (s, e) => await ClearWebDataAsync();
            pinSet.Click += async (s, e) => await PinAsync(SetPin);
            pinChange.Click += async (s, e) => await PinAsync(ChangePin);
            pinDisable.Click += async (s, e) => await PinAsync(DisablePin);
            accountTab.Click += (s, e) => ShowTab(false); viewTab.Click += (s, e) => ShowTab(true); appTab.Click += (s, e) => ShowPanel(appPanel);
            login.Input.TextChanged += (s, e) => { UpdateFooter(); ClearError(); }; password.Input.TextChanged += (s, e) => { UpdateFooter(); ClearError(); };
            password.Reveal.Click += (s, e) => UpdateRevealLabel();
            save.Click += async (s, e) => await CommitAsync(SettingsAction.Save);
            // Signing out also removes the saved login and password: a sign-out that kept them protected nothing, since
            // anyone at the computer could sign in again with them, and the PIN is what guards a computer left open.
            logout.Click += async (s, e) => await CommitAsync(SettingsAction.Remove);
            cancel.Click += async (s, e) => { if (firstLaunch) await CommitAsync(SettingsAction.Manual); else DialogResult = DialogResult.Cancel; };
            FormClosing += (s, e) => FadeOut(e);
            Shown += (s, e) => Settle();
        }
        // A running save holds the window open; a window the pointer closes fades out first.
        private void FadeOut(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; return; }
            if (animationClosed || !Motion.Enabled || Motion.KeyboardInput || !Visible || (e.CloseReason != CloseReason.UserClosing && e.CloseReason != CloseReason.None)) return;
            e.Cancel = true;
            if (closingAnimation) return;
            closingAnimation = true; closeResult = DialogResult;
            appearance?.Cancel(); var from = (float)Opacity; MotionSurface.StopTree(this); Enabled = false;
            appearance = Motion.Run(220, t => { if (!IsDisposed) Opacity = Motion.Lerp(from, 0, Motion.EaseOut(t)); }, () =>
            { if (!IsDisposed) { animationClosed = true; DialogResult = closeResult; Close(); } });
        }
        // The form has its final scale now, so what is placed by measuring goes here: a width measured earlier was already in
        // real pixels when the DPI scale was applied to it.
        private void Settle()
        {
            var area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
            Size = MinimumSize;
            Left = Math.Max(area.Left + 12, Math.Min(Left, area.Right - Width - 12));
            Top = Math.Max(area.Top + 12, Math.Min(Top, area.Bottom - Height - 12));
            laidOut = true; FitTabs(); languagePlate.Snap();
            foreach (var button in new[] { save, cancel, logout, updateCheck, updateInstall, webDataClear }) button.SnapWidth();
            ArrangeApp();
            login.Input.Focus();
            if (fadeIn) appearance = Motion.Run(Motion.Normal, t => { if (!IsDisposed) Opacity = Motion.Lerp(.25f, 1, Motion.EaseOut(t)); });
        }
        private int AccountHeight { get { return original != null && !firstLaunch ? 164 + RowHeight : 150; } }
        private void BuildAccount(Credentials credentials)
        {
            login.Input.Text = credentials?.Login ?? ""; password.Input.Text = credentials?.Password ?? "";
            // Words line up with a field's box, not with the room kept around it for the focus ring.
            var edge = TextInput.Ring; var field = Inner + edge * 2;
            loginLabel.SetBounds(edge, 0, Inner, 18); login.SetBounds(0, 20, field, 40);
            passwordLabel.SetBounds(edge, 66, Inner, 18); password.SetBounds(0, 86, field, 40);
            loginLabel.Font = passwordLabel.Font = Theme.Strong(Theme.Meta); loginLabel.ForeColor = passwordLabel.ForeColor = Theme.Ink;
            error.SetBounds(edge, 130, Inner, 36); error.ForeColor = Theme.Danger; error.Font = Theme.Font(Theme.Meta); error.Visible = false;
            privacy.SetBounds(edge, 130, Inner, 18); privacy.ForeColor = Theme.Muted; privacy.Font = Theme.Font(Theme.Meta);
            localSheet.SetBounds(edge, 164, Inner, RowHeight); localSheet.DesignHeight = RowHeight; localSheet.Lines = new int[0];
            localSheet.Visible = credentials != null && !firstLaunch;
            var words = Inner - Pad * 2 - 120;
            AddRow(localSheet, logoutTitle, logoutHint, 0, words);
            logout.Look = ButtonLook.Danger;
            AddSlot(localSheet, 14, 112, logout);
            foreach (var control in new Control[] { loginLabel, login, passwordLabel, password, privacy, error, localSheet }) accountPanel.Controls.Add(control);
            accountPanel.Height = AccountHeight;
        }
        private void BuildView(Preferences prefs)
        {
            var wide = Inner - Pad * 2; var segment = 78; var from = Inner - Pad - segment * languages.Length - 2;
            viewSheet.SetBounds(TextInput.Ring, 0, Inner, 342); viewSheet.DesignHeight = 342; viewSheet.Lines = new[] { RowHeight, 222, 282 };
            AddRow(viewSheet, languageLabel, languageHint, 0, from - Pad - 8);
            languagePlate = new SelectionPlate(viewSheet, () => Px(Kit.RadiusSmall)) { Style = PlateStyle.Thumb, Ground = () => Theme.Current.Track };
            viewSheet.Paint += (s, e) => PaintLanguageTrack(e.Graphics);
            languageIndex = Math.Max(0, Array.IndexOf(LanguageCodes, prefs.Language));
            for (var i = 0; i < languages.Length; i++)
            {
                var index = i; var button = languages[i];
                button.Look = ButtonLook.Quiet; button.Font = Theme.Font(Theme.Meta); button.AccessibleRole = AccessibleRole.RadioButton; button.Plate = languagePlate;
                button.SetBounds(from + i * segment, 14, segment, 32); button.Text = button.AccessibleName = LanguageNames[i];
                button.Click += (s, e) => SelectLanguage(index);
                viewSheet.Controls.Add(button);
            }
            Mark(languages, languageIndex);
            themeLabel.SetBounds(Pad, RowHeight + 11, wide, 20); themeLabel.Font = Theme.Font(Theme.Body); themeLabel.ForeColor = Theme.Ink; viewSheet.Controls.Add(themeLabel);
            theme = ThemeChoice.Normalize(prefs.Theme);
            var card = (wide - 24) / 3;
            for (var i = 0; i < themes.Length; i++)
            {
                var button = themes[i]; var value = ThemeValues[i];
                button.Look = ButtonLook.Card; button.Font = Theme.Font(Theme.Meta); button.AccessibleRole = AccessibleRole.RadioButton;
                button.Picture = (g, area, scale) => ThemeMiniature.Draw(g, area, scale, value);
                button.SetBounds(Pad + i * (card + 12), 98, card, 112);
                button.Click += (s, e) => SelectTheme(value);
                viewSheet.Controls.Add(button);
            }
            SelectTheme(theme);
            readable.Checked = prefs.Readable; animations.Checked = prefs.Animations;
            AddToggle(viewSheet, readable, 222, wide); AddToggle(viewSheet, animations, 282, wide);
            viewPanel.Height = 342; viewPanel.Controls.Add(viewSheet);
        }
        private void BuildApp(Preferences prefs)
        {
            var wide = Inner - Pad * 2;
            tray.Checked = prefs.Tray; AddToggle(appSheet, tray, 0, wide);
            appSheet.SetBounds(TextInput.Ring, 0, Inner, updates != null ? RowHeight * 2 : RowHeight);
            appSheet.DesignHeight = appSheet.Height; appSheet.Lines = updates != null ? new[] { RowHeight } : new int[0];
            AddRow(appSheet, updateTitle, updateStatus, RowHeight, wide - 120);
            updateCheck.Look = ButtonLook.Outline; updateInstall.Look = ButtonLook.Primary; updateCheck.Hug = updateInstall.Hug = true; updateCheck.Height = updateInstall.Height = 32;
            updateSlot.BackColor = Theme.Surface; updateSlot.SetBounds(Inner - Pad - 300, RowHeight + 14, 300, 32);
            updateCheck.Dock = updateInstall.Dock = DockStyle.Right; updateInstall.Margin = Padding.Empty;
            updateSlot.Controls.Add(updateCheck); updateSlot.Controls.Add(updateGap); updateSlot.Controls.Add(updateInstall);
            appSheet.Controls.Add(updateSlot);
            updateTitle.Visible = updateStatus.Visible = updateSlot.Visible = updates != null;
            securitySheet.SetBounds(TextInput.Ring, appSheet.Bottom + 12, Inner, RowHeight); securitySheet.DesignHeight = RowHeight; securitySheet.Lines = new int[0];
            AddRow(securitySheet, securityTitle, securityHint, 0, wide - 252);
            pinSlot.BackColor = Theme.Surface; pinSlot.SetBounds(Inner - Pad - 240, 14, 240, 32);
            foreach (var button in new[] { pinSet, pinChange, pinDisable }) { button.Look = ButtonLook.Outline; button.Hug = true; button.Height = 32; button.Dock = DockStyle.Right; }
            // Docked right, the last added sits rightmost: Change, then Turn off at the edge.
            pinSlot.Controls.Add(pinSet); pinSlot.Controls.Add(pinChange); pinSlot.Controls.Add(pinGap); pinSlot.Controls.Add(pinDisable);
            securitySheet.Controls.Add(pinSlot); appPanel.Controls.Add(securitySheet);
            externalLinks.Checked = prefs.ExternalLinks; AddToggle(linksSheet, externalLinks, 0, wide);
            linksSheet.SetBounds(TextInput.Ring, appSheet.Bottom + 12, Inner, RowHeight * 2); linksSheet.DesignHeight = RowHeight * 2; linksSheet.Lines = new[] { RowHeight };
            AddRow(linksSheet, webDataTitle, webDataHint, RowHeight, wide - 132);
            webDataClear.Look = ButtonLook.Outline; AddSlot(linksSheet, RowHeight + 14, 120, webDataClear);
            webDataClear.Visible = webDataTitle.Visible = webDataHint.Visible = true;
            appPanel.Controls.Add(linksSheet);
            about.SetBounds(TextInput.Ring, appSheet.Bottom + 12, Inner, 54); about.ForeColor = Theme.Muted; about.Font = Theme.Font(Theme.Meta); about.UseMnemonic = false;
            appPanel.Height = about.Bottom; appPanel.Controls.Add(appSheet); appPanel.Controls.Add(about);
            updateCheck.Click += async (s, e) => await updates.CheckAsync();
            updateInstall.Click += (s, e) =>
            {
                if (!updates.Installed) { OpenReleasePage?.Invoke(); return; }
                UpdateRequested = true; DialogResult = DialogResult.Cancel;
            };
            if (updates != null) updates.Changed += RenderUpdates;
        }
        private static void AddRow(Control sheet, Label title, Label hint, int top, int width)
        {
            title.SetBounds(Pad, top + 11, width, 20); title.Font = Theme.Font(Theme.Body); title.ForeColor = Theme.Ink; title.UseMnemonic = false;
            hint.SetBounds(Pad, top + 31, width, 18); hint.Font = Theme.Font(Theme.Meta); hint.ForeColor = Theme.Muted; hint.UseMnemonic = false;
            sheet.Controls.Add(title); sheet.Controls.Add(hint);
        }
        // A button that hugs its label keeps its right edge on the sheet's inner line.
        private static void AddSlot(Control sheet, int top, int width, ModernButton button)
        {
            var slot = new Panel { BackColor = Theme.Surface }; slot.SetBounds(Inner - Pad - width, top, width, 32);
            button.Hug = true; button.Height = 32; button.Dock = DockStyle.Right; slot.Controls.Add(button); sheet.Controls.Add(slot);
        }
        // The whole row is the switch's target.
        private static void AddToggle(Control sheet, Toggle toggle, int top, int width)
        {
            toggle.SetBounds(Pad, top + 1, width, RowHeight - 2); toggle.Font = Theme.Font(Theme.Body); sheet.Controls.Add(toggle);
        }
        private readonly float zoom;
        private float Px(float value) { return value * Theme.DpiScale * zoom; }
        // The three languages read as one control: the kit's segmented track with its raised thumb sliding inside.
        private void PaintLanguageTrack(Graphics g)
        {
            var track = Rectangle.Inflate(Rectangle.Union(languages[0].Bounds, languages[languages.Length - 1].Bounds), (int)Px(2), (int)Px(2));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(track, Px(Kit.RadiusMedium))) using (var fill = new SolidBrush(Theme.Current.Track)) g.FillPath(fill, path);
            languagePlate.Draw(g, 0, 0);
        }
        private void SelectLanguage(int index)
        {
            if (index == languageIndex) return;
            languageIndex = index; Mark(languages, index);
            Localize();
        }
        private void SelectTheme(string value)
        {
            theme = ThemeChoice.Normalize(value); Mark(themes, Array.IndexOf(ThemeValues, theme));
            UpdateFooter();
        }
        // A radio group: one option is chosen, and it is the group's only Tab stop; the arrows walk the rest.
        private static void Mark(ModernButton[] group, int chosen)
        {
            for (var i = 0; i < group.Length; i++) { group[i].Selected = group[i].TabStop = i == chosen; group[i].Invalidate(); }
        }
        private void ShowTab(bool appearance) { ShowPanel(appearance ? viewPanel : accountPanel); }
        private void ShowPanel(Panel shown)
        {
            var panels = new[] { accountPanel, viewPanel, appPanel }; var buttons = new[] { accountTab, viewTab, appTab };
            var from = Array.FindIndex(buttons, button => button.Selected); var to = Array.IndexOf(panels, shown);
            MotionSurface.Stop(this);
            foreach (var panel in panels) { MotionSurface.Stop(panel); panel.Visible = panel == shown; }
            for (var i = 0; i < buttons.Length; i++) { buttons[i].Selected = i == to; buttons[i].Invalidate(); }
            if (from != to && Visible) MotionSurface.Reveal(shown, to > from ? 1 : -1);
        }
        private void RenderUpdates()
        {
            if (updates == null || IsDisposed) return;
            var lang = Language; var idle = updates.Phase == UpdatePhase.Idle;
            updateStatus.Text = UpdateText.Status(updates, lang);
            updateCheck.Text = Strings.Get("updateCheckShort", lang); updateCheck.AccessibleName = Strings.Get("updateCheck", lang); updateCheck.Enabled = idle;
            updateInstall.Text = updateInstall.AccessibleName = Strings.Get(updates.Installed ? "updateInstall" : "updateOpen", lang);
            updateInstall.Visible = updateGap.Visible = updates.Available != null; updateInstall.Enabled = idle;
            ArrangeApp();
        }
        // The update row grows with its status and its words keep clear of its buttons; the note about the app follows the sheet.
        private void ArrangeApp()
        {
            if (!laidOut || IsDisposed) return;
            var scale = Px(1); Func<int, int> d = value => (int)Math.Round(value * scale);
            var top = d(RowHeight); var height = top;
            if (updates != null)
            {
                var offered = updates.Available != null;
                var buttons = updateCheck.HugWidth + (offered ? updateInstall.HugWidth + d(8) : 0);
                var words = appSheet.Width - d(Pad) * 2 - buttons - d(12);
                var lines = TextRenderer.MeasureText(updateStatus.Text, updateStatus.Font, new Size(words, int.MaxValue), TextFormatFlags.WordBreak).Height + d(2);
                updateTitle.SetBounds(d(Pad), top + d(12), words, d(20));
                updateStatus.SetBounds(d(Pad), updateTitle.Bottom + d(2), words, lines);
                var row = Math.Max(d(RowHeight), updateStatus.Bottom - top + d(12));
                updateSlot.SetBounds(appSheet.Width - d(Pad) - buttons, top + (row - d(32)) / 2, buttons, d(32));
                height = top + row;
            }
            appSheet.Height = height; appSheet.DesignHeight = (int)Math.Round(height / scale); appSheet.Invalidate();
            // The PIN row: Set when there is none, Change and Turn off when there is one; the line under it may wrap.
            var on = PinOn;
            pinSet.Visible = !on; pinChange.Visible = pinDisable.Visible = pinGap.Visible = on;
            var pinButtons = on ? pinChange.HugWidth + d(8) + pinDisable.HugWidth : pinSet.HugWidth;
            var pinWords = securitySheet.Width - d(Pad) * 2 - pinButtons - d(12);
            var pinLines = TextRenderer.MeasureText(securityHint.Text, securityHint.Font, new Size(pinWords, int.MaxValue), TextFormatFlags.WordBreak).Height + d(2);
            securityTitle.SetBounds(d(Pad), d(12), pinWords, d(20));
            securityHint.SetBounds(d(Pad), securityTitle.Bottom + d(2), pinWords, pinLines);
            var pinRow = Math.Max(d(RowHeight), securityHint.Bottom + d(12));
            pinSlot.SetBounds(securitySheet.Width - d(Pad) - pinButtons, (pinRow - d(32)) / 2, pinButtons, d(32));
            securitySheet.SetBounds(securitySheet.Left, appSheet.Bottom + d(12), securitySheet.Width, pinRow);
            securitySheet.DesignHeight = (int)Math.Round(pinRow / scale); securitySheet.Invalidate();
            // The data row's hint may wrap (Kazakh, a larger zoom): the row grows and its button stays centred.
            var dataWords = linksSheet.Width - d(Pad) * 2 - webDataClear.HugWidth - d(12);
            var dataLines = TextRenderer.MeasureText(webDataHint.Text, webDataHint.Font, new Size(dataWords, int.MaxValue), TextFormatFlags.WordBreak).Height + d(2);
            webDataTitle.SetBounds(d(Pad), d(RowHeight) + d(12), dataWords, d(20));
            webDataHint.SetBounds(d(Pad), webDataTitle.Bottom + d(2), dataWords, dataLines);
            var dataRow = Math.Max(d(RowHeight), webDataHint.Bottom - d(RowHeight) + d(12));
            webDataClear.Parent.SetBounds(linksSheet.Width - d(Pad) - webDataClear.HugWidth, d(RowHeight) + (dataRow - d(32)) / 2, webDataClear.HugWidth, d(32));
            linksSheet.SetBounds(linksSheet.Left, securitySheet.Bottom + d(12), linksSheet.Width, d(RowHeight) + dataRow);
            linksSheet.DesignHeight = (int)Math.Round(linksSheet.Height / scale); linksSheet.Invalidate();
            var note = TextRenderer.MeasureText(about.Text, about.Font, new Size(about.Width, int.MaxValue), TextFormatFlags.WordBreak).Height + d(2);
            about.SetBounds(about.Left, linksSheet.Bottom + d(12), about.Width, note);
            appPanel.Height = about.Bottom;
        }
        // The PIN windows open over Settings; after them the row shows what is now true.
        private async Task PinAsync(Func<IWin32Window, Task<bool>> run)
        {
            if (busy || pinBusy || run == null) return;
            pinBusy = true; pinSet.Enabled = pinChange.Enabled = pinDisable.Enabled = false;
            try { await run(this); }
            catch (Exception) { }
            finally { pinBusy = false; }
            if (IsDisposed) return;
            pinSet.Enabled = pinChange.Enabled = pinDisable.Enabled = true;
            securityHint.Text = Strings.Get(PinOn ? "securityOn" : "securityOff", Language); ArrangeApp();
            (PinOn ? pinChange : pinSet).Focus();
        }
        private async Task ClearWebDataAsync()
        {
            if (busy || ClearWebData == null) return;
            webDataClear.Enabled = false; webDataClear.Busy = true; UseWaitCursor = true;
            bool cleared;
            try { cleared = await ClearWebData(); }
            catch (Exception) { cleared = false; }
            if (IsDisposed) return;
            webDataClear.Busy = false; webDataClear.Enabled = true; UseWaitCursor = false;
            webDataState = cleared ? "webDataCleared" : "webDataFailed";
            webDataHint.Text = Strings.Get(webDataState, Language); webDataHint.ForeColor = cleared ? Theme.Success : Theme.Danger;
            webDataHint.AccessibleName = webDataHint.Text; ArrangeApp();
        }
        private async Task CommitAsync(SettingsAction action)
        {
            if (busy) return; var next = Credentials;
            if (action == SettingsAction.Save && (((firstLaunch || original != null) && next == null) || (next != null && (next.Login.Length == 0 || next.Password.Length == 0))))
            {
                ShowTab(false); error.Visible = true; error.Text = Strings.Get("required", Language);
                ArrangeError();
                login.Invalid = next == null || next.Login.Length == 0; password.Invalid = next == null || next.Password.Length == 0;
                if (login.Invalid) login.Input.Focus(); else password.Input.Focus(); return;
            }
            busy = true; error.Visible = false; SetBusy(); var succeeded = false;
            try { succeeded = ApplyAsync == null || await ApplyAsync(action, next, Language, Readable); }
            catch (Exception) { succeeded = false; }
            finally { busy = false; if (!IsDisposed) SetBusy(); }
            if (IsDisposed) return;
            if (succeeded) { Action = action; DialogResult = DialogResult.OK; }
            else { ShowTab(false); error.Visible = true; error.Text = Strings.Get("saveFailed", Language); ArrangeError(); }
        }
        // The message sits under the fields it is about; whatever follows moves down with it, and fixing a field clears it.
        private void ArrangeError()
        {
            var scale = Px(1);
            var room = error.Visible ? 36 : 0;
            accountPanel.Height = (int)((AccountHeight + room) * scale);
            int privacyFrom = privacy.Top, sheetFrom = localSheet.Top;
            int privacyTo = (int)((130 + room) * scale), sheetTo = (int)((164 + room) * scale);
            errorMove?.Cancel();
            errorMove = Motion.Run(Kit.Moving(this) ? Motion.Slide : 0, t =>
            {
                var e = Motion.Travel(t); if (IsDisposed) return;
                privacy.Top = Motion.Lerp(privacyFrom, privacyTo, e); localSheet.Top = Motion.Lerp(sheetFrom, sheetTo, e);
            });
        }
        private Tween errorMove;
        private void ClearError() { if (!error.Visible || busy) return; error.Visible = false; ArrangeError(); }
        private void SetBusy()
        {
            content.Enabled = accountTab.Enabled = viewTab.Enabled = appTab.Enabled = cancel.Enabled = !busy;
            save.Enabled = !busy && Changed;
            Caption(save, busy ? "saving" : SaveKey()); save.Busy = busy; save.Invalidate(); UseWaitCursor = busy;
        }
        private string SaveKey()
        {
            var change = SettingsDecision.Classify(original, Credentials, paused);
            return firstLaunch || change == SettingsChange.NewCredentials ? "save" : change == SettingsChange.ResumeSignIn ? "resume" : "saveChanges";
        }
        private bool Changed
        {
            get
            {
                return SaveKey() != "saveChanges" || Language != startLanguage || theme != startTheme || readable.Checked != startReadable || tray.Checked != startTray || animations.Checked != startAnimations || externalLinks.Checked != startExternalLinks ||
                    login.Input.Text != (original?.Login ?? "") || password.Input.Text != (original?.Password ?? "");
            }
        }
        // Nothing to save: the window is quiet and its second button simply closes it.
        private void UpdateFooter()
        {
            if (busy) return;
            var changed = Changed; Caption(save, SaveKey()); save.Enabled = changed;
            if (!firstLaunch) Caption(cancel, changed ? "cancelChanges" : "closeWindow");
        }
        private void Caption(ModernButton button, string key) { button.Text = button.AccessibleName = Strings.Get(key, Language); }
        private void UpdateRevealLabel() { password.Reveal.AccessibleName = Strings.Get(password.Input.UseSystemPasswordChar ? "showPassword" : "hidePassword", Language); }
        private void Localize()
        {
            var lang = Language; Text = Strings.Get(firstLaunch ? "signInTitle" : "settings", lang); heading.Text = Text; stage.SetLanguage(lang);
#if LEGACY_ENGINE
            legacyWarning.SetLanguage(lang);
#endif
            description.Text = firstLaunch ? Strings.Get("signInDescription", lang) : "";
            accountTab.Text = Strings.Get("account", lang); viewTab.Text = Strings.Get("appearance", lang); appTab.Text = Strings.Get("appTab", lang);
            loginLabel.Text = Strings.Get("login", lang); passwordLabel.Text = Strings.Get("password", lang);
            login.Input.AccessibleName = loginLabel.Text; password.Input.AccessibleName = passwordLabel.Text; UpdateRevealLabel();
            privacy.Text = Strings.Get("privacy", lang);
            logoutTitle.Text = Strings.Get("logout", lang); logoutHint.Text = Strings.Get("logoutHint", lang); logout.Text = Strings.Get("logoutAction", lang);
            languageLabel.Text = Strings.Get("language", lang); languageHint.Text = Strings.Get("languageHint", lang);
            themeLabel.Text = Strings.Get("theme", lang);
            for (var i = 0; i < themes.Length; i++) themes[i].Text = themes[i].AccessibleName = Strings.Get(ThemeNames[i], lang);
            readable.Text = Strings.Get("reading", lang); readable.Hint = Strings.Get("viewHint", lang);
            animations.Text = Strings.Get("animationsOption", lang); animations.Hint = Strings.Get("animationsOptionHint", lang);
            tray.Text = Strings.Get("trayOption", lang); tray.Hint = Strings.Get("trayOptionHint", lang);
            externalLinks.Text = Strings.Get("externalLinksOption", lang); externalLinks.Hint = Strings.Get("externalLinksOptionHint", lang);
            webDataTitle.Text = Strings.Get("webDataTitle", lang); webDataHint.Text = Strings.Get(webDataState.Length > 0 ? webDataState : "webDataHint", lang);
            webDataClear.Text = Strings.Get("webDataClear", lang); webDataClear.AccessibleName = webDataTitle.Text + ": " + webDataClear.Text;
            securityTitle.Text = Strings.Get("securityTitle", lang); securityHint.Text = Strings.Get(PinOn ? "securityOn" : "securityOff", lang);
            pinSet.Text = Strings.Get("securitySet", lang); pinChange.Text = Strings.Get("securityChange", lang); pinDisable.Text = Strings.Get("securityDisable", lang);
            foreach (var button in new[] { pinSet, pinChange, pinDisable }) button.AccessibleName = securityTitle.Text + ": " + button.Text;
            updateTitle.Text = Strings.Get("updateSection", lang);
            about.Text = string.Format(Strings.Get("about", lang), AppInfo.Version, AppInfo.Flavor, AppInfo.Chromium);
            if (firstLaunch) Caption(cancel, "manualButton");
            foreach (var button in new[] { accountTab, viewTab, appTab }) button.AccessibleName = button.Text;
            logout.AccessibleName = logoutTitle.Text + ": " + logoutHint.Text;
            UpdateFooter(); FitTabs(); RenderUpdates(); ArrangeApp();
        }
        private void FitTabs()
        {
            foreach (var tab in new[] { accountTab, viewTab, appTab }) tab.Width = TextRenderer.MeasureText(tab.Text, tab.Font).Width + (int)Px(4);
            tabPlate.Snap();
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { if (updates != null) updates.Changed -= RenderUpdates; appearance?.Cancel(); errorMove?.Cancel(); password.Input.Clear(); login.Input.Clear(); } base.Dispose(disposing); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Motion.KeyboardInput = true; MotionSurface.Stop(accountPanel); MotionSurface.Stop(viewPanel); MotionSurface.Stop(appPanel);
            MotionSurface.Stop(this);
            appearance?.Cancel(); Opacity = 1;
            if (closingAnimation) { animationClosed = true; DialogResult = closeResult; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            var forward = keyData == Keys.Right || keyData == Keys.Down;
            if (forward || keyData == Keys.Left || keyData == Keys.Up)
                foreach (var group in new[] { languages, themes })
                {
                    var at = Array.IndexOf(group, ActiveControl);
                    if (at < 0) continue;
                    var next = group[(at + (forward ? 1 : group.Length - 1)) % group.Length];
                    next.Focus(); next.PerformClick(); return true;
                }
            return base.ProcessDialogKey(keyData);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00A1) Motion.KeyboardInput = false;
            base.WndProc(ref m);
        }
    }
}
