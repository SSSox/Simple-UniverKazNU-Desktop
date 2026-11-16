using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Univer.Desktop.UI
{
    // The Windows calls shared by the layered pictures (MotionPopup, ZoomCurtain) and the windows moved by hand.
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
        // Shows a memory DC's pixels on a layered window. perPixel: the bitmap carries an alpha of its own.
        public static bool Present(IntPtr window, IntPtr memory, Point at, Size size, float alpha, bool perPixel)
        {
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return false;
            try
            {
                var point = new NativePoint { X = at.X, Y = at.Y }; var origin = new NativePoint(); var extent = new NativeSize { Width = size.Width, Height = size.Height };
                var blend = new Blend { Alpha = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, alpha))), Format = (byte)(perPixel ? 1 : 0) };
                return UpdateLayeredWindow(window, screen, ref point, ref extent, memory, ref origin, 0, ref blend, 2);
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref NativePoint destination, ref NativeSize size, IntPtr memory, ref NativePoint source, uint key, ref Blend blend, uint flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    }
}
