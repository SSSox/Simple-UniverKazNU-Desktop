using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class LastGradesTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 12, 0, 0);
        private const string Account = "0123456789abcdef";
        private static long Ms(DateTime local) { return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        private static string Score(string label, string max, string value) { return "{\"label\":\"" + label + "\",\"max\":\"" + max + "\",\"value\":\"" + value + "\"}"; }
        private static string Course(string name, params string[] scores) { return "{\"name\":\"" + name + "\",\"scores\":[" + string.Join(",", scores) + "]}"; }
        private static string Reading(string courses, long? at = null, string version = "1") { return "{\"v\":" + version + ",\"at\":" + (at ?? Ms(Now.AddHours(-1))) + ",\"courses\":[" + courses + "]}"; }
        private static LastGrades Parse(string raw, string account = Account) { return LastGrades.Parse(raw, Now, account); }

        [Fact]
        public void AValidReadingGivesItsCoursesAndTheTimeItWasRead()
        {
            var grades = Parse(Reading(Course("Алгебра", Score("РК1", "100", "71"), Score("Экзамен", "40", "31")) + "," + Course("Физика", Score("Итог", "", "93"))));
            Assert.NotNull(grades);
            Assert.Equal(Account, grades.Account);
            Assert.Equal(Now.AddHours(-1), grades.SavedAt);
            Assert.Equal(new[] { "Алгебра", "Физика" }, grades.Courses.Select(c => c.Name).ToArray());
            Assert.Equal(new[] { "71/100", "31/40" }, grades.Courses[0].Scores.Select(LastGrades.Score).ToArray());
            Assert.Equal("93", LastGrades.Score(grades.Courses[1].Scores[0]));
        }

        [Fact]
        public void ScoresWithoutAValueAreDroppedAndSoAreCoursesLeftWithNone()
        {
            var grades = Parse(Reading(Course("Алгебра", Score("РК1", "100", "71"), Score("РК2", "100", "")) + "," + Course("Физика", Score("РК1", "100", ""))));
            var only = Assert.Single(grades.Courses);
            Assert.Equal("Алгебра", only.Name);
            Assert.Equal("РК1", Assert.Single(only.Scores).Label);
            Assert.Null(Parse(Reading(Course("Физика", Score("РК1", "100", "")))));
            Assert.Null(Parse(Reading("")));
        }

        [Fact]
        public void TextIsFoldedAndBounded()
        {
            var folded = Parse(Reading(Course("  Алгебра\\n  и   начала  ", Score("РК1", "100", "71"))));
            Assert.Equal("Алгебра и начала", folded.Courses[0].Name);
            Assert.Null(Parse(Reading(Course(new string('а', 201), Score("РК1", "100", "71")))));
            Assert.NotNull(Parse(Reading(Course(new string('а', 200), Score("РК1", "100", "71")))));
            Assert.Null(Parse(Reading(Course("Алгебра", Score("РК1", "100", new string('7', 41))))));
            Assert.Null(Parse(Reading(Course("   ", Score("РК1", "100", "71")))));
        }

        [Fact]
        public void CountsAreBounded()
        {
            Func<int, string> courses = n => string.Join(",", Enumerable.Range(0, n).Select(i => Course("Курс " + i, Score("РК1", "100", "1"))));
            Assert.Equal(60, Parse(Reading(courses(60))).Courses.Count);
            Assert.Null(Parse(Reading(courses(61))));
            Func<int, string> scores = n => Course("Алгебра", Enumerable.Range(0, n).Select(i => Score("РК" + i, "100", "1")).ToArray());
            Assert.Equal(10, Parse(Reading(scores(10))).Courses[0].Scores.Count);
            Assert.Null(Parse(Reading(scores(11))));
        }

        [Fact]
        public void AnythingOutOfShapeIsNone()
        {
            var good = Course("Алгебра", Score("РК1", "100", "71"));
            Assert.Null(Parse(Reading(good, version: "2")));
            Assert.Null(Parse(Reading(good, Ms(Now.AddMinutes(5)))));
            Assert.Null(Parse(Reading(good, Ms(Now.AddDays(-16)))));
            Assert.Null(Parse(Reading(good, -1)));
            Assert.Null(Parse("{\"v\":1,\"courses\":[" + good + "]}"));
            Assert.Null(Parse("{\"v\":1,\"at\":\"yesterday\",\"courses\":[" + good + "]}"));
            Assert.Null(Parse("not json"));
            Assert.Null(Parse(""));
            Assert.Null(Parse("[]"));
            Assert.Null(Parse(Reading("{\"name\":\"Алгебра\"}")));
            Assert.Null(Parse(Reading("{\"name\":7,\"scores\":[]}")));
            Assert.Null(Parse(Reading("{\"name\":\"Алгебра\",\"scores\":[{\"label\":\"РК1\",\"max\":\"100\",\"value\":71}]}")));
            Assert.Null(Parse(new string(' ', LastGrades.MaxRaw + 1)));
        }

        [Fact]
        public void NothingIsKeptWithoutALoginMark()
        {
            var raw = Reading(Course("Алгебра", Score("РК1", "100", "71")));
            Assert.Null(Parse(raw, ""));
            Assert.Null(Parse(raw, null));
        }

        [Fact]
        public void TheKeptFileRoundTripsPerLoginAndOutOfShapeIsNone()
        {
            var root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var mark = ScheduleWeek.AccountMark("Student01");
                var grades = LastGrades.Parse(Reading(Course("Алгебра", Score("РК1", "100", "71"), Score("РК2", "100", "88.5")) + "," + Course("Физика", Score("Итог", "", "93"))), Now, mark);
                LastGradesFile.Write(root, grades);
                var text = File.ReadAllText(Path.Combine(root, LastGradesFile.Name));
                Assert.DoesNotContain("Student01", text, StringComparison.OrdinalIgnoreCase);
                var back = LastGradesFile.Read(root, mark);
                Assert.True(grades.SameAs(back));
                Assert.Equal(grades.SavedAt, back.SavedAt);
                Assert.Null(LastGradesFile.Read(root, ScheduleWeek.AccountMark("student02")));
                File.WriteAllText(Path.Combine(root, LastGradesFile.Name), text.Replace("\"version\":1", "\"version\":2"));
                Assert.Null(LastGradesFile.Read(root, mark));
                File.WriteAllText(Path.Combine(root, LastGradesFile.Name), text.Replace("\"value\":\"71\"", "\"value\":\"\""));
                Assert.Null(LastGradesFile.Read(root, mark));
                File.WriteAllText(Path.Combine(root, LastGradesFile.Name), text.Replace("\"saved\":\"", "\"saved\":\"bad"));
                Assert.Null(LastGradesFile.Read(root, mark));
                File.WriteAllText(Path.Combine(root, LastGradesFile.Name), "{}");
                Assert.Null(LastGradesFile.Read(root, mark));
                LastGradesFile.Write(root, grades);
                LastGradesFile.Remove(root);
                Assert.Null(LastGradesFile.Read(root, mark));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void ANewReadingReplacesTheOldOneAndTheSameOneIsTheSame()
        {
            var first = Parse(Reading(Course("Алгебра", Score("РК1", "100", "71"))));
            var same = Parse(Reading(Course("Алгебра", Score("РК1", "100", "71")), Ms(Now.AddMinutes(-5))));
            var changed = Parse(Reading(Course("Алгебра", Score("РК1", "100", "72"))));
            Assert.True(first.SameAs(same));
            Assert.False(first.SameAs(changed));
            Assert.False(first.SameAs(null));
            var root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                LastGradesFile.Write(root, first); LastGradesFile.Write(root, changed);
                Assert.Equal("72/100", LastGrades.Score(LastGradesFile.Read(root, Account).Courses[0].Scores[0]));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
