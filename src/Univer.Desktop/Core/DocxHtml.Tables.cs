using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Univer.Desktop.Core
{
    internal static partial class DocxHtml
    {
        private const int ColumnLimit = 512;
        // Resolve Word's logical grid before emitting HTML. A vertical continuation occupies
        // grid columns, but must not create an extra HTML cell under an existing rowspan.
        private static void Table(XElement table, PreviewMarkup html, Context context, int depth)
        {
            var model = new TableModel();
            var previous = new Dictionary<int, TableModel.Cell>();
            var columns = 0;
            foreach (var row in table.Elements(W + "tr"))
            {
                var cells = new List<TableModel.Cell>();
                var active = new Dictionary<int, TableModel.Cell>();
                var column = GridNumber(row.Element(W + "trPr")?.Element(W + "gridBefore"), 0);
                if (column > 0) cells.Add(new TableModel.Cell { Span = column });
                foreach (var source in row.Elements(W + "tc"))
                {
                    var properties = source.Element(W + "tcPr");
                    var cell = new TableModel.Cell { Source = source, Span = Math.Max(1, GridNumber(properties?.Element(W + "gridSpan"), 1)) };
                    var start = column;
                    column += cell.Span;
                    if (column > ColumnLimit) throw new InvalidDataException("Table grid too large to preview");
                    var merge = properties?.Element(W + "vMerge");
                    TableModel.Cell first;
                    if (merge != null && merge.Attribute(W + "val")?.Value != "restart" &&
                        previous.TryGetValue(start, out first) && first.Span == cell.Span)
                    {
                        first.Rows++;
                        cell.Continuation = true;
                        active[start] = first;
                    }
                    else if (merge != null) active[start] = cell;
                    cells.Add(cell);
                }
                var after = GridNumber(row.Element(W + "trPr")?.Element(W + "gridAfter"), 0);
                if (after > 0) cells.Add(new TableModel.Cell { Span = after });
                columns = Math.Max(columns, column + after);
                if (columns > ColumnLimit) throw new InvalidDataException("Table grid too large to preview");
                model.Rows.Add(cells);
                previous = active;
            }
            var grid = table.Element(W + "tblGrid")?.Elements(W + "gridCol").ToArray() ?? new XElement[0];
            if (grid.Length > ColumnLimit) throw new InvalidDataException("Table grid too large to preview");
            model.Columns = Math.Max(columns, grid.Length);
            model.Widths = grid.Select(c => { int value; return int.TryParse(c.Attribute(W + "w")?.Value, out value) && value > 0 && value <= 100000 ? value : 0; }).ToArray();
            DocumentMarkup.Table(html, model, depth, cell =>
            {
                foreach (var block in ((XElement)cell.Source).Elements()) Block(block, html, context, depth + 1, true);
            });
        }
        private static int GridNumber(XElement value, int fallback)
        {
            int number;
            if (!int.TryParse(value?.Attribute(W + "val")?.Value, out number) || number < 0) return fallback;
            if (number > ColumnLimit) throw new InvalidDataException("Table grid too large to preview");
            return number;
        }
    }
}
