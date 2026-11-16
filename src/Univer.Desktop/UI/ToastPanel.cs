using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // The kit's toast: a short notice on the theme's own raised surface that rises in over the page's lower right corner
    // instead of pushing the page down. Hovering or focusing it pauses the countdown.
    internal sealed class ToastPanel : Panel
    {
        private readonly Label title = new Label(), body = new Label();
        private readonly Timer countdown = new Timer { Interval = 50 };
        public ModernButton Primary { get; } = new ModernButton { Look = ButtonLook.OnToast, Height = 28 };
        public ModernButton Secondary { get; } = new ModernButton { Look = ButtonLook.OnToast, Height = 28 };
        public ModernButton Dismiss { get; } = new ModernButton { Look = ButtonLook.OnToast, Icon = UiIcon.Close, Compact = true, Width = 24, Height = 24 };
        public event Action Expired;
        public string Key { get; private set; }
        public bool Shown { get; private set; }
        private readonly int lifetime;
        internal int Lifetime { get { return lifetime; } }
        private int elapsed;
        private readonly FloatingSurface floating = new FloatingSurface();
        // Real time between ticks: a busy UI thread delays WinForms timer ticks, which must not lengthen the notice.
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private long lastTick;
        private UiMessage renderedMessage;
        private MessageTone tone;
        private float turn;
        // Control.Visible reads false while the toast itself is hidden, so the layout keeps which actions it shows.
        private bool hasPrimary, hasSecondary;
        private readonly float baseScale;
        private float scale;
        private int D(int value) { return (int)Math.Round(value * scale); }
        public ToastPanel(float scale, int lifetime = 3500)
        {
            baseScale = this.scale = scale; this.lifetime = lifetime;
            DoubleBuffered = true; Visible = false; AccessibleRole = AccessibleRole.Alert;
            title.Font = Theme.Strong(Theme.Body); title.UseMnemonic = false; title.AutoEllipsis = true;
            body.Font = Theme.Font(Theme.Meta); body.UseMnemonic = false; body.AutoEllipsis = true;
            Primary.Font = Secondary.Font = Theme.Strong(Theme.Meta);
            Paint3();
            Controls.Add(title); Controls.Add(body); Controls.Add(Primary); Controls.Add(Secondary); Controls.Add(Dismiss);
            countdown.Tick += (s, e) =>
            {
                var now = clock.ElapsedMilliseconds; var step = (int)Math.Min(int.MaxValue, now - lastTick); lastTick = now;
                if (tone == MessageTone.Working) { turn = (turn + .05f) % 1; Invalidate(new Rectangle(D(10), D(10), D(20), D(20))); }
                // Reading or reaching for an action keeps the notice in place.
                if (ClientRectangle.Contains(PointToClient(Cursor.Position)) || ContainsFocus) return;
                elapsed = (int)Math.Min(int.MaxValue, (long)elapsed + step);
                if (elapsed >= lifetime) { countdown.Stop(); Expired?.Invoke(); }
            };
        }
        public void SetZoom(float zoom) { scale = baseScale * zoom; UiZoom.ApplyFonts(this, zoom); if (Shown) { Arrange(); Place(); } }
        // Its colours follow the theme, so they are read at each showing rather than kept as roles.
        private void Paint3()
        {
            var palette = Theme.Current;
            BackColor = title.BackColor = body.BackColor = Primary.BackColor = Secondary.BackColor = Dismiss.BackColor = palette.Raised;
            title.ForeColor = palette.Ink; body.ForeColor = palette.Muted;
        }
        public void Render(UiMessage message, string heading, string text, string action, string secondary, string dismiss)
        {
            if (message == null) { Hide(); return; }
            var changed = Key != message.Key || !ReferenceEquals(renderedMessage, message) || title.Text != heading || (message.Tone != MessageTone.Working && body.Text != text);
            renderedMessage = message; tone = message.Tone; Paint3();
            Key = message.Key; title.Text = heading; body.Text = text; AccessibleName = heading + ". " + text;
            hasPrimary = message.Action != MessageAction.None && action.Length > 0; hasSecondary = secondary.Length > 0;
            Primary.Text = Primary.AccessibleName = action; Primary.Visible = hasPrimary;
            Secondary.Text = Secondary.AccessibleName = secondary; Secondary.Visible = hasSecondary;
            Dismiss.AccessibleName = dismiss; Dismiss.Visible = message.CanDismiss;
            // New text or actions can change the height; a toast already on screen keeps its bottom edge inside the window.
            Arrange(); if (Shown) Place();
            if (!Shown) Show();
            else if (changed) { elapsed = 0; Invalidate(); AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1); }
        }
        public new void Show()
        {
            if (Parent == null) return;
            Shown = true; elapsed = 0; clock.Restart(); lastTick = 0; countdown.Start();
            exitVisual?.Dispose(); exitVisual = null;
            Enabled = true;
            if (Visible) { BringToFront(); Place(); Refresh(); }
            else { Place(true); floating.Show(this, D(Kit.RadiusLarge), Shape); }
            AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1);
        }
        public new void Hide()
        {
            if (!Shown) return;
            Shown = false; countdown.Stop(); Key = null;
            floating.Settle();
            if (!Motion.Enabled || Motion.KeyboardInput || ContainsFocus) { Visible = false; return; }
            Enabled = false;
            exitVisual?.Dispose();
            exitVisual = MotionPopup.Play(RoundedPicture(), RectangleToScreen(ClientRectangle), 160);
            Visible = false;
        }
        // The toast's Region cuts its corners on screen, but a capture is a plain rectangle: the fading copy gets the same shape.
        internal Bitmap RoundedPicture()
        {
            return MotionPopup.Round(MotionPopup.Capture(this), D(Kit.RadiusLarge));
        }
        // Width follows the window, with every action immediately reachable.
        public void Place(bool opening = false)
        {
            if (Parent == null || (!Visible && !opening)) return;
            var width = Math.Min(D(380), Parent.ClientSize.Width - D(32));
            if (Width != width) { Width = width; Arrange(); }
            Location = new Point(Parent.ClientSize.Width - Width - D(16), Parent.ClientSize.Height - Height - D(16));
        }
        private void Arrange()
        {
            var left = D(38); var inner = Width - left - D(12) - (Dismiss.Visible || renderedMessage?.CanDismiss == true ? D(26) : 0);
            title.SetBounds(left, D(11), inner, D(18));
            var lines = TextRenderer.MeasureText(body.Text, body.Font, new Size(inner, int.MaxValue), TextFormatFlags.WordBreak).Height;
            var bodyHeight = Math.Min(lines + 2, D(40));
            body.SetBounds(left, title.Bottom + D(2), inner, bodyHeight);
            // The first action's words line up with the text above them.
            var y = body.Bottom + D(6); var x = left - D(10);
            if (hasPrimary) { Primary.Width = TextRenderer.MeasureText(Primary.Text, Primary.Font).Width + D(20); Primary.SetBounds(x, y, Primary.Width, D(28)); x = Primary.Right + D(4); }
            if (hasSecondary) { Secondary.Width = TextRenderer.MeasureText(Secondary.Text, Secondary.Font).Width + D(20); Secondary.SetBounds(x, y, Secondary.Width, D(28)); }
            var hasActions = hasPrimary || hasSecondary;
            // Buttons that do not fit beside each other wrap below; the toast never clips its actions.
            if (hasSecondary && hasPrimary && Secondary.Right > Width - D(12)) Secondary.SetBounds(left - D(10), Primary.Bottom + D(2), Secondary.Width, D(28));
            Dismiss.SetBounds(Width - D(32), D(8), D(24), D(24));
            Height = (hasActions ? Math.Max(hasPrimary ? Primary.Bottom : 0, hasSecondary ? Secondary.Bottom : 0) + D(6) : body.Bottom + D(12));
            if (!floating.Arriving) Shape();
            Invalidate();
        }
        private void Shape() { using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), D(Kit.RadiusLarge))) Region = new Region(path); }
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
            using (var path = Theme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), D(Kit.RadiusLarge))) using (var pen = new Pen(Theme.Current.Control)) g.DrawPath(pen, path);
            // Status keeps its colour: a finished action green, the rest the accent.
            var accent = tone == MessageTone.Working || renderedMessage?.Action == MessageAction.Notices;
            var mark = accent ? Theme.AccentInk : Theme.Current.Success; var box = new RectangleF(D(12), D(12), D(16), D(16));
            if (tone == MessageTone.Working) Kit.Spinner(g, box, turn, mark, 1.8f * scale);
            else Icons.Draw(g, renderedMessage?.Action == MessageAction.Notices ? UiIcon.Bell : UiIcon.Check, box, mark);
        }
        private MotionPopup exitVisual;
        protected override void Dispose(bool disposing) { if (disposing) { floating.Dispose(); exitVisual?.Dispose(); countdown.Dispose(); } base.Dispose(disposing); }
    }
}
