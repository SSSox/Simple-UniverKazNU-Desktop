using System;
using System.Collections.Generic;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class OfflineEntryTests
    {
        private const long Limit = OfflineEntry.WaitLimit;

        [Fact]
        public void AWaitIsOfferedOnlyPastTheLimit()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, false, 1000);
            Assert.False(entry.Offers(1000 + Limit - 1, true, false));
            entry.Observe(true, false, 1000 + Limit - 1);
            Assert.True(entry.Offers(1000 + Limit, true, false));
        }

        [Fact]
        public void AFailureIsOfferedAtOnce()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, true, 5000);
            Assert.True(entry.Offers(5000, true, false));
        }

        [Fact]
        public void ACalmLookStartsTheWaitOver()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, false, 0);
            entry.Observe(false, false, Limit - 1);
            entry.Observe(true, false, Limit);
            Assert.False(entry.Offers(Limit + 10, true, false));
            Assert.True(entry.Offers(2 * Limit, true, false));
        }

        [Fact]
        public void NothingIsOfferedWithoutACopyOrBehindThePin()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, true, 0);
            Assert.False(entry.Offers(0, false, false));
            Assert.False(entry.Offers(0, true, true));
            Assert.False(entry.Open(0, true, true));
            Assert.False(entry.Open(0, false, false));
            Assert.False(entry.Chosen);
        }

        [Fact]
        public void NothingCanBeOpenedWhileTheSiteWorks()
        {
            var entry = new OfflineEntry();
            Assert.False(entry.Open(Limit * 10, true, false));
            entry.Observe(false, false, 0);
            Assert.False(entry.Open(Limit * 10, true, false));
        }

        [Fact]
        public void TheLockForgetsTheWaitSoItStartsOverAfterTheUnlock()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, false, 0);
            entry.Forget();
            entry.Observe(true, false, Limit);
            Assert.False(entry.Offers(Limit + 1, true, false));
            Assert.True(entry.Offers(2 * Limit, true, false));
        }

        [Fact]
        public void OpeningHidesTheOfferAndSignedInContentClosesTheScreen()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, true, 0);
            Assert.True(entry.Open(0, true, false));
            Assert.True(entry.Chosen);
            Assert.False(entry.Offers(0, true, false));
            // The site's own pages keep waiting or failing under the screen: it stays.
            entry.Observe(true, true, Limit * 3);
            Assert.True(entry.Chosen);
            // A calm page that is not signed-in content (a missing page of the site) ends the trouble, not the screen.
            entry.Observe(false, false, Limit * 4);
            Assert.True(entry.Chosen);
            Assert.True(entry.SiteWorks());
            Assert.False(entry.Chosen);
            Assert.False(entry.SiteWorks());
        }

        [Fact]
        public void LeavingTheScreenOffersItAgainOnlyIfTheTroubleGoesOn()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, false, 0);
            Assert.True(entry.Open(Limit, true, false));
            Assert.True(entry.Leave());
            Assert.False(entry.Leave());
            entry.Observe(true, false, Limit + 100);
            Assert.False(entry.Offers(Limit + 200, true, false));
            Assert.True(entry.Offers(2 * Limit + 100, true, false));
            // A failure after leaving offers it again at once.
            Assert.True(entry.Open(2 * Limit + 100, true, false));
            entry.Leave();
            entry.Observe(true, true, 2 * Limit + 200);
            Assert.True(entry.Offers(2 * Limit + 200, true, false));
        }

        [Fact]
        public void AClosedOfferStaysClosedUntilTheTroubleEnds()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, true, 0);
            entry.Decline();
            entry.Observe(true, true, Limit * 5);
            Assert.False(entry.Offers(Limit * 5, true, false));
            entry.Observe(false, false, Limit * 6);
            entry.Observe(true, true, Limit * 7);
            Assert.True(entry.Offers(Limit * 7, true, false));
        }

        [Fact]
        public void ANewSessionCarriesNothingOver()
        {
            var entry = new OfflineEntry();
            entry.Observe(true, true, 0);
            entry.Open(0, true, false);
            entry.Reset();
            Assert.False(entry.Chosen);
            Assert.False(entry.Offers(Limit * 2, true, false));
        }

        [Theory]
        [InlineData("ProxyConnectionFailed", true)]
        [InlineData("NameNotResolved", true)]
        [InlineData("ConnectionTimedOut", true)]
        [InlineData("500", true)]
        [InlineData("503", true)]
        [InlineData("408", true)]
        [InlineData("429", true)]
        [InlineData("404", false)]
        [InlineData("403", false)]
        [InlineData("CertAuthorityInvalid", false)]
        [InlineData("Aborted", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyFailuresTheAppRetriesCount(string code, bool expected) { Assert.Equal(expected, OfflineEntry.Failure(code)); }
    }

    public class OfflineExamsTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 12, 0, 0);
        private static ExamItem Exam(ExamKind kind, string subject, DateTime start, string place = "") { return new ExamItem { Kind = kind, Subject = subject, Start = start, Place = place }; }

        [Fact]
        public void OnlyComingExamsShowSoonestFirstAndAtMostTwelve()
        {
            var items = new List<ExamItem> { Exam(ExamKind.Exam, "Прошлый", Now.AddHours(-1)), Exam(ExamKind.Exam, "Сейчас", Now), Exam(ExamKind.Midterm, "Б", Now.AddDays(2)), Exam(ExamKind.Exam, "А", Now.AddDays(1)) };
            for (var i = 0; i < 20; i++) items.Add(Exam(ExamKind.Exam, "Позже " + i, Now.AddDays(10 + i)));
            var shown = OfflineWeek.Exams(new ExamPlan("acc", Now, items, null), Now);
            Assert.Equal(OfflineWeek.MaxExams, shown.Count);
            Assert.Equal(new[] { "А", "Б", "Позже 0" }, shown.Take(3).Select(i => i.Subject));
            Assert.Empty(OfflineWeek.Exams(null, Now));
        }

        [Fact]
        public void ExamsGoByDayWithTheirStartKindAndRoom()
        {
            var shown = new[] { Exam(ExamKind.Exam, "Статистика", Now.Date.AddDays(3).AddHours(15), "101"), Exam(ExamKind.Midterm, "Философия", Now.Date.AddDays(3).AddHours(9)), Exam(ExamKind.Exam, "Экономика", Now.Date.AddDays(1).AddHours(9), "215") };
            var days = OfflineWeek.ExamDays(shown, "ru");
            Assert.Equal(2, days.Count);
            Assert.Equal(Now.Date.AddDays(1), days[0].Key);
            var second = days[1].Value;
            Assert.Equal(new[] { "Философия", "Статистика" }, second.Select(c => c.Name));
            Assert.Equal("09:00", OfflineWeek.Time(second[0]));
            Assert.Equal("Рубежный контроль", OfflineWeek.Details(second[0], "ru"));
            Assert.Equal("Экзамен · ауд. 101", OfflineWeek.Details(second[1], "ru"));
            Assert.Equal("Exam · room 215", OfflineWeek.Details(OfflineWeek.ExamDays(shown, "en")[0].Value[0], "en"));
        }

        [Fact]
        public void EachKindSaysWhenItsPageWasRead()
        {
            var exam = Exam(ExamKind.Exam, "Экономика", Now.AddDays(5));
            var midterm = Exam(ExamKind.Midterm, "Философия", Now.AddDays(6));
            var plan = ExamPlan.Empty("acc", Now.AddDays(-3)).Replace(ExamKind.Midterm, new[] { midterm }, Now.AddDays(-3)).Replace(ExamKind.Exam, new[] { exam }, Now.AddHours(-1));
            Assert.Equal(Now.AddHours(-1), plan.Saved(ExamKind.Exam));
            Assert.Equal(Now.AddDays(-3), plan.Saved(ExamKind.Midterm));
            Assert.Equal(new[] { ExamKind.Exam, ExamKind.Midterm }, OfflineWeek.ExamKinds(plan.Items));
            Assert.Equal("Экзамены · сохранено сегодня в 11:00", OfflineWeek.ExamSaved(plan, ExamKind.Exam, true, Now, "ru"));
            Assert.Equal("Рубежный контроль · сохранено 20 ноября в 12:00", OfflineWeek.ExamSaved(plan, ExamKind.Midterm, true, Now, "ru"));
            Assert.Equal("Сохранено сегодня в 11:00", OfflineWeek.ExamSaved(plan, ExamKind.Exam, false, Now, "ru"));
            // Marking a reminder said changes neither time.
            var said = plan.WithAnnounced(new[] { "e|" + exam.Key });
            Assert.Equal(plan.Saved(ExamKind.Midterm), said.Saved(ExamKind.Midterm));
            Assert.Equal(plan.Saved(ExamKind.Exam), said.Saved(ExamKind.Exam));
        }

        [Fact]
        public void TheKindTimesRoundTripAndAnOlderFileUsesItsOneTime()
        {
            var exam = Exam(ExamKind.Exam, "Экономика", Now.AddDays(5));
            var midterm = Exam(ExamKind.Midterm, "Философия", Now.AddDays(6));
            var plan = new ExamPlan("acc", Now, new[] { exam, midterm }, null, Now.AddHours(-2), Now.AddDays(-4));
            var back = ExamPlan.Load(plan.Serialize(), "acc");
            Assert.NotNull(back);
            Assert.Equal(Now.AddHours(-2), back.Saved(ExamKind.Exam));
            Assert.Equal(Now.AddDays(-4), back.Saved(ExamKind.Midterm));
            Assert.True(back.SameAs(plan));
            // The same list read again a moment later (sub-second clock, a new kind time) is the same plan: not written again.
            var again = plan.Replace(ExamKind.Exam, new[] { exam }, Now.AddMinutes(1).AddTicks(1234));
            Assert.True(again.SameAs(plan));
            Assert.Equal(Now.AddMinutes(1).AddTicks(1234), again.Saved(ExamKind.Exam));
            var older = plan.Serialize().Replace(",\"savedExam\":\"" + Now.AddHours(-2).ToString("yyyy-MM-ddTHH:mm:ss") + "\"", "").Replace(",\"savedMidterm\":\"" + Now.AddDays(-4).ToString("yyyy-MM-ddTHH:mm:ss") + "\"", "");
            Assert.DoesNotContain("savedExam", older);
            var old = ExamPlan.Load(older, "acc");
            Assert.NotNull(old);
            Assert.Equal(Now, old.Saved(ExamKind.Exam));
            Assert.Equal(Now, old.Saved(ExamKind.Midterm));
            Assert.Null(ExamPlan.Load(plan.Serialize().Replace("\"savedMidterm\":\"", "\"savedMidterm\":\"x"), "acc"));
        }
    }
}
