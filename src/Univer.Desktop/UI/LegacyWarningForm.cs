#if LEGACY_ENGINE
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    internal sealed class LegacyWarningForm : Form
    {
        private readonly string language;
        private readonly CheckBox neverShow = new CheckBox();
        private readonly ModernButton confirm = new ModernButton();
        private readonly Label error = new Label();
        private bool busy, acknowledged;
        public Func<bool, Task<bool>> ConfirmAsync { get; set; }
        internal CheckBox NeverShowForQa { get { return neverShow; } }
        internal ModernButton ConfirmForQa { get { return confirm; } }
        internal bool SaveErrorVisible { get { return error.Visible; } }
        public LegacyWarningForm(string language)
        {
            this.language = language;
            Text = Strings.Get("legacyWarningTitle", language);
            Font = Theme.Font(10.5f); ForeColor = Theme.Ink; BackColor = Theme.Surface; Theme.Window(this);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(552, 432); FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            var notice = new LegacyWarningPanel(true) { Dock = DockStyle.Top, Height = 172, Padding = new Padding(30, 24, 30, 16) };
            notice.SetLanguage(language);
            var content = new Panel { Dock = DockStyle.Top, Height = 176, Padding = new Padding(30, 18, 30, 8) };
            neverShow.Dock = DockStyle.Top; neverShow.Height = 34; neverShow.TabIndex = 0;
            neverShow.Text = neverShow.AccessibleName = Strings.Get("legacyWarningNeverShow", language);
            var hint = new Label { Dock = DockStyle.Top, Height = 54, Font = Theme.Font(9.5f), ForeColor = Theme.Muted,
                Text = Strings.Get("legacyWarningHint", language), UseMnemonic = false };
            error.Dock = DockStyle.Fill; error.ForeColor = Theme.Danger; error.Font = Theme.Font(9.5f);
            error.UseMnemonic = false; error.Visible = false;
            content.Controls.Add(error); content.Controls.Add(hint); content.Controls.Add(neverShow);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(30, 20, 30, 20), BackColor = Theme.Background };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            confirm.Look = ButtonLook.Primary; confirm.Dock = DockStyle.Right; confirm.Width = 260; confirm.TabIndex = 0;
            confirm.Text = confirm.AccessibleName = Strings.Get("legacyWarningConfirm", language);
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            body.Controls.Add(content); body.Controls.Add(notice);
            body.Layout += (s, e) => body.AutoScrollMinSize = new Size(0, notice.Height + content.Height);
            footer.Controls.Add(confirm); Controls.Add(body); Controls.Add(footer);
            AcceptButton = confirm;
            confirm.Click += async (s, e) => await AcknowledgeAsync();
            // Escape/Alt+F4 must not count as confirmation. System shutdown may still close the app.
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing && !acknowledged) e.Cancel = true; };
            Shown += (s, e) =>
            {
                var area = Screen.FromControl(this).WorkingArea;
                // Keep confirmation available on small screens / large DPI; the notice can scroll.
                Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                Left = Math.Max(area.Left + 12, Math.Min(Left, area.Right - Width - 12));
                Top = Math.Max(area.Top + 12, Math.Min(Top, area.Bottom - Height - 12));
                neverShow.Focus();
            };
        }
        private async Task AcknowledgeAsync()
        {
            if (busy || acknowledged) return;
            busy = true; error.Visible = false; confirm.Enabled = neverShow.Enabled = false;
            var succeeded = false;
            try { succeeded = ConfirmAsync == null || await ConfirmAsync(neverShow.Checked); }
            catch (Exception) { succeeded = false; }
            finally { busy = false; if (!IsDisposed) confirm.Enabled = neverShow.Enabled = true; }
            if (IsDisposed) return;
            if (succeeded) { acknowledged = true; DialogResult = DialogResult.OK; Close(); }
            else
            {
                error.Text = Strings.Get("legacyWarningSaveFailed", language); error.Visible = true;
                confirm.Focus();
            }
        }
    }
}
#endif
