using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // Where a UMKD file is kept: Downloads\Univer УМКД\<course>\<section>, the section being the heading the site
    // puts over the file ("Силлабус", "Конспекты лекций…"). Both names are page text, so each is reduced to one safe
    // folder name and the finished path must still lie under the root.
    internal static class StudyFolders
    {
        public const string Root = "Univer УМКД";
        private const int NameLimit = 64, PathLimit = 240;
        // One folder name from page text, or "" when nothing usable is left.
        public static string Name(string value)
        {
            var text = Regex.Replace(value ?? "", @"[\p{Cc}\p{Cf}\s]+", " ");
            text = Regex.Replace(text, @"\s*[/\\|]\s*", ", ");
            text = Regex.Replace(text, @"\s*:\s*", " - ");
            text = Regex.Replace(text, @"[*?""<>]", "").Trim(' ', '.', ',', '-');
            if (text.Length > NameLimit)
            {
                var cut = text.LastIndexOf(' ', NameLimit);
                text = text.Substring(0, cut >= NameLimit / 2 ? cut : NameLimit);
                if (char.IsHighSurrogate(text[text.Length - 1])) text = text.Substring(0, text.Length - 1);
                text = text.TrimEnd(' ', '.', ',', '-');
            }
            if (text.Length == 0) return "";
            return Regex.IsMatch(text, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase) ? "_" + text : text;
        }
        // The folder of a course's section under Downloads; a file with no known course stays in the root itself.
        public static string Relative(string course, string section)
        {
            var path = Root; var first = Name(course);
            if (first.Length == 0) return path;
            path = Path.Combine(path, first);
            var second = Name(section);
            return second.Length == 0 ? path : Path.Combine(path, second);
        }
        // The full folder for a relative one, or downloads itself when the result would leave the UMKD root.
        public static string Resolve(string downloads, string relative)
        {
            if (string.IsNullOrEmpty(relative)) return downloads;
            try
            {
                var full = Path.GetFullPath(Path.Combine(downloads, relative));
                return Inside(full, downloads) ? full : downloads;
            }
            catch (Exception) { return downloads; }
        }
        // Whether path is the UMKD root under downloads or lies anywhere below it.
        public static bool Inside(string path, string downloads)
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(downloads, Root)).TrimEnd(Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
        // A deep folder leaves less room for the name: Windows paths of this framework end at 260 characters, so a long
        // stem is shortened and its extension kept.
        public static string Fit(string folder, string name)
        {
            var room = PathLimit - folder.Length - 1;
            if (name.Length <= room) return name;
            var extension = FileNames.Extension(name); var stem = name.Substring(0, name.Length - extension.Length);
            var keep = Math.Max(8, room - extension.Length);
            if (stem.Length <= keep) return name;
            stem = stem.Substring(0, keep);
            if (char.IsHighSurrogate(stem[stem.Length - 1])) stem = stem.Substring(0, stem.Length - 1);
            return stem.TrimEnd(' ', '.') + extension;
        }
    }
}
