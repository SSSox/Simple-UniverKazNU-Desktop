using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Univer.Desktop.Core
{
    // Bundled read-only renderer for Excel 97-2003 .xls files (BIFF8), into the same model as XLSX through SheetMarkup.
    // Excel 95 and older, encrypted and damaged files throw InvalidDataException.
    internal static class XlsHtml
    {
        private const long FileLimit = 64L * 1024 * 1024;
        private sealed class Font { public bool Bold, Italic, Underline, Strike; public int Color = 0x7FFF; }
        private sealed class BoundSheet { public int Offset; public string Name; }
        public static PreviewDocument Convert(string path, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            if (new FileInfo(path).Length > FileLimit) throw new PreviewLimitException();
            return Convert(File.ReadAllBytes(path), options, cancellation);
        }
        public static PreviewDocument Convert(byte[] file, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            Workbook book;
            // Every structure comes from the file; any inconsistency the explicit checks miss is still a damaged workbook.
            try { book = Read(file, options, cancellation); }
            catch (IndexOutOfRangeException) { throw new InvalidDataException("Damaged workbook"); }
            catch (ArgumentException) { throw new InvalidDataException("Damaged workbook"); }
            catch (OverflowException) { throw new InvalidDataException("Damaged workbook"); }
            return SheetMarkup.Write(book, options, cancellation);
        }
        internal static Workbook Read(byte[] file, PreviewOptions options, CancellationToken cancellation)
        {
            var container = new CompoundFile(file);
            var stream = container.Stream("Workbook");
            if (stream == null) throw new InvalidDataException(container.Stream("Book") != null ? "Unsupported Excel version" : "Not an Excel workbook");
            if (stream.Length < 8 || U16(stream, 0) != 0x0809 || U16(stream, 4) != 0x0600) throw new InvalidDataException("Unsupported Excel version");
            var culture = options.Culture;
            var fonts = new List<Font>(); var formats = new Dictionary<int, string>(); var xfs = new List<byte[]>();
            var palette = OfficeColors.Palette(); var sheets = new List<BoundSheet>(); var strings = new List<string>();
            var date1904 = false;
            // Excel 2007 and later also write each format's exact colours (XFEXT) and the workbook theme (THEME), which
            // the 56-colour palette of older Excel cannot hold; with them an XLS shows the same colours as its XLSX.
            var extensions = new List<byte[]>(); var theme = new System.IO.MemoryStream();
            var position = 0;
            for (var first = true; position + 4 <= stream.Length; first = false)
            {
                cancellation.ThrowIfCancellationRequested();
                int type = U16(stream, position), length = U16(stream, position + 2), body = position + 4;
                if (body + length > stream.Length) throw new InvalidDataException("Damaged workbook");
                var next = body + length;
                switch (type)
                {
                    case 0x002F: throw new InvalidDataException("Encrypted workbook");
                    case 0x0022: date1904 = length >= 2 && U16(stream, body) == 1; break;
                    case 0x0031:
                        if (length >= 14)
                        {
                            var flags = U16(stream, body + 2);
                            fonts.Add(new Font { Italic = (flags & 2) != 0, Strike = (flags & 8) != 0, Color = U16(stream, body + 4), Bold = U16(stream, body + 6) >= 700, Underline = stream[body + 10] != 0 });
                        }
                        break;
                    case 0x041E:
                        if (length >= 5) { int end; formats[U16(stream, body)] = Unicode(stream, body + 2, next, false, out end); }
                        break;
                    case 0x00E0: if (length >= 20) { var xf = new byte[20]; Array.Copy(stream, body, xf, 0, 20); xfs.Add(xf); } break;
                    case 0x0092:
                        if (length >= 2)
                            for (var i = 0; i < U16(stream, body) && 8 + i < palette.Length && body + 2 + i * 4 + 3 <= next; i++)
                                palette[8 + i] = stream[body + 2 + i * 4] << 16 | stream[body + 3 + i * 4] << 8 | stream[body + 4 + i * 4];
                        break;
                    case 0x0085:
                        // Visible worksheets only: hidden sheets, chart sheets and macro sheets stay with the user's own program.
                        if (length >= 8 && (stream[body + 4] & 3) == 0 && stream[body + 5] == 0)
                        { int end; sheets.Add(new BoundSheet { Offset = (int)U32(stream, body), Name = Unicode(stream, body + 6, next, true, out end) }); }
                        break;
                    case 0x00FC:
                        var parts = new List<byte[]> { Slice(stream, body, length) };
                        while (next + 4 <= stream.Length && U16(stream, next) == 0x003C)
                        {
                            var size = U16(stream, next + 2);
                            if (next + 4 + size > stream.Length) throw new InvalidDataException("Damaged workbook");
                            parts.Add(Slice(stream, next + 4, size)); next += 4 + size;
                        }
                        SharedStrings(new Segments(parts), strings, cancellation);
                        break;
                    case 0x087D: if (length >= 20) extensions.Add(Slice(stream, body, length)); break;
                    case 0x0896:
                        // The zipped theme follows the version (the default theme of Excel 2007 alone comes without one).
                        if (length > 20 && stream[body + 16] == (byte)'P' && stream[body + 17] == (byte)'K')
                        {
                            theme.Write(stream, body + 16, length - 16);
                            while (next + 4 <= stream.Length && U16(stream, next) == 0x0875)
                            {
                                var size = U16(stream, next + 2);
                                if (next + 4 + size > stream.Length) throw new InvalidDataException("Damaged workbook");
                                if (size > 12) theme.Write(stream, next + 16, size - 12);
                                next += 4 + size;
                            }
                        }
                        break;
                }
                position = next;
                if (type == 0x000A && !first) break;
            }
            var book = new Workbook();
            foreach (var xf in xfs) book.Styles.Add(Style(xf, fonts, formats, palette, culture));
            if (book.Styles.Count == 0) book.Styles.Add(new SheetStyle { Format = "General" });
            Extensions(book, xfs, extensions, Scheme(theme, cancellation), palette);
            var stored = 0;
            foreach (var sheet in sheets)
            {
                if (sheet.Offset < 0 || sheet.Offset + 8 > stream.Length || U16(stream, sheet.Offset) != 0x0809) continue;
                var data = new SheetData { Name = sheet.Name };
                stored += Sheet(stream, sheet.Offset, data, book, strings, date1904, options, cancellation, stored);
                book.Sheets.Add(data);
            }
            return book;
        }
        private static SheetStyle Style(byte[] xf, List<Font> fonts, Dictionary<int, string> formats, int[] palette, System.Globalization.CultureInfo culture)
        {
            var style = new SheetStyle();
            int fontIndex = U16(xf, 0), formatIndex = U16(xf, 2);
            // Font index 4 does not exist in BIFF; the records after it are numbered from 5.
            var font = fontIndex < 4 ? fontIndex : fontIndex - 1;
            if (font >= 0 && font < fonts.Count)
            {
                var f = fonts[font];
                style.Bold = f.Bold; style.Italic = f.Italic; style.Underline = f.Underline; style.Strike = f.Strike;
                var color = Color(f.Color, palette);
                style.Color = color == "000000" ? null : color;
            }
            string code; style.Format = formats.TryGetValue(formatIndex, out code) ? code : CellFormat.Builtin(formatIndex, culture);
            switch (xf[6] & 7)
            {
                case 1: case 4: style.Align = 1; break;
                case 2: case 6: case 7: style.Align = 2; break;
                case 3: style.Align = 3; break;
                case 5: style.Align = 4; break;
            }
            style.Wrap = (xf[6] & 8) != 0;
            var pattern = (int)(U32(xf, 14) >> 26) & 0x3F;
            if (pattern != 0)
            {
                var fill = Color(U16(xf, 18) & 0x7F, palette);
                style.Fill = fill == "ffffff" ? null : fill;
            }
            return style;
        }
        // The theme part zipped into THEME records, as an XLSX keeps it.
        private static Dictionary<string, string> Scheme(MemoryStream theme, CancellationToken cancellation)
        {
            if (theme.Length < 22) return new Dictionary<string, string>();
            try
            {
                theme.Position = 0;
                using (var archive = new System.IO.Compression.ZipArchive(theme, System.IO.Compression.ZipArchiveMode.Read))
                {
                    var entry = System.Linq.Enumerable.FirstOrDefault(archive.Entries, e => e.FullName.EndsWith("theme1.xml", StringComparison.OrdinalIgnoreCase));
                    return entry == null ? new Dictionary<string, string>() : OfficeColors.Scheme(DocxHtml.ReadXml(entry, 4L * 1024 * 1024, cancellation));
                }
            }
            catch (InvalidDataException) { return new Dictionary<string, string>(); }
            catch (System.Xml.XmlException) { return new Dictionary<string, string>(); }
        }
        // XFEXT: extType 4 is the cell fill, 13 the text colour, each a FullColorExt (type, tint, value).
        private static void Extensions(Workbook book, List<byte[]> xfs, List<byte[]> extensions, Dictionary<string, string> scheme, int[] palette)
        {
            foreach (var record in extensions)
            {
                var index = U16(record, 14); var count = U16(record, 18);
                if (index >= book.Styles.Count || index >= xfs.Count) continue;
                var style = book.Styles[index]; var patterned = (U32(xfs[index], 14) >> 26 & 0x3F) != 0;
                for (int i = 0, at = 20; i < count && at + 4 <= record.Length; i++)
                {
                    int type = U16(record, at), size = U16(record, at + 2);
                    if (size < 4 || at + size > record.Length) break;
                    if ((type == 4 || type == 13) && size >= 12)
                    {
                        int kind = U16(record, at + 4), tint = (short)U16(record, at + 6); var value = U32(record, at + 8);
                        string color = null;
                        if (kind == 2) color = record[at + 8].ToString("x2") + record[at + 9].ToString("x2") + record[at + 10].ToString("x2");
                        else if (kind == 3) color = OfficeColors.SheetTheme(scheme, (int)value);
                        else if (kind == 1 && value < 64) color = OfficeColors.Hex(palette[value]);
                        if (color != null && tint != 0) color = OfficeColors.Tint(color, tint / 32767.0);
                        if (kind == 0 || color != null)
                        {
                            if (type == 4) { if (patterned) style.Fill = color == "ffffff" ? null : color; }
                            else style.Color = color == "000000" ? null : color;
                        }
                    }
                    at += size;
                }
            }
        }
        // Palette index to rrggbb; 64 and above are the window's own colours (automatic), shown as the page's.
        private static string Color(int index, int[] palette) { return index >= 0 && index < 64 ? OfficeColors.Hex(palette[index]) : null; }
        private static int Sheet(byte[] stream, int position, SheetData sheet, Workbook book, List<string> strings, bool date1904, PreviewOptions options, CancellationToken cancellation, int alreadyStored)
        {
            var culture = options.Culture; var stored = 0; var depth = 0;
            // A formula with a text result keeps it in the STRING record that follows.
            SheetCell pendingFormula = null; int pendingRow = 0, pendingColumn = 0;
            Action<int, int, SheetCell> put = (row, column, cell) =>
            {
                if (cell.Style < 0 || cell.Style >= book.Styles.Count) cell.Style = 0;
                if (string.IsNullOrEmpty(cell.Text) && book.Styles[cell.Style].Fill == null) return;
                if (alreadyStored + ++stored > SheetMarkup.StoredCellLimit) throw new PreviewLimitException();
                sheet.Cells[SheetData.Key(row, column)] = cell;
            };
            while (position + 4 <= stream.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                int type = U16(stream, position), length = U16(stream, position + 2), body = position + 4;
                if (body + length > stream.Length) throw new InvalidDataException("Damaged workbook");
                var next = body + length;
                if (type == 0x0809) depth++;
                else if (type == 0x000A) { if (--depth <= 0) break; }
                // Records of an embedded chart (its own BOF … EOF) are not cells of this sheet.
                else if (depth == 1)
                {
                    int row = length >= 2 ? U16(stream, body) : 0, column = length >= 4 ? U16(stream, body + 2) : 0, style = length >= 6 ? U16(stream, body + 4) : 0;
                    switch (type)
                    {
                        case 0x00FD:
                            if (length >= 10) { var index = (int)U32(stream, body + 6); put(row, column, new SheetCell { Style = style, Text = index >= 0 && index < strings.Count ? strings[index] : "" }); }
                            break;
                        case 0x0204:
                            if (length >= 9) { int end; put(row, column, new SheetCell { Style = style, Text = Unicode(stream, body + 6, next, false, out end) }); }
                            break;
                        case 0x027E:
                            if (length >= 10) put(row, column, Number(Rk(U32(stream, body + 6)), style, book, date1904, culture));
                            break;
                        case 0x00BD:
                            for (var i = 0; 6 + i * 6 + 6 <= length; i++)
                                put(row, column + i, Number(Rk(U32(stream, body + 6 + i * 6)), U16(stream, body + 4 + i * 6), book, date1904, culture));
                            break;
                        case 0x0203:
                            if (length >= 14) put(row, column, Number(BitConverter.ToDouble(stream, body + 6), style, book, date1904, culture));
                            break;
                        case 0x0205:
                            if (length >= 8)
                            {
                                var error = stream[body + 7] != 0;
                                put(row, column, new SheetCell { Style = style, Kind = error ? CellKind.Error : CellKind.Logical, Text = error ? Error(stream[body + 6]) : SheetMarkup.Logical(stream[body + 6] != 0, options) });
                            }
                            break;
                        case 0x0006:
                            if (length < 20) break;
                            if (U16(stream, body + 12) == 0xFFFF)
                            {
                                var kind = stream[body + 6];
                                if (kind == 0) { pendingFormula = new SheetCell { Style = style, Text = "" }; pendingRow = row; pendingColumn = column; put(row, column, pendingFormula); }
                                else if (kind == 1) put(row, column, new SheetCell { Style = style, Kind = CellKind.Logical, Text = SheetMarkup.Logical(stream[body + 8] != 0, options) });
                                else if (kind == 2) put(row, column, new SheetCell { Style = style, Kind = CellKind.Error, Text = Error(stream[body + 8]) });
                            }
                            else put(row, column, Number(BitConverter.ToDouble(stream, body + 6), style, book, date1904, culture));
                            break;
                        case 0x0207:
                            if (pendingFormula != null && length >= 3)
                            {
                                var parts = new List<byte[]> { Slice(stream, body, length) };
                                while (next + 4 <= stream.Length && U16(stream, next) == 0x003C)
                                {
                                    var size = U16(stream, next + 2);
                                    if (next + 4 + size > stream.Length) throw new InvalidDataException("Damaged workbook");
                                    parts.Add(Slice(stream, next + 4, size)); next += 4 + size;
                                }
                                var segments = new Segments(parts);
                                int count = segments.U16(); var high = (segments.Byte() & 1) != 0;
                                pendingFormula.Text = segments.Chars(count, high);
                                if (!sheet.Cells.ContainsKey(SheetData.Key(pendingRow, pendingColumn))) put(pendingRow, pendingColumn, pendingFormula);
                                pendingFormula = null;
                            }
                            break;
                        case 0x0201: if (length >= 6) put(row, column, new SheetCell { Style = style, Text = "" }); break;
                        case 0x00BE:
                            for (var i = 0; 4 + i * 2 + 4 <= length; i++) put(row, column + i, new SheetCell { Style = U16(stream, body + 4 + i * 2), Text = "" });
                            break;
                        case 0x00E5:
                            if (length >= 2)
                                for (var i = 0; i < U16(stream, body) && body + 2 + i * 8 + 8 <= next; i++)
                                {
                                    var at = body + 2 + i * 8;
                                    int top = U16(stream, at), bottom = U16(stream, at + 2), left = U16(stream, at + 4), right = U16(stream, at + 6);
                                    if (bottom >= top && right >= left) sheet.Merges.Add(new SheetRange { Top = top, Bottom = bottom, Left = left, Right = right });
                                }
                            break;
                        case 0x007D:
                            if (length >= 10)
                            {
                                int from = U16(stream, body), to = U16(stream, body + 2), width = U16(stream, body + 4);
                                var hidden = (U16(stream, body + 8) & 1) != 0;
                                for (var c = from; c <= to && c <= SheetMarkup.ColumnLimit; c++) { sheet.Widths[c] = width / 256.0; if (hidden || width == 0) sheet.HiddenColumns.Add(c); }
                            }
                            break;
                        case 0x0208:
                            if (length >= 16 && (U16(stream, body + 12) & 0x20) != 0) sheet.HiddenRows.Add(row);
                            break;
                    }
                }
                position = next;
            }
            return stored;
        }
        private static SheetCell Number(double value, int style, Workbook book, bool date1904, System.Globalization.CultureInfo culture)
        {
            var format = style >= 0 && style < book.Styles.Count ? book.Styles[style].Format : "General";
            return new SheetCell { Style = style, Kind = CellKind.Number, Text = CellFormat.Format(value, format, date1904, culture) };
        }
        // RK: a 30-bit integer or the top 30 bits of a double, optionally divided by 100.
        private static double Rk(uint rk)
        {
            double value = (rk & 2) != 0 ? (int)rk >> 2 : BitConverter.Int64BitsToDouble((long)(rk & 0xFFFFFFFC) << 32);
            return (rk & 1) != 0 ? value / 100 : value;
        }
        private static string Error(byte code)
        {
            switch (code)
            {
                case 0x00: return "#NULL!"; case 0x07: return "#DIV/0!"; case 0x0F: return "#VALUE!"; case 0x17: return "#REF!";
                case 0x1D: return "#NAME?"; case 0x24: return "#NUM!"; case 0x2A: return "#N/A"; default: return "#N/A";
            }
        }
        private static void SharedStrings(Segments segments, List<string> strings, CancellationToken cancellation)
        {
            segments.U32(); var unique = segments.U32();
            if (unique > 4000000) throw new PreviewLimitException();
            long total = 0;
            for (var i = 0u; i < unique && !segments.End; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                int count = segments.U16(); var flags = segments.Byte();
                var runs = (flags & 8) != 0 ? segments.U16() : 0;
                var extension = (flags & 4) != 0 ? segments.U32() : 0;
                var text = segments.Chars(count, (flags & 1) != 0);
                total += text.Length; if (total > 32 * 1024 * 1024) throw new PreviewLimitException();
                strings.Add(text);
                segments.Skip(runs * 4L + extension);
            }
        }
        // A record body and its CONTINUE records. A string's characters may continue in the next record, which then
        // starts with its own flags byte (8-bit or UTF-16 characters).
        private sealed class Segments
        {
            private readonly List<byte[]> parts; private int part, position;
            public Segments(List<byte[]> parts) { this.parts = parts; }
            public bool End { get { Ensure(); return part >= parts.Count; } }
            private bool Ensure() { while (part < parts.Count && position >= parts[part].Length) { part++; position = 0; } return part < parts.Count; }
            public byte Byte() { if (!Ensure()) throw new InvalidDataException("Damaged workbook"); return parts[part][position++]; }
            public int U16() { return Byte() | Byte() << 8; }
            public uint U32() { return (uint)(U16() | U16() << 16); }
            public string Chars(int count, bool high)
            {
                var text = new StringBuilder(count);
                while (count > 0)
                {
                    if (part >= parts.Count || position >= parts[part].Length)
                    {
                        part++; position = 0;
                        if (part >= parts.Count || parts[part].Length == 0) throw new InvalidDataException("Damaged workbook");
                        high = (parts[part][position++] & 1) != 0;
                        continue;
                    }
                    if (high)
                    {
                        if (position + 1 >= parts[part].Length) throw new InvalidDataException("Damaged workbook");
                        text.Append((char)(parts[part][position] | parts[part][position + 1] << 8)); position += 2;
                    }
                    else text.Append((char)parts[part][position++]);
                    count--;
                }
                return text.ToString();
            }
            public void Skip(long count)
            {
                while (count > 0)
                {
                    if (!Ensure()) throw new InvalidDataException("Damaged workbook");
                    var take = (int)Math.Min(count, parts[part].Length - position);
                    position += take; count -= take;
                }
            }
        }
        // XLUnicodeString (16-bit length) or ShortXLUnicodeString (8-bit length) within one record.
        private static string Unicode(byte[] data, int offset, int end, bool shortLength, out int next)
        {
            int count; if (shortLength) { count = data[offset]; offset++; } else { count = U16(data, offset); offset += 2; }
            var high = (data[offset] & 1) != 0; offset++;
            var size = high ? count * 2 : count;
            if (offset + size > end) throw new InvalidDataException("Damaged workbook");
            next = offset + size;
            return high ? Encoding.Unicode.GetString(data, offset, size) : Encoding.GetEncoding(28591).GetString(data, offset, size);
        }
        private static byte[] Slice(byte[] data, int offset, int length) { var result = new byte[length]; Array.Copy(data, offset, result, 0, length); return result; }
        private static int U16(byte[] buffer, int offset) { return CompoundFile.U16(buffer, offset); }
        private static uint U32(byte[] buffer, int offset) { return CompoundFile.U32(buffer, offset); }
    }
}
