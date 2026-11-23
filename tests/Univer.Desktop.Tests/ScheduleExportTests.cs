using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ScheduleExportTests
    {
        private static readonly DateTime Monday3 = new DateTime(2026, 11, 2), Monday4 = new DateTime(2026, 11, 9);
        private static long Ms(DateTime local) { return new DateTimeOffset(local).ToUnixTimeMilliseconds(); }
        private static Dictionary<string, object> Lesson(int start, int end, string name, string type = "Лекция", string room = "305", string teacher = "Синтетический П. П.")
        { return new Dictionary<string, object> { { "start", start }, { "end", end }, { "name", name }, { "type", type }, { "room", room }, { "teacher", teacher } }; }
        private static Dictionary<string, object> Week(int number, DateTime monday, params object[] days)
        {
            return new Dictionary<string, object> { { "number", number }, { "labelled", true }, { "start", Ms(monday) }, { "end", Ms(monday.AddDays(7)) }, { "days", days } };
        }
        private static string Json(object term, DateTime until, object[] weeks, object[] missing)
        {
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "term", term }, { "until", Ms(until) }, { "weeks", weeks }, { "missing", missing } });
        }
        private static readonly DateTime SemesterEnd = new DateTime(2026, 12, 28);
        private static object[] OddWeek() { return new object[] { new object[] { Lesson(540, 590, "Политология") }, new object[0], new object[] { Lesson(600, 650, "Философия", "Семинар") } }; }
        private static object[] EvenWeek() { return new object[] { new object[] { Lesson(540, 590, "Политология") }, new object[] { Lesson(660, 710, "Социология", "Семинар") } }; }
        private static string PairJson() { return Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3, OddWeek()), Week(4, Monday4, EvenWeek()) }, new object[0]); }
        private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0);

        [Fact]
        public void APairOfNeighbourWeeksIsRead()
        {
            var pair = SchedulePair.Parse(PairJson(), Now);
            Assert.NotNull(pair);
            Assert.Equal("2026/1", pair.Term);
            Assert.Equal(new[] { 3, 4 }, pair.Weeks.Select(w => w.Number));
            Assert.True(pair.Alternates);
            Assert.Equal(SemesterEnd, pair.Until);
            Assert.Equal(new DateTime(2026, 11, 2, 9, 0, 0), pair.Weeks[0].Week.Classes[0].Start);
            // Any week is taken, not only the current one.
            Assert.NotNull(SchedulePair.Parse(PairJson(), new DateTime(2027, 3, 1)));
        }

        public static IEnumerable<object[]> Broken()
        {
            yield return new object[] { "not seven days apart", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3, OddWeek()), Week(5, Monday4.AddDays(7), EvenWeek()) }, new object[0]) };
            yield return new object[] { "three weeks", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3), Week(4, Monday4), Week(5, Monday4.AddDays(7)) }, new object[0]) };
            yield return new object[] { "two weeks and a missing one", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3), Week(4, Monday4) }, new object[] { 5 }) };
            yield return new object[] { "two missing", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3) }, new object[] { 2, 4 }) };
            yield return new object[] { "no week", Json("2026/1", SemesterEnd, new object[0], new object[] { 4 }) };
            yield return new object[] { "bad term", Json("2026/3", SemesterEnd, new object[] { Week(3, Monday3) }, new object[0]) };
            yield return new object[] { "term not text", Json(2026, SemesterEnd, new object[] { Week(3, Monday3) }, new object[0]) };
            yield return new object[] { "until before the last week ends", Json("2026/1", Monday4.AddDays(3), new object[] { Week(3, Monday3), Week(4, Monday4) }, new object[0]) };
            yield return new object[] { "until past forty weeks", Json("2026/1", Monday3.AddDays(7 * 41), new object[] { Week(3, Monday3) }, new object[0]) };
            yield return new object[] { "the same number twice", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3), Week(3, Monday4) }, new object[0]) };
            yield return new object[] { "a missing number equal to a read one", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3) }, new object[] { 3 }) };
            yield return new object[] { "week zero", Json("2026/1", SemesterEnd, new object[] { Week(0, Monday3) }, new object[0]) };
            yield return new object[] { "week 41", Json("2026/1", SemesterEnd, new object[] { Week(41, Monday3) }, new object[0]) };
            yield return new object[] { "a missing number as text", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3) }, new object[] { "4" }) };
            yield return new object[] { "a broken class", Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3, new object[] { new object[] { Lesson(600, 590, "Наоборот") } }) }, new object[0]) };
            yield return new object[] { "not JSON", "{weeks:" };
            // overview.js sends this when the shown week cannot be read, so the press ends in the failure status.
            yield return new object[] { "the page's failure answer", "{\"failed\":true}" };
        }
        [Theory]
        [MemberData(nameof(Broken))]
        public void AWrongReadingIsRefusedWhole(string why, string json)
        { Assert.True(SchedulePair.Parse(json, Now) == null, why); }

        [Fact]
        public void LabelledMustBeABoolean()
        {
            var week = Week(3, Monday3); week["labelled"] = "true";
            Assert.Null(SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { week }, new object[0]), Now));
        }

        private static List<string> Unfold(string text)
        {
            Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
            Assert.DoesNotContain("\r", text.Replace("\r\n", ""));
            return Regex.Split(text.Replace("\r\n ", ""), "\r\n").Where(l => l.Length > 0).ToList();
        }
        private static List<Dictionary<string, string>> Events(string text)
        {
            var events = new List<Dictionary<string, string>>(); Dictionary<string, string> current = null;
            foreach (var line in Unfold(text))
            {
                if (line == "BEGIN:VEVENT") { current = new Dictionary<string, string>(); continue; }
                if (line == "END:VEVENT") { events.Add(current); current = null; continue; }
                if (current == null) continue;
                var colon = line.IndexOf(':'); current[line.Substring(0, colon)] = line.Substring(colon + 1);
            }
            return events;
        }
        private static DateTime Floating(string value) { return DateTime.ParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture); }

        [Fact]
        public void TheCalendarRepeatsEachWeekEverySecondWeekAndTheParitiesNeverShareADate()
        {
            var pair = SchedulePair.Parse(PairJson(), Now);
            var text = ScheduleCalendar.Build(pair, "ru", new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc));
            Assert.StartsWith("BEGIN:VCALENDAR\r\n", text); Assert.EndsWith("END:VCALENDAR\r\n", text);
            var events = Events(text);
            Assert.Equal(4, events.Count);
            var dates = new Dictionary<DateTime, string>();
            foreach (var e in events)
            {
                // The site's wall clock: floating times, no zone.
                Assert.Matches(@"^\d{8}T\d{6}$", e["DTSTART"]); Assert.Matches(@"^\d{8}T\d{6}$", e["DTEND"]);
                Assert.Equal("FREQ=WEEKLY;INTERVAL=2;UNTIL=20261227T235959", e["RRULE"]);
                var start = Floating(e["DTSTART"]);
                var parity = start < Monday4 ? "odd" : "even";
                for (var at = start; at <= new DateTime(2026, 12, 27, 23, 59, 59); at = at.AddDays(14))
                {
                    string other;
                    Assert.False(dates.TryGetValue(at.Date, out other) && other != parity, "both weeks on " + at.ToString("d", CultureInfo.InvariantCulture));
                    dates[at.Date] = parity;
                }
            }
            Assert.Equal("Политология (Лекция)", events[0]["SUMMARY"]);
            Assert.Equal("305", events[0]["LOCATION"]);
            Assert.Equal("Синтетический П. П.", events[0]["DESCRIPTION"]);
            Assert.Equal("20261104T120000Z", events[0]["DTSTAMP"]);
        }

        [Fact]
        public void TheSameClassesGetTheSameUidsEveryTimeAndDifferentClassesDifferentOnes()
        {
            var pair = SchedulePair.Parse(PairJson(), Now);
            var first = Events(ScheduleCalendar.Build(pair, "ru", DateTime.UtcNow)).Select(e => e["UID"]).ToList();
            var again = Events(ScheduleCalendar.Build(SchedulePair.Parse(PairJson(), Now.AddDays(3)), "en", DateTime.UtcNow.AddHours(5))).Select(e => e["UID"]).ToList();
            Assert.Equal(first, again);
            Assert.Equal(first.Count, first.Distinct().Count());
            Assert.All(first, uid => Assert.Matches("^[0-9a-f]{32}@simple-univer$", uid));
            // Politology at 9:00 on Monday of the odd and of the even week: two series, not one.
            Assert.NotEqual(first[0], first[2]);
        }

        [Fact]
        public void TwoEqualClassesAtTheSameTimeAreTwoEvents()
        {
            var week = Week(3, Monday3, new object[] { new object[] { Lesson(540, 590, "Политология"), Lesson(540, 590, "Политология") } });
            var pair = SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { week }, new object[] { 4 }), Now);
            var uids = Events(ScheduleCalendar.Build(pair, "ru", DateTime.UtcNow)).Select(e => e["UID"]).ToList();
            Assert.Equal(2, uids.Distinct().Count());
        }

        [Fact]
        public void AnUnreadWeekAddsNothingAndTheReadOneStillSkipsItsWeeks()
        {
            var pair = SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { Week(3, Monday3, OddWeek()) }, new object[] { 4 }), Now);
            Assert.NotNull(pair); Assert.True(pair.Alternates);
            var events = Events(ScheduleCalendar.Build(pair, "ru", DateTime.UtcNow));
            Assert.Equal(2, events.Count);
            Assert.All(events, e => Assert.True(Floating(e["DTSTART"]) < Monday4));
            Assert.All(events, e => Assert.Contains("INTERVAL=2", e["RRULE"]));
            Assert.Equal("Неделя 4, чётная", pair.MissingHeading(4, "ru"));
        }

        [Fact]
        public void ASingleWeekWithoutANeighbourIsNotRepeated()
        {
            var pair = SchedulePair.Parse(Json("2026/1", Monday4, new object[] { Week(3, Monday3, OddWeek()) }, new object[0]), Now);
            Assert.False(pair.Alternates);
            Assert.All(Events(ScheduleCalendar.Build(pair, "ru", DateTime.UtcNow)), e => Assert.False(e.ContainsKey("RRULE")));
        }

        [Fact]
        public void AClassTooCloseToTheEndIsNotGivenARule()
        {
            // The even week is the last one: a rule would give it no second date.
            var pair = SchedulePair.Parse(Json("2026/1", Monday4.AddDays(7), new object[] { Week(3, Monday3, OddWeek()), Week(4, Monday4, EvenWeek()) }, new object[0]), Now);
            Assert.All(Events(ScheduleCalendar.Build(pair, "ru", DateTime.UtcNow)), e => Assert.False(e.ContainsKey("RRULE")));
        }

        // The semester's first week, Wednesday 2 to Sunday 6 September: its Monday and Tuesday columns hold the classes of
        // weekdays the week does not cover.
        private static Dictionary<string, object> FirstWeek()
        {
            var week = Week(1, new DateTime(2026, 9, 2), new object[] { Lesson(540, 590, "Политология") }, new object[] { Lesson(600, 650, "Философия", "Семинар") }, new object[] { Lesson(660, 710, "История") });
            week["end"] = Ms(new DateTime(2026, 9, 7)); return week;
        }

        [Fact]
        public void AFirstWeekThatStartsMidweekPairsWithTheNextMondayWeekAndItsEarlierWeekdaysStartTwoWeeksOn()
        {
            var pair = SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { FirstWeek(), Week(2, new DateTime(2026, 9, 7), new object[] { new object[] { Lesson(540, 590, "Социология") } }) }, new object[0]), Now);
            Assert.NotNull(pair);
            // The picture shows the week's own days only.
            Assert.Equal(new[] { new DateTime(2026, 9, 2, 11, 0, 0) }, pair.Weeks[0].Week.Classes.Select(c => c.Start));
            Assert.Equal(new[] { new DateTime(2026, 8, 31, 9, 0, 0), new DateTime(2026, 9, 1, 10, 0, 0) }, pair.Weeks[0].Outside.Select(c => c.Start));
            var events = Events(ScheduleCalendar.Build(pair, "ru", DateTime.UtcNow));
            var starts = events.ToDictionary(e => e["SUMMARY"], e => Floating(e["DTSTART"]));
            Assert.Equal(new DateTime(2026, 9, 2, 11, 0, 0), starts["История (Лекция)"]);
            Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0), starts["Социология (Лекция)"]);
            // Monday and Tuesday of week 1's kind come back with week 3, never before the semester.
            Assert.Equal(new DateTime(2026, 9, 14, 9, 0, 0), starts["Политология (Лекция)"]);
            Assert.Equal(new DateTime(2026, 9, 15, 10, 0, 0), starts["Философия (Семинар)"]);
            Assert.All(events, e => Assert.Equal("FREQ=WEEKLY;INTERVAL=2;UNTIL=20261227T235959", e["RRULE"]));
        }

        [Fact]
        public void WeekdaysAWeekDoesNotCoverAreLeftOutWithoutASecondWeekOrPastTheEnd()
        {
            var lone = SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { FirstWeek() }, new object[0]), Now);
            Assert.Equal(new[] { "История (Лекция)" }, Events(ScheduleCalendar.Build(lone, "ru", DateTime.UtcNow)).Select(e => e["SUMMARY"]));
            // The other week unread still keeps the cycle: they come back two weeks on.
            var unread = SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { FirstWeek() }, new object[] { 2 }), Now);
            Assert.Equal(3, Events(ScheduleCalendar.Build(unread, "ru", DateTime.UtcNow)).Count);
            // A semester that ends before they would come back.
            var shortTerm = SchedulePair.Parse(Json("2026/1", new DateTime(2026, 9, 14), new object[] { FirstWeek() }, new object[] { 2 }), Now);
            Assert.Equal(new[] { "История (Лекция)" }, Events(ScheduleCalendar.Build(shortTerm, "ru", DateTime.UtcNow)).Select(e => e["SUMMARY"]));
        }

        [Fact]
        public void TextIsEscaped()
        {
            Assert.Equal(@"a\\b\;c\,d\ne\nf", ScheduleCalendar.Escape("a\\b;c,d\r\ne\nf"));
            var week = Week(3, Monday3, new object[] { new object[] { Lesson(540, 590, "Право; история, теория", "Лекция", "Ауд. 1\\2") } });
            var pair = SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { week }, new object[0]), Now);
            var e = Events(ScheduleCalendar.Build(pair, "ru", DateTime.UtcNow)).Single();
            Assert.Equal(@"Право\; история\, теория (Лекция)", e["SUMMARY"]);
            Assert.Equal(@"Ауд. 1\\2", e["LOCATION"]);
        }

        [Fact]
        public void LongLinesFoldAt75OctetsWithoutSplittingACharacter()
        {
            var line = "SUMMARY:" + string.Concat(Enumerable.Repeat("Политология 😀 ", 20));
            var parts = ScheduleCalendar.Fold(line).ToList();
            Assert.True(parts.Count > 1);
            Assert.All(parts, p => Assert.True(Encoding.UTF8.GetByteCount(p) <= 75, p));
            Assert.All(parts.Skip(1), p => Assert.StartsWith(" ", p));
            Assert.All(parts, p => Assert.False(char.IsHighSurrogate(p[p.Length - 1]), "a surrogate pair was split"));
            Assert.Equal(line, parts[0] + string.Concat(parts.Skip(1).Select(p => p.Substring(1))));
            Assert.Equal(new[] { "SHORT:x" }, ScheduleCalendar.Fold("SHORT:x"));
        }

        [Fact]
        public void HeadingsAndFileNamesFollowTheLanguage()
        {
            var pair = SchedulePair.Parse(PairJson(), Now);
            Assert.Equal("Неделя 3, нечётная", SchedulePair.Heading(pair.Weeks[0], "ru"));
            Assert.Equal("4-апта, жұп", SchedulePair.Heading(pair.Weeks[1], "kz"));
            Assert.Equal("Week 3, odd", SchedulePair.Heading(pair.Weeks[0], "en"));
            Assert.Equal("Расписание, недели 3–4.ics", SchedulePair.FileName(pair, "ru", ".ics"));
            Assert.Equal("Schedule, weeks 3–4.png", SchedulePair.FileName(pair, "en", ".png"));
            // A week the site does not number is named by its dates, and a missing neighbour is not given a guessed parity.
            var week = Week(5, Monday3, OddWeek()); week["labelled"] = false;
            var unnumbered = SchedulePair.Parse(Json("2026/1", SemesterEnd, new object[] { week }, new object[] { 6 }), Now);
            Assert.Equal(OfflineWeek.Range(unnumbered.Weeks[0].Week, "ru"), SchedulePair.Heading(unnumbered.Weeks[0], "ru"));
            Assert.Equal("Соседняя неделя", unnumbered.MissingHeading(6, "ru"));
            Assert.Equal("Расписание, недели 5–6.ics", SchedulePair.FileName(unnumbered, "ru", ".ics"));
        }

        [Fact]
        public void OnlyTheTwoKindsAreTaken()
        {
            Assert.Equal("ics", ScheduleExport.Kind("ics")); Assert.Equal("png", ScheduleExport.Kind("png"));
            Assert.Null(ScheduleExport.Kind("exe")); Assert.Null(ScheduleExport.Kind(null));
        }

        [Theory]
        [InlineData("https://univer.kaznu.kz/student/myschedule/", true)]
        [InlineData("https://univer.kaznu.kz/student/myschedule", true)]
        [InlineData("https://univer.kaznu.kz/student/myschedule/2026/1/", true)]
        [InlineData("https://univer.kaznu.kz/student/myschedule/2026/1/09.11.2026/15.11.2026/", true)]
        [InlineData("https://univer.kaznu.kz/student/myschedule/print/2026/1/", false)]
        [InlineData("https://univer.kaznu.kz/student/bachelor/", false)]
        [InlineData("http://univer.kaznu.kz/student/myschedule/", false)]
        [InlineData("https://univer.kaznu.kz.example.com/student/myschedule/", false)]
        public void TheButtonsWorkOnlyOnTheSchedulePage(string url, bool expected)
        { Assert.Equal(expected, SitePolicy.SchedulePage(url)); }
    }
}
