using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal enum PinDialogMode { Create, Change, Disable }
    // Setting, changing or turning off the PIN from Settings. A new PIN is typed twice and easy ones are refused; the current
    // PIN is checked by the host and counts toward the same waits and tenth-guess end as the lock screen.
    internal sealed class PinDialog : Form
    {
        private readonly PinDialogMode mode;
        private readonly string language;
        private readonly Func<string, Task<PinResult>> verify;
        private readonly PinPad pad;
        private readonly ModernButton cancel = new ModernButton();
        private string first;
        private bool verified, checking;
        // The new PIN once typed twice (Create, Change); the host stores it.
        public string NewPin { get; private set; }
        // Ten wrong current PINs: the host ends the saved account.
        public bool Wiped { get; private set; }
        internal PinPad PadForQa { get { return pad; } }
        public PinDialog(PinDialogMode mode, string language, Func<string, Task<PinResult>> verify)
        {
            this.mode = mode; this.language = language; this.verify = verify; verified = mode == PinDialogMode.Create;
            Text = Strings.Get(mode == PinDialogMode.Disable ? "securityDisableTitle" : mode == PinDialogMode.Change ? "securityChange" : "offerPinSet", language);
            Font = Theme.Font(Theme.Body); ForeColor = Theme.Ink; BackColor = Theme.Surface; Theme.Window(this);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            pad = new PinPad(v => v) { Dock = DockStyle.Fill };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 8) }; body.Controls.Add(pad);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(24, 12, 24, 12), BackColor = Theme.Surface };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.Current.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            cancel.Look = ButtonLook.Outline; cancel.Hug = true; cancel.Height = 32; cancel.Dock = DockStyle.Right; cancel.BackColor = Theme.Surface;
            cancel.Text = cancel.AccessibleName = Strings.Get("lockCancel", language); cancel.Click += (s, e) => Close();
            footer.Controls.Add(cancel); Controls.Add(body); Controls.Add(footer);
            CancelButton = cancel;
            ClientSize = new Size(pad.WantedWidth + 48, pad.Wanted - 44 + 28 + 16 + 56);
            pad.Completed += async pin => await StepAsync(pin);
            FormClosing += (s, e) => { if (checking && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
            Shown += (s, e) => { cancel.SnapWidth(); pad.Focus(); };
            Render(null);
        }
        private void Render(string messageKey, bool error = true)
        {
            if (!verified) pad.SetTexts("lockVerifyTitle", mode == PinDialogMode.Disable ? "securityDisableMessage" : null, null, language);
            else if (first == null) pad.SetTexts(mode == PinDialogMode.Change ? "lockNewTitle" : "lockCreateTitle", "lockCreateHint", null, language);
            else pad.SetTexts("lockConfirmTitle", "lockCreateHint", null, language);
            if (messageKey != null) pad.ShowKey(messageKey, error); else pad.ShowMessage("", false);
            pad.Clear();
        }
        private async Task StepAsync(string pin)
        {
            if (checking) return;
            if (!verified)
            {
                checking = true; pad.SetBusy(true);
                PinResult result;
                try { result = await verify(pin); }
                catch (Exception) { result = new PinResult(PinOutcome.Failed); }
                checking = false; if (IsDisposed) return; pad.SetBusy(false);
                switch (result.Outcome)
                {
                    case PinOutcome.Success:
                        if (mode == PinDialogMode.Disable) { DialogResult = DialogResult.OK; return; }
                        verified = true; Render(null); return;
                    case PinOutcome.Wiped: Wiped = true; DialogResult = DialogResult.Abort; return;
                    case PinOutcome.Wait: Render(null); pad.Wait(result.WaitMs); return;
                    // The window is already open, so a record that cannot be read now ends nothing: the change waits.
                    case PinOutcome.Failed: case PinOutcome.Unavailable: Render("lockFailed"); return;
                    default:
                        Render(null);
                        pad.ShowMessage(result.AttemptsLeft == 1 ? Strings.Get("lockLastAttempt", language) : string.Format(Strings.Get("lockWrongPin", language), result.AttemptsLeft), true);
                        if (result.WaitMs > 0) pad.Wait(result.WaitMs);
                        return;
                }
            }
            if (first == null)
            {
                var weakness = PinPolicy.Weakness(pin);
                if (weakness != PinWeakness.None) { Render(weakness == PinWeakness.Repeated ? "lockWeakRepeated" : weakness == PinWeakness.Sequence ? "lockWeakSequence" : "lockWeakCommon"); return; }
                first = pin; Render(null); return;
            }
            if (pin != first) { first = null; Render("lockMismatch"); return; }
            NewPin = pin; first = null; DialogResult = DialogResult.OK;
        }
    }
    // A question with two answers in the app's own look: the offer to set a PIN, and the reset of a forgotten one.
    internal sealed class ChoiceForm : Form
    {
        private readonly ModernButton primary = new ModernButton(), secondary = new ModernButton();
        internal ModernButton PrimaryForQa { get { return primary; } }
        public ChoiceForm(string title, string message, string primaryText, string secondaryText, bool danger)
        {
            Text = title;
            Font = Theme.Font(Theme.Body); ForeColor = Theme.Ink; BackColor = Theme.Surface; Theme.Window(this);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            var heading = new Label { Dock = DockStyle.Top, Height = 32, Font = Theme.Display(12.75f), UseMnemonic = false, Text = title };
            var words = new Label { Dock = DockStyle.Fill, UseMnemonic = false, Text = message };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 8) };
            body.Controls.Add(words); body.Controls.Add(heading);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(24, 12, 24, 12), BackColor = Theme.Surface };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.Current.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            secondary.Look = ButtonLook.Outline; secondary.Hug = true; secondary.Height = 32; secondary.Dock = DockStyle.Right; secondary.BackColor = Theme.Surface;
            primary.Look = danger ? ButtonLook.Danger : ButtonLook.Primary; primary.Hug = true; primary.Height = 32; primary.Dock = DockStyle.Right;
            secondary.Text = secondary.AccessibleName = secondaryText; primary.Text = primary.AccessibleName = primaryText;
            footer.Controls.Add(secondary); footer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, TabStop = false }); footer.Controls.Add(primary);
            Controls.Add(body); Controls.Add(footer);
            primary.DialogResult = DialogResult.Yes; secondary.DialogResult = DialogResult.No; AcceptButton = danger ? null : primary; CancelButton = secondary;
            var lines = TextRenderer.MeasureText(message, Font, new Size(432, int.MaxValue), TextFormatFlags.WordBreak).Height;
            ClientSize = new Size(480, 20 + 32 + lines + 16 + 56);
            Shown += (s, e) => { primary.SnapWidth(); secondary.SnapWidth(); (danger ? secondary : primary).Focus(); };
        }
    }
}
