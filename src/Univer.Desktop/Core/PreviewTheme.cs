using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // Light and dark versions of a converted page. The file's own colours are remapped with (L' + 0.05) = k / (L + 0.05),
    // which keeps every contrast ratio while the white page becomes the dark card; printing keeps the original colours.
    internal static class PreviewTheme
    {
        // The dark card of PreviewPage (#1d2229, the app's dark surface) and the white paper of the light page.
        private static readonly double Card = Luminance(0x1d, 0x22, 0x29);
        private static readonly double Scale = (Card + 0.05) * 1.05;
        private static readonly double CardHue, CardSaturation;
        static PreviewTheme() { double l; ToHsl(0x1d, 0x22, 0x29, out CardHue, out CardSaturation, out l); }
        private static readonly Regex Attribute = new Regex("style=\"([^\"]*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex Declaration = new Regex(@"(^|;)\s*(color|background|background-color)\s*:\s*#([0-9a-fA-F]{6})\b", RegexOptions.CultureInvariant);
        private static readonly Regex Hex = new Regex(@"#([0-9a-fA-F]{6})\b", RegexOptions.CultureInvariant);
        private static readonly Regex DocumentStyle = new Regex(@"<style data-doc>([\s\S]*?)</style>", RegexOptions.CultureInvariant);
        public static string Apply(string html, bool dark)
        {
            if (!dark || string.IsNullOrEmpty(html)) return html;
            var start = html.IndexOf("<html", StringComparison.Ordinal);
            if (start < 0) return html;
            var page = html.Substring(0, start) + "<html class=\"dark\"" + html.Substring(start + 5);
            page = DocumentStyle.Replace(page, match =>
                "<style data-doc>" + Hex.Replace(match.Groups[1].Value, hex => "#" + Dark(hex.Groups[1].Value)) + "@media print{" + match.Groups[1].Value + "}</style>");
            page = Attribute.Replace(page, match =>
            {
                var style = match.Groups[1].Value;
                if (style.IndexOf('#') < 0) return match.Value;
                var original = new StringBuilder();
                var mapped = Declaration.Replace(style, declaration =>
                {
                    var name = declaration.Groups[2].Value; var value = declaration.Groups[3].Value;
                    original.Append(name == "color" ? ";--pc:#" : ";--pb:#").Append(value);
                    return declaration.Groups[1].Value + name + ":#" + Dark(value);
                });
                return "style=\"" + mapped + original + "\"";
            });
            // The original colours of remapped inline styles, for printing (the shell prints on white).
            var head = page.IndexOf("</head>", StringComparison.Ordinal);
            if (head < 0) return page;
            return page.Substring(0, head) + "<style>@media print{html.dark [style*=\"--pc:\"]{color:var(--pc)!important}html.dark [style*=\"--pb:\"]{background:var(--pb)!important}}</style>" + page.Substring(head);
        }
        // "rrggbb" (no #) in, "rrggbb" out.
        public static string Dark(string hex)
        {
            int value;
            if (hex == null || hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return hex;
            int r = value >> 16 & 255, g = value >> 8 & 255, b = value & 255;
            var target = Math.Max(0, Math.Min(1, Scale / (Luminance(r, g, b) + 0.05) - 0.05));
            double h, s, l; ToHsl(r, g, b, out h, out s, out l);
            // Greys take the card's own slight blue, so white paper turns into exactly the dark card.
            if (s < 0.05) { h = CardHue; s = CardSaturation; }
            // Luminance grows with HSL lightness at a fixed hue and saturation, so a bisection finds the matching lightness.
            double low = 0, high = 1;
            for (var i = 0; i < 24; i++)
            {
                var middle = (low + high) / 2; int mr, mg, mb; FromHsl(h, s, middle, out mr, out mg, out mb);
                if (Luminance(mr, mg, mb) < target) low = middle; else high = middle;
            }
            int nr, ng, nb; FromHsl(h, s, (low + high) / 2, out nr, out ng, out nb);
            return nr.ToString("x2") + ng.ToString("x2") + nb.ToString("x2");
        }
        public static double Luminance(int r, int g, int b) { return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b); }
        public static double Contrast(double first, double second) { return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05); }
        private static double Linear(int channel) { var c = channel / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        internal static void ToHsl(int r, int g, int b, out double h, out double s, out double l)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
            l = (max + min) / 2; h = 0; s = 0;
            if (max == min) return;
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == rf) h = (gf - bf) / d + (gf < bf ? 6 : 0); else if (max == gf) h = (bf - rf) / d + 2; else h = (rf - gf) / d + 4;
            h /= 6;
        }
        internal static void FromHsl(double h, double s, double l, out int r, out int g, out int b)
        {
            if (s <= 0) { r = g = b = Channel(l); return; }
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q;
            r = Channel(Hue(p, q, h + 1.0 / 3)); g = Channel(Hue(p, q, h)); b = Channel(Hue(p, q, h - 1.0 / 3));
        }
        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        private static int Channel(double value) { return Math.Max(0, Math.Min(255, (int)Math.Round(value * 255))); }
    }
}
