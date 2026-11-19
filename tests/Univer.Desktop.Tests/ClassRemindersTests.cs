using System;
using System.Collections.Generic;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ClassRemindersTests
    {
        private static readonly DateTime Monday = new DateTime(2026, 11, 9);
        private static ScheduleClass At(int day, int hour, int minute, string name, string room = "305")
        {
            var start = Monday.AddDays(day).AddHours(hour).AddMinutes(minute);
            return new ScheduleClass { Start = start, End = start.AddMinutes(50), Name = name, Type = "Лекция", Room = room, Teacher = "" };
        }
        private static ScheduleWeek Week(params ScheduleClass[] classes) { return new ScheduleWeek { Start = Monday, End = Monday.AddDays(7), Classes = classes.ToList() }; }
        private static string[] Names(IEnumerable<ScheduleClass> list) { return list.Select(c => c.Name).ToArray(); }

        [Theory]
        // 9:00 class, 15 minutes ahead: quiet before 8:45, announced from 8:45 up to the start, never once it began.
        [InlineData(8, 44, 59, false)]
        [InlineData(8, 45, 0, true)]
        [InlineData(8, 59, 59, true)]
        [InlineData(9, 0, 0, false)]
        [InlineData(9, 20, 0, false)]
        public void OnlyInsideTheLeadWindow(int hour, int minute, int second, bool due)
        {
            var week = Week(At(0, 9, 0, "Политология"));
            var now = Monday.AddHours(hour).AddMinutes(minute).AddSeconds(second);
            Assert.Equal(due ? 1 : 0, ClassReminders.Due(week, now, 15, new HashSet<string>()).Count);
        }

        [Fact]
        public void EachClassIsAnnouncedOnce()
        {
            var week = Week(At(0, 9, 0, "Политология"), At(0, 10, 0, "Философия"));
            var announced = new HashSet<string>();
            var first = ClassReminders.Due(week, Monday.AddHours(8).AddMinutes(50), 15, announced);
            Assert.Equal(new[] { "Политология" }, Names(first));
            first.ForEach(c => announced.Add(ClassReminders.Key(c)));
            Assert.Empty(ClassReminders.Due(week, Monday.AddHours(8).AddMinutes(55), 15, announced));
            Assert.Equal(new[] { "Философия" }, Names(ClassReminders.Due(week, Monday.AddHours(9).AddMinutes(50), 15, announced)));
        }

        [Fact]
        public void ClassesStartingTogetherComeInOneReminder()
        {
            var week = Week(At(0, 9, 0, "Политология", "305"), At(0, 9, 0, "Политология", "306"), At(0, 9, 5, "Философия"));
            Assert.Equal(new[] { "305", "306" }, ClassReminders.Due(week, Monday.AddHours(8).AddMinutes(58), 15, new HashSet<string>()).Select(c => c.Room).ToArray());
        }

        [Fact]
        public void ACopyOfAnotherWeekGivesNothing()
        {
            var week = Week(At(0, 9, 0, "Политология"));
            Assert.Empty(ClassReminders.Due(week, Monday.AddDays(7).AddHours(8).AddMinutes(50), 15, new HashSet<string>()));
            Assert.Empty(ClassReminders.Due(null, Monday.AddHours(8).AddMinutes(50), 15, new HashSet<string>()));
        }

        [Theory]
        [InlineData(5, 5)]
        [InlineData(30, 30)]
        [InlineData(0, 15)]
        [InlineData(45, 15)]
        public void TheLeadIsOneOfTheOffered(int asked, int used) { Assert.Equal(used, ClassReminders.Lead(asked)); }

        [Fact]
        public void TheBalloonNamesTheClassUnlessThePinHidesIt()
        {
            var due = new List<ScheduleClass> { At(0, 9, 0, "Политология", "305"), At(0, 9, 0, "Философия", "") };
            var now = Monday.AddHours(8).AddMinutes(45);
            Assert.Equal("Пара через 15 мин", ClassReminders.Title(due, now, "ru"));
            Assert.Equal("Class in 15 min", ClassReminders.Title(due, now, "en"));
            Assert.Equal("Политология · ауд. 305" + Environment.NewLine + "Философия", ClassReminders.Body(due, "ru", false));
            var hidden = ClassReminders.Body(due, "kz", true);
            Assert.DoesNotContain("Политология", hidden);
            Assert.DoesNotContain("305", hidden);
            var long_ = new List<ScheduleClass> { At(0, 9, 0, new string('а', 400)) };
            Assert.True(ClassReminders.Body(long_, "ru", false).Length <= 255);
        }

        [Fact]
        public void MinutesLeftRoundUp()
        {
            var lesson = At(0, 9, 0, "Политология");
            Assert.Equal(15, ClassReminders.MinutesLeft(lesson, Monday.AddHours(8).AddMinutes(45)));
            Assert.Equal(15, ClassReminders.MinutesLeft(lesson, Monday.AddHours(8).AddMinutes(45).AddSeconds(30)));
            Assert.Equal(1, ClassReminders.MinutesLeft(lesson, Monday.AddHours(8).AddMinutes(59).AddSeconds(59)));
        }
    }
}
