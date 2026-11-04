using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ReviewFixesTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        public ReviewFixesTests() { Directory.CreateDirectory(root); }
        public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

        [Fact]
        public async Task History_ShowsTheSameLocalTimeAfterRestart()
        {
            var started = new DateTime(2026, 10, 16, 10, 5, 0, DateTimeKind.Local);
            var finished = started.AddMinutes(2);
            var history = new DownloadHistory(root);
            history.Upsert(new DownloadRecord { FileName = "a.pdf", State = DownloadState.Completed, Started = started, Finished = finished });
            await history.SaveAsync();
            var loaded = new DownloadHistory(root); loaded.Load();
            var item = loaded.Items.Single();
            Assert.Equal(started, item.Started); Assert.Equal(DateTimeKind.Local, item.Started.Kind);
            Assert.Equal(finished, item.Finished); Assert.Equal("16.10.2026 10:07", item.Finished.Value.ToString("dd.MM.yyyy HH:mm"));
        }

        private static bool HasLoneSurrogate(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i])) { if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return true; i++; }
                else if (char.IsLowSurrogate(value[i])) return true;
            }
            return false;
        }

        [Fact]
        public void SafeName_NeverSplitsAnEmojiWhenShortened()
        {
            var name = FileNames.Safe(new string('а', 145) + "😀😀 конспект.pdf");
            Assert.False(HasLoneSurrogate(name));
            Assert.EndsWith(".pdf", name); Assert.True(name.Length <= 150);
        }

        [Fact]
        public void PinTitle_NeverSplitsAnEmojiWhenShortened()
        {
            var title = Shortcuts.CleanTitle(new string('б', 119) + "😀 раздел");
            Assert.False(HasLoneSurrogate(title));
            Assert.Equal(new string('б', 119), title);
        }

        [Fact]
        public void CompoundFile_LoopingDirectoryChainStopsWithinTheFileSize()
        {
            // Header + FAT sector 0 + directory sector 1 whose FAT entry points back to itself.
            var data = new byte[512 * 3];
            for (var i = 0x4C; i < 512; i += 4) BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(data, i);
            BitConverter.GetBytes(0xE011CFD0u).CopyTo(data, 0); BitConverter.GetBytes(0xE11AB1A1u).CopyTo(data, 4);
            BitConverter.GetBytes((ushort)9).CopyTo(data, 0x1E); BitConverter.GetBytes((ushort)6).CopyTo(data, 0x20);
            BitConverter.GetBytes(1u).CopyTo(data, 0x2C); BitConverter.GetBytes(1u).CopyTo(data, 0x30);
            BitConverter.GetBytes(4096u).CopyTo(data, 0x38); BitConverter.GetBytes(0xFFFFFFFEu).CopyTo(data, 0x3C);
            BitConverter.GetBytes(0xFFFFFFFEu).CopyTo(data, 0x44); BitConverter.GetBytes(0u).CopyTo(data, 0x4C);
            for (var i = 512; i < 1024; i += 4) BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(data, i);
            BitConverter.GetBytes(0xFFFFFFFDu).CopyTo(data, 512); BitConverter.GetBytes(1u).CopyTo(data, 516);
            data[1024 + 66] = 5;
            var error = Assert.Throws<InvalidDataException>(() => new CompoundFile(data));
            Assert.Equal("Damaged compound file", error.Message);
        }

        // A character style whose properties refer to the style itself (sprm 0x4A30) must not recurse without end:
        // a stack overflow cannot be caught and would close the app while previewing the file.
        [Fact]
        public void LegacyWord_SelfReferencingCharacterStyleRendersWithoutEndlessRecursion()
        {
            var file = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "synthetic.doc"));
            var container = new CompoundFile(file);
            var word = container.Stream("WordDocument");
            var flags = BitConverter.ToUInt16(word, 0x0A);
            var table = container.Stream((flags & 0x0200) != 0 ? "1Table" : "0Table");
            var lw = 34 + BitConverter.ToUInt16(word, 32) * 2 + 2; var blob = lw + BitConverter.ToUInt16(word, lw - 2) * 4 + 2;
            int offset = BitConverter.ToInt32(word, blob + 8), size = BitConverter.ToInt32(word, blob + 12);
            int header = BitConverter.ToUInt16(table, offset), count = BitConverter.ToUInt16(table, offset + 2), baseLength = BitConverter.ToUInt16(table, offset + 4);
            int pos = offset + 2 + header, end = offset + size, target = -1, index = -1;
            for (var i = 0; i < count && pos + 2 <= end && target < 0; i++)
            {
                int bytes = BitConverter.ToUInt16(table, pos), start = pos + 2; pos = start + bytes;
                if (bytes < 10) continue;
                int kind = BitConverter.ToUInt16(table, start + 2) & 0x000F, upx = BitConverter.ToUInt16(table, start + 4) & 0x000F;
                var at = start + baseLength; at += 2 + BitConverter.ToUInt16(table, at) * 2 + 2;
                for (var u = 0; u < upx && at + 2 <= pos; u++)
                {
                    int length = BitConverter.ToUInt16(table, at), data = at + 2;
                    if ((kind == 1 && u == 1 || kind == 2 && u == 0) && length >= 4) { target = data; index = i; break; }
                    at = data + length + (length & 1);
                }
            }
            Assert.True(target > 0, "fixture has a character UPX");
            // The table stream is stored in file sectors; locate the property bytes by their surrounding content.
            var window = new byte[24]; Array.Copy(table, target - 8, window, 0, window.Length);
            var found = -1;
            for (var i = 0; i + window.Length <= file.Length && found < 0; i++)
            {
                var same = true; for (var j = 0; j < window.Length && same; j++) same = file[i + j] == window[j];
                if (same) found = i + 8;
            }
            Assert.True(found > 0, "character UPX located in the file");
            file[found] = 0x30; file[found + 1] = 0x4A; file[found + 2] = (byte)index; file[found + 3] = (byte)(index >> 8);
            var error = Record.Exception(() => DocHtml.Convert(file, new PreviewOptions("Fixture")));
            Assert.True(error == null || error is InvalidDataException, error?.GetType().Name);
        }

        [Fact]
        public void ListLetters_StayShortForDamagedStartValues()
        {
            Assert.Equal("aa", DocumentMarkup.Number(27, "lowerLetter"));
            Assert.Equal("ББ", DocumentMarkup.Number(30, "russianUpper"));
            Assert.Equal("2000000000", DocumentMarkup.Number(2000000000, "lowerLetter"));
        }

        // An image entry whose ZIP header understates its size still stops at the preview's image limit; the picture is
        // then left out and the document opens.
        [Fact]
        public void Docx_ImageWithUnderstatedSizeStopsAtTheLimit()
        {
            const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main", A = "http://schemas.openxmlformats.org/drawingml/2006/main", R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var stream = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                Action<string, byte[]> add = (path, bytes) => { using (var entry = zip.CreateEntry(path).Open()) entry.Write(bytes, 0, bytes.Length); };
                add("word/document.xml", System.Text.Encoding.UTF8.GetBytes("<w:document xmlns:w=\"" + W + "\" xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\"><w:body><w:p><w:r><w:drawing><a:blip r:embed=\"rId1\"/></w:drawing></w:r></w:p></w:body></w:document>"));
                add("word/_rels/document.xml.rels", System.Text.Encoding.UTF8.GetBytes("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"media/image1.png\"/></Relationships>"));
                add("word/media/image1.png", new byte[64 * 1024 * 1024]);
            }
            var data = stream.ToArray(); var name = System.Text.Encoding.ASCII.GetBytes("word/media/image1.png"); var patched = 0;
            for (var i = 0; i + 46 < data.Length; i++)
            {
                var signature = BitConverter.ToUInt32(data, i);
                int sizeAt = signature == 0x04034b50 ? 22 : signature == 0x02014b50 ? 24 : -1, nameAt = signature == 0x04034b50 ? 30 : 46;
                if (sizeAt < 0 || i + nameAt + name.Length > data.Length) continue;
                var same = true; for (var j = 0; j < name.Length && same; j++) same = data[i + nameAt + j] == name[j];
                if (!same) continue;
                BitConverter.GetBytes(100).CopyTo(data, i + sizeAt); patched++;
            }
            Assert.Equal(2, patched);
            using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(data), System.IO.Compression.ZipArchiveMode.Read))
            {
                Assert.Equal(100, archive.GetEntry("word/media/image1.png").Length);
                var document = DocxHtml.Convert(archive, new PreviewOptions("Fixture", null, "Picture omitted"));
                Assert.Empty(document.Images);
                Assert.Contains("<span class=\"omitted\">Picture omitted</span>", document.Html);
            }
        }

        // Like the .NET Framework HTTP response stream: the token is ignored once a read is pending.
        private sealed class StalledStream : Stream
        {
            private readonly TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
            public bool Closed { get; private set; }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { return pending.Task; }
            protected override void Dispose(bool disposing) { Closed = true; pending.TrySetException(new ObjectDisposedException("response")); base.Dispose(disposing); }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }

        [Fact]
        public async Task StalledDownloadRead_EndsAsCanceledWhenTheTokenFires()
        {
            using (var stream = new StalledStream())
            using (var cancel = new CancellationTokenSource())
            {
                var read = CancellableRead.ReadAsync(stream, new byte[16], 0, 16, cancel.Token);
                Assert.False(read.IsCompleted);
                cancel.Cancel();
                var finished = await Task.WhenAny(read, Task.Delay(5000));
                Assert.Same(read, finished);
                await Assert.ThrowsAsync<OperationCanceledException>(() => read);
                Assert.True(stream.Closed);
            }
        }
    }
}
