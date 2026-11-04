using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Univer.Desktop.Core
{
    // Bundled read-only DOCX renderer, independent of Office preview handlers: its own markup only (no scripts or remote
    // content), pictures served beside the page, formatting through DocumentMarkup like DOC.
    internal static partial class DocxHtml
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
        private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private const long DocumentLimit = 16L * 1024 * 1024;
        private const int DepthLimit = 128, NodeLimit = 2000000, TextLimit = 2 * 1024 * 1024, StyleDepth = 32;
        private sealed class Style
        {
            public string BasedOn; public XElement Paragraph, Run; public int Level; public bool Resolved;
            public ParagraphStyle ParagraphStyle; public RunStyle RunStyle; public string NumId; public int NumLevel = -1;
        }
        private sealed class Level { public int Start = 1; public string Format = "decimal", Text = "%1."; public XElement Indent; }
        private sealed class Field { public bool Result, Link; public StringBuilder Code = new StringBuilder(); }
        private sealed class Context
        {
            public ZipArchive Archive; public Dictionary<string, string> Relations;
            public Dictionary<string, Style> Styles = new Dictionary<string, Style>(StringComparer.Ordinal);
            public string DefaultParagraph; public RunStyle Defaults; public ParagraphStyle ParagraphDefaults; public int BaseSize = 20;
            public Dictionary<string, Level[]> Abstract = new Dictionary<string, Level[]>(StringComparer.Ordinal);
            public Dictionary<string, string> Nums = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, Dictionary<int, int>> StartOverrides = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
            public Dictionary<string, int?[]> Counters = new Dictionary<string, int?[]>(StringComparer.Ordinal);
            public Stack<Field> Fields = new Stack<Field>();
            public PreviewImages Images = new PreviewImages();
            public PreviewOptions Options;
            public CancellationToken Cancellation;
        }
        public static PreviewDocument Convert(string path, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            using (var archive = ZipFile.OpenRead(path)) return Convert(archive, options, cancellation);
        }
        public static PreviewDocument Convert(ZipArchive archive, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            var main = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("Not a Word document");
            // A preview is for course documents; an oversized body is left to the user's own program.
            if (main.Length > DocumentLimit) throw new PreviewLimitException();
            var document = ReadXml(main, DocumentLimit, cancellation);
            var context = new Context { Archive = archive, Relations = Relations(archive, cancellation), Options = options, Cancellation = cancellation };
            Styles(context);
            Numbering(context);
            var html = new PreviewMarkup(cancellation);
            Start(html, options.Title);
            var body = document.Root?.Element(W + "body");
            if (body != null) context.BaseSize = BodySize(body, context);
            if (body?.Descendants(W + "tbl").Any() == true) DocumentMarkup.TableHint(html, options);
            if (body != null) foreach (var block in body.Elements()) Block(block, html, context, 0, false);
            html.Append("</body></html>");
            return new PreviewDocument(html.ToString(), context.Images.Items);
        }
        // Shared page shell for the bundled renderers (PreviewPage).
        internal static void Start(PreviewMarkup html, string title) { PreviewPage.Start(html, title); }
        // Validates with a streaming reader before constructing or recursively rendering the DOM.
        internal static XDocument ReadXml(ZipArchiveEntry entry, long limit, CancellationToken cancellation)
        {
            if (entry.Length > limit) throw new PreviewLimitException();
            using (var stream = entry.Open()) using (var reader = XmlReader.Create(stream, XmlSettings(limit)))
            {
                var nodes = 0;
                while (reader.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (reader.Depth > DepthLimit || ++nodes > NodeLimit || reader.HasValue && reader.Value.Length > TextLimit)
                        throw new PreviewLimitException();
                }
            }
            cancellation.ThrowIfCancellationRequested();
            using (var stream = entry.Open()) using (var reader = XmlReader.Create(stream, XmlSettings(limit))) return XDocument.Load(reader);
        }
        internal static XmlReaderSettings XmlSettings(long limit)
        { return new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limit, IgnoreWhitespace = false }; }
        private static void Block(XElement block, PreviewMarkup html, Context context, int tableDepth, bool inCell)
        {
            if (block.Name == W + "p") Paragraph(block, html, context, inCell);
            else if (block.Name == W + "tbl") Table(block, html, context, tableDepth);
            else if (block.Name == W + "sdt") foreach (var inner in block.Element(W + "sdtContent")?.Elements() ?? Enumerable.Empty<XElement>()) Block(inner, html, context, tableDepth, inCell);
        }
        private static void Paragraph(XElement paragraph, PreviewMarkup html, Context context, bool inCell)
        {
            var properties = paragraph.Element(W + "pPr");
            var style = ParagraphStyleOf(context, properties?.Element(W + "pStyle")?.Attribute(W + "val")?.Value);
            var format = style?.ParagraphStyle ?? context.ParagraphDefaults;
            var runs = style?.RunStyle ?? context.Defaults;
            string numId = style?.NumId; var numLevel = Math.Max(0, style?.NumLevel ?? 0);
            var numbering = properties?.Element(W + "numPr");
            if (numbering != null)
            {
                numId = numbering.Element(W + "numId")?.Attribute(W + "val")?.Value ?? numId;
                int value; if (int.TryParse(numbering.Element(W + "ilvl")?.Attribute(W + "val")?.Value, out value)) numLevel = Math.Max(0, Math.Min(8, value));
            }
            string marker = null;
            if (numId != null && numId != "0")
            {
                Level level;
                marker = Marker(context, numId, numLevel, out level);
                if (level?.Indent != null) ApplyIndent(ref format, level.Indent);
                else if (level == null && format.Left == 0) format.Left = (numLevel + 1) * 360;
            }
            if (properties != null) ApplyParagraph(ref format, properties, false);
            var content = new PreviewMarkup(html.Cancellation);
            foreach (var node in paragraph.Elements()) Inline(node, content, context, runs, format.Level > 0);
            while (context.Fields.Count > 0 && context.Fields.Peek().Link) { content.Append("</a>"); context.Fields.Peek().Link = false; }
            DocumentMarkup.Paragraph(html, format, marker, content, inCell);
        }
        private static void Inline(XElement node, PreviewMarkup html, Context context, RunStyle paragraphRuns, bool heading)
        {
            if (node.Name == W + "r") Run(node, html, context, paragraphRuns, heading);
            else if (node.Name == W + "hyperlink")
            {
                string target = null; var id = node.Attribute(R + "id")?.Value;
                if (id != null && context.Relations.TryGetValue(id, out target) && Web(target)) html.Append("<a href=\"").Append(WebUtility.HtmlEncode(target)).Append("\">");
                else target = null;
                foreach (var run in node.Elements()) Inline(run, html, context, paragraphRuns, heading);
                if (target != null) html.Append("</a>");
            }
            else if (node.Name == W + "fldSimple")
            {
                var target = Hyperlink(node.Attribute(W + "instr")?.Value);
                if (target != null) html.Append("<a href=\"").Append(WebUtility.HtmlEncode(target)).Append("\">");
                foreach (var inner in node.Elements()) Inline(inner, html, context, paragraphRuns, heading);
                if (target != null) html.Append("</a>");
            }
            else if (node.Name == W + "smartTag" || node.Name == W + "ins" || node.Name == W + "sdt" || node.Name == W + "sdtContent" || node.Name == W + "customXml" || node.Name == W + "moveTo")
                foreach (var inner in node.Elements()) Inline(inner, html, context, paragraphRuns, heading);
        }
        private static void Run(XElement run, PreviewMarkup html, Context context, RunStyle paragraphRuns, bool heading)
        {
            var properties = run.Element(W + "rPr");
            var format = paragraphRuns;
            var characterStyle = properties?.Element(W + "rStyle")?.Attribute(W + "val")?.Value;
            if (characterStyle != null) format = Merge(format, context, characterStyle);
            if (properties != null) ApplyRun(ref format, properties);
            string close = null;
            Action open = () => { if (close == null) close = DocumentMarkup.OpenRun(html, format, context.BaseSize, heading); };
            foreach (var part in run.Elements())
            {
                // Complex fields: hide instruction text, show results, and link HYPERLINK results.
                if (part.Name == W + "fldChar")
                {
                    var kind = part.Attribute(W + "fldCharType")?.Value;
                    if (kind == "begin") context.Fields.Push(new Field());
                    else if (kind == "separate" && context.Fields.Count > 0)
                    {
                        var field = context.Fields.Peek(); field.Result = true;
                        var target = context.Fields.Count == 1 ? Hyperlink(field.Code.ToString()) : null;
                        if (target != null) { if (close != null) { html.Append(close); close = null; } html.Append("<a href=\"").Append(WebUtility.HtmlEncode(target)).Append("\">"); field.Link = true; }
                    }
                    else if (kind == "end" && context.Fields.Count > 0)
                    {
                        var field = context.Fields.Pop();
                        if (field.Link) { if (close != null) { html.Append(close); close = null; } html.Append("</a>"); }
                    }
                    continue;
                }
                if (part.Name == W + "instrText") { if (context.Fields.Count > 0 && !context.Fields.Peek().Result) context.Fields.Peek().Code.Append(part.Value); continue; }
                if (context.Fields.Any(f => !f.Result) || format.Hidden) continue;
                if (part.Name == W + "t") { if (part.Value.Length > 0) { open(); html.Append(WebUtility.HtmlEncode(part.Value)); } }
                else if (part.Name == W + "tab") { open(); html.Append("&emsp;"); }
                else if (part.Name == W + "br")
                {
                    if (part.Attribute(W + "type")?.Value == "page") { if (close != null) { html.Append(close); close = null; } html.Append("<hr class=\"page\">"); }
                    else { open(); html.Append("<br>"); }
                }
                else if (part.Name == W + "cr") { open(); html.Append("<br>"); }
                else if (part.Name == W + "noBreakHyphen") { open(); html.Append("&#8209;"); }
                else if (part.Name == W + "sym")
                {
                    int code;
                    if (int.TryParse(part.Attribute(W + "char")?.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code) && code > 0 && code <= 0xFFFF)
                    { open(); html.Append(WebUtility.HtmlEncode(DocumentMarkup.Bullet(((char)(code < 0x100 ? 0xF000 + code : code)).ToString()))); }
                }
                else if (part.Name == W + "drawing" || part.Name == W + "pict" || part.Name == W + "object" || part.Name == Mc + "AlternateContent")
                {
                    var source = part.Name == Mc + "AlternateContent" ? part.Elements().FirstOrDefault(e => e.Descendants(A + "blip").Any() || e.Descendants(V + "imagedata").Any()) : part;
                    var id = source?.Descendants(A + "blip").Select(b => b.Attribute(R + "embed")?.Value).FirstOrDefault(v => v != null)
                        ?? source?.Descendants(V + "imagedata").Select(b => b.Attribute(R + "id")?.Value).FirstOrDefault(v => v != null);
                    string path;
                    if (id == null || !Image(context, id, out path)) continue;
                    // The drawing's own size (EMU, 9525 per CSS pixel); max-width still fits it to the page.
                    long extent; var width = source.Descendants().FirstOrDefault(e => e.Name.LocalName == "extent" && e.Attribute("cx") != null)?.Attribute("cx")?.Value;
                    var pixels = long.TryParse(width, out extent) ? extent / 9525 : VmlWidth(source.Descendants(V + "shape").Select(e => e.Attribute("style")?.Value).FirstOrDefault(v => v != null));
                    PreviewImages.Write(html, path, pixels, context.Options);
                }
            }
            if (close != null) html.Append(close);
        }
        // Legacy VML pictures carry CSS-like sizes such as "width:36.3pt".
        private static long VmlWidth(string style)
        {
            var match = System.Text.RegularExpressions.Regex.Match(style ?? "", @"(?:^|;)\s*width:\s*([0-9.]+)(pt|px|in|cm|mm)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            double value;
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return 0;
            switch (match.Groups[2].Value.ToLowerInvariant())
            {
                case "pt": return (long)Math.Round(value * 4 / 3); case "in": return (long)Math.Round(value * 96);
                case "cm": return (long)Math.Round(value * 96 / 2.54); case "mm": return (long)Math.Round(value * 96 / 25.4);
                default: return (long)Math.Round(value);
            }
        }
        private static bool Web(string target)
        { return target != null && (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)); }
        // Accepts only http(s) targets from a HYPERLINK field code; local bookmarks and other schemes stay plain text.
        internal static string Hyperlink(string code)
        {
            code = (code ?? "").Trim();
            if (!code.StartsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase)) return null;
            var first = code.IndexOf('"'); var last = first < 0 ? -1 : code.IndexOf('"', first + 1);
            var target = last > first ? code.Substring(first + 1, last - first - 1) : code.Substring(9).Trim().Split(' ')[0];
            return Web(target) ? target : null;
        }
        private static void Styles(Context context)
        {
            context.Defaults = new RunStyle { Size = 20 };
            var entry = context.Archive.GetEntry("word/styles.xml");
            if (entry == null) return;
            XElement root;
            try { root = ReadXml(entry, 4L * 1024 * 1024, context.Cancellation).Root; }
            catch (XmlException) { return; /* Damaged styles only refine formatting; the text still renders. */ }
            if (root == null) return;
            var defaults = root.Element(W + "docDefaults");
            var runDefaults = defaults?.Element(W + "rPrDefault")?.Element(W + "rPr");
            if (runDefaults != null) { var run = context.Defaults; ApplyRun(ref run, runDefaults); context.Defaults = run; }
            var paragraphDefaults = defaults?.Element(W + "pPrDefault")?.Element(W + "pPr");
            if (paragraphDefaults != null) { var paragraph = context.ParagraphDefaults; ApplyParagraph(ref paragraph, paragraphDefaults, true); context.ParagraphDefaults = paragraph; }
            foreach (var element in root.Elements(W + "style"))
            {
                var id = element.Attribute(W + "styleId")?.Value; if (id == null) continue;
                var name = (element.Element(W + "name")?.Attribute(W + "val")?.Value ?? "").ToLowerInvariant();
                int level;
                var style = new Style { BasedOn = element.Element(W + "basedOn")?.Attribute(W + "val")?.Value, Paragraph = element.Element(W + "pPr"), Run = element.Element(W + "rPr") };
                if (name == "title") style.Level = 1;
                else if (name.StartsWith("heading ", StringComparison.Ordinal) && int.TryParse(name.Substring(8), out level) && level >= 1 && level <= 9) style.Level = Math.Min(3, level);
                context.Styles[id] = style;
                if (element.Attribute(W + "type")?.Value == "paragraph" && (element.Attribute(W + "default")?.Value == "1" || element.Attribute(W + "default")?.Value == "true")) context.DefaultParagraph = id;
            }
            var normal = ParagraphStyleOf(context, null);
            context.BaseSize = normal != null && normal.RunStyle.Size > 0 ? normal.RunStyle.Size : context.Defaults.Size > 0 ? context.Defaults.Size : 20;
        }
        // The most common text size becomes 1em, so body text keeps the viewer's readable size in every document.
        private static int BodySize(XElement body, Context context)
        {
            var counts = new Dictionary<int, int>();
            foreach (var paragraph in body.Descendants(W + "p"))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var runs = ParagraphStyleOf(context, paragraph.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value)?.RunStyle ?? context.Defaults;
                foreach (var run in paragraph.Descendants(W + "r"))
                {
                    var length = 0; foreach (var text in run.Elements(W + "t")) length += text.Value.Trim().Length;
                    if (length == 0) continue;
                    var format = runs; var properties = run.Element(W + "rPr");
                    var character = properties?.Element(W + "rStyle")?.Attribute(W + "val")?.Value;
                    if (character != null) format = Merge(format, context, character);
                    if (properties != null) ApplyRun(ref format, properties);
                    int count; counts.TryGetValue(format.Size, out count); counts[format.Size] = count + length;
                }
            }
            var best = context.BaseSize; var most = 0;
            foreach (var pair in counts) if (pair.Key > 0 && pair.Value > most) { best = pair.Key; most = pair.Value; }
            return best;
        }
        private static Style ParagraphStyleOf(Context context, string id)
        {
            Style style;
            if (id != null && !context.Styles.ContainsKey(id))
            {
                // Documents without styles.xml still name the built-in heading styles.
                int level;
                if (id == "Title" || id.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(id.Substring(7), out level) && level >= 1 && level <= 9)
                    context.Styles[id] = new Style { Level = id == "Title" ? 1 : Math.Min(3, int.Parse(id.Substring(7), CultureInfo.InvariantCulture)) };
            }
            if (id == null || !context.Styles.TryGetValue(id, out style)) { id = context.DefaultParagraph; if (id == null || !context.Styles.TryGetValue(id, out style)) return null; }
            Resolve(context, style, 0);
            return style;
        }
        private static void Resolve(Context context, Style style, int depth)
        {
            if (style.Resolved) return;
            style.Resolved = true; // Also stops basedOn cycles.
            Style parent = null;
            if (depth < StyleDepth && style.BasedOn != null && context.Styles.TryGetValue(style.BasedOn, out parent)) Resolve(context, parent, depth + 1);
            var run = parent?.RunStyle ?? context.Defaults; var paragraph = parent?.ParagraphStyle ?? context.ParagraphDefaults;
            if (parent != null) { style.NumId = parent.NumId; style.NumLevel = parent.NumLevel; if (style.Level == 0) style.Level = parent.Level; }
            if (style.Run != null) ApplyRun(ref run, style.Run);
            if (style.Paragraph != null)
            {
                ApplyParagraph(ref paragraph, style.Paragraph, true);
                var numbering = style.Paragraph.Element(W + "numPr");
                if (numbering != null)
                {
                    style.NumId = numbering.Element(W + "numId")?.Attribute(W + "val")?.Value ?? style.NumId;
                    int value; if (int.TryParse(numbering.Element(W + "ilvl")?.Attribute(W + "val")?.Value, out value)) style.NumLevel = Math.Max(0, Math.Min(8, value));
                }
            }
            if (style.Level > 0) paragraph.Level = style.Level;
            style.RunStyle = run; style.ParagraphStyle = paragraph;
        }
        // A character style replaces the inherited values that it sets itself.
        private static RunStyle Merge(RunStyle format, Context context, string id)
        {
            Style style; var chain = new List<XElement>();
            for (var depth = 0; id != null && depth < StyleDepth && context.Styles.TryGetValue(id, out style); depth++) { if (style.Run != null) chain.Insert(0, style.Run); id = style.BasedOn; }
            foreach (var run in chain) ApplyRun(ref format, run);
            return format;
        }
        private static void ApplyParagraph(ref ParagraphStyle format, XElement properties, bool style)
        {
            var align = Val(properties.Element(W + "jc"));
            if (align != null) format.Align = align == "center" ? 1 : align == "right" || align == "end" ? 2 : align == "both" || align == "distribute" ? 3 : 0;
            var indent = properties.Element(W + "ind");
            if (indent != null) ApplyIndent(ref format, indent);
            var outline = Val(properties.Element(W + "outlineLvl")); int level;
            if (style && outline != null && int.TryParse(outline, out level) && level >= 0 && level <= 2) format.Level = level + 1;
            var pageBreak = properties.Element(W + "pageBreakBefore");
            if (pageBreak != null) format.PageBreakBefore = On(pageBreak);
        }
        private static void ApplyIndent(ref ParagraphStyle format, XElement indent)
        {
            int value;
            if (int.TryParse(indent.Attribute(W + "left")?.Value ?? indent.Attribute(W + "start")?.Value, out value)) format.Left = value;
            if (int.TryParse(indent.Attribute(W + "hanging")?.Value, out value)) format.FirstLine = -value;
            else if (int.TryParse(indent.Attribute(W + "firstLine")?.Value, out value)) format.FirstLine = value;
        }
        private static void ApplyRun(ref RunStyle format, XElement properties)
        {
            Toggle(properties, "b", ref format.Bold); Toggle(properties, "i", ref format.Italic);
            Toggle(properties, "caps", ref format.Caps); Toggle(properties, "smallCaps", ref format.SmallCaps); Toggle(properties, "vanish", ref format.Hidden);
            var strike = properties.Element(W + "strike") ?? properties.Element(W + "dstrike"); if (strike != null) format.Strike = On(strike);
            var underline = properties.Element(W + "u"); if (underline != null) format.Underline = Val(underline) != "none";
            var vertical = Val(properties.Element(W + "vertAlign"));
            if (vertical != null) format.Script = vertical == "superscript" ? 1 : vertical == "subscript" ? 2 : 0;
            int size; if (int.TryParse(Val(properties.Element(W + "sz")), out size) && size > 0 && size < 3200) format.Size = size;
            var color = properties.Element(W + "color"); if (color != null) format.Color = DocumentMarkup.TextColor(Val(color));
            var highlight = properties.Element(W + "highlight");
            if (highlight != null) format.Highlight = DocumentMarkup.HighlightColor(Val(highlight));
            else { var shading = properties.Element(W + "shd"); if (shading != null) format.Highlight = DocumentMarkup.FillColor(shading.Attribute(W + "fill")?.Value); }
        }
        private static void Toggle(XElement properties, string name, ref bool value) { var element = properties.Element(W + name); if (element != null) value = On(element); }
        private static bool On(XElement element) { var value = Val(element); return value != "0" && value != "false" && value != "off" && value != "none"; }
        private static string Val(XElement element) { return element?.Attribute(W + "val")?.Value; }
        private static void Numbering(Context context)
        {
            var entry = context.Archive.GetEntry("word/numbering.xml");
            if (entry == null) return;
            XElement root;
            try { root = ReadXml(entry, 4L * 1024 * 1024, context.Cancellation).Root; }
            catch (XmlException) { return; }
            if (root == null) return;
            foreach (var definition in root.Elements(W + "abstractNum"))
            {
                var id = definition.Attribute(W + "abstractNumId")?.Value; if (id == null) continue;
                var levels = new Level[9];
                foreach (var element in definition.Elements(W + "lvl"))
                {
                    int index; if (!int.TryParse(element.Attribute(W + "ilvl")?.Value, out index) || index < 0 || index > 8) continue;
                    int start;
                    levels[index] = new Level
                    {
                        Start = int.TryParse(Val(element.Element(W + "start")), out start) ? start : 1,
                        Format = Val(element.Element(W + "numFmt")) ?? "decimal",
                        Text = Val(element.Element(W + "lvlText")) ?? "",
                        Indent = element.Element(W + "pPr")?.Element(W + "ind")
                    };
                }
                context.Abstract[id] = levels;
            }
            foreach (var num in root.Elements(W + "num"))
            {
                var id = num.Attribute(W + "numId")?.Value; var definition = Val(num.Element(W + "abstractNumId"));
                if (id == null || definition == null) continue;
                context.Nums[id] = definition;
                foreach (var change in num.Elements(W + "lvlOverride"))
                {
                    int index, start;
                    if (int.TryParse(change.Attribute(W + "ilvl")?.Value, out index) && int.TryParse(Val(change.Element(W + "startOverride")), out start))
                    {
                        Dictionary<int, int> starts; if (!context.StartOverrides.TryGetValue(id, out starts)) context.StartOverrides[id] = starts = new Dictionary<int, int>();
                        starts[index] = start;
                    }
                }
            }
        }
        private static string Marker(Context context, string numId, int index, out Level level)
        {
            level = null; string definition; Level[] levels;
            if (!context.Nums.TryGetValue(numId, out definition) || !context.Abstract.TryGetValue(definition, out levels)) return "•";
            level = levels[index];
            if (level == null) return "•";
            if (level.Format == "bullet") return DocumentMarkup.Bullet(level.Text);
            int?[] counters; if (!context.Counters.TryGetValue(numId, out counters)) context.Counters[numId] = counters = new int?[9];
            Func<int, int> start = i => { Dictionary<int, int> starts; int value; return context.StartOverrides.TryGetValue(numId, out starts) && starts.TryGetValue(i, out value) ? value : levels[i]?.Start ?? 1; };
            counters[index] = counters[index].HasValue ? counters[index] + 1 : start(index);
            for (var i = index + 1; i < 9; i++) counters[i] = null;
            var text = new StringBuilder(level.Text);
            for (var i = 0; i < 9; i++)
            {
                var placeholder = "%" + (i + 1);
                if (text.ToString().IndexOf(placeholder, StringComparison.Ordinal) < 0) continue;
                var format = i == index ? level.Format : levels[i]?.Format ?? "decimal";
                text.Replace(placeholder, DocumentMarkup.Number(counters[i] ?? start(i), format));
            }
            return text.Length == 0 ? null : text.ToString();
        }
        // Adds a picture part the document links to. Returns false for links that are not pictures this viewer shows
        // (external, missing, or another format); path is null when the picture is larger than the viewer takes.
        private static bool Image(Context context, string id, out string path)
        {
            string target; path = null;
            if (!context.Relations.TryGetValue(id, out target) || target.Contains("://")) return false;
            var entry = context.Archive.GetEntry("word/" + target.TrimStart('/').Replace("../", "")) ?? context.Archive.GetEntry(target.TrimStart('/'));
            if (entry == null) return false;
            var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
            var mime = extension == ".png" ? "image/png" : extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : extension == ".gif" ? "image/gif" : extension == ".bmp" ? "image/bmp" : null;
            if (mime == null) return false;
            path = context.Images.Find(entry.FullName);
            if (path != null || !context.Images.Fits(entry.Length)) return true;
            context.Cancellation.ThrowIfCancellationRequested();
            // The header's size can understate the data: .NET Framework inflates entries without checking it.
            using (var stream = entry.Open()) using (var memory = new MemoryStream())
            {
                var buffer = new byte[81920]; int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    if (!context.Images.Fits(memory.Length + read)) return true;
                    memory.Write(buffer, 0, read);
                }
                path = context.Images.Add(entry.FullName, memory.ToArray(), mime);
                return true;
            }
        }
        private static Dictionary<string, string> Relations(ZipArchive archive, CancellationToken cancellation)
        {
            var result = new Dictionary<string, string>();
            var entry = archive.GetEntry("word/_rels/document.xml.rels");
            if (entry == null) return result;
            foreach (var relation in ReadXml(entry, 4L * 1024 * 1024, cancellation).Root?.Elements(Rel + "Relationship") ?? Enumerable.Empty<XElement>())
            {
                var id = relation.Attribute("Id")?.Value; var target = relation.Attribute("Target")?.Value;
                if (id != null && target != null) result[id] = target;
            }
            return result;
        }
    }
}
