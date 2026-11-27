using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class GradeWatchTests
    {
        private const string Account = "0123456789abcdef";
        private static readonly DateTime At = new DateTime(2026, 11, 20, 9, 30, 0);
        // «Текущая аттестация» as observed (review capture of 29 October 2026) with synthetic values: layout tables around
        // the nine-column grades table, its GPA/credits row across eight columns, a script and a comment on the page.
        private static string Page(string rows = null, string head = null)
        {
            rows = rows ?? Row("Иностранный язык", "5", "99", "0", "0", "30", "0", "F", "неуд.") + Row("Алгоритмы", "5", "98", "96", "92", "94", "4", "A", "отл.");
            head = head ?? "<tr><th width=\"30%\">Дисциплина</th><th>Кред.</th><th>РК1</th><th>РК2</th><th>ПА*</th><th>Сумма</th><th>GPA</th><th>Букв.</th><th>Значение</th></tr>";
            return "<!doctype html><html><head><script>var t = '<table><tr><th>РК1</th></tr>';</script><style>td{color:red}</style></head><body>" +
                "<table class=\"inner\"><tr><td class=\"left\"></td><td class=\"center\"><table class=\"mt\"><tr class=\"top\"><td class=\"lt\"></td><td class=\"ct\">Аттестация студента</td><td class=\"rt\"></td></tr>" +
                "<tr class=\"mid\"><td class=\"lt\"></td><td class=\"tt\"><!-- <table><tr><th>Сумма</th></tr></table> --><table class=\"inner\" id=\"grades\">" + head + rows +
                "<tr><td>GPA(2026-2027 уч. год)- 0</td><td colspan=\"8\">Освоено кредитов (2026-2027 уч. год) - 0</td></tr></table></td><td class=\"rt\"></td></tr></table></td><td class=\"right\"></td></tr></table></body></html>";
        }
        private static string Row(params string[] cells) { return "<tr>" + string.Concat(cells.Select(c => "<td>" + c + "</td>")) + "</tr>"; }
        private static GradeSheet Sheet(string html) { return GradeSheet.Parse(html, Account, At); }

        [Fact]
        public void TheGradesTableIsReadWithoutItsTotalRowScriptsOrComments()
        {
            var sheet = Sheet(Page());
            Assert.NotNull(sheet);
            Assert.Equal(new[] { "Дисциплина", "Кред.", "РК1", "РК2", "ПА*", "Сумма", "GPA", "Букв.", "Значение" }, sheet.Headings);
            Assert.Equal(new[] { "Иностранный язык", "Алгоритмы" }, sheet.Rows.Select(r => r.Course));
            Assert.Equal(new[] { "5", "98", "96", "92", "94", "4", "A", "отл." }, sheet.Rows[1].Values);
        }

        [Fact]
        public void TheShortOctoberTableIsReadToo()
        {
            var head = "<tr><th>Дисциплина</th><th>Кред.</th><th>Сумма</th><th>GPA</th><th>Букв.</th><th>Значение</th></tr>";
            var sheet = Sheet(Page(Row("Алгоритмы", "5", "94", "4", "A", "Отлично"), head));
            Assert.Equal(6, sheet.Headings.Count);
            Assert.Single(sheet.Rows);
        }

        [Theory]
        [InlineData("<html><body><form id=\"login_frm\"><input type=\"text\"></form></body></html>")]
        [InlineData("<html><body><table><tr><td>Нет данных</td></tr></table></body></html>")]
        [InlineData("<table><tr><th>Дисциплина</th><th>Кред.</th><th>GPA</th><th>Букв.</th></tr><tr><td>A</td><td>5</td><td>4</td><td>A</td></tr></table>")]
        [InlineData("")]
        public void APageWithoutAGradesTableIsNoReading(string html) { Assert.Null(Sheet(html)); }

        [Fact]
        public void WithoutTheLoginsMarkNothingIsReadOrLoaded()
        {
            Assert.Null(GradeSheet.Parse(Page(), "", At));
            Assert.Null(GradeSheet.Parse(Page(), null, At));
            Assert.Null(GradeSheet.Load(Sheet(Page()).Serialize(), ""));
        }

        // Heading names of the site's Kazakh and English pages as study-pages.js knows them; the live pages were not read.
        [Theory]
        [InlineData("Пән", "Кредит", "АБ1", "АБ2", "Емт*", "Жалпы балл")]
        [InlineData("Discipline", "Credits", "MT1", "MT2", "Exam*", "Total score")]
        [InlineData("Дисциплина", "Кред.", "РА1", "БК", "Экз.", "Балл")]
        public void TheSitesOtherLanguagesNameTheirScoreColumnsToo(string course, string credits, string first, string second, string exam, string total)
        {
            var head = "<tr><th>" + course + "</th><th>" + credits + "</th><th>" + first + "</th><th>" + second + "</th><th>" + exam + "</th><th>" + total + "</th><th>GPA</th><th>Letter</th><th>Meaning</th></tr>";
            var before = Sheet(Page(head: head));
            var now = Sheet(Page(Row("Иностранный язык", "5", "99", "71", "0", "52", "1.33", "D+", "удовл.") + Row("Алгоритмы", "5", "98", "96", "92", "94", "4", "A", "отл."), head));
            var change = Assert.Single(GradeSheet.Changes(before, now));
            Assert.Equal(new[] { second + " 71", total + " 52" }, change.Cells);
            Assert.False(GradeSheet.Score(credits) || GradeSheet.Score("GPA") || GradeSheet.Score("Letter") || GradeSheet.Score("Meaning"));
        }

        [Fact]
        public void BigMenuTablesAndManyLayoutTablesDoNotHideTheGrades()
        {
            var menu = "<table>" + string.Concat(Enumerable.Repeat("<tr><td>пункт</td></tr>", HtmlTables.MaxRows + 10)) + "</table>" +
                "<table><tr>" + string.Concat(Enumerable.Repeat("<td>x</td>", HtmlTables.MaxCells + 5)) + "</tr></table>" +
                string.Concat(Enumerable.Repeat("<table><tr><td>a</td></tr></table>", 200));
            var sheet = Sheet(Page().Replace("<body>", "<body>" + menu));
            Assert.NotNull(sheet);
            Assert.Equal(new[] { "Иностранный язык", "Алгоритмы" }, sheet.Rows.Select(r => r.Course));
        }

        [Fact]
        public void AChangedScoreIsNewsAndTheOtherColumnsAreNot()
        {
            var before = Sheet(Page());
            var now = Sheet(Page(Row("Иностранный язык", "5", "99", "71", "0", "52", "1.33", "D+", "удовл.") + Row("Алгоритмы", "5", "98", "96", "92", "94", "4", "A", "отл.")));
            var changes = GradeSheet.Changes(before, now);
            var change = Assert.Single(changes);
            Assert.Equal("Иностранный язык", change.Course);
            Assert.Equal(new[] { "РК2 71", "Сумма 52" }, change.Cells);
        }

        [Fact]
        public void TheFirstReadingAnotherSemesterLanguageOrAccountSayNothing()
        {
            var before = Sheet(Page());
            Assert.Empty(GradeSheet.Changes(null, before));
            var differentSemester = Sheet(Page(Row("Calculus", "5", "98", "100", "92", "95", "4", "A", "отл.")));
            Assert.Empty(GradeSheet.Changes(before, differentSemester));
            var english = Sheet(Page(head: "<tr><th>Discipline</th><th>Cred.</th><th>RK1</th><th>RK2</th><th>PA*</th><th>Sum</th><th>GPA</th><th>Letter</th><th>Meaning</th></tr>"));
            Assert.Empty(GradeSheet.Changes(before, english));
            var other = GradeSheet.Parse(Page(), "fedcba9876543210", At);
            Assert.Empty(GradeSheet.Changes(before, other));
        }

        private const string SimpleHead = "<tr><th>Discipline</th><th>Credits</th><th>MT1</th><th>Exam</th><th>Total</th></tr>";

        [Fact]
        public void ANewSemesterWithOneSharedCourseNameIsQuiet()
        {
            var before = Sheet(Page(Row("Physics", "5", "90", "", "32") + Row("History", "5", "82", "", "28") +
                Row("Literature", "5", "70", "", "25"), SimpleHead));
            var now = Sheet(Page(Row("Physics", "5", "0", "0", "0") + Row("Calculus", "5", "85", "0", "29") +
                Row("Geography", "5", "0", "0", "0"), SimpleHead));
            Assert.Empty(GradeSheet.Changes(before, now));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("0,00")]
        [InlineData("0.0")]
        public void PlaceholderZerosOnNewCoursesAreQuiet(string zero)
        {
            var physics = Row("Physics", "5", "90", "", "32");
            var before = Sheet(Page(physics, SimpleHead));
            var now = Sheet(Page(physics + Row("History", "5", zero, zero, zero) +
                Row("Art", "5", zero, "85", zero), SimpleHead));
            var change = Assert.Single(GradeSheet.Changes(before, now));
            Assert.Equal("Art", change.Course);
            Assert.Equal(new[] { "Exam 85" }, change.Cells);
        }

        [Fact]
        public void ChangedScoresSurviveCoursesAndColumnsMovingAppearingOrDisappearing()
        {
            var before = Sheet(Page(Row("Math", "5", "70", "", "25") + Row("History", "5", "82", "", "28") +
                Row("Physics", "5", "90", "", "32"), SimpleHead));
            var revisedHead = "<tr><th>Discipline</th><th>Total</th><th>MT2</th><th>Credits</th><th>MT1</th></tr>";
            var now = Sheet(Page(Row("Physics", "32", "", "5", "90") + Row("Art", "0", "80", "4", "") +
                Row("Math", "25", "75", "5", "72"), revisedHead));
            var changes = GradeSheet.Changes(before, now);
            Assert.Equal(new[] { "Art", "Math" }, changes.Select(c => c.Course));
            Assert.Equal(new[] { "MT2 80" }, changes[0].Cells);
            Assert.Equal(new[] { "MT2 75", "MT1 72" }, changes[1].Cells);
        }

        [Fact]
        public void AmbiguousRepeatedNamesDoNotHideChangesInUnambiguousCourses()
        {
            var before = Sheet(Page(Row("Math", "5", "60", "", "") + Row("Math", "3", "70", "", "") +
                Row("History", "5", "80", "", ""), SimpleHead));
            var now = Sheet(Page(Row("Math", "3", "70", "", "") + Row("History", "5", "81", "", "") +
                Row("Math", "5", "60", "", ""), SimpleHead));
            var change = Assert.Single(GradeSheet.Changes(before, now));
            Assert.Equal("History", change.Course);
            Assert.Equal(new[] { "MT1 81" }, change.Cells);
        }

        [Fact]
        public void ARepeatedScoreHeadingDoesNotHideOtherColumnsOrInventChanges()
        {
            var head = SimpleHead.Replace("<th>Total", "<th>MT1</th><th>Total");
            var before = Sheet(Page(Row("Math", "5", "70", "", "72", "25"), head));
            var now = Sheet(Page(Row("Math", "5", "72", "80", "70", "25"), head));
            Assert.Equal(new[] { "Exam 80" }, Assert.Single(GradeSheet.Changes(before, now)).Cells);
        }

        [Theory]
        [InlineData("Exam", "Total")]
        [InlineData("ПА*", "Сумма")]
        [InlineData("Емт*", "Жалпы балл")]
        public void InitialFinalZerosAreQuietButControlZerosAndCorrectionsCount(string exam, string total)
        {
            var head = SimpleHead.Replace(">Exam<", ">" + exam + "<").Replace(">Total<", ">" + total + "<");
            var before = Sheet(Page(Row("Math", "5", "", "", "") + Row("History", "5", "80", "70", "60"), head));
            var now = Sheet(Page(Row("Math", "5", "0", "0,00", "0.0") + Row("History", "5", "80", "0", "60"), head));
            var changes = GradeSheet.Changes(before, now);
            Assert.Equal(new[] { "MT1 0" }, changes[0].Cells);
            Assert.Equal(new[] { exam.TrimEnd('*') + " 0" }, changes[1].Cells);
        }

        [Theory]
        [InlineData("<tr><td>Math</td><td colspan='2'>5</td><td>70</td><td>65</td></tr>")]
        [InlineData("<tr><td>Math</td><td>5</td><td>70</td><td>65</td></tr>")]
        [InlineData("<tr><th>GPA(2026-2027)- 0</th><th colspan='4'>Credits - 0</th></tr>")]
        public void ARowThatIsNotACourseDoesNotStopTheReading(string irregular)
        {
            // The live page ends with a total row made of heading cells: it must never turn the reading off.
            var history = Row("History", "5", "80", "", "");
            Assert.Equal("History", Assert.Single(Sheet(Page(history + irregular, SimpleHead)).Rows).Course);
        }

        [Fact]
        public void ATermThatKeepsEveryCourseNameButResetsTheScoresIsQuiet()
        {
            var before = Sheet(Page(Row("Language", "5", "90", "80", "85") + Row("Sport", "2", "70", "", "28"), SimpleHead));
            var now = Sheet(Page(Row("Language", "5", "0", "0", "0") + Row("Sport", "2", "0", "0", "0"), SimpleHead));
            Assert.Empty(GradeSheet.Changes(before, now));
        }

        [Fact]
        public void AFailedReadingRetriesInOneThenFiveMinutesBeforeReturningToTheInterval()
        {
            var watch = new GradeWatch();
            Assert.Equal(60000L, watch.NextDelay(15, false));
            Assert.Equal(300000L, watch.NextDelay(15, false));
            for (var i = 0; i < 5; i++) Assert.Equal(900000L, watch.NextDelay(15, false));
            Assert.Equal(900000L, watch.NextDelay(15, true));
            Assert.Equal(60000L, watch.NextDelay(15, false));
        }

        [Fact]
        public async Task ALoginMarkRecoveredAfterStartupLoadsItsKeptReadingBeforeComparing()
        {
            var before = Sheet(Page(Row("Math", "5", "70", "", "25"), SimpleHead));
            var after = Sheet(Page(Row("Math", "5", "80", "", "25"), SimpleHead));
            var watch = new GradeWatch();
            var raw = before.Serialize();
            var requested = new List<string>();
            Func<string, GradeSheet> load = account => { requested.Add(account); return GradeSheet.Load(raw, account); };
            watch.LoadBaseline("", load);
            watch.LoadBaseline(Account, load);
            var announced = new List<GradeChange>();
            Assert.True(await watch.RecordAsync(after, changes => { announced.AddRange(changes); return true; }, sheet => Task.CompletedTask));
            Assert.Equal(new[] { "MT1 80" }, Assert.Single(announced).Cells);
            watch.LoadBaseline(Account, load);
            Assert.Equal(Account, Assert.Single(requested));
            Assert.Same(after, watch.Baseline);
        }

        [Fact]
        public async Task LoadingADifferentAccountDiscardsThePreviousAccountsPendingReading()
        {
            const string otherAccount = "fedcba9876543210";
            var before = Sheet(Page(Row("Math", "5", "70", "", "25"), SimpleHead));
            var after = Sheet(Page(Row("Math", "5", "80", "", "25"), SimpleHead));
            var other = GradeSheet.Parse(Page(Row("Math", "5", "95", "", ""), SimpleHead), otherAccount, At);
            var watch = new GradeWatch();
            watch.LoadBaseline(Account, account => before);
            Assert.False(await watch.RecordAsync(after, changes => false, sheet => Task.CompletedTask));
            watch.LoadBaseline(otherAccount, account => other);
            Assert.Same(other, watch.Baseline);
            Assert.False(watch.HasPending);
        }

        [Fact]
        public async Task WithoutAnAvailableBalloonTheReadingWaitsToBeAnnouncedAndSaved()
        {
            var before = Sheet(Page(Row("Math", "5", "70", "", "25"), SimpleHead));
            var after = Sheet(Page(Row("Math", "5", "80", "", "25"), SimpleHead));
            var watch = new GradeWatch();
            watch.LoadBaseline(Account, account => before);
            var saved = new List<GradeSheet>();
            Func<GradeSheet, Task> save = sheet => { saved.Add(sheet); return Task.CompletedTask; };

            Assert.False(await watch.RecordAsync(after, changes => false, save));
            Assert.Same(before, watch.Baseline);
            Assert.Empty(saved);

            Assert.True(await watch.RecordAsync(null, changes =>
            {
                Assert.Equal(new[] { "MT1 80" }, Assert.Single(changes).Cells);
                return true;
            }, save));
            Assert.Same(after, Assert.Single(saved));
            Assert.Same(after, watch.Baseline);
        }

        [Fact]
        public async Task ASaveFailureRetriesTheSameReadingWithoutAnnouncingTwice()
        {
            var before = Sheet(Page(Row("Math", "5", "70", "", "25"), SimpleHead));
            var after = Sheet(Page(Row("Math", "5", "80", "", "25"), SimpleHead));
            var watch = new GradeWatch();
            watch.LoadBaseline(Account, account => before);
            var announcements = 0;
            Func<List<GradeChange>, bool> announce = changes => { announcements++; return true; };

            Assert.False(await watch.RecordAsync(after, announce, sheet => Task.FromException(new IOException())));
            Assert.Equal(1, announcements);
            Assert.Same(after, watch.Baseline);

            Assert.True(await watch.RecordAsync(null, announce, sheet =>
            {
                Assert.Same(after, sheet);
                return Task.CompletedTask;
            }));
            Assert.Equal(1, announcements);
        }

        [Fact]
        public async Task MoreGradesCanBeAnnouncedWhileSavingIsUnavailable()
        {
            var before = Sheet(Page(Row("Math", "5", "70", "", "25"), SimpleHead));
            var first = Sheet(Page(Row("Math", "5", "80", "", "25"), SimpleHead));
            var second = Sheet(Page(Row("Math", "5", "90", "", "25"), SimpleHead));
            var watch = new GradeWatch();
            watch.LoadBaseline(Account, account => before);
            var announced = new List<string>();
            Func<List<GradeChange>, bool> announce = changes =>
            {
                announced.Add(Assert.Single(Assert.Single(changes).Cells));
                return true;
            };
            Func<GradeSheet, Task> fail = sheet => Task.FromException(new IOException());

            Assert.False(await watch.RecordAsync(first, announce, fail));
            Assert.False(await watch.RecordAsync(second, announce, fail));
            Assert.Equal(new[] { "MT1 80", "MT1 90" }, announced);
            Assert.True(await watch.RecordAsync(null, announce, sheet =>
            {
                Assert.Same(second, sheet);
                return Task.CompletedTask;
            }));
            Assert.Equal(2, announced.Count);
        }

        [Fact]
        public void ACellThatBecameEmptyIsNotNews()
        {
            var before = Sheet(Page());
            var now = Sheet(Page(Row("Иностранный язык", "5", "", "0", "0", "30", "0", "F", "неуд.") + Row("Алгоритмы", "5", "98", "96", "92", "94", "4", "A", "отл.")));
            Assert.Empty(GradeSheet.Changes(before, now));
        }

        [Fact]
        public void TheKeptReadingRoundTripsAndRejectsAnythingElse()
        {
            var sheet = Sheet(Page());
            var raw = sheet.Serialize();
            Assert.DoesNotContain("<", raw);
            var loaded = GradeSheet.Load(raw, Account);
            Assert.NotNull(loaded);
            Assert.Equal(sheet.Headings, loaded.Headings);
            Assert.Empty(GradeSheet.Changes(sheet, loaded));
            Assert.Null(GradeSheet.Load(raw, "fedcba9876543210"));
            Assert.Null(GradeSheet.Load(raw.Replace("\"version\":1", "\"version\":2"), Account));
            Assert.Null(GradeSheet.Load(raw.Replace("\"5\"", "5"), Account));
            Assert.Null(GradeSheet.Load("{", Account));
            Assert.Null(GradeSheet.Load(new string('x', 200001), Account));
        }

        [Fact]
        public void TheBalloonNamesThreeCoursesCountsTheRestAndHidesThemBehindAPin()
        {
            var changes = Enumerable.Range(1, 5).Select(i => new GradeChange { Course = "Курс " + i, Cells = new List<string> { "РК1 9" + i } }).ToList();
            Assert.Equal("Новые оценки", GradeNotice.Title(changes, "ru"));
            Assert.Equal("New grade", GradeNotice.Title(changes.Take(1).ToList(), "en"));
            var body = GradeNotice.Body(changes, "ru", false);
            Assert.Equal(string.Join(Environment.NewLine, "Курс 1: РК1 91", "Курс 2: РК1 92", "Курс 3: РК1 93", "и ещё 2"), body);
            var locked = GradeNotice.Body(changes, "kz", true);
            Assert.DoesNotContain("Курс", locked);
            var long1 = new List<GradeChange> { new GradeChange { Course = new string('я', 300), Cells = new List<string> { "РК1 90" } } };
            Assert.True(GradeNotice.Body(long1, "ru", false).Length <= 250);
        }

        [Theory]
        [InlineData(15, 15)]
        [InlineData(120, 120)]
        [InlineData(0, 30)]
        [InlineData(7, 30)]
        public void OnlyTheOfferedIntervalsAreKept(int asked, int kept) { Assert.Equal(kept, GradeNotice.Interval(asked)); }

        [Fact]
        public void APageOfManyUnclosedOpenersEndsWithinTheBudget()
        {
            var html = string.Concat(Enumerable.Repeat("<!--<script>", 160000)).Substring(0, 1900000);
            var clock = Stopwatch.StartNew();
            Assert.Null(Sheet(html));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), clock.Elapsed.ToString());
        }

        [Fact]
        public void TablesBeyondTheLimitsAreNoReading()
        {
            Assert.Null(Sheet(new string(' ', HtmlTables.MaxLength + 1) + Page()));
            Assert.Null(Sheet(Page(string.Concat(Enumerable.Repeat(Row("Курс", "5", "1", "2", "3", "4", "0", "F", "x"), 401)))));
            var wide = "<tr>" + string.Concat(Enumerable.Range(0, 17).Select(i => "<th>РК" + i + "</th>")) + "</tr>";
            Assert.Null(Sheet(Page(Row("Курс", "5"), wide)));
            Assert.Null(Sheet(Page(string.Concat(Enumerable.Range(0, 61).Select(i => Row("Курс " + i, "5", "1", "2", "3", "4", "0", "F", "x"))))));
            Assert.Equal(GradeSheet.MaxText, Sheet(Page(Row(new string('к', 500), "5", "1", "2", "3", "4", "0", "F", "x"))).Rows[0].Course.Length);
        }

        // The reading over HTTP: asked addresses and answers, by a fake transport.
        private sealed class Fake
        {
            public readonly List<string> Asked = new List<string>();
            public Func<string, HttpResponseMessage> Answer;
            public Task<HttpResponseMessage> GetAsync(string url, CancellationToken token) { Asked.Add(url); return Task.FromResult(Answer(url)); }
        }
        private static HttpResponseMessage Html(string body, string type = "text/html", string charset = "utf-8")
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(type) { CharSet = charset };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
        private static HttpResponseMessage Redirect(string location) { var r = new HttpResponseMessage(HttpStatusCode.Found) { Content = new ByteArrayContent(new byte[0]) }; r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute); return r; }
        private const string Url = "https://univer.kaznu.kz/student/attestation/";

        [Fact]
        public async Task TheAttestationPageIsRead()
        {
            var fake = new Fake { Answer = url => Html(Page()) };
            var html = await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None);
            Assert.NotNull(Sheet(html));
            Assert.Equal(new[] { Url }, fake.Asked);
        }

        [Fact]
        public async Task ARedirectToTheSignInPageIsNeverFollowed()
        {
            var fake = new Fake { Answer = url => Redirect("/user/login?return=/student/attestation/") };
            Assert.Null(await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None));
            Assert.Equal(new[] { Url }, fake.Asked);
            fake = new Fake { Answer = url => Redirect("https://example.com/student/attestation/") };
            Assert.Null(await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None));
            Assert.Single(fake.Asked);
            fake = new Fake { Answer = url => Redirect("/student/attentionStudents/12/") };
            Assert.Null(await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None));
            Assert.Single(fake.Asked);
        }

        [Fact]
        public async Task RedirectsWithinTheSiteStopAtTheLimit()
        {
            var fake = new Fake { Answer = url => Redirect(url + "x/") };
            Assert.Null(await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None));
            Assert.Equal(GradeFetch.RedirectLimit + 1, fake.Asked.Count);
            fake = new Fake { Answer = url => url == Url ? Redirect("/student/attestation/2026/1/") : Html(Page()) };
            Assert.NotNull(await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None));
        }

        [Fact]
        public async Task ASignInPageAtTheAskedAddressIsNoReading()
        {
            var fake = new Fake { Answer = url => Html("<!doctype html><html><body><form id=\"login_frm\" method=\"post\"></form></body></html>") };
            Assert.Null(await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None));
        }

        [Fact]
        public async Task AnythingButAPlainHtmlPageIsNoReading()
        {
            Assert.Null(await GradeFetch.ReadAsync(new Fake { Answer = url => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new ByteArrayContent(new byte[0]) } }.GetAsync, Url, CancellationToken.None));
            Assert.Null(await GradeFetch.ReadAsync(new Fake { Answer = url => Html("{}", "application/json") }.GetAsync, Url, CancellationToken.None));
            Assert.Null(await GradeFetch.ReadAsync(new Fake { Answer = url => { var r = Html(Page()); r.Content.Headers.ContentEncoding.Add("gzip"); return r; } }.GetAsync, Url, CancellationToken.None));
            // Too long, by the body itself when the length is not announced.
            Assert.Null(await GradeFetch.ReadAsync(new Fake { Answer = url => { var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new System.IO.MemoryStream(new byte[GradeFetch.SizeLimit + 1])) }; r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html"); return r; } }.GetAsync, Url, CancellationToken.None));
            Assert.Null(await GradeFetch.ReadAsync(new Fake { Answer = url => Html(Page()) }.GetAsync, "https://example.com/student/attestation/", CancellationToken.None));
        }

        [Fact]
        public async Task ThePageIsDecodedByItsCharset()
        {
            var bytes = Encoding.GetEncoding(1251).GetBytes(Page());
            var fake = new Fake { Answer = url => { var c = new ByteArrayContent(bytes); c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") { CharSet = "windows-1251" }; return new HttpResponseMessage(HttpStatusCode.OK) { Content = c }; } };
            var sheet = Sheet(await GradeFetch.ReadAsync(fake.GetAsync, Url, CancellationToken.None));
            Assert.Equal("Алгоритмы", sheet.Rows[1].Course);
        }
    }
}
