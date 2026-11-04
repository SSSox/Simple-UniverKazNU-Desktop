using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    internal enum FileKind { Pdf, Word, Excel, PowerPoint, Image, Text, Archive, Html, Other }
    // File types as the downloads list groups and labels them.
    internal static class FileKinds
    {
        public static FileKind Of(string name)
        {
            switch (System.IO.Path.GetExtension(name ?? "").ToLowerInvariant())
            {
                case ".pdf": return FileKind.Pdf;
                case ".doc": case ".docx": case ".dot": case ".dotx": case ".rtf": case ".odt": return FileKind.Word;
                case ".xls": case ".xlsx": case ".xlsm": case ".xlt": case ".xltx": case ".csv": case ".ods": return FileKind.Excel;
                case ".ppt": case ".pptx": case ".pptm": case ".pps": case ".ppsx": case ".pot": case ".potx": case ".odp": return FileKind.PowerPoint;
                case ".html": case ".htm": case ".xhtml": return FileKind.Html;
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".webp": return FileKind.Image;
                case ".txt": return FileKind.Text;
                case ".zip": case ".rar": case ".7z": return FileKind.Archive;
                default: return FileKind.Other;
            }
        }
        public static string Badge(FileKind kind)
        {
            switch (kind)
            {
                case FileKind.Pdf: return "PDF";
                case FileKind.Word: return "DOC";
                case FileKind.Excel: return "XLS";
                case FileKind.PowerPoint: return "PPT";
                case FileKind.Image: return "IMG";
                case FileKind.Text: return "TXT";
                case FileKind.Archive: return "ZIP";
                case FileKind.Html: return "HTML";
                default: return "FILE";
            }
        }
        // Kazakh uses the same Cyrillic unit names as Russian.
        public static string Size(long bytes, string language)
        {
            if (bytes < 0) return "";
            var units = language == "en" ? new[] { "B", "KB", "MB", "GB" } : new[] { "Б", "КБ", "МБ", "ГБ" };
            double value = bytes; var unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return (unit == 0 ? value.ToString("0") : value.ToString(value < 10 ? "0.0" : "0")) + " " + units[unit];
        }
        // Search ignores case and treats "ё" as "е", like the site menu and document filters.
        public static bool Matches(DownloadRecord item, string query, FileKind? kind, string source = null)
        {
            if (source != null && !string.Equals((item.Source ?? "").Trim(), source, StringComparison.CurrentCultureIgnoreCase)) return false;
            if (kind.HasValue)
            {
                var actual = Of(item.FileName);
                if (kind == FileKind.Other ? actual != FileKind.Other && actual != FileKind.Text && actual != FileKind.Archive && actual != FileKind.Html : actual != kind) return false;
            }
            if (string.IsNullOrWhiteSpace(query)) return true;
            Func<string, string> fold = s => (s ?? "").ToLowerInvariant().Replace('ё', 'е');
            var words = Regex.Split(fold(query).Trim(), @"\s+");
            var text = fold(item.FileName + " " + item.Source);
            return words.All(w => text.Contains(w));
        }
    }
}
