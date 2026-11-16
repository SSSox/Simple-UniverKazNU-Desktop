using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // «Скачать все» in a UMKD section, and the Downloads list dropping files that left their place.
    public class SectionDownloadTests : IDisposable
    {
        private const string First = "https://univer.kaznu.kz/student/umkd/get/9002/321/2026/1";
        private const string Second = "https://univer.kaznu.kz/student/umkd/get/9003/321/2026/1";
        private readonly string folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void SectionMessageCarriesTheSectionsUmkdFilesOnceEach()
        {
            var batch = DownloadBatch.Parse("{\"section\":\" Конспекты лекций \",\"urls\":[\"" + First + "\",\"" + Second + "\",\"" + First + "\"]}");
            Assert.NotNull(batch);
            Assert.Equal("Конспекты лекций", batch.Section);
            Assert.Equal(new[] { First, Second }, batch.Urls.ToArray());
        }

        // Any script on the site can send the message: one address that is not a UMKD file of the site voids all of it.
        [Theory]
        [InlineData("{\"section\":\"Лекции\",\"urls\":[\"" + First + "\",\"https://univer.kaznu.kz/user/logout\"]}")]
        [InlineData("{\"section\":\"Лекции\",\"urls\":[\"https://univer.kaznu.kz.example.org/student/umkd/get/1/2/2026/1\"]}")]
        [InlineData("{\"section\":\"Лекции\",\"urls\":[\"http://univer.kaznu.kz/student/umkd/get/1/2/2026/1\"]}")]
        [InlineData("{\"section\":\"Лекции\",\"urls\":[\"https://univer.kaznu.kz:8443/student/umkd/get/1/2/2026/1\"]}")]
        [InlineData("{\"section\":\"Лекции\",\"urls\":[\"https://example.org/lecture.pdf\"]}")]
        [InlineData("{\"section\":\"Лекции\",\"urls\":[5]}")]
        [InlineData("{\"section\":\"Лекции\",\"urls\":[]}")]
        [InlineData("{\"section\":\"Лекции\",\"urls\":\"" + First + "\"}")]
        [InlineData("{\"section\":\"  \",\"urls\":[\"" + First + "\"]}")]
        [InlineData("{\"urls\":[\"" + First + "\"]}")]
        [InlineData("not json")]
        [InlineData("")]
        [InlineData(null)]
        public void AnythingElseVoidsTheWholeMessage(string payload)
        {
            Assert.Null(DownloadBatch.Parse(payload));
        }

        [Fact]
        public void SectionMessageHasALimit()
        {
            var urls = Enumerable.Range(1, DownloadBatch.Limit + 1).Select(i => "\"https://univer.kaznu.kz/student/umkd/get/" + i + "/321/2026/1\"");
            Assert.Null(DownloadBatch.Parse("{\"section\":\"Лекции\",\"urls\":[" + string.Join(",", urls) + "]}"));
            Assert.Equal(DownloadBatch.Limit, DownloadBatch.Parse("{\"section\":\"Лекции\",\"urls\":[" + string.Join(",", urls.Take(DownloadBatch.Limit)) + "]}").Urls.Count);
        }

        // The same file spelled another way (a query, a trailing slash, case, leading zeros) is one file: a page script cannot make the app
        // fetch it over and over in one message.
        [Fact]
        public void SameFileSpelledAnotherWayCountsOnce()
        {
            var batch = DownloadBatch.Parse("{\"section\":\"Лекции\",\"urls\":[\"" + First + "\",\"" + First + "?x=1\",\"" + First + "?x=2\",\"" + First + "/\",\"" + Second + "\"," +
                "\"https://univer.kaznu.kz/STUDENT/UMKD/GET/9002/321/2026/1\",\"https://univer.kaznu.kz/student/umkd/get/009002/0321/2026/01\"]}");
            Assert.Equal(new[] { First, Second }, batch.Urls.ToArray());
        }

        // Stop comes only from a button that shows a run and stops only that run; a double click before the label changes
        // shows the run again instead of stopping it, and a late Stop after the run ended never starts a new one.
        [Fact]
        public void ClickOnASectionsButtonNeverStopsByAccidentOrStartsFromStop()
        {
            Assert.True(DownloadBatch.Parse("{\"section\":\"Лекции\",\"urls\":[\"" + First + "\"],\"stop\":true}").Stop);
            Assert.False(DownloadBatch.Parse("{\"section\":\"Лекции\",\"urls\":[\"" + First + "\"],\"stop\":\"true\"}").Stop);
            Assert.False(DownloadBatch.Parse("{\"section\":\"Лекции\",\"urls\":[\"" + First + "\"]}").Stop);
            Assert.Equal(SectionClick.Start, DownloadBatch.Choose(false, false, false));
            Assert.Equal(SectionClick.Show, DownloadBatch.Choose(false, true, false));
            Assert.Equal(SectionClick.Busy, DownloadBatch.Choose(false, false, true));
            Assert.Equal(SectionClick.Stop, DownloadBatch.Choose(true, true, false));
            Assert.Equal(SectionClick.Relabel, DownloadBatch.Choose(true, false, false));
            Assert.Equal(SectionClick.Relabel, DownloadBatch.Choose(true, false, true));
        }

        [Fact]
        public void FilesReadFromThePageKeepTheirNameCourseAndSection()
        {
            var files = StudyMaterials.Files("{\"" + First + "\":{\"name\":\"Лекция 1.pdf\",\"course\":\"Социология\",\"section\":\"Конспекты лекций\"},\"odd\":5}");
            Assert.Equal("Лекция 1.pdf", files[First].Name);
            Assert.Equal("Социология", files[First].Course);
            Assert.Equal("Конспекты лекций", files[First].Section);
            Assert.False(files.ContainsKey("odd"));
            Assert.Equal(@"Univer УМКД\Социология\Конспекты лекций", StudyFolders.Relative(files[First].Course, files[First].Section));
            Assert.Empty(StudyMaterials.Files("{broken"));
            Assert.Empty(StudyMaterials.Files(""));
        }

        [Fact]
        public void OpeningAMissingFileForgetsItOnlyWhenItIsStillGoneAMomentLater()
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "Лекция 2.pdf");
            Assert.True(DownloadHistory.StillGoneAsync(file, TimeSpan.FromMilliseconds(50)).Result);
            // A file written back during the pause (an editor replacing it) is not gone.
            var check = DownloadHistory.StillGoneAsync(file, TimeSpan.FromMilliseconds(600));
            System.Threading.Thread.Sleep(150); File.WriteAllText(file, "x");
            Assert.False(check.Result);
            Assert.False(DownloadHistory.StillGoneAsync(file, TimeSpan.FromMilliseconds(10)).Result);
        }
        [Fact]
        public void FileIsGoneOnlyWhenItsPlaceAnswersWithoutIt()
        {
            var course = Path.Combine(folder, "Социология"); Directory.CreateDirectory(course);
            var file = Path.Combine(course, "Лекция 1.pdf"); File.WriteAllText(file, "x");
            Assert.Equal(FilePresence.Present, DownloadHistory.Locate(file));
            File.Delete(file);
            Assert.Equal(FilePresence.Gone, DownloadHistory.Locate(file));
            Directory.Delete(course);
            Assert.Equal(FilePresence.Gone, DownloadHistory.Locate(file));
            Directory.CreateDirectory(file);
            Assert.Equal(FilePresence.Gone, DownloadHistory.Locate(file));
        }

        // An unplugged drive or a path that says nothing about a place must never empty the list.
        [Fact]
        public void UnreachableOrUnclearPlacesAreUnknown()
        {
            var free = "ZYXWVUTSRQPONMLKJIHG".Select(letter => letter + @":\").First(root => !Directory.Exists(root));
            Assert.Equal(FilePresence.Unknown, DownloadHistory.Locate(Path.Combine(free, "Downloads", "Лекция 1.pdf")));
            Assert.Equal(FilePresence.Unknown, DownloadHistory.Locate(""));
            Assert.Equal(FilePresence.Unknown, DownloadHistory.Locate(null));
            Assert.Equal(FilePresence.Unknown, DownloadHistory.Locate("Лекция 1.pdf"));
        }

        // Downloads (or a course folder) linked to a USB drive that is unplugged: the file is not known to be gone. Behind a
        // link whose target answers, a missing file is gone as anywhere else.
        [Fact]
        public void FileBehindALinkToAMissingFolderIsUnknown()
        {
            var target = Path.Combine(folder, "drive"); var link = Path.Combine(folder, "Downloads");
            Directory.CreateDirectory(target);
            using (var mklink = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            { mklink.StandardOutput.ReadToEnd(); mklink.WaitForExit(); }
            try
            {
                Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "the junction exists");
                var file = Path.Combine(link, "Социология", "Лекция 1.pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, "x");
                Assert.Equal(FilePresence.Present, DownloadHistory.Locate(file));
                File.Delete(file);
                Assert.Equal(FilePresence.Gone, DownloadHistory.Locate(file));
                Directory.Delete(target, true);
                Assert.Equal(FilePresence.Unknown, DownloadHistory.Locate(file));
                Assert.Equal(FilePresence.Unknown, DownloadHistory.Locate(Path.Combine(link, "Лекция 2.pdf")));
            }
            finally { if (Directory.Exists(link) || File.Exists(link)) Directory.Delete(link); }
        }

        public void Dispose()
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
