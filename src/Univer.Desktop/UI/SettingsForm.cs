using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal enum SettingsAction { None, Save, Logout, Remove, Manual }
    internal sealed class SettingsForm : Form
    {
        private readonly Credentials original;
        private readonly bool firstLaunch, paused;
        private bool busy;
        private bool closingAnimation, animationClosed;
        private DialogResult closeResult;
        private Tween appearance;
        private readonly TextInput login = new TextInput(false), password = new TextInput(true);
        private readonly ComboBox language = new FadeComboBox();
        private readonly CheckBox readable = new CheckBox(), tray = new CheckBox(), animations = new CheckBox();
        private readonly Label heading = new Label(), description = new Label(), error = new Label();
        private readonly Label loginLabel = new Label(), passwordLabel = new Label(), accountHint = new Label(), privacy = new Label();
        private readonly Label languageLabel = new Label(), languageHint = new Label(), viewHint = new Label(), localHint = new Label(), trayHint = new Label(), animationsHint = new Label(), about = new Label();
        private readonly Label themeLabel = new Label(), themeHint = new Label();
        // Light, dark or as Windows: three buttons that act as one choice.
        private readonly ModernButton themeSystem = new ModernButton(), themeLight = new ModernButton(), themeDark = new ModernButton();
        private string theme;
        private readonly ModernButton accountTab = new ModernButton(), viewTab = new ModernButton();
        private readonly ModernButton save = new ModernButton(), logout = new ModernButton(), remove = new ModernButton(), cancel = new ModernButton();
        private readonly Panel accountPanel = new Panel(), viewPanel = new Panel(), content = new Panel();
        private readonly FlowLayoutPanel localActions = new FlowLayoutPanel();
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
        public Func<SettingsAction, Credentials, string, bool, Task<bool>> ApplyAsync { get; set; }
        public Credentials Credentials { get { return login.Input.Text.Length == 0 && password.Input.Text.Length == 0 ? null : new Credentials { Login = login.Input.Text.Trim(), Password = password.Input.Text }; } }
        public string Language { get { return language.SelectedIndex == 1 ? "kz" : language.SelectedIndex == 2 ? "en" : "ru"; } }
        public bool Readable { get { return readable.Checked; } }
        public bool Tray { get { return tray.Checked; } }
        public bool Animations { get { return animations.Checked; } }
        public string SelectedTheme { get { return theme; } }
        internal ModernButton SaveButton { get { return save; } }
        internal TextInput LoginInput { get { return login; } }
        internal TextInput PasswordInput { get { return password; } }
        internal void SelectAppearanceForQa() { ShowTab(true); }
        internal bool AboutFitsForQa()
        {
            var shown = content.RectangleToClient(about.RectangleToScreen(about.ClientRectangle));
            var text = TextRenderer.MeasureText(about.Text, about.Font, new Size(about.Width, int.MaxValue), TextFormatFlags.WordBreak);
            return about.Visible && shown.Top >= 0 && shown.Bottom <= content.ClientSize.Height && text.Height <= about.Height && about.Text.Contains(AppInfo.Version + " (" + AppInfo.Flavor + ", Chromium ");
        }
        public SettingsForm(Credentials credentials, Preferences prefs, bool firstLaunch = false)
        {
            original = credentials; this.firstLaunch = firstLaunch; paused = prefs.AutoLoginPaused;
            Font = Theme.Font(10.5f); BackColor = Theme.Surface; ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(552, firstLaunch ? 486 : 854); MinimumSize = Size;
#if LEGACY_ENGINE
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 112); MinimumSize = Size;
#endif
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            Theme.Window(this);
            var header = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(30, 24, 30, 8) };
            heading.Font = Theme.Strong(18); heading.Dock = DockStyle.Top; heading.Height = 36; heading.UseMnemonic = false;
            description.Dock = DockStyle.Fill; description.ForeColor = Theme.Muted; header.Controls.Add(description); header.Controls.Add(heading);
            var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(28, 4, 30, 8), WrapContents = false };
            accountTab.Icon = UiIcon.Person; viewTab.Icon = UiIcon.Eye; accountTab.Width = 200; viewTab.Width = 220; accountTab.Height = viewTab.Height = 40;
            accountTab.Look = viewTab.Look = ButtonLook.Navigation; tabs.Controls.Add(accountTab); tabs.Controls.Add(viewTab); tabs.Visible = !firstLaunch;
            tabs.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 30, tabs.Height - 1, tabs.Width - 30, tabs.Height - 1); };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(30, 20, 30, 20), BackColor = Theme.Background };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            save.Look = ButtonLook.Primary; save.Width = 240; save.Height = 44; save.Dock = DockStyle.Right;
            cancel.Look = ButtonLook.Outline; cancel.Width = firstLaunch ? 200 : 130; cancel.Height = 44; cancel.Dock = DockStyle.Left; cancel.BackColor = Theme.Surface;
            footer.Controls.Add(save); footer.Controls.Add(cancel);
            content.Dock = DockStyle.Fill; content.Padding = new Padding(30, 16, 30, 12); content.AutoScroll = true;
            accountPanel.Dock = viewPanel.Dock = DockStyle.Top; accountPanel.Height = firstLaunch ? 240 : 340; viewPanel.Height = 586;
            content.Controls.Add(accountPanel); content.Controls.Add(viewPanel);
            Controls.Add(content);
