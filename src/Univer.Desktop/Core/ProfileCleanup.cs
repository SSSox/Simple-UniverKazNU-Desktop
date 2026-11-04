using System;
using System.Collections.Generic;
using System.IO;

namespace Univer.Desktop.Core
{
    internal static class ProfileCleanup
    {
        // GetFullPath is lexical: check every existing ancestor before following directory entries.
        public static bool IsPlainPath(string path)
        {
            try
            {
                for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
                    if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        public static bool IsPlainTree(string root)
        {
            if (!IsPlainPath(root)) return false;
            try
            {
                var pending = new Stack<string>(); pending.Push(root);
                while (pending.Count > 0)
                    foreach (var directory in Directory.GetDirectories(pending.Pop()))
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return false;
                        pending.Push(directory);
                    }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
