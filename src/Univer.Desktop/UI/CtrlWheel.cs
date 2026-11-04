using System;
using System.Runtime.InteropServices;

namespace Univer.Desktop.UI
{
    // Ctrl+wheel over a browser zooms the app like Ctrl+plus/minus (Chromium 152 would zoom only that page, 109 not at all).
    // All CEF windows share CEF's UI thread, so one message hook there takes the message before Chromium reads it.
    internal sealed class CtrlWheel : IDisposable
    {
        private const int GetMessageHook = 3, MouseWheel = 0x020A, ControlFlag = 0x0008, Remove = 1;
        private readonly HookProc filter;
        private readonly Action<int, int> wheel;
        private IntPtr hook;
        // wheel: the delta and the message time (GetTickCount milliseconds, as Environment.TickCount).
        public CtrlWheel(Action<int, int> wheel) { this.wheel = wheel; filter = Filter; }
        public bool Attached { get { return hook != IntPtr.Zero; } }
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
