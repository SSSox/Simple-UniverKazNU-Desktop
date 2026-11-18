using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Univer.Desktop.Core
{
    // The last step of a download from the site: the finished file gets its name in the folder and the mark of a file
    // that came from the internet.
    internal static class SavedFile
    {
        // Moves the finished file to its name in folder, adding " (1)", " (2)"… when that name is taken: File.Move never
        // overwrites a user's existing file. A file the app made itself (origin null, the grade summary) gets no mark.
        public static string Keep(string temp, string folder, string name, string origin)
        {
            for (var i = 0; ; i++)
            {
                var destination = Path.Combine(folder, i == 0 ? name : Path.GetFileNameWithoutExtension(name) + " (" + i + ")" + Path.GetExtension(name));
                try { File.Move(temp, destination); if (origin != null) Mark(destination, origin); return destination; }
                catch (IOException) { if (i >= 999 || !File.Exists(destination)) throw; }
            }
        }
        // Zone.Identifier, as browsers write it: Windows SmartScreen and Office's Protected View then treat the file as a
        // download. It is an NTFS stream beside the file's data, so a drive without such streams (FAT, some network
        // shares) has none, and that never fails the download.
        public static bool Mark(string path, string origin)
        {
            try
            {
                using (var handle = CreateFileW(Long(path) + ZoneStream, 0x40000000, 0, IntPtr.Zero, 2, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return false;
                    using (var stream = new FileStream(handle, FileAccess.Write))
                    {
                        var bytes = Encoding.ASCII.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + origin + "\r\n");
                        stream.Write(bytes, 0, bytes.Length);
                    }
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
        }
        // The file's mark as text, or null when it has none.
        public static string Zone(string path)
        {
            try
            {
                using (var handle = CreateFileW(Long(path) + ZoneStream, 0x80000000, 1, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid) return null;
                    using (var reader = new StreamReader(new FileStream(handle, FileAccess.Read), Encoding.ASCII)) return reader.ReadToEnd();
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
        }
        private const string ZoneStream = ":Zone.Identifier";
        // The stream's name makes a path near the 260-character limit longer than Windows may take without this prefix.
        // The prefix turns off Windows' own reading of "/" and "..", so a shorter path goes as it is.
        private static string Long(string path)
        {
            if (path.Length + ZoneStream.Length < 260 || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            path = Path.GetFullPath(path);
            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }
        // .NET Framework's own file APIs refuse a path with a stream name, so the stream is opened through Windows.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    }
}
