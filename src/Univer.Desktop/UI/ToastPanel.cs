using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // A short-lived success notice that floats over the page's lower right corner instead of pushing the page down.
    // A short entrance stays inside the workspace; hovering or focusing it pauses the countdown.
    internal sealed class ToastPanel : Panel
    {
        private readonly Label title = new Label(), body = new Label();
        private readonly Timer countdown = new Timer { Interval = 50 };
        public ModernButton Primary { get; } = new ModernButton { Look = ButtonLook.Primary, Height = 34 };
        public ModernButton Secondary { get; } = new ModernButton { Look = ButtonLook.Outline, Height = 34 };
        public ModernButton Dismiss { get; } = new ModernButton { Icon = UiIcon.Close, Compact = true, Width = 30, Height = 30 };
        public event Action Expired;
        public string Key { get; private set; }
        public bool Shown { get; private set; }
        private readonly int lifetime;
        private int elapsed, offset;
        // Real time between ticks: a busy UI thread delays WinForms timer ticks, which must not lengthen the notice.
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private long lastTick;
        private Tween slide;
        private float progress;
        private UiMessage renderedMessage;
        private MessageTone tone;
        // Control.Visible reads false while the toast itself is hidden, so the layout keeps which actions it shows.
        private bool hasPrimary, hasSecondary;
        private readonly float scale;
        private int D(int value) { return (int)Math.Round(value * scale); }
        public ToastPanel(float scale, int lifetime = 6500)
        {
            this.scale = scale; this.lifetime = lifetime;
            DoubleBuffered = true; Visible = false; BackColor = Theme.Surface; AccessibleRole = AccessibleRole.Alert;
            title.Font = Theme.Strong(Theme.Emphasis); title.ForeColor = Theme.Ink; title.UseMnemonic = false; title.AutoEllipsis = true;
            body.Font = Theme.Font(Theme.Body); body.ForeColor = Theme.Muted; body.UseMnemonic = false; body.AutoEllipsis = true;
            title.BackColor = body.BackColor = Theme.Surface; Primary.BackColor = Secondary.BackColor = Dismiss.BackColor = Theme.Surface;
            Primary.Font = Secondary.Font = Theme.Font(Theme.Meta);
            Controls.Add(title); Controls.Add(body); Controls.Add(Primary); Controls.Add(Secondary); Controls.Add(Dismiss);
            countdown.Tick += (s, e) =>
            {
                var now = clock.ElapsedMilliseconds; var step = (int)Math.Min(int.MaxValue, now - lastTick); lastTick = now;
                // Reading or reaching for an action keeps the notice in place.
                if (ClientRectangle.Contains(PointToClient(Cursor.Position)) || ContainsFocus) return;
                elapsed = (int)Math.Min(int.MaxValue, (long)elapsed + step); progress = Math.Min(1, elapsed / (float)lifetime); Invalidate(new Rectangle(0, Height - D(4), Width, D(4)));
                if (elapsed >= lifetime) { countdown.Stop(); Expired?.Invoke(); }
            };
        }
        public void Render(UiMessage message, string heading, string text, string action, string secondary, string dismiss)
        {
            if (message == null) { Hide(); return; }
            var changed = Key != message.Key || !ReferenceEquals(renderedMessage, message) || title.Text != heading || (message.Tone != MessageTone.Working && body.Text != text);
            renderedMessage = message; tone = message.Tone;
            Key = message.Key; title.Text = heading; body.Text = text; AccessibleName = heading + ". " + text;
            hasPrimary = message.Action != MessageAction.None && action.Length > 0; hasSecondary = secondary.Length > 0;
            Primary.Text = Primary.AccessibleName = action; Primary.Visible = hasPrimary;
            Secondary.Text = Secondary.AccessibleName = secondary; Secondary.Visible = hasSecondary;
            Dismiss.AccessibleName = dismiss; Dismiss.Visible = message.CanDismiss;
            // New text or actions can change the height; a toast already on screen keeps its bottom edge inside the window.
            Arrange(); if (Shown) Place();
            if (!Shown) Show();
            else if (changed) { elapsed = 0; progress = 0; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1); }
        }
        public new void Show()
        {
            if (Parent == null) return;
            Shown = true; elapsed = 0; progress = 0; clock.Restart(); lastTick = 0; countdown.Start();
            exitVisual?.Dispose(); exitVisual = null;
            slide?.Cancel(); Enabled = true;
            var from = Motion.Enabled && !Motion.Reduced && !Motion.KeyboardInput ? (Visible ? offset : D(16)) : 0;
            offset = from;
            Visible = true; BringToFront(); Place(); Refresh();
            AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1);
            slide = Motion.Run(from != 0 ? Motion.Normal : 0, t => { offset = Motion.Lerp(from, 0, Motion.EaseOut(t)); Place(); });
        }
        public new void Hide()
        {
            if (!Shown) return;
            Shown = false; countdown.Stop(); Key = null;
            slide?.Cancel();
            if (!Motion.Enabled || Motion.KeyboardInput || ContainsFocus) { Visible = false; return; }
            Enabled = false;
            exitVisual?.Dispose();
            exitVisual = MotionPopup.Play(MotionPopup.Capture(this), RectangleToScreen(ClientRectangle), 260);
            Visible = false;
        }
        // Width follows the window, with every action immediately reachable.
        public void Place()
        {
            if (Parent == null || !Visible) return;
            var width = Math.Min(D(420), Parent.ClientSize.Width - D(32));
            if (Width != width) { Width = width; Arrange(); }
            Location = new Point(Parent.ClientSize.Width - Width - D(20), Parent.ClientSize.Height - Height - D(20) + offset);
        }
        private void Arrange()
        {
            var inner = Width - D(72) - D(40);
            title.SetBounds(D(64), D(16), inner, D(22));
            var lines = TextRenderer.MeasureText(body.Text, body.Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak).Height;
            var bodyHeight = Math.Min(lines + 2, D(44));
            body.SetBounds(D(64), D(40), inner, bodyHeight);
            var y = body.Bottom + D(12); var x = D(64);
            if (hasPrimary) { Primary.Width = TextRenderer.MeasureText(Primary.Text, Primary.Font).Width + D(32); Primary.SetBounds(x, y, Primary.Width, D(34)); x = Primary.Right + D(8); }
            if (hasSecondary) { Secondary.Width = TextRenderer.MeasureText(Secondary.Text, Secondary.Font).Width + D(32); Secondary.SetBounds(x, y, Secondary.Width, D(34)); }
            var hasActions = hasPrimary || hasSecondary;
            // Buttons that do not fit beside each other wrap below; the toast never clips its actions.
            if (hasSecondary && hasPrimary && Secondary.Right > Width - D(16)) Secondary.SetBounds(D(64), Primary.Bottom + D(8), Secondary.Width, D(34));
            Dismiss.SetBounds(Width - D(40), D(10), D(30), D(30));
            Height = (hasActions ? Math.Max(hasPrimary ? Primary.Bottom : 0, hasSecondary ? Secondary.Bottom : 0) : body.Bottom) + D(20);
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), D(12))) Region = new Region(path);
            Invalidate();
        }
        // Escape inside the toast closes it, like the close button.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && Dismiss.Visible) { Dismiss.PerformClick(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            // Progress and new notifications in the accent colours, a finished action in green.
            var accent = tone == MessageTone.Working || renderedMessage?.Action == MessageAction.Notices;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), D(12)))
            using (var pen = new Pen(accent ? Theme.Border : Theme.Current.SuccessLine)) g.DrawPath(pen, path);
            var badge = new RectangleF(D(18), D(16), D(34), D(34));
            using (var fill = new SolidBrush(accent ? Theme.Selected : Theme.Current.SuccessBadge)) g.FillEllipse(fill, badge);
            var icon = renderedMessage?.Action == MessageAction.Notices ? UiIcon.Bell : tone == MessageTone.Working ? UiIcon.Downloads : UiIcon.Check;
            Icons.Draw(g, icon, new RectangleF(badge.X + D(8), badge.Y + D(8), D(18), D(18)), accent ? Theme.AccentInk : Theme.Success);
            // The remaining time shrinks toward the left edge.
            var remaining = (Width - D(24)) * (1 - progress);
            using (var track = new SolidBrush(accent ? Theme.Border : Theme.Current.SuccessTrack)) g.FillRectangle(track, D(12), Height - D(4), Width - D(24), D(2));
            using (var bar = new SolidBrush(accent ? Theme.Accent : Theme.Success)) g.FillRectangle(bar, D(12), Height - D(4), remaining, D(2));
        }
        private MotionPopup exitVisual;
        protected override void Dispose(bool disposing) { if (disposing) { slide?.Cancel(); exitVisual?.Dispose(); countdown.Dispose(); } base.Dispose(disposing); }
    }
}
