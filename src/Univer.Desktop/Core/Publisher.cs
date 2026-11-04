using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Univer.Desktop.Core
{
    // Who signed a file. An unsigned app has only the checksum to go on; once it is signed, only the same publisher may replace it.
    internal static class Publisher
    {
        public static bool SameApp(string running, string update)
        {
            var expected = Subject(running);
            return expected == null || (expected == Subject(update) && Trusted(update));
        }
        internal static string Subject(string path)
        {
            try { return X509Certificate.CreateFromSignedFile(path).Subject; }
            catch (CryptographicException) { return null; }
        }
        // Revocation is not checked: the file was just matched to its release, and a check would need the network again.
        internal static bool Trusted(string path)
        {
            var file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = path };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            try
            {
                Marshal.StructureToPtr(file, pointer, false);
                var data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), UiChoice = 2, UnionChoice = 1, File = pointer };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                Marshal.DestroyStructure(pointer, typeof(TrustFile));
                return result == 0;
            }
            finally { Marshal.FreeHGlobal(pointer); }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct TrustFile
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr Handle, Subject;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            public uint Size;
            public IntPtr Policy, SipData;
            public uint UiChoice, Revocation, UnionChoice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr State, Reference;
            public uint ProviderFlags, UiContext;
        }
        [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    }
}
