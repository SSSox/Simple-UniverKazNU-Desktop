using System;
using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class DocHtmlTests
    {
        // The same synthetic document saved by Word as .doc and .docx: heading, bold/italic, a web link,
        // colour, size, highlight, a numbered list, a first-line indent, a picture and a table with merged cells.
        private static readonly string Doc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "synthetic.doc");
        private static readonly string Docx = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "synthetic.docx");
        private static readonly PreviewOptions Options = new PreviewOptions("Fixture", "Table hint");
        private static string Body(string html) { return html.Substring(html.IndexOf("<body>", StringComparison.Ordinal)); }
        [Fact]
        public void LegacyWordDocumentRendersWithoutOffice()
        {
            var document = DocHtml.Convert(Doc, Options);
            var body = Body(document.Html);
            Assert.Contains("<h1><span style=\"color:#2f5496\">Методические указания QA</span></h1>", body);
            Assert.Contains("Обычный текст и <b>жирный фрагмент</b> и <i>курсив</i>.", body);
            Assert.Contains("<a href=\"https://example.com/qa\">", body);
            Assert.Contains("<p style=\"text-align:center\"><span style=\"color:#ff0000\">Красный по центру</span></p>", body);
            Assert.Contains("<span style=\"font-size:1.64em\">Крупный</span> обычный", body);
            Assert.Contains("<span style=\"background:#ffff00\">Выделено</span>", body);
            Assert.Contains("<span class=\"marker\">1.</span>Первый пункт", body);
            Assert.Contains("<span class=\"marker\">2.</span>Второй пункт", body);
            Assert.Contains("<p style=\"text-indent:48px\">Абзац с отступом</p>", body);
            // The picture is served beside the page instead of being inlined into the markup.
            Assert.Contains("<img alt=\"\" style=\"width:48px\" src=\"images/1.png\">", body);
            Assert.DoesNotContain("data:image", body);
            var picture = Assert.Single(document.Images);
            Assert.Equal("images/1.png", picture.Path);
            Assert.Equal("image/png", picture.MimeType);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, new[] { picture.Data[0], picture.Data[1], picture.Data[2], picture.Data[3] });
            Assert.Contains("<td rowspan=\"2\"><p>1</p></td>", body);
            Assert.Contains("<td colspan=\"2\"><p>Итог</p></td>", body);
            Assert.Contains("<p class=\"table-help\" role=\"note\" data-text=\"Table hint\"></p>", body);
            Assert.True(body.IndexOf("</table>", StringComparison.Ordinal) < body.IndexOf("Текст после таблицы", StringComparison.Ordinal));
            Assert.DoesNotContain("HYPERLINK", body);
        }
        [Fact]
        public void DocAndDocxOfTheSameDocumentLookTheSame()
        {
            var docx = DocxHtml.Convert(Docx, Options); var doc = DocHtml.Convert(Doc, Options);
            Assert.Equal(Body(docx.Html), Body(doc.Html));
            Assert.Equal(docx.Images.Count, doc.Images.Count);
            Assert.Equal(docx.Images[0].Data, doc.Images[0].Data);
        }
        // A picture header (PICF) without a horizontal scale means 100%, not a thousandth of the goal width.
        [Fact]
        public void PictureWithoutAScaleKeepsItsWidth()
        {
            var file = File.ReadAllBytes(Doc); var found = 0;
            for (var i = 0; i + 0x22 <= file.Length; i++)
            {
                // cbHeader 0x44 at +4, mx 1000 (100%) at +0x20 and a goal width of 48 px (720 twips) at +0x1C.
                if (file[i + 4] != 0x44 || file[i + 5] != 0 || BitConverter.ToUInt16(file, i + 0x20) != 1000 || BitConverter.ToUInt16(file, i + 0x1C) != 720) continue;
                file[i + 0x20] = 0; file[i + 0x21] = 0; found++;
            }
            Assert.Equal(1, found);
            Assert.Contains("<img alt=\"\" style=\"width:48px\" src=\"images/1.png\">", DocHtml.Convert(file, Options).Html);
        }
        [Fact]
        public void NonCompoundFilesAreRejected()
        {
            Assert.Throws<InvalidDataException>(() => DocHtml.Convert(new byte[4096], Options));
            Assert.Throws<InvalidDataException>(() => DocHtml.Convert(System.Text.Encoding.ASCII.GetBytes("{\rtf1 not a binary document}"), Options));
        }
        [Fact]
        public void DamagedDocumentsFailCleanly()
        {
            var original = File.ReadAllBytes(Doc);
            var random = new Random(7);
            // Truncated or corrupted files either still render or fail with a data error, never a hang or an unrelated crash.
            foreach (var length in new[] { 512, 1024, original.Length / 2, original.Length - 100 })
            {
                var cut = new byte[length]; Array.Copy(original, cut, length);
                var error = Record.Exception(() => DocHtml.Convert(cut, Options));
                Assert.True(error == null || error is InvalidDataException || error is IOException, error?.GetType().Name);
            }
            for (var round = 0; round < 200; round++)
            {
                var copy = (byte[])original.Clone();
                for (var i = 0; i < 24; i++) copy[512 + random.Next(copy.Length - 512)] = (byte)random.Next(256);
                try { DocHtml.Convert(copy, Options); }
                catch (InvalidDataException) { }
                catch (IOException) { }
            }
        }
    }
}
