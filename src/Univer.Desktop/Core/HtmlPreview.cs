using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Univer.Desktop.Core
{
    // Saved web pages rebuilt from an allow-list (text, headings, lists, tables, emphasis, colours); scripts, styles, frames,
    // forms, handlers, remote pictures and non-http(s)/mailto addresses are dropped, and the viewer's policy blocks the rest.
    internal static class HtmlPreview
    {
        public const long FileLimit = 16L * 1024 * 1024;
        private const int DepthLimit = 200;
        private static readonly HashSet<string> Kept = new HashSet<string>(StringComparer.Ordinal)
        {
            "p", "div", "span", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "dl", "dt", "dd", "table", "thead", "tbody", "tfoot",
            "tr", "td", "th", "caption", "colgroup", "col", "b", "strong", "i", "em", "u", "s", "del", "ins", "sub", "sup", "small", "mark", "code",
            "pre", "kbd", "samp", "var", "blockquote", "q", "cite", "abbr", "address", "section", "article", "header", "footer", "main", "nav", "aside",
            "figure", "figcaption", "details", "summary", "a", "img", "wbr", "time", "bdi"
        };
        // Old presentational tags shown through a kept one.
        private static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.Ordinal)
        { { "strike", "s" }, { "tt", "code" }, { "big", "span" }, { "font", "span" }, { "center", "div" }, { "nobr", "span" }, { "bdo", "span" }, { "fieldset", "div" }, { "legend", "strong" }, { "listing", "pre" }, { "xmp", "pre" }, { "plaintext", "pre" } };
        // Dropped with everything inside them.
        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.Ordinal)
        { "head", "script", "style", "title", "template", "svg", "math", "object", "embed", "iframe", "frame", "frameset", "applet", "audio", "video", "canvas", "select", "datalist", "map", "noembed", "noframes", "dialog" };
        private static readonly HashSet<string> Void = new HashSet<string>(StringComparer.Ordinal) { "br", "hr", "img", "col", "wbr", "input", "meta", "link", "base", "area", "param", "source", "track" };
        private static readonly HashSet<string> Blocks = new HashSet<string>(StringComparer.Ordinal)
        { "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "dl", "table", "pre", "blockquote", "hr", "section", "article", "header", "footer", "main", "nav", "aside", "figure", "address", "details" };
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "black", "000000" }, { "white", "ffffff" }, { "red", "ff0000" }, { "green", "008000" }, { "blue", "0000ff" }, { "yellow", "ffff00" },
            { "gray", "808080" }, { "grey", "808080" }, { "silver", "c0c0c0" }, { "maroon", "800000" }, { "navy", "000080" }, { "purple", "800080" },
            { "teal", "008080" }, { "olive", "808000" }, { "lime", "00ff00" }, { "aqua", "00ffff" }, { "fuchsia", "ff00ff" }, { "orange", "ffa500" },
            { "brown", "a52a2a" }, { "pink", "ffc0cb" }, { "darkblue", "00008b" }, { "darkred", "8b0000" }, { "darkgreen", "006400" }, { "lightgray", "d3d3d3" },
            { "lightgrey", "d3d3d3" }, { "lightblue", "add8e6" }, { "lightyellow", "ffffe0" }, { "lightgreen", "90ee90" }, { "whitesmoke", "f5f5f5" }, { "gold", "ffd700" }
        };
        private static readonly Regex Charset = new Regex(@"charset\s*=\s*[""']?\s*([A-Za-z0-9_\-:.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private sealed class Tag { public string Name; public bool End, SelfClosing; public List<KeyValuePair<string, string>> Attributes = new List<KeyValuePair<string, string>>(); }
        public static PreviewDocument Convert(string path, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            if (new FileInfo(path).Length > FileLimit) throw new PreviewLimitException();
            return Convert(File.ReadAllBytes(path), options, cancellation);
        }
        public static PreviewDocument Convert(byte[] data, PreviewOptions options, CancellationToken cancellation = default(CancellationToken))
        {
            if (data.Length > FileLimit) throw new PreviewLimitException();
            var source = Decode(data);
            var images = new PreviewImages();
            var body = new PreviewMarkup(cancellation);
            Rebuild(source, body, images, cancellation);
            var html = new PreviewMarkup(cancellation);
            PreviewPage.Start(html, options.Title, "web");
            var note = options.Label("htmlNote");
            if (!string.IsNullOrEmpty(note)) html.Append("<p class=\"note\" role=\"note\" data-text=\"").Append(WebUtility.HtmlEncode(note)).Append("\"></p>");
            html.Append(body).Append("</body></html>");
            return new PreviewDocument(html.ToString(), images.Items);
        }
        // Byte order mark, then a declared charset, then strict UTF-8, then Windows-1251 (Cyrillic pages saved without one).
        internal static string Decode(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            var head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 4096));
            var match = Charset.Match(head);
            if (match.Success)
            {
                try
                {
                    var declared = Encoding.GetEncoding(match.Groups[1].Value);
                    if (declared.CodePage != 1200 && declared.CodePage != 1201) return declared.GetString(data);
                }
                catch (ArgumentException) { }
            }
            try { return new UTF8Encoding(false, true).GetString(data); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(1251).GetString(data); }
        }
        private static void Rebuild(string source, PreviewMarkup html, PreviewImages images, CancellationToken cancellation)
        {
            var open = new List<string>(); var tables = 0; var position = 0; var steps = 0;
            while (position < source.Length)
            {
                if (++steps % 4096 == 0) cancellation.ThrowIfCancellationRequested();
                var lt = source.IndexOf('<', position);
                if (lt < 0) { Text(html, source.Substring(position)); break; }
                if (lt > position) Text(html, source.Substring(position, lt - position));
                position = lt;
                if (string.CompareOrdinal(source, position, "<!--", 0, 4) == 0)
                {
                    var end = source.IndexOf("-->", position + 4, StringComparison.Ordinal);
                    position = end < 0 ? source.Length : end + 3; continue;
                }
                if (position + 1 < source.Length && (source[position + 1] == '!' || source[position + 1] == '?'))
                {
                    var end = source.IndexOf('>', position);
                    position = end < 0 ? source.Length : end + 1; continue;
                }
                Tag tag; int next;
                if (!Parse(source, position, out tag, out next)) { Text(html, "<"); position++; continue; }
                position = next;
                var name = tag.Name;
                if (!tag.End && Skipped.Contains(name))
                {
                    // Skip to the matching end tag (raw text elements have no nested markup worth reading).
                    position = SkipTo(source, position, name); continue;
                }
                if (name == "textarea" && !tag.End)
                {
                    var end = source.IndexOf("</textarea", position, StringComparison.OrdinalIgnoreCase);
                    var text = end < 0 ? source.Substring(position) : source.Substring(position, end - position);
                    html.Append("<pre>"); Text(html, text); html.Append("</pre>");
                    position = end < 0 ? source.Length : SkipTo(source, end, "textarea", true); continue;
                }
                string output;
                if (Kept.Contains(name)) output = name;
                else if (!Renamed.TryGetValue(name, out output)) continue; // Unknown, form and document tags keep only their content.
                if (tag.End)
                {
                    var index = open.LastIndexOf(output);
                    if (index < 0) continue;
                    // Inside a table, an end tag closes nothing outside its own table.
                    var table = open.LastIndexOf("table");
                    if (output != "table" && table > index) continue;
                    while (open.Count > index) Close(html, open, ref tables);
                    continue;
                }
                if (Blocks.Contains(output) || output == "li" || output == "dt" || output == "dd") CloseParagraph(html, open, ref tables);
                if (output == "li") CloseUpTo(html, open, ref tables, "li", "ul", "ol");
                if (output == "dt" || output == "dd") { CloseUpTo(html, open, ref tables, "dt", "dl"); CloseUpTo(html, open, ref tables, "dd", "dl"); }
                if (output == "tr") { CloseUpTo(html, open, ref tables, "td", "table"); CloseUpTo(html, open, ref tables, "th", "table"); CloseUpTo(html, open, ref tables, "tr", "table"); }
                if (output == "td" || output == "th") { CloseUpTo(html, open, ref tables, "td", "tr", "table"); CloseUpTo(html, open, ref tables, "th", "tr", "table"); }
                if (output == "thead" || output == "tbody" || output == "tfoot")
                    foreach (var section in new[] { "td", "th", "tr", "thead", "tbody", "tfoot" }) CloseUpTo(html, open, ref tables, section, "table");
                if (open.Count >= DepthLimit) continue;
                if (output == "img") { Image(html, tag, images); continue; }
                if (output == "table" && tables == 0) html.Append("<div class=\"table-scroll\" tabindex=\"0\">");
                html.Append('<').Append(output);
                Attributes(html, name, output, tag);
                html.Append('>');
                if (Void.Contains(output)) continue;
                if (output == "table") tables++;
                open.Add(output);
                if (name == "plaintext") { Text(html, source.Substring(position)); position = source.Length; }
            }
            while (open.Count > 0) Close(html, open, ref tables);
        }
        private static void Close(PreviewMarkup html, List<string> open, ref int tables)
        {
            var name = open[open.Count - 1]; open.RemoveAt(open.Count - 1);
            html.Append("</").Append(name).Append('>');
            if (name == "table" && --tables == 0) html.Append("</div>");
        }
        private static void CloseParagraph(PreviewMarkup html, List<string> open, ref int tables)
        {
            var index = open.LastIndexOf("p");
            if (index >= 0 && open.LastIndexOf("table") < index && open.LastIndexOf("td") < index && open.LastIndexOf("th") < index)
                while (open.Count > index) Close(html, open, ref tables);
        }
        // Closes the nearest open name unless one of the boundaries is nearer.
        private static void CloseUpTo(PreviewMarkup html, List<string> open, ref int tables, string name, params string[] boundaries)
        {
            var index = open.LastIndexOf(name);
            if (index < 0) return;
            foreach (var boundary in boundaries) if (open.LastIndexOf(boundary) > index) return;
            while (open.Count > index) Close(html, open, ref tables);
        }
        private static int SkipTo(string source, int position, string name, bool fromEndTag = false)
        {
            var end = fromEndTag ? position : source.IndexOf("</" + name, position, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return source.Length;
            var close = source.IndexOf('>', end);
            return close < 0 ? source.Length : close + 1;
        }
        private static void Text(PreviewMarkup html, string text)
        {
            if (text.Length == 0) return;
            html.Append(WebUtility.HtmlEncode(WebUtility.HtmlDecode(text)));
        }
        private static bool Parse(string source, int start, out Tag tag, out int next)
        {
            tag = new Tag(); next = start;
            var i = start + 1;
            if (i < source.Length && source[i] == '/') { tag.End = true; i++; }
            var nameStart = i;
            while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '-' || source[i] == ':')) i++;
            if (i == nameStart || !char.IsLetter(source[nameStart])) return false;
            tag.Name = source.Substring(nameStart, i - nameStart).ToLowerInvariant();
            while (i < source.Length)
            {
                while (i < source.Length && (char.IsWhiteSpace(source[i]) || source[i] == '/')) { if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '>') tag.SelfClosing = true; i++; }
                if (i >= source.Length) break;
                if (source[i] == '>') { next = i + 1; return true; }
                var attributeStart = i;
                while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] != '=' && source[i] != '>' && source[i] != '/') i++;
                var attribute = source.Substring(attributeStart, i - attributeStart).ToLowerInvariant();
                while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
                var value = "";
                if (i < source.Length && source[i] == '=')
                {
                    i++;
                    while (i < source.Length && char.IsWhiteSpace(source[i])) i++;
                    if (i < source.Length && (source[i] == '"' || source[i] == '\''))
                    {
                        var quote = source[i]; var end = source.IndexOf(quote, i + 1);
                        if (end < 0) end = source.Length;
                        value = source.Substring(i + 1, end - i - 1); i = Math.Min(source.Length, end + 1);
                    }
                    else
                    {
                        var valueStart = i;
                        while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] != '>') i++;
                        value = source.Substring(valueStart, i - valueStart);
                    }
                }
                if (attribute.Length > 0 && tag.Attributes.Count < 64) tag.Attributes.Add(new KeyValuePair<string, string>(attribute, WebUtility.HtmlDecode(value)));
            }
            next = source.Length;
            return true;
        }
        private static void Attributes(PreviewMarkup html, string original, string output, Tag tag)
        {
            var css = new StringBuilder();
            if (original == "center") css.Append("text-align:center;");
            foreach (var pair in tag.Attributes)
            {
                var value = pair.Value.Trim();
                switch (pair.Key)
                {
                    case "colspan": case "rowspan":
                        int span;
                        if ((output == "td" || output == "th") && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out span) && span > 1 && span <= 1000)
                            html.Append(' ').Append(pair.Key).Append("=\"").Append(span).Append('"');
                        break;
                    case "span":
                        int columns;
                        if ((output == "col" || output == "colgroup") && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out columns) && columns > 1 && columns <= 1000)
                            html.Append(" span=\"").Append(columns).Append('"');
                        break;
                    case "start": case "value":
                        int number;
                        if ((output == "ol" && pair.Key == "start" || output == "li" && pair.Key == "value") && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
                            html.Append(' ').Append(pair.Key).Append("=\"").Append(number).Append('"');
                        break;
                    case "type":
                        if (output == "ol" && Regex.IsMatch(value, "^[1aAiI]$")) html.Append(" type=\"").Append(value).Append('"');
                        break;
                    case "title":
                        if (value.Length > 0 && value.Length <= 500) html.Append(" title=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
                        break;
                    case "dir":
                        if (value == "ltr" || value == "rtl") html.Append(" dir=\"").Append(value).Append('"');
                        break;
                    case "href":
                        if (output == "a" && (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) && value.IndexOfAny(new[] { '"', '<', '>', ' ', '\n', '\r', '\t' }) < 0)
                            html.Append(" href=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
                        break;
                    case "align":
                        var align = value.ToLowerInvariant();
                        if (align == "left" || align == "right" || align == "center" || align == "justify")
                        {
                            if (output == "table" && align == "center") css.Append("margin-left:auto;margin-right:auto;");
                            else if (output != "table" && output != "img") css.Append("text-align:").Append(align).Append(';');
                        }
                        break;
                    case "valign":
                        var vertical = value.ToLowerInvariant();
                        if (vertical == "top" || vertical == "middle" || vertical == "bottom" || vertical == "baseline") css.Append("vertical-align:").Append(vertical).Append(';');
                        break;
                    case "bgcolor":
                        var fill = DocumentMarkup.FillColor(Color(value));
                        if (fill != null) css.Append("background:").Append(fill).Append(';');
                        break;
                    case "color":
                        if (original == "font") { var color = Ink(Color(value)); if (color != null) css.Append("color:").Append(color).Append(';'); }
                        break;
                    case "size":
                        int size;
                        if (original == "font" && int.TryParse(value.TrimStart('+'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out size))
                        {
                            if (value.StartsWith("+", StringComparison.Ordinal) || value.StartsWith("-", StringComparison.Ordinal)) size += 3;
                            size = Math.Max(1, Math.Min(7, size));
                            css.Append("font-size:").Append(new[] { "", ".75em", ".85em", "1em", "1.15em", "1.4em", "1.8em", "2.4em" }[size]).Append(';');
                        }
                        break;
                    case "width":
                        if (output == "td" || output == "th" || output == "col" || output == "table") { var width = Length(value, true); if (width != null) css.Append("width:").Append(width).Append(';'); }
                        break;
                    case "nowrap":
                        if (output == "td" || output == "th") css.Append("white-space:nowrap;");
                        break;
                    case "style":
                        Style(css, value, output);
                        break;
                }
            }
            if (css.Length > 0) html.Append(" style=\"").Append(WebUtility.HtmlEncode(css.ToString().TrimEnd(';'))).Append('"');
        }
        // Only declarations that cannot load anything or move content out of the page.
        private static void Style(StringBuilder css, string style, string output)
        {
            if (style.Length > 2000) return;
            foreach (var declaration in style.Split(';'))
            {
                var colon = declaration.IndexOf(':');
                if (colon <= 0) continue;
                var name = declaration.Substring(0, colon).Trim().ToLowerInvariant();
                var value = declaration.Substring(colon + 1).Trim().ToLowerInvariant().Replace("!important", "").Trim();
                if (value.Length == 0 || value.Length > 100 || value.IndexOfAny(new[] { '(', '\\', '<', '>', '"', '\'', '/', '@' }) >= 0 && !value.StartsWith("rgb(", StringComparison.Ordinal)) continue;
                switch (name)
                {
                    case "color": var color = Ink(Color(value)); if (color != null) css.Append("color:").Append(color).Append(';'); break;
                    case "background": case "background-color": var fill = DocumentMarkup.FillColor(Color(value)); if (fill != null) css.Append("background:").Append(fill).Append(';'); break;
                    case "text-align": if (Regex.IsMatch(value, "^(left|right|center|justify)$")) css.Append("text-align:").Append(value).Append(';'); break;
                    case "font-weight": if (Regex.IsMatch(value, "^(bold|bolder|normal|[1-9]00)$")) css.Append("font-weight:").Append(value).Append(';'); break;
                    case "font-style": if (value == "italic" || value == "normal" || value == "oblique") css.Append("font-style:").Append(value).Append(';'); break;
                    case "text-decoration": case "text-decoration-line": if (Regex.IsMatch(value, "^(none|underline|line-through)( (underline|line-through))?$")) css.Append("text-decoration:").Append(value).Append(';'); break;
                    case "vertical-align": if (Regex.IsMatch(value, "^(top|middle|bottom|baseline|sub|super)$")) css.Append("vertical-align:").Append(value).Append(';'); break;
                    case "white-space": if (Regex.IsMatch(value, "^(normal|nowrap|pre|pre-wrap|pre-line)$")) css.Append("white-space:").Append(value).Append(';'); break;
                    case "font-size": var size = Length(value, false); if (size != null) css.Append("font-size:").Append(size).Append(';'); break;
                    case "width": if (output == "td" || output == "th" || output == "col" || output == "table") { var width = Length(value, true); if (width != null) css.Append("width:").Append(width).Append(';'); } break;
                }
            }
        }
        // Lengths in px, pt, em or %, within readable bounds.
        private static string Length(string value, bool width)
        {
            var match = Regex.Match(value.Trim().ToLowerInvariant(), @"^([0-9]+(?:\.[0-9]+)?)(px|pt|em|%)?$");
            double number;
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return null;
            var unit = match.Groups[2].Success && match.Groups[2].Value.Length > 0 ? match.Groups[2].Value : "px";
            if (width) { if (unit == "%") return number > 0 && number <= 100 ? number.ToString("0.##", CultureInfo.InvariantCulture) + "%" : null; return number > 0 && number <= 4000 && unit != "em" ? number.ToString("0.##", CultureInfo.InvariantCulture) + unit : null; }
            var px = unit == "pt" ? number * 4 / 3 : unit == "em" ? number * 15 : unit == "%" ? number * 0.15 : number;
            return px >= 9 && px <= 48 ? number.ToString("0.##", CultureInfo.InvariantCulture) + unit : null;
        }
        // #rgb, #rrggbb, rgb(r,g,b) or a common colour name, as rrggbb (no #).
        private static string Color(string value)
        {
            value = (value ?? "").Trim().ToLowerInvariant();
            string named; if (Names.TryGetValue(value, out named)) return named;
            var match = Regex.Match(value, @"^#?([0-9a-f]{3}|[0-9a-f]{6})$");
            if (match.Success)
            {
                var hex = match.Groups[1].Value;
                return hex.Length == 3 ? new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] }) : hex;
            }
            match = Regex.Match(value, @"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*(?:,\s*[0-9.]+\s*)?\)$");
            if (!match.Success) return null;
            int r = Math.Min(255, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)), g = Math.Min(255, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)), b = Math.Min(255, int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            return r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
        }
        private static string Ink(string hex) { return DocumentMarkup.TextColor(hex); }
        private static readonly Regex DataImage = new Regex(@"^data:image/(png|jpeg|jpg|gif|bmp);base64,([A-Za-z0-9+/=\s]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Embedded pictures are shown; linked ones would need the network or the disk, so only their description stays.
        private static void Image(PreviewMarkup html, Tag tag, PreviewImages images)
        {
            string source = null, alt = null; double width = 0;
            foreach (var pair in tag.Attributes)
            {
                if (pair.Key == "src") source = pair.Value.Trim();
                else if (pair.Key == "alt") alt = pair.Value.Trim();
                else if (pair.Key == "width") double.TryParse(pair.Value.Trim().Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out width);
            }
            var match = source == null ? null : DataImage.Match(source);
            if (match != null && match.Success)
            {
                byte[] data = null;
                try { data = System.Convert.FromBase64String(Regex.Replace(match.Groups[2].Value, @"\s", "")); } catch (FormatException) { }
                var type = match.Groups[1].Value.ToLowerInvariant(); var mime = "image/" + (type == "jpg" ? "jpeg" : type);
                if (data != null)
                {
                    var key = "html-" + images.Items.Count;
                    var path = images.Add(key, data, mime);
                    PreviewImages.Write(html, path, (long)width, new PreviewOptions(""));
                    return;
                }
            }
            if (!string.IsNullOrEmpty(alt)) html.Append("<span class=\"omitted\">").Append(WebUtility.HtmlEncode(alt.Length > 300 ? alt.Substring(0, 300) : alt)).Append("</span>");
        }
    }
}
