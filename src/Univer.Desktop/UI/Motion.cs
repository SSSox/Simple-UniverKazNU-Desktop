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
        public const int Fast = 150, Normal = 220, Slide = 300;
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
        // The kit's curves, also the page's motion tokens. Settling feedback; solve x before evaluating y.
        public static float EaseOut(float t)
        { return Curve(t, .22f, 1, .36f, 1); }
        // Something going from one place to another: a departure, clear travel and a soft arrival.
        public static float Travel(float t)
        { return Curve(t, .45f, 0, .1f, 1); }
        // Something arriving from nowhere.
        public static float Arrive(float t)
        { return Curve(t, .25f, .8f, .25f, 1); }
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
                if (child is CefSharp.WinForms.ChromiumWebBrowser || child is PreviewHost || child is DownloadRow) continue;
                var button = child as ModernButton; if (button != null) button.Zoom = factor;
                var toggle = child as Toggle; if (toggle != null) toggle.Zoom = factor;
                var input = child as TextInput; if (input != null) input.Zoom = factor;
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
    // Chromium presents through the GPU, so the part of the window's own surface under the website is never drawn and stays
    // almost black. Anything that opens over the website shows that black until it paints. This panel does not clip its
    // children, so it fills the surface under Chromium with the page colour; drawn controls are left out of the fill.
    internal sealed class BackdropPanel : Panel
    {
        public BackdropPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.Style &= ~0x02000000; return value; } }
        // The stock background fill would run over every child, which then stays blank until something repaints it.
        protected override void OnPaintBackground(PaintEventArgs e) { }
        internal void PaintForQa(Graphics g, Rectangle area) { using (var e = new PaintEventArgs(g, area)) { OnPaintBackground(e); OnPaint(e); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            foreach (Control child in Controls) if (child.Visible && !(child is CefSharp.WinForms.ChromiumWebBrowser)) e.Graphics.ExcludeClip(child.Bounds);
            using (var fill = new SolidBrush(BackColor)) e.Graphics.FillRectangle(fill, ClientRectangle);
        }
    }
}
