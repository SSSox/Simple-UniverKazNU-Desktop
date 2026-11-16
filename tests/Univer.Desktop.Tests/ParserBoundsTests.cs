using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // Files come from the site, so a crafted one must cost the preview a bounded amount of work: no recursion as deep as
    // the file says, and no loop whose length the file multiplies. Each case here used to end the process or never end.
    public sealed class ParserBoundsTests
    {
        private static readonly PreviewOptions Options = new PreviewOptions("Fixture", "Table hint");
        private static string Fixture(string name) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name); }
        // Runs on a pool thread, as the preview does, and fails instead of waiting for work that does not end.
        private static T Within<T>(int seconds, Func<CancellationToken, T> work)
        {
            using (var stop = new CancellationTokenSource())
            {
                var task = Task.Run(() => work(stop.Token));
                if (!task.Wait(TimeSpan.FromSeconds(seconds))) { stop.Cancel(); Assert.Fail("the parser did not finish in " + seconds + " s"); }
                return task.Result;
            }
        }
        private static void Put(byte[] data, int at, int value) { BitConverter.GetBytes(value).CopyTo(data, at); }
        private static void Put16(byte[] data, int at, int value) { BitConverter.GetBytes((ushort)value).CopyTo(data, at); }

        // ---- PowerPoint 97-2003 ----
        private static byte[] Record(int type, bool container, params byte[][] parts)
        {
            var body = parts.SelectMany(p => p).ToArray(); var record = new byte[8 + body.Length];
            Put16(record, 0, container ? 0xF : 0); Put16(record, 2, type); Put(record, 4, body.Length); body.CopyTo(record, 8);
            return record;
        }
        // Shape groups nested one inside another, the innermost holding the given records.
        private static byte[] Nested(int depth, byte[] inner)
        {
            var data = new byte[depth * 8 + inner.Length];
            for (var i = 0; i < depth; i++) { Put16(data, i * 8, 0xF); Put16(data, i * 8 + 2, 0xF003); Put(data, i * 8 + 4, data.Length - (i + 1) * 8); }
            inner.CopyTo(data, depth * 8);
            return data;
        }
        private static byte[] TextShape(string text)
        { return Record(0xF004, true, Record(0xF00D, true, Record(0x0F9F, false, BitConverter.GetBytes(4)), Record(0x0FA0, false, System.Text.Encoding.Unicode.GetBytes(text)))); }
        // A one-slide PowerPoint Document stream around the slide's shape tree, and its Current User stream.
        private static byte[] Presentation(byte[] tree, out byte[] current, int unusedEntries = 0, bool selfLoop = false)
        {
            var persist = new byte[20]; Put(persist, 0, 2);
            var document = Record(0x03E8, true, Record(0x0FF0, true, Record(0x03F3, false, persist)));
            var slide = Record(0x03EE, true, Record(0x040C, true, Record(0xF002, true, Record(0xF003, true, tree))));
            var entries = new List<byte>();
            entries.AddRange(BitConverter.GetBytes(1 | 2 << 20)); entries.AddRange(BitConverter.GetBytes(0)); entries.AddRange(BitConverter.GetBytes(document.Length));
            for (var i = 0; i < unusedEntries; i++) { entries.AddRange(BitConverter.GetBytes(100 | 4095 << 20)); entries.AddRange(new byte[4095 * 4]); }
            var directory = Record(0x1772, false, entries.ToArray());
            var edit = new byte[28]; var editAt = document.Length + slide.Length + directory.Length;
            Put(edit, 8, selfLoop ? editAt : 0); Put(edit, 12, document.Length + slide.Length); Put(edit, 16, 1);
            current = new byte[20]; Put(current, 12, unchecked((int)0xE391C05F)); Put(current, 16, editAt);
            return document.Concat(slide).Concat(directory).Concat(Record(0x0FF5, false, edit)).ToArray();
        }
        private static string Slides(byte[] tree)
        {
            byte[] current; var document = Presentation(tree, out current);
            return Within(20, stop => SlideMarkup.Write(PptHtml.Read(document, current, null, stop), Options, stop).Html);
        }

        [Fact]
        public void Ppt_GroupsNestedWithoutEndDoNotOverflowTheStack()
        {
            // 100 000 groups inside one another: recursion that deep is a stack overflow, which no handler can catch.
            var html = Slides(Nested(100000, new byte[0]));
            Assert.Contains("class=\"slide\"", html);
        }

        [Fact]
        public void Ppt_GroupsOfARealFileKeepTheirText()
        {
            var head = Record(0xF004, true);
            Assert.Contains("Вложенный текст", Slides(Nested(16, head.Concat(TextShape("Вложенный текст")).ToArray())));
            Assert.DoesNotContain("Слишком глубоко", Slides(Nested(17, head.Concat(TextShape("Слишком глубоко")).ToArray())));
        }

        [Fact]
        public void Ppt_EditThatNamesItselfIsRejectedWithoutRereadingTheDirectory()
        {
            // Four million directory entries, and a last edit whose "previous edit" is itself.
            byte[] current; var document = Presentation(new byte[0], out current, 1000, true);
            var error = Within(15, stop => Xunit.Record.Exception(() => PptHtml.Read(document, current, null, stop)));
            Assert.IsType<InvalidDataException>(error);
        }

        [Fact]
        public void Ppt_DirectoriesLaidOverOneAnotherAreNotReadOncePerEdit()
        {
            // 4 000 edits, each naming its own directory, and every directory runs to the end of the same 4 MB of entries.
            const int count = 4000;
            byte[] current; var start = Presentation(new byte[0], out current);
            var shared = new byte[250 * 4096 * 4];
            for (var at = 0; at < shared.Length; at += 4096 * 4) Put(shared, at, 100 | 4095 << 20);
            var headers = new byte[count * 8];
            for (var i = 0; i < count; i++) { Put16(headers, i * 8 + 2, 0x1772); Put(headers, i * 8 + 4, (count - 1 - i) * 8 + shared.Length); }
            var edits = new List<byte>(); var first = start.Length + headers.Length + shared.Length;
            for (var i = 0; i < count; i++)
            {
                var edit = new byte[28];
                Put(edit, 8, i == 0 ? 0 : first + (i - 1) * 36); Put(edit, 12, start.Length + i * 8); Put(edit, 16, 1);
                edits.AddRange(Record(0x0FF5, false, edit));
            }
            Put(current, 16, first + (count - 1) * 36);
            var document = start.Concat(headers).Concat(shared).Concat(edits).ToArray();
            var error = Within(15, stop => Xunit.Record.Exception(() => PptHtml.Read(document, current, null, stop)));
            Assert.IsType<InvalidDataException>(error);
        }

        [Fact]
        public void Ppt_LaterEditWithItsOwnDirectoryReplacesTheSlide()
        {
            // A file saved twice: the second edit's directory names a new copy of the slide, the first still lists the old.
            var head = Record(0xF004, true);
            byte[] current; var start = Presentation(head.Concat(TextShape("Старый текст")).ToArray(), out current);
            var slide = Record(0x03EE, true, Record(0x040C, true, Record(0xF002, true, Record(0xF003, true, head.Concat(TextShape("Новый текст")).ToArray()))));
            var directory = Record(0x1772, false, BitConverter.GetBytes(2 | 1 << 20), BitConverter.GetBytes(start.Length));
            var edit = new byte[28]; Put(edit, 8, BitConverter.ToInt32(current, 16)); Put(edit, 12, start.Length + slide.Length); Put(edit, 16, 1);
            Put(current, 16, start.Length + slide.Length + directory.Length);
            var document = start.Concat(slide).Concat(directory).Concat(Record(0x0FF5, false, edit)).ToArray();
            var html = Within(20, stop => SlideMarkup.Write(PptHtml.Read(document, current, null, stop), Options, stop).Html);
            Assert.Contains("Новый текст", html);
            Assert.DoesNotContain("Старый текст", html);
        }

        // ---- Word 97-2003 ----
        private sealed class Word
        {
            public byte[] Document, Table, Data; public int Lengths, Blob;
            public void Pair(int index, int offset, int size) { Put(Document, Blob + index * 8, offset); Put(Document, Blob + index * 8 + 4, size); }
            public int Append(byte[] bytes) { var at = Table.Length; Table = Table.Concat(bytes).ToArray(); return at; }
            public string Html(int seconds = 20)
            { return Within(seconds, stop => DocHtml.Convert(Document, name => name == "Data" ? Data : Table, Options, stop).Html); }
            // Replaces the style sheet with character styles that have the given properties.
            public void Styles(int count, Func<int, byte[]> properties)
            {
                var sheet = new List<byte>(); var header = new byte[20]; Put16(header, 0, 18); Put16(header, 2, count); Put16(header, 4, 10);
                sheet.AddRange(header);
                for (var i = 0; i < count; i++)
                {
                    var sprms = properties(i); var style = new byte[2 + 10 + 4 + 2 + sprms.Length];
                    Put16(style, 0, style.Length - 2); Put16(style, 2, 0x0FFE); Put16(style, 4, 0xFFF2); Put16(style, 6, 1);
                    Put16(style, 16, sprms.Length); sprms.CopyTo(style, 18);
                    sheet.AddRange(style);
                }
                Pair(1, Append(sheet.ToArray()), sheet.Count);
            }
            // Replaces the text with 8-bit characters, mapped by a piece table of the given character positions.
            public void Text(byte[] characters, int[] positions)
            {
                var origin = Document.Length; Document = Document.Concat(characters).ToArray(); var count = positions.Length - 1;
                var clx = new byte[5 + (count + 1) * 4 + count * 8]; clx[0] = 2; Put(clx, 1, clx.Length - 5);
                for (var i = 0; i <= count; i++) Put(clx, 5 + i * 4, positions[i]);
                for (var i = 0; i < count; i++) Put(clx, 5 + (count + 1) * 4 + i * 8 + 2, 0x40000000 | origin * 2);
                Pair(33, Append(clx), clx.Length); Put(Document, Lengths + 12, characters.Length);
            }
        }
        private static Word Doc()
        {
            var container = new CompoundFile(File.ReadAllBytes(Fixture("synthetic.doc")));
            var word = (byte[])container.Stream("WordDocument").Clone();
            var lengths = 34 + BitConverter.ToUInt16(word, 32) * 2 + 2;
            return new Word
            {
                Document = word, Data = container.Stream("Data"), Lengths = lengths, Blob = lengths + BitConverter.ToUInt16(word, lengths - 2) * 4 + 2,
                Table = container.Stream((BitConverter.ToUInt16(word, 0x0A) & 0x0200) != 0 ? "1Table" : "0Table")
            };
        }
        private static byte[] Follow(int style) { return new byte[] { 0x30, 0x4A, (byte)style, (byte)(style >> 8) }; }

        [Fact]
        public void Doc_SeamReadsTheFixtureAsTheFileDoes()
        { Assert.Equal(DocHtml.Convert(Fixture("synthetic.doc"), Options).Html, Doc().Html()); }

        [Fact]
        public void Doc_ChainOfStylesNamingOneAnotherDoesNotOverflowTheStack()
        {
            // 60 000 character styles, each applying the next: resolving them one inside another is a stack overflow.
            var word = Doc(); word.Styles(60000, i => Follow((i + 1) % 60000));
            Assert.Contains("Методические указания QA", word.Html());
        }

        [Fact]
        public void Doc_StyleApplyingItselfManyTimesStops()
        {
            // Sixteen self-references fan out sixteenfold at every level of the allowed depth.
            var word = Doc(); word.Styles(4, i => Enumerable.Repeat(Follow(i), 16).SelectMany(b => b).ToArray());
            Assert.Contains("Методические указания QA", word.Html());
        }

        [Fact]
        public void Doc_OverlappingPiecesAreCopiedOnce()
        {
            // 20 000 pieces that each claim the whole text of 200 000 characters.
            var text = Enumerable.Range(0, 200000).Select(i => i % 80 == 79 ? (byte)'\r' : (byte)'x').ToArray();
            var word = Doc(); word.Text(text, Enumerable.Range(0, 40001).Select(i => i % 2 == 0 ? 0 : text.Length).ToArray());
            Assert.Contains("<p>" + new string('x', 79) + "</p>", word.Html());
        }

        [Fact]
        public void Doc_FieldsOpenedWithoutEndStayCheapToRead()
        {
            // One field left open, 100 000 more with a result inside it, then 100 000 characters of text.
            var text = new List<byte> { 0x13 };
            for (var i = 0; i < 100000; i++) { text.Add(0x13); text.Add(0x14); }
            text.AddRange(Enumerable.Repeat((byte)'a', 100000)); text.Add((byte)'\r'); text.AddRange(System.Text.Encoding.ASCII.GetBytes("after\r"));
            var word = Doc(); word.Text(text.ToArray(), new[] { 0, text.Count });
            Assert.Contains("</body>", word.Html());
        }

        // A document of the given text in runs of one character, each naming character style 10, which has these properties.
        private static string StyledRuns(byte[] text, byte[] properties)
        {
            var word = Doc(); word.Styles(16, i => properties);
            var origin = word.Document.Length; word.Text(text, new[] { 0, text.Length });
            const int runs = 100; var pages = text.Length / runs;
            var first = (word.Document.Length + 511) / 512; var formatted = new byte[first * 512 + pages * 512]; word.Document.CopyTo(formatted, 0);
            var table = new byte[(pages + 1) * 4 + pages * 4];
            for (var page = 0; page < pages; page++)
            {
                var at = (first + page) * 512;
                for (var run = 0; run <= runs; run++) Put(formatted, at + run * 4, origin + page * runs + run);
                for (var run = 0; run < runs; run++) formatted[at + (runs + 1) * 4 + run] = 253;
                formatted[at + 506] = 4; Follow(10).CopyTo(formatted, at + 507); formatted[at + 511] = runs;
                Put(table, page * 4, origin + page * runs); Put(table, (pages + 1) * 4 + page * 4, first + page);
            }
            Put(table, pages * 4, origin + text.Length);
            word.Document = formatted; word.Pair(12, word.Append(table), table.Length);
            return word.Html();
        }
        private static byte[] Lines(int length) { return Enumerable.Range(0, length).Select(i => i % 80 == 79 ? (byte)'\r' : (byte)'x').ToArray(); }

        [Fact]
        public void Doc_StyleAsLargeAsItsRecordAllowsIsNotReadForEveryRun()
        {
            // 200 000 runs, each naming a character style of 63 KB of properties.
            var html = StyledRuns(Lines(200000), Enumerable.Repeat(new byte[] { 0x35, 0x08, 1 }, 21000).SelectMany(b => b).ToArray());
            Assert.Contains(new string('x', 79) + "</", html);
            Assert.Contains("</body>", html);
        }

        [Fact]
        public void Doc_CharacterStyleOfARealSizeStillShapesItsRuns()
        {
            // Bold and italic set by the style a run names: an ordinary style, of 1 KB at most, is applied in full.
            var plain = StyledRuns(Lines(400), new byte[0]);
            var bold = StyledRuns(Lines(400), new byte[] { 0x35, 0x08, 1 });
            var padded = StyledRuns(Lines(400), Enumerable.Repeat(new byte[] { 0x36, 0x08, 0 }, 340).SelectMany(b => b).Concat(new byte[] { 0x35, 0x08, 1 }).ToArray());
            Assert.NotEqual(plain, bold);
            Assert.Equal(bold, padded);
        }

        // ---- Workbooks ----
        [Fact]
        public void Sheet_MergedAreasBeyondARealSheetStayCheapToDraw()
        {
            var book = new Workbook(); book.Styles.Add(new SheetStyle());
            var sheet = new SheetData { Name = "Лист" }; book.Sheets.Add(sheet);
            sheet.Cells[SheetData.Key(0, 0)] = new SheetCell { Text = "Начало" };
            sheet.Cells[SheetData.Key(4999, 255)] = new SheetCell { Text = "Конец" };
            // Overlapping areas that each end at the last cell, and areas as tall as the whole sheet.
            for (var top = 4999; top >= 0; top--) sheet.Merge(book, top, 0, 4999, 255);
            for (var i = 0; i < 14000; i++) sheet.Merge(book, 0, 1 + i % 200, 1048575, 255);
            // And as many small ones as a workbook keeps.
            for (var i = 0; book.Merged < SheetData.MergeLimit; i++) sheet.Merge(book, i % 5000, i % 200, i % 5000, i % 200 + 1);
            var html = Within(20, stop => SheetMarkup.Write(book, Options, stop).Html);
            Assert.Contains("Начало", html);
            Assert.Contains("rowspan=", html);
        }

        [Fact]
        public void Sheet_KeepsNoMoreMergedAreasThanARealWorkbookHas()
        {
            // The same sheet named many times over must not multiply what is kept.
            var book = new Workbook();
            for (var s = 0; s < 4; s++)
            {
                var sheet = new SheetData(); book.Sheets.Add(sheet);
                for (var i = 0; i < SheetData.MergeLimit / 2; i++) sheet.Merge(book, i, 0, i, 1);
            }
            Assert.Equal(SheetData.MergeLimit, book.Sheets.Sum(sheet => sheet.Merges.Count));
            Assert.Equal(0, book.Sheets[2].Merges.Count);
        }
    }
}
