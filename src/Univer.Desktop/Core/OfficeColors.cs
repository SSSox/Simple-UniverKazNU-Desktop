using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Univer.Desktop.Core
{
    // Colours as Office files name them: the 64-entry indexed palette shared by XLS and XLSX, theme colours with a tint,
    // and the colour scheme of a DrawingML theme part (XLSX and PPTX).
    internal static class OfficeColors
    {
        private static readonly int[] Defaults =
        {
            0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
            0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
            0x800000, 0x008000, 0x000080, 0x808000, 0x800080, 0x008080, 0xC0C0C0, 0x808080,
            0x9999FF, 0x993366, 0xFFFFCC, 0xCCFFFF, 0x660066, 0xFF8080, 0x0066CC, 0xCCCCFF,
            0x000080, 0xFF00FF, 0xFFFF00, 0x00FFFF, 0x800080, 0x800000, 0x008080, 0x0000FF,
            0x00CCFF, 0xCCFFFF, 0xCCFFCC, 0xFFFF99, 0x99CCFF, 0xFF99CC, 0xCC99FF, 0xFFCC99,
            0x3366FF, 0x33CCCC, 0x99CC00, 0xFFCC00, 0xFF9900, 0xFF6600, 0x666699, 0x969696,
            0x003366, 0x339966, 0x003300, 0x333300, 0x993300, 0x993366, 0x333399, 0x333333
        };
        // A fresh copy of the default palette; XLS PALETTE records and XLSX indexedColors replace entries from 8 on.
        public static int[] Palette() { return (int[])Defaults.Clone(); }
        public static string Hex(int rgb) { return (rgb & 0xFFFFFF).ToString("x6"); }
        // "FF1F3864" (ARGB) or "1F3864" to rrggbb, or null.
        public static string FromArgb(string value)
        {
            int rgb;
            if (value == null) return null;
            if (value.Length == 8) value = value.Substring(2);
            return value.Length == 6 && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb) ? Hex(rgb) : null;
        }
        // Excel's tint: towards black below zero, towards white above, on the colour's HSL lightness.
        public static string Tint(string hex, double tint)
        {
            int rgb;
            if (hex == null || Math.Abs(tint) < 0.0001 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return hex;
            double h, s, l; PreviewTheme.ToHsl(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255, out h, out s, out l);
            l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + tint;
            return Lightness(h, s, l);
        }
        // DrawingML lumMod/lumOff (in 1/100000).
        public static string Modulate(string hex, int lumMod, int lumOff)
        {
            int rgb;
            if (hex == null || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return hex;
            double h, s, l; PreviewTheme.ToHsl(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255, out h, out s, out l);
            return Lightness(h, s, l * lumMod / 100000.0 + lumOff / 100000.0);
        }
        private static string Lightness(double h, double s, double l)
        {
            int r, g, b; PreviewTheme.FromHsl(h, s, Math.Max(0, Math.Min(1, l)), out r, out g, out b);
            return r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
        }
        // The colour scheme of a theme part by name (dk1, lt1, dk2, lt2, accent1..6, hlink, folHlink).
        public static Dictionary<string, string> Scheme(XDocument theme)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var scheme = theme?.Descendants().FirstOrDefault(e => e.Name.LocalName == "clrScheme");
            if (scheme == null) return result;
            foreach (var entry in scheme.Elements())
            {
                var color = entry.Elements().FirstOrDefault();
                var value = color == null ? null : color.Name.LocalName == "srgbClr" ? (string)color.Attribute("val") : color.Name.LocalName == "sysClr" ? (string)color.Attribute("lastClr") : null;
                value = FromArgb(value);
                if (value != null) result[entry.Name.LocalName] = value;
            }
            return result;
        }
        // SpreadsheetML theme indexes swap the first two pairs: 0 lt1, 1 dk1, 2 lt2, 3 dk2, then accents and links.
        public static string SheetTheme(Dictionary<string, string> scheme, int index)
        {
            var names = new[] { "lt1", "dk1", "lt2", "dk2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" };
            string value;
            return index >= 0 && index < names.Length && scheme != null && scheme.TryGetValue(names[index], out value) ? value : null;
        }
    }
}
