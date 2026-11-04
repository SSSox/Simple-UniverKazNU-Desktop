using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class FileNamesTests
    {
        [Theory]
        [InlineData("../../file.pdf", "_.._file.pdf")]
        [InlineData("CON.pdf", "_CON.pdf")]
        [InlineData("NUL", "_NUL")]
        [InlineData("\"силлабус.pdf\"", "силлабус.pdf")]
        [InlineData("   ...", "Univer-document")]
        [InlineData("bad\r\nname.pdf", "bad__name.pdf")]
        public void Safe_UntrustedServerName_DoesNotEscapeDownloads(string value, string expected) { Assert.Equal(expected, FileNames.Safe(value)); }
        [Theory]
        [InlineData("\uFEFF  <!doctype html><title>Sign in</title>", true)]
        [InlineData("<HTML><body>Login</body></HTML>", true)]
        [InlineData("garbage <form id='login_frm'>", true)]
        [InlineData("%PDF-1.7 synthetic-fixture", false)]
        public void LoginHtml_MislabeledAttachment_RejectsLoginPage(string text, bool expected)
        { var bytes = Encoding.UTF8.GetBytes(text); Assert.Equal(expected, FileNames.LoginHtml(bytes, bytes.Length)); }
        [Theory]
        [InlineData(null, null, "application/octet-stream", "%PDF-1.4 fixture", "Univer-document.pdf")]
        [InlineData("attachment; filename=Lecture", null, "application/octet-stream", "%PDF-1.7 fixture", "Lecture.pdf")]
        [InlineData("attachment; filename=wrong.doc", null, "application/octet-stream", "%PDF-1.7 fixture", "wrong.pdf")]
        [InlineData(null, "Лекция 1", "application/pdf", "%PDF-1.4 fixture", "Лекция 1.pdf")]
        [InlineData(null, null, "application/msword", "fixture", "Univer-document.doc")]
        [InlineData("attachment; filename=table.xls", null, "application/octet-stream", "fixture", "table.xls")]
        public void Resolve_DownloadsRecoverUsableFileExtensions(string header, string hint, string type, string payload, string expected)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            Assert.Equal(expected, FileNames.Resolve(header, hint, "https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", type, bytes, bytes.Length));
        }
        [Theory]
        [InlineData("attachment; filename*=UTF-8''%D0%9B%D0%B5%D0%BA%D1%86%D0%B8%D1%8F.pdf", "Лекция.pdf")]
        [InlineData("attachment; filename=fallback.pdf; filename*=UTF-8'ru'%D0%A2%D0%B5%D1%81%D1%82.pdf", "Тест.pdf")]
        [InlineData("inline; filename=\"course notes.pdf\"", "course notes.pdf")]
        [InlineData("attachment; filename=\"=?UTF-8?B?0KLQtdGB0YIucGRm?=\"", "Тест.pdf")]
        [InlineData("attachment; filename=\"=?UTF-8?Q?course_notes.pdf?=\"", "course notes.pdf")]
        [InlineData("attachment; filename=backup.pdf; filename*=UTF-8''%ZZ", "backup.pdf")]
        public void HeaderName_RealWorldHeaderEncodings(string header, string expected)
        { Assert.Equal(expected, FileNames.HeaderName(header)); }
        [Fact]
        public void HeaderName_RecoversTheSitesRawUtf8CyrillicHeader()
        {
            const string name = "силлабус виб 25-26.pdf";
            var raw = Encoding.GetEncoding(28591).GetString(Encoding.UTF8.GetBytes(name));
            Assert.Equal(name, FileNames.HeaderName("attachment; filename=\"" + raw + "\""));
            Assert.Equal("Ñandú.pdf", FileNames.HeaderName("attachment; filename=\"Ñandú.pdf\""));
        }
        [Theory]
        [InlineData("word/document.xml", ".docx")]
        [InlineData("xl/workbook.xml", ".xlsx")]
        [InlineData("ppt/presentation.xml", ".pptx")]
        [InlineData("fixture.txt", ".zip")]
        public void Resolve_ZipContainerDoesNotLoseOfficeType(string entry, string extension)
        {
            var path = Path.GetTempFileName();
            try
            {
                using (var stream = File.Create(path)) using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("synthetic fixture");
                var bytes = File.ReadAllBytes(path);
                Assert.Equal("Univer-document" + extension, FileNames.Resolve(null, null, null, "application/octet-stream", bytes, bytes.Length, path));
            }
            finally { File.Delete(path); }
        }
        [Theory]
        [InlineData(null, "Лекция 1. Введение", "application/octet-stream", "%PDF-1.4 fixture", "Лекция 1. Введение.pdf")]
        [InlineData(null, "Тема 2. Основы", "application/msword", "fixture", "Тема 2. Основы.doc")]
        [InlineData("attachment; filename=\"Отчёт 08.10.2026\"", null, "application/pdf", "fixture", "Отчёт 08.10.2026.pdf")]
        [InlineData(null, "лекция 14 .pdf", "application/pdf", "%PDF-1.4 fixture", "лекция 14 .pdf")]
        [InlineData(null, "Report.PDF", "application/pdf", "%PDF-1.4 fixture", "Report.PDF")]
        public void Resolve_DottedTitleIsNotTruncatedToAFakeExtension(string header, string hint, string type, string payload, string expected)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            Assert.Equal(expected, FileNames.Resolve(header, hint, "https://univer.kaznu.kz/student/umkd/get/1/2/2026/1", type, bytes, bytes.Length));
        }
        [Theory]
        [InlineData("WordDocument", "Методичка", ".doc")]
        [InlineData("Workbook", "Ведомость", ".xls")]
        [InlineData("PowerPoint Document", "Лекция 3. Сети", ".ppt")]
        public void Resolve_LegacyOfficeCompoundFileGetsItsRealExtension(string stream, string hint, string extension)
        {
            var path = Path.GetTempFileName();
            try
            {
                // Synthetic compound-file header followed by a UTF-16 directory entry name.
                var header = new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 };
                var bytes = new byte[2048]; Array.Copy(header, bytes, header.Length);
                var name = Encoding.Unicode.GetBytes(stream); Array.Copy(name, 0, bytes, 1024, name.Length);
                File.WriteAllBytes(path, bytes);
                Assert.Equal(hint + extension, FileNames.Resolve(null, hint, null, "application/octet-stream", bytes, bytes.Length, path));
                Assert.Equal("table.xls", FileNames.Resolve("attachment; filename=table.xls", null, null, "application/octet-stream", bytes, bytes.Length, path));
            }
            finally { File.Delete(path); }
        }
        [Theory]
        [InlineData("notes.pdf", ".pdf")]
        [InlineData("Лекция 1. Введение", "")]
        [InlineData("Отчёт 08.10.2026", "")]
        [InlineData("archive.tar.gz", ".gz")]
        public void Extension_OnlyRecognisesRealSuffixes(string name, string expected) { Assert.Equal(expected, FileNames.Extension(name)); }
        [Fact]
        public void Safe_LongServerNameKeepsTheExtension()
        { var result = FileNames.Safe(new string('я', 200) + ".docx"); Assert.EndsWith(".docx", result); Assert.True(result.Length <= 150); }
        [Theory]
        [InlineData("test.pdf", true)]
        [InlineData("test.docx", true)]
        [InlineData("test.exe", false)]
        [InlineData("test.cmd", false)]
        [InlineData("test.html", false)]
        public void CanOpen_NoExecutableOrHtmlAttachments(string name, bool expected)
        { Assert.Equal(expected, FileNames.CanOpen(name)); }
    }
}
