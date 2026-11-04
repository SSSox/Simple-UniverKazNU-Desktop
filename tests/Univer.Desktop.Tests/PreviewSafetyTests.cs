using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class PreviewSafetyTests
    {
        private static string Render(string body, CancellationToken cancellation = default(CancellationToken), bool image = false)
        { return Convert(body, cancellation, image ? 65536 : 0).Html; }
        // imageBytes > 0 adds word/media/test.png of that size under the relation id "img".
        private static PreviewDocument Convert(string body, CancellationToken cancellation = default(CancellationToken), long imageBytes = 0)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                        writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><w:body>" + body + "</w:body></w:document>");
                    if (imageBytes > 0)
                    {
                        using (var writer = new StreamWriter(zip.CreateEntry("word/_rels/document.xml.rels").Open()))
                            writer.Write("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='img' Target='media/test.png'/></Relationships>");
                        using (var stream = zip.CreateEntry("word/media/test.png").Open())
                        {
                            var block = new byte[65536];
                            for (var written = 0L; written < imageBytes; written += block.Length) stream.Write(block, 0, (int)Math.Min(block.Length, imageBytes - written));
                        }
                    }
                }
                memory.Position = 0;
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Read)) return DocxHtml.Convert(zip, new PreviewOptions("Fixture", null, "Picture omitted"), cancellation);
            }
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeepBlockAndInlineContentAreRejectedBeforeRendering(bool inline)
        {
            var body = new StringBuilder(inline ? "<w:p>" : "");
            for (var i = 0; i < 12000; i++) body.Append("<w:sdt><w:sdtContent>");
            body.Append("<w:r><w:t>Deep</w:t></w:r>");
            for (var i = 0; i < 12000; i++) body.Append("</w:sdtContent></w:sdt>");
            if (inline) body.Append("</w:p>");
            Assert.Throws<PreviewLimitException>(() => Render(body.ToString()));
        }
        [Fact]
        public void RepeatedImageIsKeptOnceBesideThePage()
        {
            var body = new StringBuilder();
            for (var i = 0; i < 128; i++) body.Append("<w:p><w:r><w:drawing><a:blip r:embed='img'/></w:drawing></w:r></w:p>");
            var document = Convert(body.ToString(), imageBytes: 65536);
            Assert.Single(document.Images);
            Assert.Equal(128, document.Html.Split(new[] { "src=\"images/1.png\"" }, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("data:image", document.Html);
        }
        [Fact]
        public void OversizedPictureIsLeftOutWhileTheTextStillOpens()
        {
            var document = Convert("<w:p><w:r><w:t>Before</w:t><w:drawing><a:blip r:embed='img'/></w:drawing><w:t>After</w:t></w:r></w:p>", imageBytes: PreviewImages.ImageLimit + 1);
            Assert.Empty(document.Images);
            Assert.Contains("Before", document.Html); Assert.Contains("After", document.Html);
            Assert.Contains("<span class=\"omitted\">Picture omitted</span>", document.Html);
            Assert.DoesNotContain("<img", document.Html);
        }
        [Fact]
        public void PicturesShareOneBudget()
        {
            var images = new PreviewImages(); var picture = new byte[PreviewImages.ImageLimit];
            var added = 0;
            while (images.Add("picture" + added, picture, "image/png") != null) added++;
            Assert.Equal(PreviewImages.TotalLimit / PreviewImages.ImageLimit, added);
            Assert.Null(images.Add("small", new byte[1], "image/png"));
            Assert.Equal("images/1.png", images.Find("picture0"));
            Assert.Null(images.Find("small"));
        }
        [Fact]
        public void AggregateEncodedTextHasAnOutputBudget()
        {
            var paragraph = "<w:p><w:r><w:t>" + new string('&', 200000).Replace("&", "&amp;") + "</w:t></w:r></w:p>";
            var body = new StringBuilder(); for (var i = 0; i < 10; i++) body.Append(paragraph);
            Assert.Throws<PreviewLimitException>(() => Render(body.ToString()));
        }
        [Fact]
        public void LongCourseDocumentWithManyElementsStillRenders()
        {
            // A real 427 KB course DOCX has about 5 MB of XML and more than 200 000 nodes.
            var body = new StringBuilder();
            for (var i = 0; i < 70000; i++) body.Append("<w:p><w:r><w:t>Line ").Append(i).Append("</w:t></w:r></w:p>");
            var html = Render(body.ToString());
            Assert.Contains("Line 0", html); Assert.Contains("Line 69999", html);
        }
        [Fact]
        public void OrdinaryNestedContentAndImageStillRender()
        {
            var html = Render("<w:sdt><w:sdtContent><w:p><w:r><w:t>Readable</w:t><w:drawing><a:blip r:embed='img'/></w:drawing></w:r></w:p></w:sdtContent></w:sdt>", image: true);
            Assert.Contains("Readable", html); Assert.Contains("src=\"images/1.png\"", html);
        }
        [Fact]
        public void CancelledDocumentStopsBeforeConversion()
        { Assert.Throws<OperationCanceledException>(() => Render("<w:p/>", new CancellationToken(true))); }
        [Fact]
        public void TextReadsOnlyThePrefixAndOneLookaheadByte()
        {
            using (var stream = new GeneratedTextStream(1L << 30))
            {
                var result = PreviewText.Read(stream, CancellationToken.None);
                Assert.True(result.Truncated); Assert.Equal(PreviewText.ByteLimit, result.Text.Length);
                Assert.Equal(PreviewText.ByteLimit + 1, stream.BytesRead);
            }
        }
        [Fact]
        public void Utf8CharacterCutAtTheLimitDoesNotBecomeLegacyMojibake()
        {
            var bytes = new byte[PreviewText.ByteLimit + 2];
            for (var i = 0; i < PreviewText.ByteLimit - 1; i++) bytes[i] = (byte)'A';
            bytes[PreviewText.ByteLimit - 1] = 0xd0; bytes[PreviewText.ByteLimit] = 0xb6;
            using (var stream = new MemoryStream(bytes))
            {
                var result = PreviewText.Read(stream, CancellationToken.None);
                Assert.True(result.Truncated); Assert.Equal(new string('A', PreviewText.ByteLimit - 1), result.Text);
            }
        }
        [Fact]
        public void CompleteUtf8AndLegacyTextRemainReadable()
        {
            foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.GetEncoding(1251) })
                using (var stream = new MemoryStream(encoding.GetBytes("Лекция")))
                { var result = PreviewText.Read(stream, CancellationToken.None); Assert.Equal("Лекция", result.Text); Assert.False(result.Truncated); }
        }
        [Fact]
        public void CancelledTextReadDoesNotReadAnyBytes()
        {
            using (var stream = new GeneratedTextStream(1L << 30))
            { Assert.Throws<OperationCanceledException>(() => PreviewText.Read(stream, new CancellationToken(true))); Assert.Equal(0, stream.BytesRead); }
        }
        private sealed class GeneratedTextStream : Stream
        {
            private readonly long length;
            public long BytesRead { get; private set; }
            public GeneratedTextStream(long length) { this.length = length; }
            public override int Read(byte[] buffer, int offset, int count)
            { count = (int)Math.Min(count, length - BytesRead); for (var i = 0; i < count; i++) buffer[offset + i] = (byte)'a'; BytesRead += count; return count; }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => length; public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
            public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
    public class DownloadFilterCoverageTests
    {
        [Theory]
        [InlineData("notes.txt", true)] [InlineData("archive.zip", true)] [InlineData("archive.rar", true)] [InlineData("archive.7z", true)]
        [InlineData("unknown.bin", true)] [InlineData("paper.pdf", false)] [InlineData("paper.docx", false)] [InlineData("sheet.xlsx", false)]
        [InlineData("slides.pptx", false)] [InlineData("photo.png", false)]
        public void OtherCoversEveryTypeWithoutItsOwnFilter(string name, bool expected)
        { Assert.Equal(expected, FileKinds.Matches(new DownloadRecord { FileName = name }, "", FileKind.Other)); }
    }
}
