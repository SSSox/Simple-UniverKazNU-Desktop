using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Univer.Desktop.UI;

namespace Univer.Desktop.Core
{
    // The schedule page's two export buttons (univer-layout.js, overview.js): the page's signal and what it asks for.
    internal static class ScheduleExport
    {
        public const string MessagePrefix = "univer-schedule:", Calendar = "ics", Picture = "png";
        public static string Kind(string value) { return value == Calendar || value == Picture ? value : null; }
    }
    // One week of an export: the site's week number (Labelled when it came from the option's text, not its place in the
    // list) and its classes. Outside holds the classes of weekdays the site's week does not cover (a week that starts
    // midweek), dated in that week's Monday-to-Sunday: the calendar repeats them from two weeks on, the picture leaves them out.
    internal sealed class ExportWeek
    {
        public int Number; public bool Labelled; public ScheduleWeek Week; public List<ScheduleClass> Outside = new List<ScheduleClass>();
        public bool Odd { get { return Number % 2 == 1; } }
    }
    // The schedule export (the schedule page's «В календарь» and «Картинкой»): the week the page showed and its neighbour in
    // #weekSelect, both read on the student's press (overview.js). The site alternates odd and even weeks, so the calendar
    // repeats each of the two every second week until the semester's last week. A week the page could not read is named in
    // Missing and its weeks stay empty: no other week stands in for it. Nothing of it is kept.
    internal sealed class SchedulePair
    {
        internal const int MaxWeek = 40, MaxLength = 400000;
        public string Term = "";
        // The day after the last week #weekSelect offers.
        public DateTime Until;
        public List<ExportWeek> Weeks = new List<ExportWeek>();
        public List<int> Missing = new List<int>();
        // Two weeks of the site's cycle, read or not: each repeats every second week.
        public bool Alternates { get { return Weeks.Count + Missing.Count == 2; } }
        public IEnumerable<int> Numbers { get { return Weeks.Select(w => w.Number).Concat(Missing).OrderBy(n => n); } }

