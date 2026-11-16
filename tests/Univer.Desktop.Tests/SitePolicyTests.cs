using System;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class SitePolicyTests
    {
        [Theory]
        [InlineData("https://univer.kaznu.kz/", true)]
        [InlineData("https://univer.kaznu.kz:443/student/transcript", true)]
        [InlineData("http://univer.kaznu.kz/", false)]
        [InlineData("https://univer.kaznu.kz.evil.test/", false)]
        [InlineData("https://user@univer.kaznu.kz/", false)]
        [InlineData("https://univer.kaznu.kz:444/", false)]
        [InlineData("https://evil.test/?site=univer.kaznu.kz", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("file:///c:/test", false)]
        [InlineData(null, false)]
        public void Trusted_ExactOrigin_RejectsLookalikes(string url, bool expected) { Assert.Equal(expected, SitePolicy.Trusted(url)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/myschedule/", true)]
        [InlineData("https://univer.kaznu.kz/student/attendance/full/", true)]
        [InlineData("https://univer.kaznu.kz/news/student/2", true)]
        [InlineData("https://univer.kaznu.kz/", false)]
        [InlineData("https://univer.kaznu.kz/my/news/3", false)]
        [InlineData("https://univer.kaznu.kz/user/login", false)]
        [InlineData("https://univer.kaznu.kz/user/logout", false)]
        [InlineData("https://univer.kaznu.kz/lang/change/kz/", false)]
        [InlineData("https://univer.kaznu.kz/student/attentionStudents/1", false)]
        [InlineData("https://univer.kaznu.kz/student/attention/", false)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", false)]
        [InlineData("https://univer.kaznu.kz/files/plan.pdf", false)]
        [InlineData("https://univer.kaznu.kz/student/transcript/", false)]
        [InlineData("https://evil.test/student/myschedule/", false)]
        [InlineData(null, false)]
        public void Resumable_OnlySectionPagesReopenAfterAutomaticSignIn(string url, bool expected) { Assert.Equal(expected, SitePolicy.Resumable(url)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/user/login", true)]
        [InlineData("https://univer.kaznu.kz/user/login/", true)]
        [InlineData("https://univer.kaznu.kz/user/loginMoodle", false)]
        [InlineData("https://univer.kaznu.kz.evil.test/user/login", false)]
        public void LoginPage_OnlyTheSitesOwnForm(string url, bool expected) { Assert.Equal(expected, SitePolicy.LoginPage(url)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/user/logout", true, false, true)]
        [InlineData("https://univer.kaznu.kz/user/logout/", true, false, true)]
        [InlineData("https://univer.kaznu.kz/user/logout", true, true, false)]
        [InlineData("https://univer.kaznu.kz/user/logout", false, false, false)]
        [InlineData("https://univer.kaznu.kz/user/login", true, false, false)]
        [InlineData("https://evil.test/user/logout", true, false, false)]
        public void UserLogout_OnlyUsersOwnClickPausesSignIn(string url, bool gesture, bool redirect, bool expected)
        { Assert.Equal(expected, SitePolicy.UserLogout(url, gesture, redirect)); }
        [Theory]
        [InlineData("/lang/change/ru/", "ru")]
        [InlineData("/lang/change/kz/", "kz")]
        [InlineData("/lang/change/en/", "en")]
        [InlineData("/lang/change/3/", null)]
        [InlineData("/student/transcript/lang/ru", null)]
        public void Language_OnlyFlagPaths_UpdatePreference(string path, string expected) { Assert.Equal(expected, SitePolicy.Language(SitePolicy.Origin + path)); }
        [Theory]
        [InlineData("/student/iup/1/2", true)]
        [InlineData("/student/transcript", true)]
        [InlineData("/student/recbook/1", true)]
        [InlineData("/student/interactive/syllabus/print/1/2", true)]
        [InlineData("/student/myschedule/print/2026/1", true)]
        [InlineData("/student/myschedule/print/2026/1/extra", false)]
        [InlineData("/student/umkd/get/1/2/2026/1", false)]
        [InlineData("/student/transcript/download/malware.exe", false)]
        public void PrintDocument_ObservedRoutes_OnlyMatchesHtmlDocuments(string path, bool expected) { Assert.Equal(expected, SitePolicy.PrintDocument(SitePolicy.Origin + path)); }
        [Theory]
        [InlineData("https://example.com/", true)]
        [InlineData("mailto:test@example.com", true)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("file:///c:/Windows", false)]
        [InlineData("shell:Downloads", false)]
        [InlineData("https://user:password@example.com", false)]
        public void External_AllowedSchemes_DoesNotLaunchArbitraryProtocols(string url, bool expected) { Assert.Equal(expected, SitePolicy.External(url)); }
        [Theory]
        [InlineData("about:blank", true)]
        [InlineData("about:srcdoc", false)]
        [InlineData("https://univer.kaznu.kz/student/news", false)]
        [InlineData(null, false)]
        public void EmptyDocument_IsOnlyTheBlankPageOfAnEmptyFrame(string url, bool expected)
        { Assert.Equal(expected, SitePolicy.EmptyDocument(url)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/news", true)]
        [InlineData("https://univer.kaznu.kz:443/", true)]
        [InlineData("https://www.youtube.com/embed/x", false)]
        [InlineData("about:blank", false)]
        [InlineData("javascript:alert(1)", false)]
        public void ChildFrame_AllowsOnlyUniverDocuments(string url, bool expected)
        { Assert.Equal(expected, SitePolicy.ChildFrame(url)); }

        // A UMKD material that is a teacher's link: the real shape is a 0,00 б row without an extension whose address answers
        // 302 to Google Drive. Only a material address sent on to a named web host is such a link.
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "https://drive.google.com/drive/folders/abc?usp=sharing", true)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "http://example.org/page", true)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "https://univer.kaznu.kz/user/login", false)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "https://user:pw@drive.google.com/x", false)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "https://10.0.0.1/x", false)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "file:///C:/Windows/notepad.exe", false)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "mailto:a@b.kz", false)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", "https://localhost/x", false)]
        [InlineData("https://univer.kaznu.kz/student/myschedule/", "https://drive.google.com/x", false)]
        [InlineData("https://example.com/student/umkd/get/1/2/2026/1", "https://drive.google.com/x", false)]
        public void MaterialLink_IsAMaterialSentOnToAWebPage(string material, string target, bool expected)
        { Assert.Equal(expected, SitePolicy.MaterialLink(material, target)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", true)]
        [InlineData("https://univer.kaznu.kz/student/authorizationononlinetestform/", true)]
        [InlineData("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm/1", false)]
        [InlineData("https://univer.kaznu.kz/student/TestList", false)]
        [InlineData("http://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", false)]
        [InlineData("https://univer.kaznu.kz.evil.test/student/AuthorizationOnOnlineTestForm", false)]
        public void OnlineTest_OnlyTheSitesOwnRoute(string url, bool expected) { Assert.Equal(expected, SitePolicy.OnlineTest(url)); }
        [Theory]
        [InlineData("https://online-test.kaznu.kz/Account/LoginFromUniver?token=abc", true)]
        [InlineData("https://ONLINE-TEST.kaznu.kz/", true)]
        [InlineData("https://online-test.kaznu.kz:443/x", true)]
        [InlineData("http://online-test.kaznu.kz/x", false)]
        [InlineData("https://online-test.kaznu.kz:8443/x", false)]
        [InlineData("https://online-test.kaznu.kz.evil.test/x", false)]
        [InlineData("https://evil-online-test.kaznu.kz/x", false)]
        [InlineData("https://user@online-test.kaznu.kz/x", false)]
        [InlineData("https://online-test.kaznu.kz@evil.test/x", false)]
        [InlineData("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData(null, false)]
        public void OnlineTestPortal_ExactHostOverHttps(string url, bool expected) { Assert.Equal(expected, SitePolicy.OnlineTestPortal(url)); }
        [Fact]
        public void OnlineTestPortal_RejectsControlCharacters() { Assert.False(SitePolicy.OnlineTestPortal("https://online-test.kaznu.kz/x\nSet-Cookie:a")); }
        [Theory]
        [InlineData("GET", true, true, false, true)]
        [InlineData("get", true, true, false, true)]
        [InlineData("GET", false, true, false, false)]
        [InlineData("POST", true, true, false, false)]
        [InlineData("GET", true, false, false, false)]
        [InlineData("GET", true, true, true, false)]
        public void OnlineTestExit_OnlyTheArmedServerRedirectInTheSiteWindow(string method, bool isRedirect, bool armed, bool viewer, bool expected)
        { Assert.Equal(expected, SitePolicy.OnlineTestExit("https://online-test.kaznu.kz/Account/LoginFromUniver?token=abc", method, isRedirect, armed, viewer)); }
        [Fact]
        public void OnlineTestExit_RejectsAnotherHostEvenWhenArmed()
        {
            Assert.False(SitePolicy.OnlineTestExit("https://moodle.kaznu.kz/login", "GET", true, true, false));
            Assert.False(SitePolicy.OnlineTestExit("https://online-test.kaznu.kz.evil.test/", "GET", true, true, false));
        }
        [Theory]
        [InlineData("GET", true, false, false, true)]
        [InlineData("GET", false, false, false, false)]
        [InlineData("GET", true, true, false, false)]
        [InlineData("GET", true, false, true, false)]
        [InlineData("POST", true, false, false, false)]
        public void OnlineTestClick_OnlyTheStudentsOwnClickArms(string method, bool userGesture, bool isRedirect, bool viewer, bool expected)
        { Assert.Equal(expected, SitePolicy.OnlineTestClick("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", method, userGesture, isRedirect, viewer)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/TestList", "https://univer.kaznu.kz/student/TestList")]
        [InlineData("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", null)]
        [InlineData("https://univer.kaznu.kz/user/login", null)]
        [InlineData("https://univer.kaznu.kz/student/attentionStudents/1", null)]
        [InlineData("https://evil.test/student/TestList", null)]
        [InlineData(null, null)]
        public void OnlineTestReturnPage_IsTheStudentsOwnPage(string lastGet, string expected) { Assert.Equal(expected, SitePolicy.OnlineTestReturnPage(lastGet)); }
        // A second click from the route itself or from an account page keeps the page found by the first click; a click from
        // another of the student's pages returns there instead, even within the window.
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", 29, "https://univer.kaznu.kz/student/TestList")]
        [InlineData("https://univer.kaznu.kz/user/login", 29, "https://univer.kaznu.kz/student/TestList")]
        [InlineData("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", 31, null)]
        [InlineData("https://univer.kaznu.kz/student/myschedule/", 5, "https://univer.kaznu.kz/student/myschedule/")]
        public void OnlineTestReturnPage_KeepsTheFirstPageOnlyWithoutANewOne(string lastGet, int seconds, string expected)
        {
            var first = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
            Assert.Equal(expected, SitePolicy.OnlineTestReturnPage(lastGet, "https://univer.kaznu.kz/student/TestList", first, first.AddSeconds(seconds)));
            Assert.Null(SitePolicy.OnlineTestReturnPage("https://univer.kaznu.kz/student/AuthorizationOnOnlineTestForm", null, first, first.AddSeconds(1)));
        }
    }
}
