using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Univer.Desktop.UI;

namespace Univer.Desktop.Core
{
    internal enum ExamKind { Exam, Midterm }
    // One exam or midterm of the student's lists, at its own local wall-clock start.
    internal sealed class ExamItem
    {
        public ExamKind Kind { get; set; }
        public string Subject { get; set; }
        public DateTime Start { get; set; }
        public string Place { get; set; }
        // What an announcement is remembered by: the kind and the start, not the subject or the room, which the site words
        // in the language of the page (opening the exam page in another language must not repeat a reminder). Two exams of
        // one kind that start together are announced together anyway (ExamPlan.Due). Keys of the older form (with the subject
        // at the end) match no exam any more and are dropped on load, so a reminder already said may be said once more.
        internal string Key { get { return Start.ToString("s", CultureInfo.InvariantCulture) + "|" + Kind; } }
        internal bool SameAs(ExamItem other) { return other != null && other.Kind == Kind && other.Start == Start && other.Subject == Subject && other.Place == Place; }
    }
    // A reminder that is due: the evening before ("tomorrow") or the lead time before the start.
    internal sealed class ExamReminder
    {
        public ExamItem Item { get; set; }
        public bool Evening { get; set; }
        public DateTime At { get; set; }
        // What the plan remembers once this one is shown, so a restart does not say it again.
        public string Key { get { return (Evening ? "e|" : "l|") + Item.Key; } }
    }
    // The exams and midterms the student last saw on the site's exam and midterm schedule pages (exams.json), kept for the
    // reminders (MainForm.Exams.cs). A page replaces its own kind as a whole, so an exam that moved or went stops reminding;
    // the reminders already shown stay in `Announced` for as long as their exam is listed. The plan belongs to one login
    // (a short hash) and goes with every logout, Remove and account change, like the week copy.
    internal sealed class ExamPlan
    {
        internal const int Version = 1, MaxItems = 60, MaxText = 200, MaxRaw = 100000, MaxStored = 2 * MaxItems;
        public DateTime SavedAt { get; private set; }
        public string Account { get; private set; }
        public IList<ExamItem> Items { get; private set; }
        public ISet<string> Announced { get; private set; }
        private static readonly Regex Start = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$", RegexOptions.CultureInvariant);
        internal ExamPlan(string account, DateTime savedAt, IEnumerable<ExamItem> items, IEnumerable<string> announced)
        {
            Account = account ?? ""; SavedAt = savedAt;
            Items = items.OrderBy(i => i.Start).ThenBy(i => i.Subject, StringComparer.Ordinal).ToList();
            var keys = new HashSet<string>(Items.SelectMany(i => new[] { "e|" + i.Key, "l|" + i.Key }), StringComparer.Ordinal);
            Announced = new HashSet<string>((announced ?? new string[0]).Where(keys.Contains), StringComparer.Ordinal);
        }
        internal static ExamPlan Empty(string account, DateTime now) { return new ExamPlan(account, now, new ExamItem[0], null); }
        // The page's reading: {"v":1,"kind":"exam","items":[{"kind":"exam","subject","start":"2026-12-15T13:00","place"}]}.
        // Anything out of shape is null; items that already began, or are over a year away, are dropped (not an error).
        internal static bool Parse(string raw, DateTime now, out ExamKind kind, out List<ExamItem> items)
        {
            kind = ExamKind.Exam; items = null;
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxRaw) return false;
            Dictionary<string, object> json;
            try { json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>; }
            catch (Exception) { return false; }
            object version, kindValue, list;
            if (json == null || !json.TryGetValue("v", out version) || !(version is int) || (int)version != Version) return false;
            if (!json.TryGetValue("kind", out kindValue) || !KindOf(kindValue, out kind)) return false;
            var array = json.TryGetValue("items", out list) ? list as IList : null;
            if (array == null || array.Count > MaxItems) return false;
            var found = new List<ExamItem>();
            foreach (var raw1 in array)
            {
                var item = Item(raw1 as Dictionary<string, object>);
                if (item == null || item.Kind != kind) return false;
                if (item.Start > now && item.Start < now.AddDays(366)) found.Add(item);
            }
            items = found;
            return true;
        }
        // The same list with `kind`'s items replaced by `fresh`; items that have begun go, and so do reminders of exams that left.
        internal ExamPlan Replace(ExamKind kind, IEnumerable<ExamItem> fresh, DateTime now)
        {
            var others = Items.Where(i => i.Kind != kind && i.Start > now);
            return new ExamPlan(Account, now, others.Concat(fresh), Announced);
        }
        internal ExamPlan WithAnnounced(IEnumerable<string> keys) { return new ExamPlan(Account, SavedAt, Items, Announced.Concat(keys)); }
        internal bool SameAs(ExamPlan other)
        {
            return other != null && other.Account == Account && other.Items.Count == Items.Count && other.Announced.SetEquals(Announced) &&
                Items.Zip(other.Items, (a, b) => a.SameAs(b)).All(same => same);
        }
        // The reminders to say now, one group: what is due, soonest first; the evening ones of one kind together, the lead ones
        // that start together. The evening one is said from 19:00 the evening before until midnight (a computer opened at
        // 19:30 still hears of tomorrow's exam, once); the lead one from `lead` minutes before the start until it begins. An
        // exam that has begun is never announced.
        internal List<ExamReminder> Due(DateTime now, int lead)
        {
            var window = TimeSpan.FromMinutes(ClassReminders.Lead(lead));
            var due = new List<ExamReminder>();
            foreach (var item in Items.Where(i => i.Start > now))
            {
                var evening = new ExamReminder { Item = item, Evening = true, At = item.Start.Date.AddDays(-1).AddHours(19) };
                if (evening.At <= now && now < item.Start.Date && !Announced.Contains(evening.Key)) due.Add(evening);
                var soon = new ExamReminder { Item = item, Evening = false, At = item.Start - window };
                if (soon.At <= now && !Announced.Contains(soon.Key)) due.Add(soon);
            }
            var first = due.OrderBy(r => r.At).ThenBy(r => r.Item.Subject, StringComparer.Ordinal).FirstOrDefault();
            if (first == null) return due;
            return due.Where(r => r.Evening == first.Evening && r.Item.Kind == first.Item.Kind && (r.Evening || r.Item.Start == first.Item.Start))
                .OrderBy(r => r.Item.Start).ThenBy(r => r.Item.Subject, StringComparer.Ordinal).ToList();
        }
        private static bool KindOf(object value, out ExamKind kind)
        {
            kind = ExamKind.Exam;
            if ("exam".Equals(value)) return true;
            if (!"midterm".Equals(value)) return false;
            kind = ExamKind.Midterm; return true;
        }
        private static ExamItem Item(Dictionary<string, object> json)
        {
            object kindValue, startValue; ExamKind kind; string subject, place; DateTime start;
            if (json == null || !json.TryGetValue("kind", out kindValue) || !KindOf(kindValue, out kind)) return null;
            if (!Text(json, "subject", false, out subject) || subject.Length == 0 || !Text(json, "place", true, out place)) return null;
            if (!json.TryGetValue("start", out startValue) || !(startValue is string) || !Start.IsMatch((string)startValue)) return null;
            if (!DateTime.TryParseExact((string)startValue, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out start)) return null;
            return new ExamItem { Kind = kind, Subject = subject, Start = start, Place = place };
        }
        // Text the page gave: a string within bounds (absent is empty where allowed), control characters and runs of spaces
        // folded to one space.
        private static bool Text(Dictionary<string, object> json, string key, bool optional, out string value)
        {
            value = ""; object raw;
            if (!json.TryGetValue(key, out raw) || raw == null) return optional;
            if (!(raw is string)) return false;
            var folded = new StringBuilder(); var space = false;
            foreach (var ch in (string)raw)
            {
                if (char.IsControl(ch) || char.IsWhiteSpace(ch)) { space = folded.Length > 0; continue; }
                if (space) { folded.Append(' '); space = false; }
                folded.Append(ch);
            }
            value = folded.ToString();
            return value.Length <= MaxText;
        }
        internal string Serialize()
        {
            var items = Items.Select(i => new Dictionary<string, object> { { "kind", i.Kind == ExamKind.Exam ? "exam" : "midterm" }, { "subject", i.Subject }, { "start", i.Start.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) }, { "place", i.Place } }).ToList();
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "version", Version }, { "account", Account },
                { "saved", SavedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) }, { "items", items }, { "announced", Announced.OrderBy(k => k, StringComparer.Ordinal).ToList() } });
        }
        // The saved copy, checked again as strictly as a page's answer: a damaged file, another account's or version is none.
        internal static ExamPlan Load(string raw, string account)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > 200000) return null;
            try
            {
                var json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                object version, owner, saved, list, announced;
                if (json == null || !json.TryGetValue("version", out version) || !(version is int) || (int)version != Version) return null;
                if (!json.TryGetValue("account", out owner) || !(owner is string) || (string)owner != (account ?? "")) return null;
                DateTime at;
                if (!json.TryGetValue("saved", out saved) || !(saved is string) || !DateTime.TryParseExact((string)saved, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at)) return null;
                var array = json.TryGetValue("items", out list) ? list as IList : null;
                if (array == null || array.Count > MaxStored) return null;
                var items = new List<ExamItem>();
                foreach (var entry in array)
                {
                    var item = Item(entry as Dictionary<string, object>);
                    if (item == null) return null;
                    items.Add(item);
                }
                var keys = new List<string>();
                var seen = json.TryGetValue("announced", out announced) ? announced as IList : null;
                if (seen == null || seen.Count > 2 * MaxStored) return null;
                foreach (var key in seen) { var text = key as string; if (text == null || text.Length > 400) return null; keys.Add(text); }
                return new ExamPlan((string)owner, at, items, keys);
            }
            catch (Exception) { return null; }
        }
    }
    // The words of an exam balloon (the balloon's title stays short: Windows cuts it at 63 characters).
    internal static class ExamReminders
    {
        internal static string KindWord(ExamKind kind, string lang) { return Strings.Get(kind == ExamKind.Exam ? "examKindExam" : "examKindMidterm", lang); }
        // Behind the PIN only the kind and the time show, never which subject or where.
        internal static string Title(IList<ExamReminder> due, DateTime now, string lang, bool hidden)
        {
            var first = due[0]; var kind = KindWord(first.Item.Kind, lang);
            if (first.Evening) return string.Format(Strings.Get("examTomorrow", lang), kind);
            if (hidden) return string.Format(Strings.Get("examSoon", lang), kind);
            return string.Format(Strings.Get("examIn", lang), Math.Max(1, (int)Math.Ceiling((first.Item.Start - now).TotalMinutes)), kind);
        }
        internal static string Body(IList<ExamReminder> due, string lang, bool hidden)
        {
            var starts = string.Format(Strings.Get("examStarts", lang), due[0].Item.Start.ToString("HH:mm", CultureInfo.InvariantCulture));
            if (hidden) return char.ToUpperInvariant(starts[0]) + starts.Substring(1);
            var text = string.Join(Environment.NewLine, due.Take(3).Select(r =>
            {
                var parts = new List<string> { r.Item.Subject };
                if (r.Item.Place.Length > 0) parts.Add(string.Format(Strings.Get("reminderRoom", lang), r.Item.Place));
                parts.Add(string.Format(Strings.Get("examStarts", lang), r.Item.Start.ToString("HH:mm", CultureInfo.InvariantCulture)));
                return string.Join(" · ", parts);
            }));
            // Windows takes at most 255 characters for a balloon's text.
            return text.Length <= 255 ? text : text.Substring(0, 254) + "…";
        }
    }
    // One balloon when a class reminder and an exam reminder are due at the same time: a second balloon would replace the first
    // and its click target. The title is that of the more urgent one (the class unless an exam's lead reminder starts earlier),
    // the body lists the classes, then the exams. Behind the PIN no class name or subject is written, only the kinds and times.
    // A click opens the schedule. Windows cuts a balloon's title at 63 characters and its text at 255.
    internal static class CombinedReminder
    {
        internal const int TitleLimit = 63, BodyLimit = 255;
        internal static string Title(IList<ScheduleClass> classes, IList<ExamReminder> exams, DateTime now, string lang, bool hidden)
        {
            var examFirst = !exams[0].Evening && exams[0].Item.Start < classes[0].Start;
            var title = examFirst ? ExamReminders.Title(exams, now, lang, hidden) : ClassReminders.Title(classes, now, lang);
            return ClassReminders.Cut(title, TitleLimit);
        }
        internal static string Body(IList<ScheduleClass> classes, IList<ExamReminder> exams, string lang, bool hidden)
        {
            var lines = new List<string>();
            if (hidden)
            {
                lines.AddRange(classes.Select(c => c.Start).Distinct().Take(2).Select(t => Cap(string.Format(Strings.Get("reminderClassAt", lang), Time(t)))));
                lines.AddRange(exams.Take(2).Select(r => Cap(ExamReminders.KindWord(r.Item.Kind, lang)) + " · " + string.Format(Strings.Get("examStarts", lang), Time(r.Item.Start))));
            }
            else
            {
                lines.AddRange(classes.Take(2).Select(c =>
                {
                    var parts = new List<string> { c.Name };
                    if (c.Room.Length > 0) parts.Add(string.Format(Strings.Get("reminderRoom", lang), c.Room));
                    parts.Add(string.Format(Strings.Get("examStarts", lang), Time(c.Start)));
                    return string.Join(" · ", parts);
                }));
                lines.AddRange(exams.Take(2).Select(r =>
                {
                    var parts = new List<string> { r.Item.Subject };
                    if (r.Item.Place.Length > 0) parts.Add(string.Format(Strings.Get("reminderRoom", lang), r.Item.Place));
                    parts.Add(string.Format(Strings.Get("examStarts", lang), Time(r.Item.Start)));
                    var line = string.Join(" · ", parts);
                    return r.Evening ? string.Format(Strings.Get("examTomorrow", lang), ExamReminders.KindWord(r.Item.Kind, lang)) + ": " + line : line;
                }));
            }
            return ClassReminders.Cut(string.Join(Environment.NewLine, lines), BodyLimit);
        }
        private static string Time(DateTime at) { return at.ToString("HH:mm", CultureInfo.InvariantCulture); }
        private static string Cap(string text) { return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1); }
    }
    internal static class ExamFile
    {
        internal const string Name = "exams.json";
        internal static ExamPlan Read(string root, string account)
        {
            try { var path = Path.Combine(root, Name); return File.Exists(path) ? ExamPlan.Load(File.ReadAllText(path, Encoding.UTF8), account) : null; }
            catch (Exception) { return null; }
        }
        internal static void Write(string root, ExamPlan plan) { AtomicFile.Write(Path.Combine(root, Name), Encoding.UTF8.GetBytes(plan.Serialize())); }
        internal static void Remove(string root) { File.Delete(Path.Combine(root, Name)); }
    }
}
