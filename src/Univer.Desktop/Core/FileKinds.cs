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
        // Files saved from the site's own pages rather than from a course; the course filter lists them apart.
        public static bool PageSource(string source)
        {
            return System.Text.RegularExpressions.Regex.IsMatch((source ?? "").Trim(), @"^(журнал посещ|журнал успев|новост|сегодня|мой кабинет|сообщени|информационный бюллетень|жаңалық|бүгін|менің кабинетім|хабарлама|ақпараттық бюллетень|news|today|my account|messages|information bulletin|attendance)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        private static string Fold(string text) { return (text ?? "").ToLowerInvariant().Replace('ё', 'е'); }
        // A search as its words, split once for a whole list; an empty search has none.
        public static string[] Words(string query)
        {
            var folded = Fold(query).Trim();
            return folded.Length == 0 ? new string[0] : Regex.Split(folded, @"\s+");
        }
        // A filter's kind: Other also holds the types without a chip of their own.
        public static bool Is(FileKind actual, FileKind wanted)
        {
            return wanted == FileKind.Other ? actual == FileKind.Other || actual == FileKind.Text || actual == FileKind.Archive || actual == FileKind.Html : actual == wanted;
        }
        public static bool Matches(DownloadRecord item, string query, FileKind? kind, string source = null) { return Matches(item, Words(query), kind, source); }
        public static bool Matches(DownloadRecord item, string[] words, FileKind? kind, string source = null)
        {
            if (source != null && !string.Equals((item.Source ?? "").Trim(), source, StringComparison.CurrentCultureIgnoreCase)) return false;
            if (kind.HasValue && !Is(Of(item.FileName), kind.Value)) return false;
            if (words.Length == 0) return true;
            var text = Fold(item.FileName + " " + item.Source);
            return words.All(word => text.Contains(word));
        }
    }
}
