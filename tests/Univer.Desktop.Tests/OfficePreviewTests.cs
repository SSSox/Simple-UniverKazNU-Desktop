using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // Bundled renderers on synthetic workbooks and presentations Excel and PowerPoint saved in both formats from the same
    // content (merged cells, formats, hidden parts, formulas, lists, tables, pictures).
    public class OfficePreviewTests
    {
        private static string Fixture(string name) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name); }
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "sheets", "Листы" }, { "sheetEmpty", "Лист пуст" }, { "sheetTruncated", "Показаны {0} строк и {1} столбцов" },
            { "slide", "Слайд {0} из {1}" }, { "slideEmpty", "Нет текста" }, { "htmlNote", "Без скриптов" }
        };
        private static PreviewOptions Options(string language = "ru") { return new PreviewOptions("Fixture", "Table hint", "Too large", language, Labels); }
        private static string Body(string html) { return html.Substring(html.IndexOf("<body", StringComparison.Ordinal)); }
        [Fact]
        public void LegacyAndOpenXmlWorkbooksLookTheSame()
        {
            var xlsx = XlsxHtml.Convert(Fixture("synthetic.xlsx"), Options());
            var xls = XlsHtml.Convert(Fixture("synthetic.xls"), Options());
            Assert.Equal(xlsx.Html, xls.Html);
            var page = xlsx.Html; var body = Body(page);
            Assert.Contains("<li><a href=\"#sheet-1\" data-text=\"Оценки\" aria-label=\"Оценки\"></a></li><li><a href=\"#sheet-2\" data-text=\"Расписание\"", body);
            Assert.DoesNotContain("Скрытый", body);
            Assert.DoesNotContain("Не показывать", body);
            Assert.Contains("<td colspan=\"4\" class=\"c s0\">Ведомость (синтетические данные)</td>", body);
            Assert.Contains("<td class=\"s1\">Студент</td>", body);
            Assert.Contains("<td class=\"n\">1&#160;234,50</td><td class=\"n\">87,5%</td><td class=\"n\">31.12.2025</td>", body);
            Assert.Contains("<td class=\"n s6\">-42,00</td>", body);
            Assert.Contains("<td>Итого: 1192,5</td><td class=\"c\">ИСТИНА</td>", body);
            Assert.Contains("<td class=\"w\">Длинный текст с переносом строки внутри ячейки</td><td class=\"c\">#DIV/0!</td>", body);
            Assert.Contains("<td class=\"n\">13:30</td>", body);
            // The hidden row 5 and column F are left out, as Excel shows the sheet.
            Assert.DoesNotContain("data-text=\"5\"", body);
            Assert.DoesNotContain("Скрытая строка", body);
            Assert.DoesNotContain("Скрытый столбец", body);
            // Exact colours: the header fill, red text and a theme colour with its tint, from XFEXT and THEME in the XLS.
            Assert.Contains("<style data-doc>.grid .s0{font-weight:700}.grid .s1{font-weight:700;background:#d7e6f5}.grid .s6{color:#c00000}.grid .s8{background:#b4c7e7}</style>", page);
            // Column letters and row numbers come from attributes, so find never matches them.
            Assert.Contains("<th scope=\"col\" data-text=\"A\"></th>", body);
            Assert.Contains("<th scope=\"row\" data-text=\"7\"></th>", body);
            Assert.Empty(xlsx.Images);
        }
        [Fact]
        public void EnglishWorkbooksUseEnglishNumbersAndDates()
        {
            var body = Body(XlsHtml.Convert(Fixture("synthetic.xls"), Options("en")).Html);
            Assert.Contains("<td class=\"n\">1,234.50</td>", body);
            Assert.Contains("<td class=\"c\">TRUE</td>", body);
            Assert.Equal(body, Body(XlsxHtml.Convert(Fixture("synthetic.xlsx"), Options("en")).Html));
        }
        [Fact]
        public void LegacyAndOpenXmlPresentationsLookTheSame()
        {
            var pptx = PptxHtml.Convert(Fixture("synthetic.pptx"), Options());
            var ppt = PptHtml.Convert(Fixture("synthetic.ppt"), Options());
            Assert.Equal(pptx.Html, ppt.Html);
            var body = Body(pptx.Html);
            Assert.StartsWith("<body class=\"deck\"><section class=\"slide\" id=\"slide-1\" aria-label=\"Слайд 1 из 4\"><p class=\"slide-no\" data-text=\"Слайд 1 из 4\"></p><h2>Синтетическая презентация</h2><p>Подзаголовок для проверки</p></section>", body);
            Assert.Contains("<h2>Список тем</h2><p style=\"margin-left:24px;text-indent:-24px\"><span class=\"marker\">•</span>Первый пункт</p>", body);
            Assert.Contains("<p style=\"margin-left:48px;text-indent:-24px\"><span class=\"marker\">•</span>Вложенный пункт</p>", body);
            Assert.Contains("<b>Жирный </b>и <span style=\"color:#b02020\">цветной </span>пункт", body);
            Assert.Contains("<tr><td><p><b>Неделя</b></p></td><td><p><b>Тема</b></p></td><td><p><b>Часы</b></p></td></tr>", body);
            Assert.Contains("<tr><td colspan=\"2\"><p>Итог</p></td><td><p>2</p></td></tr>", body);
            Assert.Contains("<figure><img alt=\"\" src=\"images/1.png\" style=\"width:33.3%\"></figure><p>Подпись к рисунку</p>", body);
            Assert.Equal(pptx.Images.Count, ppt.Images.Count);
            var picture = Assert.Single(ppt.Images);
            Assert.Equal("image/png", picture.MimeType);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, picture.Data.Take(4).ToArray());
            Assert.Equal(Assert.Single(pptx.Images).Data, picture.Data);
        }
        [Fact]
        public void PresentationPlaceholdersTakeTheirOrderFromTheLayout()
        {
            // The title has no position of its own (it inherits it from the layout) and is written after the body.
            var slide = "<p:sld xmlns:a='" + A + "' xmlns:p='" + P + "' xmlns:r='" + R + "'><p:cSld><p:spTree>" +
                Shape("<p:ph idx='1'/>", "<a:xfrm><a:off x='100' y='2000'/></a:xfrm>", "<a:p><a:r><a:t>Тело</a:t></a:r></a:p><a:p><a:pPr><a:buAutoNum type='arabicParenR'/></a:pPr><a:r><a:t>Раз</a:t></a:r></a:p><a:p><a:pPr><a:buAutoNum type='arabicParenR'/></a:pPr><a:r><a:t>Два</a:t></a:r></a:p>") +
                Shape("<p:ph type='title'/>", "", "<a:p><a:r><a:t>Заголовок</a:t></a:r></a:p>") +
                Shape("", "<a:xfrm><a:off x='0' y='9000'/></a:xfrm>", "<a:p><a:r><a:rPr><a:solidFill><a:srgbClr val='FFFFFF'/></a:solidFill><a:hlinkClick r:id='rLink'/></a:rPr><a:t>Белый текст</a:t></a:r></a:p>") +
                Shape("<p:ph type='sldNum' idx='12'/>", "<a:xfrm><a:off x='0' y='9500'/></a:xfrm>", "<a:p><a:r><a:t>99</a:t></a:r></a:p>") +
                "</p:spTree></p:cSld></p:sld>";
            var layout = "<p:sldLayout xmlns:a='" + A + "' xmlns:p='" + P + "'><p:cSld><p:spTree>" + Shape("<p:ph type='title'/>", "<a:xfrm><a:off x='100' y='500'/></a:xfrm>", "") + "</p:spTree></p:cSld></p:sldLayout>";
            var master = "<p:sldMaster xmlns:a='" + A + "' xmlns:p='" + P + "'><p:cSld><p:spTree/></p:cSld><p:txStyles><p:bodyStyle><a:lvl1pPr><a:buChar char='–'/></a:lvl1pPr></p:bodyStyle></p:txStyles></p:sldMaster>";
            var body = Body(Presentation(slide, layout, master).Html);
            Assert.Contains("<h2>Заголовок</h2><p style=\"margin-left:24px;text-indent:-24px\"><span class=\"marker\">–</span>Тело</p>", body);
            Assert.Contains("<span class=\"marker\">1)</span>Раз</p><p style=\"margin-left:24px;text-indent:-24px\"><span class=\"marker\">2)</span>Два</p>", body);
            // White text written for a dark slide uses the page's ink; web links stay links.
            Assert.Contains("<p><a href=\"https://example.com/slides\">Белый текст</a></p>", body);
            Assert.DoesNotContain(">99<", body);
        }
        private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main", P = "http://schemas.openxmlformats.org/presentationml/2006/main",
            R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships", Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static string Shape(string placeholder, string transform, string text)
        { return "<p:sp><p:nvSpPr><p:cNvPr id='2' name='s'/><p:cNvSpPr/><p:nvPr>" + placeholder + "</p:nvPr></p:nvSpPr><p:spPr>" + transform + "</p:spPr><p:txBody><a:bodyPr/>" + text + "</p:txBody></p:sp>"; }
        private static PreviewDocument Presentation(string slide, string layout, string master)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    Action<string, string> add = (name, text) => { using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); };
                    add("ppt/presentation.xml", "<p:presentation xmlns:p='" + P + "' xmlns:r='" + R + "'><p:sldIdLst><p:sldId id='256' r:id='rId1'/></p:sldIdLst><p:sldSz cx='9144000' cy='6858000'/></p:presentation>");
                    add("ppt/_rels/presentation.xml.rels", "<Relationships xmlns='" + Rel + "'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide' Target='slides/slide1.xml'/></Relationships>");
                    add("ppt/slides/slide1.xml", slide);
                    add("ppt/slides/_rels/slide1.xml.rels", "<Relationships xmlns='" + Rel + "'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout' Target='../slideLayouts/slideLayout1.xml'/>" +
                        "<Relationship Id='rLink' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink' Target='https://example.com/slides' TargetMode='External'/></Relationships>");
                    add("ppt/slideLayouts/slideLayout1.xml", layout);
                    add("ppt/slideLayouts/_rels/slideLayout1.xml.rels", "<Relationships xmlns='" + Rel + "'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster' Target='../slideMasters/slideMaster1.xml'/></Relationships>");
                    add("ppt/slideMasters/slideMaster1.xml", master);
                }
                memory.Position = 0;
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Read)) return PptxHtml.Convert(zip, Options());
            }
        }
        private static PreviewDocument Workbook(string sheet, string styles = null, string strings = null)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    Action<string, string> add = (name, text) => { using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); };
                    const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    add("xl/workbook.xml", "<workbook xmlns='" + main + "' xmlns:r='" + R + "'><sheets><sheet name='Лист' sheetId='1' r:id='rId1'/></sheets></workbook>");
                    add("xl/_rels/workbook.xml.rels", "<Relationships xmlns='" + Rel + "'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/>" +
                        (strings != null ? "<Relationship Id='rId2' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings' Target='sharedStrings.xml'/>" : "") + "</Relationships>");
                    add("xl/worksheets/sheet1.xml", "<worksheet xmlns='" + main + "'>" + sheet + "</worksheet>");
                    if (styles != null) add("xl/styles.xml", "<styleSheet xmlns='" + main + "'>" + styles + "</styleSheet>");
                    if (strings != null) add("xl/sharedStrings.xml", "<sst xmlns='" + main + "'>" + strings + "</sst>");
                }
                memory.Position = 0;
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Read)) return XlsxHtml.Convert(zip, Options());
            }
        }
        [Fact]
        public void SharedStringsSkipPhoneticTextAndInlineStringsShow()
        {
            var body = Body(Workbook("<sheetData><row r='1'><c r='A1' t='s'><v>0</v></c><c r='B1' t='inlineStr'><is><t>Встроенный</t></is></c><c r='C1' t='s'><v>1</v></c></row></sheetData>",
                strings: "<si><r><t>Первая </t></r><r><t>часть</t></r><rPh sb='0' eb='1'><t>ふりがな</t></rPh></si><si><t>Вторая</t></si>").Html);
            Assert.Contains("<td>Первая часть</td><td>Встроенный</td><td>Вторая</td>", body);
            Assert.DoesNotContain("ふりがな", body);
        }
        [Fact]
        public void HugeSheetsAreCutWithANote()
        {
            var rows = new StringBuilder("<sheetData>");
            for (var r = 1; r <= SheetMarkup.RowLimit + 200; r++) rows.Append("<row r='").Append(r).Append("'><c r='A").Append(r).Append("'><v>").Append(r).Append("</v></c></row>");
            rows.Append("</sheetData>");
            var body = Body(Workbook(rows.ToString()).Html);
            Assert.Contains("<p class=\"note\" data-text=\"Показаны " + SheetMarkup.RowLimit + " строк и 1 столбцов\"></p>", body);
            Assert.Equal(SheetMarkup.RowLimit, Regex.Matches(body, "<th scope=\"row\"").Count);
        }
        [Fact]
        public void EmptySheetsSayTheyAreEmpty()
        {
            Assert.Contains("<p class=\"note\" data-text=\"Лист пуст\"></p>", Workbook("<sheetData/>").Html);
        }
        [Fact]
        public void DamagedAndUnsupportedFilesFallBack()
        {
            var random = new byte[4096]; new Random(7).NextBytes(random);
            Assert.Throws<InvalidDataException>(() => XlsHtml.Convert(random, Options()));
            Assert.Throws<InvalidDataException>(() => PptHtml.Convert(random, Options()));
            // The Word fixture is a compound file without a workbook or a presentation stream.
            var doc = File.ReadAllBytes(Fixture("synthetic.doc"));
            Assert.Throws<InvalidDataException>(() => XlsHtml.Convert(doc, Options()));
            Assert.Throws<InvalidDataException>(() => PptHtml.Convert(doc, Options()));
            // Cut files: the compound file reader refuses a truncated sector chain.
            var xls = File.ReadAllBytes(Fixture("synthetic.xls"));
            Assert.Throws<InvalidDataException>(() => XlsHtml.Convert(xls.Take(xls.Length / 2).ToArray(), Options()));
            var ppt = File.ReadAllBytes(Fixture("synthetic.ppt"));
            Assert.Throws<InvalidDataException>(() => PptHtml.Convert(ppt.Take(ppt.Length / 2).ToArray(), Options()));
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) zip.CreateEntry("other.xml");
                memory.Position = 0;
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
                {
                    Assert.Throws<InvalidDataException>(() => XlsxHtml.Convert(zip, Options()));
                    Assert.Throws<InvalidDataException>(() => PptxHtml.Convert(zip, Options()));
                }
            }
            Assert.Throws<InvalidDataException>(() => Workbook("<sheetData><row><c><v>1</v></sheetData>"));
        }
        [Theory]
        [InlineData(1234.5, "General", "1234,5")]
        [InlineData(0.1 + 0.2, "General", "0,3")]
        [InlineData(123456789012.0, "General", "1,23457E+11")]
        [InlineData(1234.567, "#,##0.00", "1 234,57")]
        [InlineData(-1234.567, "#,##0.00;(#,##0.00)", "(1 234,57)")]
        [InlineData(0, "0.00;-0.00;\"нет\"", "нет")]
        [InlineData(0.256, "0.0%", "25,6%")]
        [InlineData(12345, "0.00E+00", "1,23E+04")]
        [InlineData(1500, "#,##0 \"₸\"", "1 500 ₸")]
        [InlineData(1500, "[$€-407] #,##0", "€ 1 500")]
        [InlineData(46022, "dd.mm.yyyy", "31.12.2025")]
        [InlineData(46022, "d mmmm yyyy", "31 Декабрь 2025")]
        [InlineData(46022.75, "dd.mm.yy h:mm", "31.12.25 18:00")]
        [InlineData(0.5625, "h:mm AM/PM", "1:30 PM")]
        [InlineData(1.25, "[h]:mm", "30:00")]
        [InlineData(0.000694444, "mm:ss", "01:00")]
        [InlineData(60, "dd.mm.yyyy", "29.02.1900")]
        [InlineData(61, "dd.mm.yyyy", "01.03.1900")]
        [InlineData(5, "[<0]\"минус\";[>10]\"много\";0", "5")]
        [InlineData(50, "[<0]\"минус\";[>10]\"много\";0", "много")]
        [InlineData(0.5, "# ?/?", "0,5")]
        public void NumberFormatsReadAsExcelShowsThem(double value, string code, string expected)
        {
            Assert.Equal(expected, CellFormat.Format(value, code, false, CultureInfo.GetCultureInfo("ru-RU")));
        }
        [Fact]
        public void DateSystemsAndBuiltInFormats()
        {
            var russian = CultureInfo.GetCultureInfo("ru-RU"); var english = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("01.01.1904", CellFormat.Format(0, "dd.mm.yyyy", true, russian));
            Assert.Equal("dd.mm.yyyy", CellFormat.Builtin(14, russian));
            Assert.Equal("m/d/yyyy", CellFormat.Builtin(14, english));
            Assert.Equal("12/31/2025", CellFormat.Format(46022, CellFormat.Builtin(14, english), false, english));
            Assert.True(CellFormat.IsDate("[$-419]d mmm yy;@"));
            Assert.False(CellFormat.IsDate("#,##0.00\"р.\""));
            Assert.False(CellFormat.IsDate("General"));
        }
        [Fact]
        public void WebPagesKeepTheirTextAndLoseEverythingActive()
        {
            var source = "<html><head><title>T</title><style>body{background:url(https://evil.example/x.png)}</style><script>alert(1)</script>" +
                "<link rel=stylesheet href='https://evil.example/a.css'><meta http-equiv='refresh' content='0;url=https://evil.example'></head>" +
                "<body onload=\"steal()\"><h1 style=\"color:#123456;background:url(javascript:alert(1))\">Заголовок</h1>" +
                "<p>Текст <b>жирный</b> &amp; <a href=\"javascript:alert(2)\">плохая</a> <a href=\"https://example.com/ok\" onclick=\"x()\">хорошая</a></p>" +
                "<img src=\"https://evil.example/track.gif\" alt=\"Удалённый рисунок\"><img src=\"data:image/png;base64,iVBORw0KGgo=\" width=\"20\">" +
                "<iframe src=\"https://evil.example\">рамка</iframe><form action=\"https://evil.example\"><input name=q value=secret><button>Отправить</button></form>" +
                "<table border=1><tr><td colspan=2 bgcolor=yellow>Ячейка<td>Вторая</table><svg><script>alert(3)</script></svg><p>После <font color=red size=5>шрифт</font></p>" +
                "<ul><li>Один<li>Два</ul><div><span>незакрытый";
            var document = HtmlPreview.Convert(Encoding.UTF8.GetBytes(source), Options());
            var body = Body(document.Html);
            foreach (var bad in new[] { "<script", "alert(", "onload", "onclick", "javascript:", "evil.example", "<iframe", "<form", "<input", "<svg", "url(", "secret", "<style" })
                Assert.DoesNotContain(bad, body);
            Assert.Contains("<p class=\"note\" role=\"note\" data-text=\"Без скриптов\"></p>", body);
            Assert.Contains("<h1 style=\"color:#123456\">Заголовок</h1>", body);
            Assert.Contains("<p>Текст <b>жирный</b> &amp; <a>плохая</a> <a href=\"https://example.com/ok\">хорошая</a></p>", body);
            Assert.Contains("<span class=\"omitted\">Удалённый рисунок</span><img alt=\"\" style=\"width:20px\" src=\"images/1.png\">", body);
            Assert.Contains("<div class=\"table-scroll\" tabindex=\"0\"><table><tr><td colspan=\"2\" style=\"background:#ffff00\">Ячейка</td><td>Вторая</td></tr></table></div>", body);
            Assert.Contains("Отправить", body);
            Assert.Contains("<p>После <span style=\"color:#ff0000;font-size:1.4em\">шрифт</span></p>", body);
            Assert.Contains("<ul><li>Один</li><li>Два</li></ul><div><span>незакрытый</span></div></body></html>", body);
            Assert.Equal("image/png", Assert.Single(document.Images).MimeType);
            Assert.DoesNotContain("<title>T</title>", body);
        }
        [Fact]
        public void WebPagesWithoutDeclaredCharsetReadCyrillic()
        {
            var cp1251 = Encoding.GetEncoding(1251).GetBytes("<p>Привет, мир</p>");
            Assert.Contains("<p>Привет, мир</p>", HtmlPreview.Convert(cp1251, Options()).Html);
            var declared = Encoding.GetEncoding(1251).GetBytes("<meta charset=\"windows-1251\"><p>Ёлка</p>");
            Assert.Contains("<p>Ёлка</p>", HtmlPreview.Convert(declared, Options()).Html);
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<p>Қазақша</p>")).ToArray();
            Assert.Contains("<p>Қазақша</p>", HtmlPreview.Convert(utf8, Options()).Html);
        }
        [Fact]
        public void DarkThemeKeepsTheContrastOfTheDocumentsOwnColours()
        {
            var page = PreviewTheme.Apply(XlsxHtml.Convert(Fixture("synthetic.xlsx"), Options()).Html, true);
            Assert.StartsWith("<!doctype html><html class=\"dark\">", page);
            // The light shell is untouched; the dark variables switch it.
            Assert.Contains("html.dark{--bg:#15191e;--card:#1d2229", page);
            Assert.Contains("@media print{.grid .s0{font-weight:700}.grid .s1{font-weight:700;background:#d7e6f5}", page);
            var docx = PreviewTheme.Apply(DocxHtml.Convert(Fixture("synthetic.docx"), Options()).Html, true);
            Assert.Contains("--pc:#ff0000", docx);
            Assert.DoesNotContain("style=\"color:#ff0000\"", docx);
            Assert.Equal("<html><body style=\"color:#ff0000\"></body></html>", PreviewTheme.Apply("<html><body style=\"color:#ff0000\"></body></html>", false));
            foreach (var hex in new[] { "000000", "1f3864", "c00000", "0000ff", "ffff00", "d7e6f5", "ffffff", "808080" })
            {
                var dark = PreviewTheme.Dark(hex);
                double light = Lum(hex), mapped = Lum(dark), card = PreviewTheme.Luminance(0x1d, 0x22, 0x29);
                var before = PreviewTheme.Contrast(light, 1); var after = PreviewTheme.Contrast(mapped, card);
                // Pure black clips at white; everything else keeps its contrast with the page within rounding.
                if (hex != "000000") Assert.InRange(after, before * 0.93, before * 1.07);
                else Assert.True(after > 14);
            }
            // White paper becomes the dark card, so a fill keeps its contrast with the text on it.
            var paper = int.Parse(PreviewTheme.Dark("ffffff"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.InRange(paper >> 16 & 255, 0x1c, 0x1e); Assert.InRange(paper >> 8 & 255, 0x21, 0x23); Assert.InRange(paper & 255, 0x28, 0x2a);
        }
        private static double Lum(string hex)
        {
            var value = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return PreviewTheme.Luminance(value >> 16 & 255, value >> 8 & 255, value & 255);
        }
        [Fact]
        public void TextFilesShowOnTheSameCard()
        {
            var page = PreviewPage.Text("notes.txt", "a < b\nстрока 2", "Показано начало");
            Assert.Contains("<p class=\"note\" role=\"note\" data-text=\"Показано начало\"></p><pre class=\"text\">a &lt; b\nстрока 2</pre>", page);
            Assert.StartsWith("<html class=\"dark\">", PreviewTheme.Apply(page, true).Substring("<!doctype html>".Length));
        }
    }
}
