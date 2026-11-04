using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Univer.Desktop.Core
{
    // Bundled read-only renderer for Word 97-2003 .doc files, through DocumentMarkup so DOC and DOCX look alike.
    // Floating shapes, text boxes, headers, footnotes and comments stay with the user's own program.
    internal static class DocHtml
    {
        private const long FileLimit = 64L * 1024 * 1024;
        private const int CharacterLimit = 4 * 1024 * 1024, StyleDepth = 32;
        private static readonly Encoding Compressed = Encoding.GetEncoding(1252);
        private static readonly string[] Ico = { null, "#000000", "#0000ff", "#00ffff", "#00ff00", "#ff00ff", "#ff0000", "#ffff00", "#ffffff",
            "#000080", "#008080", "#008000", "#800080", "#800000", "#808000", "#808080", "#c0c0c0" };
        private sealed class Paragraph
        {
            public int Start, End, Depth, Style, ListFormat, ListLevel; public bool Row;
            public ParagraphStyle Format; public RunStyle Runs; public byte[] Sprms; public int From, To;
        }
        private sealed class Style
        {
            public int Kind, Base = 0x0FFF, Sti = -1; public byte[] Paragraph, Character; public bool Resolved;
            public ParagraphStyle ParagraphStyle; public RunStyle RunStyle; public int ListFormat, ListLevel;
        }
        private sealed class ListLevel { public int Start = 1, Format; public string Text = ""; public byte[] Paragraph; }
        private sealed class Field { public bool Result, Link; public StringBuilder Code = new StringBuilder(); }
        private sealed class Document
        {
            public byte[] Word, Table, Data; public int Blob; public CancellationToken Cancellation;
            public List<Style> Styles = new List<Style>(); public RunStyle Defaults = new RunStyle { Size = 20 }; public int BaseSize = 20;
            public Dictionary<int, ListLevel[]> Lists = new Dictionary<int, ListLevel[]>();
            public List<int> Overrides = new List<int>(); public List<Dictionary<int, int>> Starts = new List<Dictionary<int, int>>();
            public Dictionary<int, int?[]> Counters = new Dictionary<int, int?[]>();
            public PreviewImages Images = new PreviewImages();
            public PreviewOptions Options;
            public int Fc(int index) { return I32(Word, Blob + index * 8); }
            public int Lcb(int index) { return I32(Word, Blob + index * 8 + 4); }
        }
        public static PreviewDocument Convert(string path, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            if (new FileInfo(path).Length > FileLimit) throw new PreviewLimitException();
            return Convert(File.ReadAllBytes(path), options, cancellation);
        }
        public static PreviewDocument Convert(byte[] file, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            // Every structure comes from the file; any inconsistency the explicit checks miss is still a damaged document.
            try { return Build(file, options, cancellation); }
            catch (IndexOutOfRangeException) { throw new InvalidDataException("Damaged document"); }
            catch (ArgumentException) { throw new InvalidDataException("Damaged document"); }
            catch (OverflowException) { throw new InvalidDataException("Damaged document"); }
        }
        private static PreviewDocument Build(byte[] file, PreviewOptions options, CancellationToken cancellation)
        {
            var container = new CompoundFile(file);
            var word = container.Stream("WordDocument") ?? throw new InvalidDataException("Not a Word document");
            if (U16(word, 0) != 0xA5EC) throw new InvalidDataException("Not a Word document");
            var flags = U16(word, 0x0A);
            if ((flags & 0x0100) != 0) throw new InvalidDataException("Encrypted document");
            // FibBase, then FibRgW97, FibRgLw97 and FibRgFcLcb97 (Word 97 and later; Word 95 files differ).
            var lw = 34 + U16(word, 32) * 2 + 2; var blob = lw + U16(word, lw - 2) * 4 + 2; var pairs = U16(word, blob - 2);
            if (pairs < 75 || U16(word, lw - 2) < 4) throw new InvalidDataException("Unsupported Word version");
            var document = new Document
            {
                Word = word, Blob = blob, Cancellation = cancellation, Data = container.Stream("Data"), Options = options,
                Table = container.Stream((flags & 0x0200) != 0 ? "1Table" : "0Table") ?? throw new InvalidDataException("Missing table stream")
            };
            var length = I32(word, lw + 12);
            if (length < 0) throw new InvalidDataException("Damaged document");
            if (length > CharacterLimit) throw new PreviewLimitException();
            int[] positions;
            var text = Text(document, length, out positions);
            cancellation.ThrowIfCancellationRequested();
            Styles(document);
            Lists(document);
            var paragraphs = Paragraphs(document, text, positions);
            var characters = new FkpLookup(document.Word, document.Table, document.Fc(12), document.Lcb(12), false);
            document.BaseSize = BodySize(document, text, positions, paragraphs, characters);
            var html = new PreviewMarkup(cancellation);
            DocxHtml.Start(html, options.Title);
            if (paragraphs.Exists(p => p.Depth > 0)) DocumentMarkup.TableHint(html, options);
            Render(document, html, text, positions, paragraphs, characters);
            html.Append("</body></html>");
            return new PreviewDocument(html.ToString(), document.Images.Items);
        }
        private static char[] Text(Document document, int length, out int[] positions)
        {
            // The Clx piece table maps character positions to UTF-16 or compressed 8-bit runs in WordDocument.
            var table = document.Table; var word = document.Word; var offset = document.Fc(33); var size = document.Lcb(33);
            if (offset < 0 || size <= 0 || (long)offset + size > table.Length) throw new InvalidDataException("Damaged document");
            var end = offset + size; var pos = offset;
            while (pos < end && table[pos] == 1) pos += 3 + U16(table, pos + 1);
            if (pos >= end || table[pos] != 2) throw new InvalidDataException("Damaged document");
            var plc = I32(table, pos + 1); var start = pos + 5;
            if (plc < 4 || (plc - 4) % 12 != 0 || (long)start + plc > table.Length) throw new InvalidDataException("Damaged document");
            var count = (plc - 4) / 12;
            var text = new char[length]; positions = new int[length];
            for (var i = 0; i < count; i++)
            {
                var first = I32(table, start + i * 4); var last = I32(table, start + (i + 1) * 4);
                var raw = U32(table, start + (count + 1) * 4 + i * 8 + 2);
                var compressed = (raw & 0x40000000) != 0;
                var origin = compressed ? (raw & 0x3FFFFFFF) / 2 : raw;
                for (var cp = Math.Max(0, first); cp < Math.Min(length, last); cp++)
                {
                    var at = (long)origin + (long)(cp - first) * (compressed ? 1 : 2);
                    if (at < 0 || at + (compressed ? 1 : 2) > word.Length) throw new InvalidDataException("Damaged document");
                    text[cp] = compressed ? Compressed.GetChars(word, (int)at, 1)[0] : (char)U16(word, (int)at);
                    positions[cp] = (int)at;
                }
            }
            return text;
        }
        // Style sheet: base style, paragraph and character properties of each style (STSH, UPX).
        private static void Styles(Document document)
        {
            var table = document.Table; var offset = document.Fc(1); var size = document.Lcb(1);
            try
            {
                if (offset < 0 || size <= 4 || (long)offset + size > table.Length) return;
                var header = U16(table, offset); var count = U16(table, offset + 2); var baseLength = U16(table, offset + 4);
                var pos = offset + 2 + header; var end = offset + size;
                for (var i = 0; i < count && pos + 2 <= end; i++)
                {
                    var bytes = U16(table, pos); var start = pos + 2; pos = start + bytes;
                    var style = new Style();
                    document.Styles.Add(style);
                    if (bytes < 10 || pos > end) continue;
                    style.Sti = U16(table, start) & 0x0FFF;
                    var kind = U16(table, start + 2); style.Kind = kind & 0x000F; style.Base = kind >> 4;
                    var upx = U16(table, start + 4) & 0x000F;
                    var at = start + baseLength;
                    var name = U16(table, at); at += 2 + name * 2 + 2;
                    for (var u = 0; u < upx && at + 2 <= pos; u++)
                    {
                        var length = U16(table, at); var data = at + 2;
                        if (data + length > pos) break;
                        if (style.Kind == 1 && u == 0) { if (length >= 2) style.Paragraph = Slice(table, data + 2, length - 2); }
                        else if (style.Kind == 1 && u == 1 || style.Kind == 2 && u == 0) style.Character = Slice(table, data, length);
                        at = data + length + (length & 1);
                    }
                }
            }
            catch (InvalidDataException) { /* A damaged style sheet only loses formatting. */ }
            var normal = StyleAt(document, 0);
            document.BaseSize = normal != null && normal.RunStyle.Size > 0 ? normal.RunStyle.Size : 20;
        }
        private static Style StyleAt(Document document, int index)
        {
            if (index < 0 || index >= document.Styles.Count) return null;
            var style = document.Styles[index];
            Resolve(document, style, 0);
            return style;
        }
        private static void Resolve(Document document, Style style, int depth)
        {
            if (style.Resolved) return;
            style.Resolved = true; // Also stops base-style cycles.
            var parent = depth < StyleDepth && style.Base != 0x0FFF && style.Base < document.Styles.Count ? document.Styles[style.Base] : null;
            if (parent != null) Resolve(document, parent, depth + 1);
            var run = parent?.RunStyle ?? document.Defaults; var paragraph = parent?.ParagraphStyle ?? default(ParagraphStyle);
            style.ListFormat = parent?.ListFormat ?? 0; style.ListLevel = parent?.ListLevel ?? 0;
            if (style.Paragraph != null) ApplyParagraph(style.Paragraph, 0, style.Paragraph.Length, ref paragraph, ref style.ListFormat, ref style.ListLevel);
            if (style.Character != null) run = ApplyRun(document, style.Character, 0, style.Character.Length, run, run);
            if (style.Sti >= 1 && style.Sti <= 9) paragraph.Level = Math.Min(3, style.Sti); else if (style.Sti == 62) paragraph.Level = 1;
            else if (parent != null && style.Kind == 1) paragraph.Level = parent.ParagraphStyle.Level;
            style.RunStyle = run; style.ParagraphStyle = paragraph;
        }
        // List definitions (PlfLst with LVLs) and their instances (PlfLfo with start overrides).
        private static void Lists(Document document)
        {
            var table = document.Table;
            try
            {
                var offset = document.Fc(73); var size = document.Lcb(73);
                if (offset > 0 && size > 2 && (long)offset + size <= table.Length)
                {
                    var count = (short)U16(table, offset);
                    var ids = new List<int>(); var simple = new List<bool>();
                    for (var i = 0; i < count; i++) { ids.Add(I32(table, offset + 2 + i * 28)); simple.Add((table[offset + 2 + i * 28 + 26] & 1) != 0); }
                    var pos = offset + 2 + count * 28;
                    for (var i = 0; i < count; i++)
                    {
                        var levels = new ListLevel[9];
                        for (var l = 0; l < (simple[i] ? 1 : 9); l++) levels[l] = Level(table, ref pos);
                        document.Lists[ids[i]] = levels;
                    }
                }
                offset = document.Fc(74); size = document.Lcb(74);
                if (offset > 0 && size > 4 && (long)offset + size <= table.Length)
                {
                    var count = I32(table, offset);
                    if (count < 0 || count > 10000) return;
                    var levels = new List<int>();
                    for (var i = 0; i < count; i++) { document.Overrides.Add(I32(table, offset + 4 + i * 16)); levels.Add(table[offset + 4 + i * 16 + 12]); }
                    var pos = offset + 4 + count * 16;
                    for (var i = 0; i < count; i++)
                    {
                        var starts = new Dictionary<int, int>();
                        document.Starts.Add(starts);
                        pos += 4; // LFOData.cp
                        for (var l = 0; l < levels[i]; l++)
                        {
                            var start = I32(table, pos); var options = U32(table, pos + 4); pos += 8;
                            if ((options & 0x10) != 0) starts[(int)(options & 0x0F)] = start;
                            if ((options & 0x20) != 0) Level(table, ref pos);
                        }
                    }
                }
            }
            catch (InvalidDataException) { /* Without list tables, list paragraphs keep a plain bullet. */ }
        }
        private static ListLevel Level(byte[] table, ref int pos)
        {
            var level = new ListLevel { Start = I32(table, pos), Format = table[pos + 4] };
            var character = table[pos + 24]; var paragraph = table[pos + 25];
            pos += 28;
            level.Paragraph = Slice(table, pos, paragraph); pos += paragraph + character;
            var length = U16(table, pos); pos += 2;
            if (length > 64 || pos + length * 2 > table.Length) throw new InvalidDataException("Damaged list");
            level.Text = Encoding.Unicode.GetString(table, pos, length * 2); pos += length * 2;
            return level;
        }
        private static string Marker(Document document, int format, int index)
        {
            if (format < 1 || format > document.Overrides.Count) return "•";
            ListLevel[] levels;
            var list = document.Overrides[format - 1];
            if (!document.Lists.TryGetValue(list, out levels) || levels[index] == null) return "•";
            var level = levels[index];
            if (level.Format == 23) return DocumentMarkup.Bullet(level.Text);
            int?[] counters; if (!document.Counters.TryGetValue(format, out counters)) document.Counters[format] = counters = new int?[9];
            var starts = document.Starts.Count >= format ? document.Starts[format - 1] : null;
            Func<int, int> start = i => { int value; return starts != null && starts.TryGetValue(i, out value) ? value : levels[i]?.Start ?? 1; };
            counters[index] = counters[index].HasValue ? counters[index] + 1 : start(index);
            for (var i = index + 1; i < 9; i++) counters[i] = null;
            var text = new StringBuilder();
            foreach (var c in level.Text)
            {
                if (c < 9) text.Append(DocumentMarkup.Number(counters[c] ?? start(c), NumberFormat(levels[c]?.Format ?? 0)));
                else text.Append(c);
            }
            return text.Length == 0 ? null : text.ToString();
        }
        private static string NumberFormat(int nfc)
        {
            switch (nfc)
            {
                case 1: return "upperRoman"; case 2: return "lowerRoman"; case 3: return "upperLetter"; case 4: return "lowerLetter";
                case 22: return "decimalZero"; case 255: return "none"; default: return "decimal";
            }
        }
        private static List<Paragraph> Paragraphs(Document document, char[] text, int[] positions)
        {
            var lookup = new FkpLookup(document.Word, document.Table, document.Fc(13), document.Lcb(13), true);
            var result = new List<Paragraph>();
            var start = 0;
            for (var i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && text[i] != '\r' && text[i] != '\a') continue;
                if (i == text.Length && start >= text.Length) break;
                var paragraph = new Paragraph { Start = start, End = i };
                byte[] grpprl; int from = 0, to = 0, istd;
                if (i < text.Length && lookup.Find(positions[i], out grpprl, out from, out to, out istd)) { paragraph.Style = istd; paragraph.Sprms = grpprl; paragraph.From = from; paragraph.To = to; }
                var style = StyleAt(document, paragraph.Style) ?? StyleAt(document, 0);
                var format = style?.ParagraphStyle ?? default(ParagraphStyle);
                int listFormat = style?.ListFormat ?? 0, listLevel = style?.ListLevel ?? 0;
                bool inTable = false, ttp = false; var itap = 0;
                if (paragraph.Sprms != null)
                {
                    // List level indents sit between the style and the paragraph's own properties.
                    int directFormat = listFormat, directLevel = listLevel; var scratch = format;
                    ApplyParagraph(paragraph.Sprms, from, to, ref scratch, ref directFormat, ref directLevel);
                    var level = ListLevelOf(document, directFormat, directLevel);
                    if (level?.Paragraph != null) { int f = 0, l = 0; ApplyParagraph(level.Paragraph, 0, level.Paragraph.Length, ref format, ref f, ref l); }
                    ApplyParagraph(paragraph.Sprms, from, to, ref format, ref listFormat, ref listLevel);
                    Sprms(paragraph.Sprms, from, to, (sprm, at) =>
                    {
                        if (sprm == 0x2416) inTable = paragraph.Sprms[at] != 0;
                        else if (sprm == 0x2417) ttp = paragraph.Sprms[at] != 0;
                        else if (sprm == 0x6649) itap = I32(paragraph.Sprms, at);
                    });
                }
                else
                {
                    var level = ListLevelOf(document, listFormat, listLevel);
                    if (level?.Paragraph != null) { int f = 0, l = 0; var styleFormat = format; ApplyParagraph(level.Paragraph, 0, level.Paragraph.Length, ref format, ref f, ref l); format.Align = styleFormat.Align; }
                }
                paragraph.Format = format; paragraph.Runs = style?.RunStyle ?? document.Defaults;
                paragraph.ListFormat = listFormat; paragraph.ListLevel = Math.Max(0, Math.Min(8, listLevel));
                paragraph.Depth = itap > 0 ? itap : inTable ? 1 : 0;
                // Nested tables are flattened into their outer cell; only the outer row structure is kept.
                paragraph.Row = paragraph.Depth == 1 && ttp;
                if (paragraph.Depth > 1 && i < text.Length && text[i] == '\a') text[i] = '\r';
                result.Add(paragraph); start = i + 1;
            }
            return result;
        }
        private static ListLevel ListLevelOf(Document document, int format, int index)
        {
            ListLevel[] levels;
            if (format < 1 || format > document.Overrides.Count || index < 0 || index > 8) return null;
            return document.Lists.TryGetValue(document.Overrides[format - 1], out levels) ? levels[index] : null;
        }
        private static void ApplyParagraph(byte[] sprms, int from, int to, ref ParagraphStyle format, ref int listFormat, ref int listLevel)
        {
            var result = format; int list = listFormat, level = listLevel;
            Sprms(sprms, from, to, (sprm, at) =>
            {
                switch (sprm)
                {
                    case 0x2403: case 0x2461: var align = sprms[at]; result.Align = align == 1 ? 1 : align == 2 ? 2 : align == 3 || align == 4 ? 3 : 0; break;
                    case 0x840F: case 0x845E: result.Left = (short)U16(sprms, at); break;
                    case 0x8411: case 0x8460: result.FirstLine = (short)U16(sprms, at); break;
                    case 0x2407: result.PageBreakBefore = sprms[at] != 0; break;
                    case 0x460B: list = (short)U16(sprms, at); break;
                    case 0x260A: level = sprms[at]; break;
                }
            });
            format = result; listFormat = list; listLevel = level;
        }
        // Applies character properties (0x80 keeps the style's value, 0x81 inverts it). A character style is followed only as
        // deep as base styles: a style naming itself would otherwise overflow the stack and end the process.
        private static RunStyle ApplyRun(Document document, byte[] sprms, int from, int to, RunStyle format, RunStyle style, int depth = 0)
        {
            var result = format;
            Sprms(sprms, from, to, (sprm, at) =>
            {
                var value = sprms[at];
                Func<bool, bool, bool> toggle = (current, inherited) => value == 0x80 ? inherited : value == 0x81 ? !inherited : value != 0;
                switch (sprm)
                {
                    case 0x0835: result.Bold = toggle(result.Bold, style.Bold); break;
                    case 0x0836: result.Italic = toggle(result.Italic, style.Italic); break;
                    case 0x0837: case 0x2A53: result.Strike = toggle(result.Strike, style.Strike); break;
                    case 0x083A: result.SmallCaps = toggle(result.SmallCaps, style.SmallCaps); break;
                    case 0x083B: result.Caps = toggle(result.Caps, style.Caps); break;
                    case 0x083C: result.Hidden = toggle(result.Hidden, style.Hidden); break;
                    case 0x2A3E: result.Underline = value != 0; break;
                    case 0x2A48: result.Script = value; break;
                    case 0x4A43: var size = U16(sprms, at); if (size > 0 && size < 3200) result.Size = size; break;
                    case 0x2A42: result.Color = value < Ico.Length && Ico[value] != null ? Color(Ico[value]) : null; break;
                    case 0x6870: result.Color = sprms[at + 3] == 0xFF ? null : DocumentMarkup.TextColor(sprms[at], sprms[at + 1], sprms[at + 2]); break;
                    case 0x2A0C: result.Highlight = value > 0 && value < Ico.Length ? Ico[value] : null; break;
                    case 0x4A30:
                        var character = StyleAt(document, U16(sprms, at));
                        if (depth < StyleDepth && character?.Character != null) result = ApplyRun(document, character.Character, 0, character.Character.Length, result, result, depth + 1);
                        break;
                }
            });
            return result;
        }
        private static string Color(string hex) { return DocumentMarkup.TextColor(hex.Substring(1)); }
        // The most common text size becomes 1em, so body text keeps the viewer's readable size in every document.
        private static int BodySize(Document document, char[] text, int[] positions, List<Paragraph> paragraphs, FkpLookup characters)
        {
            var counts = new Dictionary<int, int>(); int low = -1, high = -1; var size = 0;
            foreach (var paragraph in paragraphs)
            {
                low = high = -1;
                for (var i = paragraph.Start; i < paragraph.End; i++)
                {
                    if (text[i] <= ' ') continue;
                    if (positions[i] < low || positions[i] >= high)
                    {
                        byte[] chpx; int from, to;
                        size = characters.Find(positions[i], out chpx, out from, out to, out low, out high) ? ApplyRun(document, chpx, from, to, paragraph.Runs, paragraph.Runs).Size : paragraph.Runs.Size;
                        if (low < 0) { low = positions[i]; high = positions[i] + 1; }
                    }
                    int count; counts.TryGetValue(size, out count); counts[size] = count + 1;
                }
            }
            var best = document.BaseSize; var most = 0;
            foreach (var pair in counts) if (pair.Key > 0 && pair.Value > most) { best = pair.Key; most = pair.Value; }
            return best;
        }
        private static void Render(Document document, PreviewMarkup html, char[] text, int[] positions, List<Paragraph> paragraphs, FkpLookup characters)
        {
            var fields = new Stack<Field>();
            TableModel table = null; var row = new List<TableModel.Cell>(); var bounds = new List<int[]>(); PreviewMarkup cell = null;
            Action flush = () =>
            {
                if (table == null) return;
                Finish(table, bounds);
                DocumentMarkup.Table(html, table, 0, c => html.Append((PreviewMarkup)c.Source));
                table = null; bounds.Clear();
            };
            foreach (var paragraph in paragraphs)
            {
                document.Cancellation.ThrowIfCancellationRequested();
                if (paragraph.Depth == 0) flush();
                if (paragraph.Depth > 0 && table == null) { table = new TableModel(); row = new List<TableModel.Cell>(); cell = null; }
                if (paragraph.Row)
                {
                    if (cell != null) { row.Add(new TableModel.Cell { Source = cell }); cell = null; }
                    var tap = Tap(paragraph);
                    table.Rows.Add(row); bounds.Add(tap.Item1);
                    Merge(row, tap.Item2);
                    row = new List<TableModel.Cell>();
                    continue;
                }
                var target = table != null ? (cell ?? (cell = new PreviewMarkup(html.Cancellation))) : html;
                var content = Inline(document, text, positions, paragraph, characters, fields, html.Cancellation);
                var marker = paragraph.ListFormat > 0 ? Marker(document, paragraph.ListFormat, paragraph.ListLevel) : null;
                DocumentMarkup.Paragraph(target, paragraph.Format, marker, content, table != null);
                if (table != null && paragraph.End < text.Length && text[paragraph.End] == '\a') { row.Add(new TableModel.Cell { Source = cell ?? new PreviewMarkup(html.Cancellation) }); cell = null; }
            }
            if (table != null && (row.Count > 0 || cell != null))
            {
                if (cell != null) row.Add(new TableModel.Cell { Source = cell });
                table.Rows.Add(row); bounds.Add(null);
            }
            flush();
        }
        // Row properties from the row-end mark: cell boundaries (twips) and merge flags.
        private static Tuple<int[], int[]> Tap(Paragraph paragraph)
        {
            int[] bounds = null, merges = null;
            if (paragraph.Sprms == null) return Tuple.Create(bounds, merges);
            var sprms = paragraph.Sprms;
            Sprms(sprms, paragraph.From, paragraph.To, (sprm, at) =>
            {
                if (sprm != 0xD608) return;
                var count = sprms[at];
                if (count == 0 || count > 63 || at + 1 + (count + 1) * 2 > sprms.Length) return;
                bounds = new int[count + 1];
                for (var i = 0; i <= count; i++) bounds[i] = (short)U16(sprms, at + 1 + i * 2);
                merges = new int[count];
                var cells = at + 1 + (count + 1) * 2;
                for (var i = 0; i < count && cells + i * 20 + 2 <= sprms.Length; i++) merges[i] = U16(sprms, cells + i * 20);
            });
            return Tuple.Create(bounds, merges);
        }
        // Horizontal merges join cell content into the first cell; vertical continuations extend the cell above.
        private static void Merge(List<TableModel.Cell> row, int[] merges)
        {
            if (merges == null) return;
            for (var i = 0; i < row.Count && i < merges.Length; i++)
            {
                var horizontal = merges[i] & 0x3; var vertical = merges[i] >> 5 & 0x3;
                if (horizontal == 2 && i > 0) { row[i].Continuation = true; row[i].Rows = -1; }
                if (vertical == 1) row[i].Rows = 0;
            }
        }
        private static void Finish(TableModel table, List<int[]> bounds)
        {
            // A shared grid from every row's cell boundaries gives colspans and proportional widths.
            var edges = new SortedSet<int>();
            foreach (var b in bounds) if (b != null) foreach (var x in b) edges.Add(x);
            var grid = new List<int>(edges);
            var fallback = 0; foreach (var r in table.Rows) fallback = Math.Max(fallback, r.Count);
            var useGrid = grid.Count >= 2 && grid.Count <= 65;
            table.Columns = useGrid ? grid.Count - 1 : fallback;
            if (useGrid) { table.Widths = new int[grid.Count - 1]; for (var i = 0; i + 1 < grid.Count; i++) table.Widths[i] = grid[i + 1] - grid[i]; }
            var above = new Dictionary<int, TableModel.Cell>();
            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r]; var b = r < bounds.Count ? bounds[r] : null;
                var current = new Dictionary<int, TableModel.Cell>();
                TableModel.Cell owner = null;
                for (var i = 0; i < row.Count; i++)
                {
                    var cell = row[i];
                    var column = useGrid && b != null && i < b.Length - 1 ? grid.IndexOf(b[i]) : i;
                    cell.Span = useGrid && b != null && i < b.Length - 1 ? Math.Max(1, grid.IndexOf(b[i + 1]) - column) : 1;
                    if (cell.Rows == -1 && owner != null)
                    {
                        // Horizontally merged: content moves into the first cell.
                        owner.Span += cell.Span; ((PreviewMarkup)owner.Source).Append((PreviewMarkup)cell.Source); cell.Rows = 1; continue;
                    }
                    TableModel.Cell start;
                    if (cell.Rows == 0 && above.TryGetValue(column, out start) && start.Span == cell.Span)
                    { start.Rows++; cell.Continuation = true; cell.Rows = 1; current[column] = start; }
                    else { cell.Rows = 1; cell.Continuation = false; current[column] = cell; owner = cell; }
                }
                if (!useGrid) { var missing = table.Columns - row.Count; if (missing > 0 && row.Count > 0) row[row.Count - 1].Span += missing; }
                above = current;
            }
        }
        private static PreviewMarkup Inline(Document document, char[] text, int[] positions, Paragraph paragraph, FkpLookup characters, Stack<Field> fields, CancellationToken cancellation)
        {
            var html = new PreviewMarkup(cancellation);
            var run = new StringBuilder(); string close = null; RunStyle current = default(RunStyle); var low = -1; var high = -1; RunStyle format = paragraph.Runs;
            byte[] chpx = null; int from = 0, to = 0;
            var heading = paragraph.Format.Level > 0;
            Action flush = () => { if (run.Length > 0) { html.Append(WebUtility.HtmlEncode(run.ToString()).Replace("\n", "<br>").Replace("\t", "&emsp;")); run.Clear(); } };
            Action end = () => { flush(); if (close != null) { html.Append(close); close = null; } };
            for (var i = paragraph.Start; i < paragraph.End; i++)
            {
                var c = text[i];
                if (c == '\u0013') { fields.Push(new Field()); continue; }
                if (c == '\u0014')
                {
                    if (fields.Count == 0) continue;
                    var field = fields.Peek(); field.Result = true;
                    var link = fields.Count == 1 ? DocxHtml.Hyperlink(field.Code.ToString()) : null;
                    if (link != null) { end(); html.Append("<a href=\"").Append(WebUtility.HtmlEncode(link)).Append("\">"); field.Link = true; }
                    continue;
                }
                if (c == '\u0015')
                {
                    if (fields.Count == 0) continue;
                    var field = fields.Pop();
                    if (field.Link) { end(); html.Append("</a>"); }
                    continue;
                }
                if (fields.Count > 0 && !fields.Peek().Result) { fields.Peek().Code.Append(c); continue; }
                if (Hidden(fields)) continue;
                if (positions[i] < low || positions[i] >= high)
                {
                    format = paragraph.Runs; chpx = null;
                    if (characters.Find(positions[i], out chpx, out from, out to, out low, out high)) format = ApplyRun(document, chpx, from, to, paragraph.Runs, paragraph.Runs);
                    else { low = high = -1; }
                }
                if (format.Hidden) continue;
                if (c == '\u0001' || c == '\b')
                {
                    if (c == '\u0001' && chpx != null) Picture(document, html, chpx, from, to, end);
                    continue;
                }
                string value;
                if (c == '\v') value = "\n";
                else if (c == '\f') { end(); html.Append("<hr class=\"page\">"); continue; }
                else if (c == '\t') value = "\t";
                else if (c == '\u001E') value = "‑";
                else if (c < ' ' || c == '\u001F') continue;
                else if (c >= '' && c <= '') value = DocumentMarkup.Bullet(c.ToString());
                else value = c.ToString();
                if (close == null || !format.SameAs(current)) { end(); current = format; close = DocumentMarkup.OpenRun(html, format, document.BaseSize, heading); }
                run.Append(value);
            }
            end();
            while (fields.Count > 0 && fields.Peek().Link) { html.Append("</a>"); fields.Peek().Link = false; }
            return html;
        }
        private static bool Hidden(Stack<Field> fields) { foreach (var field in fields) if (!field.Result) return true; return false; }
        // Inline picture: PICF in the Data stream, followed by an Office Art blip (PNG, JPEG or DIB). closeRun ends the
        // open text run before the picture is written.
        private static void Picture(Document document, PreviewMarkup html, byte[] chpx, int from, int to, Action closeRun)
        {
            var location = -1; var special = false;
            Sprms(chpx, from, to, (sprm, at) => { if (sprm == 0x6A03 || sprm == 0x680E) location = I32(chpx, at); else if (sprm == 0x0855) special = chpx[at] != 0; });
            var data = document.Data;
            if (!special || location < 0 || data == null || location + 0x44 > data.Length) return;
            try
            {
                var size = I32(data, location); var header = U16(data, location + 4);
                if (header != 0x44 || size < header || (long)location + size > data.Length) return;
                var mode = U16(data, location + 6);
                // Goal width (twips) times the horizontal scale in tenths of a percent; an unset scale means 100%.
                var scale = (int)U16(data, location + 0x20);
                var width = U16(data, location + 0x1C) * (scale > 0 ? scale : 1000) / 1000;
                var pos = location + header + (mode == 0x66 ? 1 + data[location + header] : 0);
                var end = location + size;
                while (pos + 8 <= end)
                {
                    var type = U16(data, pos + 2); var length = I32(data, pos + 4);
                    if (length < 0 || pos + 8 + length > end) return;
                    if (type == 0xF007 && length >= 36)
                    {
                        var name = data[pos + 8 + 33];
                        var blip = pos + 8 + 36 + name;
                        if (blip + 8 > pos + 8 + length) return;
                        var key = "data:" + blip;
                        var path = document.Images.Find(key);
                        if (path == null)
                        {
                            string mime; var bytes = Blip(document.Images, data, blip, out mime);
                            if (mime == null) return;
                            path = document.Images.Add(key, bytes, mime);
                        }
                        closeRun(); PreviewImages.Write(html, path, width / 15, document.Options);
                        return;
                    }
                    pos += 8 + length;
                }
            }
            catch (InvalidDataException) { }
        }
        // Returns the picture bytes with their type; the type without bytes means a picture too large to show.
        private static byte[] Blip(PreviewImages images, byte[] data, int pos, out string mime)
        {
            mime = null;
            var instance = U16(data, pos) >> 4; var type = U16(data, pos + 2); var length = I32(data, pos + 4);
            if (length <= 0 || pos + 8 + length > data.Length) return null;
            int skip; string kind;
            if (type == 0xF01D || type == 0xF02A) { kind = "image/jpeg"; skip = instance == 0x46B || instance == 0x6E3 ? 33 : 17; }
            else if (type == 0xF01E) { kind = "image/png"; skip = instance == 0x6E1 ? 33 : 17; }
            else if (type == 0xF01F) { kind = "image/bmp"; skip = instance == 0x7A9 ? 33 : 17; }
            else return null;
            if (skip >= length) return null;
            mime = kind;
            if (!images.Fits(length - skip + 14)) return null;
            var bytes = new byte[length - skip]; Array.Copy(data, pos + 8 + skip, bytes, 0, bytes.Length);
            if (type != 0xF01F) return bytes;
            bytes = Bitmap(bytes);
            if (bytes == null) mime = null;
            return bytes;
        }
        // A packed DIB becomes a BMP file by prefixing its file header.
        private static byte[] Bitmap(byte[] dib)
        {
            if (dib.Length < 40) return null;
            var header = I32(dib, 0); var bits = U16(dib, 14); var compression = I32(dib, 16); var colors = I32(dib, 32);
            if (header < 12 || header > dib.Length) return null;
            var palette = (colors > 0 ? colors : bits <= 8 ? 1 << bits : 0) * 4 + (compression == 3 && header == 40 ? 12 : 0);
            var result = new byte[14 + dib.Length];
            result[0] = (byte)'B'; result[1] = (byte)'M';
            BitConverter.GetBytes(result.Length).CopyTo(result, 2);
            BitConverter.GetBytes(14 + header + palette).CopyTo(result, 10);
            dib.CopyTo(result, 14);
            return result;
        }
        // Formatted disk pages (FKP) give paragraph (PAPX) or character (CHPX) properties by file position.
        private sealed class FkpLookup
        {
            private readonly byte[] word; private readonly int[] bounds; private readonly int[] pages; private readonly bool paragraphs;
            public FkpLookup(byte[] word, byte[] table, int offset, int size, bool paragraphs)
            {
                this.word = word; this.paragraphs = paragraphs;
                if (offset < 0 || size < 8 || (size - 4) % 8 != 0 || (long)offset + size > table.Length) { bounds = new int[0]; pages = new int[0]; return; }
                var count = (size - 4) / 8;
                bounds = new int[count + 1]; pages = new int[count];
                for (var i = 0; i <= count; i++) bounds[i] = I32(table, offset + i * 4);
                for (var i = 0; i < count; i++) pages[i] = (int)(U32(table, offset + (count + 1) * 4 + i * 4) & 0x3FFFFF);
            }
            public bool Find(int position, out byte[] grpprl, out int from, out int to, out int istd)
            {
                int low, high; istd = 0;
                if (!Find(position, out grpprl, out from, out to, out low, out high)) return false;
                if (!paragraphs) return true;
                istd = U16(word, from); from += 2; return true;
            }
            public bool Find(int position, out byte[] grpprl, out int from, out int to, out int low, out int high)
            {
                grpprl = word; from = to = 0; low = high = -1;
                int page;
                if (!Locate(position, out page, out low, out high)) return false;
                var pos = page; var cb = word[pos];
                if (paragraphs)
                {
                    int bytes;
                    if (cb == 0) { bytes = 2 * word[pos + 1]; pos += 2; } else { bytes = 2 * cb - 1; pos += 1; }
                    if (bytes < 2 || pos + bytes > word.Length) return false;
                    from = pos; to = pos + bytes; return true;
                }
                if (pos + 1 + cb > word.Length) return false;
                from = pos + 1; to = pos + 1 + cb; return true;
            }
            private bool Locate(int position, out int properties, out int start, out int end)
            {
                properties = 0; start = end = -1;
                for (var i = 0; i < pages.Length; i++)
                {
                    if (position < bounds[i] || position >= bounds[i + 1]) continue;
                    var fkp = (long)pages[i] * 512;
                    if (fkp + 512 > word.Length) return false;
                    var crun = word[fkp + 511];
                    if ((crun + 1) * 4 + crun * (paragraphs ? 13 : 1) > 511) return false;
                    for (var j = 0; j < crun; j++)
                    {
                        var first = I32(word, (int)fkp + j * 4); var next = I32(word, (int)fkp + (j + 1) * 4);
                        if (position < first || position >= next) continue;
                        start = first; end = next;
                        var offset = paragraphs ? word[fkp + (crun + 1) * 4 + j * 13] : word[fkp + (crun + 1) * 4 + j];
                        if (offset == 0) return false;
                        properties = (int)fkp + offset * 2; return true;
                    }
                    return false;
                }
                return false;
            }
        }
        // Visits each property modifier with the offset of its operand.
        private static void Sprms(byte[] buffer, int from, int to, Action<int, int> visit)
        {
            var pos = from;
            while (pos + 2 <= to)
            {
                var sprm = U16(buffer, pos); pos += 2;
                int size, operand = pos;
                switch (sprm >> 13)
                {
                    case 0: case 1: size = 1; break;
                    case 2: case 4: case 5: size = 2; break;
                    case 3: size = 4; break;
                    case 7: size = 3; break;
                    default:
                        if (pos >= to) return;
                        if (sprm == 0xD608 || sprm == 0xD606) { if (pos + 2 > to) return; size = U16(buffer, pos) + 1; operand = pos + 2; }
                        else { if (sprm == 0xC615 && buffer[pos] == 255) return; size = buffer[pos] + 1; operand = pos + 1; }
                        break;
                }
                if (pos + size > to) return;
                visit(sprm, operand);
                pos += size;
            }
        }
        private static byte[] Slice(byte[] buffer, int start, int length)
        {
            if (length < 0 || start < 0 || start + length > buffer.Length) throw new InvalidDataException("Damaged document");
            var result = new byte[length]; Array.Copy(buffer, start, result, 0, length); return result;
        }
        private static ushort U16(byte[] buffer, int offset) { return CompoundFile.U16(buffer, offset); }
        private static uint U32(byte[] buffer, int offset) { return CompoundFile.U32(buffer, offset); }
        private static int I32(byte[] buffer, int offset) { return unchecked((int)CompoundFile.U32(buffer, offset)); }
    }
}
