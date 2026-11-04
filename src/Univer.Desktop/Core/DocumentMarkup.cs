using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace Univer.Desktop.Core
{
    // Formatting shared by the bundled DOCX and DOC renderers, so both file types read the same:
    // one page shell, one paragraph/run vocabulary and one table layout.
    internal struct RunStyle
    {
        public bool Bold, Italic, Underline, Strike, Caps, SmallCaps, Hidden;
        public int Script;          // 1 superscript, 2 subscript
        public int Size;            // half-points; 0 inherits
        public string Color, Highlight; // "#rrggbb" or null
        public bool SameAs(RunStyle other)
        {
            return Bold == other.Bold && Italic == other.Italic && Underline == other.Underline && Strike == other.Strike && Caps == other.Caps &&
                SmallCaps == other.SmallCaps && Script == other.Script && Size == other.Size && Color == other.Color && Highlight == other.Highlight;
        }
    }
    internal struct ParagraphStyle
    {
        public int Level;           // 1-3 heading, 0 body
        public int Align;           // 0 left, 1 center, 2 right, 3 justify
        public int Left, FirstLine; // twips; FirstLine is negative for a hanging indent
        public bool PageBreakBefore;
    }
    internal sealed class TableModel
    {
        public sealed class Cell { public int Span = 1, Rows = 1; public bool Continuation; public object Source; }
        public readonly List<List<Cell>> Rows = new List<List<Cell>>();
        public int[] Widths;        // relative grid widths, or null
        public int Columns;
    }
    internal static class DocumentMarkup
    {
        private const int TwipsPerPixel = 15;
        // How to scroll a wide table, above the document. The text is drawn from an attribute, so find (Ctrl+F), which
        // reads only the document's own text, never matches the hint; screen readers still announce it.
        public static void TableHint(PreviewMarkup html, PreviewOptions options)
        {
            if (string.IsNullOrEmpty(options.TableHint)) return;
            html.Append("<p class=\"table-help\" role=\"note\" data-text=\"").Append(WebUtility.HtmlEncode(options.TableHint)).Append("\"></p>");
        }
        // Writes the opening tags for a run and returns the matching closing tags.
        public static string OpenRun(PreviewMarkup html, RunStyle style, int baseSize, bool heading)
        {
            var close = new StringBuilder();
            Action<string> tag = name => { html.Append('<').Append(name).Append('>'); close.Insert(0, "</" + name + ">"); };
            if (style.Bold) tag("b"); if (style.Italic) tag("i"); if (style.Underline) tag("u"); if (style.Strike) tag("s");
            if (style.Script == 1) tag("sup"); else if (style.Script == 2) tag("sub");
            var css = new StringBuilder();
            if (style.Caps) css.Append("text-transform:uppercase;");
            else if (style.SmallCaps) css.Append("font-variant:small-caps;");
            if (style.Color != null) css.Append("color:").Append(style.Color).Append(';');
            if (style.Highlight != null) css.Append("background:").Append(style.Highlight).Append(';');
            // Sizes are relative to the document's body text: a 14 pt title stays larger than 12 pt text,
            // while every document keeps the viewer's readable base size. Headings keep their own scale.
            if (!heading && style.Size > 0 && baseSize > 0 && style.Script == 0)
            {
                var scale = Math.Max(0.75, Math.Min(2.0, style.Size / (double)baseSize));
                if (Math.Abs(scale - 1) >= 0.05) css.Append("font-size:").Append(scale.ToString("0.##", CultureInfo.InvariantCulture)).Append("em;");
            }
            if (css.Length > 0) { html.Append("<span style=\"").Append(css.ToString().TrimEnd(';')).Append("\">"); close.Insert(0, "</span>"); }
            return close.ToString();
        }
        public static void Paragraph(PreviewMarkup html, ParagraphStyle style, string marker, PreviewMarkup content, bool inCell)
        {
            if (style.PageBreakBefore && !inCell) html.Append("<hr class=\"page\">");
            if (content.Length == 0 && marker == null) { html.Append(inCell ? "" : "<p>&nbsp;</p>"); return; }
            var tag = style.Level > 0 ? "h" + Math.Min(3, style.Level) : "p";
            var css = new StringBuilder();
            if (style.Align == 1) css.Append("text-align:center;"); else if (style.Align == 2) css.Append("text-align:right;"); else if (style.Align == 3) css.Append("text-align:justify;");
            var left = Clamp(style.Left / TwipsPerPixel, 0, 360); var first = Clamp(style.FirstLine / TwipsPerPixel, -left, 240);
            if (left > 0) css.Append("margin-left:").Append(left).Append("px;");
            if (first != 0) css.Append("text-indent:").Append(first).Append("px;");
            html.Append('<').Append(tag);
            if (css.Length > 0) html.Append(" style=\"").Append(css.ToString().TrimEnd(';')).Append('"');
            html.Append('>');
            if (marker != null) html.Append("<span class=\"marker\">").Append(WebUtility.HtmlEncode(marker)).Append("</span>");
            html.Append(content).Append("</").Append(tag).Append('>');
        }
        public static void Table(PreviewMarkup html, TableModel table, int depth, Action<TableModel.Cell> content)
        {
            var widths = table.Widths;
            var knownWidths = widths != null && widths.Length == table.Columns && widths.Length > 0 && Array.TrueForAll(widths, w => w > 0);
            var total = 0; if (knownWidths) foreach (var width in widths) total += width;
            if (depth == 0) html.Append("<div class=\"table-scroll\" tabindex=\"0\">");
            html.Append("<table");
            // Keep text readable in a narrow window; the wrapper holds both scrollbars within
            // the viewport. Nested tables share their outer scroll surface.
            if (depth == 0 && table.Columns > 4)
                html.Append(" style=\"min-width:").Append(Math.Min(4096, knownWidths ? Math.Max(table.Columns * 128, total / 15) : table.Columns * 128)).Append("px\"");
            html.Append('>');
            if (knownWidths)
            {
                html.Append("<colgroup>");
                foreach (var width in widths)
                    html.Append("<col style=\"width:").Append((100.0 * width / total).ToString("0.####", CultureInfo.InvariantCulture)).Append("%\">");
                html.Append("</colgroup>");
            }
            foreach (var row in table.Rows)
            {
                html.Append("<tr>");
                foreach (var cell in row)
                {
                    if (cell.Continuation) continue;
                    html.Append("<td");
                    if (cell.Span > 1) html.Append(" colspan=\"").Append(cell.Span).Append('"');
                    if (cell.Rows > 1) html.Append(" rowspan=\"").Append(cell.Rows).Append('"');
                    html.Append('>');
                    if (cell.Source != null) content(cell);
                    html.Append("</td>");
                }
                html.Append("</tr>");
            }
            html.Append("</table>");
            if (depth == 0) html.Append("</div>");
        }
        // List numbers in Word's formats; unknown formats fall back to decimal.
        public static string Number(int value, string format)
        {
            switch (format)
            {
                case "none": return "";
                case "decimalZero": return value.ToString("00", CultureInfo.InvariantCulture);
                case "upperRoman": return Roman(value);
                case "lowerRoman": return Roman(value).ToLowerInvariant();
                case "upperLetter": return Letters(value, 'A');
                case "lowerLetter": return Letters(value, 'a');
                case "russianUpper": return Letters(value, 'А', "АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩЭЮЯ");
                case "russianLower": return Letters(value, 'а', "абвгдежзиклмнопрстуфхцчшщэюя");
                default: return value.ToString(CultureInfo.InvariantCulture);
            }
        }
        // Symbol and Wingdings bullets live in the private-use area; show a readable equivalent.
        public static string Bullet(string text)
        {
            if (string.IsNullOrEmpty(text)) return "•";
            var result = new StringBuilder();
            foreach (var c in text)
            {
                if (c == '' || c == '') result.Append('▪');
                else if (c == '' || c == '' || c == '') result.Append(c == '' ? '✓' : '➢');
                else if (c == '') result.Append('❖');
                else if (c >= '' && c <= '') result.Append('•');
                else if (c == 'o') result.Append('◦');
                else result.Append(c);
            }
            return result.ToString();
        }
        public static string HighlightColor(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "yellow": return "#ffff00"; case "green": return "#00ff00"; case "cyan": return "#00ffff"; case "magenta": return "#ff00ff";
                case "blue": return "#0000ff"; case "red": return "#ff0000"; case "darkblue": return "#000080"; case "darkcyan": return "#008080";
                case "darkgreen": return "#008000"; case "darkmagenta": return "#800080"; case "darkred": return "#800000"; case "darkyellow": return "#808000";
                case "darkgray": return "#808080"; case "lightgray": return "#c0c0c0"; case "black": return "#000000";
                default: return null;
            }
        }
        // Text colours that would vanish on the white page, or equal the default ink, are not applied.
        public static string TextColor(int r, int g, int b)
        {
            if (0.2126 * r + 0.7152 * g + 0.0722 * b > 225) return null;
            if (r < 40 && g < 40 && b < 40) return null;
            return "#" + r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
        }
        public static string TextColor(string hex)
        {
            int value;
            if (hex == null || hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return null;
            return TextColor(value >> 16 & 255, value >> 8 & 255, value & 255);
        }
        public static string FillColor(string hex)
        {
            int value;
            if (hex == null || hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return null;
            return value == 0xFFFFFF ? null : "#" + hex.ToLowerInvariant();
        }
        private static int Clamp(int value, int low, int high) { return Math.Max(low, Math.Min(high, value)); }
        private static string Roman(int value)
        {
            if (value <= 0 || value >= 4000) return value.ToString(CultureInfo.InvariantCulture);
            var numbers = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var result = new StringBuilder();
            for (var i = 0; i < numbers.Length; i++) while (value >= numbers[i]) { result.Append(symbols[i]); value -= numbers[i]; }
            return result.ToString();
        }
        private static string Letters(int value, char first, string alphabet = null)
        {
            var count = alphabet?.Length ?? 26;
            // Word repeats letters (aa, bbb, …); a damaged start value must not build a string of millions of letters.
            if (value <= 0 || (value - 1) / count >= 50) return value.ToString(CultureInfo.InvariantCulture);
            // Word repeats the letter after the alphabet: a..z, aa..zz.
            var letter = alphabet != null ? alphabet[(value - 1) % count] : (char)(first + (value - 1) % count);
            return new string(letter, (value - 1) / count + 1);
        }
    }
}
