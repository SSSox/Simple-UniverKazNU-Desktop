using System;
using System.Runtime.InteropServices;

namespace Univer.Desktop.UI
{
    // The mouse over a browser, before Chromium reads it. All CEF windows share CEF's UI thread, so one message hook there
    // takes the message first: Ctrl+wheel zooms the app like Ctrl+plus/minus (Chromium 152 would zoom only that page, 109
    // not at all), and the side buttons go Back and Forward (Chromium leaves them to the page, which does nothing with them).
    internal sealed class BrowserMouse : IDisposable
    {
        private const int GetMessageHook = 3, MouseWheel = 0x020A, ControlFlag = 0x0008, Remove = 1;
        private const int SideDown = 0x020B, SideUp = 0x020C, SideDouble = 0x020D, Forward = 2;
        private readonly HookProc filter;
        private readonly Action<int, int> wheel;
        private readonly Action<bool> side;
        private IntPtr hook;
        // wheel: the delta and the message time (GetTickCount milliseconds, as Environment.TickCount). side: true for
        // Forward (XBUTTON2), false for Back (XBUTTON1), once per click, on release.
        public BrowserMouse(Action<int, int> wheel, Action<bool> side) { this.wheel = wheel; this.side = side; filter = Filter; }
        // window: any browser window of CEF's UI thread; the first one installs the hook for all of them. If the hook
        // cannot be installed, Ctrl+wheel stays Chromium's own page zoom and the next browser window tries again.
        public void Attach(IntPtr window)
        {
            if (hook != IntPtr.Zero || window == IntPtr.Zero) return;
            uint process;
            var thread = GetWindowThreadProcessId(window, out process);
            if (thread == 0 || process != (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return;
            hook = SetWindowsHookEx(GetMessageHook, filter, IntPtr.Zero, thread);
        }
        private IntPtr Filter(int code, IntPtr wParam, IntPtr lParam)
        {
            // A message read without removing it (PM_NOREMOVE) is seen again when it is removed.
            if (code >= 0 && (long)wParam == Remove)
            {
                var message = (NativeMessage)Marshal.PtrToStructure(lParam, typeof(NativeMessage));
                if (message.message == MouseWheel && ((long)message.wParam & ControlFlag) != 0)
                {
                    var delta = (short)((long)message.wParam >> 16 & 0xFFFF);
                    // WM_NULL: Chromium receives nothing for this message.
                    message.message = 0; Marshal.StructureToPtr(message, lParam, false);
                    try { wheel(delta, message.time); } catch (Exception) { /* An exception must never leave CEF's thread through the hook. */ }
                }
                else if (message.message == SideDown || message.message == SideUp || message.message == SideDouble)
                {
                    // The whole click is taken (a quick second press arrives as a double click), and it acts once, on release.
                    var forward = ((long)message.wParam >> 16 & 0xFFFF) == Forward;
                    var up = message.message == SideUp;
                    message.message = 0; Marshal.StructureToPtr(message, lParam, false);
                    if (up) try { side(forward); } catch (Exception) { }
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }
        public void Dispose()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook); hook = IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeMessage { public IntPtr window; public int message; public IntPtr wParam, lParam; public int time, x, y; }
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, HookProc procedure, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    }
}
