using System;
using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ScheduleCopyTests
    {
        private static readonly DateTime Monday = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Local);
        private static long Ms(DateTime local) { return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        private static string Lesson(int start, int end, string name, string room = "305") { return "{\"start\":" + start + ",\"end\":" + end + ",\"name\":\"" + name + "\",\"type\":\"Лекция\",\"room\":\"" + room + "\",\"teacher\":\"Синтетический П. П.\"}"; }
        private static string Week(DateTime start, DateTime end, params string[] days) { return "{\"start\":" + Ms(start) + ",\"end\":" + Ms(end) + ",\"days\":[" + string.Join(",", days) + "]}"; }

        [Fact]
        public void TheCurrentWeekBecomesClassesAtTheirOwnDayAndTime()
        {
            var raw = Week(Monday, Monday.AddDays(7), "[" + Lesson(540, 590, "Политология") + "]", "[]", "[" + Lesson(600, 650, "Культурология") + "," + Lesson(480, 530, "Философия") + "]");
            var week = ScheduleWeek.Parse(raw, Monday.AddDays(1).AddHours(8), "acc");
            Assert.NotNull(week);
            Assert.Equal(new[] { "Политология", "Философия", "Культурология" }, week.Classes.ConvertAll(c => c.Name).ToArray());
            Assert.Equal(new DateTime(2026, 11, 9, 9, 0, 0), week.Classes[0].Start);
            Assert.Equal(new DateTime(2026, 11, 11, 8, 50, 0), week.Classes[1].End);
            Assert.Equal("305", week.Classes[0].Room);
            Assert.Equal("acc", week.Account);
        }

        [Fact]
        public void AWeekThatStartsMidweekDropsTheDaysBeforeIt()
        {
            var wednesday = Monday.AddDays(2);
            var raw = Week(wednesday, Monday.AddDays(7), "[" + Lesson(540, 590, "Понедельник") + "]", "[]", "[" + Lesson(540, 590, "Среда") + "]");
            var week = ScheduleWeek.Parse(raw, wednesday.AddHours(10), "");
            Assert.Equal(new[] { "Среда" }, week.Classes.ConvertAll(c => c.Name).ToArray());
            Assert.Equal(wednesday.AddMinutes(540), week.Classes[0].Start);
        }

        [Theory]
        // Another week than the one now, a "week" longer than seven days, a class ending before it starts or after
        // midnight, an empty or overlong name, a missing field, a number given as text, too many days, broken JSON.
        [InlineData("past")]
        [InlineData("long")]
        [InlineData("backwards")]
        [InlineData("midnight")]
        [InlineData("noname")]
        [InlineData("overlong")]
        [InlineData("missing")]
        [InlineData("text")]
        [InlineData("days")]
        [InlineData("broken")]
        public void AnythingButACurrentWeekOfClassesIsRefused(string kind)
        {
            var now = Monday.AddHours(8); var end = Monday.AddDays(7); var day = "[" + Lesson(540, 590, "Политология") + "]";
            string raw;
            switch (kind)
            {
                case "past": raw = Week(Monday.AddDays(-7), Monday, day); break;
                case "long": raw = Week(Monday, Monday.AddDays(9), day); break;
                case "backwards": raw = Week(Monday, end, "[" + Lesson(590, 540, "Политология") + "]"); break;
                case "midnight": raw = Week(Monday, end, "[" + Lesson(1400, 1500, "Политология") + "]"); break;
                case "noname": raw = Week(Monday, end, "[" + Lesson(540, 590, "") + "]"); break;
                case "overlong": raw = Week(Monday, end, "[" + Lesson(540, 590, new string('а', 401)) + "]"); break;
                case "missing": raw = Week(Monday, end, "[{\"start\":540,\"end\":590,\"name\":\"Политология\"}]"); break;
                case "text": raw = Week(Monday, end, "[{\"start\":\"540\",\"end\":590,\"name\":\"Политология\",\"type\":\"\",\"room\":\"\",\"teacher\":\"\"}]"); break;
                case "days": raw = Week(Monday, end, day, day, day, day, day, day, day, day); break;
                default: raw = "{\"start\":"; break;
            }
            Assert.Null(ScheduleWeek.Parse(raw, now, ""));
        }

        [Fact]
        public void ControlCharactersAndSpaceRunsAreFolded()
        {
            var raw = Week(Monday, Monday.AddDays(7), "[" + Lesson(540, 590, "  Полито\\u0007логия \\n  курс ") + "]");
            Assert.Equal("Полито логия курс", ScheduleWeek.Parse(raw, Monday.AddHours(8), "").Classes[0].Name);
        }

        [Fact]
        public void AccountMarkKeepsItsRulesThroughTheRememberedAnswer()
        {
            var a = ScheduleWeek.AccountMark("Memo-One");
            Assert.Equal(a, ScheduleWeek.AccountMark("Memo-One"));
            var b = ScheduleWeek.AccountMark("memo-two");
            Assert.NotEqual(a, b);
            // Back to the first login after another: still its own mark, case and spaces folded as before.
            Assert.Equal(a, ScheduleWeek.AccountMark("  memo-one "));
            Assert.Equal("", ScheduleWeek.AccountMark("   "));
            Assert.Equal("", ScheduleWeek.AccountMark(null));
            Assert.Equal(b, ScheduleWeek.AccountMark("MEMO-TWO"));
        }

        [Fact]
        public void TheSavedCopyBelongsToOneAccountAndGoesWithRemove()
        {
            var root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var mark = ScheduleWeek.AccountMark("Student01");
                Assert.Equal(mark, ScheduleWeek.AccountMark(" student01 "));
                Assert.NotEqual(mark, ScheduleWeek.AccountMark("student02"));
                Assert.Equal(16, mark.Length);
                var week = ScheduleWeek.Parse(Week(Monday, Monday.AddDays(7), "[" + Lesson(540, 590, "Политология") + "]"), Monday.AddHours(8), mark);
                ScheduleFile.Write(root, week);
                Assert.DoesNotContain("Student01", File.ReadAllText(Path.Combine(root, ScheduleFile.Name)), StringComparison.OrdinalIgnoreCase);
                var back = ScheduleFile.Read(root, mark);
                Assert.True(week.SameAs(back));
                Assert.Null(ScheduleFile.Read(root, ScheduleWeek.AccountMark("student02")));
                File.WriteAllText(Path.Combine(root, ScheduleFile.Name), "{\"version\":1,\"account\":\"" + mark + "\",\"start\":\"x\"}");
                Assert.Null(ScheduleFile.Read(root, mark));
                ScheduleFile.Remove(root);
                Assert.Null(ScheduleFile.Read(root, mark));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
