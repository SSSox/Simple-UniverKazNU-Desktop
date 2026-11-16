using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // «Скачать все» in a section of a course's UMKD page: the section's heading and its files' addresses, as the page
    // sends them. Any script on the site could send the same message, so only UMKD file addresses on the exact origin
    // pass, each file once (by its path, whatever the query, case or leading zeros) and at most Limit of them; anything
    // else makes the whole message void. Stop says the button showed a run when it was clicked.
    internal sealed class DownloadBatch
    {
        public const string MessagePrefix = "univer-download-all:";
        public const int Limit = 200;
        private const int MaxLength = 64 * 1024;
        public string Section { get; private set; }
        public IList<string> Urls { get; private set; }
        public bool Stop { get; private set; }
        public static DownloadBatch Parse(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > MaxLength) return null;
            try
            {
                var values = new JavaScriptSerializer { MaxJsonLength = MaxLength }.Deserialize<Dictionary<string, object>>(payload);
                object section, urls, stop;
                if (values == null || !values.TryGetValue("section", out section) || !values.TryGetValue("urls", out urls)) return null;
                var name = (section as string ?? "").Trim();
                var list = urls as IEnumerable;
                if (name.Length == 0 || name.Length > 200 || list == null || urls is string) return null;
                var unique = new List<string>(); var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in list)
                {
                    var url = item as string;
                    if (!SitePolicy.ProtectedDownload(url)) return null;
                    if (paths.Add(FileKey(url))) unique.Add(url);
                    if (unique.Count > Limit) return null;
                }
                var halt = values.TryGetValue("stop", out stop) && stop is bool && (bool)stop;
                return unique.Count == 0 ? null : new DownloadBatch { Section = name, Urls = unique, Stop = halt };
            }
            catch (Exception) { return null; }
        }
        private static readonly Regex LeadingZeros = new Regex(@"(?<=/)0+(?=\d)");
        // The site reads /student/umkd/get/{ids} whatever the case and leading zeros, so they name the same file.
        private static string FileKey(string url) { return LeadingZeros.Replace(new Uri(url).AbsolutePath.TrimEnd('/').ToLowerInvariant(), ""); }
        // What a click on a section's button does. Stop comes only from a button that shows a run, and stops only that
        // run: a late one, after the run has ended, just gets its label back. A plain click while that section runs (a
        // double click, or a button the page drew again) shows the run again instead of stopping it.
        public static SectionClick Choose(bool stop, bool runningHere, bool runningElsewhere)
        {
            if (stop) return runningHere ? SectionClick.Stop : SectionClick.Relabel;
            if (runningHere) return SectionClick.Show;
            return runningElsewhere ? SectionClick.Busy : SectionClick.Start;
        }
    }
    internal enum SectionClick { Start, Stop, Show, Relabel, Busy }
}
