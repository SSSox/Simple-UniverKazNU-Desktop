using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // Regressions for the low findings of the 15 November 2026 review (L2, L7, L13).
    public class LowFindingsTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        public LowFindingsTests() { Directory.CreateDirectory(folder); }
        public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        [Fact]
        public void PageMessagesWithoutThisRunsKeyAreDropped()
        {
            var key = NativeChannel.NewKey();
            Assert.NotEqual(key, NativeChannel.NewKey());
            Assert.Equal("univer-pin:{}", NativeChannel.Unwrap(key, key + "univer-pin:{}"));
            Assert.Null(NativeChannel.Unwrap(key, "univer-pin:{}"));
            Assert.Null(NativeChannel.Unwrap(key, NativeChannel.NewKey() + "univer-pin:{}"));
            Assert.Null(NativeChannel.Unwrap(key, null));
            Assert.Null(NativeChannel.Unwrap("", "univer-busy"));
            // The page loses the global binding before its own scripts run; only the closure keeps it.
            var channel = NativeChannel.Script(key);
            Assert.Contains("delete window.__univerNativeNotify", channel);
            // Strict and without page-replaceable globals (tests/dom/native-channel.test.cjs runs it against a swapped String).
            Assert.Contains("'use strict'", channel);
            Assert.DoesNotContain("String(", channel);
        }

        [Theory]
        [InlineData(1, "Анкета (1)", false, "Анкета (1)")]
        [InlineData(1, "Анкета (1)", true, null)]
        [InlineData(3, "", false, null)]
        [InlineData(1, "", false, null)]
        public void TrayAndToastNameNoNoticeWhileLocked(int count, string heading, bool locked, string expected)
        { Assert.Equal(expected, Notices.Announced(count, heading, locked)); }

        [Fact]
        public void OfficeNamesFollowTheirContentForPreview()
        {
            var page = Write("marks.xls", Encoding.UTF8.GetBytes("﻿\r\n<html xmlns:o=\"urn:schemas-microsoft-com:office:office\"><body><table><tr><td>5</td></tr></table></body></html>"));
            Assert.Equal(".html", FileNames.PreviewExtension(page));
            var unicodePage = Write("list.xls", new byte[] { 0xff, 0xfe }.Concat(Encoding.Unicode.GetBytes("<table><tr><td>1</td></tr></table>")));
            Assert.Equal(".html", FileNames.PreviewExtension(unicodePage));
            var ole = new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1, 0, 0, 0, 0 };
            Assert.Equal(".xls", FileNames.PreviewExtension(Write("old.xlsx", ole)));
            Assert.Equal(".doc", FileNames.PreviewExtension(Write("old.docx", ole)));
            Assert.Equal(".xls", FileNames.PreviewExtension(Write("real.xls", ole)));
            var workbook = Path.Combine(folder, "book.xls");
            using (var archive = ZipFile.Open(workbook, ZipArchiveMode.Create)) archive.CreateEntry("xl/workbook.xml");
            Assert.Equal(".xlsx", FileNames.PreviewExtension(workbook));
            var macro = Path.Combine(folder, "book.xlsm");
            File.Copy(workbook, macro);
            Assert.Equal(".xlsm", FileNames.PreviewExtension(macro));
            // Other names, unknown content and missing files keep the extension.
            Assert.Equal(".pdf", FileNames.PreviewExtension(Write("a.pdf", Encoding.UTF8.GetBytes("<html></html>"))));
            Assert.Equal(".doc", FileNames.PreviewExtension(Write("plain.doc", Encoding.UTF8.GetBytes("just words"))));
            Assert.Equal(".xls", FileNames.PreviewExtension(Path.Combine(folder, "missing.xls")));
            // A web page saved as .xls that the HTML reader cannot show still opens in the Excel handler (review L13).
            Assert.True(FileNames.HandlerFallback("table.xls", ".html", new PreviewLimitException()));
            Assert.True(FileNames.HandlerFallback("old.xls", ".xls", new InvalidDataException()));
            Assert.False(FileNames.HandlerFallback("old.xls", ".xls", new PreviewLimitException()));
            Assert.False(FileNames.HandlerFallback("page.html", ".html", new InvalidDataException()));
            Assert.False(FileNames.HandlerFallback("page.htm", ".html", new PreviewLimitException()));
            Assert.False(FileNames.HandlerFallback("old.xlsx", ".xls", new PreviewLimitException()));
            Assert.False(FileNames.HandlerFallback("table.xls", ".html", new OperationCanceledException()));
        }
        private string Write(string name, byte[] bytes) { var path = Path.Combine(folder, name); File.WriteAllBytes(path, bytes); return path; }
        private string Write(string name, System.Collections.Generic.IEnumerable<byte> bytes) { return Write(name, bytes.ToArray()); }
    }
}
