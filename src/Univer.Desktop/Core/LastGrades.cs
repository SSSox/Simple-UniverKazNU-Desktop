using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // One mark of a course's total: "РК1", out of "100", the value "71". Max may be empty.
    internal sealed class LastGradeScore
    {
        public string Label { get; set; }
        public string Max { get; set; }
        public string Value { get; set; }
    }
    internal sealed class LastGradeCourse
    {
        public string Name { get; set; }
        public List<LastGradeScore> Scores { get; set; }
    }
    // The course totals the student last saw (last-grades.json), shown under the offline screen's week as «Последние оценки»
    // (UI/OfflineWeekView.cs). The home page's grades panel already reads them from the journal (overview.js), and the app asks
    // the shown home page for that reading (MainForm.LastGrades.cs, app → page), checks every field here and keeps it for this
    // login. Unlike grades.json (Core/GradeWatch.cs, the new-grade notifications) it is written whether or not the option is
    // on, and goes only with the session: every logout, Remove and account change (RotateSessionAsync, FinishSessionClear).
    internal sealed class LastGrades
    {
        internal const int Version = 1, MaxCourses = 60, MaxScores = 10, MaxText = 200, MaxMark = 40, MaxRaw = 400000;
        public DateTime SavedAt { get; private set; }
        public string Account { get; private set; }
        public IList<LastGradeCourse> Courses { get; private set; }
        internal LastGrades(string account, DateTime savedAt, IEnumerable<LastGradeCourse> courses)
        {
            Account = account ?? ""; SavedAt = savedAt; Courses = courses.ToList();
        }
        // The page's reading: {"v":1,"at":<epoch ms>,"courses":[{"name","scores":[{"label","max","value"}]}]}. Anything out of
        // shape is null, and so is a reading without a login mark or without one grade; scores with no value yet and courses
        // left with none are dropped (an empty value is not a grade).
        internal static LastGrades Parse(string raw, DateTime now, string account)
        {
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(raw) || raw.Length > MaxRaw) return null;
            Dictionary<string, object> json;
            try { json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>; }
            catch (Exception) { return null; }
            object version, list; double at;
            if (json == null || !json.TryGetValue("v", out version) || !(version is int) || (int)version != Version || !ScheduleWeek.Number(json, "at", out at)) return null;
            var read = ScheduleWeek.FromEpoch(at);
            if (read == null || read.Value > now.AddMinutes(1) || read.Value < now.AddDays(-15)) return null;
            var courses = ReadCourses(json.TryGetValue("courses", out list) ? list as IList : null, true);
            return courses == null || courses.Count == 0 ? null : new LastGrades(account, read.Value, courses);
        }
        // Null when the list is out of shape. `page`: a page's reading, whose empty values are dropped; the saved file holds
        // none, so an empty one there is damage.
        private static List<LastGradeCourse> ReadCourses(IList array, bool page)
        {
            if (array == null || array.Count > MaxCourses) return null;
            var courses = new List<LastGradeCourse>();
            foreach (var item in array)
            {
                var json = item as Dictionary<string, object>; string name; object list;
                var scores = json != null && json.TryGetValue("scores", out list) ? list as IList : null;
                if (json == null || !Text(json, "name", MaxText, out name) || name.Length == 0 || scores == null || scores.Count > MaxScores) return null;
                var kept = new List<LastGradeScore>();
                foreach (var entry in scores)
                {
                    var score = entry as Dictionary<string, object>; string label, max, value;
                    if (score == null || !Text(score, "label", MaxMark, out label) || !Text(score, "max", MaxMark, out max) || !Text(score, "value", MaxMark, out value)) return null;
                    if (value.Length == 0) { if (page) continue; return null; }
                    kept.Add(new LastGradeScore { Label = label, Max = max, Value = value });
                }
                if (kept.Count == 0) { if (page) continue; return null; }
                courses.Add(new LastGradeCourse { Name = name, Scores = kept });
            }
            return courses;
        }
        // "71/100", or just "71" when the page gave no maximum.
        internal static string Score(LastGradeScore score) { return score.Max.Length == 0 ? score.Value : score.Value + "/" + score.Max; }
        internal bool SameAs(LastGrades other)
        {
            return other != null && other.Account == Account && other.Courses.Count == Courses.Count &&
                Courses.Zip(other.Courses, (a, b) => a.Name == b.Name && a.Scores.Count == b.Scores.Count &&
                    a.Scores.Zip(b.Scores, (x, y) => x.Label == y.Label && x.Max == y.Max && x.Value == y.Value).All(same => same)).All(same => same);
        }
        // Text the page gave: a string within bounds (absent is empty), control characters and runs of spaces folded to one
        // space.
        private static bool Text(Dictionary<string, object> json, string key, int max, out string value)
        {
            value = ""; object raw;
            if (!json.TryGetValue(key, out raw) || raw == null) return true;
            if (!(raw is string) || ((string)raw).Length > max) return false;
            var folded = new StringBuilder(); var space = false;
            foreach (var ch in (string)raw)
            {
                if (char.IsControl(ch) || char.IsWhiteSpace(ch)) { space = folded.Length > 0; continue; }
                if (space) { folded.Append(' '); space = false; }
                folded.Append(ch);
            }
            value = folded.ToString();
            return true;
        }
        internal string Serialize()
        {
            var courses = Courses.Select(c => new Dictionary<string, object> { { "name", c.Name }, { "scores", c.Scores.Select(s => new Dictionary<string, object> { { "label", s.Label }, { "max", s.Max }, { "value", s.Value } }).ToList() } }).ToList();
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "version", Version }, { "account", Account },
                { "saved", SavedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) }, { "courses", courses } });
        }
        // The saved copy, checked again as strictly as a page's answer: a damaged file, another account's or version is none.
        internal static LastGrades Load(string raw, string account)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxRaw) return null;
            try
            {
                var json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                object version, owner, saved, list; DateTime at;
                if (json == null || !json.TryGetValue("version", out version) || !(version is int) || (int)version != Version) return null;
                if (!json.TryGetValue("account", out owner) || !(owner is string) || (string)owner != (account ?? "")) return null;
                if (!json.TryGetValue("saved", out saved) || !(saved is string) || !DateTime.TryParseExact((string)saved, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at)) return null;
                var courses = ReadCourses(json.TryGetValue("courses", out list) ? list as IList : null, false);
                return courses == null || courses.Count == 0 ? null : new LastGrades((string)owner, at, courses);
            }
            catch (Exception) { return null; }
        }
    }
    internal static class LastGradesFile
    {
        internal const string Name = "last-grades.json";
        internal static LastGrades Read(string root, string account)
        {
            try { var path = Path.Combine(root, Name); return File.Exists(path) ? LastGrades.Load(File.ReadAllText(path, Encoding.UTF8), account) : null; }
            catch (Exception) { return null; }
        }
        internal static void Write(string root, LastGrades grades) { AtomicFile.Write(Path.Combine(root, Name), Encoding.UTF8.GetBytes(grades.Serialize())); }
        internal static void Remove(string root) { File.Delete(Path.Combine(root, Name)); }
    }
}
