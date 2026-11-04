using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Univer.Desktop.Core
{
    // Excel number formats shared by the XLS and XLSX readers: decimals, percent, scientific, currency, text, dates and
    // times in both date systems, elapsed time and sections. Fractions and other rare codes fall back to General.
    internal static class CellFormat
    {
        public static string Builtin(int id, CultureInfo culture)
        {
            var shortDate = culture.Name.StartsWith("en", StringComparison.Ordinal) ? "m/d/yyyy" : "dd.mm.yyyy";
            switch (id)
            {
                case 0: return "General";
                case 1: return "0"; case 2: return "0.00"; case 3: return "#,##0"; case 4: return "#,##0.00";
                case 9: return "0%"; case 10: return "0.00%"; case 11: return "0.00E+00"; case 12: return "# ?/?"; case 13: return "# ??/??";
                case 14: return shortDate; case 15: return "d-mmm-yy"; case 16: return "d-mmm"; case 17: return "mmm-yy";
                case 18: return "h:mm AM/PM"; case 19: return "h:mm:ss AM/PM"; case 20: return "h:mm"; case 21: return "h:mm:ss"; case 22: return shortDate + " h:mm";
                case 37: return "#,##0 ;(#,##0)"; case 38: return "#,##0 ;[Red](#,##0)"; case 39: return "#,##0.00;(#,##0.00)"; case 40: return "#,##0.00;[Red](#,##0.00)";
                case 45: return "mm:ss"; case 46: return "[h]:mm:ss"; case 47: return "mm:ss.0"; case 48: return "##0.0E+0"; case 49: return "@";
                default: return id >= 27 && id <= 36 || id >= 50 && id <= 58 ? shortDate : "General";
            }
        }
        public static bool IsDate(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            var section = Sections(code)[0];
            for (var i = 0; i < section.Length; i++)
            {
                var c = section[i];
                if (c == '"') { i = section.IndexOf('"', i + 1); if (i < 0) return false; continue; }
                if (c == '\\' || c == '_' || c == '*') { i++; continue; }
                if (c == '[')
                {
                    var end = section.IndexOf(']', i); if (end < 0) return false;
                    var inner = section.Substring(i + 1, end - i - 1).ToLowerInvariant();
                    if (inner.Length > 0 && inner.Trim('h') == "" || inner.Trim('m') == "" && inner.Length > 0 || inner.Trim('s') == "" && inner.Length > 0) return true;
                    i = end; continue;
                }
                if ("dmyhsDMYHS".IndexOf(c) >= 0) return true;
            }
            return false;
        }
        public static string Format(double value, string code, bool date1904, CultureInfo culture)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "#NUM!";
            if (string.IsNullOrEmpty(code) || code.Equals("General", StringComparison.OrdinalIgnoreCase)) return General(value, culture);
            var sections = Sections(code);
            string section; var signed = value;
            var conditional = Pick(sections, value, out section);
            if (!conditional)
            {
                // Without conditions: positive (and the only section), negative without its sign, zero.
                var count = sections.Count;
                if (count > 1 && sections[count - 1].IndexOf('@') >= 0 && !IsDate(sections[count - 1]) && Digits(sections[count - 1]) == 0) count--;
                if (value < 0 && count >= 2) { section = sections[1]; signed = -value; }
                else if (value == 0 && count >= 3) section = sections[2];
                else section = sections[0];
            }
            if (section.Length == 0) return "";
            try
            {
                if (IsDate(section)) return Date(value, section, date1904, culture) ?? General(value, culture);
                return Number(signed, section, culture) ?? General(signed, culture);
            }
            catch (FormatException) { return General(value, culture); }
        }
        // Excel's General: up to about eleven characters, scientific for very large and very small values.
        public static string General(double value, CultureInfo culture)
        {
            if (value == 0) return "0";
            var abs = Math.Abs(value);
            if (abs >= 1e11 || abs < 1e-9) return value.ToString("0.#####E+00", culture);
            var whole = abs >= 1 ? (int)Math.Floor(Math.Log10(abs)) + 1 : 1;
            var decimals = Math.Max(0, Math.Min(15, 10 - whole));
            return Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), culture);
        }
        // Serial day numbers: 1900 system (with Excel's 29 February 1900) or 1904 system.
        public static bool ToDate(double serial, bool date1904, out DateTime date)
        {
            date = default(DateTime);
            if (serial < 0 || serial >= 2958466) return false;
            var day = Math.Floor(serial);
            var origin = date1904 ? new DateTime(1904, 1, 1) : day >= 61 ? new DateTime(1899, 12, 30) : new DateTime(1899, 12, 31);
            if (!date1904 && day == 60) { date = new DateTime(1900, 2, 28).AddMilliseconds(Math.Round((serial - day) * 86400000)); return true; }
            date = origin.AddDays(day).AddMilliseconds(Math.Round((serial - day) * 86400000));
            return true;
        }
        private static List<string> Sections(string code)
        {
            var result = new List<string>(); var start = 0;
            for (var i = 0; i < code.Length; i++)
            {
                var c = code[i];
                if (c == '"') { var end = code.IndexOf('"', i + 1); i = end < 0 ? code.Length : end; }
                else if (c == '\\' || c == '_' || c == '*') i++;
                else if (c == '[') { var end = code.IndexOf(']', i); i = end < 0 ? code.Length : end; }
                else if (c == ';') { result.Add(code.Substring(start, i - start)); start = i + 1; }
            }
            result.Add(code.Substring(Math.Min(start, code.Length)));
            return result;
        }
        // Sections with conditions such as [<0] or [>=100]: the first one that matches, else the next plain section.
        private static bool Pick(List<string> sections, double value, out string section)
        {
            section = null; var any = false;
            foreach (var candidate in sections)
            {
                string op; double limit;
                if (!Condition(candidate, out op, out limit)) { if (any) { section = candidate; return true; } continue; }
                any = true;
                if (op == "<" && value < limit || op == "<=" && value <= limit || op == ">" && value > limit || op == ">=" && value >= limit ||
                    op == "=" && value == limit || op == "<>" && value != limit) { section = candidate; return true; }
            }
            if (any) section = sections[sections.Count - 1];
            return any;
        }
        private static bool Condition(string section, out string op, out double limit)
        {
            op = null; limit = 0;
            for (var start = section.IndexOf('['); start >= 0; start = section.IndexOf('[', start + 1))
            {
                var end = section.IndexOf(']', start); if (end < 0) return false;
                var inner = section.Substring(start + 1, end - start - 1).Trim();
                foreach (var candidate in new[] { "<=", ">=", "<>", "<", ">", "=" })
                    if (inner.StartsWith(candidate, StringComparison.Ordinal) &&
                        double.TryParse(inner.Substring(candidate.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out limit)) { op = candidate; return true; }
            }
            return false;
        }
        private static int Digits(string section)
        {
            var count = 0;
            for (var i = 0; i < section.Length; i++)
            {
                var c = section[i];
                if (c == '"') { var end = section.IndexOf('"', i + 1); i = end < 0 ? section.Length : end; }
                else if (c == '\\' || c == '_' || c == '*') i++;
                else if (c == '[') { var end = section.IndexOf(']', i); i = end < 0 ? section.Length : end; }
                else if (c == '0' || c == '#' || c == '?') count++;
            }
            return count;
        }
        // Translates one section into a .NET custom format, whose 0 # . , % and E+0 match Excel's; every literal is
        // escaped. Returns null for codes it does not cover (fractions).
        private static string Number(double value, string section, CultureInfo culture)
        {
            if (section.IndexOf("General", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var at = section.IndexOf("General", StringComparison.OrdinalIgnoreCase);
                return Literal(section.Substring(0, at), culture) + General(value, culture) + Literal(section.Substring(at + 7), culture);
            }
            var format = new StringBuilder();
            for (var i = 0; i < section.Length; i++)
            {
                var c = section[i];
                if (c == '"') { var end = section.IndexOf('"', i + 1); if (end < 0) end = section.Length; Escape(format, section.Substring(i + 1, end - i - 1)); i = end; }
                else if (c == '\\') { if (i + 1 < section.Length) Escape(format, section[++i].ToString()); }
                else if (c == '_') { i++; format.Append("\\ "); }
                else if (c == '*') i++;
                else if (c == '[') { var end = section.IndexOf(']', i); if (end < 0) end = section.Length; Escape(format, Currency(section.Substring(i + 1, Math.Max(0, end - i - 1)))); i = end; }
                else if (c == '0' || c == '#' || c == '.' || c == ',' || c == '%') format.Append(c);
                else if (c == '?') format.Append('#');
                else if ((c == 'E' || c == 'e') && i + 1 < section.Length && (section[i + 1] == '+' || section[i + 1] == '-')) { format.Append('E').Append(section[i + 1]); i++; }
                else if (c == '/') return null;
                else if (c == '@') continue;
                else Escape(format, c.ToString());
            }
            return value.ToString(format.ToString(), culture);
        }
        private static string Literal(string section, CultureInfo culture)
        {
            var text = new StringBuilder();
            for (var i = 0; i < section.Length; i++)
            {
                var c = section[i];
                if (c == '"') { var end = section.IndexOf('"', i + 1); if (end < 0) end = section.Length; text.Append(section, i + 1, end - i - 1); i = end; }
                else if (c == '\\') { if (i + 1 < section.Length) text.Append(section[++i]); }
                else if (c == '_') { i++; text.Append(' '); }
                else if (c == '*') i++;
                else if (c == '[') { var end = section.IndexOf(']', i); if (end < 0) end = section.Length; text.Append(Currency(section.Substring(i + 1, Math.Max(0, end - i - 1)))); i = end; }
                else text.Append(c);
            }
            return text.ToString();
        }
        private static void Escape(StringBuilder format, string text) { foreach (var c in text) format.Append('\\').Append(c); }
        // [$€-407] shows €; [Red], [Color 10] and conditions show nothing.
        private static string Currency(string inner)
        {
            if (!inner.StartsWith("$", StringComparison.Ordinal)) return "";
            var dash = inner.IndexOf('-');
            return dash < 0 ? inner.Substring(1) : inner.Substring(1, dash - 1);
        }
        private sealed class Token { public char Kind; public string Text; }
        private static string Date(double value, string section, bool date1904, CultureInfo culture)
        {
            var tokens = new List<Token>(); var hour12 = false; var fraction = 0;
            for (var i = 0; i < section.Length; i++)
            {
                var c = section[i];
                if (c == '"') { var end = section.IndexOf('"', i + 1); if (end < 0) end = section.Length; tokens.Add(new Token { Kind = 'L', Text = section.Substring(i + 1, end - i - 1) }); i = end; }
                else if (c == '\\') { if (i + 1 < section.Length) tokens.Add(new Token { Kind = 'L', Text = section[++i].ToString() }); }
                else if (c == '_') { i++; tokens.Add(new Token { Kind = 'L', Text = " " }); }
                else if (c == '*') i++;
                else if (c == '[')
                {
                    var end = section.IndexOf(']', i); if (end < 0) end = section.Length;
                    var inner = section.Substring(i + 1, Math.Max(0, end - i - 1)).ToLowerInvariant();
                    if (inner.Length > 0 && (inner.Trim('h') == "" || inner.Trim('m') == "" || inner.Trim('s') == "")) tokens.Add(new Token { Kind = 'E', Text = inner });
                    else tokens.Add(new Token { Kind = 'L', Text = Currency(inner) });
                    i = end;
                }
                else if (string.Compare(section, i, "AM/PM", 0, 5, StringComparison.OrdinalIgnoreCase) == 0) { tokens.Add(new Token { Kind = 'A', Text = "AM/PM" }); hour12 = true; i += 4; }
                else if (string.Compare(section, i, "A/P", 0, 3, StringComparison.OrdinalIgnoreCase) == 0) { tokens.Add(new Token { Kind = 'A', Text = "A/P" }); hour12 = true; i += 2; }
                else if ("dmyhsDMYHS".IndexOf(c) >= 0)
                {
                    var lower = char.ToLowerInvariant(c); var start = i;
                    while (i + 1 < section.Length && char.ToLowerInvariant(section[i + 1]) == lower) i++;
                    tokens.Add(new Token { Kind = 'D', Text = new string(lower, i - start + 1) });
                }
                else if (c == '.' && i + 1 < section.Length && section[i + 1] == '0')
                {
                    var start = i + 1; while (i + 1 < section.Length && section[i + 1] == '0') i++;
                    fraction = Math.Max(fraction, i - start + 1);
                    tokens.Add(new Token { Kind = 'F', Text = new string('0', i - start + 1) });
                }
                else tokens.Add(new Token { Kind = 'L', Text = c.ToString() });
            }
            // Time is rounded to the shown precision first, as Excel does (59.6 s shows as the next minute).
            var precision = Math.Pow(10, Math.Min(3, fraction));
            var rounded = Math.Round(value * 86400 * precision, MidpointRounding.AwayFromZero) / precision / 86400;
            DateTime date;
            if (!ToDate(rounded, date1904, out date)) return null;
            var names = culture.DateTimeFormat; var text = new StringBuilder();
            // Excel's 29 February 1900 (serial 60) does not exist in DateTime, which holds 28 February for it.
            var leap = !date1904 && Math.Floor(rounded) == 60;
            for (var t = 0; t < tokens.Count; t++)
            {
                var token = tokens[t];
                switch (token.Kind)
                {
                    case 'L': text.Append(token.Text); break;
                    case 'A':
                        var pm = date.Hour >= 12;
                        text.Append(token.Text == "A/P" ? (pm ? "P" : "A") : pm ? "PM" : "AM"); break;
                    case 'F':
                        var part = (rounded * 86400 - Math.Floor(rounded * 86400 + 1e-9)) * Math.Pow(10, token.Text.Length);
                        text.Append(((long)Math.Round(part)).ToString(new string('0', token.Text.Length), CultureInfo.InvariantCulture).Substring(0, token.Text.Length)); break;
                    case 'E':
                        var total = rounded * 24;
                        var unit = token.Text[0] == 'h' ? Math.Floor(total + 1e-9) : token.Text[0] == 'm' ? Math.Floor(total * 60 + 1e-9) : Math.Floor(total * 3600 + 1e-9);
                        text.Append(((long)unit).ToString(new string('0', token.Text.Length), CultureInfo.InvariantCulture)); break;
                    default:
                        var n = token.Text.Length;
                        switch (token.Text[0])
                        {
                            case 'y': text.Append(n <= 2 ? (date.Year % 100).ToString("00", CultureInfo.InvariantCulture) : date.Year.ToString("0000", CultureInfo.InvariantCulture)); break;
                            case 'd':
                                if (n >= 4) text.Append(names.GetDayName(date.DayOfWeek)); else if (n == 3) text.Append(names.GetAbbreviatedDayName(date.DayOfWeek));
                                else text.Append((leap ? 29 : date.Day).ToString(n == 2 ? "00" : "0", CultureInfo.InvariantCulture));
                                break;
                            case 'h':
                                var hour = hour12 ? (date.Hour % 12 == 0 ? 12 : date.Hour % 12) : date.Hour;
                                text.Append(hour.ToString(n >= 2 ? "00" : "0", CultureInfo.InvariantCulture)); break;
                            case 's': text.Append(date.Second.ToString(n >= 2 ? "00" : "0", CultureInfo.InvariantCulture)); break;
                            default:
                                if (n <= 2 && Minutes(tokens, t)) text.Append(date.Minute.ToString(n == 2 ? "00" : "0", CultureInfo.InvariantCulture));
                                else if (n >= 5) text.Append(names.GetMonthName(date.Month).Substring(0, 1));
                                else if (n == 4) text.Append(names.GetMonthName(date.Month));
                                else if (n == 3) text.Append(names.GetAbbreviatedMonthName(date.Month).TrimEnd('.'));
                                else text.Append(date.Month.ToString(n == 2 ? "00" : "0", CultureInfo.InvariantCulture));
                                break;
                        }
                        break;
                }
            }
            return text.ToString();
        }
        // "m" means minutes right after hours or right before seconds.
        private static bool Minutes(List<Token> tokens, int index)
        {
            for (var i = index - 1; i >= 0; i--)
            {
                if (tokens[i].Kind == 'D' || tokens[i].Kind == 'E') { if (tokens[i].Text[0] == 'h') return true; break; }
            }
            for (var i = index + 1; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == 'D' || tokens[i].Kind == 'E') return tokens[i].Text[0] == 's';
            }
            return false;
        }
    }
}
