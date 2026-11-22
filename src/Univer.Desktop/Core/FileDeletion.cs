using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Univer.Desktop.Core
{
    // Whether a file's place keeps a Recycle Bin. Busy: the file could not be opened (another program holds it), so
    // nothing was decided about its drive.
    internal enum RecycleSupport { Yes, NoBin, Busy }
    // Where a deleted download goes. The app moves a file only through this, so self-test, the review and xUnit use the
    // in-memory copy and never touch the user's Downloads folder or Recycle Bin.
    internal interface IFileRecycler
    {
        // Yes when the file's real drive keeps a Recycle Bin, so the move can be undone.
        RecycleSupport Support(string path);
        // Moves the file to the Recycle Bin; owner is the window any shell question belongs to. Faults with IOException
        // when Windows could not (the user declined the shell's own warning, the bin cannot take it).
        Task RecycleAsync(string path, IntPtr owner);
    }
    internal sealed class MemoryRecycler : IFileRecycler
    {
        public readonly List<string> Calls = new List<string>();
        public readonly List<IntPtr> Owners = new List<IntPtr>();
        public bool Available = true, Busy;
        public RecycleSupport Support(string path) { return Busy ? RecycleSupport.Busy : Available ? RecycleSupport.Yes : RecycleSupport.NoBin; }
        public Task RecycleAsync(string path, IntPtr owner) { Calls.Add(path); Owners.Add(owner); return Task.FromResult(0); }
    }
    // Only a fixed local drive is trusted to have a Recycle Bin, and "fixed" is read from where the file really is (a
    // SUBST drive or a junction to a share resolves to its target). SHFileOperation with FOF_ALLOWUNDO deletes for good on
    // a removable or network drive (they have no bin), so those are refused before the call. The flags add
    // FOF_WANTNUKEWARNING: on a fixed drive whose bin is switched off or too small for the file the shell then asks before
    // destroying it, and a "No" aborts, so a quiet permanent deletion cannot happen. The call runs on its own STA thread
    // with the main window as owner, so that question opens in front of the app and the UI thread is never blocked.
    internal sealed class RecycleBinRecycler : IFileRecycler
    {
        private const int DriveFixed = 3;
        private const ushort Silent = 0x0004, NoConfirmation = 0x0010, AllowUndo = 0x0040, NoErrorUi = 0x0400, WantNukeWarning = 0x4000;
        private const uint Delete = 3;
        // The apartment the last call ran in (QA reads it).
        internal static ApartmentState LastApartment = ApartmentState.Unknown;
        public RecycleSupport Support(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || DownloadHistory.Remote(path) || !Path.IsPathRooted(path)) return RecycleSupport.NoBin;
                string resolved;
                using (var handle = CreateFileW(path, 0x80000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid)
                    {
                        var error = Marshal.GetLastWin32Error();
                        return error == 5 || error == 32 || error == 33 ? RecycleSupport.Busy : RecycleSupport.NoBin;
                    }
                    var text = new StringBuilder(520);
                    var length = GetFinalPathNameByHandleW(handle, text, (uint)text.Capacity, 0);
                    if (length >= text.Capacity) { text = new StringBuilder((int)length + 1); length = GetFinalPathNameByHandleW(handle, text, (uint)text.Capacity, 0); }
                    resolved = length == 0 || length >= text.Capacity ? null : text.ToString();
                }
                var root = FileDeletion.LocalDriveRoot(resolved);
                return root != null && GetDriveTypeW(root) == DriveFixed ? RecycleSupport.Yes : RecycleSupport.NoBin;
            }
            catch (Exception) { return RecycleSupport.NoBin; }
        }
        public Task RecycleAsync(string path, IntPtr owner)
        {
            var done = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try { LastApartment = Thread.CurrentThread.GetApartmentState(); Move(path, owner); done.SetResult(true); }
                catch (Exception ex) { done.SetException(ex); }
            }) { IsBackground = true, Name = "Univer.Recycle" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return done.Task;
        }
        private static void Move(string path, IntPtr owner)
        {
            var from = path + "\0\0";
            var flags = (ushort)(Silent | NoConfirmation | AllowUndo | NoErrorUi | WantNukeWarning);
            int code; bool aborted;
            if (IntPtr.Size == 8)
            {
                var op = new Op64 { hwnd = owner, wFunc = Delete, pFrom = from, fFlags = flags };
                code = SHFileOperation64(ref op); aborted = op.fAnyOperationsAborted != 0;
            }
            else
            {
                var op = new Op32 { hwnd = owner, wFunc = Delete, pFrom = from, fFlags = flags };
                code = SHFileOperation32(ref op); aborted = op.fAnyOperationsAborted != 0;
            }
            if (code != 0 || aborted || File.Exists(path)) throw new IOException("The Recycle Bin did not take the file (code " + code + ").");
        }
        // The structure of the running bitness as the marshaller lays it out (shellapi.h: 30 bytes packed on x86, 56 on
        // x64; fFlags and fAnyOperationsAborted at 16 and 18, or 32 and 36).
        internal static void Layout(out int size, out int flagsOffset, out int abortedOffset)
        {
            var type = IntPtr.Size == 8 ? typeof(Op64) : typeof(Op32);
            size = Marshal.SizeOf(type);
            flagsOffset = (int)Marshal.OffsetOf(type, "fFlags");
            abortedOffset = (int)Marshal.OffsetOf(type, "fAnyOperationsAborted");
        }
        // The shell's structure is packed on 32-bit Windows and aligned on 64-bit.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Op64
        {
            public IntPtr hwnd; public uint wFunc; [MarshalAs(UnmanagedType.LPWStr)] public string pFrom; [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags; public int fAnyOperationsAborted; public IntPtr hNameMappings; [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
        private struct Op32
        {
            public IntPtr hwnd; public uint wFunc; [MarshalAs(UnmanagedType.LPWStr)] public string pFrom; [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags; public int fAnyOperationsAborted; public IntPtr hNameMappings; [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }
        [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)] private static extern int SHFileOperation64(ref Op64 op);
        [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode)] private static extern int SHFileOperation32(ref Op32 op);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveTypeW(string root);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder text, uint size, uint flags);
    }
    internal enum DeleteVerdict { Ok, NotAFile, Partial, Missing, Directory, Link, Changed, NoBin, Busy, Failed }
    // Whether a downloaded file may be deleted, and the deletion itself. Only the record's own path is ever touched, and
    // only while it is still the file the app saved: a plain file of the same size.
    internal static class FileDeletion
    {
        // The record says a finished file; the disk is read here (one entry, no recursion).
        public static DeleteVerdict Check(DownloadRecord record)
        {
            if (record == null || record.State != DownloadState.Completed || string.IsNullOrWhiteSpace(record.Path)) return DeleteVerdict.NotAFile;
            var path = record.Path;
            if (!Path.IsPathRooted(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return DeleteVerdict.NotAFile;
            if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) return DeleteVerdict.Partial;
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return DeleteVerdict.Missing; }
            catch (DirectoryNotFoundException) { return DeleteVerdict.Missing; }
            catch (Exception) { return DeleteVerdict.Failed; }
            if ((attributes & FileAttributes.Directory) != 0) return DeleteVerdict.Directory;
            if ((attributes & FileAttributes.ReparsePoint) != 0) return DeleteVerdict.Link;
            long size;
            try { size = new FileInfo(path).Length; } catch (Exception) { return DeleteVerdict.Failed; }
            var expected = record.Total >= 0 ? record.Total : record.Received;
            return expected >= 0 && size == expected ? DeleteVerdict.Ok : DeleteVerdict.Changed;
        }
        // A GetFinalPathNameByHandle answer of the form \\?\X:\... is a path on local drive X; anything else (UNC, a volume
        // GUID, empty) is not a place the app can show to have a Recycle Bin. Returns the drive root ("X:\") or null.
        public static string LocalDriveRoot(string resolved)
        {
            if (resolved == null || !resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) return null;
            var rest = resolved.Substring(4);
            if (rest.Length < 3 || !((rest[0] >= 'A' && rest[0] <= 'Z') || (rest[0] >= 'a' && rest[0] <= 'z')) || rest[1] != ':' || rest[2] != '\\') return null;
            return rest.Substring(0, 3);
        }
        // After a preview let go: waits until the file opens for exclusive use, up to timeoutMs. False when it never did.
        public static async Task<bool> WaitReleasedAsync(string path, int timeoutMs = 2000, int stepMs = 100)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                try { using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return true; }
                catch (FileNotFoundException) { return true; }
                catch (DirectoryNotFoundException) { return true; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (DateTime.UtcNow >= until) return false;
                await Task.Delay(stepMs);
            }
        }
        // Check, then move to the Recycle Bin. Never a permanent deletion: no bin, no deletion. The shell is called once:
        // a "No" to its own question is final.
        public static async Task<DeleteVerdict> DeleteAsync(DownloadRecord record, IFileRecycler recycler, IntPtr owner)
        {
            var verdict = Check(record);
            if (verdict != DeleteVerdict.Ok) return verdict;
            var support = recycler.Support(record.Path);
            if (support == RecycleSupport.NoBin) return DeleteVerdict.NoBin;
            if (support == RecycleSupport.Busy) return DeleteVerdict.Busy;
            try { await recycler.RecycleAsync(record.Path, owner); }
            catch (Exception) { return DeleteVerdict.Failed; }
            return DeleteVerdict.Ok;
        }
        // The status line for a refusal; Ok is the success line.
        public static string StatusKey(DeleteVerdict verdict)
        {
            switch (verdict)
            {
                case DeleteVerdict.Ok: return "fileDeleted";
                case DeleteVerdict.Missing: return "downloadGone";
                case DeleteVerdict.Changed: return "deleteChanged";
                case DeleteVerdict.NoBin: return "deleteNoBin";
                case DeleteVerdict.Busy: return "deleteBusy";
                case DeleteVerdict.Partial: case DeleteVerdict.NotAFile: case DeleteVerdict.Directory: case DeleteVerdict.Link: return "deleteNotFile";
                default: return "deleteFailed";
            }
        }
    }
}
