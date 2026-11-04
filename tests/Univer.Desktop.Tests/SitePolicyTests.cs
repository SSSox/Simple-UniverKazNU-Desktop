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
    }
}
