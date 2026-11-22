using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // One class of the kept week, at its own day and time (local).
    internal sealed class ScheduleClass
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Room { get; set; }
        public string Teacher { get; set; }
    }
    // The app's own copy of the current week's classes (schedule.json): read from the schedule page or the home page's
    // reading of it (overview.js week()), checked here field by field, and kept for the reminders and for the schedule
    // shown when the site cannot be reached. It belongs to one login (a short hash) and goes with every logout, Remove and
    // account change (MainForm.RotateSessionAsync, Storage.FinishSessionClear).
    internal sealed class ScheduleWeek
    {
        internal const int Version = 1, MaxClasses = 120, MaxText = 400;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public DateTime SavedAt { get; set; }
        public string Account { get; set; }
        public List<ScheduleClass> Classes { get; set; }
        // What the page gave, or null when any part of it is not a current week of classes.
        internal static ScheduleWeek Parse(string raw, DateTime now, string account)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > 200000) return null;
            Dictionary<string, object> json;
            try { json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>; }
            catch (Exception) { return null; }
            var week = FromJson(json, now, account);
            if (week == null || now < week.Start || now >= week.End) return null;
            return week;
        }
        // One week of the page's reading, by the same field rules; any week, the caller decides which ones it takes
        // (the kept copy only the current one, an export the shown week and its neighbour). A week the site starts or ends
        // midweek (the semester's first days) has columns with no date of its own; their classes are dropped, or, when
        // `outside` is given, put there at that weekday of the week's Monday-to-Sunday (Monday), outside the week's dates.
        internal static ScheduleWeek FromJson(Dictionary<string, object> json, DateTime savedAt, string account, List<ScheduleClass> outside = null)
        {
            double startMs, endMs;
            if (json == null || !Number(json, "start", out startMs) || !Number(json, "end", out endMs)) return null;
            var start = FromEpoch(startMs); var end = FromEpoch(endMs);
            if (start == null || end == null || end <= start || end.Value - start.Value > TimeSpan.FromDays(7).Add(TimeSpan.FromHours(2))) return null;
            object daysValue; var days = json.TryGetValue("days", out daysValue) ? daysValue as IList : null;
            if (days == null || days.Count > 7) return null;
            var classes = new List<ScheduleClass>();
            for (var column = 0; column < days.Count; column++)
            {
                var lessons = days[column] as IList;
                if (lessons == null || lessons.Count > 40) return null;
                // Column 1 is Monday: the day of the site's week that falls on that weekday (none when the week starts later).
                var weekday = (DayOfWeek)((column + 1) % 7);
                DateTime? date = null;
                for (var d = start.Value.Date; d < end.Value; d = d.AddDays(1)) if (d >= start.Value.Date && d.DayOfWeek == weekday) { date = d; break; }
                foreach (var item in lessons)
                {
                    var lesson = item as Dictionary<string, object>;
                    double from, to;
                    if (lesson == null || !Number(lesson, "start", out from) || !Number(lesson, "end", out to)) return null;
                    if (from != Math.Floor(from) || to != Math.Floor(to) || from < 0 || to > 1440 || to <= from) return null;
                    string name, type, room, teacher;
                    if (!Text(lesson, "name", out name) || name.Length == 0 || !Text(lesson, "type", out type) || !Text(lesson, "room", out room) || !Text(lesson, "teacher", out teacher)) return null;
                    if (date == null && outside == null) continue;
                    var day = date ?? Monday(start.Value).AddDays(column);
                    (date == null ? outside : classes).Add(new ScheduleClass { Start = day.AddMinutes(from), End = day.AddMinutes(to), Name = name, Type = type, Room = room, Teacher = teacher });
                    if (classes.Count + (outside == null ? 0 : outside.Count) > MaxClasses) return null;
                }
            }
            return new ScheduleWeek { Start = start.Value, End = end.Value, SavedAt = savedAt, Account = account ?? "", Classes = classes.OrderBy(c => c.Start).ThenBy(c => c.Name, StringComparer.Ordinal).ToList() };
        }
        // The Monday the week's columns count from: the Monday on or before its first day, the next day for a week the site
        // starts on a Sunday (its Monday column is that next day, as above).
        internal static DateTime Monday(DateTime start)
        {
            var date = start.Date;
            return date.DayOfWeek == DayOfWeek.Sunday ? date.AddDays(1) : date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        }
        // The same week and the same classes: nothing to write.
        internal bool SameAs(ScheduleWeek other)
        {
            return other != null && other.Start == Start && other.End == End && other.Account == Account && other.Classes.Count == Classes.Count &&
                Classes.Zip(other.Classes, (a, b) => a.Start == b.Start && a.End == b.End && a.Name == b.Name && a.Type == b.Type && a.Room == b.Room && a.Teacher == b.Teacher).All(same => same);
        }
        // A short, one-way mark of the login, so a copy is never shown to another account of this Windows user.
        // Asked every second with the same login: the last answer is kept as one immutable pair.
        private sealed class Mark { public string Login, Value; }
        private static volatile Mark lastMark;
        internal static string AccountMark(string login)
        {
            if (string.IsNullOrWhiteSpace(login)) return "";
            var last = lastMark;
            if (last != null && string.Equals(last.Login, login, StringComparison.Ordinal)) return last.Value;
            var value = ComputeMark(login);
            lastMark = new Mark { Login = login, Value = value };
            return value;
        }
        private static string ComputeMark(string login)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(login.Trim().ToLowerInvariant()));
                return string.Concat(hash.Take(8).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }
        internal string Serialize()
        {
            var classes = Classes.Select(c => new Dictionary<string, object> { { "start", Stamp(c.Start) }, { "end", Stamp(c.End) }, { "name", c.Name }, { "type", c.Type }, { "room", c.Room }, { "teacher", c.Teacher } }).ToList();
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "version", Version }, { "account", Account }, { "saved", Stamp(SavedAt) }, { "start", Stamp(Start) }, { "end", Stamp(End) }, { "classes", classes } });
        }
        // The saved copy, checked again as strictly as a page's answer: a damaged file, another account's or another version
        // is not a week.
        internal static ScheduleWeek Load(string raw, string account)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > 400000) return null;
            try
            {
                var json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                object version, owner, list; string start, end, saved;
                if (json == null || !json.TryGetValue("version", out version) || !(version is int) || (int)version != Version) return null;
                if (!json.TryGetValue("account", out owner) || !(owner is string) || (string)owner != (account ?? "")) return null;
                if (!Text(json, "start", out start) || !Text(json, "end", out end) || !Text(json, "saved", out saved)) return null;
                DateTime from, to, at;
                if (!Parse(start, out from) || !Parse(end, out to) || !Parse(saved, out at) || to <= from || to - from > TimeSpan.FromDays(7).Add(TimeSpan.FromHours(2))) return null;
                var items = json.TryGetValue("classes", out list) ? list as IList : null;
                if (items == null || items.Count > MaxClasses) return null;
                var classes = new List<ScheduleClass>();
                foreach (var item in items)
                {
                    var c = item as Dictionary<string, object>; string s, e, name, type, room, teacher; DateTime cs, ce;
                    if (c == null || !Text(c, "start", out s) || !Text(c, "end", out e) || !Parse(s, out cs) || !Parse(e, out ce) || ce <= cs || cs < from.Date || ce > to.AddDays(1)) return null;
                    if (!Text(c, "name", out name) || name.Length == 0 || !Text(c, "type", out type) || !Text(c, "room", out room) || !Text(c, "teacher", out teacher)) return null;
                    classes.Add(new ScheduleClass { Start = cs, End = ce, Name = name, Type = type, Room = room, Teacher = teacher });
                }
                return new ScheduleWeek { Start = from, End = to, SavedAt = at, Account = (string)owner, Classes = classes.OrderBy(c => c.Start).ToList() };
            }
            catch (Exception) { return null; }
        }
        private static string Stamp(DateTime value) { return value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture); }
        private static bool Parse(string value, out DateTime result)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
        }
        internal static bool Number(Dictionary<string, object> json, string key, out double value)
        {
            value = 0; object raw;
            if (!json.TryGetValue(key, out raw) || raw == null || raw is string || raw is bool) return false;
            try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); } catch (Exception) { return false; }
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
        // Text the page gave: a string within bounds, its control characters and runs of spaces folded to one space.
        private static bool Text(Dictionary<string, object> json, string key, out string value)
        {
            value = ""; object raw;
            if (!json.TryGetValue(key, out raw) || !(raw is string) || ((string)raw).Length > MaxText) return false;
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
        internal static DateTime? FromEpoch(double ms)
        {
            if (ms < 0 || ms > 253402300799000d) return null;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
        }
    }
    internal static class ScheduleFile
    {
        internal const string Name = "schedule.json";
        internal static ScheduleWeek Read(string root, string account)
        {
            try { var path = Path.Combine(root, Name); return File.Exists(path) ? ScheduleWeek.Load(File.ReadAllText(path, Encoding.UTF8), account) : null; }
            catch (Exception) { return null; }
        }
        internal static void Write(string root, ScheduleWeek week) { AtomicFile.Write(Path.Combine(root, Name), Encoding.UTF8.GetBytes(week.Serialize())); }
        internal static void Remove(string root) { File.Delete(Path.Combine(root, Name)); }
    }
}
