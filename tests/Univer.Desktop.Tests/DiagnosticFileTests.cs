using System;
using System.Collections.Generic;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class DiagnosticFileTests
    {
        private static readonly string[] Secrets = { "student.login", "User", "DESKTOP-QA7", @"C:\Users\User" };
        private static string Clean(string text) { return DiagnosticFile.Clean(text, Secrets); }

        [Theory]
        [InlineData("signed in as student.login", "signed in as <hidden>")]
        [InlineData("STUDENT.LOGIN failed", "<hidden> failed")]
        [InlineData(@"saved to C:\Users\User\Downloads\a.pdf", @"saved to <hidden>\Downloads\a.pdf")]
        [InlineData(@"saved to D:\Users\Aigerim\Desktop", @"saved to D:\Users\<user>\Desktop")]
        [InlineData("computer DESKTOP-QA7 is slow", "computer <hidden> is slow")]
        [InlineData("write to aigerim.n@live.kaznu.kz please", "write to <e-mail> please")]
        [InlineData("IIN 870112345678", "IIN <number>")]
        [InlineData("call +7 (701) 123-45-67", "call <number>")]
        [InlineData("cookie eyJhbGciOiJIUzI1NiJ9abcdefghijkl", "cookie <token>")]
        public void PersonalDetailsAreRemoved(string text, string expected) { Assert.Equal(expected, Clean(text)); }

        [Theory]
        [InlineData("Users folder and the UserProfile", "Users folder and the UserProfile")]
        [InlineData("App: 0.17.0 Modern x64, installed", "App: 0.17.0 Modern x64, installed")]
        [InlineData("Windows: Microsoft Windows NT 10.0.26200.0, 64-bit", "Windows: Microsoft Windows NT 10.0.26200.0, 64-bit")]
        [InlineData("Display: 2 screen(s), primary 1920x1080, scale 125%", "Display: 2 screen(s), primary 1920x1080, scale 125%")]
        [InlineData("status ProxyConnectionFailed", "status ProxyConnectionFailed")]
        public void TheAppsOwnFactsStay(string text, string expected) { Assert.Equal(expected, Clean(text)); }

        [Theory]
        [InlineData("https://univer.kaznu.kz/student/attentionStudents/12345/?token=abc#top", "https://univer.kaznu.kz/student/attentionStudents/{n}/")]
        [InlineData("https://user:secret@univer.kaznu.kz/student/myschedule/", "https://univer.kaznu.kz/student/myschedule/")]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/88123/4412/2026/1", "https://univer.kaznu.kz/student/umkd/get/{n}/{n}/{n}/{n}")]
        [InlineData("about:blank", "about:blank")]
        public void AnAddressKeepsOnlyItsShape(string url, string expected)
        {
            Assert.Equal(expected, DiagnosticFile.Address(url));
            Assert.Equal("page " + expected, Clean("page " + url));
        }

        [Fact]
        public void AFileAddressLosesItsUserFolder()
        {
            Assert.Equal("open file:///<file>", Clean("open file:///C:/Users/Aigerim/report.pdf"));
            Assert.Equal("at D:/Users/<user>/x", Clean("at D:/Users/Aigerim/x"));
        }

        [Fact]
        public void ShortSecretsAreIgnoredAndLongLinesCut()
        {
            Assert.Equal("a b c", DiagnosticFile.Clean("a b c", new[] { "a", "b" }));
            var line = DiagnosticFile.Clean(new string('x', 2000) + " end", null);
            Assert.True(line.Length <= DiagnosticFile.MaxLine);
            Assert.Equal("one two", DiagnosticFile.Clean("one\r\n\u2028two", null));
        }

        [Fact]
        public void TheFileHasTheFactsAndTheLastEventsOnlyCleaned()
        {
            var log = new DiagnosticLog(() => Secrets);
            var start = new DateTime(2026, 11, 20, 9, 0, 0);
            for (var i = 0; i < 50; i++) log.Add(start.AddMinutes(i), "status networkError " + i + " student.login");
            log.Add(start.AddMinutes(49).AddSeconds(1), "status networkError 49 student.login");
            Assert.Equal(DiagnosticFile.MaxEvents, log.Items.Count);
            Assert.All(log.Items, e => Assert.DoesNotContain("student.login", e.Text));
            var facts = new[] { new KeyValuePair<string, string>("App", "0.17.0 Modern x64"), new KeyValuePair<string, string>("Shown page", "https://univer.kaznu.kz/student/attestation/?x=student.login") };
            var text = DiagnosticFile.Build(facts, log.Items, Secrets, start);
            Assert.StartsWith("Univer diagnostics 2026-11-20 09:00:00", text);
            Assert.Contains("App: 0.17.0 Modern x64", text);
            Assert.Contains("Shown page: https://univer.kaznu.kz/student/attestation/", text);
            Assert.DoesNotContain("student.login", text);
            Assert.DoesNotContain("status networkError 9 ", text);
            Assert.Contains("2026-11-20 09:49:00 status networkError 49 <hidden>", text);
            Assert.Equal(1, text.Split('\n').Count(l => l.Contains("networkError 49")));
            Assert.Equal("Univer-diagnostics-2026-11-20-0900.txt", DiagnosticFile.FileName(start));
        }
    }
}
