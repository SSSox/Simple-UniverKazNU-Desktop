using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // Recognising a file that was downloaded before, without keeping the address it came from.
    internal static class DownloadKeys
    {
        // The first 128 bits of SHA-256 over the address without its fragment.
        public static string Of(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            var hash = url.IndexOf('#'); if (hash >= 0) url = url.Substring(0, hash);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url)), 0, 16).Replace("-", "").ToLowerInvariant();
        }
        // A saved name without the " (1)" a repeated save adds and without its extension, for comparing with a link caption.
        public static string Bare(string name)
        {
            var value = (name ?? "").Trim(); var extension = Path.GetExtension(value);
            if (extension.Length > 1 && extension.Length <= 6 && extension.Skip(1).All(char.IsLetterOrDigit)) value = value.Substring(0, value.Length - extension.Length);
            return Regex.Replace(value, @"\s*\(\d+\)$", "").Trim().ToLowerInvariant().Replace('ё', 'е');
        }
    }
    // Temporary names of downloads in progress. A process that exits mid-download cannot run its own cleanup,
    // so the next start removes them; only this exact pattern is touched, never a user's own files.
    internal static class PartialDownloads
    {
        private static readonly Regex Pattern = new Regex("^\\.univer-[0-9a-f]{32}\\.part$");
        public static string NewName() { return ".univer-" + Guid.NewGuid().ToString("N") + ".part"; }
        public static int CleanUp(string folder, TimeSpan age)
        {
            var removed = 0;
            try
            {
                if (!Directory.Exists(folder)) return 0;
                foreach (var file in Directory.EnumerateFiles(folder, ".univer-*.part"))
                {
                    if (!Pattern.IsMatch(Path.GetFileName(file)) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < age) continue;
                    try { File.Delete(file); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return removed;
        }
    }
}
