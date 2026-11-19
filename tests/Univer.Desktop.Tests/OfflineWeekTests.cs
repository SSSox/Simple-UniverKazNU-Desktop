using System;
using System.Collections.Generic;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class OfflineWeekTests
    {
        private static ScheduleWeek Week(DateTime monday, params ScheduleClass[] classes) { return new ScheduleWeek { Start = monday, End = monday.AddDays(7), SavedAt = monday.AddHours(14), Classes = classes.ToList() }; }
        private static ScheduleClass At(DateTime start, string name, string type = "Лекция", string room = "305", string teacher = "Иванов И. И.") { return new ScheduleClass { Start = start, End = start.AddMinutes(50), Name = name, Type = type, Room = room, Teacher = teacher }; }
        private static readonly DateTime Monday = new DateTime(2026, 11, 16);

        [Theory]
        [InlineData("ru", "16–22 ноября")]
        [InlineData("kz", "16–22 қараша")]
        [InlineData("en", "16–22 November")]
        public void TheWeekIsNamedByItsDays(string lang, string expected) { Assert.Equal(expected, OfflineWeek.Range(Week(Monday), lang)); }

        [Fact]
        public void AWeekAcrossTwoMonthsNamesBoth() { Assert.Equal("30 ноября – 6 декабря", OfflineWeek.Range(Week(new DateTime(2026, 11, 30)), "ru")); }

        [Theory]
        [InlineData("ru", "Понедельник, 16 ноября")]
        [InlineData("kz", "Дүйсенбі, 16 қараша")]
        [InlineData("en", "Monday, 16 November")]
        public void DaysAreNamedInTheAppLanguage(string lang, string expected) { Assert.Equal(expected, OfflineWeek.DayTitle(Monday, lang)); }

        [Fact]
        public void SundayIsTheLastDay() { Assert.Equal("Воскресенье, 22 ноября", OfflineWeek.DayTitle(Monday.AddDays(6), "ru")); }

        [Fact]
        public void TheSavedTimeSaysTodayOrTheDate()
        {
            var saved = Monday.AddHours(14).AddMinutes(5);
            Assert.Equal("Сохранено сегодня в 14:05", OfflineWeek.Saved(saved, saved.AddHours(3), "ru"));
            Assert.Equal("Сохранено 16 ноября в 14:05", OfflineWeek.Saved(saved, saved.AddDays(1), "ru"));
            Assert.Equal("Сақталған уақыты: 16 қараша, 14:05", OfflineWeek.Saved(saved, saved.AddDays(1), "kz"));
            Assert.Equal("Saved today at 14:05", OfflineWeek.Saved(saved, saved, "en"));
        }

        [Fact]
        public void ClassesGoByDayInTimeOrderAndEmptyDaysAreLeftOut()
        {
            var week = Week(Monday, At(Monday.AddDays(2).AddHours(10), "Б"), At(Monday.AddHours(10), "В"), At(Monday.AddHours(8), "А"));
            var days = OfflineWeek.ByDay(week);
            Assert.Equal(new[] { Monday, Monday.AddDays(2) }, days.Select(d => d.Key).ToArray());
            Assert.Equal(new[] { "А", "В" }, days[0].Value.Select(c => c.Name).ToArray());
            Assert.Empty(OfflineWeek.ByDay(Week(Monday)));
            Assert.False(OfflineWeek.Shows(Week(Monday)));
            Assert.False(OfflineWeek.Shows(null));
        }

        [Fact]
        public void DetailsNameOnlyWhatThePageGave()
        {
            Assert.Equal("Лекция · ауд. 305 · Иванов И. И.", OfflineWeek.Details(At(Monday, "А"), "ru"));
            Assert.Equal("Семинар", OfflineWeek.Details(At(Monday, "А", "Семинар", "", ""), "ru"));
            Assert.Equal("room 305", OfflineWeek.Details(At(Monday, "А", "", "305", ""), "en"));
            Assert.Equal("08:00–08:50", OfflineWeek.Time(At(Monday.AddHours(8), "А")));
        }

        [Fact]
        public void TheCurrentWeekEndsAtItsEnd()
        {
            var week = Week(Monday);
            Assert.True(OfflineWeek.Current(week, Monday));
            Assert.True(OfflineWeek.Current(week, Monday.AddDays(7).AddTicks(-1)));
            Assert.False(OfflineWeek.Current(week, Monday.AddDays(7)));
            Assert.False(OfflineWeek.Current(week, Monday.AddTicks(-1)));
        }
    }
}