#if LEGACY_ENGINE
            // Outside the tab content and its scroll area: never hidden by tab switching.
            legacyWarning.Dock = DockStyle.Bottom; legacyWarning.Height = 112;
            legacyWarning.Padding = new Padding(30, 12, 30, 12); Controls.Add(legacyWarning);
#endif
            Controls.Add(footer); Controls.Add(tabs); Controls.Add(header);
            login.Input.Text = credentials?.Login ?? ""; password.Input.Text = credentials?.Password ?? "";
            loginLabel.SetBounds(0, 0, 490, 22); login.SetBounds(0, 26, 490, 44);
            accountHint.SetBounds(0, 74, 490, 18); accountHint.Font = Theme.Font(9); accountHint.ForeColor = Theme.Muted;
            passwordLabel.SetBounds(0, 104, 490, 22); password.SetBounds(0, 130, 490, 44);
            loginLabel.Font = passwordLabel.Font = Theme.Strong(10);
            privacy.SetBounds(0, 184, 490, 40); privacy.ForeColor = Theme.Muted; privacy.Font = Theme.Font(9.5f);
            error.SetBounds(0, 230, 490, 40); error.ForeColor = Theme.Danger; error.Visible = false;
            localHint.SetBounds(0, 236, 490, 42); localHint.ForeColor = Theme.Muted; localHint.Font = Theme.Font(9.5f);
            localActions.SetBounds(0, 286, 490, 44); localActions.WrapContents = false; localActions.Padding = Padding.Empty;
            logout.Look = remove.Look = ButtonLook.Danger; logout.Width = 190; remove.Width = 250; logout.Height = remove.Height = 40;
            logout.Margin = new Padding(0, 0, 10, 0); remove.Margin = Padding.Empty;
            localActions.Controls.Add(logout); localActions.Controls.Add(remove); localHint.Visible = localActions.Visible = credentials != null && !firstLaunch;
            foreach (var control in new Control[] { loginLabel, login, accountHint, passwordLabel, password, privacy, error, localHint, localActions }) accountPanel.Controls.Add(control);
            languageLabel.SetBounds(0, 0, 490, 22); languageLabel.Font = Theme.Strong(10); language.SetBounds(0, 28, 490, 34);
            language.Font = Theme.Font(12); language.DropDownStyle = ComboBoxStyle.DropDownList;
            language.Items.AddRange(new object[] { "Русский", "Қазақша", "English" }); language.SelectedIndex = prefs.Language == "kz" ? 1 : prefs.Language == "en" ? 2 : 0;
            languageHint.SetBounds(0, 70, 490, 44); languageHint.ForeColor = Theme.Muted; languageHint.Font = Theme.Font(9.5f);
            themeLabel.SetBounds(0, 126, 490, 22); themeLabel.Font = Theme.Strong(10);
            theme = ThemeChoice.Normalize(prefs.Theme);
            var themes = new[] { themeSystem, themeLight, themeDark }; var values = new[] { ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark };
            for (var i = 0; i < themes.Length; i++)
            {
                var button = themes[i]; var value = values[i];
                button.Look = ButtonLook.Outline; button.Font = Theme.Font(10); button.AccessibleRole = AccessibleRole.RadioButton;
                button.SetBounds(i * 166, 152, 158, 40);
                button.Click += (s, e) => SelectTheme(value);
            }
            themeHint.SetBounds(0, 198, 490, 40); themeHint.ForeColor = Theme.Muted; themeHint.Font = Theme.Font(9.5f);
            readable.SetBounds(0, 242, 490, 30); readable.Checked = prefs.Readable; readable.AutoSize = false; readable.Font = Theme.Strong(10);
            viewHint.SetBounds(0, 276, 490, 66); viewHint.ForeColor = Theme.Muted; viewHint.Font = Theme.Font(9.5f);
            tray.SetBounds(0, 352, 490, 30); tray.Checked = prefs.Tray; tray.AutoSize = false; tray.Font = Theme.Strong(10);
            trayHint.SetBounds(0, 386, 490, 48); trayHint.ForeColor = Theme.Muted; trayHint.Font = Theme.Font(9.5f);
            animations.SetBounds(0, 442, 490, 30); animations.Checked = prefs.Animations; animations.AutoSize = false; animations.Font = Theme.Strong(10);
            animationsHint.SetBounds(0, 476, 490, 48); animationsHint.ForeColor = Theme.Muted; animationsHint.Font = Theme.Font(9.5f);
            about.SetBounds(0, 532, 490, 54); about.ForeColor = Theme.Muted; about.Font = Theme.Font(9); about.UseMnemonic = false;
            foreach (var control in new Control[] { languageLabel, language, languageHint, themeLabel, themeSystem, themeLight, themeDark, themeHint, readable, viewHint, tray, trayHint, animations, animationsHint, about }) viewPanel.Controls.Add(control);
            SelectTheme(theme);
            accountTab.Click += (s, e) => ShowTab(false); viewTab.Click += (s, e) => ShowTab(true);
            language.SelectedIndexChanged += (s, e) => Localize();
            login.Input.TextChanged += (s, e) => UpdateSaveCaption(); password.Input.TextChanged += (s, e) => UpdateSaveCaption();
            password.Reveal.Click += (s, e) => UpdateRevealLabel();
            save.Click += async (s, e) => await CommitAsync(SettingsAction.Save);
            logout.Click += async (s, e) => await CommitAsync(SettingsAction.Logout);
            remove.Click += async (s, e) => await CommitAsync(SettingsAction.Remove);
            cancel.Click += async (s, e) => { if (firstLaunch) await CommitAsync(SettingsAction.Manual); else DialogResult = DialogResult.Cancel; };
            FormClosing += (s, e) =>
            {
                if (busy) { e.Cancel = true; return; }
                if (animationClosed || !Motion.Enabled || Motion.KeyboardInput || !Visible || (e.CloseReason != CloseReason.UserClosing && e.CloseReason != CloseReason.None)) return;
                e.Cancel = true;
                if (closingAnimation) return;
                closingAnimation = true; closeResult = DialogResult;
                appearance?.Cancel(); var from = (float)Opacity; MotionSurface.StopTree(this); Enabled = false;
                appearance = Motion.Run(220, t => { if (!IsDisposed) Opacity = Motion.Lerp(from, 0, Motion.EaseOut(t)); }, () =>
                { if (!IsDisposed) { animationClosed = true; DialogResult = closeResult; Close(); } });
            };
            AcceptButton = save; if (!firstLaunch) CancelButton = cancel;
            var animateAppearance = Motion.Enabled && !Motion.KeyboardInput;
            if (animateAppearance) Opacity = .25;
            Shown += (s, e) =>
            {
                var area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                Size = MinimumSize;
                Left = Math.Max(area.Left + 12, Math.Min(Left, area.Right - Width - 12));
                Top = Math.Max(area.Top + 12, Math.Min(Top, area.Bottom - Height - 12));
                login.Input.Focus();
                if (animateAppearance)
                {
                    appearance = Motion.Run(Motion.Normal, t => { if (!IsDisposed) Opacity = Motion.Lerp(.25f, 1, Motion.EaseOut(t)); });
                }
            };
            ShowTab(false); Localize();
        }
        private void SelectTheme(string value)
        {
            theme = ThemeChoice.Normalize(value);
            foreach (var pair in new[] { Tuple.Create(themeSystem, ThemeChoice.System), Tuple.Create(themeLight, ThemeChoice.Light), Tuple.Create(themeDark, ThemeChoice.Dark) })
            {
                var chosen = pair.Item2 == theme;
                pair.Item1.Selected = chosen; pair.Item1.Icon = chosen ? UiIcon.Check : UiIcon.None;
                pair.Item1.Invalidate();
            }
        }
        private void ShowTab(bool appearance)
        {
            var changed = appearance != viewTab.Selected;
            MotionSurface.Stop(this);
            MotionSurface.Stop(accountPanel); MotionSurface.Stop(viewPanel);
            viewPanel.Visible = appearance; accountPanel.Visible = !appearance;
            accountTab.Selected = !appearance; viewTab.Selected = appearance; accountTab.Invalidate(); viewTab.Invalidate();
            if (changed && Visible) MotionSurface.Reveal(appearance ? viewPanel : accountPanel, appearance ? 1 : -1);
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
        private void ArrangeError()
        {
            float scale; using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            localHint.Top = (int)((error.Visible ? 278 : 236) * scale); localActions.Top = (int)((error.Visible ? 328 : 286) * scale);
            accountPanel.Height = (int)((firstLaunch ? (error.Visible ? 280 : 240) : (error.Visible ? 384 : 340)) * scale);
        }
        private void SetBusy()
        {
            content.Enabled = accountTab.Enabled = viewTab.Enabled = save.Enabled = cancel.Enabled = !busy;
            save.Text = Strings.Get(busy ? "saving" : SaveKey(), Language); save.Invalidate(); UseWaitCursor = busy;
        }
        private string SaveKey()
        {
            var change = SettingsDecision.Classify(original, Credentials, paused);
            return firstLaunch || change == SettingsChange.NewCredentials ? "save" : change == SettingsChange.ResumeSignIn ? "resume" : "saveChanges";
        }
        private void UpdateSaveCaption() { if (!busy) save.Text = Strings.Get(SaveKey(), Language); }
        private void UpdateRevealLabel() { password.Reveal.AccessibleName = Strings.Get(password.Input.UseSystemPasswordChar ? "showPassword" : "hidePassword", Language); }
        private void Localize()
        {
            var lang = Language; Text = Strings.Get(firstLaunch ? "signInTitle" : "settings", lang); heading.Text = Text;
#if LEGACY_ENGINE
            legacyWarning.SetLanguage(lang);
#endif
            description.Text = Strings.Get(firstLaunch ? "signInDescription" : "settingsDescription", lang);
            accountTab.Text = Strings.Get("account", lang); viewTab.Text = Strings.Get("appearance", lang);
            loginLabel.Text = Strings.Get("login", lang); passwordLabel.Text = Strings.Get("password", lang);
            login.Input.AccessibleName = loginLabel.Text; password.Input.AccessibleName = passwordLabel.Text; UpdateRevealLabel();
            accountHint.Text = Strings.Get("accountHint", lang); privacy.Text = Strings.Get("privacy", lang);
            languageLabel.Text = Strings.Get("language", lang); language.AccessibleName = languageLabel.Text;
            languageHint.Text = Strings.Get("languageHint", lang); readable.Text = Strings.Get("reading", lang); viewHint.Text = Strings.Get("viewHint", lang);
            themeLabel.Text = Strings.Get("theme", lang); themeHint.Text = Strings.Get("themeHint", lang);
            themeSystem.Text = Strings.Get("themeSystem", lang); themeLight.Text = Strings.Get("themeLight", lang); themeDark.Text = Strings.Get("themeDark", lang);
            tray.Text = Strings.Get("trayOption", lang); trayHint.Text = Strings.Get("trayOptionHint", lang);
            animations.Text = Strings.Get("animationsOption", lang); animationsHint.Text = Strings.Get("animationsOptionHint", lang);
            about.Text = string.Format(Strings.Get("about", lang), AppInfo.Version, AppInfo.Flavor, AppInfo.Chromium);
            localHint.Text = Strings.Get("localAccountHint", lang); logout.Text = Strings.Get("logout", lang); remove.Text = Strings.Get("forget", lang);
            cancel.Text = Strings.Get(firstLaunch ? "manualButton" : "cancelChanges", lang); UpdateSaveCaption();
            foreach (var button in new[] { accountTab, viewTab, save, logout, remove, cancel, themeSystem, themeLight, themeDark }) button.AccessibleName = button.Text;
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { appearance?.Cancel(); password.Input.Clear(); login.Input.Clear(); } base.Dispose(disposing); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Motion.KeyboardInput = true; MotionSurface.Stop(accountPanel); MotionSurface.Stop(viewPanel);
            MotionSurface.Stop(this);
            appearance?.Cancel(); Opacity = 1;
            if (closingAnimation) { animationClosed = true; DialogResult = closeResult; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00A1) Motion.KeyboardInput = false;
            base.WndProc(ref m);
        }
    }
}
