using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Univer.Desktop.UI;

namespace Univer.Desktop.Core
{
    // One course row of «Текущая аттестация» (/student/attestation/): the discipline and its cells in the page's order.
    internal sealed class GradeRow
    {
        public string Course = "";
        public List<string> Values = new List<string>();
    }
    // What the attestation table said at one reading: its headings and rows, as text. Nothing else of the page is kept.
    internal sealed class GradeSheet
    {
        internal const int Version = 1;
        internal const int MaxRows = 60, MaxColumns = 16, MaxText = 200;
        public List<string> Headings = new List<string>();
        public List<GradeRow> Rows = new List<GradeRow>();
        public string Account = "";
        public DateTime ReadAt;

        // The table whose first row is only headings (four or more) and names a score column; the course rows are the ones
        // with a cell under every heading (the GPA/credits total row spans columns and is not a course).
        internal static GradeSheet Parse(string html, string account, DateTime at)
        {
            // Without the login's mark nothing is read or kept: a reading could not be told from another account's.
            if (string.IsNullOrEmpty(account)) return null;
            var tables = HtmlTables.Read(html);
            if (tables == null) return null;
            foreach (var table in tables)
            {
                if (table.Count < 1) continue;
                var head = table[0];
                if (head.Count < 4 || head.Count > MaxColumns || head.Any(c => !c.Heading)) continue;
                var headings = head.Select(c => Fold(c.Text)).ToList();
                if (!headings.Any(Score)) continue;
                var sheet = new GradeSheet { Headings = headings, Account = account ?? "", ReadAt = at };
                foreach (var row in table.Skip(1))
                {
                    if (row.Count != head.Count || row.Any(c => c.Heading || c.Span > 1)) continue;
                    var name = Fold(row[0].Text);
                    if (name.Length == 0) continue;
                    if (sheet.Rows.Count >= MaxRows) return null;
                    sheet.Rows.Add(new GradeRow { Course = name, Values = row.Skip(1).Select(c => Fold(c.Text)).ToList() });
                }
                return sheet.Rows.Count > 0 ? sheet : null;
            }
            return null;
        }
        // Scores are the control columns, the exam and the total; credits, GPA, the letter and its word follow from them.
        private static readonly Regex ScoreHeading = new Regex(@"^(?:(?:РК|RK|АБ|МБ|ПА|PA|ИК|IC)\s*\d?\*?|Сумма|Sum|Total|Жиыны|Барлығы|Қорытынды|Итог|Экзамен|Емтихан|Exam)\*?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        internal static bool Score(string heading) { return ScoreHeading.IsMatch(heading ?? ""); }
        internal static string Fold(string text)
        {
            var folded = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return folded.Length > MaxText ? folded.Substring(0, MaxText) : folded;
        }

        // What is new since the last reading: changed score cells of the same courses under the same headings. A first
        // reading, other courses (a new semester, the site's language) or other headings say nothing: the new reading is
        // then the baseline. A cell that became empty is not news.
        internal static List<GradeChange> Changes(GradeSheet before, GradeSheet now)
        {
            var changes = new List<GradeChange>();
            if (before == null || now == null || !Same(before, now)) return changes;
            for (var r = 0; r < now.Rows.Count; r++)
            {
                var was = before.Rows[r]; var row = now.Rows[r];
                var cells = new List<string>();
                for (var c = 0; c < row.Values.Count && c + 1 < now.Headings.Count; c++)
                {
                    var heading = now.Headings[c + 1];
                    if (!Score(heading)) continue;
                    var value = row.Values[c];
                    if (value.Length == 0 || value == was.Values[c]) continue;
                    cells.Add(heading.TrimEnd('*') + " " + value);
                }
                if (cells.Count > 0) changes.Add(new GradeChange { Course = row.Course, Cells = cells });
            }
            return changes;
        }
        private static bool Same(GradeSheet a, GradeSheet b)
        {
            return a.Account == b.Account && a.Headings.SequenceEqual(b.Headings) && a.Rows.Count == b.Rows.Count &&
                a.Rows.Zip(b.Rows, (x, y) => x.Course == y.Course && x.Values.Count == y.Values.Count).All(same => same);
        }

        internal string Serialize()
        {
            var rows = Rows.Select(r => new Dictionary<string, object> { { "course", r.Course }, { "values", r.Values.Cast<object>().ToList() } }).ToList();
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "version", Version }, { "account", Account },
                { "read", ReadAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) }, { "headings", Headings.Cast<object>().ToList() }, { "rows", rows } });
        }
        // The kept reading, checked as strictly as a page: a damaged file, another account's or another version is none.
        internal static GradeSheet Load(string raw, string account)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > 200000 || string.IsNullOrEmpty(account)) return null;
            try
            {
                var json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                object version, owner, read, headings, rows; DateTime at;
                if (json == null || !json.TryGetValue("version", out version) || !(version is int) || (int)version != Version) return null;
                if (!json.TryGetValue("account", out owner) || !(owner is string) || (string)owner != (account ?? "")) return null;
                if (!json.TryGetValue("read", out read) || !(read is string) || !DateTime.TryParseExact((string)read, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at)) return null;
                var heads = json.TryGetValue("headings", out headings) ? Texts(headings as IList) : null;
                if (heads == null || heads.Count < 4 || heads.Count > MaxColumns) return null;
                var list = json.TryGetValue("rows", out rows) ? rows as IList : null;
                if (list == null || list.Count == 0 || list.Count > MaxRows) return null;
                var sheet = new GradeSheet { Headings = heads, Account = (string)owner, ReadAt = at };
                foreach (var item in list)
                {
                    var row = item as Dictionary<string, object>; object course, values;
                    if (row == null || !row.TryGetValue("course", out course) || !(course is string) || ((string)course).Length == 0 || ((string)course).Length > MaxText) return null;
                    var cells = row.TryGetValue("values", out values) ? Texts(values as IList) : null;
                    if (cells == null || cells.Count != heads.Count - 1) return null;
                    sheet.Rows.Add(new GradeRow { Course = (string)course, Values = cells });
                }
                return sheet;
            }
            catch (Exception) { return null; }
        }
        private static List<string> Texts(IList items)
        {
            if (items == null || items.Count > MaxColumns) return null;
            var texts = new List<string>();
            foreach (var item in items) { var text = item as string; if (text == null || text.Length > MaxText) return null; texts.Add(text); }
            return texts;
        }
    }
    internal sealed class GradeChange
    {
        public string Course = "";
        public List<string> Cells = new List<string>();
    }
    // The balloon's words: at most three courses, the rest counted; behind a PIN only that something is new.
    internal static class GradeNotice
    {
        internal const int Lines = 3;
        internal static string Title(IList<GradeChange> changes, string lang) { return Strings.Get(changes.Count > 1 ? "gradesNewMany" : "gradesNew", lang); }
        internal static string Body(IList<GradeChange> changes, string lang, bool locked)
        {
            if (locked) return Strings.Get("gradesLocked", lang);
            var lines = changes.Take(Lines).Select(c => c.Course + ": " + string.Join(" · ", c.Cells)).ToList();
            if (changes.Count > Lines) lines.Add(string.Format(Strings.Get("gradesMore", lang), changes.Count - Lines));
            var body = string.Join(Environment.NewLine, lines);
            // A balloon holds 255 characters.
            return body.Length > 250 ? body.Substring(0, 249) + "…" : body;
        }
        // How often the attestation page is read: a choice of these, in minutes.
        internal static readonly int[] Intervals = { 15, 30, 60, 120 };
        internal const int DefaultInterval = 30;
        internal static int Interval(int minutes) { return Intervals.Contains(minutes) ? minutes : DefaultInterval; }
    }
    internal static class GradeFile
    {
        internal const string Name = "grades.json";
        internal static GradeSheet Read(string root, string account)
        {
            try { var path = Path.Combine(root, Name); return File.Exists(path) ? GradeSheet.Load(File.ReadAllText(path, Encoding.UTF8), account) : null; }
            catch (Exception) { return null; }
        }
        internal static void Write(string root, GradeSheet sheet) { AtomicFile.Write(Path.Combine(root, Name), Encoding.UTF8.GetBytes(sheet.Serialize())); }
        internal static void Remove(string root) { File.Delete(Path.Combine(root, Name)); }
    }

    // A tolerant, bounded reading of HTML tables: rows of cells with their text, whether a cell is a heading and its column
    // span. No scripts, styles or comments are read as text; nested tables are tables of their own. The work stops at the
    // limits (the answer's size, tags, tables, rows a table, cells a row) instead of going as far as the page says.
    internal sealed class HtmlCell
    {
        public bool Heading; public int Span = 1; public string Text = "";
    }
    internal static class HtmlTables
    {
        internal const int MaxLength = 2000000, MaxTags = 60000, MaxTables = 64, MaxRows = 400, MaxCells = 64;
        // The whole reading of one page: the regex timeout bounds a single match only, and many unclosed «<!--» or «<script»
        // would each scan to the end of the page.
        internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
        private static readonly Regex Tag = new Regex(@"<!--.*?-->|<(script|style|textarea)\b[^>]*>.*?</\1\s*>|<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*)>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        private static readonly Regex ColSpan = new Regex(@"\bcolspan\s*=\s*[""']?(\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        internal static List<List<List<HtmlCell>>> Read(string html)
        {
            if (string.IsNullOrEmpty(html) || html.Length > MaxLength) return null;
            try { return Tables(html); }
            catch (RegexMatchTimeoutException) { return null; }
        }
        private static List<List<List<HtmlCell>>> Tables(string html)
        {
            var done = new List<List<List<HtmlCell>>>();
            var open = new Stack<List<List<HtmlCell>>>();
            HtmlCell cell = null; var text = new StringBuilder(); var tags = 0; var at = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Action closeCell = () => { if (cell != null) { cell.Text = GradeSheet.Fold(WebUtility.HtmlDecode(text.ToString())); cell = null; } text.Clear(); };
            for (var m = Tag.Match(html); m.Success; m = m.NextMatch())
            {
                if (++tags > MaxTags || clock.Elapsed > Budget) return null;
                if (cell != null && m.Index > at && text.Length < 4 * GradeSheet.MaxText) text.Append(html, at, Math.Min(m.Index - at, 4 * GradeSheet.MaxText));
                at = m.Index + m.Length;
                if (!m.Groups[3].Success) continue;
                var closing = m.Groups[2].Value == "/"; var name = m.Groups[3].Value.ToLowerInvariant();
                if (name == "br" && cell != null) { text.Append(' '); continue; }
                if (name == "table")
                {
                    closeCell();
                    if (!closing) { if (open.Count + done.Count >= MaxTables) return null; open.Push(new List<List<HtmlCell>>()); }
                    else if (open.Count > 0) done.Add(open.Pop());
                    continue;
                }
                if (open.Count == 0) continue;
                var table = open.Peek();
                if (name == "tr") { closeCell(); if (!closing) { if (table.Count >= MaxRows) return null; table.Add(new List<HtmlCell>()); } continue; }
                if (name == "td" || name == "th")
                {
                    closeCell();
                    if (closing) continue;
                    if (table.Count == 0) table.Add(new List<HtmlCell>());
                    var row = table[table.Count - 1];
                    if (row.Count >= MaxCells) return null;
                    var span = ColSpan.Match(m.Groups[4].Value); int count;
                    cell = new HtmlCell { Heading = name == "th", Span = span.Success && int.TryParse(span.Groups[1].Value, out count) && count > 1 ? Math.Min(count, MaxCells) : 1 };
                    row.Add(cell);
                }
            }
            closeCell();
            while (open.Count > 0) done.Add(open.Pop());
            return done;
        }
    }
}
