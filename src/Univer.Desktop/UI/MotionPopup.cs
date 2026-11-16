using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // A non-activating layered HWND avoids adding a Form to the native menu's focus stack.
    // Its menu pixels never own commands, input or a persisted bitmap.
    internal sealed class MotionPopup : NativeWindow, IDisposable
    {
        private Bitmap picture, canvas;
        private Tween tween;
        private float progress;
        private readonly bool reduced;
        private readonly Rectangle bounds;
        private bool disposed;
        // A flight: the picture travels along a soft arc to a point and fades as it lands.
        private readonly bool flight;
        private Point start, finish;
        private float travelled;
        // An arrival: the picture fades in where the real control will stand, and the control takes over under it.
        private readonly bool arriving;
        private Action ready;
        internal static int ActiveCount { get; private set; }
        private MotionPopup(Bitmap picture, Rectangle bounds, int duration, Point? landing = null, Action ready = null)
        {
            this.picture = picture; this.bounds = bounds; reduced = Motion.Reduced; arriving = ready != null; this.ready = ready; progress = arriving ? 0 : 1;
            flight = landing.HasValue; start = bounds.Location; finish = landing ?? bounds.Location;
            CreateHandle(new CreateParams { Caption = "", X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                Style = unchecked((int)0x80000000) | 0x08000000, ExStyle = 0x00080000 | 0x08000000 | 0x20 | 0x80 | 0x8 });
            ActiveCount++; Draw();
            if (disposed) return;
            Native.ShowWindow(Handle, 4);
            if (arriving) tween = Motion.Run(duration, t => { if (!disposed) { progress = Motion.EaseOut(t); Draw(); } }, Handover);
            else if (flight) tween = Motion.Run(duration, t => { if (!disposed) { travelled = Motion.Travel(t); Draw(); } }, Dispose);
            else tween = Motion.Run(duration, t => { if (!disposed) { progress = 1 - (duration > 200 ? Motion.EaseInOut(t) : Motion.EaseOut(t)); Draw(); } }, Dispose);
        }
        internal static Bitmap Capture(Control menu)
        {
            if (menu.Width < 1 || menu.Height < 1) return null;
            var bitmap = new Bitmap(menu.Width, menu.Height);
            bitmap.SetResolution(96 * Theme.DpiScale, 96 * Theme.DpiScale);
            try { menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size)); return bitmap; }
            catch (ArgumentException) { bitmap.Dispose(); return null; }
        }
        // The picture keeps a Region's rounded corners, which a capture does not have.
        internal static Bitmap Round(Bitmap flat, int radius)
        {
            if (flat == null || radius <= 0) return flat;
            var shaped = new Bitmap(flat.Width, flat.Height, PixelFormat.Format32bppPArgb);
            shaped.SetResolution(flat.HorizontalResolution, flat.VerticalResolution);
            using (flat) using (var graphics = Graphics.FromImage(shaped)) using (var brush = new TextureBrush(flat)) using (var path = Theme.Rounded(new RectangleF(0, 0, flat.Width, flat.Height), radius))
            { graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.FillPath(brush, path); }
            return shaped;
        }
        internal static MotionPopup Arrive(Bitmap bitmap, Rectangle bounds, int duration, Action ready)
        {
            try { return new MotionPopup(bitmap, bounds, duration, null, ready); }
            catch (System.ComponentModel.Win32Exception) { bitmap.Dispose(); ready(); return null; }
        }
        // The finished picture stays a moment over the real control, so the two read as one.
        private void Handover()
        {
            var take = ready; ready = null;
            if (disposed || take == null) return;
            take();
            tween = Motion.Run(34, t => { }, Dispose);
        }
        // Leaving before the arrival ended: the control is released at once and the picture goes.
        internal void Drop() { var take = ready; ready = null; take?.Invoke(); Dispose(); }
        internal static MotionPopup Play(Bitmap bitmap, Rectangle bounds, int duration = 140)
        {
            if (bitmap == null) return null;
            try { return new MotionPopup(bitmap, bounds, duration); }
            catch (System.ComponentModel.Win32Exception) { bitmap.Dispose(); return null; }
        }
        // One trip for a picture of something: from where it started to the place it now lives.
        internal static MotionPopup Fly(Bitmap bitmap, Point from, Point to, int duration = 700)
        {
            if (bitmap == null) return null;
            if (!Motion.Enabled || Motion.Reduced) { bitmap.Dispose(); return null; }
            try { return new MotionPopup(bitmap, new Rectangle(from, bitmap.Size), duration, to); }
            catch (System.ComponentModel.Win32Exception) { bitmap.Dispose(); return null; }
        }
        private void Draw()
        {
            if (Handle == IntPtr.Zero || picture == null) return;
            // One frame buffer serves the whole animation instead of a new full-size bitmap per frame.
            if (canvas == null) canvas = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Transparent); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var scale = flight ? 1 - .45f * travelled : reduced ? 1 : .97f + .03f * progress;
                if (flight) graphics.DrawImage(picture, new RectangleF(bounds.Width * (1 - scale) / 2, bounds.Height * (1 - scale) / 2, bounds.Width * scale, bounds.Height * scale));
                else graphics.DrawImage(picture, new RectangleF(arriving ? bounds.Width * (1 - scale) / 2 : 0, reduced ? 0 : -6 * (1 - progress), bounds.Width * scale, bounds.Height * scale));
            }
            var at = bounds.Location;
            if (flight)
            {
                var lift = Math.Min(90f, Math.Abs(finish.X - start.X) * .18f) * (float)Math.Sin(Math.PI * travelled);
                at = new Point(Motion.Lerp(start.X, finish.X - bounds.Width / 2, travelled), (int)Math.Round(Motion.Lerp((float)start.Y, finish.Y - bounds.Height / 2f, travelled) - lift));
            }
            IntPtr memory = Native.CreateCompatibleDC(IntPtr.Zero), bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            var updated = false;
            try
            {
                bitmap = canvas.GetHbitmap(Color.FromArgb(0));
                if (memory != IntPtr.Zero && bitmap != IntPtr.Zero)
                { previous = Native.SelectObject(memory, bitmap); updated = Native.Present(Handle, memory, at, bounds.Size, flight ? Math.Min(1f, (1 - travelled) / .2f) : progress, true); }
            }
            catch (ExternalException) { updated = false; }
            finally
            {
                if (previous != IntPtr.Zero) Native.SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                if (memory != IntPtr.Zero) Native.DeleteDC(memory);
            }
            if (!updated) Dispose();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; tween?.Cancel(); picture?.Dispose(); picture = null; canvas?.Dispose(); canvas = null;
            if (Handle != IntPtr.Zero) DestroyHandle(); ActiveCount--;
        }
    }
}
