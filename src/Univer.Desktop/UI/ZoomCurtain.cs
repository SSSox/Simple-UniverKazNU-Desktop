using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // A zoom step relays out the whole window and Chromium repaints at its new size, which shows as jumps and a black
    // frame. So the step happens behind a picture of the window: the picture eases to the new scale, the real layout is
    // applied once the steps stop, and the picture fades into it. A click-through, non-activating layered window owned by
    // the form; it holds pixels only, never input. Plain GDI keeps a full-window frame cheap.
    internal sealed class ZoomCurtain : NativeWindow, IDisposable
    {
        private readonly int width, height, left, top;
        private IntPtr source, sourceBitmap, sourceOld, frame, frameBitmap, frameOld, ground;
        private float scale = 1, alpha = 1;
        private Tween move, fade;
        private bool disposed;
        public static int ActiveCount { get; private set; }
        public ZoomCurtain(Form owner, Color background)
        {
            var origin = owner.PointToScreen(Point.Empty); left = origin.X; top = origin.Y; width = owner.ClientSize.Width; height = owner.ClientSize.Height;
            var screen = Native.GetDC(IntPtr.Zero);
            try
            {
                source = Native.CreateCompatibleDC(screen); sourceBitmap = CreateCompatibleBitmap(screen, width, height); sourceOld = Native.SelectObject(source, sourceBitmap);
                frame = Native.CreateCompatibleDC(screen); frameBitmap = CreateCompatibleBitmap(screen, width, height); frameOld = Native.SelectObject(frame, frameBitmap);
                BitBlt(source, 0, 0, width, height, screen, left, top, 0x00CC0020 | 0x40000000);
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
            ground = CreateSolidBrush(ColorTranslator.ToWin32(background));
            SetStretchBltMode(frame, 4); SetBrushOrgEx(frame, 0, 0, IntPtr.Zero);
            CreateHandle(new CreateParams { Caption = "", X = left, Y = top, Width = width, Height = height, Parent = owner.Handle,
                Style = unchecked((int)0x80000000), ExStyle = 0x00080000 | 0x08000000 | 0x20 | 0x80 });
            ActiveCount++; Draw();
            if (!disposed) Native.ShowWindow(Handle, 4);
        }
        // Eases to this scale of the captured frame.
        public void Aim(float target)
        {
            if (disposed) return;
            fade?.Cancel(); alpha = 1; move?.Cancel(); var from = scale;
            move = Motion.Run(Motion.Enabled && !Motion.Reduced ? 140 : 0, t => { if (disposed) return; scale = Motion.Lerp(from, target, Motion.EaseOut(t)); Draw(); });
        }
        // The real window is ready underneath: the picture dissolves into it.
        public void Reveal()
        {
            if (disposed) return;
            fade?.Cancel();
            fade = Motion.Run(Motion.Enabled ? 130 : 0, t => { if (disposed) return; alpha = 1 - Motion.EaseOut(t); Draw(); }, Dispose);
        }
        private void Draw()
        {
            if (disposed || Handle == IntPtr.Zero) return;
            var w = (int)Math.Round(width * scale); var h = (int)Math.Round(height * scale);
            var edge = new Rect { Left = 0, Top = 0, Right = width, Bottom = height };
            if (w < width || h < height) FillRect(frame, ref edge, ground);
            StretchBlt(frame, 0, 0, w, h, source, 0, 0, width, height, 0x00CC0020);
            if (!Native.Present(Handle, frame, new Point(left, top), new Size(width, height), alpha, false)) Dispose();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; move?.Cancel(); fade?.Cancel();
            if (Handle != IntPtr.Zero) { DestroyHandle(); ActiveCount--; }
            if (source != IntPtr.Zero) { Native.SelectObject(source, sourceOld); Native.DeleteDC(source); }
            if (frame != IntPtr.Zero) { Native.SelectObject(frame, frameOld); Native.DeleteDC(frame); }
            foreach (var handle in new[] { sourceBitmap, frameBitmap, ground }) if (handle != IntPtr.Zero) Native.DeleteObject(handle);
            source = frame = sourceBitmap = frameBitmap = ground = IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect area, IntPtr brush);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, int operation);
        [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, int operation);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
    }
}
