using System;
using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // UMKD files are kept under Downloads\Univer УМКД\<course>\<section>; the names come from page text.
    public class StudyFoldersTests
    {
        private static readonly string Downloads = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", "Downloads");
        [Theory]
        [InlineData("Социология", "Социология")]
        [InlineData("  Казахский   (русский)\r\n язык ", "Казахский (русский) язык")]
        [InlineData("Конспекты лекций/тематический материал для практических занятий.", "Конспекты лекций, тематический материал для практических занятий")]
        [InlineData("Руководство по СРО: задания", "Руководство по СРО - задания")]
        [InlineData("a<b>c?\"d*", "abcd")]
        [InlineData("..\\..\\Windows", "Windows")]
        [InlineData("...", "")]
        [InlineData(null, "")]
        [InlineData("CON", "_CON")]
        [InlineData("nul.txt", "_nul.txt")]
        public void Name_IsOneSafeFolderName(string value, string expected) { Assert.Equal(expected, StudyFolders.Name(value)); }
        [Fact]
        public void Name_CutsALongHeadingAtAWord()
        {
            var name = StudyFolders.Name("Методические рекомендации к практическим / семинарским / лабораторным занятиям.");
            Assert.Equal("Методические рекомендации к практическим, семинарским", name);
            Assert.True(name.Length <= 64);
            Assert.Equal(64, StudyFolders.Name(new string('я', 200)).Length);
            // A cut never leaves half of a surrogate pair at the end of the name.
            var cut = StudyFolders.Name(new string('я', 63) + "\U0001F600" + new string('я', 10));
            Assert.Equal(63, cut.Length); Assert.False(char.IsHighSurrogate(cut[cut.Length - 1]));
        }
        [Fact]
        public void Relative_PutsTheSectionInsideTheCourse()
        {
            Assert.Equal(Path.Combine("Univer УМКД", "Социология", "Силлабус"), StudyFolders.Relative("Социология", "Силлабус"));
            Assert.Equal(Path.Combine("Univer УМКД", "Социология"), StudyFolders.Relative("Социология", " "));
            // Without a course the section alone would mix files of different courses.
            Assert.Equal("Univer УМКД", StudyFolders.Relative(null, "Силлабус"));
            Assert.Equal(Path.Combine("Univer УМКД", "Windows", "System32"), StudyFolders.Relative("..\\Windows", "..\\..\\System32"));
        }
        [Theory]
        [InlineData("Univer УМКД\\Социология\\Силлабус", true)]
        [InlineData("Univer УМКД", true)]
        [InlineData("..\\Outside", false)]
        [InlineData("Univer УМКД\\..\\..\\Outside", false)]
        [InlineData("C:\\Windows", false)]
        [InlineData("Other\\Folder", false)]
        [InlineData("Univer УМКД evil\\Folder", false)]
        public void Resolve_StaysUnderTheRoot(string relative, bool kept)
        {
            var folder = StudyFolders.Resolve(Downloads, relative);
            Assert.Equal(kept ? Path.GetFullPath(Path.Combine(Downloads, relative)) : Downloads, folder);
            Assert.Equal(kept, StudyFolders.Inside(folder, Downloads));
        }
        [Fact]
        public void Resolve_KeepsDownloadsForNoFolderOrABrokenOne()
        {
            Assert.Equal(Downloads, StudyFolders.Resolve(Downloads, null));
            Assert.Equal(Downloads, StudyFolders.Resolve(Downloads, ""));
            Assert.Equal(Downloads, StudyFolders.Resolve(Downloads, "Univer УМКД\\a\0b"));
        }
        [Fact]
        public void Fit_ShortensOnlyANameThatWouldPassThePathLimit()
        {
            var folder = Path.Combine(Downloads, "Univer УМКД", new string('к', 64), new string('с', 64));
            Assert.Equal("Лекция 1.pdf", StudyFolders.Fit(folder, "Лекция 1.pdf"));
            var fitted = StudyFolders.Fit(folder, new string('ф', 146) + ".pdf");
            Assert.EndsWith(".pdf", fitted);
            Assert.True(folder.Length + 1 + fitted.Length <= 240, "the whole path stays under the limit");
            Assert.True(fitted.Length >= 12);
        }
        [Fact]
        public void History_KeepsUmkdPathsOfARemoteDownloadsFolder()
        {
            const string share = @"\\server\share\Downloads";
            Assert.True(StudyFolders.Inside(share + @"\Univer УМКД\Социология\Силлабус\file.pdf", share));
            Assert.False(StudyFolders.Inside(share + @"\Other\file.pdf", share));
            Assert.False(StudyFolders.Inside(@"\\other\share\Univer УМКД\file.pdf", share));
        }
        [Theory]
        [InlineData("{\"course\":\"Социология\",\"section\":\"Силлабус\"}", "Univer УМКД\\Социология\\Силлабус")]
        [InlineData("{\"course\":\"Социология\",\"section\":\"\"}", "Univer УМКД\\Социология")]
        [InlineData("{\"course\":5,\"section\":[\"x\"]}", "Univer УМКД")]
        [InlineData("", "Univer УМКД")]
        [InlineData(null, "Univer УМКД")]
        [InlineData("not json", "Univer УМКД")]
        public void Folder_ReadsThePageAnswer(string place, string expected) { Assert.Equal(expected, StudyMaterials.Folder(place)); }
        [Fact]
        public void Scripts_CarryTheAddressAsData()
        {
            const string address = "https://example.org/a'b\"c</script>";
            foreach (var script in new[] { StudyMaterials.LinkCheck(address), StudyMaterials.Place(address) })
            {
                Assert.Contains("\\u0027b\\\"c\\u003c/script\\u003e", script);
                Assert.DoesNotContain("a'b", script);
            }
        }
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/umkd/3152303/2026/1", true)]
        [InlineData("https://univer.kaznu.kz/student/umkd/3152303/2026/1/", true)]
        [InlineData("https://univer.kaznu.kz/student/umkd/", false)]
        [InlineData("https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", false)]
        [InlineData("https://univer.kaznu.kz/news/student", false)]
        [InlineData("https://evil.test/student/umkd/1/2026/1", false)]
        [InlineData("http://univer.kaznu.kz/student/umkd/1/2026/1", false)]
        public void StudyMaterials_IsOnlyACoursePage(string url, bool expected) { Assert.Equal(expected, SitePolicy.StudyMaterials(url)); }
        [Theory]
        [InlineData("https://www.youtube.com/watch?v=abc", true)]
        [InlineData("http://elibrary.kaznu.kz/ru/node/1", true)]
        [InlineData("https://xn--80aswg.xn--p1ai/", true)]
        [InlineData("mailto:teacher@example.org", false)]
        [InlineData("tel:+77000000000", false)]
        [InlineData("https://user:pass@example.org/", false)]
        [InlineData("https://192.168.1.1/admin", false)]
        [InlineData("http://[::1]/", false)]
        [InlineData("http://localhost/", false)]
        [InlineData("http://router/", false)]
        [InlineData("https://univer.kaznu.kz/user/logout", false)]
        [InlineData("file:///C:/Windows/system32/calc.exe", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("ms-settings:privacy", false)]
        [InlineData("https://example.org/\u0000", false)]
        [InlineData(null, false)]
        public void StudyLink_IsAPlainWebPageOnANamedHost(string url, bool expected) { Assert.Equal(expected, SitePolicy.StudyLink(url)); }
        [Fact]
        public void OutsideLinkMessages_AnswerTheClickWithoutOfferingRetry()
        {
            var opened = UiStatus.Describe("externalOpened"); var blocked = UiStatus.Describe("externalBlocked");
            Assert.Equal(MessageTone.Success, opened.Tone); Assert.Equal(MessageTone.Warning, blocked.Tone);
            Assert.Equal(MessageAction.None, opened.Action); Assert.Equal(MessageAction.None, blocked.Action);
            var status = new UiStatus(); status.Update("externalBlocked"); status.Update("externalOpened");
            Assert.Equal("externalOpened", status.Current.Key);
            status.Dismiss(); Assert.Null(status.Current);
        }
    }
}
