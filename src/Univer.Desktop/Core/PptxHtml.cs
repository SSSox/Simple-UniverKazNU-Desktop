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
    // Bundled read-only PPTX (PPTM/PPSX/POTX) renderer into the Deck model and SlideMarkup, like PPT. Placeholder positions
    // missing on a slide come from its layout and master.
    internal static class PptxHtml
    {
        private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
        private const long PartLimit = 16L * 1024 * 1024;
        private sealed class Placeholder { public string Type, Index; public double Top, Left; }
        private sealed class Master
        {
            public List<Placeholder> Places = new List<Placeholder>();
            public Dictionary<string, string> Scheme = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
            public XElement[] Body = new XElement[9];   // a:lvlNpPr of the master's body style
        }
        private sealed class Layout { public List<Placeholder> Places = new List<Placeholder>(); public Master Master; }
        private sealed class Context
        {
            public ZipArchive Archive; public Deck Deck; public CancellationToken Cancellation; public double Width;
            public Dictionary<string, OfficePackage.Relation> Relations; public Layout Layout;
            public Dictionary<string, Layout> Layouts = new Dictionary<string, Layout>(StringComparer.Ordinal);
            public Dictionary<string, Master> Masters = new Dictionary<string, Master>(StringComparer.Ordinal);
        }
        public static PreviewDocument Convert(string path, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            using (var archive = ZipFile.OpenRead(path)) return Convert(archive, options, cancellation);
        }
        public static PreviewDocument Convert(ZipArchive archive, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            try { return SlideMarkup.Write(Read(archive, cancellation), options, cancellation); }
            catch (XmlException) { throw new InvalidDataException("Damaged presentation"); }
        }
        internal static Deck Read(ZipArchive archive, CancellationToken cancellation)
        {
            var main = archive.GetEntry("ppt/presentation.xml") ?? throw new InvalidDataException("Not a presentation");
            var presentation = DocxHtml.ReadXml(main, PartLimit, cancellation).Root ?? throw new InvalidDataException("Not a presentation");
            var relations = OfficePackage.Relations(archive, "ppt/presentation.xml", cancellation);
            var context = new Context { Archive = archive, Deck = new Deck(), Cancellation = cancellation };
            double width; context.Width = double.TryParse((string)presentation.Element(P + "sldSz")?.Attribute("cx"), NumberStyles.Float, CultureInfo.InvariantCulture, out width) && width > 0 ? width : 9144000;
            foreach (var id in presentation.Element(P + "sldIdLst")?.Elements(P + "sldId") ?? Enumerable.Empty<XElement>())
            {
                cancellation.ThrowIfCancellationRequested();
                if (context.Deck.Slides.Count >= SlideMarkup.SlideLimit) throw new PreviewLimitException();
                OfficePackage.Relation relation;
                var rid = (string)id.Attribute(R + "id");
                if (rid == null || !relations.TryGetValue(rid, out relation)) continue;
                var entry = archive.GetEntry(relation.Target);
                if (entry == null) continue;
                var slide = new Slide();
                context.Relations = OfficePackage.Relations(archive, relation.Target, cancellation);
                var layoutTarget = context.Relations.Values.FirstOrDefault(r => r.Type.EndsWith("/slideLayout", StringComparison.Ordinal))?.Target;
                context.Layout = layoutTarget == null ? new Layout { Master = new Master() } : LayoutOf(context, layoutTarget);
                var tree = DocxHtml.ReadXml(entry, PartLimit, cancellation).Root?.Element(P + "cSld")?.Element(P + "spTree");
                if (tree != null) Shapes(tree, context, slide.Blocks);
                context.Deck.Slides.Add(slide);
            }
            return context.Deck;
        }
        private static Layout LayoutOf(Context context, string target)
        {
            Layout layout;
            if (context.Layouts.TryGetValue(target, out layout)) return layout;
            layout = new Layout();
            context.Layouts[target] = layout;
            var entry = context.Archive.GetEntry(target);
            var relations = OfficePackage.Relations(context.Archive, target, context.Cancellation);
            var masterTarget = relations.Values.FirstOrDefault(r => r.Type.EndsWith("/slideMaster", StringComparison.Ordinal))?.Target;
            layout.Master = masterTarget == null ? new Master() : MasterOf(context, masterTarget);
            if (entry != null) { try { Places(DocxHtml.ReadXml(entry, PartLimit, context.Cancellation).Root, layout.Places); } catch (XmlException) { } }
            return layout;
        }
        private static Master MasterOf(Context context, string target)
        {
            Master master;
            if (context.Masters.TryGetValue(target, out master)) return master;
            master = new Master();
            context.Masters[target] = master;
            var entry = context.Archive.GetEntry(target);
            if (entry == null) return master;
            XElement root;
            try { root = DocxHtml.ReadXml(entry, PartLimit, context.Cancellation).Root; } catch (XmlException) { return master; }
            Places(root, master.Places);
            foreach (var attribute in root?.Element(P + "clrMap")?.Attributes() ?? Enumerable.Empty<XAttribute>()) master.Map[attribute.Name.LocalName] = attribute.Value;
            var body = root?.Element(P + "txStyles")?.Element(P + "bodyStyle");
            for (var i = 0; i < 9; i++) master.Body[i] = body?.Element(A + ("lvl" + (i + 1) + "pPr"));
            var relations = OfficePackage.Relations(context.Archive, target, context.Cancellation);
            var themeTarget = relations.Values.FirstOrDefault(r => r.Type.EndsWith("/theme", StringComparison.Ordinal))?.Target;
            var theme = themeTarget == null ? null : context.Archive.GetEntry(themeTarget);
            if (theme != null) { try { master.Scheme = OfficeColors.Scheme(DocxHtml.ReadXml(theme, PartLimit, context.Cancellation)); } catch (XmlException) { } }
            return master;
        }
        private static void Places(XElement root, List<Placeholder> places)
        {
            foreach (var shape in root?.Descendants(P + "sp") ?? Enumerable.Empty<XElement>())
            {
                var ph = shape.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph");
                var offset = shape.Element(P + "spPr")?.Element(A + "xfrm")?.Element(A + "off");
                double top, left;
                if (ph == null || offset == null || !Number(offset.Attribute("y"), out top) || !Number(offset.Attribute("x"), out left)) continue;
                places.Add(new Placeholder { Type = (string)ph.Attribute("type") ?? "obj", Index = (string)ph.Attribute("idx"), Top = top, Left = left });
            }
        }
        private static bool Number(XAttribute attribute, out double value)
        { value = 0; return attribute != null && double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value); }
        private static void Shapes(XElement tree, Context context, List<SlideBlock> blocks)
        {
            foreach (var element in tree.Elements())
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var name = element.Name.LocalName;
                if (name == "AlternateContent")
                {
                    // Newer content in mc:Choice, older in mc:Fallback; the fallback is what every reader understands.
                    var chosen = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Fallback") ?? element.Elements().FirstOrDefault();
                    if (chosen != null) Shapes(chosen, context, blocks);
                }
                else if (name == "sp") Shape(element, context, blocks);
                else if (name == "pic") Picture(element, context, blocks);
                else if (name == "graphicFrame") Frame(element, context, blocks);
                else if (name == "grpSp")
                {
                    var group = new SlideBlock { Kind = SlideBlockKind.Group };
                    Position(element.Element(P + "grpSpPr")?.Element(A + "xfrm"), group);
                    Shapes(element, context, group.Children);
                    if (group.Children.Count > 0) blocks.Add(group);
                }
            }
        }
        private static void Position(XElement transform, SlideBlock block)
        {
            var offset = transform?.Element(A + "off"); double value;
            if (Number(offset?.Attribute("y"), out value)) block.Top = value;
            if (Number(offset?.Attribute("x"), out value)) block.Left = value;
        }
        private static void Shape(XElement shape, Context context, List<SlideBlock> blocks)
        {
            var ph = shape.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph");
            var type = ph == null ? null : (string)ph.Attribute("type") ?? "obj";
            // Dates, footers and slide numbers repeat on every slide and add nothing to the content.
            if (type == "dt" || type == "ftr" || type == "sldNum" || type == "hdr" || type == "sldImg") return;
            var body = shape.Element(P + "txBody");
            if (body == null) return;
            var title = type == "title" || type == "ctrTitle";
            var block = new SlideBlock { Kind = title ? SlideBlockKind.Title : SlideBlockKind.Text };
            var transform = shape.Element(P + "spPr")?.Element(A + "xfrm");
            if (transform?.Element(A + "off") != null) Position(transform, block);
            else if (ph != null) Inherit(context, type, (string)ph.Attribute("idx"), block);
            block.Paragraphs = Paragraphs(body, context, type == "body" || type == "obj");
            if (block.Paragraphs.Any(p => p.HasText)) blocks.Add(block);
        }
        // A placeholder without its own position takes the layout's, then the master's, so titles and bodies keep their order.
        private static void Inherit(Context context, string type, string index, SlideBlock block)
        {
            Func<List<Placeholder>, Placeholder> find = places =>
                (index != null ? places.FirstOrDefault(p => p.Index == index) : null) ??
                places.FirstOrDefault(p => p.Type == type || (type == "ctrTitle" && p.Type == "title") || (type == "obj" && p.Type == "body") || (type == "subTitle" && p.Type == "body"));
            var place = find(context.Layout.Places) ?? find(context.Layout.Master.Places);
            if (place != null) { block.Top = place.Top; block.Left = place.Left; }
        }
        private static List<SlideParagraph> Paragraphs(XElement body, Context context, bool bulleted)
        {
            var result = new List<SlideParagraph>();
            var counters = new int?[9]; string lastScheme = null;
            var master = context.Layout.Master;
            foreach (var p in body.Elements(A + "p"))
            {
                var properties = p.Element(A + "pPr");
                int level; if (!int.TryParse((string)properties?.Attribute("lvl"), out level) || level < 0 || level > 8) level = 0;
                var paragraph = new SlideParagraph { Level = level };
                switch ((string)properties?.Attribute("algn"))
                {
                    case "ctr": paragraph.Align = 1; break;
                    case "r": paragraph.Align = 2; break;
                    case "just": case "dist": paragraph.Align = 3; break;
                }
                foreach (var node in p.Elements())
                {
                    if (node.Name == A + "br") paragraph.Runs.Add(new SlideRun { Break = true });
                    else if (node.Name == A + "r" || node.Name == A + "fld")
                    {
                        var text = (string)node.Element(A + "t");
                        if (string.IsNullOrEmpty(text)) continue;
                        string link;
                        paragraph.Runs.Add(new SlideRun { Text = text, Style = Run(node.Element(A + "rPr"), context, out link), Link = link });
                    }
                }
                // Bullets: the paragraph's own, else the master's body style for placeholders that are lists.
                var source = properties != null && (properties.Element(A + "buNone") != null || properties.Element(A + "buChar") != null || properties.Element(A + "buAutoNum") != null)
                    ? properties : bulleted ? master.Body[level] : null;
                if (paragraph.HasText && source != null && source.Element(A + "buNone") == null)
                {
                    var number = source.Element(A + "buAutoNum");
                    if (number != null)
                    {
                        var scheme = (string)number.Attribute("type");
                        int start; if (!int.TryParse((string)number.Attribute("startAt"), out start)) start = 1;
                        if (scheme != lastScheme) counters[level] = null;
                        counters[level] = counters[level].HasValue ? counters[level] + 1 : start;
                        paragraph.Marker = SlideMarkup.AutoNumber(scheme, counters[level].Value); lastScheme = scheme;
                    }
                    else if (source.Element(A + "buChar") != null) paragraph.Marker = DocumentMarkup.Bullet((string)source.Element(A + "buChar").Attribute("char"));
                }
                if (paragraph.HasText) for (var i = level + 1; i < 9; i++) counters[i] = null;
                if (paragraph.HasText && paragraph.Marker == null) { counters[level] = null; }
                result.Add(paragraph);
            }
            return result;
        }
        private static RunStyle Run(XElement properties, Context context, out string link)
        {
            var style = new RunStyle(); link = null;
            if (properties == null) return style;
            style.Bold = Flag(properties.Attribute("b")); style.Italic = Flag(properties.Attribute("i"));
            var underline = (string)properties.Attribute("u"); style.Underline = underline != null && underline != "none";
            var strike = (string)properties.Attribute("strike"); style.Strike = strike != null && strike != "noStrike";
            int size; if (int.TryParse((string)properties.Attribute("sz"), out size) && size >= 100 && size < 400000) style.Size = size / 50;
            int baseline; if (int.TryParse((string)properties.Attribute("baseline"), out baseline) && baseline != 0) style.Script = baseline > 0 ? 1 : 2;
            var cap = (string)properties.Attribute("cap"); style.Caps = cap == "all"; style.SmallCaps = cap == "small";
            style.Color = SlideMarkup.Readable(Color(properties.Element(A + "solidFill"), context));
            var click = properties.Element(A + "hlinkClick");
            var id = (string)click?.Attribute(R + "id"); OfficePackage.Relation relation;
            if (id != null && context.Relations.TryGetValue(id, out relation) && relation.External &&
                (relation.Target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || relation.Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))) link = relation.Target;
            return style;
        }
        private static bool Flag(XAttribute attribute) { return attribute != null && (attribute.Value == "1" || attribute.Value == "true"); }
        private static string Color(XElement fill, Context context)
        {
            var color = fill?.Elements().FirstOrDefault();
            if (color == null) return null;
            string value = null;
            switch (color.Name.LocalName)
            {
                case "srgbClr": value = OfficeColors.FromArgb((string)color.Attribute("val")); break;
                case "sysClr": value = OfficeColors.FromArgb((string)color.Attribute("lastClr")); break;
                case "schemeClr":
                    var name = (string)color.Attribute("val") ?? ""; string mapped;
                    var master = context.Layout.Master;
                    if (master.Map.TryGetValue(name, out mapped)) name = mapped;
                    else if (name == "tx1") name = "dk1"; else if (name == "bg1") name = "lt1"; else if (name == "tx2") name = "dk2"; else if (name == "bg2") name = "lt2";
                    master.Scheme.TryGetValue(name, out value);
                    break;
            }
            int modulation, offset;
            if (value != null && int.TryParse((string)color.Element(A + "lumMod")?.Attribute("val"), out modulation))
                value = OfficeColors.Modulate(value, modulation, int.TryParse((string)color.Element(A + "lumOff")?.Attribute("val"), out offset) ? offset : 0);
            return value;
        }
        private static void Picture(XElement picture, Context context, List<SlideBlock> blocks)
        {
            var id = (string)picture.Element(P + "blipFill")?.Element(A + "blip")?.Attribute(R + "embed");
            OfficePackage.Relation relation; string path;
            if (id == null || !context.Relations.TryGetValue(id, out relation) || relation.External) return;
            if (!OfficePackage.Image(context.Archive, relation.Target, context.Deck.Images, context.Cancellation, out path)) return;
            var block = new SlideBlock { Kind = SlideBlockKind.Picture, Image = path };
            var transform = picture.Element(P + "spPr")?.Element(A + "xfrm");
            Position(transform, block);
            double width; if (Number(transform?.Element(A + "ext")?.Attribute("cx"), out width)) block.Width = width / context.Width;
            blocks.Add(block);
        }
        private static void Frame(XElement frame, Context context, List<SlideBlock> blocks)
        {
            var data = frame.Element(A + "graphic")?.Element(A + "graphicData");
            if (data == null) return;
            var block = new SlideBlock();
            Position(frame.Element(P + "xfrm"), block);
            var table = data.Element(A + "tbl");
            if (table != null)
            {
                block.Kind = SlideBlockKind.Table; block.Table = new TableModel();
                block.Table.Widths = table.Element(A + "tblGrid")?.Elements(A + "gridCol").Select(c => { int w; return int.TryParse((string)c.Attribute("w"), out w) ? w : 0; }).ToArray();
                foreach (var row in table.Elements(A + "tr"))
                {
                    var cells = new List<TableModel.Cell>();
                    foreach (var tc in row.Elements(A + "tc"))
                    {
                        int span, rows;
                        var cell = new TableModel.Cell
                        {
                            Span = int.TryParse((string)tc.Attribute("gridSpan"), out span) && span > 1 ? Math.Min(span, 64) : 1,
                            Rows = int.TryParse((string)tc.Attribute("rowSpan"), out rows) && rows > 1 ? Math.Min(rows, 1000) : 1,
                            Continuation = Flag(tc.Attribute("hMerge")) || Flag(tc.Attribute("vMerge"))
                        };
                        var body = tc.Element(A + "txBody");
                        cell.Source = body == null ? new List<SlideParagraph>() : Paragraphs(body, context, false);
                        cells.Add(cell);
                    }
                    block.Table.Rows.Add(cells);
                }
                block.Table.Columns = block.Table.Widths?.Length ?? block.Table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max();
                // A header row draws in bold through the table style; PPT files store that bold in the text itself.
                if (Flag(table.Element(A + "tblPr")?.Attribute("firstRow")) && block.Table.Rows.Count > 0)
                    foreach (var cell in block.Table.Rows[0])
                        foreach (var paragraph in (List<SlideParagraph>)cell.Source)
                            for (var i = 0; i < paragraph.Runs.Count; i++) { var style = paragraph.Runs[i].Style; style.Bold = true; paragraph.Runs[i].Style = style; }
                blocks.Add(block);
                return;
            }
            // SmartArt: the text of its points, in the diagram's own order, as a list.
            var ids = data.Element(Dgm + "relIds");
            var dataId = (string)ids?.Attribute(R + "dm"); OfficePackage.Relation relation;
            if (dataId == null || !context.Relations.TryGetValue(dataId, out relation)) return;
            var entry = context.Archive.GetEntry(relation.Target);
            if (entry == null) return;
            XElement root;
            try { root = DocxHtml.ReadXml(entry, PartLimit, context.Cancellation).Root; } catch (XmlException) { return; }
            block.Kind = SlideBlockKind.Text;
            foreach (var point in root?.Element(Dgm + "ptLst")?.Elements(Dgm + "pt") ?? Enumerable.Empty<XElement>())
            {
                var kind = (string)point.Attribute("type") ?? "node";
                if (kind != "node" && kind != "asst") continue;
                var text = point.Element(Dgm + "t");
                if (text == null) continue;
                foreach (var paragraph in Paragraphs(text, context, false)) { if (paragraph.Marker == null) paragraph.Marker = "•"; block.Paragraphs.Add(paragraph); }
            }
            if (block.Paragraphs.Any(p => p.HasText)) blocks.Add(block);
        }
    }
}
