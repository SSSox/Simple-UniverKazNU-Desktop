using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Univer.Desktop.Core
{
    // Where a deleted download goes. The app moves a file only through this, so self-test, the review and xUnit use the
    // in-memory copy and never touch the user's Downloads folder or Recycle Bin.
    internal interface IFileRecycler
    {
        // True when the file's drive keeps a Recycle Bin, so the move can be undone.
        bool CanRecycle(string path);
        // Moves the file to the Recycle Bin. Throws IOException when Windows could not (the file is open elsewhere, the
        // user declined the shell's own warning, the bin cannot take it).
        void Recycle(string path);
    }
    internal sealed class MemoryRecycler : IFileRecycler
    {
        public readonly List<string> Calls = new List<string>();
        public bool Available = true;
        public bool CanRecycle(string path) { return Available; }
        public void Recycle(string path) { Calls.Add(path); }
    }
    // Only a fixed local drive is trusted to have a Recycle Bin. SHFileOperation with FOF_ALLOWUNDO deletes for good on a
    // removable or network drive (they have no bin), so those are refused before the call. The flags add
    // FOF_WANTNUKEWARNING: on a fixed drive whose bin is switched off or too small for the file the shell then asks before
    // destroying it, and a "No" aborts, so a quiet permanent deletion cannot happen.
    internal sealed class RecycleBinRecycler : IFileRecycler
    {
        private const int DriveFixed = 3;
        private const ushort Silent = 0x0004, NoConfirmation = 0x0010, AllowUndo = 0x0040, NoErrorUi = 0x0400, WantNukeWarning = 0x4000;
        private const uint Delete = 3;
        public bool CanRecycle(string path)
        {
            try
            {
                if (DownloadHistory.Remote(path) || !Path.IsPathRooted(path)) return false;
                var root = Path.GetPathRoot(path);
                return !string.IsNullOrEmpty(root) && root.Length >= 3 && GetDriveTypeW(root) == DriveFixed;
            }
            catch (Exception) { return false; }
        }
        public void Recycle(string path)
        {
            var from = path + "\0\0";
            var flags = (ushort)(Silent | NoConfirmation | AllowUndo | NoErrorUi | WantNukeWarning);
            int code; bool aborted;
            if (IntPtr.Size == 8)
            {
                var op = new Op64 { wFunc = Delete, pFrom = from, fFlags = flags };
                code = SHFileOperation64(ref op); aborted = op.fAnyOperationsAborted != 0;
            }
            else
            {
                var op = new Op32 { wFunc = Delete, pFrom = from, fFlags = flags };
                code = SHFileOperation32(ref op); aborted = op.fAnyOperationsAborted != 0;
            }
            if (code != 0 || aborted || File.Exists(path)) throw new IOException("The Recycle Bin did not take the file (code " + code + ").");
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
    }
    internal enum DeleteVerdict { Ok, NotAFile, Partial, Missing, Directory, Link, Changed, NoBin, Failed }
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
        // Check, then move to the Recycle Bin. Never a permanent deletion: no bin, no deletion.
        public static DeleteVerdict Delete(DownloadRecord record, IFileRecycler recycler)
        {
            var verdict = Check(record);
            if (verdict != DeleteVerdict.Ok) return verdict;
            if (!recycler.CanRecycle(record.Path)) return DeleteVerdict.NoBin;
            try { recycler.Recycle(record.Path); }
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
                case DeleteVerdict.Partial: case DeleteVerdict.NotAFile: case DeleteVerdict.Directory: case DeleteVerdict.Link: return "deleteNotFile";
                default: return "deleteFailed";
            }
        }
    }
}
