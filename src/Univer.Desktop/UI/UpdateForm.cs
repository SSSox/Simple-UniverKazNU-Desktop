using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // The window at entry and the download behind it: Later and Install, then progress with Cancel.
    internal sealed class UpdateForm : Form
    {
        private readonly UpdateService updates;
        private readonly string language, version;
        private readonly bool installNow;
        private readonly Label heading = new Label(), message = new Label(), status = new Label(), error = new Label();
        private readonly CheckBox neverShow = new CheckBox();
        private readonly Panel bar = new Panel();
        private readonly ModernButton later = new ModernButton(), install = new ModernButton();
        private bool working, answered;
        public Action OpenPage { get; set; }
        public string SetupPath { get; private set; }
        internal CheckBox NeverShowForQa { get { return neverShow; } }
        internal ModernButton LaterForQa { get { return later; } }
        internal ModernButton InstallForQa { get { return install; } }
        internal string ErrorForQa { get { return error.Visible ? error.Text : ""; } }
        public UpdateForm(UpdateService updates, string language, bool installNow = false)
        {
            this.updates = updates; this.language = language; this.installNow = installNow; version = updates.Available;
            Text = Strings.Get("updateTitle", language);
            Font = Theme.Font(10.5f); ForeColor = Theme.Ink; BackColor = Theme.Surface; Theme.Window(this);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(520, 300); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 24, 30, 8) };
            heading.Dock = DockStyle.Top; heading.Height = 40; heading.Font = Theme.Strong(17); heading.UseMnemonic = false; heading.Text = Text;
            message.Dock = DockStyle.Top; message.Height = 76; message.UseMnemonic = false;
            message.Text = string.Format(Strings.Get(updates.Installed ? "updateMessage" : "updateMessagePortable", language), version, updates.Current);
            neverShow.Dock = DockStyle.Top; neverShow.Height = 34; neverShow.Text = neverShow.AccessibleName = Strings.Get("updateNever", language);
            status.Dock = DockStyle.Top; status.Height = 28; status.UseMnemonic = false;
            bar.Dock = DockStyle.Top; bar.Height = 8; bar.BackColor = Theme.Border; bar.AccessibleRole = AccessibleRole.ProgressBar;
            bar.Paint += (s, e) => { using (var fill = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(fill, 0, 0, bar.Width * Shown100() / 100, bar.Height); };
            error.Dock = DockStyle.Fill; error.ForeColor = Theme.Danger; error.Font = Theme.Font(9.5f); error.UseMnemonic = false; error.Visible = false;
            body.Controls.Add(error); body.Controls.Add(bar); body.Controls.Add(status); body.Controls.Add(neverShow); body.Controls.Add(message); body.Controls.Add(heading);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(30, 20, 30, 20), BackColor = Theme.Background };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            later.Look = ButtonLook.Outline; later.Dock = DockStyle.Left; later.Width = 130; later.BackColor = Theme.Surface;
            install.Look = ButtonLook.Primary; install.Dock = DockStyle.Right; install.Width = 240;
            install.Text = install.AccessibleName = Strings.Get(updates.Installed ? "updateInstall" : "updateOpen", language);
            footer.Controls.Add(later); footer.Controls.Add(install); Controls.Add(body); Controls.Add(footer);
            AcceptButton = install; CancelButton = later;
            later.Click += (s, e) => { if (working) updates.Cancel(); else Close(); };
            install.Click += async (s, e) => await InstallAsync();
            updates.Changed += Render;
            // Closing the window is Later; during the download it is Cancel, and the window closes when that has finished.
            FormClosing += (s, e) =>
            {
                if (working && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; updates.Cancel(); return; }
                if (answered) return;
                answered = true;
                if (neverShow.Checked) updates.Skip(version); else updates.Later();
            };
            Shown += async (s, e) =>
            {
                var area = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                Left = Math.Max(area.Left + 12, Math.Min(Left, area.Right - Width - 12));
                Top = Math.Max(area.Top + 12, Math.Min(Top, area.Bottom - Height - 12));
                if (this.installNow) await InstallAsync(); else install.Focus();
            };
            Arrange();
        }
        private int Shown100() { return updates.Phase == UpdatePhase.Verifying ? 100 : updates.Percent; }
        private async Task InstallAsync()
        {
            if (working) return;
            if (!updates.Installed) { answered = true; updates.Later(); OpenPage?.Invoke(); Close(); return; }
            working = true; answered = true; Arrange();
            var setup = await updates.DownloadAsync();
            working = false;
            if (IsDisposed) return;
            if (setup != null || updates.Failure == UpdateFailure.None) { SetupPath = setup; Close(); return; }
            Arrange();
        }
        private void Arrange()
        {
            neverShow.Visible = !working && !installNow; status.Visible = bar.Visible = working; install.Visible = !working;
            later.Text = later.AccessibleName = Strings.Get(working ? "updateCancel" : "updateLater", language);
            Render();
        }
        private void Render()
        {
            if (IsDisposed) return;
            status.Text = bar.AccessibleName = updates.Phase == UpdatePhase.Verifying ? Strings.Get("updatePreparing", language) : string.Format(Strings.Get("updateDownloading", language), updates.Percent);
            bar.Invalidate();
            var failure = UpdateText.Failure(updates.Failure, language);
            error.Text = failure ?? ""; error.Visible = !working && failure != null;
        }
        protected override void Dispose(bool disposing) { if (disposing) updates.Changed -= Render; base.Dispose(disposing); }
    }
    internal static class UpdateText
    {
        public static string Failure(UpdateFailure failure, string language)
        {
            switch (failure)
            {
                case UpdateFailure.Check: return Strings.Get("updateCheckFailed", language);
                case UpdateFailure.Download: return Strings.Get("updateDownloadFailed", language);
                case UpdateFailure.Verify: return Strings.Get("updateVerifyFailed", language);
                case UpdateFailure.Signature: return Strings.Get("updateSignatureFailed", language);
                case UpdateFailure.Install: return Strings.Get("updateInstallFailed", language);
                default: return null;
            }
        }
        // The Settings lines: the running version, what the last check found, and when it ran.
        public static string Status(UpdateService updates, string language)
        {
            var found = updates.Phase == UpdatePhase.Checking ? Strings.Get("updateChecking", language) :
                updates.Phase == UpdatePhase.Downloading ? string.Format(Strings.Get("updateDownloading", language), updates.Percent) :
                updates.Phase == UpdatePhase.Verifying ? Strings.Get("updatePreparing", language) :
                Failure(updates.Failure, language) ?? (updates.Available != null ? string.Format(Strings.Get("updateAvailable", language), updates.Available) :
                updates.CheckedAt > 0 ? Strings.Get("updateLatest", language) : null);
            var text = string.Format(Strings.Get("updateCurrent", language), updates.Current);
            if (found != null) text += "\n" + found;
            if (updates.CheckedAt > 0) text += "\n" + string.Format(Strings.Get("updateChecked", language), new DateTime(updates.CheckedAt, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
            return text;
        }
    }
}
