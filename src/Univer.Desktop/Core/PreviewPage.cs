using System.Net;

namespace Univer.Desktop.Core
{
    // The page shell of every converted file: one background, card, type scale and table look. Colours are variables, so
    // html.dark (added when the page is served, see PreviewTheme) switches it to the dark theme.
    internal static class PreviewPage
    {
        public static void Start(PreviewMarkup html, string title, string bodyClass = null, string documentCss = null)
        {
            html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>").Append(WebUtility.HtmlEncode(title)).Append("</title><style>")
                .Append(":root{--bg:#eef2f6;--card:#fff;--ink:#1d2b39;--muted:#5b6b7c;--line:#dfe5ec;--grid:#c8d3df;--head:#f3f6f9;--link:#1b5fa6;--shadow:rgba(16,40,70,.08);color-scheme:light}")
                .Append("html.dark{--bg:#15191e;--card:#1d2229;--ink:#e3e8ee;--muted:#9aa8b7;--line:#2c343e;--grid:#3a4552;--head:#232a33;--link:#7fb2ec;--shadow:rgba(0,0,0,.45);color-scheme:dark}")
                .Append("html{background:var(--bg)}body{box-sizing:border-box;max-width:1280px;margin:20px auto;padding:36px;background:var(--card);color:var(--ink);")
                .Append("font:15px/1.6 'Segoe UI Variable Text','Segoe UI',Calibri,Arial,sans-serif;border:1px solid var(--line);border-radius:10px;box-shadow:0 1px 8px var(--shadow)}")
                .Append("p{margin:0 0 10px}h1,h2,h3{line-height:1.3;margin:18px 0 10px}h1{font-size:24px}h2{font-size:20px}h3{font-size:17px}")
                .Append("body>p,body>h1,body>h2,body>h3{overflow-wrap:anywhere}.table-help,.note{color:var(--muted);font-size:13px;margin:0 0 22px}.table-help::before,.note::before{content:attr(data-text)}")
                .Append(".table-scroll{box-sizing:border-box;max-width:100%;overflow:auto;max-height:calc(100vh - 160px);margin:14px 0 22px;border:1px solid var(--grid)}")
                .Append(".table-scroll:focus{outline:2px solid var(--link);outline-offset:3px}table{border-collapse:collapse;width:100%;table-layout:fixed}")
                .Append("td{border:1px solid var(--grid);padding:8px;vertical-align:top;overflow-wrap:anywhere}td p:last-child{margin-bottom:0}td table td{padding:4px}")
                .Append("img{max-width:100%;height:auto}.marker{display:inline-block;min-width:1.6em;padding-right:.35em;text-indent:0}")
                .Append(".omitted{display:inline-block;padding:2px 8px;border:1px dashed var(--grid);border-radius:6px;color:var(--muted);font-size:13px}")
                .Append("hr.page{border:0;border-top:1px dashed var(--grid);margin:28px 0}a{color:var(--link)}")
                // Workbooks: one section per sheet; the column letters and row numbers are drawn from attributes, so find
                // (Ctrl+F) matches only the cells' own text.
                .Append(".sheets{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 22px;padding:0;list-style:none}.sheets a{display:inline-block;padding:3px 12px;border:1px solid var(--line);border-radius:999px;font-size:13px;text-decoration:none}")
                .Append(".sheets a::before{content:attr(data-text)}.sheet{margin:0 0 30px}.sheet>h2{margin-top:0}.sheet .table-scroll{max-height:calc(100vh - 120px)}")
                .Append("table.grid{width:auto;table-layout:auto;border-collapse:separate;border-spacing:0;font-size:14px;line-height:1.45}")
                .Append(".grid td,.grid th{border:0;border-right:1px solid var(--grid);border-bottom:1px solid var(--grid);padding:4px 8px;white-space:nowrap;overflow-wrap:normal;vertical-align:bottom}")
                .Append(".grid th{position:sticky;background:var(--head);color:var(--muted);font-weight:600;font-size:12px;text-align:center;min-width:2.4em}.grid th::before{content:attr(data-text)}")
                .Append(".grid thead th{top:0;z-index:2}.grid tbody th{left:0;z-index:1;padding:4px 6px}.grid thead th:first-child{left:0;z-index:3}")
                .Append(".grid .n{text-align:right}.grid .c{text-align:center}.grid .w{white-space:pre-wrap;overflow-wrap:anywhere}")
                // Presentations: one card per slide, in the order of the show.
                .Append("body.deck{max-width:1080px;padding:0;background:none;border:0;box-shadow:none}.slide{box-sizing:border-box;margin:0 0 20px;padding:28px 40px 26px;background:var(--card);")
                .Append("border:1px solid var(--line);border-radius:10px;box-shadow:0 1px 8px var(--shadow);overflow-wrap:anywhere}.slide>h2{font-size:24px;margin:4px 0 16px}")
                .Append(".slide-no{margin:0 0 6px;color:var(--muted);font-size:13px}.slide-no::before{content:attr(data-text)}.slide figure{margin:14px 0;text-align:center}.slide figure img{display:inline-block}")
                .Append(".slide .table-scroll{max-height:none}.slide-empty{color:var(--muted);font-size:13px;margin:0}.slide-empty::before{content:attr(data-text)}")
                // Web pages: their own text and tables in this page's look; preformatted text keeps its lines.
                .Append("pre,.text{white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.55 Consolas,'Cascadia Mono','Courier New',monospace;margin:0}")
                .Append("blockquote{margin:0 0 12px;padding:2px 0 2px 16px;border-left:3px solid var(--grid)}th{border:1px solid var(--grid);padding:8px;text-align:left;vertical-align:top}")
                .Append("body.web table{table-layout:auto}body.web td,body.web th{padding:6px 8px;overflow-wrap:break-word}")
                .Append("@media(max-width:900px){body{margin:0;padding:22px 16px;border:0;border-radius:0}.table-scroll{max-height:calc(100vh - 120px)}body.deck{padding:12px}.slide{padding:20px 18px}}")
                .Append("@media print{html{background:#fff}html.dark{--card:#fff;--ink:#1d2b39;--muted:#5b6b7c;--line:#dfe5ec;--grid:#c8d3df;--head:#f3f6f9;--link:#1b5fa6;color-scheme:light}")
                .Append("body{max-width:none;margin:0;padding:0;border:0;box-shadow:none}.table-help,.sheets{display:none}.table-scroll{max-height:none;overflow:visible;border:0}table{min-width:0!important}")
                .Append(".grid th{position:static}.slide{break-inside:avoid;box-shadow:none}}")
                .Append("</style>");
            // The file's own formatting (cell styles of a workbook) is kept apart from the shell, so the dark theme can
            // adapt its colours (PreviewTheme) without touching the shell's.
            if (!string.IsNullOrEmpty(documentCss)) html.Append("<style data-doc>").Append(documentCss).Append("</style>");
            html.Append("</head><body").Append(string.IsNullOrEmpty(bodyClass) ? "" : " class=\"" + bodyClass + "\"").Append('>');
        }
        // A plain-text file on the same card, lines kept; note (the truncation notice) goes above it.
        public static string Text(string title, string text, string note)
        {
            var start = new PreviewMarkup(System.Threading.CancellationToken.None);
            Start(start, title);
            var page = new System.Text.StringBuilder(start.ToString(), text.Length + 4096);
            if (!string.IsNullOrEmpty(note)) page.Append("<p class=\"note\" role=\"note\" data-text=\"").Append(WebUtility.HtmlEncode(note)).Append("\"></p>");
            page.Append("<pre class=\"text\">").Append(WebUtility.HtmlEncode(text)).Append("</pre></body></html>");
            return page.ToString();
        }
    }
}
