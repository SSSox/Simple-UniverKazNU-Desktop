using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace Univer.Desktop.Core
{
    internal sealed class SlideRun { public string Text; public RunStyle Style; public string Link; public bool Break; }
    internal sealed class SlideParagraph
    {
        public readonly List<SlideRun> Runs = new List<SlideRun>();
        public int Level, Align;        // Align: 0 left, 1 center, 2 right, 3 justify
        public string Marker;           // bullet or number, null for none
        public bool HasText { get { return Runs.Any(r => !r.Break && !string.IsNullOrWhiteSpace(r.Text)); } }
    }
    internal enum SlideBlockKind { Title, Text, Picture, Table, Group }
    internal sealed class SlideBlock
    {
        public SlideBlockKind Kind;
        public double Top, Left;        // in the file's own units; only the order matters
        public double Bottom, Right;    // PPT shapes only: a table there is a group of cell shapes on a grid
        public List<SlideParagraph> Paragraphs = new List<SlideParagraph>();
        public string Image;            // picture address, or null when it was too large (Picture)
        public double Width;            // share of the slide's width (Picture), 0 when unknown
        public TableModel Table;        // cells carry List<SlideParagraph> as Source
        public List<SlideBlock> Children = new List<SlideBlock>();
    }
    internal sealed class Slide { public readonly List<SlideBlock> Blocks = new List<SlideBlock>(); }
    internal sealed class Deck
    {
        public readonly List<Slide> Slides = new List<Slide>();
        public readonly PreviewImages Images = new PreviewImages();
    }
    // Both presentation formats in one look: a card per slide with its number, title, text, lists, tables and pictures in
    // reading order. Backgrounds, animations, notes and charts stay with the user's own program.
    internal static class SlideMarkup
    {
        public const int SlideLimit = 2000;
        public static PreviewDocument Write(Deck deck, PreviewOptions options, System.Threading.CancellationToken cancellation)
        {
            var html = new PreviewMarkup(cancellation);
            PreviewPage.Start(html, options.Title, "deck");
            var baseSize = BaseSize(deck);
            for (var i = 0; i < deck.Slides.Count; i++)
            {
                html.Append("<section class=\"slide\" id=\"slide-").Append(i + 1).Append("\" aria-label=\"").Append(WebUtility.HtmlEncode(Label(options, i, deck.Slides.Count))).Append("\">")
                    .Append("<p class=\"slide-no\" data-text=\"").Append(WebUtility.HtmlEncode(Label(options, i, deck.Slides.Count))).Append("\"></p>");
                var blocks = Order(deck.Slides[i].Blocks);
                var shown = 0;
                foreach (var block in blocks) shown += Block(html, block, baseSize, options);
                if (shown == 0) html.Append("<p class=\"slide-empty\" data-text=\"").Append(WebUtility.HtmlEncode(options.Label("slideEmpty"))).Append("\"></p>");
                html.Append("</section>");
            }
            if (deck.Slides.Count == 0) html.Append("<section class=\"slide\"><p class=\"slide-empty\" data-text=\"").Append(WebUtility.HtmlEncode(options.Label("slideEmpty"))).Append("\"></p></section>");
            html.Append("</body></html>");
            return new PreviewDocument(html.ToString(), deck.Images.Items);
        }
        private static string Label(PreviewOptions options, int index, int count)
        {
            var format = options.Label("slide");
            return string.IsNullOrEmpty(format) ? (index + 1).ToString(CultureInfo.InvariantCulture) : string.Format(CultureInfo.InvariantCulture, format, index + 1, count);
        }
        // Titles first, then everything else from top to bottom and left to right; groups keep their own order inside.
        private static List<SlideBlock> Order(List<SlideBlock> blocks)
        {
            var result = blocks.Where(b => b.Kind == SlideBlockKind.Title).OrderBy(b => b.Top).ThenBy(b => b.Left).ToList();
            result.AddRange(blocks.Where(b => b.Kind != SlideBlockKind.Title).OrderBy(b => b.Top).ThenBy(b => b.Left));
            return result;
        }
        // Returns how many visible items it wrote.
        private static int Block(PreviewMarkup html, SlideBlock block, int baseSize, PreviewOptions options)
        {
            switch (block.Kind)
            {
                case SlideBlockKind.Title:
                    var lines = block.Paragraphs.Where(p => p.HasText).ToList();
                    if (lines.Count == 0) return 0;
                    html.Append("<h2>");
                    for (var i = 0; i < lines.Count; i++) { if (i > 0) html.Append("<br>"); Runs(html, lines[i], baseSize, true); }
                    html.Append("</h2>");
                    return 1;
                case SlideBlockKind.Text:
                    var count = 0;
                    foreach (var paragraph in block.Paragraphs) if (Paragraph(html, paragraph, baseSize, false)) count++;
                    return count;
                case SlideBlockKind.Picture:
                    html.Append("<figure>");
                    if (block.Image == null) PreviewImages.Write(html, null, 0, options);
                    else
                    {
                        html.Append("<img alt=\"\" src=\"").Append(block.Image).Append('"');
                        if (block.Width > 0.02 && block.Width <= 1.5) html.Append(" style=\"width:").Append(Math.Min(100, block.Width * 100).ToString("0.#", CultureInfo.InvariantCulture)).Append("%\"");
                        html.Append('>');
                    }
                    html.Append("</figure>");
                    return 1;
                case SlideBlockKind.Table:
                    if (block.Table == null || block.Table.Rows.Count == 0) return 0;
                    DocumentMarkup.Table(html, block.Table, 0, cell =>
                    {
                        foreach (var paragraph in (List<SlideParagraph>)cell.Source) Paragraph(html, paragraph, baseSize, true);
                    });
                    return 1;
                default:
                    var written = 0;
                    foreach (var child in Order(block.Children)) written += Block(html, child, baseSize, options);
                    return written;
            }
        }
        private static bool Paragraph(PreviewMarkup html, SlideParagraph paragraph, int baseSize, bool inCell)
        {
            if (!paragraph.HasText) return false;
            var content = new PreviewMarkup(html.Cancellation);
            Runs(content, paragraph, baseSize, false);
            var style = new ParagraphStyle { Align = paragraph.Align, Left = paragraph.Marker != null || paragraph.Level > 0 ? (paragraph.Level + (paragraph.Marker != null ? 1 : 0)) * 360 : 0 };
            if (paragraph.Marker != null) style.FirstLine = -360;
            DocumentMarkup.Paragraph(html, style, paragraph.Marker, content, inCell);
            return true;
        }
        private static void Runs(PreviewMarkup html, SlideParagraph paragraph, int baseSize, bool heading)
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.Break) { html.Append("<br>"); continue; }
                if (string.IsNullOrEmpty(run.Text)) continue;
                if (run.Link != null) html.Append("<a href=\"").Append(WebUtility.HtmlEncode(run.Link)).Append("\">");
                var close = DocumentMarkup.OpenRun(html, run.Style, baseSize, heading);
                html.Append(WebUtility.HtmlEncode(run.Text)).Append(close);
                if (run.Link != null) html.Append("</a>");
            }
        }
        // The most common text size (outside titles) becomes the page's base size, like body text in documents.
        private static int BaseSize(Deck deck)
        {
            var counts = new Dictionary<int, int>();
            Action<IEnumerable<SlideParagraph>> count = null;
            Action<SlideBlock> visit = null;
            count = paragraphs =>
            {
                foreach (var paragraph in paragraphs)
                    foreach (var run in paragraph.Runs)
                        if (!run.Break && run.Style.Size > 0 && !string.IsNullOrEmpty(run.Text)) { int value; counts.TryGetValue(run.Style.Size, out value); counts[run.Style.Size] = value + run.Text.Length; }
            };
            visit = block =>
            {
                if (block.Kind == SlideBlockKind.Text) count(block.Paragraphs);
                else if (block.Kind == SlideBlockKind.Table && block.Table != null) foreach (var row in block.Table.Rows) foreach (var cell in row) if (cell.Source != null) count((List<SlideParagraph>)cell.Source);
                foreach (var child in block.Children) visit(child);
            };
            foreach (var slide in deck.Slides) foreach (var block in slide.Blocks) visit(block);
            var best = 36; var most = 0;
            foreach (var pair in counts) if (pair.Value > most) { best = pair.Key; most = pair.Value; }
            return best;
        }
        // Slide text is shown on the page's own card, without the slide's background: colours that would be faint on
        // white (white or pale text written for a dark slide) fall back to the page's ink.
        public static string Readable(string hex)
        {
            int value;
            if (hex == null || hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return null;
            int r = value >> 16 & 255, g = value >> 8 & 255, b = value & 255;
            if (PreviewTheme.Contrast(PreviewTheme.Luminance(r, g, b), 1) < 3) return null;
            return DocumentMarkup.TextColor(r, g, b);
        }
        // Automatic numbers of PowerPoint lists: arabicPeriod, alphaLcParenR, romanUcPeriod and the like.
        public static string AutoNumber(string scheme, int value)
        {
            scheme = scheme ?? "arabicPeriod";
            string number;
            if (scheme.StartsWith("alphaLc", StringComparison.Ordinal)) number = DocumentMarkup.Number(value, "lowerLetter");
            else if (scheme.StartsWith("alphaUc", StringComparison.Ordinal)) number = DocumentMarkup.Number(value, "upperLetter");
            else if (scheme.StartsWith("romanLc", StringComparison.Ordinal)) number = DocumentMarkup.Number(value, "lowerRoman");
            else if (scheme.StartsWith("romanUc", StringComparison.Ordinal)) number = DocumentMarkup.Number(value, "upperRoman");
            else number = value.ToString(CultureInfo.InvariantCulture);
            if (scheme.EndsWith("ParenBoth", StringComparison.Ordinal)) return "(" + number + ")";
            if (scheme.EndsWith("ParenR", StringComparison.Ordinal)) return number + ")";
            if (scheme.EndsWith("Plain", StringComparison.Ordinal)) return number;
            return number + ".";
        }
    }
}
