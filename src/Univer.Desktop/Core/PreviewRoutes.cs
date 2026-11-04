using System;
using System.IO;
using System.Linq;

namespace Univer.Desktop.Core
{
    // Viewer addresses on a reserved in-memory HTTPS host: /<token>/<name> is the page, /<token>/<folder>/<name> its
    // resource (a picture, the PDF), /assets/<folder>/<path> one of the app's reader files.
    internal static class PreviewRoutes
    {
        public const string Host = "univer-files.local";
        public const string AssetsSegment = "assets";
        private static readonly string[] SharedFolders = { "pdfjs", "pdf-reader" };
        public static string PageUrl(string token, string name)
        { return "https://" + Host + "/" + token + "/" + Uri.EscapeDataString(string.IsNullOrEmpty(name) ? "file" : name); }
        // The PDF file beside the reader page, relative to it.
        public static string FilePath(string name) { return "file/" + Uri.EscapeDataString(string.IsNullOrEmpty(name) ? "file" : name); }
        public static string FileKey(string name) { return "file/" + (string.IsNullOrEmpty(name) ? "file" : name); }
        public static bool Owns(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == Host && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);
        }
        // Unescaped path segments. Empty, dot and separator-bearing segments are refused, so a segment can never step
        // outside its folder or name a drive.
        public static bool TrySplit(string absolutePath, out string[] segments)
        {
            segments = null;
            if (string.IsNullOrEmpty(absolutePath) || absolutePath[0] != '/') return false;
            var parts = absolutePath.Substring(1).Split('/');
            if (parts.Length < 2) return false;
            for (var i = 0; i < parts.Length; i++)
            {
                string part;
                try { part = Uri.UnescapeDataString(parts[i]); } catch (Exception) { return false; }
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || part.Any(char.IsControl)) return false;
                parts[i] = part;
            }
            segments = parts;
            return true;
        }
        // Resolves /assets/<folder>/<path> to a file of the app's own reader, or null for anything else.
        public static string AssetFile(string assetsRoot, string[] segments)
        {
            if (segments == null || segments.Length < 3 || segments[0] != AssetsSegment || Array.IndexOf(SharedFolders, segments[1]) < 0) return null;
            for (var i = 1; i < segments.Length; i++)
                if (!segments[i].All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) || segments[i].StartsWith(".", StringComparison.Ordinal)) return null;
            var root = Path.GetFullPath(Path.Combine(assetsRoot, segments[1])) + Path.DirectorySeparatorChar;
            var file = Path.GetFullPath(Path.Combine(assetsRoot, string.Join(Path.DirectorySeparatorChar.ToString(), segments, 1, segments.Length - 1)));
            return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file) ? file : null;
        }
        public static string MimeType(string path)
        {
            switch (Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".mjs": case ".js": return "text/javascript";
                case ".css": return "text/css";
                case ".wasm": return "application/wasm";
                case ".txt": case "": return "text/plain";
                default: return "application/octet-stream";
            }
        }
    }
}
