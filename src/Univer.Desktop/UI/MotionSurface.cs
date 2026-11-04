using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // Move the real native panel. Scaling a bitmap changes ClearType text and replays stale layouts.
    internal sealed class MotionSurface : Control
    {
        private Control target;
        private Tween tween;
        private float progress;
        private int direction;
        private bool reduced;
        private System.Drawing.Point origin;
        internal float Progress { get { return progress; } }
        private MotionSurface() { TabStop = false; AccessibleRole = AccessibleRole.None; Size = System.Drawing.Size.Empty; }
        internal static void Stop(Control target)
        {
            foreach (var surface in target.Controls.OfType<MotionSurface>().ToArray()) surface.Dispose();
        }
        internal static void StopTree(Control target)
        {
            if (target.IsDisposed) return;
            Stop(target);
            foreach (Control child in target.Controls.Cast<Control>().ToArray()) StopTree(child);
        }
        internal static void Reveal(Control target, int direction = 1)
        {
            Stop(target);
            if (!Motion.Enabled || Motion.KeyboardInput || Motion.Reduced || target is Form || target.IsDisposed || !target.Visible || !target.IsHandleCreated || HasNativeViewer(target)) return;
            var surface = new MotionSurface { target = target, direction = direction, origin = target.Location };
            target.Controls.Add(surface); target.LocationChanged += surface.TargetMoved; surface.MoveFrame();
            surface.tween = Motion.Run(240, t => { if (!surface.IsDisposed) { surface.progress = Motion.EaseInOut(t); surface.MoveFrame(); } }, surface.Dispose);
        }
        internal void SampleForQa(float value, bool reducedMotion)
        { tween?.Cancel(); progress = value; reduced = reducedMotion; MoveFrame(); }
        private bool moving;
        private void MoveFrame()
        {
            if (target == null || target.IsDisposed || !target.IsHandleCreated) return;
            var offset = reduced ? 0 : (int)Math.Round(8 * (1 - progress));
            moving = true;
            try { SetWindowPos(target.Handle, IntPtr.Zero, origin.X + direction * offset, origin.Y + (direction == 0 ? offset : 0), 0, 0, 0x0015); }
            finally { moving = false; }
        }
        // Layout moved the panel during the animation (a resize re-wrapped the chips): that place is where it now rests.
        private void TargetMoved(object sender, EventArgs e)
        {
            if (moving || target == null) return;
            origin = target.Location; Dispose();
        }
        private static bool HasNativeViewer(Control target)
        { return target is CefSharp.WinForms.ChromiumWebBrowser || target is PreviewHost || target.Controls.Cast<Control>().Any(HasNativeViewer); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tween?.Cancel(); progress = 1; if (target != null) target.LocationChanged -= TargetMoved; MoveFrame(); target = null; }
            base.Dispose(disposing);
        }
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    }
}
