using System.IO;

namespace Univer.Desktop.Core
{
    // Replaces a file in one step: the new content is written beside it and swapped in, so an interrupted write leaves
    // the old file or the new one, never a half-written file.
    internal static class AtomicFile
    {
        public static void Write(string path, byte[] bytes, bool durable = false)
        {
            var temp = path + ".new";
            // A durable file (preferences, credentials, the PIN) is written through to the disk before the swap, so a power
            // cut cannot leave an empty file in its place; history and caches, written often, skip that cost.
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, durable ? FileOptions.WriteThrough : FileOptions.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(durable); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }
}
