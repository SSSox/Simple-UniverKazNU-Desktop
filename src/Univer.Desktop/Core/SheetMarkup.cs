using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace Univer.Desktop.Core
{
    // One cell format of a workbook, as both readers (XLS and XLSX) describe it.
    internal sealed class SheetStyle
    {
        public bool Bold, Italic, Underline, Strike;
        public string Color, Fill;  // "rrggbb" or null
        public int Align;           // 0 general, 1 left, 2 center, 3 right, 4 justify
        public bool Wrap;
        public string Format;       // Excel number format code
        public bool SameLook(SheetStyle other)
        {
            return Bold == other.Bold && Italic == other.Italic && Underline == other.Underline && Strike == other.Strike &&
                Color == other.Color && Fill == other.Fill && Align == other.Align && Wrap == other.Wrap;
        }
    }
    internal enum CellKind { Text, Number, Logical, Error }
    internal sealed class SheetCell
    {
        public string Text;
        public int Style;
        public CellKind Kind;
    }
    internal sealed class SheetRange { public int Top, Left, Bottom, Right; }
    internal sealed class SheetData
    {
        public const int ColumnCount = 16384;
        public string Name;
        public readonly Dictionary<long, SheetCell> Cells = new Dictionary<long, SheetCell>();
        public readonly List<SheetRange> Merges = new List<SheetRange>();
        public readonly Dictionary<int, double> Widths = new Dictionary<int, double>();   // in characters
        public readonly HashSet<int> HiddenRows = new HashSet<int>(), HiddenColumns = new HashSet<int>();
        public static long Key(int row, int column) { return (long)row * ColumnCount + column; }
        public SheetCell Cell(int row, int column) { SheetCell cell; return Cells.TryGetValue(Key(row, column), out cell) ? cell : null; }
    }
    // Workbooks of both formats in one look: a list of sheets, then each sheet as a grid with column letters and row
    // numbers, merged cells, column widths, fonts, fills and alignment. Formulas show their saved results.
    internal sealed class Workbook
    {
        public readonly List<SheetData> Sheets = new List<SheetData>();
        public readonly List<SheetStyle> Styles = new List<SheetStyle>();
    }
    internal static class SheetMarkup
    {
        // A preview shows the part of a sheet a person reads; the whole sheet stays with the user's own program.
        public const int RowLimit = 5000, ColumnLimit = 256, CellLimit = 250000;
        // Readers stop storing cells past this many, so a huge sheet cannot exhaust memory before it is cut.
        public const int StoredCellLimit = 2000000;
        public static PreviewDocument Write(Workbook book, PreviewOptions options, System.Threading.CancellationToken cancellation)
        {
            var css = new StringBuilder(); var used = new Dictionary<int, string>();
            var body = new PreviewMarkup(cancellation);
            var visible = book.Sheets;
            if (visible.Count > 1)
            {
                body.Append("<nav><ul class=\"sheets\" aria-label=\"").Append(WebUtility.HtmlEncode(options.Label("sheets"))).Append("\">");
                for (var i = 0; i < visible.Count; i++)
                    body.Append("<li><a href=\"#sheet-").Append(i + 1).Append("\" data-text=\"").Append(WebUtility.HtmlEncode(visible[i].Name)).Append("\" aria-label=\"").Append(WebUtility.HtmlEncode(visible[i].Name)).Append("\"></a></li>");
                body.Append("</ul></nav>");
            }
            var budget = CellLimit;
            for (var i = 0; i < visible.Count; i++) budget -= Sheet(body, book, visible[i], i + 1, options, used, css, budget);
            var html = new PreviewMarkup(cancellation);
            PreviewPage.Start(html, options.Title, null, css.ToString());
            if (visible.Count == 0) html.Append("<p class=\"note\" data-text=\"").Append(WebUtility.HtmlEncode(options.Label("sheetEmpty"))).Append("\"></p>");
            html.Append(body).Append("</body></html>");
            return new PreviewDocument(html.ToString(), new List<PreviewImage>());
        }
        // Returns the number of cells written.
        private static int Sheet(PreviewMarkup html, Workbook book, SheetData sheet, int number, PreviewOptions options, Dictionary<int, string> used, StringBuilder css, int budget)
        {
            html.Append("<section class=\"sheet\" id=\"sheet-").Append(number).Append("\"><h2>").Append(WebUtility.HtmlEncode(sheet.Name)).Append("</h2>");
            // The used area: cells with text or a fill, and merged areas that start in it. Formatting applied to whole
            // columns or rows does not stretch the grid to the end of the sheet.
            int lastRow = -1, lastColumn = -1;
            foreach (var pair in sheet.Cells)
            {
                var cell = pair.Value;
                if (string.IsNullOrEmpty(cell.Text) && Fill(book, cell.Style) == null) continue;
                var row = (int)(pair.Key / SheetData.ColumnCount); var column = (int)(pair.Key % SheetData.ColumnCount);
                if (sheet.HiddenRows.Contains(row) || sheet.HiddenColumns.Contains(column)) continue;
                lastRow = Math.Max(lastRow, row); lastColumn = Math.Max(lastColumn, column);
            }
            if (lastRow < 0)
            {
                html.Append("<p class=\"note\" data-text=\"").Append(WebUtility.HtmlEncode(options.Label("sheetEmpty"))).Append("\"></p></section>");
                return 0;
            }
            foreach (var merge in sheet.Merges)
                if (merge.Top <= lastRow && merge.Left <= lastColumn) { lastRow = Math.Max(lastRow, merge.Bottom); lastColumn = Math.Max(lastColumn, merge.Right); }
            var rows = new List<int>(); var columns = new List<int>();
            for (var c = 0; c <= lastColumn && columns.Count < ColumnLimit; c++) if (!sheet.HiddenColumns.Contains(c)) columns.Add(c);
            var perRow = Math.Max(1, columns.Count);
            var rowLimit = Math.Max(1, Math.Min(RowLimit, budget / perRow));
            for (var r = 0; r <= lastRow && rows.Count < rowLimit; r++) if (!sheet.HiddenRows.Contains(r)) rows.Add(r);
            var cut = rows.Count > 0 && rows[rows.Count - 1] < lastRow && HasVisibleAfter(sheet, rows[rows.Count - 1], lastRow) ||
                columns.Count > 0 && columns[columns.Count - 1] < lastColumn && HasVisibleColumnAfter(sheet, columns[columns.Count - 1], lastColumn);
            if (cut)
                html.Append("<p class=\"note\" data-text=\"").Append(WebUtility.HtmlEncode(string.Format(CultureInfo.InvariantCulture, options.Label("sheetTruncated"), rows.Count, columns.Count))).Append("\"></p>");
            // Merged areas over the shown rows and columns: the first shown cell carries the spans, the rest are skipped.
            var rowIndex = new Dictionary<int, int>(); for (var i = 0; i < rows.Count; i++) rowIndex[rows[i]] = i;
            var columnIndex = new Dictionary<int, int>(); for (var i = 0; i < columns.Count; i++) columnIndex[columns[i]] = i;
            var covered = new HashSet<long>(); var spans = new Dictionary<long, int[]>();
            foreach (var merge in sheet.Merges)
            {
                int top = -1, bottom = -1, left = -1, right = -1;
                for (var r = merge.Top; r <= merge.Bottom && r <= lastRow; r++) { int index; if (rowIndex.TryGetValue(r, out index)) { if (top < 0) top = index; bottom = index; } }
                for (var c = merge.Left; c <= merge.Right && c <= lastColumn; c++) { int index; if (columnIndex.TryGetValue(c, out index)) { if (left < 0) left = index; right = index; } }
                if (top < 0 || left < 0 || top == bottom && left == right) continue;
                var anchor = SheetData.Key(rows[top], columns[left]);
                if (covered.Contains(anchor) || spans.ContainsKey(anchor)) continue;
                spans[anchor] = new[] { bottom - top + 1, right - left + 1, merge.Top, merge.Left };
                for (var r = top; r <= bottom; r++) for (var c = left; c <= right; c++) if (r != top || c != left) covered.Add(SheetData.Key(rows[r], columns[c]));
            }
            html.Append("<div class=\"table-scroll\" tabindex=\"0\"><table class=\"grid\"><colgroup><col>");
            foreach (var column in columns)
            {
                double width; if (!sheet.Widths.TryGetValue(column, out width) || width <= 0) width = 8.43;
                html.Append("<col style=\"width:").Append((int)Math.Round(Math.Min(120, width) * 7 + 5)).Append("px\">");
            }
            html.Append("</colgroup><thead><tr><th data-text=\"\"></th>");
            foreach (var column in columns) html.Append("<th scope=\"col\" data-text=\"").Append(Letters(column)).Append("\"></th>");
            html.Append("</tr></thead><tbody>");
            var written = 0;
            foreach (var row in rows)
            {
                html.Cancellation.ThrowIfCancellationRequested();
                html.Append("<tr><th scope=\"row\" data-text=\"").Append(row + 1).Append("\"></th>");
                foreach (var column in columns)
                {
                    var key = SheetData.Key(row, column);
                    if (covered.Contains(key)) continue;
                    int[] span; spans.TryGetValue(key, out span);
                    // A merged area shows the value of its top-left cell even when that cell is hidden.
                    var cell = span != null ? sheet.Cell(span[2], span[3]) ?? sheet.Cell(row, column) : sheet.Cell(row, column);
                    html.Append("<td");
                    if (span != null && span[1] > 1) html.Append(" colspan=\"").Append(span[1]).Append('"');
                    if (span != null && span[0] > 1) html.Append(" rowspan=\"").Append(span[0]).Append('"');
                    var classes = Classes(book, cell, used, css);
                    if (classes.Length > 0) html.Append(" class=\"").Append(classes).Append('"');
                    html.Append('>');
                    if (cell != null && !string.IsNullOrEmpty(cell.Text)) html.Append(WebUtility.HtmlEncode(cell.Text));
                    html.Append("</td>");
                    written++;
                }
                html.Append("</tr>");
            }
            html.Append("</tbody></table></div></section>");
            return written;
        }
        private static bool HasVisibleAfter(SheetData sheet, int shown, int last)
        { for (var r = shown + 1; r <= last; r++) if (!sheet.HiddenRows.Contains(r)) return true; return false; }
        private static bool HasVisibleColumnAfter(SheetData sheet, int shown, int last)
        { for (var c = shown + 1; c <= last; c++) if (!sheet.HiddenColumns.Contains(c)) return true; return false; }
        private static string Fill(Workbook book, int style) { return style >= 0 && style < book.Styles.Count ? book.Styles[style].Fill : null; }
        // The cell's alignment class (numbers right, logical values and errors centred, as Excel's general alignment) and
        // a class for its format; formats that look alike share one class.
        private static string Classes(Workbook book, SheetCell cell, Dictionary<int, string> used, StringBuilder css)
        {
            var style = cell != null && cell.Style >= 0 && cell.Style < book.Styles.Count ? book.Styles[cell.Style] : null;
            var align = style?.Align ?? 0;
            var result = new StringBuilder();
            if (align == 3 || align == 0 && cell != null && cell.Kind == CellKind.Number) result.Append('n');
            else if (align == 2 || align == 0 && cell != null && (cell.Kind == CellKind.Logical || cell.Kind == CellKind.Error)) result.Append('c');
            if (style == null) return result.ToString();
            if (style.Wrap || align == 4) result.Append(result.Length > 0 ? " w" : "w");
            string name;
            if (!used.TryGetValue(cell.Style, out name))
            {
                name = null;
                foreach (var pair in used)
                    if (pair.Value != null && book.Styles[pair.Key].SameLook(style)) { name = pair.Value; break; }
                if (name == null)
                {
                    var rule = new StringBuilder();
                    if (style.Bold) rule.Append("font-weight:700;"); if (style.Italic) rule.Append("font-style:italic;");
                    if (style.Underline || style.Strike) rule.Append("text-decoration:").Append(style.Underline ? "underline " : "").Append(style.Strike ? "line-through" : "").Append(';');
                    if (style.Color != null) rule.Append("color:#").Append(style.Color).Append(';');
                    if (style.Fill != null) rule.Append("background:#").Append(style.Fill).Append(';');
                    if (align == 1) rule.Append("text-align:left;"); else if (align == 4) rule.Append("text-align:justify;");
                    if (rule.Length > 0)
                    {
                        name = "s" + used.Count;
                        css.Append(".grid .").Append(name).Append('{').Append(rule.ToString().TrimEnd(';')).Append('}');
                    }
                }
                used[cell.Style] = name;
            }
            if (name != null) result.Append(result.Length > 0 ? " " : "").Append(name);
            return result.ToString();
        }
        // 0 → A, 25 → Z, 26 → AA.
        public static string Letters(int column)
        {
            var text = ""; column++;
            while (column > 0) { var rest = (column - 1) % 26; text = (char)('A' + rest) + text; column = (column - 1) / 26; }
            return text;
        }
        // "B12" → row 11, column 1. False for anything else.
        public static bool Reference(string value, out int row, out int column)
        {
            row = column = -1;
            if (string.IsNullOrEmpty(value)) return false;
            var i = 0; var c = 0;
            while (i < value.Length && char.IsLetter(value[i])) { var letter = char.ToUpperInvariant(value[i]); if (letter < 'A' || letter > 'Z') return false; c = c * 26 + (letter - 'A' + 1); i++; if (c > SheetData.ColumnCount) return false; }
            int r;
            if (i == 0 || i == value.Length || !int.TryParse(value.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out r) || r < 1 || r > 1048576) return false;
            row = r - 1; column = c - 1; return true;
        }
        // The value of a logical cell and the text of error codes, as Excel shows them in this language.
        public static string Logical(bool value, PreviewOptions options)
        { return options.Language == "en" ? (value ? "TRUE" : "FALSE") : (value ? "ИСТИНА" : "ЛОЖЬ"); }
    }
}
