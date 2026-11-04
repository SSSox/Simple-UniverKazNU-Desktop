using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Univer.Desktop.Core
{
    // Bundled read-only renderer for PowerPoint 97-2003 .ppt files into the same Deck model as PPTX (SlideMarkup). It follows
    // the persist directory to each slide's current version; encrypted and damaged files throw InvalidDataException.
    internal static class PptHtml
    {
        private const long FileLimit = 64L * 1024 * 1024;
        private struct Record { public int Type, Instance, Version, Body, Length; public int End { get { return Body + Length; } } public bool Container { get { return Version == 0xF; } } }
        private sealed class Text { public int Type; public string Value = ""; public byte[] Style; public bool Used; }
        private sealed class SlideEntry { public uint Persist; public readonly List<Text> Texts = new List<Text>(); }
        private sealed class Context
        {
            public byte[] Document, Pictures; public Deck Deck; public CancellationToken Cancellation;
            public List<int> Blips = new List<int>(); public List<byte[]> BlipSources = new List<byte[]>();
            public double Width = 5760; public SlideEntry Slide;
            // Inside a table group: cells without text still hold their place in the grid.
            public bool Cells;
        }
        public static PreviewDocument Convert(string path, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            if (new FileInfo(path).Length > FileLimit) throw new PreviewLimitException();
            return Convert(File.ReadAllBytes(path), options, cancellation);
        }
        public static PreviewDocument Convert(byte[] file, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            Deck deck;
            try { deck = Read(file, cancellation); }
            catch (IndexOutOfRangeException) { throw new InvalidDataException("Damaged presentation"); }
            catch (ArgumentException) { throw new InvalidDataException("Damaged presentation"); }
            catch (OverflowException) { throw new InvalidDataException("Damaged presentation"); }
            return SlideMarkup.Write(deck, options, cancellation);
        }
        internal static Deck Read(byte[] file, CancellationToken cancellation)
        {
            var container = new CompoundFile(file);
            var document = container.Stream("PowerPoint Document") ?? throw new InvalidDataException("Not a presentation");
            var context = new Context { Document = document, Pictures = container.Stream("Pictures"), Deck = new Deck(), Cancellation = cancellation };
            uint documentRef;
            var offsets = Persist(document, container.Stream("Current User"), out documentRef);
            int at;
            if (!offsets.TryGetValue(documentRef, out at)) throw new InvalidDataException("Damaged presentation");
            var root = Header(document, at);
            if (root.Type != 0x03E8 || !root.Container) throw new InvalidDataException("Damaged presentation");
            var slides = new List<SlideEntry>();
            foreach (var child in Children(document, root))
            {
                if (child.Type == 0x03E9 && child.Length >= 8) { var width = I32(document, child.Body); if (width > 0) context.Width = width; }
                else if (child.Type == 0x0FF0 && child.Instance == 0) SlideList(document, child, slides);
                else if (child.Type == 0x040B) Pictures(context, child);
            }
            foreach (var slide in slides)
            {
                cancellation.ThrowIfCancellationRequested();
                if (context.Deck.Slides.Count >= SlideMarkup.SlideLimit) throw new PreviewLimitException();
                var result = new Slide(); context.Slide = slide;
                int offset;
                if (offsets.TryGetValue(slide.Persist, out offset) && offset >= 0 && offset + 8 <= document.Length)
                {
                    var record = Header(document, offset);
                    if (record.Type == 0x03EE && record.Container)
                        foreach (var drawing in Children(document, record).Where(r => r.Type == 0x040C && r.Container))
                            foreach (var group in Children(document, drawing).Where(r => r.Type == 0xF002 && r.Container))
                                foreach (var tree in Children(document, group).Where(r => r.Type == 0xF003 && r.Container))
                                    result.Blocks.AddRange(Group(context, tree, true));
                }
                // Text that no shape refers to (older writers keep it only in the slide list) still shows, after the shapes.
                var order = 1e9;
                foreach (var text in slide.Texts.Where(t => !t.Used))
                {
                    var block = TextBlock(text, order++, 0);
                    if (block != null) result.Blocks.Add(block);
                }
                context.Deck.Slides.Add(result);
            }
            return context.Deck;
        }
        // Persist object id → offset in the PowerPoint Document stream, newest edit first.
        private static Dictionary<uint, int> Persist(byte[] document, byte[] current, out uint documentRef)
        {
            if (current == null || current.Length < 20) throw new InvalidDataException("Damaged presentation");
            var token = U32(current, 12);
            if (token == 0xF3D1C4DF) throw new InvalidDataException("Encrypted presentation");
            var edit = U32(current, 16);
            var offsets = new Dictionary<uint, int>(); documentRef = 0;
            for (var guard = 0; ; guard++)
            {
                if (guard > 4096 || edit + 8 > (uint)document.Length) throw new InvalidDataException("Damaged presentation");
                var record = Header(document, (int)edit);
                if (record.Type != 0x0FF5 || record.Length < 28) throw new InvalidDataException("Damaged presentation");
                if (guard == 0)
                {
                    documentRef = U32(document, record.Body + 16);
                    if (record.Length >= 32 && U32(document, record.Body + 28) != 0) throw new InvalidDataException("Encrypted presentation");
                }
                var directory = U32(document, record.Body + 12);
                if (directory + 8 > (uint)document.Length) throw new InvalidDataException("Damaged presentation");
                var list = Header(document, (int)directory);
                if (list.Type != 0x1772) throw new InvalidDataException("Damaged presentation");
                for (var position = list.Body; position + 4 <= list.End;)
                {
                    var entry = U32(document, position); position += 4;
                    uint id = entry & 0xFFFFF, count = entry >> 20;
                    for (var i = 0u; i < count && position + 4 <= list.End; i++, position += 4)
                        if (!offsets.ContainsKey(id + i)) offsets[id + i] = (int)U32(document, position);
                }
                edit = U32(document, record.Body + 8);
                if (edit == 0) break;
            }
            return offsets;
        }
        private static void SlideList(byte[] document, Record list, List<SlideEntry> slides)
        {
            SlideEntry slide = null; Text text = null;
            foreach (var record in Children(document, list))
            {
                switch (record.Type)
                {
                    case 0x03F3: if (record.Length >= 4) { slide = new SlideEntry { Persist = U32(document, record.Body) }; slides.Add(slide); text = null; } break;
                    case 0x0F9F: if (slide != null && record.Length >= 4) { text = new Text { Type = (int)U32(document, record.Body) }; slide.Texts.Add(text); } break;
                    case 0x0FA0: if (text != null) text.Value = Encoding.Unicode.GetString(document, record.Body, record.Length & ~1); break;
                    case 0x0FA8: if (text != null) text.Value = Encoding.GetEncoding(28591).GetString(document, record.Body, record.Length); break;
                    case 0x0FA1: if (text != null) text.Style = Slice(document, record.Body, record.Length); break;
                }
            }
        }
        // The picture store: blip records numbered from 1, kept in this stream or in the Pictures stream.
        private static void Pictures(Context context, Record drawingGroup)
        {
            var document = context.Document;
            foreach (var dgg in Children(document, drawingGroup).Where(r => r.Type == 0xF000 && r.Container))
                foreach (var store in Children(document, dgg).Where(r => r.Type == 0xF001 && r.Container))
                    foreach (var entry in Children(document, store))
                    {
                        int offset = -1; byte[] source = null;
                        if (entry.Type == 0xF007 && entry.Length >= 36)
                        {
                            var name = document[entry.Body + 33];
                            if (entry.Length > 36 + name + 8) { offset = entry.Body + 36 + name; source = document; }
                            else if (context.Pictures != null && U32(document, entry.Body + 24) > 0) { offset = (int)U32(document, entry.Body + 28); source = context.Pictures; }
                        }
                        context.Blips.Add(offset); context.BlipSources.Add(source);
                    }
        }
        private static List<SlideBlock> Group(Context context, Record tree, bool root)
        {
            var document = context.Document; var blocks = new List<SlideBlock>(); var first = true;
            foreach (var child in Children(document, tree))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (child.Type == 0xF003 && child.Container)
                {
                    var cells = context.Cells; context.Cells = Table(document, child);
                    var inner = Group(context, child, false);
                    context.Cells = cells;
                    if (inner.Count == 0) continue;
                    // The first shape of a nested group is the group itself; its position orders the whole group.
                    var head = inner[0].Kind == SlideBlockKind.Group && inner[0].Children.Count == 0 ? inner[0] : null;
                    if (Table(document, child))
                    {
                        var table = Grid(head != null ? inner.Skip(1).ToList() : inner);
                        if (table != null) { table.Top = head?.Top ?? table.Top; table.Left = head?.Left ?? table.Left; blocks.Add(table); continue; }
                    }
                    var group = new SlideBlock { Kind = SlideBlockKind.Group, Top = head?.Top ?? inner.Min(b => b.Top), Left = head?.Left ?? inner.Min(b => b.Left) };
                    group.Children.AddRange(head != null ? inner.Skip(1) : inner);
                    if (group.Children.Count > 0) blocks.Add(group);
                }
                else if (child.Type == 0xF004 && child.Container)
                {
                    var block = Shape(context, child, first && !root);
                    if (block != null) blocks.Add(block);
                }
                first = false;
            }
            return blocks;
        }
        // One shape: its position, then its text (own or from the slide list) or its picture. The first shape of a nested
        // group comes back as an empty Group block that carries the group's position.
        private static SlideBlock Shape(Context context, Record shape, bool groupHead)
        {
            var document = context.Document;
            double top = 0, left = 0, right = 0, bottom = 0; var placement = -1; var picture = -1; Text text = null; var deleted = false;
            foreach (var part in Children(document, shape))
            {
                switch (part.Type)
                {
                    case 0xF00A: if (part.Length >= 8) deleted = (U32(document, part.Body + 4) & 0x8) != 0; break;
                    case 0xF00B:
                        for (var i = 0; i < part.Instance && part.Body + i * 6 + 6 <= part.End; i++)
                            if ((U16(document, part.Body + i * 6) & 0x3FFF) == 0x0104) picture = (int)U32(document, part.Body + i * 6 + 2);
                        break;
                    case 0xF010:
                        if (part.Length >= 16) { top = I32(document, part.Body); left = I32(document, part.Body + 4); right = I32(document, part.Body + 8); bottom = I32(document, part.Body + 12); }
                        else if (part.Length >= 8) { top = I16(document, part.Body); left = I16(document, part.Body + 2); right = I16(document, part.Body + 4); bottom = I16(document, part.Body + 6); }
                        break;
                    case 0xF00F:
                        if (part.Length >= 16) { left = I32(document, part.Body); top = I32(document, part.Body + 4); right = I32(document, part.Body + 8); bottom = I32(document, part.Body + 12); }
                        break;
                    case 0xF011:
                        foreach (var data in Children(document, part)) if (data.Type == 0x0BC3 && data.Length >= 5) placement = document[data.Body + 4];
                        break;
                    case 0xF00D:
                        Text own = null;
                        foreach (var item in Children(document, part))
                        {
                            if (item.Type == 0x0F9E && item.Length >= 4)
                            {
                                var index = I32(document, item.Body);
                                if (context.Slide != null && index >= 0 && index < context.Slide.Texts.Count) { text = context.Slide.Texts[index]; text.Used = true; }
                            }
                            else if (item.Type == 0x0F9F && item.Length >= 4) { own = new Text { Type = (int)U32(document, item.Body) }; text = own; }
                            else if (item.Type == 0x0FA0 && own != null) own.Value = Encoding.Unicode.GetString(document, item.Body, item.Length & ~1);
                            else if (item.Type == 0x0FA8 && own != null) own.Value = Encoding.GetEncoding(28591).GetString(document, item.Body, item.Length);
                            else if (item.Type == 0x0FA1 && own != null) own.Style = Slice(document, item.Body, item.Length);
                        }
                        break;
                }
            }
            if (groupHead) return new SlideBlock { Kind = SlideBlockKind.Group, Top = top, Left = left };
            if (deleted) return null;
            // Dates, slide numbers, footers and headers repeat on every slide.
            if (placement >= 0x07 && placement <= 0x0A) return null;
            if (text != null)
            {
                var title = placement == 0x0D || placement == 0x0F || placement == 0x11 || placement == 0x01 || placement == 0x03;
                var block = TextBlock(text, top, left, title);
                if (block == null && context.Cells) block = new SlideBlock { Kind = SlideBlockKind.Text, Top = top, Left = left };
                if (block != null) { block.Right = right; block.Bottom = bottom; }
                return block;
            }
            if (picture > 0 && picture <= context.Blips.Count)
            {
                string path;
                if (!Blip(context, picture - 1, out path)) return null;
                return new SlideBlock { Kind = SlideBlockKind.Picture, Image = path, Top = top, Left = left, Width = right > left ? (right - left) / context.Width : 0 };
            }
            return null;
        }
        // A group is a table when its own shape carries the table properties (0x039F) with fHasTable set.
        private static bool Table(byte[] document, Record group)
        {
            foreach (var shape in Children(document, group))
            {
                if (shape.Type != 0xF004 || !shape.Container) continue;
                foreach (var part in Children(document, shape))
                    if (part.Type == 0xF00B || part.Type == 0xF122)
                        for (var i = 0; i < part.Instance && part.Body + i * 6 + 6 <= part.End; i++)
                            if ((U16(document, part.Body + i * 6) & 0x3FFF) == 0x039F && (U32(document, part.Body + i * 6 + 2) & 1) != 0) return true;
                return false;
            }
            return false;
        }
        // Cell shapes to rows and columns by their edges; a cell over several grid lines spans them.
        private static SlideBlock Grid(List<SlideBlock> cells)
        {
            cells = cells.Where(c => c.Kind == SlideBlockKind.Text && c.Right > c.Left && c.Bottom > c.Top).ToList();
            if (cells.Count == 0) return null;
            Func<IEnumerable<double>, List<double>> lines = values =>
            {
                var result = new List<double>();
                foreach (var value in values.OrderBy(v => v)) if (result.Count == 0 || value - result[result.Count - 1] > 2) result.Add(value);
                return result;
            };
            var rows = lines(cells.Select(c => c.Top)); var columns = lines(cells.Select(c => c.Left));
            if (rows.Count * columns.Count > 10000) return null;
            Func<List<double>, double, int> index = (list, value) => { var best = 0; for (var i = 0; i < list.Count; i++) if (Math.Abs(list[i] - value) < Math.Abs(list[best] - value)) best = i; return best; };
            var grid = new TableModel.Cell[rows.Count, columns.Count];
            foreach (var cell in cells)
            {
                int r = index(rows, cell.Top), c = index(columns, cell.Left);
                if (grid[r, c] != null) continue;
                var height = Math.Max(1, rows.Count(v => v >= cell.Top - 2 && v < cell.Bottom - 2));
                var width = Math.Max(1, columns.Count(v => v >= cell.Left - 2 && v < cell.Right - 2));
                for (var y = r; y < Math.Min(rows.Count, r + height); y++)
                    for (var x = c; x < Math.Min(columns.Count, c + width); x++)
                        if (grid[y, x] == null) grid[y, x] = new TableModel.Cell { Continuation = y != r || x != c };
                grid[r, c] = new TableModel.Cell { Span = width, Rows = height, Source = cell.Paragraphs };
            }
            var model = new TableModel { Columns = columns.Count };
            var right = cells.Max(c => c.Right);
            model.Widths = columns.Select((left, i) => (int)Math.Round((i + 1 < columns.Count ? columns[i + 1] : right) - left)).ToArray();
            for (var y = 0; y < rows.Count; y++)
            {
                var row = new List<TableModel.Cell>();
                for (var x = 0; x < columns.Count; x++) row.Add(grid[y, x] ?? new TableModel.Cell { Source = new List<SlideParagraph>() });
                model.Rows.Add(row);
            }
            return new SlideBlock { Kind = SlideBlockKind.Table, Table = model, Top = rows[0], Left = columns[0] };
        }
        private static SlideBlock TextBlock(Text text, double top, double left, bool title = false)
        {
            title |= text.Type == 0 || text.Type == 6;
            // Bodies of the slide layouts are lists unless the text says otherwise; subtitles and text boxes are not.
            var bulleted = text.Type == 1 || text.Type == 7 || text.Type == 8;
            var block = new SlideBlock { Kind = title ? SlideBlockKind.Title : SlideBlockKind.Text, Top = top, Left = left, Paragraphs = Paragraphs(text, bulleted) };
            return block.Paragraphs.Any(p => p.HasText) ? block : null;
        }
        private sealed class ParagraphFormat { public int Count, Level, Align; public bool? Bullet; public char BulletChar; }
        private sealed class CharacterFormat { public int Count; public RunStyle Style; }
        private static List<SlideParagraph> Paragraphs(Text text, bool bulleted)
        {
            var value = text.Value ?? "";
            List<ParagraphFormat> paragraphs; List<CharacterFormat> characters;
            if (!Styles(text.Style, value.Length + 1, out paragraphs, out characters)) { paragraphs = new List<ParagraphFormat>(); characters = new List<CharacterFormat>(); }
            var result = new List<SlideParagraph>();
            int paragraphRun = 0, paragraphLeft = paragraphs.Count > 0 ? paragraphs[0].Count : int.MaxValue;
            int characterRun = 0, characterLeft = characters.Count > 0 ? characters[0].Count : int.MaxValue;
            SlideParagraph current = null; var run = new StringBuilder(); var runStyle = default(RunStyle); var hasRunStyle = false;
            Action flush = () => { if (run.Length > 0 && current != null) current.Runs.Add(new SlideRun { Text = run.ToString(), Style = runStyle }); run.Clear(); };
            for (var i = 0; i <= value.Length; i++)
            {
                if (current == null)
                {
                    var format = paragraphRun < paragraphs.Count ? paragraphs[paragraphRun] : null;
                    current = new SlideParagraph { Level = Math.Max(0, Math.Min(8, format?.Level ?? 0)), Align = format?.Align ?? 0 };
                    var bullet = format?.Bullet ?? bulleted;
                    if (bullet) current.Marker = format != null && format.BulletChar != '\0' ? DocumentMarkup.Bullet(format.BulletChar.ToString()) : "•";
                    result.Add(current);
                }
                var style = characterRun < characters.Count ? characters[characterRun].Style : default(RunStyle);
                if (!hasRunStyle || !style.SameAs(runStyle)) { flush(); runStyle = style; hasRunStyle = true; }
                var end = i == value.Length;
                var c = end ? '\r' : value[i];
                if (c == '\r') { flush(); current = null; }
                else if (c == '\v') { flush(); current.Runs.Add(new SlideRun { Break = true }); }
                else if (c >= ' ' || c == '\t') run.Append(c == '\t' ? ' ' : c);
                if (--paragraphLeft <= 0 && paragraphRun < paragraphs.Count) { paragraphRun++; paragraphLeft = paragraphRun < paragraphs.Count ? paragraphs[paragraphRun].Count : int.MaxValue; }
                if (--characterLeft <= 0 && characterRun < characters.Count) { characterRun++; characterLeft = characterRun < characters.Count ? characters[characterRun].Count : int.MaxValue; }
            }
            return result;
        }
        // StyleTextPropAtom: paragraph runs, then character runs, each covering the text plus its final mark. A layout
        // this reader does not follow drops only the formatting.
        private static bool Styles(byte[] data, int length, out List<ParagraphFormat> paragraphs, out List<CharacterFormat> characters)
        {
            paragraphs = new List<ParagraphFormat>(); characters = new List<CharacterFormat>();
            if (data == null || data.Length < 10) return false;
            try
            {
                var position = 0; var covered = 0;
                while (covered < length && position + 10 <= data.Length)
                {
                    var format = new ParagraphFormat { Count = (int)U32(data, position), Level = U16(data, position + 4) };
                    var masks = U32(data, position + 6); position += 10;
                    if (format.Count <= 0) return false;
                    if ((masks & 0x000F) != 0) { var flags = U16(data, position); position += 2; if ((masks & 1) != 0) format.Bullet = (flags & 1) != 0; }
                    if ((masks & 0x0080) != 0) { format.BulletChar = (char)U16(data, position); position += 2; }
                    if ((masks & 0x0010) != 0) position += 2;
                    if ((masks & 0x0040) != 0) position += 2;
                    if ((masks & 0x0020) != 0) position += 4;
                    if ((masks & 0x0800) != 0) { var align = U16(data, position); position += 2; format.Align = align == 1 ? 1 : align == 2 ? 2 : align == 3 || align == 4 ? 3 : 0; }
                    if ((masks & 0x1000) != 0) position += 2;
                    if ((masks & 0x2000) != 0) position += 2;
                    if ((masks & 0x4000) != 0) position += 2;
                    if ((masks & 0x0100) != 0) position += 2;
                    if ((masks & 0x0400) != 0) position += 2;
                    if ((masks & 0x8000) != 0) position += 2;
                    if ((masks & 0x100000) != 0) { var stops = U16(data, position); position += 2 + stops * 4; }
                    if ((masks & 0x10000) != 0) position += 2;
                    if ((masks & 0xE0000) != 0) position += 2;
                    if ((masks & 0x200000) != 0) position += 2;
                    if (position > data.Length) return false;
                    paragraphs.Add(format); covered += format.Count;
                }
                covered = 0;
                while (covered < length && position + 8 <= data.Length)
                {
                    var format = new CharacterFormat { Count = (int)U32(data, position) };
                    var masks = U32(data, position + 4); position += 8;
                    if (format.Count <= 0) return false;
                    var style = new RunStyle();
                    if ((masks & 0xFFFF) != 0)
                    {
                        var flags = U16(data, position); position += 2;
                        style.Bold = (masks & 1) != 0 && (flags & 1) != 0; style.Italic = (masks & 2) != 0 && (flags & 2) != 0; style.Underline = (masks & 4) != 0 && (flags & 4) != 0;
                    }
                    if ((masks & 0x10000) != 0) position += 2;
                    if ((masks & 0x200000) != 0) position += 2;
                    if ((masks & 0x400000) != 0) position += 2;
                    if ((masks & 0x800000) != 0) position += 2;
                    if ((masks & 0x20000) != 0) { var size = U16(data, position); position += 2; if (size > 0 && size < 4000) style.Size = size * 2; }
                    if ((masks & 0x40000) != 0)
                    {
                        if (position + 4 > data.Length) return false;
                        if (data[position + 3] == 0xFE) style.Color = SlideMarkup.Readable(data[position].ToString("x2") + data[position + 1].ToString("x2") + data[position + 2].ToString("x2"));
                        position += 4;
                    }
                    if ((masks & 0x80000) != 0) { var shift = I16(data, position); position += 2; if (shift != 0) style.Script = shift > 0 ? 1 : 2; }
                    if ((masks & 0x100000) != 0) position += 4;
                    if ((masks & 0x1000000) != 0) position += 2;
                    if ((masks & 0x2000000) != 0) position += 2;
                    if ((masks & 0x4000000) != 0) position += 4;
                    if (position > data.Length) return false;
                    format.Style = style; characters.Add(format); covered += format.Count;
                }
                return true;
            }
            catch (InvalidDataException) { paragraphs.Clear(); characters.Clear(); return false; }
        }
        // PNG, JPEG and device-independent bitmaps (shown as BMP); metafiles stay with the user's own program.
        private static bool Blip(Context context, int index, out string path)
        {
            path = null;
            var source = context.BlipSources[index]; var offset = context.Blips[index];
            if (source == null || offset < 0 || offset + 8 > source.Length) return false;
            var key = (source == context.Document ? "d" : "p") + offset;
            path = context.Deck.Images.Find(key);
            if (path != null) return true;
            var record = Header(source, offset);
            if (record.End > source.Length) return false;
            string mime;
            switch (record.Type)
            {
                case 0xF01E: mime = "image/png"; break;
                case 0xF01D: case 0xF02A: mime = "image/jpeg"; break;
                case 0xF01F: mime = "image/bmp"; break;
                default: return false;
            }
            var skip = ((record.Instance & 1) != 0 ? 32 : 16) + 1;
            if (record.Length <= skip) return false;
            if (!context.Deck.Images.Fits(record.Length)) return true;
            var data = Slice(source, record.Body + skip, record.Length - skip);
            if (mime == "image/bmp") data = Bitmap(data);
            if (data == null) return false;
            path = context.Deck.Images.Add(key, data, mime);
            return true;
        }
        // A DIB with the file header a .bmp file starts with.
        private static byte[] Bitmap(byte[] dib)
        {
            if (dib.Length < 40) return null;
            var header = (int)U32(dib, 0); var bits = U16(dib, 14); var compression = U32(dib, 16); var used = (int)U32(dib, 32);
            if (header < 12 || header > dib.Length) return null;
            var colors = used > 0 ? used : bits <= 8 ? 1 << bits : 0;
            var masks = header == 40 && (compression == 3 || compression == 6) ? (compression == 3 ? 12 : 16) : 0;
            var data = new byte[14 + dib.Length];
            data[0] = (byte)'B'; data[1] = (byte)'M';
            BitConverter.GetBytes(data.Length).CopyTo(data, 2);
            BitConverter.GetBytes(14 + header + masks + colors * 4).CopyTo(data, 10);
            Array.Copy(dib, 0, data, 14, dib.Length);
            return data;
        }
        private static Record Header(byte[] data, int offset)
        {
            var info = U16(data, offset);
            var record = new Record { Version = info & 0xF, Instance = info >> 4, Type = U16(data, offset + 2), Body = offset + 8 };
            var length = U32(data, offset + 4);
            if (length > data.Length - record.Body) throw new InvalidDataException("Damaged presentation");
            record.Length = (int)length;
            return record;
        }
        private static IEnumerable<Record> Children(byte[] data, Record container)
        {
            for (var position = container.Body; position + 8 <= container.End;)
            {
                var record = Header(data, position);
                if (record.End > container.End) throw new InvalidDataException("Damaged presentation");
                yield return record;
                position = record.End;
            }
        }
        private static byte[] Slice(byte[] data, int offset, int length) { var result = new byte[length]; Array.Copy(data, offset, result, 0, length); return result; }
        private static int U16(byte[] buffer, int offset) { return CompoundFile.U16(buffer, offset); }
        private static uint U32(byte[] buffer, int offset) { return CompoundFile.U32(buffer, offset); }
        private static int I16(byte[] buffer, int offset) { return (short)CompoundFile.U16(buffer, offset); }
        private static int I32(byte[] buffer, int offset) { return (int)CompoundFile.U32(buffer, offset); }
    }
}
