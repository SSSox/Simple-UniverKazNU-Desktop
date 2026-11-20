using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // Shown over the site window when its page cannot be reached: the app's own copy of the week (Core/ScheduleCopy.cs),
    // its dates and when it was saved, one card a day. Past days and finished classes are quieter; today's card is marked
    // and scrolled into view. The banner above keeps Retry and the automatic retry.
    internal sealed class OfflineWeekView : Panel
    {
        private readonly ScrollHost scroller = new ScrollHost { Dock = DockStyle.Fill };
        private readonly WeekSheet sheet = new WeekSheet();
        private readonly float baseScale;
        private float zoom = 1;
        // Until the student scrolls, every layout (the panel appearing, a resize, a zoom) keeps today's card in view.
        private bool followToday = true;
        public OfflineWeekView(float scale)
        {
            baseScale = scale; Visible = false;
            BackColor = scroller.BackColor = Theme.Background;
            AccessibleRole = AccessibleRole.Document;
            scroller.Sheet = sheet; Controls.Add(scroller);
            scroller.Resize += (s, e) => Arrange();
            scroller.UserScrolled += () => followToday = false;
        }
        public void SetZoom(float value) { zoom = value; Arrange(); }
        // The page under it is an error page, so the keys go to the schedule: arrows, Page Up/Down, Home and End scroll it.
        public void FocusSheet() { scroller.Focus(); }
        internal bool FocusedForQa { get { return scroller.Focused; } }
        internal void PressForQa(Keys key) { scroller.Press(key); }
        public void Render(ScheduleWeek week, DateTime now, string lang)
        {
            var again = sheet.Same(week, lang);
            sheet.Set(week, now, lang);
            if (!again) followToday = true;
            Arrange();
        }
        private void Arrange()
        {
            if (scroller.ClientSize.Width <= 0) return;
            sheet.Arrange(scroller.ClientSize.Width, baseScale * zoom);
            // Painted by the scrolling area itself: a child control under AutoScroll lost its place when the position was
            // set while the panel was hidden (it was drawn a screen further down, and the window stayed empty).
            scroller.AutoScrollMinSize = new Size(0, sheet.Size.Height); scroller.Invalidate();
            AccessibleName = sheet.Title; AccessibleDescription = sheet.Words;
            // Today's card stays in view (the top when it is already in the upper part of the window). The scroll range
            // follows the sheet's new height only after layout.
            if (followToday && sheet.TodayTop >= 0 && scroller.ClientSize.Height > 0)
            {
                scroller.PerformLayout();
                var top = sheet.TodayTop <= scroller.ClientSize.Height * 3 / 5 ? 0 : sheet.TodayTop - (int)Math.Round(16 * baseScale * zoom);
                if (-scroller.AutoScrollPosition.Y != top) scroller.AutoScrollPosition = new Point(0, top);
            }
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Arrange(); }
        protected override void Dispose(bool disposing) { if (disposing) sheet.Dispose(); base.Dispose(disposing); }
        internal string TitleForQa { get { return sheet.Title; } }
        internal string WordsForQa { get { return sheet.Words; } }
        internal int DaysForQa { get { return sheet.DayCount; } }
        internal bool TodayMarkedForQa { get { return sheet.TodayTop >= 0; } }
        internal bool FitsForQa { get { return sheet.Fits; } }
        internal int ScrollForQa { get { return -scroller.AutoScrollPosition.Y; } }
        // Today's card (when the week has one) begins inside the window, where the sheet really is.
        internal bool TodayInViewForQa { get { var top = sheet.TodayTop - ScrollForQa; return sheet.TodayTop < 0 || (top >= 0 && top < scroller.ClientSize.Height); } }
        internal string LayoutForQa { get { return "sheet=" + sheet.Size + " scroll=" + ScrollForQa + " display=" + scroller.DisplayRectangle + " client=" + scroller.ClientSize + " today=" + sheet.TodayTop; } }
        // The window shows the sheet: a picture of the panel has text on it, not only the background.
        internal bool PaintedForQa
        {
            get
            {
                if (Width <= 0 || Height <= 0) return false;
                using (var picture = new Bitmap(Width, Height))
                {
                    DrawToBitmap(picture, new Rectangle(Point.Empty, Size));
                    var ground = Theme.Background; var ink = 0;
                    for (var y = 0; y < picture.Height; y += 2)
                        for (var x = 0; x < picture.Width; x += 2)
                        {
                            var c = picture.GetPixel(x, y);
                            if (Math.Abs(c.R - ground.R) + Math.Abs(c.G - ground.G) + Math.Abs(c.B - ground.B) > 90) ink++;
                        }
                    return ink >= 200;
                }
            }
        }
        // Each day's name is painted inside its card's heading: a name drawn anywhere else leaves that heading blank.
        internal bool TitlesDrawnForQa
        {
            get
            {
                if (sheet.Size.Width <= 0 || sheet.Size.Height <= 0 || sheet.TitleBoxes.Count == 0) return false;
                using (var picture = new Bitmap(sheet.Size.Width, sheet.Size.Height))
                {
                    using (var g = Graphics.FromImage(picture)) { g.Clear(Theme.Background); sheet.Paint(g); }
                    var surface = Theme.Surface;
                    return sheet.TitleBoxes.All(box =>
                    {
                        var ink = 0;
                        for (var y = Math.Max(0, box.Top); y < Math.Min(picture.Height, box.Bottom); y++)
                            for (var x = Math.Max(0, box.Left); x < Math.Min(picture.Width, box.Left + Math.Min(box.Width, 160)); x++)
                            {
                                var c = picture.GetPixel(x, y);
                                if (Math.Abs(c.R - surface.R) + Math.Abs(c.G - surface.G) + Math.Abs(c.B - surface.B) > 90) ink++;
                            }
                        return ink >= 12;
                    });
                }
            }
        }

        // A scrolling area that takes focus (a click, or the screen appearing over the page) and scrolls by the keys.
        private sealed class ScrollHost : Panel
        {
            public ScrollHost()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                AutoScroll = true; TabStop = true;
            }
            internal WeekSheet Sheet;
            protected override void OnPaint(PaintEventArgs e)
            {
                using (var fill = new SolidBrush(Theme.Background)) e.Graphics.FillRectangle(fill, ClientRectangle);
                if (Sheet == null) return;
                e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
                Sheet.Paint(e.Graphics);
            }
            // The student's own scrolling: keys, the wheel or the scroll bar (a position the view sets raises none of them).
            internal event Action UserScrolled;
            protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
            protected override void OnMouseWheel(MouseEventArgs e) { UserScrolled?.Invoke(); base.OnMouseWheel(e); }
            protected override void OnScroll(ScrollEventArgs se) { UserScrolled?.Invoke(); base.OnScroll(se); }
            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode) { case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true; }
                return base.IsInputKey(keyData);
            }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.Modifiers == Keys.None && Press(e.KeyCode)) { e.Handled = true; return; } base.OnKeyDown(e); }
            internal bool Press(Keys key)
            {
                var line = (int)Math.Round(40 * Theme.DpiScale); var page = Math.Max(line, ClientSize.Height - line);
                var max = Math.Max(0, DisplayRectangle.Height - ClientSize.Height); var y = -AutoScrollPosition.Y;
                switch (key)
                {
                    case Keys.Up: y -= line; break;
                    case Keys.Down: y += line; break;
                    case Keys.PageUp: y -= page; break;
                    case Keys.PageDown: y += page; break;
                    case Keys.Home: y = 0; break;
                    case Keys.End: y = max; break;
                    default: return false;
                }
                UserScrolled?.Invoke();
                AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(max, y)));
                return true;
            }
        }
        // One week laid out as day cards: the offline screen's sheet, and (SetExport) one column of the schedule picture.
        internal sealed class WeekSheet : IDisposable
        {
            private ScheduleWeek week; private DateTime now; private string lang = "ru";
            // The picture's column: its heading, and a note in place of the days (a week that could not be read).
            private string exportHeading, exportNote;
            private float scale = 1;
            private Font titleFont, bodyFont, strongFont, metaFont;
            private readonly List<Action<Graphics>> paint = new List<Action<Graphics>>();
            internal string Title = "", Words = "";
            internal int TodayTop = -1, DayCount;
            internal readonly List<Rectangle> TitleBoxes = new List<Rectangle>();
            internal bool Fits = true;
            internal Size Size { get; private set; }
            internal bool Same(ScheduleWeek other, string language) { return other == week && language == lang; }
            internal void Set(ScheduleWeek value, DateTime at, string language) { week = value; now = at; lang = language; exportHeading = exportNote = null; }
            // A week of the schedule picture: no saved time, no offline notes, no today or past marks; null week with a note
            // for a week that could not be read.
            internal void SetExport(ScheduleWeek value, string heading, string note, string language) { week = value; now = DateTime.MinValue; lang = language; exportHeading = heading; exportNote = note; }
            private int D(float value) { return (int)Math.Round(value * scale); }
            private void Fonts(float value)
            {
                if (titleFont != null && Math.Abs(value - scale) < .001f) return;
                foreach (var font in new[] { titleFont, bodyFont, strongFont, metaFont }) font?.Dispose();
                var zoom = value / Theme.DpiScale;
                titleFont = Theme.Strong(Theme.Title * zoom); bodyFont = Theme.Font(Theme.Body * zoom); strongFont = Theme.Strong(Theme.Body * zoom); metaFont = Theme.Font(Theme.Meta * zoom);
            }
            // PreserveGraphicsTranslateTransform: the text moves with the scroll offset the area paints at.
            private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl | TextFormatFlags.PreserveGraphicsTranslateTransform;
            private const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsTranslateTransform;
            private static int Measure(string text, Font font, int width) { return text.Length == 0 ? 0 : TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), Wrap).Height; }
            // One pass lays the sheet out and records what to paint, so painting draws exactly what was measured.
            internal void Arrange(int width, float value)
            {
                Fonts(value); scale = value; paint.Clear(); TitleBoxes.Clear(); TodayTop = -1; DayCount = 0; Fits = true;
                var words = new StringBuilder();
                if (exportHeading != null) { ArrangeExport(width, words); return; }
                Title = Strings.Get("offlineTitle", lang);
                var column = Math.Min(D(720), Math.Max(D(200), width - D(48)));
                var left = Math.Max(D(16), (width - column) / 2);
                var y = D(28);
                y = Write(Title, titleFont, () => Theme.Ink, left, y, column) + D(6);
                words.AppendLine(Title);
                if (OfflineWeek.Shows(week))
                {
                    var range = string.Format(Strings.Get("offlineWeek", lang), OfflineWeek.Range(week, lang));
                    var saved = OfflineWeek.Saved(week.SavedAt, now, lang);
                    y = Write(range, bodyFont, () => Theme.Ink, left, y, column) + D(2);
                    y = Write(saved, metaFont, () => Theme.Muted, left, y, column) + D(4);
                    words.AppendLine(range).AppendLine(saved);
                    var current = OfflineWeek.Current(week, now);
                    if (!current)
                    {
                        var note = Strings.Get(now >= week.End ? "offlinePast" : "offlineAhead", lang);
                        y = Write(note, metaFont, () => Theme.Ink, left, y + D(4), column) + D(4);
                        words.AppendLine(note);
                    }
                    y += D(16);
                    foreach (var day in OfflineWeek.ByDay(week))
                    {
                        DayCount++;
                        var today = current && day.Key == now.Date;
                        var past = day.Key < now.Date;
                        if (today) TodayTop = y;
                        y = Card(day.Key, day.Value, today, past, left, y, column, words) + D(12);
                    }
                }
                y += D(16);
                Words = words.ToString().Trim();
                Size = new Size(width, y);
            }
            private void ArrangeExport(int width, StringBuilder words)
            {
                Title = exportHeading;
                var column = Math.Max(D(160), width - D(48)); var left = D(24);
                var y = Write(Title, titleFont, () => Theme.Ink, left, D(28), column) + D(4);
                words.AppendLine(Title);
                // The dates under a heading that names the week by number (an unnumbered week's heading is its dates).
                var range = week != null ? OfflineWeek.Range(week, lang) : "";
                if (range.Length > 0 && range != Title) { y = Write(range, bodyFont, () => Theme.Muted, left, y, column) + D(4); words.AppendLine(range); }
                var note = exportNote ?? (OfflineWeek.Shows(week) ? null : Strings.Get("exportEmpty", lang));
                if (note != null) { y = Write(note, bodyFont, () => Theme.Ink, left, y + D(12), column) + D(4); words.AppendLine(note); }
                y += D(16);
                foreach (var day in OfflineWeek.ByDay(week))
                {
                    DayCount++;
                    y = Card(day.Key, day.Value, false, false, left, y, column, words) + D(12);
                }
                y += D(16);
                Words = words.ToString().Trim();
                Size = new Size(width, y);
            }
            private int Write(string text, Font font, Func<Color> color, int x, int y, int width)
            {
                var height = Measure(text, font, width);
                paint.Add(g => TextRenderer.DrawText(g, text, font, new Rectangle(x, y, width, height), color(), Wrap));
                return y + height;
            }
            private int Card(DateTime day, List<ScheduleClass> classes, bool today, bool past, int left, int top, int width, StringBuilder words)
            {
                var pad = D(16); var timeWidth = D(92); var gap = D(8);
                var inner = width - pad * 2;
                var steps = new List<Action<Graphics>>();
                var title = OfflineWeek.DayTitle(day, lang);
                var titleHeight = Measure(title, strongFont, inner);
                var chip = today ? Strings.Get("offlineToday", lang) : null;
                var y = top + D(14);
                var titleWidth = TextRenderer.MeasureText(title, strongFont, Size.Empty, Line).Width;
                // The box is fixed here: y moves on below, and a painting step that read it would draw the name under the card.
                var titleBox = new Rectangle(left + pad, y, inner, titleHeight); TitleBoxes.Add(titleBox);
                // Colours are read when painting, so a theme switch repaints the laid-out sheet in the new one.
                steps.Add(g => TextRenderer.DrawText(g, title, strongFont, titleBox, past ? Theme.Muted : Theme.Ink, Wrap));
                words.AppendLine(today ? title + " · " + chip : title);
                if (chip != null)
                {
                    var chipSize = TextRenderer.MeasureText(chip, metaFont, Size.Empty, Line);
                    var chipBox = new Rectangle(left + pad + Math.Min(titleWidth, inner) + gap, y + (titleHeight - chipSize.Height - D(4)) / 2, chipSize.Width + D(12), chipSize.Height + D(4));
                    if (chipBox.Right > left + width - pad) Fits = false;
                    steps.Add(g =>
                    {
                        using (var path = Theme.Rounded(chipBox, chipBox.Height / 2f)) using (var fill = new SolidBrush(Theme.Selected)) g.FillPath(fill, path);
                        TextRenderer.DrawText(g, chip, metaFont, chipBox, Theme.AccentInk, Line | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    });
                }
                y += titleHeight + D(10);
                foreach (var lesson in classes)
                {
                    var lineTop = y;
                    var time = OfflineWeek.Time(lesson); var details = OfflineWeek.Details(lesson, lang);
                    var nameWidth = inner - timeWidth;
                    var nameHeight = Measure(lesson.Name, bodyFont, nameWidth); var detailsHeight = Measure(details, metaFont, nameWidth);
                    var timeHeight = TextRenderer.MeasureText(time, bodyFont, Size.Empty, Line).Height;
                    if (TextRenderer.MeasureText(time, bodyFont, Size.Empty, Line).Width > timeWidth - gap) Fits = false;
                    var rowTop = lineTop + D(10);
                    var rowHeight = Math.Max(timeHeight, nameHeight + (detailsHeight > 0 ? D(2) + detailsHeight : 0));
                    var done = past || (today && lesson.End <= now);
                    steps.Add(g =>
                    {
                        using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, left + pad, lineTop, left + width - pad, lineTop);
                        TextRenderer.DrawText(g, time, bodyFont, new Rectangle(left + pad, rowTop, timeWidth, timeHeight), done ? Theme.Muted : Theme.Ink, Line);
                        TextRenderer.DrawText(g, lesson.Name, bodyFont, new Rectangle(left + pad + timeWidth, rowTop, nameWidth, nameHeight), done ? Theme.Muted : Theme.Ink, Wrap);
                        if (detailsHeight > 0) TextRenderer.DrawText(g, details, metaFont, new Rectangle(left + pad + timeWidth, rowTop + nameHeight + D(2), nameWidth, detailsHeight), Theme.Muted, Wrap);
                    });
                    words.AppendLine(time + " " + lesson.Name + (details.Length > 0 ? " · " + details : ""));
                    y = rowTop + rowHeight + D(10);
                }
                var card = new Rectangle(left, top, width, y + D(4) - top);
                paint.Add(g =>
                {
                    using (var path = Theme.Rounded(new RectangleF(card.X + .5f, card.Y + .5f, card.Width - 1, card.Height - 1), D(Kit.RadiusCard)))
                    using (var fill = new SolidBrush(Theme.Surface)) using (var pen = new Pen(today ? Theme.AccentInk : Theme.Border))
                    { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(fill, path); g.DrawPath(pen, path); g.SmoothingMode = SmoothingMode.None; }
                });
                paint.AddRange(steps);
                return card.Bottom;
            }
            internal void Paint(Graphics g) { foreach (var step in paint) step(g); }
            public void Dispose() { foreach (var font in new[] { titleFont, bodyFont, strongFont, metaFont }) font?.Dispose(); }
        }
    }
}
