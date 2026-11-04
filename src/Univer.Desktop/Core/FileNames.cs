using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    internal static class FileNames
    {
        public static string Safe(string value)
        {
            var name = Regex.Replace((value ?? "").Trim('"'), @"[\p{Cc}\p{Cf}/\\:*?""<>|]", "_").Trim(' ', '.');
            if (name.Length > 150)
            {
                var extension = Path.GetExtension(name);
                if (extension.Length > 12) extension = "";
                var stem = name.Substring(0, 150 - extension.Length);
                // Never keep half of a surrogate pair (an emoji or a rare character) before the extension.
                if (char.IsHighSurrogate(stem[stem.Length - 1])) stem = stem.Substring(0, stem.Length - 1);
                name = stem.TrimEnd(' ', '.') + extension;
            }
            if (name.Length > 0 && char.IsHighSurrogate(name[name.Length - 1])) name = name.Substring(0, name.Length - 1);
            if (name.Length == 0) name = "Univer-document";
            if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
            return name;
        }
        // A number-only UMKD URL is not a file name. Prefer the server, then the
        // observed link/CEF suggestion, and recover missing extensions from the payload.
        public static string Resolve(string disposition, string hint, string url, string mediaType, byte[] prefix, int count, string savedPath = null)
        {
            var name = HeaderName(disposition);
            if (string.IsNullOrWhiteSpace(name)) name = hint;
            if (string.IsNullOrWhiteSpace(name))
            {
                Uri parsed;
                if (Uri.TryCreate(url, UriKind.Absolute, out parsed))
                {
                    var segment = Uri.UnescapeDataString(Path.GetFileName(parsed.AbsolutePath));
                    if (Regex.IsMatch(segment, @"\.[a-z0-9]{1,8}$", RegexOptions.IgnoreCase)) name = segment;
                }
            }
            name = Safe(name);
            var extension = SignatureExtension(prefix, count, savedPath);
            var current = Extension(name);
            if (extension == ".zip" && Regex.IsMatch(current, @"^\.(docx|xlsx|pptx|odt|ods|odp|epub|jar|zip)$")) extension = "";
            if (extension == ".ole")
            {
                // Legacy Office: keep a matching name, otherwise read the compound file's stream names.
                extension = Regex.IsMatch(current, @"^\.(doc|dot|xls|xlt|ppt|pps|pot|msg|vsd)$") ? "" : OleExtension(savedPath) ?? LegacyMime(mediaType) ?? ".doc";
            }
            // A dotted title ("Лекция 1. Введение") has no extension: keep it whole and append one.
            var stem = current.Length > 0 ? Path.GetFileNameWithoutExtension(name) : name;
            if (extension.Length > 0 && current != extension) name = stem + extension;
            else if (current.Length == 0) name += MimeExtension(mediaType);
            return Safe(name);
        }
        // Only a short ASCII suffix with a letter is treated as an existing extension.
        public static string Extension(string name)
        {
            var value = Path.GetExtension(name ?? "").ToLowerInvariant();
            return Regex.IsMatch(value, @"^\.(?=[a-z0-9]*[a-z])[a-z0-9]{1,8}$") ? value : "";
        }
        private static string LegacyMime(string mediaType)
        {
            var extension = MimeExtension(mediaType);
            return extension == ".doc" || extension == ".xls" || extension == ".ppt" ? extension : null;
        }
        private static string OleExtension(string savedPath)
        {
            if (savedPath == null) return null;
            try
            {
                var bytes = new byte[Math.Min(new FileInfo(savedPath).Length, 64L * 1024 * 1024)];
                using (var file = File.OpenRead(savedPath)) { var read = 0; while (read < bytes.Length) { var n = file.Read(bytes, read, bytes.Length - read); if (n == 0) break; read += n; } }
                // Directory entries store UTF-16LE stream names.
                if (Contains(bytes, Encoding.Unicode.GetBytes("WordDocument"))) return ".doc";
                if (Contains(bytes, Encoding.Unicode.GetBytes("Workbook")) || Contains(bytes, Encoding.Unicode.GetBytes("Book\0"))) return ".xls";
                if (Contains(bytes, Encoding.Unicode.GetBytes("PowerPoint Document"))) return ".ppt";
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
        private static bool Contains(byte[] data, byte[] pattern)
        {
            for (var i = 0; i <= data.Length - pattern.Length; i++)
            {
                if (data[i] != pattern[0]) continue;
                var j = 1; while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return true;
            }
            return false;
        }
        public static string HeaderName(string disposition)
        {
            if (string.IsNullOrWhiteSpace(disposition)) return null;
            var extended = Regex.Match(disposition, @"(?:^|;)\s*filename\*\s*=\s*(?:""(?<v>[^""]*)""|(?<v>[^;]*))", RegexOptions.IgnoreCase);
            if (extended.Success)
            {
                var value = extended.Groups["v"].Value.Trim();
                var parts = value.Split(new[] { '\'' }, 3);
                if (parts.Length == 3)
                {
                    try
                    {
                        var encoding = Encoding.GetEncoding(parts[0]);
                        using (var bytes = new MemoryStream())
                        {
                            for (var i = 0; i < parts[2].Length; i++)
                            {
                                if (parts[2][i] == '%' && i + 2 < parts[2].Length)
                                { bytes.WriteByte(Convert.ToByte(parts[2].Substring(i + 1, 2), 16)); i += 2; }
                                else if (parts[2][i] <= 127) bytes.WriteByte((byte)parts[2][i]);
                                else throw new FormatException();
                            }
                            return encoding.GetString(bytes.ToArray());
                        }
                    }
                    catch (ArgumentException) { }
                    catch (FormatException) { }
                }
            }
            var plain = Regex.Match(disposition, @"(?:^|;)\s*filename\s*=\s*(?:""(?<v>[^""]*)""|(?<v>[^;]*))", RegexOptions.IgnoreCase);
            if (!plain.Success) return null;
            var result = plain.Groups["v"].Value.Trim();
            var mime = Regex.Match(result, @"^=\?([^?]+)\?([bq])\?([^?]*)\?=$", RegexOptions.IgnoreCase);
            if (mime.Success)
            {
                try
                {
                    var encoding = Encoding.GetEncoding(mime.Groups[1].Value);
                    if (mime.Groups[2].Value.Equals("b", StringComparison.OrdinalIgnoreCase)) return encoding.GetString(Convert.FromBase64String(mime.Groups[3].Value));
                    var quoted = mime.Groups[3].Value.Replace('_', ' ');
                    using (var bytes = new MemoryStream())
                    {
                        for (var i = 0; i < quoted.Length; i++)
                        {
                            if (quoted[i] == '=' && i + 2 < quoted.Length) { bytes.WriteByte(Convert.ToByte(quoted.Substring(i + 1, 2), 16)); i += 2; }
                            else bytes.WriteByte((byte)quoted[i]);
                        }
                        return encoding.GetString(bytes.ToArray());
                    }
                }
                catch (ArgumentException) { }
                catch (FormatException) { }
            }
            // HttpClient exposes legacy non-ASCII header bytes as Latin-1. Univer
            // sends UTF-8 in some plain filename parameters, without filename*.
            for (var attempt = 0; attempt < 2 && result.IndexOfAny(new[] { '\u00d0', '\u00d1', '\u00c2', '\u00c3' }) >= 0; attempt++)
            {
                try
                {
                    var latin = Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    var decoded = new UTF8Encoding(false, true).GetString(latin.GetBytes(result));
                    if (decoded == result) break;
                    result = decoded;
                }
                catch (ArgumentException) { break; }
            }
            return result;
        }
        private static string SignatureExtension(byte[] bytes, int count, string savedPath)
        {
            if (bytes == null || count < 4) return "";
            if (bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F') return ".pdf";
            if (count >= 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G') return ".png";
            if (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return ".jpg";
            if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8') return ".gif";
            if (count >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "{\\rtf") return ".rtf";
            if (count >= 8 && bytes[0] == 0xd0 && bytes[1] == 0xcf && bytes[2] == 0x11 && bytes[3] == 0xe0 && bytes[4] == 0xa1 && bytes[5] == 0xb1 && bytes[6] == 0x1a && bytes[7] == 0xe1) return ".ole";
            if (count >= 6 && bytes[0] == 'R' && bytes[1] == 'a' && bytes[2] == 'r' && bytes[3] == '!' && bytes[4] == 0x1a && bytes[5] == 0x07) return ".rar";
            if (count >= 6 && bytes[0] == '7' && bytes[1] == 'z' && bytes[2] == 0xbc && bytes[3] == 0xaf && bytes[4] == 0x27 && bytes[5] == 0x1c) return ".7z";
            if (bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4)
            {
                if (savedPath != null)
                {
                    try
                    {
                        using (var archive = ZipFile.OpenRead(savedPath))
                        {
                            if (archive.GetEntry("word/document.xml") != null) return ".docx";
                            if (archive.GetEntry("xl/workbook.xml") != null) return ".xlsx";
                            if (archive.GetEntry("ppt/presentation.xml") != null) return ".pptx";
                        }
                    }
                    catch (InvalidDataException) { }
                    catch (IOException) { }
                }
                return ".zip";
            }
            return "";
        }
        private static string MimeExtension(string mediaType)
        {
            switch ((mediaType ?? "").ToLowerInvariant().Split(';')[0].Trim())
            {
                case "application/pdf": return ".pdf";
                case "application/msword": return ".doc";
                case "application/vnd.openxmlformats-officedocument.wordprocessingml.document": return ".docx";
                case "application/vnd.ms-excel": return ".xls";
                case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": return ".xlsx";
                case "application/vnd.ms-powerpoint": return ".ppt";
                case "application/vnd.openxmlformats-officedocument.presentationml.presentation": return ".pptx";
                case "application/rtf": case "text/rtf": return ".rtf";
                case "application/zip": case "application/x-zip-compressed": return ".zip";
                case "application/x-rar-compressed": case "application/vnd.rar": return ".rar";
                case "application/x-7z-compressed": return ".7z";
                case "text/plain": return ".txt";
                case "text/csv": return ".csv";
                case "image/jpeg": return ".jpg";
                case "image/png": return ".png";
                default: return ".bin";
            }
        }
        public static bool CanOpen(string path)
        { return Regex.IsMatch(Path.GetExtension(path ?? ""), @"^\.(pdf|docx?|xlsx?|pptx?|rtf|txt|csv|png|jpe?g|gif|odt|ods|odp)$", RegexOptions.IgnoreCase); }
        public static bool LoginHtml(byte[] bytes, int count)
        {
            var text = Encoding.UTF8.GetString(bytes, 0, count).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            return Regex.IsMatch(text, @"^(<!doctype\s+html|<html\b|<head\b|<body\b|<form\b)", RegexOptions.IgnoreCase) ||
                text.IndexOf("id=\"login_frm\"", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("id='login_frm'", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
