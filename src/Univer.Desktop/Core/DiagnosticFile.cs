using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // The file a student can attach to a report of a fault (Settings → «Файл для сообщения об ошибке»): the app's version,
    // engine, Windows, settings as on/off and numbers, what the window shows now, and the last events. Every line, the
    // facts as much as the events, goes through Clean, the one place that removes what could name the student: the login,
    // the Windows user and computer, profile paths, e-mail addresses, long numbers (IIN, phone, record ids), page
    // addresses' queries and ids, and long tokens. Nothing of the site's pages is ever given to it.
    internal static class DiagnosticFile
    {
        internal const int MaxLine = 300, MaxEvents = 40, MinSecret = 3;
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
        private static readonly Regex Control = new Regex(@"[\u0000-\u001F\u007F-\u009F\u2028\u2029]+", Options, Limit);
        private static readonly Regex Url = new Regex(@"\b(?:https?|file|ftp)://[^\s""'<>]+", Options, Limit);
        private static readonly Regex Profile = new Regex(@"(?<=\b[a-z]:[\\/](?:users|documents and settings)[\\/])[^\\/:*?""<>|\s]+", Options, Limit);
        private static readonly Regex Mail = new Regex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}", Options, Limit);
        private static readonly Regex Phone = new Regex(@"\+?\d[\d\s()\-]{7,}\d", Options, Limit);
        private static readonly Regex Number = new Regex(@"\d{6,}", Options, Limit);
        // Without «/» and «.», so an address's host and path are not taken for one; a JWT's parts are each long enough.
        private static readonly Regex Token = new Regex(@"[A-Za-z0-9_\-+=%]{24,}", Options, Limit);

        // One line with nothing that could name the student. Secrets are whole words (a Windows user called «User» leaves
        // «Users» alone); ones shorter than MinSecret are ignored, or a one-letter login would empty the file.
        internal static string Clean(string text, IEnumerable<string> secrets)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                var line = Control.Replace(text, " ");
                if (line.Length > 4 * MaxLine) line = line.Substring(0, 4 * MaxLine);
                // Addresses first, while user info and queries are still whole; a secret left in a path goes next.
                line = Url.Replace(line, m => Address(m.Value));
                foreach (var secret in (secrets ?? Enumerable.Empty<string>()).Where(s => s != null && s.Trim().Length >= MinSecret).Select(s => s.Trim()).OrderByDescending(s => s.Length))
                    line = Regex.Replace(line, @"(?<![\p{L}\p{N}])" + Regex.Escape(secret) + @"(?![\p{L}\p{N}])", "<hidden>", Options, Limit);
                line = Profile.Replace(line, "<user>");
                line = Mail.Replace(line, "<e-mail>");
                line = Token.Replace(line, m => m.Value.Any(char.IsDigit) || m.Value.Length >= 32 ? "<token>" : m.Value);
                line = Phone.Replace(line, m => m.Value.Count(char.IsDigit) >= 10 ? "<number>" : m.Value);
                line = Number.Replace(line, "<number>");
                line = line.Trim();
                return line.Length > MaxLine ? line.Substring(0, MaxLine - 1) + "…" : line;
            }
            catch (RegexMatchTimeoutException) { return "<line removed>"; }
        }
        // An address keeps its kind, host and the shape of its path: the query, the fragment, user info and every number
        // in the path go (/student/attentionStudents/123/ → /student/attentionStudents/{n}/).
        internal static string Address(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return "<address>";
            if (uri.IsFile || uri.IsUnc) return "file:///<file>";
            var path = Regex.Replace(uri.AbsolutePath, @"[^/]*\d[^/]*", "{n}", Options, Limit);
            if (path.Length > 120) path = path.Substring(0, 120) + "…";
            return uri.Scheme + (uri.Host.Length > 0 ? "://" + uri.Host : ":") + path;
        }
        // The whole file: a heading, the facts as «name: value», then the events oldest first.
        internal static string Build(IEnumerable<KeyValuePair<string, string>> facts, IEnumerable<DiagnosticEvent> events, IEnumerable<string> secrets, DateTime now)
        {
            var hidden = (secrets ?? Enumerable.Empty<string>()).ToList();
            var text = new StringBuilder();
            text.AppendLine("Univer diagnostics " + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            text.AppendLine("No login, password, cookies, page contents or personal data are included.");
            text.AppendLine();
            foreach (var fact in facts ?? Enumerable.Empty<KeyValuePair<string, string>>())
                text.AppendLine(Clean(fact.Key + ": " + fact.Value, hidden));
            text.AppendLine();
            text.AppendLine("Recent events:");
            var list = (events ?? Enumerable.Empty<DiagnosticEvent>()).ToList();
            if (list.Count == 0) text.AppendLine("(none)");
            foreach (var item in list.Skip(Math.Max(0, list.Count - MaxEvents)))
                text.AppendLine(item.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + Clean(item.Text, hidden));
            return text.ToString();
        }
        internal static string FileName(DateTime now) { return "Univer-diagnostics-" + now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".txt"; }
    }
    internal sealed class DiagnosticEvent
    {
        public DateTime At; public string Text = "";
    }
    // The last events of this run, in memory only: status keys and failure codes, each cleaned when it is recorded.
    internal sealed class DiagnosticLog
    {
        private readonly Queue<DiagnosticEvent> items = new Queue<DiagnosticEvent>();
        private readonly Func<IEnumerable<string>> secrets;
        internal DiagnosticLog(Func<IEnumerable<string>> secrets) { this.secrets = secrets; }
        internal void Add(DateTime at, string text)
        {
            var clean = DiagnosticFile.Clean(text, secrets?.Invoke());
            if (clean.Length == 0) return;
            var last = items.Count > 0 ? items.Last() : null;
            if (last != null && last.Text == clean && (at - last.At).TotalSeconds < 5) return;
            items.Enqueue(new DiagnosticEvent { At = at, Text = clean });
            while (items.Count > DiagnosticFile.MaxEvents) items.Dequeue();
        }
        internal IList<DiagnosticEvent> Items { get { return items.ToList(); } }
    }
}
