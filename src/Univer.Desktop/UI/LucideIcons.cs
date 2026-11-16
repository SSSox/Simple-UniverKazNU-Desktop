using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Univer.Desktop.UI
{
    // Lucide 0.460 outlines (ISC licence, see licenses/lucide.txt) on their 24-unit grid. A path is one SVG "d" string;
    // "|" separates shapes. Every icon draws at one stroke weight, so the shell reads as one set.
    internal static class LucideIcons
    {
        private static readonly Dictionary<UiIcon, string> Paths = new Dictionary<UiIcon, string>
        {
            { UiIcon.Award, "m15.477 12.89 1.515 8.526a.5.5 0 0 1-.81.47l-3.58-2.687a1 1 0 0 0-1.197 0l-3.586 2.686a.5.5 0 0 1-.81-.469l1.514-8.526|M6 8a6 6 0 1 0 12 0a6 6 0 1 0-12 0" },
            { UiIcon.Laptop, "M6 5h12a2 2 0 0 1 2 2v9H4V7a2 2 0 0 1 2-2z|M2 19h20" },
            { UiIcon.Home, "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8|M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" },
            { UiIcon.Downloads, "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4|M7 10l5 5 5-5|M12 15V3" },
            { UiIcon.Settings, "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z|M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0" },
            { UiIcon.External, "M15 3h6v6|M10 14 21 3|M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" },
            { UiIcon.Back, "m12 19-7-7 7-7|M19 12H5" },
            { UiIcon.Forward, "M5 12h14|m12 5 7 7-7 7" },
            { UiIcon.Reload, "M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8|M21 3v5h-5" },
            { UiIcon.Menu, "M4 12h16|M4 6h16|M4 18h16" },
            { UiIcon.Plus, "M5 12h14|M12 5v14" },
            { UiIcon.Minus, "M5 12h14" },
            { UiIcon.Eye, "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0|M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0" },
            { UiIcon.EyeOff, "M10.733 5.076a10.744 10.744 0 0 1 11.205 6.575 1 1 0 0 1 0 .696 10.747 10.747 0 0 1-1.444 2.49|M14.084 14.158a3 3 0 0 1-4.242-4.242|M17.479 17.499a10.75 10.75 0 0 1-15.417-5.151 1 1 0 0 1 0-.696 10.75 10.75 0 0 1 4.446-5.143|m2 2 20 20" },
            { UiIcon.Lock, "M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z|M7 11V7a5 5 0 0 1 10 0v4" },
            { UiIcon.Person, "M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2|M8 7a4 4 0 1 0 8 0a4 4 0 1 0-8 0" },
            { UiIcon.Close, "M18 6 6 18|m6 6 12 12" },
            { UiIcon.Check, "M20 6 9 17l-5-5" },
            { UiIcon.Info, "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0|M12 16v-4|M12 8h.01" },
            { UiIcon.Warning, "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3|M12 9v4|M12 17h.01" },
            { UiIcon.Calendar, "M8 2v4|M16 2v4|M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z|M3 10h18|M8 14h.01|M12 14h.01|M16 14h.01|M8 18h.01|M12 18h.01|M16 18h.01" },
            { UiIcon.Book, "M12 7v14|M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z" },
            { UiIcon.Journal, "M9 2h6a1 1 0 0 1 1 1v2a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z|M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2|m9 14 2 2 4-4" },
            { UiIcon.Folder, "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z" },
            { UiIcon.Sort, "m3 16 4 4 4-4|M7 20V4|m21 8-4-4-4 4|M17 4v16" },
            { UiIcon.Cap, "M21.42 10.922a1 1 0 0 0-.019-1.838L12.83 5.18a2 2 0 0 0-1.66 0L2.6 9.08a1 1 0 0 0 0 1.832l8.57 3.908a2 2 0 0 0 1.66 0z|M22 10v6|M6 12.5V16a6 3 0 0 0 12 0v-3.5" },
            { UiIcon.Search, "M3 11a8 8 0 1 0 16 0a8 8 0 1 0-16 0|m21 21-4.3-4.3" },
            { UiIcon.Up, "m18 15-6-6-6 6" },
            { UiIcon.Left, "m15 18-6-6 6-6" },
            { UiIcon.Right, "m9 18 6-6-6-6" },
            { UiIcon.Present, "M2 3h20|M21 3v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V3|m7 21 5-5 5 5" },
            { UiIcon.Down, "m6 9 6 6 6-6" },
            { UiIcon.Bell, "M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9|M10.3 21a1.94 1.94 0 0 0 3.4 0" },
            { UiIcon.File, "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z|M14 2v4a2 2 0 0 0 2 2h4|M10 9H8|M16 13H8|M16 17H8" },
        };
        private static readonly Dictionary<UiIcon, GraphicsPath> Cache = new Dictionary<UiIcon, GraphicsPath>();
        public static GraphicsPath Get(UiIcon icon)
        {
            GraphicsPath path;
            if (Cache.TryGetValue(icon, out path)) return path;
            string data;
            if (!Paths.TryGetValue(icon, out data)) return null;
            path = new GraphicsPath();
            foreach (var shape in data.Split('|')) new PathReader(shape, path).Read();
            Cache[icon] = path; return path;
        }
        // The part of SVG path syntax Lucide uses: M L H V C S A Z, absolute and relative, with implicit repeats.
        private sealed class PathReader
        {
            private readonly string text; private readonly GraphicsPath path; private int at;
            private float x, y, startX, startY, controlX, controlY; private bool hasControl;
            public PathReader(string text, GraphicsPath path) { this.text = text; this.path = path; }
            public void Read()
            {
                var command = ' ';
                path.StartFigure();
                while (true)
                {
                    Skip();
                    if (at >= text.Length) return;
                    if (char.IsLetter(text[at])) command = text[at++];
                    var relative = char.IsLower(command);
                    float ox = relative ? x : 0, oy = relative ? y : 0;
                    switch (char.ToUpperInvariant(command))
                    {
                        case 'M':
                            x = ox + Number(); y = oy + Number(); startX = x; startY = y; path.StartFigure();
                            command = relative ? 'l' : 'L'; hasControl = false; Dot(); break;
                        case 'L': Line(ox + Number(), oy + Number()); break;
                        case 'H': Line(ox + Number(), y); break;
                        case 'V': Line(x, oy + Number()); break;
                        case 'C':
                            { float x1 = ox + Number(), y1 = oy + Number(), x2 = ox + Number(), y2 = oy + Number(), ex = ox + Number(), ey = oy + Number(); Curve(x1, y1, x2, y2, ex, ey); }
                            break;
                        case 'S':
                            {
                                var x1 = hasControl ? 2 * x - controlX : x; var y1 = hasControl ? 2 * y - controlY : y;
                                float x2 = ox + Number(), y2 = oy + Number(), ex = ox + Number(), ey = oy + Number(); Curve(x1, y1, x2, y2, ex, ey);
                            }
                            break;
                        case 'A':
                            { float rx = Number(), ry = Number(), turn = Number(); var large = Flag(); var sweep = Flag(); float ex = ox + Number(), ey = oy + Number(); Arc(rx, ry, turn, large, sweep, ex, ey); }
                            break;
                        case 'Z': path.CloseFigure(); x = startX; y = startY; hasControl = false; break;
                        default: throw new FormatException("Unsupported path command " + command);
                    }
                }
            }
            // A path that only moves still needs its start for round-capped dots such as "M12 8h.01".
            private void Dot() { }
            private void Line(float ex, float ey) { path.AddLine(x, y, ex, ey); x = ex; y = ey; hasControl = false; }
            private void Curve(float x1, float y1, float x2, float y2, float ex, float ey)
            { path.AddBezier(x, y, x1, y1, x2, y2, ex, ey); controlX = x2; controlY = y2; x = ex; y = ey; hasControl = true; }
            // The SVG endpoint arc as cubic segments of at most a quarter turn each.
            private void Arc(float rx, float ry, float turn, bool large, bool sweep, float ex, float ey)
            {
                if (rx == 0 || ry == 0 || (x == ex && y == ey)) { Line(ex, ey); return; }
                double phi = turn * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
                double dx = (x - ex) / 2.0, dy = (y - ey) / 2.0, x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
                double a = Math.Abs(rx), b = Math.Abs(ry), scale = x1 * x1 / (a * a) + y1 * y1 / (b * b);
                if (scale > 1) { a *= Math.Sqrt(scale); b *= Math.Sqrt(scale); }
                var factor = Math.Sqrt(Math.Max(0, (a * a * b * b - a * a * y1 * y1 - b * b * x1 * x1) / (a * a * y1 * y1 + b * b * x1 * x1)));
                if (large == sweep) factor = -factor;
                double cx1 = factor * a * y1 / b, cy1 = -factor * b * x1 / a;
                double cx = cos * cx1 - sin * cy1 + (x + ex) / 2.0, cy = sin * cx1 + cos * cy1 + (y + ey) / 2.0;
                var from = Math.Atan2((y1 - cy1) / b, (x1 - cx1) / a); var to = Math.Atan2((-y1 - cy1) / b, (-x1 - cx1) / a); var delta = to - from;
                if (sweep && delta < 0) delta += 2 * Math.PI; else if (!sweep && delta > 0) delta -= 2 * Math.PI;
                var parts = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9); var step = delta / parts; var handle = 4.0 / 3 * Math.Tan(step / 4);
                for (var i = 0; i < parts; i++)
                {
                    double t1 = from + i * step, t2 = t1 + step, c1 = Math.Cos(t1), s1 = Math.Sin(t1), c2 = Math.Cos(t2), s2 = Math.Sin(t2);
                    Func<double, double, PointF> place = (px, py) => new PointF((float)(cos * a * px - sin * b * py + cx), (float)(sin * a * px + cos * b * py + cy));
                    var p1 = place(c1 - handle * s1, s1 + handle * c1); var p2 = place(c2 + handle * s2, s2 - handle * c2); var p3 = place(c2, s2);
                    path.AddBezier(new PointF(x, y), p1, p2, p3); x = p3.X; y = p3.Y;
                }
                x = ex; y = ey; hasControl = false;
            }
            private void Skip() { while (at < text.Length && (text[at] == ' ' || text[at] == ',')) at++; }
            private bool Flag() { Skip(); return text[at++] == '1'; }
            private float Number()
            {
                Skip(); var start = at;
                if (at < text.Length && (text[at] == '-' || text[at] == '+')) at++;
                var dot = false;
                while (at < text.Length && (char.IsDigit(text[at]) || (text[at] == '.' && !dot))) { if (text[at] == '.') dot = true; at++; }
                return float.Parse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
