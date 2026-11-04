using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Univer.Desktop.UI
{
    // Hosts a registered Windows preview handler (the Explorer preview pane mechanism) for one local file.
    // Handlers run out of process in prevhost.exe; the file is opened read-only.
    internal sealed class PreviewHost : Control
    {
        private const string HandlerKey = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
        private object handler;
        private IStream stream;
        public PreviewHost() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }
        // Keyboard focus inside the preview belongs to the out-of-process handler. It offers these app shortcuts back through
        // IPreviewHandlerFrame, so Escape, Alt+Left, Ctrl+P and the rest still work after a click into the document.
        public event Action<Keys> Shortcut;
        private static readonly Keys[] AppKeys = { Keys.Escape, Keys.Alt | Keys.Left, Keys.F5, Keys.Control | Keys.P, Keys.Control | Keys.J, Keys.Control | Keys.Oemcomma,
            Keys.Control | Keys.D0, Keys.Control | Keys.NumPad0, Keys.Control | Keys.Oemplus, Keys.Control | Keys.Add, Keys.Control | Keys.OemMinus, Keys.Control | Keys.Subtract };
        private IntPtr accelerators;
        private Frame frame;
        private IntPtr Accelerators()
        {
            if (accelerators != IntPtr.Zero) return accelerators;
            var table = new Accel[AppKeys.Length];
            for (var i = 0; i < AppKeys.Length; i++)
            {
                var key = AppKeys[i]; byte virt = 1; // FVIRTKEY
                if ((key & Keys.Control) != 0) virt |= 8; if ((key & Keys.Alt) != 0) virt |= 0x10; if ((key & Keys.Shift) != 0) virt |= 4;
                table[i] = new Accel { virt = virt, key = (ushort)(key & Keys.KeyCode), command = (ushort)(i + 1) };
            }
            accelerators = CreateAcceleratorTable(table, table.Length); return accelerators;
        }
        private bool Offer(int message, IntPtr keyCode)
        {
            if (message != 0x0100 && message != 0x0104) return false; // WM_KEYDOWN, WM_SYSKEYDOWN
            // The keystroke went to the handler's process, so the modifiers are read from the keyboard itself.
            var key = (Keys)(int)keyCode & Keys.KeyCode;
            if (message == 0x0104 || GetAsyncKeyState(0x12) < 0) key |= Keys.Alt;
            if (GetAsyncKeyState(0x11) < 0) key |= Keys.Control;
            if (GetAsyncKeyState(0x10) < 0) key |= Keys.Shift;
            if (key == (Keys.Control | Keys.Shift | Keys.Oemplus)) key = Keys.Control | Keys.Oemplus;
            if (Array.IndexOf(AppKeys, key) < 0 || IsDisposed || !IsHandleCreated) return false;
            BeginInvoke(new Action(() => { if (!IsDisposed) Shortcut?.Invoke(key); }));
            return true;
        }
        public static Guid? HandlerFor(string path)
        {
            var extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            if (extension.Length < 2) return null;
            try
            {
                foreach (var key in new[] { extension + @"\ShellEx\" + HandlerKey, @"SystemFileAssociations\" + extension + @"\ShellEx\" + HandlerKey })
                {
                    var value = Read(key); if (value != null) return value;
                }
                using (var type = Registry.ClassesRoot.OpenSubKey(extension))
                {
                    var progId = type?.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(progId)) return Read(progId + @"\ShellEx\" + HandlerKey);
                }
            }
            catch (Exception) { }
            return null;
        }
        private static Guid? Read(string key)
        {
            using (var entry = Registry.ClassesRoot.OpenSubKey(key))
            {
                Guid value;
                return Guid.TryParse(entry?.GetValue(null) as string, out value) ? value : (Guid?)null;
            }
        }
        public bool Open(string path)
        {
            Unload();
            var clsid = HandlerFor(path);
            if (!clsid.HasValue || !File.Exists(path)) return false;
            try
            {
                var id = clsid.Value; var iid = typeof(IPreviewHandler).GUID;
                // CLSCTX_LOCAL_SERVER uses the handler's surrogate (prevhost.exe), independent of x86/x64.
                if (CoCreateInstance(ref id, IntPtr.Zero, 4, ref iid, out handler) != 0 || handler == null) { Unload(); return false; }
                var withFile = handler as IInitializeWithFile;
                var withStream = handler as IInitializeWithStream;
                if (withFile != null) withFile.Initialize(path, 0);
                else if (withStream != null)
                {
                    if (SHCreateStreamOnFileEx(path, 0x00000000 | 0x00000040, 0, false, IntPtr.Zero, out stream) != 0) { Unload(); return false; }
                    withStream.Initialize(stream, 0);
                }
                else { Unload(); return false; }
                var site = handler as IObjectWithSite;
                if (site != null) { if (frame == null) frame = new Frame(this); site.SetSite(frame); }
                // Word's handler keeps the vertical offset of its first layout unless the host resizes after DoPreview, hiding the top;
                // opening one pixel shorter and then filling the area makes it lay out from the first line.
                var dock = Dock; var size = Size;
                Dock = DockStyle.None; Size = new Size(size.Width, Math.Max(1, size.Height - 1));
                try
                {
                    var rect = Area(); var preview = (IPreviewHandler)handler;
                    preview.SetWindow(Handle, ref rect);
                    preview.DoPreview();
                }
                finally { Dock = dock; if (dock == DockStyle.None) Size = size; }
                return true;
            }
            catch (Exception) { Unload(); return false; }
        }
        private RECT Area() { return new RECT { left = 0, top = 0, right = Math.Max(1, ClientSize.Width), bottom = Math.Max(1, ClientSize.Height) }; }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            var preview = handler as IPreviewHandler;
            if (preview == null) return;
            try { var rect = Area(); preview.SetRect(ref rect); } catch (Exception) { }
        }
        public void Unload()
        {
            var preview = handler as IPreviewHandler;
            if (preview != null) { try { preview.Unload(); } catch (Exception) { } }
            var site = handler as IObjectWithSite;
            if (site != null) { try { site.SetSite(null); } catch (Exception) { } }
            if (handler != null) { try { Marshal.FinalReleaseComObject(handler); } catch (Exception) { } handler = null; }
            if (stream != null) { try { Marshal.FinalReleaseComObject(stream); } catch (Exception) { } stream = null; }
        }
        protected override void Dispose(bool disposing)
        {
            Unload();
            if (accelerators != IntPtr.Zero) { DestroyAcceleratorTable(accelerators); accelerators = IntPtr.Zero; }
            base.Dispose(disposing);
        }
        [StructLayout(LayoutKind.Sequential)] private struct Accel { public byte virt; public ushort key; public ushort command; }
        [StructLayout(LayoutKind.Sequential)] internal struct FrameInfo { public IntPtr accelerators; public uint entries; }
        [StructLayout(LayoutKind.Sequential)] internal struct NativeMessage { public IntPtr window; public int message; public IntPtr wParam, lParam; public int time, x, y; }
        [ComImport, Guid("fec87aaf-35f9-447a-adb7-20234491401a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPreviewHandlerFrame
        {
            void GetWindowContext(out FrameInfo info);
            [PreserveSig] int TranslateAccelerator(ref NativeMessage message);
        }
        [ComImport, Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectWithSite
        {
            void SetSite([MarshalAs(UnmanagedType.IUnknown)] object site);
            void GetSite(ref Guid iid, out IntPtr site);
        }
        // S_OK: the app took the keystroke; S_FALSE: the handler keeps it.
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        internal sealed class Frame : IPreviewHandlerFrame
        {
            private readonly PreviewHost host;
            public Frame(PreviewHost host) { this.host = host; }
            public void GetWindowContext(out FrameInfo info) { info = new FrameInfo { accelerators = host.Accelerators(), entries = (uint)AppKeys.Length }; }
            public int TranslateAccelerator(ref NativeMessage message) { return host.Offer(message.message, message.wParam) ? 0 : 1; }
        }
        [DllImport("user32.dll")] private static extern IntPtr CreateAcceleratorTable([In] Accel[] table, int count);
        [DllImport("user32.dll")] private static extern bool DestroyAcceleratorTable(IntPtr table);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPreviewHandler
        {
            void SetWindow(IntPtr hwnd, ref RECT rect);
            void SetRect(ref RECT rect);
            void DoPreview();
            void Unload();
            void SetFocus();
            void QueryFocus(out IntPtr phwnd);
            [PreserveSig] uint TranslateAccelerator(IntPtr message);
        }
        [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }
        [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IInitializeWithStream { void Initialize(IStream stream, uint mode); }
        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, bool create, IntPtr template, out IStream stream);
    }
}
