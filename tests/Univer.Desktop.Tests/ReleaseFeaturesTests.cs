using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class NewsShortcutTests
    {
        [Theory]
        [InlineData("https://univer.kaznu.kz/", "https://univer.kaznu.kz/news/student")]
        [InlineData("https://univer.kaznu.kz/my/news/", "https://univer.kaznu.kz/news/student")]
        [InlineData("https://univer.kaznu.kz/my/news/1", "https://univer.kaznu.kz/news/student")]
        [InlineData("https://univer.kaznu.kz/my/news/2", "https://univer.kaznu.kz/news/student/2")]
        [InlineData("https://univer.kaznu.kz/my/news/17/", "https://univer.kaznu.kz/news/student/17")]
        public void NewsShortcut_AvoidsTheReadNoticeDetour(string url, string expected) { Assert.Equal(expected, SitePolicy.NewsShortcut(url)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/news/student")]
        [InlineData("https://univer.kaznu.kz/news/student/2")]
        [InlineData("https://univer.kaznu.kz/student/attention")]
        [InlineData("https://univer.kaznu.kz/my/newsletter")]
        [InlineData("https://univer.kaznu.kz/?lang=ru")]
        [InlineData("https://univer.kaznu.kz.evil.test/my/news/2")]
        [InlineData("http://univer.kaznu.kz/")]
        public void NewsShortcut_LeavesOtherRoutesAlone(string url) { Assert.Null(SitePolicy.NewsShortcut(url)); }
        [Theory]
        [InlineData("https://univer.kaznu.kz/user/loginMoodle", true)]
        [InlineData("https://univer.kaznu.kz/user/loginmoodle/", true)]
        [InlineData("https://univer.kaznu.kz/user/login", false)]
        [InlineData("https://evil.test/user/loginMoodle", false)]
        public void SingleSignOn_OnlyUniversOwnMoodleHandOff(string url, bool expected) { Assert.Equal(expected, SitePolicy.SingleSignOn(url)); }
    }
    public class SignOnPageTests
    {
        [Fact]
        public void ParseForm_DecodesUrlencodedHandOff()
        {
            var fields = SignOnPage.ParseForm("username=student&token=a%26b%3D&note=two+words&flag");
            Assert.Equal(new[] { "username", "token", "note", "flag" }, fields.Select(f => f.Key));
            Assert.Equal("a&b=", fields[1].Value); Assert.Equal("two words", fields[2].Value); Assert.Equal("", fields[3].Value);
        }
        [Fact]
        public void AutoSubmitPage_EncodesValuesAndHasNoScriptTag()
        {
            var page = SignOnPage.AutoSubmitPage("https://moodle.example.kz/login/index.php?a=1&b=2", "POST",
                new[] { new System.Collections.Generic.KeyValuePair<string, string>("token", "\"><script>alert(1)</script>") }, "Moodle");
            Assert.Contains("method=\"post\"", page);
            Assert.Contains("action=\"https://moodle.example.kz/login/index.php?a=1&amp;b=2\"", page);
            Assert.DoesNotContain("<script", page);
        }
        [Theory]
        [InlineData("https://moodle.example.kz/", true)]
        [InlineData("http://moodle.example.kz/", false)]
        [InlineData("https://user:pass@moodle.example.kz/", false)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("file:///C:/Windows/system32/cmd.exe", false)]
        public void CanOpen_OnlyPlainHttps(string url, bool expected) { Assert.Equal(expected, SignOnPage.CanOpen(url)); }
        [Theory]
        [InlineData("GET", true, false, true)]
        [InlineData("GET", false, false, false)]
        [InlineData("POST", false, true, true)]
        [InlineData("POST", true, false, false)]
        [InlineData("PUT", true, true, false)]
        public void HandOffExit_OnlyServerRedirectOrPostedForm(string method, bool isRedirect, bool hasFormBody, bool expected)
        { Assert.Equal(expected, SignOnPage.HandOffExit("https://moodle.example.kz/login", method, isRedirect, hasFormBody)); }
        [Fact]
        public void HandOffExit_RejectsJavascriptEvenOnARedirect()
        { Assert.False(SignOnPage.HandOffExit("javascript:alert(1)", "GET", true, false)); }
    }
    public class DownloadHistoryTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        public DownloadHistoryTests() { Directory.CreateDirectory(root); }
        [Fact]
        public async System.Threading.Tasks.Task History_RoundTripsAndMarksInterruptedDownloads()
        {
            var history = new DownloadHistory(root);
            history.Upsert(new DownloadRecord { FileName = "Лекция 1.pdf", Path = Path.Combine(root, "Лекция 1.pdf"), State = DownloadState.Completed, Total = 10, Source = "Социология" });
            history.Upsert(new DownloadRecord { FileName = "large.docx", State = DownloadState.Running });
            await history.SaveAsync();
            var loaded = new DownloadHistory(root); loaded.Load();
            Assert.Equal(2, loaded.Items.Count);
            Assert.Equal(DownloadState.Failed, loaded.Items.Single(x => x.FileName == "large.docx").State);
            Assert.Equal("Социология", loaded.Items.Single(x => x.FileName == "Лекция 1.pdf").Source);
            Assert.DoesNotContain("http", File.ReadAllText(Path.Combine(root, "downloads.json")));
        }
        [Fact]
        public void History_DamagedOrForeignEntriesAreIgnored()
        {
            File.WriteAllText(Path.Combine(root, "downloads.json"), "[{\"Id\":\"a\",\"Path\":\"relative\\\\x.pdf\"},{\"Id\":\"\"},null,{\"Id\":\"b\",\"FileName\":\"ok.pdf\",\"Path\":\"C:\\\\Users\\\\x\\\\ok.pdf\",\"State\":1}]");
            var history = new DownloadHistory(root); history.Load();
            Assert.Equal(new[] { "b" }, history.Items.Select(x => x.Id));
            File.WriteAllText(Path.Combine(root, "downloads.json"), "{ not json");
            history.Load(); Assert.Empty(history.Items);
        }
        [Fact]
        public void History_KeepsTheNewestEntriesWithinTheLimit()
        {
            var history = new DownloadHistory(root);
            for (var i = 0; i < DownloadHistory.Limit + 5; i++) history.Upsert(new DownloadRecord { FileName = "f" + i + ".pdf", State = DownloadState.Failed });
            Assert.Equal(DownloadHistory.Limit, history.Items.Count);
            Assert.Equal("f" + (DownloadHistory.Limit + 4) + ".pdf", history.Items[0].FileName);
        }
        [Theory]
        [InlineData("Лекция 1.PDF", "Pdf")]
        [InlineData("методичка.doc", "Word")]
        [InlineData("ведомость.xlsx", "Excel")]
        [InlineData("слайды.pptx", "PowerPoint")]
        [InlineData("фото.jpeg", "Image")]
        [InlineData("архив.rar", "Archive")]
        [InlineData("noextension", "Other")]
        public void FileKinds_GroupCommonCourseFiles(string name, string kind) { Assert.Equal((FileKind)Enum.Parse(typeof(FileKind), kind), FileKinds.Of(name)); }
        [Fact]
        public void Matches_SearchesNameAndCourseIgnoringCaseAndYo()
        {
            var item = new DownloadRecord { FileName = "Лекция 3. Ёмкость.pdf", Source = "Физика" };
            Assert.True(FileKinds.Matches(item, "емкость", null));
            Assert.True(FileKinds.Matches(item, "физика лекция", FileKind.Pdf));
            Assert.False(FileKinds.Matches(item, "химия", null));
            Assert.False(FileKinds.Matches(item, "", FileKind.Word));
        }
        [Theory]
        [InlineData(512, "512 Б")]
        [InlineData(1536, "1.5 КБ")]
        [InlineData(5L * 1024 * 1024, "5.0 МБ")]
        public void Size_IsReadable(long bytes, string expected) { Assert.Equal(expected, FileKinds.Size(bytes, "ru").Replace(',', '.')); }
        [Theory]
        [InlineData("downloadTooLarge", "downloadTooLarge")]
        [InlineData("downloadLogin", "downloadLogin")]
        [InlineData("offOrigin", "offOrigin")]
        [InlineData("downloadFailed", "downloadFailed")]
        [InlineData("something-else", "downloadFailed")]
        public void DownloadOutcome_KeepsKnownFailures(string message, string expected) { Assert.Equal(expected, DownloadOutcome.Key(message)); }
        [Fact]
        public void PartialDownloads_RemovesOnlyStaleTemporaryFilesOfTheApp()
        {
            Func<string, bool, string> make = (name, old) =>
            {
                var file = Path.Combine(root, name); File.WriteAllText(file, "x");
                if (old) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
                return file;
            };
            var stale = make(PartialDownloads.NewName(), true);
            var fresh = make(PartialDownloads.NewName(), false);
            var lookalike = make(".univer-notes.part", true);
            var users = make("Лекция.pdf.part", true);
            Assert.Equal(1, PartialDownloads.CleanUp(root, TimeSpan.FromMinutes(1)));
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(fresh) && File.Exists(lookalike) && File.Exists(users));
            Assert.Equal(0, PartialDownloads.CleanUp(Path.Combine(root, "missing"), TimeSpan.Zero));
        }
        public void Dispose()
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
    public class DocxHtmlTests
    {
        private static ZipArchive Docx(string body, string relations = "")
        {
            var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                Write(archive, "word/document.xml", "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>" + body + "</w:body></w:document>");
                Write(archive, "word/_rels/document.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + relations + "</Relationships>");
            }
            memory.Position = 0;
            return new ZipArchive(memory, ZipArchiveMode.Read);
        }
        private static void Write(ZipArchive archive, string name, string text)
        { using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); }
        [Fact]
        public void Convert_RendersHeadingsFormattingListsAndTables()
        {
            using (var archive = Docx("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Силлабус</w:t></w:r></w:p>" +
                "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Жирный</w:t></w:r><w:r><w:t xml:space=\"preserve\"> текст &lt;script&gt;</w:t></w:r></w:p>" +
                "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr><w:r><w:t>Пункт</w:t></w:r></w:p>" +
                "<w:tbl><w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p><w:r><w:t>Неделя</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
                "<w:p><w:hyperlink r:id=\"rId9\"><w:r><w:t>сайт</w:t></w:r></w:hyperlink></w:p>",
                "<Relationship Id=\"rId9\" Type=\"hyperlink\" Target=\"https://www.kaznu.kz/\" TargetMode=\"External\"/>"))
            {
                var html = DocxHtml.Convert(archive, new PreviewOptions("Тест")).Html;
                Assert.Contains("<h1>Силлабус</h1>", html);
                Assert.Contains("<b>Жирный</b> текст &lt;script&gt;", html);
                Assert.Contains("<span class=\"marker\">•</span>Пункт", html);
                Assert.Contains("<td colspan=\"2\">", html);
                Assert.Contains("href=\"https://www.kaznu.kz/\"", html);
                Assert.DoesNotContain("<script", html);
            }
        }
        [Fact]
        public void Convert_RejectsDtdAndExternalEntities()
        {
            var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
                Write(archive, "word/document.xml", "<!DOCTYPE document [<!ENTITY content SYSTEM 'https://example.invalid/private'>]><w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>&content;</w:t></w:r></w:p></w:body></w:document>");
            memory.Position = 0;
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Read))
                Assert.Throws<System.Xml.XmlException>(() => DocxHtml.Convert(archive, new PreviewOptions("QA")));
        }
        [Fact]
        public void Convert_DoesNotEmbedRemoteImagesOrActiveLinksAndEncodesTheTitle()
        {
            using (var archive = Docx("<w:p><w:hyperlink r:id='rId1'><w:r><w:t>Open</w:t></w:r></w:hyperlink><w:r><w:drawing xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><a:blip r:embed='rId2'/></w:drawing></w:r></w:p>",
                "<Relationship Id='rId1' Target='javascript:alert(1)'/><Relationship Id='rId2' Target='https://example.invalid/track.png' TargetMode='External'/>"))
            {
                var html = DocxHtml.Convert(archive, new PreviewOptions("<script>QA</script>")).Html;
                Assert.Contains("&lt;script&gt;QA&lt;/script&gt;", html);
                Assert.Contains("Open", html);
                Assert.DoesNotContain("<script", html); Assert.DoesNotContain("<a href", html); Assert.DoesNotContain("<img", html);
            }
        }
        [Fact]
        public void Convert_RejectsAnArchiveThatIsNotWord()
        {
            var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) Write(archive, "xl/workbook.xml", "<x/>");
            memory.Position = 0;
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Read)) Assert.Throws<InvalidDataException>(() => DocxHtml.Convert(archive, new PreviewOptions("x")));
        }
    }
    public class PreferencesTests
    {
        [Fact]
        public void Preferences_OlderFilesGetTrayOnAndCloneCopiesEverything()
        {
            var root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "preferences.json"), "{\"Language\":\"kz\",\"Profile\":\"" + Guid.NewGuid().ToString("N") + "\"}");
                var prefs = new Storage(root).LoadPreferences();
                // Animations are on for older files too; only the in-app setting turns them off.
                Assert.True(prefs.Tray); Assert.True(prefs.Animations); Assert.Equal(100, prefs.Zoom);
                prefs.Tray = false; prefs.Animations = false; prefs.Window = "10,20,1200,800,1";
                var copy = prefs.Clone();
                Assert.False(copy.Tray); Assert.False(copy.Animations); Assert.Equal("10,20,1200,800,1", copy.Window); Assert.Equal("kz", copy.Language);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
