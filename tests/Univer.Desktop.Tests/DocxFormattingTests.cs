using System.IO;
using System.IO.Compression;
using System.Text;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class DocxFormattingTests
    {
        private const string Ns = "xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'";
        private static string Render(string body, string styles = null, string numbering = null)
        {
            var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                Write(zip, "word/document.xml", "<w:document " + Ns + "><w:body>" + body + "</w:body></w:document>");
                if (styles != null) Write(zip, "word/styles.xml", "<w:styles " + Ns + ">" + styles + "</w:styles>");
                if (numbering != null) Write(zip, "word/numbering.xml", "<w:numbering " + Ns + ">" + numbering + "</w:numbering>");
            }
            memory.Position = 0;
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
            {
                var html = DocxHtml.Convert(zip, new PreviewOptions("Fixture")).Html;
                return html.Substring(html.IndexOf("<body>", System.StringComparison.Ordinal));
            }
        }
        private static void Write(ZipArchive zip, string name, string text)
        { using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); }
        [Fact]
        public void ParagraphAndCharacterStylesApplyThroughBasedOn()
        {
            var styles = "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val='24'/></w:rPr></w:rPrDefault></w:docDefaults>" +
                "<w:style w:type='paragraph' w:default='1' w:styleId='Normal'><w:name w:val='Normal'/></w:style>" +
                "<w:style w:type='paragraph' w:styleId='Centered'><w:name w:val='Centered'/><w:basedOn w:val='Normal'/><w:pPr><w:jc w:val='center'/></w:pPr><w:rPr><w:b/></w:rPr></w:style>" +
                "<w:style w:type='paragraph' w:styleId='Child'><w:name w:val='Child'/><w:basedOn w:val='Centered'/><w:rPr><w:i/></w:rPr></w:style>" +
                "<w:style w:type='character' w:styleId='Accent'><w:name w:val='Accent'/><w:rPr><w:color w:val='C00000'/></w:rPr></w:style>" +
                "<w:style w:type='paragraph' w:styleId='Loop'><w:name w:val='Loop'/><w:basedOn w:val='Loop'/></w:style>";
            var body = Render("<w:p><w:pPr><w:pStyle w:val='Child'/></w:pPr><w:r><w:t>Title</w:t></w:r></w:p>" +
                "<w:p><w:r><w:rPr><w:rStyle w:val='Accent'/></w:rPr><w:t>Red</w:t></w:r><w:r><w:rPr><w:b w:val='0'/></w:rPr><w:t> plain</w:t></w:r></w:p>" +
                "<w:p><w:pPr><w:pStyle w:val='Loop'/></w:pPr><w:r><w:t>Cycle</w:t></w:r></w:p>", styles);
            Assert.Contains("<p style=\"text-align:center\"><b><i>Title</i></b></p>", body);
            Assert.Contains("<span style=\"color:#c00000\">Red</span> plain", body);
            Assert.Contains("<p>Cycle</p>", body);
        }
        [Fact]
        public void NumberingFollowsLevelsFormatsAndRestarts()
        {
            var numbering = "<w:abstractNum w:abstractNumId='0'>" +
                "<w:lvl w:ilvl='0'><w:start w:val='1'/><w:numFmt w:val='decimal'/><w:lvlText w:val='%1.'/><w:pPr><w:ind w:left='720' w:hanging='360'/></w:pPr></w:lvl>" +
                "<w:lvl w:ilvl='1'><w:start w:val='1'/><w:numFmt w:val='lowerLetter'/><w:lvlText w:val='%1.%2)'/></w:lvl></w:abstractNum>" +
                "<w:abstractNum w:abstractNumId='1'><w:lvl w:ilvl='0'><w:numFmt w:val='bullet'/><w:lvlText w:val='\uF0B7'/></w:lvl></w:abstractNum>" +
                "<w:num w:numId='1'><w:abstractNumId w:val='0'/></w:num><w:num w:numId='2'><w:abstractNumId w:val='1'/></w:num>" +
                "<w:num w:numId='3'><w:abstractNumId w:val='0'/><w:lvlOverride w:ilvl='0'><w:startOverride w:val='5'/></w:lvlOverride></w:num>";
            System.Func<string, int, string, string> item = (id, level, text) => "<w:p><w:pPr><w:numPr><w:ilvl w:val='" + level + "'/><w:numId w:val='" + id + "'/></w:numPr></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";
            var body = Render(item("1", 0, "One") + item("1", 1, "Sub") + item("1", 1, "Sub2") + item("1", 0, "Two") + item("1", 1, "Again") + item("2", 0, "Dot") + item("3", 0, "Five"), null, numbering);
            Assert.Contains("<p style=\"margin-left:48px;text-indent:-24px\"><span class=\"marker\">1.</span>One</p>", body);
            Assert.Contains("<span class=\"marker\">1.a)</span>Sub", body);
            Assert.Contains("<span class=\"marker\">1.b)</span>Sub2", body);
            Assert.Contains("<span class=\"marker\">2.</span>Two", body);
            Assert.Contains("<span class=\"marker\">2.a)</span>Again", body);
            Assert.Contains("<span class=\"marker\">•</span>Dot", body);
            Assert.Contains("<span class=\"marker\">5.</span>Five", body);
        }
        [Fact]
        public void FieldsHiddenTextBreaksAndSymbolsRenderLikeWord()
        {
            var body = Render("<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText> HYPERLINK \"https://example.com/a\" </w:instrText></w:r>" +
                "<w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>link</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r>" +
                "<w:r><w:rPr><w:vanish/></w:rPr><w:t>secret</w:t></w:r><w:r><w:t>A</w:t><w:br/><w:t>B</w:t><w:noBreakHyphen/><w:sym w:char='F0B7'/></w:r></w:p>" +
                "<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:rPr><w:caps/><w:highlight w:val='yellow'/><w:color w:val='FFFFFF'/></w:rPr><w:t>next</w:t></w:r></w:p>" +
                "<w:p><w:r><w:fldChar w:fldCharType='begin'/></w:r><w:r><w:instrText>HYPERLINK \"javascript:alert(1)\"</w:instrText></w:r><w:r><w:fldChar w:fldCharType='separate'/></w:r><w:r><w:t>unsafe</w:t></w:r><w:r><w:fldChar w:fldCharType='end'/></w:r></w:p>");
            Assert.Contains("<a href=\"https://example.com/a\">link</a>", body);
            Assert.DoesNotContain("secret", body); Assert.DoesNotContain("HYPERLINK", body);
            Assert.Contains("A<br>B&#8209;•", body);
            Assert.Contains("<hr class=\"page\"><p><span style=\"text-transform:uppercase;background:#ffff00\">next</span></p>", body);
            Assert.Contains("<p>unsafe</p>", body); Assert.DoesNotContain("javascript", body);
        }
    }
}
