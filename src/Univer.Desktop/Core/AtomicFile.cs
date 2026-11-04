using System.IO;

namespace Univer.Desktop.Core
{
    // Replaces a file in one step: the new content is written beside it and swapped in, so an interrupted write leaves
    // the old file or the new one, never a half-written file.
    internal static class AtomicFile
    {
        public static void Write(string path, byte[] bytes)
        {
            var temp = path + ".new";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }
}
