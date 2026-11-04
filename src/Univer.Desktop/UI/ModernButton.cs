using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    internal enum ButtonLook { Quiet, Primary, Navigation, Danger, Outline }
    internal sealed class ModernButton : Button
    {
        private bool hovered, pressed, selected;
        // Pointer feedback retargets from its current value. Keyboard navigation stays immediate.
        private float hoverLevel, selectLevel;
        private Tween fade, selection;
        private Tween compression;
        private float pressLevel;
        private UiIcon icon, oldIcon;
        private float iconLevel = 1;
        private Tween iconChange;
        public UiIcon Icon
        {
            get { return icon; }
            set
            {
                if (icon == value) return;
                var from = oldIcon == value && iconLevel < 1 ? 1 - iconLevel : 0;
                iconChange?.Cancel(); oldIcon = icon; icon = value;
                var animate = IsHandleCreated && Visible && !Motion.KeyboardInput;
                iconChange = Motion.Run(animate ? Motion.Fast : 0, t => { iconLevel = Motion.Lerp(from, 1, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
            }
        }
        public ButtonLook Look { get; set; }
        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value; selection?.Cancel();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                var from = selectLevel; var to = value ? 1f : 0f;
                selection = Motion.Run(!IsHandleCreated || !Visible || Motion.KeyboardInput ? 0 : Motion.Normal,
                    t => { selectLevel = Motion.Lerp(from, to, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
            }
        }
        public bool Compact { get; set; }
        // A short count over the icon's upper right corner (unread notifications); empty draws nothing.
        private string badge = "";
        private static Font badgeFont;
        public string Badge { get { return badge; } set { value = value ?? ""; if (badge == value) return; badge = value; Invalidate(); } }
        // Sections, settings tabs and filter chips act as toggles: assistive technology hears which one is current.
        protected override AccessibleObject CreateAccessibilityInstance() { return new SelectableAccessibleObject(this); }
        private sealed class SelectableAccessibleObject : ControlAccessibleObject
        {
            private readonly ModernButton owner;
            public SelectableAccessibleObject(ModernButton owner) : base(owner) { this.owner = owner; }
            public override AccessibleStates State { get { return owner.Selected ? base.State | AccessibleStates.Checked : base.State; } }
            public override string DefaultAction { get { return "Press"; } }
            public override void DoDefaultAction() { if (owner.Enabled) owner.PerformClick(); }
        }
        private void Animate()
        {
            fade?.Cancel();
            float hoverFrom = hoverLevel, hoverTo = hovered && Enabled ? 1 : 0;
            if (!IsHandleCreated || !Visible) { hoverLevel = hoverTo; Invalidate(); return; }
            fade = Motion.Run(Motion.Fast, t =>
            {
                var e = Motion.EaseOut(t);
                hoverLevel = Motion.Lerp(hoverFrom, hoverTo, e);
                if (!IsDisposed) Invalidate();
            });
        }
        private void AnimatePress(bool down)
        {
            compression?.Cancel(); var from = pressLevel;
            var to = down && Motion.Enabled && !Motion.Reduced && !Motion.KeyboardInput ? 1f : 0f;
            if (System.Math.Abs(from - to) < .001f) { pressLevel = to; Invalidate(); return; }
            compression = Motion.Run(!Motion.Enabled || Motion.Reduced || Motion.KeyboardInput ? 0 : down ? 100 : 180,
                t => { pressLevel = Motion.Lerp(from, to, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
        }
        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Theme.Surface;
            Cursor = Cursors.Hand; Font = Theme.Font(10.5f); Size = new Size(160, 42); AccessibleRole = AccessibleRole.PushButton;
        }
        protected override void OnMouseEnter(System.EventArgs e) { hovered = true; Animate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(System.EventArgs e) { hovered = pressed = false; AnimatePress(false); Animate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Motion.KeyboardInput = false; var form = FindForm(); if (form != null) MotionSurface.StopTree(form); if (e.Button == MouseButtons.Left) { pressed = true; AnimatePress(true); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnKeyDown(KeyEventArgs e) { Motion.KeyboardInput = true; AnimatePress(false); base.OnKeyDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; AnimatePress(false); Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(System.EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(System.EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(System.EventArgs e) { if (!Enabled) hovered = pressed = false; Animate(); base.OnEnabledChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) { fade?.Cancel(); selection?.Cancel(); compression?.Cancel(); iconChange?.Cancel(); } base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            PaintButton(e.Graphics);
        }
        private void PaintButton(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Theme.Behind(this));
            var scale = g.DpiX / 96f;
            var primary = Look == ButtonLook.Primary;
            var depression = Motion.Enabled && !Motion.Reduced ? pressLevel : pressed ? 1f : 0f;
            Color bg, fg;
            if (primary) { bg = !Enabled ? Theme.Border : Motion.Mix(Motion.Mix(Theme.Accent, Theme.AccentHover, hoverLevel), Theme.AccentHover, depression); fg = Enabled ? Color.White : Theme.Muted; }
            else
            {
                bg = Motion.Mix(Motion.Mix(BackColor, Theme.Hover, hoverLevel), Theme.Selected, depression);
                bg = Motion.Mix(bg, Theme.Selected, selectLevel);
                fg = !Enabled ? Theme.Current.Disabled : Look == ButtonLook.Danger ? Theme.Danger : Motion.Mix(Theme.Ink, Theme.AccentInk, selectLevel);
            }
            var inset = 1.5f * scale * pressLevel;
            using (var path = Theme.Rounded(new RectangleF(.5f + inset, .5f + inset, Width - 1.5f - 2 * inset, Height - 1.5f - 2 * inset), 8 * scale))
            {
                using (var fill = new SolidBrush(bg)) g.FillPath(fill, path);
                if (Look == ButtonLook.Outline || (Look == ButtonLook.Danger && !Selected))
                    using (var pen = new Pen(Look == ButtonLook.Danger ? Theme.Current.DangerLine : Theme.Border)) g.DrawPath(pen, path);
            }
            var iconSize = System.Math.Min(20f * scale, Height - 14);
            var hasText = !Compact && !string.IsNullOrEmpty(Text);
            var centered = Look != ButtonLook.Navigation;
            var textWidth = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width : 0;
            var gap = 8 * scale;
            float iconX;
            if (!hasText) iconX = (Width - iconSize) / 2;
            else if (centered) iconX = System.Math.Max(10 * scale, (Width - textWidth - (Icon == UiIcon.None ? 0 : iconSize + gap)) / 2);
            else iconX = 12 * scale;
            var iconColor = Look == ButtonLook.Navigation && Enabled ? Motion.Mix(Theme.Muted, Theme.AccentInk, selectLevel) : fg;
            if (Icon != UiIcon.None)
            {
                var iconScale = Motion.Reduced ? 1 : .9f + .1f * iconLevel;
                var size = iconSize * iconScale;
                if (oldIcon != UiIcon.None && iconLevel < .999f)
                    Icons.Draw(g, oldIcon, new RectangleF(iconX, (Height - iconSize) / 2, iconSize, iconSize), Color.FromArgb((int)(255 * (1 - iconLevel)), iconColor));
                Icons.Draw(g, Icon, new RectangleF(iconX + (iconSize - size) / 2, (Height - size) / 2, size, size), Color.FromArgb((int)(255 * iconLevel), iconColor));
            }
            if (hasText)
            {
                var left = Icon == UiIcon.None ? (int)(centered ? 8 * scale : 12 * scale) : (int)(iconX + iconSize + gap);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                    (centered && Icon == UiIcon.None ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
                var right = Icon == UiIcon.None || !centered ? Width - (int)(8 * scale) : Width - (int)(6 * scale);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(left, 0, System.Math.Max(1, right - left), Height), fg, flags);
            }
            // A fixed marker identifies the active section without moving the browser layout.
            if (selectLevel > 0.01f && Look == ButtonLook.Navigation)
                using (var bar = new SolidBrush(Motion.Mix(Color.FromArgb(0, Theme.AccentInk), Theme.AccentInk, selectLevel)))
                    g.FillRectangle(bar, 0, Height * (0.5f - 0.25f * selectLevel), 3 * scale, Height * 0.5f * selectLevel);
            if (badge.Length > 0)
            {
                if (badgeFont == null) badgeFont = Theme.Strong(7.5f);
                var size = TextRenderer.MeasureText(g, badge, badgeFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                var height = 15 * scale; var width = System.Math.Max(height, size.Width + 8 * scale);
                var pill = new RectangleF(System.Math.Min(Width - width - 1, iconX + iconSize - width / 2), System.Math.Max(1, (Height - iconSize) / 2 - height / 3), width, height);
                using (var path = Theme.Rounded(pill, height / 2)) using (var fill = new SolidBrush(Theme.Danger)) using (var ring = new Pen(Theme.Behind(this), 1.5f * scale))
                { g.FillPath(fill, path); g.DrawPath(ring, path); }
                TextRenderer.DrawText(g, badge, badgeFont, Rectangle.Round(pill), Theme.Dark ? Theme.Background : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(new RectangleF(2, 2, Width - 5, Height - 5), 6 * scale))
                using (var pen = new Pen(primary ? Color.White : Theme.AccentInk, 2f)) g.DrawPath(pen, path);
        }
    }
}
