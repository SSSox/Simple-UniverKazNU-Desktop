using System;
using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // The app's own browser window for other sites: what it may open, where Univer and files go, and what is never saved.
    public class WebLinksTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));

        [Theory]
        [InlineData("https://moodle.kaznu.kz/course/view.php?id=5", true, "Allow")]
        [InlineData("http://teacher.example.org/page", true, "Allow")]
        [InlineData("https://univer.kaznu.kz/student/myschedule/", true, "Univer")]
        [InlineData("https://univer.kaznu.kz/student/myschedule/", false, "Block")]
        [InlineData("mailto:teacher@example.org", true, "Shell")]
        [InlineData("mailto:teacher@example.org", false, "Block")]
        [InlineData("tel:+77000000000", true, "Shell")]
        [InlineData("https://user:secret@moodle.kaznu.kz/", true, "Block")]
        [InlineData("file:///C:/Windows/win.ini", true, "Block")]
        [InlineData("javascript:alert(1)", true, "Block")]
        [InlineData("chrome://settings/", true, "Block")]
        [InlineData("ms-settings:privacy", true, "Block")]
        [InlineData("about:blank", true, "Allow")]
        [InlineData("about:srcdoc", false, "Allow")]
        [InlineData("data:text/html,hi", false, "Allow")]
        [InlineData("data:text/html,hi", true, "Block")]
        [InlineData("blob:https://moodle.kaznu.kz/0b8a", false, "Allow")]
        [InlineData("", true, "Block")]
        [InlineData("not a url", true, "Block")]
        public void NavigationsAreSortedSafely(string url, bool mainFrame, string expected)
        {
            Assert.Equal(expected, WebLinks.Decide(url, mainFrame).ToString());
        }

        // Logout, language and notice addresses change the session with a plain GET: a page in the window (a link with
        // target=_blank, window.open, a middle click) must not send the main window there.
        [Theory]
        [InlineData("https://univer.kaznu.kz/student/umkd/321/2026/1", "Univer")]
        [InlineData("https://univer.kaznu.kz/", "Univer")]
        [InlineData("https://univer.kaznu.kz/user/logout", "Block")]
        [InlineData("https://univer.kaznu.kz/User/Logout/", "Block")]
        [InlineData("https://univer.kaznu.kz/lang/change/kz/", "Block")]
        [InlineData("https://univer.kaznu.kz/user/setagree", "Block")]
        [InlineData("https://univer.kaznu.kz/student/attentionStudents/5", "Block")]
        [InlineData("https://moodle.kaznu.kz/course/view.php?id=5", "Allow")]
        [InlineData("about:blank", "Block")]
        [InlineData("about:srcdoc", "Block")]
        [InlineData("mailto:teacher@example.org", "Shell")]
        [InlineData("javascript:alert(1)", "Block")]
        public void NewWindowsNeverSteerTheSignedInSession(string url, string expected)
        {
            Assert.Equal(expected, WebLinks.Opened(url).ToString());
            Assert.Equal(expected == "Univer", WebLinks.HandBack(url));
        }

        [Theory]
        [InlineData("https://moodle.kaznu.kz/login/index.php", true)]
        [InlineData("https://drive.google.com/drive/folders/x", true)]
        [InlineData("https://univer.kaznu.kz/student/umkd/", false)]
        [InlineData("https://127.0.0.1/", false)]
        [InlineData("http://localhost/", false)]
        [InlineData("mailto:teacher@example.org", false)]
        [InlineData("file:///C:/x.pdf", false)]
        public void OnlyNamedWebSitesOpenInside(string url, bool expected)
        {
            Assert.Equal(expected, WebLinks.InApp(url));
        }

        [Fact]
        public void HandOffPageLivesAtMoodlesOwnOriginOverHttpsOnly()
        {
            Assert.Equal("https://moodle.kaznu.kz/univer-desktop-handoff", WebLinks.HandOffAddress("https://moodle.kaznu.kz/login/index.php?x=1"));
            Assert.Equal("https://moodle.kaznu.kz:8443/univer-desktop-handoff", WebLinks.HandOffAddress("https://moodle.kaznu.kz:8443/login/"));
            Assert.Null(WebLinks.HandOffAddress("http://moodle.kaznu.kz/login/index.php"));
            Assert.Null(WebLinks.HandOffAddress("https://univer.kaznu.kz/user/login"));
            Assert.Null(WebLinks.HandOffAddress("not a url"));
            Assert.True(WebLinks.HandOff("https://moodle.kaznu.kz/univer-desktop-handoff"));
            Assert.False(WebLinks.HandOff("https://moodle.kaznu.kz/login/index.php"));
        }

        [Theory]
        [InlineData("setup.exe", true)]
        [InlineData("SETUP.EXE", true)]
        [InlineData("installer.exe. ", true)]
        [InlineData("run.ps1", true)]
        [InlineData("page.hta", true)]
        [InlineData("disk.iso", true)]
        [InlineData("link.url", true)]
        [InlineData("lecture.pdf", false)]
        [InlineData("tasks.docx", false)]
        [InlineData("slides.pptx", false)]
        [InlineData("archive.zip", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ProgramsAndScriptsAreNeverSaved(string name, bool expected)
        {
            Assert.Equal(expected, WebLinks.Blocked(name));
        }

        [Fact]
        public void SavedFilesNeverReplaceExistingOnes()
        {
            Directory.CreateDirectory(folder);
            var first = WebLinks.Target(folder, "lecture.pdf");
            Assert.Equal(Path.Combine(folder, "lecture.pdf"), first);
            File.WriteAllText(first, "x");
            File.WriteAllText(Path.Combine(folder, "lecture (2).pdf.crdownload"), "x");
            Assert.Equal(Path.Combine(folder, "lecture (3).pdf"), WebLinks.Target(folder, "lecture.pdf"));
            Assert.Equal(Path.Combine(folder, "download"), WebLinks.Target(folder, "  "));
            Assert.Null(WebLinks.Target(folder, "setup.exe"));
            var unsafeName = WebLinks.Target(folder, "..\\..\\evil.pdf");
            Assert.Equal(folder, Path.GetDirectoryName(unsafeName));
        }
        [Fact]
        public void TwoDownloadsOfOneNameAskedTogetherGetTwoPaths()
        {
            Directory.CreateDirectory(folder);
            var taken = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = WebLinks.Target(folder, "Notes.pdf", taken.Contains); taken.Add(first);
            var second = WebLinks.Target(folder, "notes.pdf", taken.Contains);
            Assert.Equal(Path.Combine(folder, "Notes.pdf"), first);
            Assert.Equal(Path.Combine(folder, "notes (2).pdf"), second);
        }

        [Fact]
        public void WebPreferencesSurviveCopyAndSaving()
        {
            var original = new Preferences { ExternalLinks = true, PinOffered = true };
            var copy = original.Clone();
            Assert.True(copy.ExternalLinks); Assert.True(copy.PinOffered); Assert.Equal(original.WebProfile, copy.WebProfile);

            var storage = new Storage(folder);
            storage.SavePreferences(original);
            var loaded = storage.LoadPreferences();
            Assert.True(loaded.ExternalLinks); Assert.True(loaded.PinOffered); Assert.Equal(original.WebProfile, loaded.WebProfile);
            Assert.True(storage.WebProfileSaved(original.WebProfile));
            Assert.False(storage.WebProfileSaved(Guid.NewGuid().ToString("N")));
        }

        [Fact]
        public void BrokenWebProfileNameIsReplacedOnLoad()
        {
            var storage = new Storage(folder);
            storage.SavePreferences(new Preferences { WebProfile = "..\\..\\Windows" });
            var loaded = storage.LoadPreferences();
            Assert.NotEqual("..\\..\\Windows", loaded.WebProfile);
            Assert.StartsWith(storage.BrowserRoot, storage.WebProfilePath(loaded.WebProfile));
            Assert.Throws<ArgumentException>(() => storage.WebProfilePath("..\\x"));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
