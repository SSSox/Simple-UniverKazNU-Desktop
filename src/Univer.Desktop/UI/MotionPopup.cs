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
        internal static int ActiveCount { get; private set; }
        private MotionPopup(Bitmap picture, Rectangle bounds, int duration)
        {
            this.picture = picture; this.bounds = bounds; reduced = Motion.Reduced; progress = 1;
            CreateHandle(new CreateParams { Caption = "", X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
                Style = unchecked((int)0x80000000) | 0x08000000, ExStyle = 0x00080000 | 0x08000000 | 0x20 | 0x80 | 0x8 });
            ActiveCount++; Draw();
            if (disposed) return;
            ShowWindow(Handle, 4);
            tween = Motion.Run(duration, t => { if (!disposed) { progress = 1 - (duration > 200 ? Motion.EaseInOut(t) : Motion.EaseOut(t)); Draw(); } }, Dispose);
        }
        internal static Bitmap Capture(Control menu)
        {
            if (menu.Width < 1 || menu.Height < 1) return null;
            var bitmap = new Bitmap(menu.Width, menu.Height);
            using (var graphics = menu.CreateGraphics()) bitmap.SetResolution(graphics.DpiX, graphics.DpiY);
            try { menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size)); return bitmap; }
            catch (ArgumentException) { bitmap.Dispose(); return null; }
        }
        internal static MotionPopup Play(Bitmap bitmap, Rectangle bounds, int duration = 140)
        {
            if (bitmap == null) return null;
            try { return new MotionPopup(bitmap, bounds, duration); }
            catch (System.ComponentModel.Win32Exception) { bitmap.Dispose(); return null; }
        }
        private void Draw()
        {
            if (Handle == IntPtr.Zero || picture == null) return;
            // One frame buffer serves the whole animation instead of a new full-size bitmap per frame.
            if (canvas == null) canvas = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            {
                using (var graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.Transparent); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var scale = reduced ? 1 : .97f + .03f * progress;
                    graphics.DrawImage(picture, new RectangleF(0, reduced ? 0 : -6 * (1 - progress), bounds.Width * scale, bounds.Height * scale));
                }
                var screen = GetDC(IntPtr.Zero); IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
                var point = new NativePoint { X = bounds.X, Y = bounds.Y }; var origin = new NativePoint();
                var size = new NativeSize { Width = bounds.Width, Height = bounds.Height };
                var blend = new Blend { Alpha = (byte)Math.Round(255 * progress), Format = 1 };
                var updated = false;
                try
                {
                    memory = CreateCompatibleDC(screen); bitmap = canvas.GetHbitmap(Color.FromArgb(0));
                    if (screen != IntPtr.Zero && memory != IntPtr.Zero && bitmap != IntPtr.Zero)
                    { previous = SelectObject(memory, bitmap); updated = UpdateLayeredWindow(Handle, screen, ref point, ref size, memory, ref origin, 0, ref blend, 2); }
                }
                catch (ExternalException) { updated = false; }
                finally
                {
                    if (previous != IntPtr.Zero) SelectObject(memory, previous);
                    if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                    if (memory != IntPtr.Zero) DeleteDC(memory);
                    if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
                }
                if (!updated) Dispose();
            }
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
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref NativePoint destination, ref NativeSize size, IntPtr memory, ref NativePoint source, uint key, ref Blend blend, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
