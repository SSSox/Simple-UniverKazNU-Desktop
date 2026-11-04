using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed class FeedbackPanel : Panel
    {
        private readonly Label title = new Label(), body = new Label();
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        public ModernButton Primary { get; } = new ModernButton { Look = ButtonLook.Primary, Width = 148, Height = 36 };
        public ModernButton Secondary { get; } = new ModernButton { Width = 130, Height = 36 };
        public ModernButton Dismiss { get; } = new ModernButton { Icon = UiIcon.Close, Compact = true, Width = 32, Height = 32 };
        private MessageTone tone;
        private string renderedKey;
        private MotionPopup exitVisual;
        public FeedbackPanel()
        {
            DoubleBuffered = true; Dock = DockStyle.Top; Padding = new Padding(16, 12, 16, 12); AccessibleRole = AccessibleRole.Alert;
            title.Font = Theme.Strong(11); title.ForeColor = Theme.Ink; body.Font = Theme.Font(10); body.ForeColor = Theme.Ink;
            title.AutoSize = false; body.AutoSize = false; actions.WrapContents = false; actions.BackColor = Color.Transparent;
            actions.Controls.Add(Primary); actions.Controls.Add(Secondary); Controls.Add(title); Controls.Add(body); Controls.Add(actions); Controls.Add(Dismiss);
            Resize += (s, e) => Arrange();
        }
        public void Render(UiMessage message, string heading, string text, string action, string secondary, string dismiss)
        {
            var changed = message != null && (!Visible || renderedKey != message.Key);
            if (changed) exitVisual?.Dispose();
            if (message == null)
            {
                MotionSurface.Stop(this);
                if (Visible && Motion.Enabled && !Motion.KeyboardInput)
                { exitVisual?.Dispose(); exitVisual = MotionPopup.Play(MotionPopup.Capture(this), RectangleToScreen(ClientRectangle), 260); }
            }
            Visible = message != null;
            renderedKey = message?.Key;
            if (message == null) return;
            tone = message.Tone; title.Text = heading; body.Text = text;
            BackColor = tone == MessageTone.Error ? Theme.Current.ErrorFill : tone == MessageTone.Warning ? Theme.Current.WarningFill : tone == MessageTone.Success ? Theme.Current.SuccessFill : Theme.Current.InfoFill;
            title.BackColor = body.BackColor = actions.BackColor = BackColor; Primary.Text = action; Primary.AccessibleName = action; Primary.Visible = message.Action != MessageAction.None;
            Primary.Width = Math.Max(148, TextRenderer.MeasureText(action, Primary.Font).Width + 28);
            Secondary.Width = Math.Max(130, TextRenderer.MeasureText(secondary, Secondary.Font).Width + 28);
            Secondary.Text = secondary; Secondary.AccessibleName = secondary; Secondary.Visible = secondary.Length > 0;
            Dismiss.Visible = message.CanDismiss; Dismiss.AccessibleName = dismiss; Dismiss.BackColor = BackColor; Arrange(); Invalidate();
            if (changed) MotionSurface.Reveal(this);
            // The banner does not take focus, so a new problem or result is announced to screen readers as an alert.
            if (changed) { AccessibleName = heading + ". " + text; AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1); }
        }
        private void Arrange()
        {
            if (!Visible || Width < 100) return;
            float scale; using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96f;
            Func<int, int> px = value => (int)Math.Round(value * scale);
            var available = Math.Max(px(100), Width - px(90));
            title.Bounds = new Rectangle(px(48), px(12), available, px(24));
            var bodyHeight = TextRenderer.MeasureText(body.Text, body.Font, new Size(available, int.MaxValue), TextFormatFlags.WordBreak).Height + 4;
            body.Bounds = new Rectangle(px(48), px(38), available, bodyHeight);
            var hasActions = Primary.Visible || Secondary.Visible;
            Primary.Height = Secondary.Height = px(36);
            actions.Bounds = new Rectangle(px(48), px(43) + bodyHeight, available, hasActions ? px(40) : 0);
            Dismiss.Bounds = new Rectangle(Width - px(40), px(8), px(32), px(32));
            Height = px(52) + bodyHeight + (hasActions ? px(40) : 0);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = e.Graphics.DpiX / 96f;
            var accent = tone == MessageTone.Error ? Theme.Danger : tone == MessageTone.Warning ? Theme.Current.Warning : tone == MessageTone.Success ? Theme.Success : Theme.AccentInk;
            using (var bar = new SolidBrush(accent)) e.Graphics.FillRectangle(bar, 0, 0, 3 * scale, Height);
            using (var line = new Pen(Theme.Border)) e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
            Icons.Draw(e.Graphics, tone == MessageTone.Error || tone == MessageTone.Warning ? UiIcon.Warning : tone == MessageTone.Success ? UiIcon.Check : UiIcon.Info,
                new RectangleF(16 * scale, 13 * scale, 22 * scale, 22 * scale), accent);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) exitVisual?.Dispose(); base.Dispose(disposing); }
    }
}
