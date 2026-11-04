using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Univer.Desktop.Core
{
    // Bundled read-only XLSX (XLSM/XLTX) renderer: a streaming sheet reader into SheetMarkup, like XLS.
    // Pictures, charts and comments stay with the user's own program.
    internal static class XlsxHtml
    {
        private const long SheetLimit = 64L * 1024 * 1024, PartLimit = 16L * 1024 * 1024, StringLimit = 32 * 1024 * 1024;
        public static PreviewDocument Convert(string path, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            using (var archive = ZipFile.OpenRead(path)) return Convert(archive, options, cancellation);
        }
        public static PreviewDocument Convert(ZipArchive archive, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            try { return SheetMarkup.Write(Read(archive, options, cancellation), options, cancellation); }
            catch (XmlException) { throw new InvalidDataException("Damaged workbook"); }
        }
        internal static Workbook Read(ZipArchive archive, PreviewOptions options, CancellationToken cancellation)
        {
            var workbookPart = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Not a workbook");
            var workbook = DocxHtml.ReadXml(workbookPart, PartLimit, cancellation).Root ?? throw new InvalidDataException("Not a workbook");
            var relations = OfficePackage.Relations(archive, "xl/workbook.xml", cancellation);
            var date1904 = workbook.Elements().Where(e => e.Name.LocalName == "workbookPr").Select(e => (string)e.Attribute("date1904")).Any(v => v == "1" || v == "true");
            var book = new Workbook();
            var scheme = new Dictionary<string, string>();
            var themeTarget = relations.Values.FirstOrDefault(r => r.Type.EndsWith("/theme", StringComparison.Ordinal))?.Target;
            var themeEntry = themeTarget == null ? null : archive.GetEntry(themeTarget);
            if (themeEntry != null) { try { scheme = OfficeColors.Scheme(DocxHtml.ReadXml(themeEntry, PartLimit, cancellation)); } catch (XmlException) { } }
            var stylesTarget = relations.Values.FirstOrDefault(r => r.Type.EndsWith("/styles", StringComparison.Ordinal))?.Target ?? "xl/styles.xml";
            Styles(archive.GetEntry(stylesTarget), book, scheme, options.Culture, cancellation);
            var stringsTarget = relations.Values.FirstOrDefault(r => r.Type.EndsWith("/sharedStrings", StringComparison.Ordinal))?.Target ?? "xl/sharedStrings.xml";
            var strings = SharedStrings(archive.GetEntry(stringsTarget), cancellation);
            var sheets = workbook.Elements().FirstOrDefault(e => e.Name.LocalName == "sheets");
            var stored = 0;
            foreach (var sheet in sheets?.Elements().Where(e => e.Name.LocalName == "sheet") ?? Enumerable.Empty<XElement>())
            {
                var state = (string)sheet.Attribute("state");
                if (state == "hidden" || state == "veryHidden") continue;
                var id = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                OfficePackage.Relation relation;
                if (id == null || !relations.TryGetValue(id, out relation) || !relation.Type.EndsWith("/worksheet", StringComparison.Ordinal)) continue;
                var entry = archive.GetEntry(relation.Target);
                if (entry == null) continue;
                var data = new SheetData { Name = (string)sheet.Attribute("name") ?? "" };
                stored += Sheet(entry, data, book, strings, date1904, options, cancellation, stored);
                book.Sheets.Add(data);
            }
            return book;
        }
        private static void Styles(ZipArchiveEntry entry, Workbook book, Dictionary<string, string> scheme, CultureInfo culture, CancellationToken cancellation)
        {
            book.Styles.Add(new SheetStyle { Format = "General" });
            if (entry == null) return;
            XElement root;
            try { root = DocxHtml.ReadXml(entry, PartLimit, cancellation).Root; }
            catch (XmlException) { return; /* Damaged styles only refine formatting; the values still show. */ }
            if (root == null) return;
            Func<XElement, string, XElement> child = (parent, name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            Func<XElement, string, IEnumerable<XElement>> children = (parent, name) => parent?.Elements().Where(e => e.Name.LocalName == name) ?? Enumerable.Empty<XElement>();
            var palette = OfficeColors.Palette();
            var indexed = children(child(child(root, "colors"), "indexedColors"), "rgbColor").ToList();
            for (var i = 0; i < indexed.Count && i < palette.Length; i++) { var value = OfficeColors.FromArgb((string)indexed[i].Attribute("rgb")); if (value != null) palette[i] = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
            var formats = new Dictionary<int, string>();
            foreach (var format in children(child(root, "numFmts"), "numFmt"))
            { int id; if (int.TryParse((string)format.Attribute("numFmtId"), out id)) formats[id] = (string)format.Attribute("formatCode") ?? "General"; }
            var fonts = children(child(root, "fonts"), "font").ToList();
            var fills = children(child(root, "fills"), "fill").ToList();
            book.Styles.Clear();
            foreach (var xf in children(child(root, "cellXfs"), "xf"))
            {
                var style = new SheetStyle();
                int formatId; int.TryParse((string)xf.Attribute("numFmtId"), out formatId);
                string code; style.Format = formats.TryGetValue(formatId, out code) ? code : CellFormat.Builtin(formatId, culture);
                int fontId;
                if (int.TryParse((string)xf.Attribute("fontId"), out fontId) && fontId >= 0 && fontId < fonts.Count)
                {
                    var font = fonts[fontId];
                    style.Bold = On(child(font, "b")); style.Italic = On(child(font, "i")); style.Strike = On(child(font, "strike"));
                    var underline = child(font, "u"); style.Underline = underline != null && (string)underline.Attribute("val") != "none";
                    style.Color = Color(child(font, "color"), palette, scheme, true);
                }
                int fillId;
                if (int.TryParse((string)xf.Attribute("fillId"), out fillId) && fillId >= 0 && fillId < fills.Count)
                {
                    var pattern = child(fills[fillId], "patternFill");
                    var type = (string)pattern?.Attribute("patternType");
                    if (type != null && type != "none") style.Fill = Color(child(pattern, "fgColor"), palette, scheme, false) ?? Color(child(pattern, "bgColor"), palette, scheme, false);
                    if (style.Fill == "ffffff") style.Fill = null;
                }
                var alignment = child(xf, "alignment");
                if (alignment != null)
                {
                    switch ((string)alignment.Attribute("horizontal"))
                    {
                        case "left": case "fill": style.Align = 1; break;
                        case "center": case "centerContinuous": case "distributed": style.Align = 2; break;
                        case "right": style.Align = 3; break;
                        case "justify": style.Align = 4; break;
                    }
                    style.Wrap = On(alignment.Attribute("wrapText"));
                }
                book.Styles.Add(style);
            }
            if (book.Styles.Count == 0) book.Styles.Add(new SheetStyle { Format = "General" });
        }
        private static bool On(XElement element)
        {
            if (element == null) return false;
            var value = (string)element.Attribute("val");
            return value == null || value == "1" || value == "true";
        }
        private static bool On(XAttribute attribute) { return attribute != null && (attribute.Value == "1" || attribute.Value == "true"); }
        // Automatic and default text colours (black) are left to the page, so text keeps the viewer's ink.
        private static string Color(XElement color, int[] palette, Dictionary<string, string> scheme, bool text)
        {
            if (color == null || (string)color.Attribute("auto") == "1") return null;
            string value = null;
            var rgb = (string)color.Attribute("rgb"); int index;
            if (rgb != null) value = OfficeColors.FromArgb(rgb);
            else if (int.TryParse((string)color.Attribute("indexed"), out index)) { if (index < 64) value = OfficeColors.Hex(palette[index]); }
            else if (int.TryParse((string)color.Attribute("theme"), out index)) value = OfficeColors.SheetTheme(scheme, index);
            double tint;
            if (value != null && double.TryParse((string)color.Attribute("tint"), NumberStyles.Float, CultureInfo.InvariantCulture, out tint)) value = OfficeColors.Tint(value, tint);
            if (text && value == "000000") return null;
            return value;
        }
        private static List<string> SharedStrings(ZipArchiveEntry entry, CancellationToken cancellation)
        {
            var result = new List<string>();
            if (entry == null) return result;
            if (entry.Length > PartLimit * 4) throw new PreviewLimitException();
            var total = 0;
            using (var stream = entry.Open()) using (var reader = XmlReader.Create(stream, DocxHtml.XmlSettings(PartLimit * 4)))
            {
                var text = new System.Text.StringBuilder(); var inItem = false; var phonetic = 0;
                reader.Read();
                while (!reader.EOF)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (reader.Depth > 32) throw new PreviewLimitException();
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.LocalName == "si") { inItem = true; text.Clear(); if (reader.IsEmptyElement) { result.Add(""); inItem = false; } }
                        else if (reader.LocalName == "rPh" && !reader.IsEmptyElement) phonetic++;
                        else if (reader.LocalName == "t" && inItem && phonetic == 0 && !reader.IsEmptyElement)
                        {
                            // Leaves the reader on the node after </t>, which the loop then looks at.
                            var value = reader.ReadElementContentAsString();
                            total += value.Length; if (total > StringLimit) throw new PreviewLimitException();
                            text.Append(value);
                            continue;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.LocalName == "rPh") phonetic = Math.Max(0, phonetic - 1);
                        else if (reader.LocalName == "si" && inItem) { result.Add(text.ToString()); inItem = false; }
                    }
                    reader.Read();
                }
            }
            return result;
        }
        // Returns the number of cells stored.
        private static int Sheet(ZipArchiveEntry entry, SheetData sheet, Workbook book, List<string> strings, bool date1904, PreviewOptions options, CancellationToken cancellation, int alreadyStored)
        {
            if (entry.Length > SheetLimit) throw new PreviewLimitException();
            var stored = 0; var culture = options.Culture;
            using (var stream = entry.Open()) using (var reader = XmlReader.Create(stream, DocxHtml.XmlSettings(SheetLimit * 4)))
            {
                int row = -1, column = -1;
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.Depth > 64) throw new PreviewLimitException();
                    switch (reader.LocalName)
                    {
                        case "col":
                            int min, max; double width;
                            if (int.TryParse(reader.GetAttribute("min"), out min) && int.TryParse(reader.GetAttribute("max"), out max) && min >= 1 && max >= min)
                            {
                                var hidden = reader.GetAttribute("hidden") == "1" || reader.GetAttribute("hidden") == "true";
                                var known = double.TryParse(reader.GetAttribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out width);
                                for (var c = min - 1; c < Math.Min(max, SheetMarkup.ColumnLimit + 1); c++)
                                {
                                    if (hidden || known && width <= 0) sheet.HiddenColumns.Add(c);
                                    if (known) sheet.Widths[c] = width;
                                }
                            }
                            break;
                        case "row":
                            int number;
                            row = int.TryParse(reader.GetAttribute("r"), out number) && number >= 1 ? number - 1 : row + 1;
                            column = -1;
                            if (reader.GetAttribute("hidden") == "1" || reader.GetAttribute("hidden") == "true") sheet.HiddenRows.Add(row);
                            break;
                        case "c":
                            int r, c2;
                            if (SheetMarkup.Reference(reader.GetAttribute("r"), out r, out c2)) { row = r; column = c2; } else column++;
                            if (row < 0) row = 0;
                            int styleIndex; if (!int.TryParse(reader.GetAttribute("s"), out styleIndex) || styleIndex < 0 || styleIndex >= book.Styles.Count) styleIndex = 0;
                            var type = reader.GetAttribute("t") ?? "n";
                            var cell = Cell(reader, type, styleIndex, book, strings, date1904, options, culture);
                            if (cell != null && (cell.Text.Length > 0 || book.Styles[styleIndex].Fill != null))
                            {
                                if (alreadyStored + ++stored > SheetMarkup.StoredCellLimit) throw new PreviewLimitException();
                                sheet.Cells[SheetData.Key(row, column)] = cell;
                            }
                            break;
                        case "mergeCell":
                            var reference = reader.GetAttribute("ref") ?? ""; var parts = reference.Split(':');
                            int top, left, bottom, right;
                            if (parts.Length == 2 && SheetMarkup.Reference(parts[0], out top, out left) && SheetMarkup.Reference(parts[1], out bottom, out right) && bottom >= top && right >= left)
                                sheet.Merges.Add(new SheetRange { Top = top, Left = left, Bottom = bottom, Right = right });
                            break;
                    }
                }
            }
            return stored;
        }
        // Reads one c element to its end; null for an empty cell.
        private static SheetCell Cell(XmlReader reader, string type, int style, Workbook book, List<string> strings, bool date1904, PreviewOptions options, CultureInfo culture)
        {
            string value = null, inline = null;
            if (!reader.IsEmptyElement)
            {
                // Ends on the cell's own end tag, so the sheet loop continues with the next node.
                var depth = reader.Depth;
                reader.Read();
                while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "v" && !reader.IsEmptyElement) { value = reader.ReadElementContentAsString(); continue; }
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "is") inline = InlineText(reader);
                    reader.Read();
                }
            }
            var cell = new SheetCell { Style = style, Text = "" };
            switch (type)
            {
                case "s":
                    int index;
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index >= 0 && index < strings.Count) cell.Text = strings[index];
                    break;
                case "inlineStr": cell.Text = inline ?? ""; break;
                case "str": cell.Text = value ?? ""; break;
                case "b": cell.Kind = CellKind.Logical; if (value != null) cell.Text = SheetMarkup.Logical(value.Trim() == "1", options); break;
                case "e": cell.Kind = CellKind.Error; cell.Text = value ?? ""; break;
                case "d":
                    DateTime date;
                    if (value != null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date))
                    {
                        var serial = (date - (date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 30))).TotalDays;
                        cell.Kind = CellKind.Number; cell.Text = CellFormat.Format(serial, book.Styles[style].Format, date1904, culture);
                    }
                    else cell.Text = value ?? "";
                    break;
                default:
                    double number;
                    if (value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    { cell.Kind = CellKind.Number; cell.Text = CellFormat.Format(number, book.Styles[style].Format, date1904, culture); }
                    else if (value != null) cell.Text = value;
                    break;
            }
            return value == null && inline == null && book.Styles[style].Fill == null ? null : cell;
        }
        private static string InlineText(XmlReader reader)
        {
            // Ends on </is>.
            if (reader.IsEmptyElement) return "";
            var text = new System.Text.StringBuilder(); var depth = reader.Depth; var phonetic = 0;
            reader.Read();
            while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t" && phonetic == 0 && !reader.IsEmptyElement) { text.Append(reader.ReadElementContentAsString()); continue; }
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "rPh" && !reader.IsEmptyElement) phonetic++;
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "rPh") phonetic--;
                reader.Read();
            }
            return text.ToString();
        }
    }
}
