using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ExamCopyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 12, 0, 0);
        private static string Item(string start, string subject = "Алгебра", string kind = "exam", string place = "ФИТ · 309")
        {
            return "{\"kind\":\"" + kind + "\",\"subject\":\"" + subject + "\",\"start\":\"" + start + "\",\"place\":\"" + place + "\"}";
        }
        private static string Batch(string items, string kind = "exam", string version = "1") { return "{\"v\":" + version + ",\"kind\":\"" + kind + "\",\"items\":" + items + "}"; }
        private static List<ExamItem> Parsed(string raw, out ExamKind kind) { List<ExamItem> items; return ExamPlan.Parse(raw, Now, out kind, out items) ? items : null; }
        private static ExamItem Make(string start, string subject, ExamKind kind = ExamKind.Exam) { return new ExamItem { Kind = kind, Subject = subject, Start = DateTime.Parse(start), Place = "" }; }
        private static ExamPlan Plan(params ExamItem[] items) { return new ExamPlan("acc", Now, items, null); }

        [Fact]
        public void AValidMessageGivesItsComingItemsAndDropsPastOnes()
        {
            ExamKind kind;
            var items = Parsed(Batch("[" + Item("2026-12-15T13:00") + "," + Item("2026-11-20T09:00", "Прошедший") + "," + Item("2026-11-23T11:59", "Уже идёт") + "]"), out kind);
            Assert.Equal(ExamKind.Exam, kind);
            var only = Assert.Single(items);
            Assert.Equal("Алгебра", only.Subject);
            Assert.Equal(new DateTime(2026, 12, 15, 13, 0, 0), only.Start);
            Assert.Equal("ФИТ · 309", only.Place);
        }

        [Fact]
        public void AnItemMoreThanAYearAwayIsDropped()
        {
            ExamKind kind;
            Assert.Empty(Parsed(Batch("[" + Item("2027-12-15T13:00") + "]"), out kind));
        }

        [Theory]
        [InlineData("2026-12-15 13:00")]
        [InlineData("2026-12-15T25:00")]
        [InlineData("2026-12-15T13:60")]
        [InlineData("2026-02-30T10:00")]
        [InlineData("15.12.2026")]
        public void AnItemWithABadStartRefusesTheMessage(string start)
        {
            ExamKind kind;
            Assert.Null(Parsed(Batch("[" + Item(start) + "]"), out kind));
        }

        [Fact]
        public void AnythingOutsideTheShapeIsRefused()
        {
            ExamKind kind;
            var good = Item("2026-12-15T13:00");
            Assert.NotNull(Parsed(Batch("[" + good + "]"), out kind));
            Assert.Null(Parsed(Batch("[" + good + "]", version: "2"), out kind));
            Assert.Null(Parsed(Batch("[" + good + "]", kind: "consult"), out kind));
            Assert.Null(Parsed(Batch("[" + Item("2026-12-15T13:00", kind: "midterm") + "]"), out kind));
            Assert.Null(Parsed(Batch("[" + Item("2026-12-15T13:00", subject: "") + "]"), out kind));
            Assert.Null(Parsed(Batch("[" + Item("2026-12-15T13:00", subject: new string('x', 201)) + "]"), out kind));
            Assert.Null(Parsed(Batch("[" + Item("2026-12-15T13:00", place: new string('x', 201)) + "]"), out kind));
            Assert.Null(Parsed(Batch("\"none\""), out kind));
            Assert.Null(Parsed(Batch("[\"text\"]"), out kind));
            Assert.Null(Parsed(Batch("[" + string.Join(",", Enumerable.Repeat(good, ExamPlan.MaxItems + 1)) + "]"), out kind));
            Assert.NotNull(Parsed(Batch("[" + string.Join(",", Enumerable.Repeat(good, ExamPlan.MaxItems)) + "]"), out kind));
            Assert.Null(Parsed("not json", out kind));
            Assert.Null(Parsed("", out kind));
            Assert.Null(Parsed(null, out kind));
        }

        [Fact]
        public void TextIsFoldedAndAnAbsentPlaceIsEmpty()
        {
            ExamKind kind;
            var items = Parsed(Batch("[{\"kind\":\"midterm\",\"subject\":\"  Теория \\n  графов \",\"start\":\"2026-12-15T13:00\"}]", kind: "midterm"), out kind);
            Assert.Equal(ExamKind.Midterm, kind);
            Assert.Equal("Теория графов", items[0].Subject);
            Assert.Equal("", items[0].Place);
        }

        [Fact]
        public void ABatchReplacesOnlyItsOwnKindAndForgetsTheRemindersOfExamsThatLeft()
        {
            var exam = Make("2026-12-15 10:00", "Алгебра"); var midterm = Make("2026-12-16 10:00", "Физика", ExamKind.Midterm);
            var plan = Plan(exam, midterm).WithAnnounced(new[] { "e|" + exam.Key, "l|" + midterm.Key });
            var next = plan.Replace(ExamKind.Exam, new ExamItem[0], Now.AddMinutes(1));
            Assert.Equal(new[] { "Физика" }, next.Items.Select(i => i.Subject).ToArray());
            Assert.Equal(new[] { "l|" + midterm.Key }, next.Announced.ToArray());
            var again = next.Replace(ExamKind.Exam, new[] { Make("2026-12-17 10:00", "Геометрия") }, Now.AddMinutes(2));
            Assert.Equal(new[] { "Физика", "Геометрия" }, again.Items.Select(i => i.Subject).ToArray());
        }

        [Fact]
        public void AnExamHasAnEveningBeforeReminderAt19AndALeadReminder()
        {
            var plan = Plan(Make("2026-12-15 13:00", "Алгебра"));
            Assert.Empty(plan.Due(new DateTime(2026, 12, 14, 18, 59, 0), 30));
            var evening = Assert.Single(plan.Due(new DateTime(2026, 12, 14, 19, 0, 0), 30));
            Assert.True(evening.Evening);
            plan = plan.WithAnnounced(new[] { evening.Key });
            Assert.Empty(plan.Due(new DateTime(2026, 12, 14, 19, 30, 0), 30));
            Assert.Empty(plan.Due(new DateTime(2026, 12, 15, 12, 29, 0), 30));
            var soon = Assert.Single(plan.Due(new DateTime(2026, 12, 15, 12, 30, 0), 30));
            Assert.False(soon.Evening);
            plan = plan.WithAnnounced(new[] { soon.Key });
            Assert.Empty(plan.Due(new DateTime(2026, 12, 15, 12, 40, 0), 30));
        }

        [Fact]
        public void AnEarlyExamIsRemindedTheEveningBeforeNotThatMorning()
        {
            var plan = Plan(Make("2026-12-15 08:30", "Алгебра"));
            Assert.Empty(plan.Due(new DateTime(2026, 12, 14, 18, 0, 0), 15));
            Assert.True(plan.Due(new DateTime(2026, 12, 14, 19, 0, 0), 15)[0].Evening);
            // After midnight the evening word would be wrong, and the lead one is not yet due.
            Assert.Empty(plan.Due(new DateTime(2026, 12, 15, 1, 0, 0), 15));
        }

        [Fact]
        public void AnExamThatHasBegunIsNeverAnnouncedAndALateStartKeepsTheLeadReminder()
        {
            var plan = Plan(Make("2026-11-24 09:00", "Завтрашний"), Make("2026-11-23 11:00", "Начался"));
            // Opened at 19:30 the evening before: tomorrow's exam is said once, the one that began never.
            var late = plan.Due(new DateTime(2026, 11, 23, 19, 30, 0), 15);
            Assert.Equal(new[] { "Завтрашний" }, late.Select(r => r.Item.Subject).ToArray());
            Assert.True(late[0].Evening);
            Assert.Equal(new[] { false }, plan.WithAnnounced(late.Select(r => r.Key)).Due(new DateTime(2026, 11, 24, 8, 45, 0), 15).Select(r => r.Evening).ToArray());
            Assert.Empty(plan.WithAnnounced(late.Select(r => r.Key)).Due(new DateTime(2026, 11, 24, 9, 0, 0), 15));
            Assert.Empty(plan.Due(new DateTime(2026, 11, 24, 9, 1, 0), 15));
        }

        [Fact]
        public void TheSoonestReminderComesFirstAndEveningOnesOfOneKindTogether()
        {
            var plan = Plan(Make("2026-12-20 10:00", "Поздний"), Make("2026-12-15 13:00", "Ранний"), Make("2026-12-15 15:00", "Тоже завтра"));
            var evening = plan.Due(new DateTime(2026, 12, 14, 19, 5, 0), 10);
            Assert.Equal(new[] { "Ранний", "Тоже завтра" }, evening.Select(r => r.Item.Subject).ToArray());
            Assert.All(evening, r => Assert.True(r.Evening));
        }

        [Fact]
        public void TheLeadIsTheClassReminderLeadAndAnUnknownOneFallsBack()
        {
            var plan = Plan(Make("2026-12-15 13:00", "Алгебра"));
            Assert.Empty(plan.Due(new DateTime(2026, 12, 15, 12, 54, 0), 5));
            Assert.Single(plan.Due(new DateTime(2026, 12, 15, 12, 55, 0), 5));
            Assert.Single(plan.Due(new DateTime(2026, 12, 15, 12, 45, 0), 77));
        }

        [Fact]
        public void TheBalloonNamesTheSubjectUnlessLockedAndStaysWithinTheLimits()
        {
            var item = new ExamItem { Kind = ExamKind.Exam, Subject = "Алгебра", Start = new DateTime(2026, 12, 15, 13, 0, 0), Place = "ФИТ · 309" };
            var soon = new[] { new ExamReminder { Item = item, Evening = false, At = item.Start.AddMinutes(-15) } };
            var evening = new[] { new ExamReminder { Item = item, Evening = true, At = new DateTime(2026, 12, 14, 19, 0, 0) } };
            var now = new DateTime(2026, 12, 15, 12, 45, 0);
            Assert.Equal("Через 15 мин экзамен", ExamReminders.Title(soon, now, "ru", false));
            Assert.Equal("Завтра экзамен", ExamReminders.Title(evening, now, "ru", false));
            Assert.Equal("Tomorrow: exam", ExamReminders.Title(evening, now, "en", false));
            Assert.Equal("Ертең емтихан", ExamReminders.Title(evening, now, "kz", true));
            Assert.Equal("Алгебра · ауд. ФИТ · 309 · начало в 13:00", ExamReminders.Body(soon, "ru", false));
            Assert.Equal("Скоро экзамен", ExamReminders.Title(soon, now, "ru", true));
            var locked = ExamReminders.Body(soon, "ru", true);
            Assert.Equal("Начало в 13:00", locked);
            Assert.DoesNotContain("Алгебра", locked);
            Assert.DoesNotContain("309", locked);
            Assert.True(ExamReminders.Title(soon, now, "kz", false).Length <= 63);
        }

        [Fact]
        public void TheKeptFileRoundTripsPerLoginAndOutOfShapeIsNone()
        {
            var root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var mark = ScheduleWeek.AccountMark("Student01");
                var exam = Make("2026-12-15 13:00", "Алгебра"); var midterm = Make("2026-12-16 09:30", "Физика", ExamKind.Midterm);
                var plan = new ExamPlan(mark, Now, new[] { midterm, exam }, new[] { "e|" + exam.Key, "e|gone" });
                ExamFile.Write(root, plan);
                var text = File.ReadAllText(Path.Combine(root, ExamFile.Name));
                Assert.DoesNotContain("Student01", text, StringComparison.OrdinalIgnoreCase);
                var back = ExamFile.Read(root, mark);
                Assert.True(plan.SameAs(back));
                Assert.Equal(new[] { "Алгебра", "Физика" }, back.Items.Select(i => i.Subject).ToArray());
                Assert.Equal(new[] { "e|" + exam.Key }, back.Announced.ToArray());
                Assert.Null(ExamFile.Read(root, ScheduleWeek.AccountMark("student02")));
                File.WriteAllText(Path.Combine(root, ExamFile.Name), text.Replace("\"version\":1", "\"version\":2"));
                Assert.Null(ExamFile.Read(root, mark));
                File.WriteAllText(Path.Combine(root, ExamFile.Name), text.Replace("2026-12-15T13:00", "bad"));
                Assert.Null(ExamFile.Read(root, mark));
                ExamFile.Write(root, plan);
                ExamFile.Remove(root);
                Assert.Null(ExamFile.Read(root, mark));
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void TheSameExamsInAnotherLanguageKeepWhatWasAnnounced()
        {
            var ru = new ExamItem { Kind = ExamKind.Exam, Subject = "Алгебра", Start = new DateTime(2026, 12, 15, 13, 0, 0), Place = "ФИТ · 309" };
            var plan = Plan(ru);
            var evening = Assert.Single(plan.Due(new DateTime(2026, 12, 14, 19, 0, 0), 30));
            plan = plan.WithAnnounced(new[] { evening.Key });
            // The site words the subject and the room in the page's language; the exam is the same one.
            var en = new ExamItem { Kind = ExamKind.Exam, Subject = "Algebra", Start = ru.Start, Place = "IT · 309" };
            var other = plan.Replace(ExamKind.Exam, new[] { en }, new DateTime(2026, 12, 14, 19, 5, 0));
            Assert.Equal(new[] { evening.Key }, other.Announced.ToArray());
            Assert.Empty(other.Due(new DateTime(2026, 12, 14, 19, 10, 0), 30));
            Assert.Equal(ru.Key, en.Key);
            Assert.DoesNotContain("Алгебра", ru.Key);
        }

        [Fact]
        public void AnnouncedKeysOfTheOlderFormWithTheSubjectAreDroppedOnLoad()
        {
            var exam = Make("2026-12-15 10:00", "Алгебра");
            var plan = new ExamPlan("acc", Now, new[] { exam }, new[] { "e|2026-12-15T10:00:00|Exam|Алгебра", "e|" + exam.Key });
            Assert.Equal(new[] { "e|" + exam.Key }, plan.Announced.ToArray());
        }

        private static List<ScheduleClass> ClassAt(DateTime start, string name, string room = "305")
        {
            return new List<ScheduleClass> { new ScheduleClass { Start = start, End = start.AddMinutes(50), Name = name, Type = "", Room = room, Teacher = "" } };
        }
        private static List<ExamReminder> ExamDue(ExamItem item, DateTime now, int lead = 15) { return Plan(item).Due(now, lead); }

        [Fact]
        public void ACombinedBalloonTakesTheNearerOnesTitleAndListsClassesThenExams()
        {
            var now = new DateTime(2026, 12, 15, 12, 40, 0);
            var classes = ClassAt(now.AddMinutes(10), "Политология");
            var exams = ExamDue(new ExamItem { Kind = ExamKind.Exam, Subject = "Алгебра", Start = now.AddMinutes(14), Place = "309" }, now);
            Assert.Equal("Пара через 10 мин", CombinedReminder.Title(classes, exams, now, "ru", false));
            var body = CombinedReminder.Body(classes, exams, "ru", false).Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(new[] { "Политология · ауд. 305 · начало в 12:50", "Алгебра · ауд. 309 · начало в 12:54" }, body);
            // An exam that starts before the class gives its title.
            var early = ExamDue(new ExamItem { Kind = ExamKind.Exam, Subject = "Алгебра", Start = now.AddMinutes(5), Place = "" }, now);
            Assert.Equal("Через 5 мин экзамен", CombinedReminder.Title(classes, early, now, "ru", false));
        }

        [Fact]
        public void AnEveningExamNeverTakesTheTitleFromAClassButSaysTomorrow()
        {
            var now = new DateTime(2026, 12, 14, 19, 5, 0);
            var classes = ClassAt(now.AddMinutes(10), "Политология", "");
            var exams = Plan(new ExamItem { Kind = ExamKind.Midterm, Subject = "Физика", Start = new DateTime(2026, 12, 15, 9, 0, 0), Place = "" }).Due(now, 15);
            Assert.True(exams[0].Evening);
            Assert.Equal("Class in 10 min", CombinedReminder.Title(classes, exams, now, "en", false));
            var body = CombinedReminder.Body(classes, exams, "en", false);
            Assert.Contains("Tomorrow: midterm: Физика · starts at 09:00", body);
        }

        [Fact]
        public void BehindThePinAnEveningExamInACombinedBalloonStillSaysTomorrow()
        {
            var now = new DateTime(2026, 12, 14, 19, 5, 0);
            var classes = ClassAt(now.AddMinutes(10), "Политология", "");
            var plan = Plan(new ExamItem { Kind = ExamKind.Exam, Subject = "Физика", Start = new DateTime(2026, 12, 15, 9, 0, 0), Place = "" },
                new ExamItem { Kind = ExamKind.Exam, Subject = "Химия", Start = new DateTime(2026, 12, 15, 9, 0, 0), Place = "" });
            var body = CombinedReminder.Body(classes, plan.Due(now, 15), "ru", true).Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal(new[] { "Пара в 19:15", "Завтра экзамен · начало в 09:00" }, body);
        }

        [Fact]
        public void ACombinedBalloonNamesTwoOfEachAndOnlyThoseCountAsAnnounced()
        {
            var now = new DateTime(2026, 12, 14, 19, 5, 0);
            var start = new DateTime(2026, 12, 15, 9, 0, 0);
            var exams = Plan(new ExamItem { Kind = ExamKind.Exam, Subject = "Физика", Start = start, Place = "" },
                new ExamItem { Kind = ExamKind.Exam, Subject = "Химия", Start = start.AddHours(2), Place = "" },
                new ExamItem { Kind = ExamKind.Exam, Subject = "История", Start = start.AddHours(4), Place = "" }).Due(now, 15);
            var classes = ClassAt(now.AddMinutes(10), "A").Concat(ClassAt(now.AddMinutes(10), "B")).Concat(ClassAt(now.AddMinutes(10), "C")).ToList();
            Assert.Equal(3, exams.Count);
            var named = CombinedReminder.NamedExams(exams); var namedClasses = CombinedReminder.NamedClasses(classes, false);
            Assert.Equal(2, named.Count); Assert.Equal(2, namedClasses.Count);
            var body = CombinedReminder.Body(namedClasses, named, "ru", false);
            Assert.Contains("Физика", body); Assert.Contains("Химия", body); Assert.DoesNotContain("История", body);
            Assert.Equal(3, CombinedReminder.NamedClasses(classes, true).Count);
        }

        [Fact]
        public void BehindThePinACombinedBalloonNamesNoClassOrSubjectOrRoomOnlyKindsAndTimes()
        {
            var now = new DateTime(2026, 12, 15, 12, 40, 0);
            var classes = ClassAt(now.AddMinutes(10), "Политология", "305");
            var exams = ExamDue(new ExamItem { Kind = ExamKind.Exam, Subject = "Секретный", Start = now.AddMinutes(14), Place = "777" }, now);
            foreach (var lang in new[] { "ru", "kz", "en" })
            {
                var text = CombinedReminder.Title(classes, exams, now, lang, true) + Environment.NewLine + CombinedReminder.Body(classes, exams, lang, true);
                Assert.DoesNotContain("Политология", text); Assert.DoesNotContain("Секретный", text);
                Assert.DoesNotContain("305", text); Assert.DoesNotContain("777", text);
                Assert.Contains("12:50", text); Assert.Contains("12:54", text);
            }
            var examFirst = ExamDue(new ExamItem { Kind = ExamKind.Midterm, Subject = "Секретный", Start = now.AddMinutes(5), Place = "777" }, now);
            Assert.Equal("Soon: midterm", CombinedReminder.Title(classes, examFirst, now, "en", true));
        }

        [Fact]
        public void ACombinedBalloonStaysInsideWindowsLimits()
        {
            var now = new DateTime(2026, 12, 15, 12, 40, 0);
            var classes = ClassAt(now.AddMinutes(10), new string('а', 400), new string('7', 300));
            classes.AddRange(ClassAt(now.AddMinutes(10), new string('б', 400)));
            var exams = ExamDue(new ExamItem { Kind = ExamKind.Midterm, Subject = new string('в', 200), Start = now.AddMinutes(5), Place = new string('8', 200) }, now);
            foreach (var lang in new[] { "ru", "kz", "en" })
                foreach (var hidden in new[] { false, true })
                {
                    Assert.InRange(CombinedReminder.Title(classes, exams, now, lang, hidden).Length, 1, CombinedReminder.TitleLimit);
                    Assert.InRange(CombinedReminder.Body(classes, exams, lang, hidden).Length, 1, CombinedReminder.BodyLimit);
                }
            Assert.Equal(63, CombinedReminder.TitleLimit); Assert.Equal(255, CombinedReminder.BodyLimit);
        }
    }
}
