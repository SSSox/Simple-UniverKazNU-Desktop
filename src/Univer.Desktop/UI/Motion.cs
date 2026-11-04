using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // The app preference enables motion; Windows reduces travel, rather than removing all feedback.
    internal static class Motion
    {
        private static bool requested = true;
        public static bool Enabled
        {
            get { return requested; }
            set { requested = value; }
        }
        public static bool Reduced
        {
            get { bool animate; return SystemParametersInfo(0x1042, 0, out animate, 0) && !animate; }
        }
        public static bool KeyboardInput { get; set; }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint parameter,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] out bool value, uint flags);
        private static readonly List<Tween> running = new List<Tween>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static Timer timer;
        public const int Fast = 140, Normal = 220;
        public static Tween Run(int duration, Action<float> step, Action done = null)
        {
            var tween = new Tween(step, done, duration, clock.ElapsedMilliseconds);
            if (!Enabled || duration <= 0) { tween.Finish(); return tween; }
            running.Add(tween); step(0);
            if (timer == null) { timer = new Timer { Interval = 15 }; timer.Tick += (s, e) => Tick(); }
            timer.Start(); return tween;
        }
        private static void Tick()
        {
            var now = clock.ElapsedMilliseconds;
            foreach (var tween in running.ToArray())
            {
                if (tween.Stopped) { running.Remove(tween); continue; }
                var t = Enabled ? Math.Min(1f, (now - tween.Started) / (float)tween.Duration) : 1f;
                if (t >= 1) { running.Remove(tween); tween.Finish(); }
                else tween.Step(t);
            }
            if (running.Count == 0) timer.Stop();
        }
        public static void FadeIn(ToolStripDropDown menu)
        {
            Tween fade = null;
            MotionPopup visual = null; Bitmap pending = null; Rectangle closingBounds = Rectangle.Empty;
            Region originalClip = null;
            var hasOriginalClip = false;
            Action restoreClip = () => { if (hasOriginalClip && !menu.IsDisposed) { var clip = menu.Region; menu.Region = originalClip; originalClip = null; hasOriginalClip = false; clip?.Dispose(); } };
            menu.Opening += (s, e) => { fade?.Cancel(); restoreClip(); visual?.Dispose(); pending?.Dispose(); pending = null; originalClip = menu.Region?.Clone(); hasOriginalClip = true; menu.Opacity = Enabled && !KeyboardInput ? .25 : 1; };
            menu.Opened += (s, e) =>
            {
                var from = (float)menu.Opacity;
                fade = Run(from < 1 ? 180 : 0, t =>
                {
                    if (menu.IsDisposed) return;
                    menu.Opacity = Lerp(from, 1, EaseOut(t));
                    if (from < 1 && !Reduced)
                    {
                        var scale = .97f + .03f * EaseOut(t); var clip = menu.Region;
                        menu.Region = new Region(new RectangleF(0, 0, menu.Width * scale, menu.Height * scale)); clip?.Dispose();
                    }
                }, restoreClip);
            };
            menu.KeyDown += (s, e) => { KeyboardInput = true; fade?.Cancel(); visual?.Dispose(); restoreClip(); menu.Opacity = 1; };
            menu.MouseDown += (s, e) => { KeyboardInput = false; visual?.Dispose(); };
            menu.Closing += (s, e) =>
            {
                pending?.Dispose(); pending = null;
                if (Enabled && !KeyboardInput && e.CloseReason != ToolStripDropDownCloseReason.Keyboard && e.CloseReason != ToolStripDropDownCloseReason.CloseCalled)
                { closingBounds = menu.Bounds; pending = MotionPopup.Capture(menu); }
            };
            menu.Closed += (s, e) =>
            {
                fade?.Cancel(); visual?.Dispose(); restoreClip(); if (!menu.IsDisposed) menu.Opacity = 1;
                visual = MotionPopup.Play(pending, closingBounds); pending = null;
            };
            menu.Disposed += (s, e) => { fade?.Cancel(); visual?.Dispose(); pending?.Dispose(); pending = null; originalClip?.Dispose(); originalClip = null; };
        }
        // Same cubic-bezier(.23,1,.32,1) as the page's motion token; solve x before evaluating y.
        public static float EaseOut(float t)
        { return Curve(t, .23f, 1, .32f, 1); }
        public static float EaseInOut(float t)
        { return Curve(t, .77f, 0, .175f, 1); }
        private static float Curve(float t, float x1, float y1, float x2, float y2)
        {
            if (t <= 0 || t >= 1) return Math.Max(0, Math.Min(1, t));
            float low = 0, high = 1, u = t;
            for (var i = 0; i < 14; i++)
            {
                var v = 1 - u;
                var x = 3 * v * v * u * x1 + 3 * v * u * u * x2 + u * u * u;
                if (x < t) low = u; else high = u;
                u = (low + high) / 2;
            }
            var rest = 1 - u;
            return 3 * rest * rest * u * y1 + 3 * rest * u * u * y2 + u * u * u;
        }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static int Lerp(int a, int b, float t) { return (int)Math.Round(a + (b - a) * t); }
        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(Lerp(a.A, b.A, t), Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
        }
    }
    internal sealed class Tween
    {
        private readonly Action<float> step;
        private readonly Action done;
        public int Duration { get; private set; }
        public long Started { get; private set; }
        public bool Stopped { get; private set; }
        public Tween(Action<float> step, Action done, int duration, long started) { this.step = step; this.done = done; Duration = duration; Started = started; }
        public void Step(float t) { if (!Stopped) step(t); }
        public void Finish() { if (Stopped) return; Stopped = true; step(1); done?.Invoke(); }
        // Cancelling leaves the value where it is; the caller starts the next tween from there.
        public void Cancel() { Stopped = true; }
    }
    // The app's zoom also enlarges its own pages (sidebar, Downloads): every font is scaled from the size it was
    // created with, so repeated zoom steps never drift. Embedded browsers keep their own zoom.
    internal static class UiZoom
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> original = new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();
        public static void ApplyFonts(Control root, float factor) { ApplyFonts(root, factor, false); }
        private static void ApplyFonts(Control root, float factor, bool rootScaled)
        {
            foreach (Control child in root.Controls)
            {
                var type = child.GetType().Name;
                if (type == "ChromiumWebBrowser" || type == "PreviewHost") continue;
                // A font inherited from a parent scaled in this pass already follows it; scaling it again would compound the zoom.
                object recorded;
                if (rootScaled && !original.TryGetValue(child, out recorded) && ReferenceEquals(child.Font, root.Font)) { ApplyFonts(child, factor, true); continue; }
                var size = (float)original.GetValue(child, c => (object)c.Font.Size);
                var wanted = (float)Math.Round(size * factor * 4) / 4f;
                if (Math.Abs(child.Font.Size - wanted) > 0.01f) child.Font = Theme.Font(wanted, child.Font);
                ApplyFonts(child, factor, true);
            }
        }
    }
    // Painted chrome redraws off-screen, so resizing the window does not flicker through half-drawn panels.
    internal sealed class SmoothPanel : Panel
    {
        public SmoothPanel() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); }
    }
    // A drop-down list drawn by the app, closed and open: Windows draws the closed box light (light blue with focus)
    // even in dark mode. Arrows keep the standard selection; Alt+Down and F4 open the app's list.
    internal sealed class FadeComboBox : ComboBox
    {
        private readonly ContextMenuStrip list = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Renderer = new ToolStripProfessionalRenderer(new MenuColors()) };
        private bool hovered;
        public FadeComboBox()
        {
            Motion.FadeIn(list);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void WndProc(ref Message m)
        {
            const int down = 0x0201, doubleClick = 0x0203;
            if ((m.Msg == down || m.Msg == doubleClick) && Enabled && Items.Count > 0) { Motion.KeyboardInput = false; Focus(); ShowList(); return; }
            base.WndProc(ref m);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData == (Keys.Alt | Keys.Down) || keyData == (Keys.Alt | Keys.Up) || keyData == Keys.F4) && Enabled && Items.Count > 0) { Motion.KeyboardInput = true; ShowList(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Behind(this)); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var scale = g.DpiX / 96f; var ink = Enabled ? Theme.Ink : Theme.Current.Disabled;
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 7))
            {
                using (var fill = new SolidBrush(hovered && Enabled ? Theme.Hover : Theme.Surface)) g.FillPath(fill, path);
                using (var pen = new Pen(Focused ? Theme.AccentInk : Theme.Border, Focused ? 1.8f : 1)) g.DrawPath(pen, path);
            }
            var arrow = 16 * scale; var inset = (int)(12 * scale);
            Icons.Draw(g, UiIcon.Down, new RectangleF(Width - inset - arrow, (Height - arrow) / 2, arrow, arrow), Enabled ? Theme.Muted : ink);
            var text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : Text;
            TextRenderer.DrawText(g, text, Font, new Rectangle(inset, 0, Math.Max(1, Width - inset * 2 - (int)arrow - inset / 2), Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        private void ShowList()
        {
            list.Items.Clear(); list.Font = Font; Theme.Style(list);
            for (var i = 0; i < Items.Count; i++)
            {
                var index = i;
                var item = new ToolStripMenuItem(GetItemText(Items[i])) { Checked = i == SelectedIndex, AutoSize = false, Width = Width, Height = Math.Max(32, Height - 4) };
                item.Click += (s, e) => { SelectedIndex = index; Focus(); };
                list.Items.Add(item);
            }
            list.MinimumSize = new Size(Width, 0);
            list.Show(this, new Point(0, Height));
            // Opened from the keyboard, the arrows start at the current choice.
            if (Motion.KeyboardInput && SelectedIndex >= 0 && SelectedIndex < list.Items.Count) list.Items[SelectedIndex].Select();
        }
        protected override void Dispose(bool disposing) { if (disposing) list.Dispose(); base.Dispose(disposing); }
    }
}
