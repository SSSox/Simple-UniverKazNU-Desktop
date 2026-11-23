using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // Quiet is the kit's ghost button, Outline its secondary, Tertiary a filled quiet one; OnToast sits on a toast's surface.
    internal enum ButtonLook { Quiet, Primary, Navigation, Danger, Outline, Tertiary, OnToast, Chip, Card }
    internal sealed class ModernButton : Button
    {
        private bool hovered, pressed, selected, open, busy;
        // Pointer feedback retargets from its current value. Keyboard navigation stays immediate.
        private float hoverLevel, selectLevel;
        private Tween fade, selection;
        private Tween compression;
        private float pressLevel;
        private UiIcon icon, oldIcon;
        private float iconLevel = 1;
        private Tween iconChange;
        // A label that changes crossfades on the same button, and the button eases to its new width.
        private string oldText = "";
        private float textLevel = 1;
        private Tween textChange, widthChange;
        // Busy opens a slot for the spinner beside the label, which moves over with it.
        private float busyLevel, turn;
        private Tween busyChange;
        private Timer spinner;
        public UiIcon Icon
        {
            get { return icon; }
            set
            {
                if (icon == value) return;
                var from = oldIcon == value && iconLevel < 1 ? 1 - iconLevel : 0;
                iconChange?.Cancel(); oldIcon = icon; icon = value;
                iconChange = Motion.Run(Kit.Fading(this) ? Motion.Fast : 0, t => { iconLevel = Motion.Lerp(from, 1, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
            }
        }
        public ButtonLook Look { get; set; }
        // A Card shows this picture above its label: the area to fill and the drawing scale.
        public System.Action<Graphics, RectangleF, float> Picture { get; set; }
        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value; selection?.Cancel();
                if (plate != null) { if (value) plate.MoveTo(this); else plate.Release(this); }
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                var from = selectLevel; var to = value ? 1f : 0f;
                selection = Motion.Run(Kit.Fading(this) ? Motion.Fast : 0,
                    t => { selectLevel = Motion.Lerp(from, to, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
            }
        }
        public bool Compact { get; set; }
        // Opens a list: a chevron at the far end, turned while the list is open.
        public bool Chevron { get; set; }
        // A quieter count after the label of a filter chip.
        private string note = "";
        public string Note { get { return note; } set { value = value ?? ""; if (note == value) return; note = value; Invalidate(); } }
        // The chrome's zoom: icons, padding and radius grow with the button, not only its label.
        private float zoom = 1;
        public float Zoom { get { return zoom; } set { if (System.Math.Abs(zoom - value) < .001f) return; zoom = value; Invalidate(); } }
        // Width follows the label, so neighbours move with a label that changes.
        public bool Hug { get; set; }
        // Where a hugging button is heading, for a layout that places things around it.
        public int HugWidth { get { return WantedWidth(); } }
        // The menu or panel this button opened is showing: it keeps its pressed look until that closes.
        public bool Open { get { return open; } set { if (open == value) return; open = value; Animate(); } }
        public bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value) return;
                busy = value; busyChange?.Cancel(); var from = busyLevel; var to = value ? 1f : 0f;
                if (value)
                {
                    if (spinner == null) { spinner = new Timer { Interval = 33 }; spinner.Tick += (s, e) => { turn = (turn + .03f) % 1; Invalidate(); }; }
                    spinner.Start();
                }
                busyChange = Motion.Run(Kit.Moving(this) ? Motion.Normal : 0, t => { busyLevel = Motion.Lerp(from, to, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); }, () => { if (!busy) spinner?.Stop(); });
                if (Hug) FitWidth();
            }
        }
        // Shared by a group whose selection travels between its buttons; each button repaints its part of the plate.
        private SelectionPlate plate;
        public SelectionPlate Plate { get { return plate; } set { plate = value; if (plate != null && selected) plate.MoveTo(this); } }
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
            float hoverFrom = hoverLevel, hoverTo = (hovered || open) && Enabled ? 1 : 0;
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
            Cursor = Cursors.Hand; Font = Theme.Font(Theme.Body); Size = new Size(160, 32); AccessibleRole = AccessibleRole.PushButton;
        }
        public override string Text
        {
            get { return base.Text; }
            set
            {
                var old = base.Text; base.Text = value;
                if (old == (value ?? "")) return;
                textChange?.Cancel();
                if (old.Length > 0 && Kit.Moving(this))
                {
                    oldText = old;
                    textChange = Motion.Run(Motion.Normal, t => { textLevel = t; if (!IsDisposed) Invalidate(); }, () => { textLevel = 1; oldText = ""; });
                }
                else { textLevel = 1; oldText = ""; }
                if (Hug) FitWidth();
            }
        }
        private int WantedWidth()
        {
            var scale = Theme.DpiScale * zoom;
            var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            // A chevron button draws its label with DrawText's own padding and keeps the arrow's place on the right (OnPaint's trail).
            var chevron = Chevron ? TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width - text + (int)(22 * scale) : 0;
            return text + (int)(28 * scale) + (Icon != UiIcon.None || busy ? (int)(22 * scale) : 0) + chevron;
        }
        private void FitWidth()
        {
            widthChange?.Cancel(); var from = Width; var to = WantedWidth();
            if (from == to) return;
            widthChange = Motion.Run(Kit.Moving(this) ? Motion.Normal : 0, t => { if (!IsDisposed) Width = Motion.Lerp(from, to, Motion.EaseOut(t)); });
        }
        // Settles a hugging button on its label at once, once the window's scale is final.
        public void SnapWidth() { if (!Hug) return; widthChange?.Cancel(); Width = WantedWidth(); }
        protected override void OnHandleCreated(System.EventArgs e) { base.OnHandleCreated(e); SnapWidth(); }
        protected override void OnMouseEnter(System.EventArgs e) { hovered = true; Animate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(System.EventArgs e) { hovered = pressed = false; AnimatePress(false); Animate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Motion.KeyboardInput = false; var form = FindForm(); if (form != null) MotionSurface.StopTree(form); if (e.Button == MouseButtons.Left) { pressed = true; AnimatePress(true); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnKeyDown(KeyEventArgs e) { Motion.KeyboardInput = true; AnimatePress(false); base.OnKeyDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; AnimatePress(false); Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(System.EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(System.EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(System.EventArgs e) { if (!Enabled) hovered = pressed = false; Animate(); base.OnEnabledChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) { fade?.Cancel(); selection?.Cancel(); compression?.Cancel(); iconChange?.Cancel(); textChange?.Cancel(); widthChange?.Cancel(); busyChange?.Cancel(); spinner?.Dispose(); } base.Dispose(disposing); }
        protected override void OnPaint(PaintEventArgs e)
        {
            PaintButton(e.Graphics);
        }
        private void Colors(float depression, out Color bg, out Color fg)
        {
            var palette = Theme.Current; var level = System.Math.Max(hoverLevel, depression);
            switch (Look)
            {
                case ButtonLook.Primary:
                    bg = !Enabled ? palette.Control : Motion.Mix(palette.Accent, palette.AccentHover, level); fg = Enabled ? palette.OnAccent : palette.Muted; return;
                case ButtonLook.Danger:
                    bg = Motion.Mix(palette.ErrorFill, palette.DangerSoft, level); fg = Enabled ? palette.Danger : palette.Disabled; return;
                case ButtonLook.Outline:
                    bg = Motion.Mix(BackColor, palette.Hover, level); fg = Enabled ? palette.Ink : palette.Disabled; return;
                case ButtonLook.Tertiary:
                    bg = Motion.Mix(palette.Track, palette.Control, level); fg = Enabled ? palette.Ink : palette.Disabled; return;
                case ButtonLook.OnToast:
                    bg = Motion.Mix(palette.Raised, palette.Hover, level); fg = palette.AccentInk; return;
                case ButtonLook.Chip:
                    bg = Motion.Mix(Motion.Mix(palette.Surface, palette.Hover, level), palette.AccentSoft, selectLevel); fg = Enabled ? Motion.Mix(palette.Ink, palette.AccentInk, selectLevel) : palette.Disabled; return;
            }
            // A section is a button at rest too: a faint fill sets it apart from the captions without competing with the
            // chosen section, and hover lights it.
            if (Look == ButtonLook.Navigation && Enabled)
            {
                fg = Motion.Mix(palette.Ink, palette.AccentInk, System.Math.Max(selectLevel, level));
                bg = Color.FromArgb((int)(255 * (1 - selectLevel)), Motion.Mix(Motion.Mix(palette.Surface, palette.Background, .5f), palette.AccentSoft, level));
                return;
            }
            var ranked = Look == ButtonLook.Navigation || plate != null;
            fg = !Enabled ? palette.Disabled : ranked ? Motion.Mix(Look == ButtonLook.Navigation ? palette.Ink : palette.Muted, palette.AccentInk, selectLevel) : palette.Ink;
            bg = Color.FromArgb((int)(255 * level * (1 - (plate != null ? selectLevel : 0))), palette.Hover);
            if (plate == null) bg = Motion.Mix(bg, palette.Selected, selectLevel);
            // Tabs have no hover surface: their label and the bar under them carry the state.
            else if (plate.Style == PlateStyle.Underline) bg = Color.FromArgb(0, bg);
        }
        // A choice shown by its picture. The outline and a tick mark the chosen one, so colour is not the only sign.
        private void PaintCard(Graphics g, float scale)
        {
            var palette = Theme.Current; var label = 26 * scale; var gap = 3 * scale; var press = System.Math.Min(Width, Height) * .015f * pressLevel;
            var box = new RectangleF(gap + press, gap + press, Width - 2 * gap - 2 * press, Height - label - 2 * gap - 2 * press);
            Picture?.Invoke(g, box, scale);
            var edge = Motion.Mix(Motion.Mix(palette.Control, palette.Muted, hoverLevel), palette.AccentInk, selectLevel);
            using (var path = Theme.Rounded(box, Kit.RadiusMedium * scale)) using (var pen = new Pen(edge, (1 + selectLevel) * scale)) g.DrawPath(pen, path);
            if (selectLevel > .01f)
            {
                var size = 18 * scale; var disc = new RectangleF(box.Right - size - 6 * scale, box.Bottom - size - 6 * scale, size, size); var alpha = (int)(255 * selectLevel);
                using (var fill = new SolidBrush(Color.FromArgb(alpha, palette.Accent))) g.FillEllipse(fill, disc);
                Icons.Draw(g, UiIcon.Check, RectangleF.Inflate(disc, -4 * scale, -4 * scale), Color.FromArgb(alpha, palette.OnAccent));
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, (int)(Height - label), Width, (int)label), Motion.Mix(palette.Ink, palette.AccentInk, selectLevel), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(RectangleF.Inflate(box, 2 * scale, 2 * scale), (Kit.RadiusMedium + 2) * scale)) using (var pen = new Pen(Theme.AccentInk, 1.5f)) g.DrawPath(pen, path);
        }
        private void PaintButton(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; var ground = plate?.Ground?.Invoke() ?? (Look == ButtonLook.OnToast ? Theme.Current.Raised : Theme.Behind(this)); g.Clear(ground);
            var scale = g.DpiX / 96f * zoom;
            if (Look == ButtonLook.Card) { PaintCard(g, scale); return; }
            // A tab stands on the row's line, which runs on under it; the chosen tab's bar lies over that line.
            if (plate?.Style == PlateStyle.Underline) using (var line = new SolidBrush(Theme.Current.Line)) { g.SmoothingMode = SmoothingMode.None; g.FillRectangle(line, 0, Height - 1, Width, 1); g.SmoothingMode = SmoothingMode.AntiAlias; }
            plate?.Draw(g, -Left, -Top);
            var depression = Motion.Enabled && !Motion.Reduced ? pressLevel : pressed ? 1f : 0f;
            Color bg, fg; Colors(depression, out bg, out fg);
            // A press scales the face to .97, as the kit's button does.
            var inset = System.Math.Min(Width, Height) * .015f * pressLevel;
            var radius = (Height >= 32 * scale ? Kit.RadiusMedium : Kit.RadiusSmall) * scale;
            using (var path = Theme.Rounded(new RectangleF(.5f + inset, .5f + inset, Width - 1.5f - 2 * inset, Height - 1.5f - 2 * inset), radius))
            {
                using (var fill = new SolidBrush(bg)) g.FillPath(fill, path);
                if (Look == ButtonLook.Outline) using (var pen = new Pen(Theme.Current.Control)) g.DrawPath(pen, path);
                else if (Look == ButtonLook.Chip) using (var pen = new Pen(Motion.Mix(Theme.Current.Control, Theme.AccentInk, selectLevel))) g.DrawPath(pen, path);
                else if (Look == ButtonLook.Navigation && Enabled && hoverLevel > .01f && selectLevel < .99f) using (var pen = new Pen(Color.FromArgb((int)(255 * hoverLevel * (1 - selectLevel)), Theme.Current.Control))) g.DrawPath(pen, path);
            }
            var iconSize = Height <= 26 * scale ? 14f * scale : 16f * scale;
            var hasText = !Compact && !string.IsNullOrEmpty(Text);
            var centered = Look != ButtonLook.Navigation;
            var noteWidth = hasText && note.Length > 0 ? TextRenderer.MeasureText(g, note, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + (int)(6 * scale) : 0;
            var trail = Chevron ? (int)(22 * scale) : 0;
            var textWidth = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + noteWidth : 0;
            var gap = (Look == ButtonLook.Navigation ? 10 : 6) * scale;
            var lead = Icon != UiIcon.None ? iconSize + gap : (iconSize + gap) * busyLevel;
            float iconX;
            if (!hasText) iconX = (Width - iconSize) / 2;
            else if (Chevron) iconX = 10 * scale;
            else if (centered) iconX = System.Math.Max(8 * scale, (Width - textWidth - lead) / 2);
            else iconX = 10 * scale;
            var iconColor = fg;
            var slot = new RectangleF(iconX, (Height - iconSize) / 2, iconSize, iconSize);
            if (busyLevel > .01f) Kit.Spinner(g, RectangleF.Inflate(slot, -1 * scale, -1 * scale), turn, Motion.Mix(ground, fg, busyLevel), 1.8f * scale);
            else if (Icon != UiIcon.None)
            {
                if (oldIcon != UiIcon.None && iconLevel < .999f) Icons.Draw(g, oldIcon, slot, Color.FromArgb((int)(255 * (1 - iconLevel)), iconColor));
                Icons.Draw(g, Icon, slot, Color.FromArgb((int)(255 * iconLevel), iconColor));
            }
            if (hasText)
            {
                var tab = plate?.Style == PlateStyle.Underline;
                var left = tab ? 0 : lead < 1 ? (int)(centered ? 8 * scale : 12 * scale) : (int)(iconX + lead);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                    (centered && lead < 1 && noteWidth == 0 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
                if (noteWidth > 0) left = System.Math.Max(left, (Width - textWidth) / 2);
                var right = (tab ? Width : lead < 1 || !centered ? Width - (int)(8 * scale) : Width - (int)(6 * scale)) - trail - noteWidth;
                var area = new Rectangle(left, 0, System.Math.Max(1, right - left), Height);
                if (textLevel < 1 && oldText.Length > 0) TextRenderer.DrawText(g, oldText, Font, area, Motion.Mix(fg, bg.A == 255 ? bg : ground, textLevel), flags);
                TextRenderer.DrawText(g, Text, Font, area, textLevel < 1 ? Motion.Mix(bg.A == 255 ? bg : ground, fg, textLevel) : fg, flags);
                if (noteWidth > 0) TextRenderer.DrawText(g, note, Font, new Rectangle(right, 0, noteWidth + (int)(4 * scale), Height), Motion.Mix(Theme.Muted, fg, selectLevel), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (Chevron) { var size = 14 * scale; Icons.Draw(g, open ? UiIcon.Up : UiIcon.Down, new RectangleF(Width - size - 10 * scale, (Height - size) / 2, size, size), Theme.Muted); }
            if (badge.Length > 0)
            {
                if (badgeFont == null) badgeFont = Theme.Strong(7.5f);
                var size = TextRenderer.MeasureText(g, badge, badgeFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                var height = 14 * scale; var width = System.Math.Max(height, size.Width + 7 * scale);
                var pill = new RectangleF(System.Math.Min(Width - width - 1, iconX + iconSize - width / 2), System.Math.Max(1, (Height - iconSize) / 2 - height / 2), width, height);
                using (var path = Theme.Rounded(pill, height / 2)) using (var fill = new SolidBrush(Theme.Danger)) using (var ring = new Pen(Theme.Behind(this), 1.5f * scale))
                { g.FillPath(fill, path); g.DrawPath(ring, path); }
                TextRenderer.DrawText(g, badge, badgeFont, Rectangle.Round(pill), Theme.Dark ? Theme.Background : Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(new RectangleF(1.5f, 1.5f, Width - 4, Height - 4), radius - 1))
                using (var pen = new Pen(Look == ButtonLook.Primary ? Theme.OnAccent : Theme.AccentInk, 1.5f)) g.DrawPath(pen, path);
        }
    }
}