        // The page's reading, checked field by field as strictly as the kept copy; null when any part is wrong.
        internal static SchedulePair Parse(string raw, DateTime now)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength) return null;
            Dictionary<string, object> json;
            try { json = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>; }
            catch (Exception) { return null; }
            object term, weeksValue, missingValue; double untilMs;
            if (json == null || !json.TryGetValue("term", out term) || !(term is string) || !Regex.IsMatch((string)term, @"^\d{4}/[12]$")) return null;
            if (!ScheduleWeek.Number(json, "until", out untilMs)) return null;
            var until = ScheduleWeek.FromEpoch(untilMs);
            var weeks = json.TryGetValue("weeks", out weeksValue) ? weeksValue as IList : null;
            var missing = json.TryGetValue("missing", out missingValue) ? missingValue as IList : null;
            if (until == null || weeks == null || missing == null || weeks.Count < 1 || weeks.Count > 2 || missing.Count > 1 || weeks.Count + missing.Count > 2) return null;
            var pair = new SchedulePair { Term = (string)term, Until = until.Value };
            foreach (var item in weeks)
            {
                var entry = item as Dictionary<string, object>; double number; object labelled;
                if (entry == null || !ScheduleWeek.Number(entry, "number", out number) || !WeekNumber(number) || !entry.TryGetValue("labelled", out labelled) || !(labelled is bool)) return null;
                var outside = new List<ScheduleClass>();
                var week = ScheduleWeek.FromJson(entry, now, "", outside);
                if (week == null) return null;
                pair.Weeks.Add(new ExportWeek { Number = (int)number, Labelled = (bool)labelled, Week = week, Outside = outside });
            }
            foreach (var item in missing)
            {
                if (item == null || item is string || item is bool) return null;
                double number;
                try { number = Convert.ToDouble(item, CultureInfo.InvariantCulture); } catch (Exception) { return null; }
                if (!WeekNumber(number)) return null;
                pair.Missing.Add((int)number);
            }
            var numbers = pair.Numbers.ToList();
            if (numbers.Distinct().Count() != numbers.Count) return null;
            // Two read weeks are the site's two neighbours: their Mondays one week apart (the semester's first week may start
            // midweek; dates, so the clock's DST does not count).
            if (pair.Weeks.Count == 2 && Math.Abs((ScheduleWeek.Monday(pair.Weeks[1].Week.Start) - ScheduleWeek.Monday(pair.Weeks[0].Week.Start)).TotalDays) != 7) return null;
            var first = pair.Weeks.Min(w => w.Week.Start); var last = pair.Weeks.Max(w => w.Week.End);
            if (pair.Until < last || pair.Until > first.Date.AddDays(7 * MaxWeek)) return null;
            return pair;
        }
        private static bool WeekNumber(double value) { return value == Math.Floor(value) && value >= 1 && value <= MaxWeek; }

        // «Неделя 7, нечётная» when the site numbered it; otherwise its dates.
        internal static string Heading(ExportWeek week, string lang)
        {
            if (!week.Labelled) return OfflineWeek.Range(week.Week, lang);
            return string.Format(Strings.Get("exportWeekHeading", lang), week.Number, Strings.Get(week.Odd ? "exportOdd" : "exportEven", lang));
        }
        // A week that could not be read: its number and parity only when the site numbers its weeks (the read week says so).
        internal string MissingHeading(int number, string lang)
        {
            if (!Weeks.All(w => w.Labelled)) return Strings.Get("exportOtherWeek", lang);
            return string.Format(Strings.Get("exportWeekHeading", lang), number, Strings.Get(number % 2 == 1 ? "exportOdd" : "exportEven", lang));
        }
        // «Расписание, недели 7–8.ics»: SavedFile adds « (1)» when it is already there.
        internal static string FileName(SchedulePair pair, string lang, string extension)
        {
            var numbers = pair.Numbers.ToList();
            var span = numbers.Count == 2 ? numbers[0].ToString(CultureInfo.InvariantCulture) + "–" + numbers[1].ToString(CultureInfo.InvariantCulture) : numbers[0].ToString(CultureInfo.InvariantCulture);
            return string.Format(Strings.Get(numbers.Count == 2 ? "scheduleFileWeeks" : "scheduleFileWeek", lang), span) + extension;
        }
    }

    // The calendar file (RFC 5545). Times are the site's wall clock, written as floating local times: no time zone is
    // guessed from this computer's settings, and a calendar shows them as the site does. Each class of a week repeats every
    // second week (INTERVAL=2) until the semester ends, so the odd and the even week never share a date. Its UID is made
    // of what the class is (term, parity, weekday, times, name, kind, room), never of the login or the time of export, so
    // an export made again updates the same events instead of adding copies.
    internal static class ScheduleCalendar
    {
        internal const int LineOctets = 75;
        internal static string Build(SchedulePair pair, string lang, DateTime stamp)
        {
            var lines = new List<string>
            {
                "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Simple Univer//Schedule//" + lang.ToUpperInvariant(), "CALSCALE:GREGORIAN", "METHOD:PUBLISH",
                "X-WR-CALNAME:" + Escape(Strings.Get("scheduleCalendarName", lang))
            };
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            var dtstamp = stamp.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            foreach (var week in pair.Weeks)
                foreach (var lesson in Lessons(pair, week))
                {
                    var key = string.Join("|", pair.Term, week.Odd ? "1" : "0", ((int)lesson.Start.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                        lesson.Start.ToString("HHmm", CultureInfo.InvariantCulture), lesson.End.ToString("HHmm", CultureInfo.InvariantCulture), lesson.Name, lesson.Type ?? "", lesson.Room ?? "");
                    int seen; used.TryGetValue(key, out seen); used[key] = seen + 1;
                    lines.Add("BEGIN:VEVENT");
                    lines.Add("UID:" + Uid(key + "|" + seen.ToString(CultureInfo.InvariantCulture)));
                    lines.Add("DTSTAMP:" + dtstamp);
                    lines.Add("DTSTART:" + Floating(lesson.Start));
                    lines.Add("DTEND:" + Floating(lesson.End));
                    // The last start the rule may give: the last moment before the semester's end.
                    if (pair.Alternates && lesson.Start.AddDays(14) < pair.Until)
                        lines.Add("RRULE:FREQ=WEEKLY;INTERVAL=2;UNTIL=" + Floating(pair.Until.AddSeconds(-1)));
                    lines.Add("SUMMARY:" + Escape(string.IsNullOrEmpty(lesson.Type) ? lesson.Name : lesson.Name + " (" + lesson.Type + ")"));
                    if (!string.IsNullOrEmpty(lesson.Room)) lines.Add("LOCATION:" + Escape(lesson.Room));
                    if (!string.IsNullOrEmpty(lesson.Teacher)) lines.Add("DESCRIPTION:" + Escape(lesson.Teacher));
                    lines.Add("TRANSP:OPAQUE");
                    lines.Add("END:VEVENT");
                }
            lines.Add("END:VCALENDAR");
            var text = new StringBuilder();
            foreach (var line in lines) foreach (var part in Fold(line)) text.Append(part).Append("\r\n");
            return text.ToString();
        }
        // A week's classes, and those of weekdays it does not cover two weeks on (the first time their week comes back),
        // while that is still within the semester; with no second week in the cycle nothing comes back.
        private static IEnumerable<ScheduleClass> Lessons(SchedulePair pair, ExportWeek week)
        {
            var later = pair.Alternates
                ? week.Outside.Select(c => new ScheduleClass { Start = c.Start.AddDays(14), End = c.End.AddDays(14), Name = c.Name, Type = c.Type, Room = c.Room, Teacher = c.Teacher }).Where(c => c.Start < pair.Until)
                : Enumerable.Empty<ScheduleClass>();
            return week.Week.Classes.Concat(later).OrderBy(c => c.Start).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        }
        internal static string Floating(DateTime value) { return value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture); }
        // TEXT values: backslash, semicolon, comma and line breaks are escaped.
        internal static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
        }
        // Lines longer than 75 octets go on in lines that start with a space; a character is never split, a surrogate
        // pair neither.
        internal static IEnumerable<string> Fold(string line)
        {
            var parts = new List<string>(); var current = new StringBuilder(); var octets = 0; var limit = LineOctets;
            for (var i = 0; i < line.Length; i++)
            {
                var unit = char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]) ? line.Substring(i++, 2) : line[i].ToString();
                var size = Encoding.UTF8.GetByteCount(unit);
                if (octets + size > limit)
                {
                    parts.Add(current.ToString()); current.Clear().Append(' '); octets = 1; limit = LineOctets;
                }
                current.Append(unit); octets += size;
            }
            parts.Add(current.ToString());
            return parts;
        }
        private static string Uid(string key)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(key)).Take(16).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))) + "@simple-univer";
        }
    }
}
