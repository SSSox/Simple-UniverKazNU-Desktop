using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // "Загрузки": every file saved through the app, with search, type and course filters, sorting, live progress and
    // the in-app viewer (FilePreview).
    internal sealed partial class DownloadsView : Panel
    {
        public event Action<DownloadRecord> OpenOutside, ShowInFolder, CancelDownload, RemoveRecord;
        public event Action OpenFolder, ViewChanged, OrderChanged;
        public event Action<Keys> Shortcut;
        private const int PageSize = 100;
        private static readonly FileKind?[] Filters = { null, FileKind.Pdf, FileKind.Word, FileKind.Excel, FileKind.PowerPoint, FileKind.Image, FileKind.Other };
        private static readonly string[] FilterNames = { "filterAll", "filterPdf", "filterWord", "filterExcel", "filterPowerPoint", "filterImages", "filterOther" };
        private float scale, zoom = 1;
        private readonly float baseScale;
        private readonly Panel listPage = new Panel(), list = new Panel(), sheet = new Panel();
        private readonly FilePreview preview;
        private readonly TextInput search = new TextInput(false);
        private readonly FlowLayoutPanel chips = new FlowLayoutPanel();
        private readonly ModernButton folder = new ModernButton(), more = new ModernButton(), sort = new ModernButton(), subjects = new ModernButton();
        private readonly PickMenu sortMenu, subjectMenu;
        private readonly ContextMenuStrip rowMenu = new ContextMenuStrip();
        private readonly ToolTip tips = new ToolTip();
        private readonly RowStyle rowStyle;
        private readonly Label empty = new Label();
        private readonly StageView emptyStage = new StageView(StageScene.Downloads) { Visible = false };
        private readonly Dictionary<string, DownloadRow> rows = new Dictionary<string, DownloadRow>();
        private readonly List<DownloadRecord> items = new List<DownloadRecord>();
        // The course (or site section) a file was downloaded from; null shows every file.
        private string subject;
        // Control.Visible reads false while the page itself is hidden, so the layout keeps its own flag.
        private bool hasSubjects;
        private DownloadSort order = DownloadSort.Newest;
        private FileKind? filter;
        private string language = "ru";
        private int limit = PageSize;
        public DownloadsView(float scale, Action<string> external)
        {
            this.scale = baseScale = scale;
            rowStyle = new RowStyle(tips); rowStyle.Set(scale, 1); Theme.Tips(tips);
            sortMenu = new PickMenu(""); subjectMenu = new PickMenu(Strings.Get("subjectSearch", language));
            Dock = DockStyle.Fill; BackColor = Theme.Background; Visible = false;
            listPage.Dock = DockStyle.Fill;
            preview = new FilePreview(scale, external);
            preview.Back += ClosePreview;
            preview.OpenOutside += item => OpenOutside?.Invoke(item);
            preview.ShowInFolder += item => ShowInFolder?.Invoke(item);
            preview.Shortcut += key => Shortcut?.Invoke(key);
            preview.ContentChanged += () => ViewChanged?.Invoke();
            Controls.Add(preview); Controls.Add(listPage);
            search.Input.TextChanged += (s, e) => { limit = PageSize; Rebuild(); };
            chips.WrapContents = true; chips.BackColor = Theme.Background;
            foreach (var kind in Filters)
            {
                var chip = new ModernButton { Look = ButtonLook.Chip, Height = D(34), Tag = kind, BackColor = Theme.Background, Font = Theme.Font(Theme.Body) };
                chip.Margin = new Padding(0, 0, D(6), D(6));
                chip.Click += (s, e) => { filter = (FileKind?)((ModernButton)s).Tag; limit = PageSize; UpdateChips(); Rebuild(); MotionSurface.Reveal(list); };
                chips.Controls.Add(chip);
            }
            folder.Look = ButtonLook.Outline; folder.Icon = UiIcon.Folder; folder.Compact = true; folder.BackColor = Theme.Background; folder.Click += (s, e) => OpenFolder?.Invoke();
            // The chosen order and course stay readable on their own buttons; a chevron says each one opens a list.
            sort.Look = ButtonLook.Outline; sort.Icon = UiIcon.Sort; sort.Chevron = true; sort.BackColor = Theme.Background; sort.Font = Theme.Font(Theme.Body);
            sort.Click += (s, e) => OpenSort();
            sortMenu.Opened += (s, e) => sort.Open = true; sortMenu.Closed += (s, e) => sort.Open = false;
            sortMenu.Chosen += value => SetOrder((DownloadSort)value);
            subjectMenu.Opened += (s, e) => subjects.Open = true; subjectMenu.Closed += (s, e) => subjects.Open = false;
            subjectMenu.Chosen += value => SetSubject((string)value);
            // Courses come from the list itself: each one the files were downloaded from, with its file count.
            subjects.Look = ButtonLook.Outline; subjects.Icon = UiIcon.Book; subjects.Chevron = true; subjects.BackColor = Theme.Background; subjects.Font = Theme.Font(Theme.Body); subjects.Visible = false;
            subjects.Click += (s, e) => OpenSubjects();
            Motion.FadeIn(rowMenu); rowMenu.Font = Theme.Font(Theme.Body); rowMenu.ShowImageMargin = false; Theme.Style(rowMenu);
            // The list only ever scrolls down: rows take the width they are given.
            list.AutoScroll = false; list.HorizontalScroll.Maximum = 0; list.HorizontalScroll.Enabled = false; list.HorizontalScroll.Visible = false; list.AutoScroll = true;
            list.BackColor = sheet.BackColor = Theme.Background; list.Resize += (s, e) => { if (!arranging) LayoutRows(); };
            list.Controls.Add(sheet); sheet.LocationChanged += (s, e) => PlaceNear(); settle.Tick += (s, e) => Settle();
            empty.ForeColor = Theme.Muted; empty.Font = Theme.Font(Theme.Body); empty.TextAlign = ContentAlignment.TopCenter; empty.AutoSize = false; empty.BackColor = Theme.Background;
            more.Look = ButtonLook.Outline; more.BackColor = Theme.Background; more.Click += (s, e) => { limit += PageSize; Rebuild(); MotionSurface.Reveal(list); };
            sheet.Controls.Add(emptyStage); sheet.Controls.Add(empty); sheet.Controls.Add(more);
            foreach (var control in new Control[] { search, chips, subjects, sort, folder, list }) listPage.Controls.Add(control);
            listPage.Resize += (s, e) => ArrangeList();
            SetLanguage(language);
        }
        private int D(int value) { return (int)Math.Round(value * scale); }
        public bool Previewing { get { return preview.Showing; } }
        public int PreviewBarHeight { get { return preview.BarHeight; } }
        public string PreviewName { get { return preview.Item?.FileName ?? ""; } }
        public int Count { get { return items.Count; } }
        public IFindHandler FindHandler { get { return preview.FindHandler; } set { preview.FindHandler = value; } }
        public ChromiumWebBrowser FindTarget { get { return preview.FindTarget; } }
        internal bool PreferBuiltIn { get { return preview.PreferBuiltIn; } set { preview.PreferBuiltIn = value; } }
        internal void AnimateEntrance() { if (Previewing) preview.Reveal(); else MotionSurface.Reveal(listPage); }
        internal void FocusWorkspace() { if (Previewing) preview.FocusBack(); else search.Input.Focus(); }
        internal void FocusSearch() { if (Previewing) return; search.Input.Focus(); search.Input.SelectAll(); }
        // Follows the app zoom: sizes come from the zoomed scale, fonts are scaled from their original sizes.
        public void SetZoom(float value)
        {
            value = Math.Max(0.5f, Math.Min(2f, value));
            if (Math.Abs(zoom - value) < 0.001f) return;
            zoom = value; scale = baseScale * zoom; rowStyle.Set(scale, zoom);
            chips.SuspendLayout();
            foreach (Control chip in chips.Controls) { chip.Height = D(34); chip.Margin = new Padding(0, 0, D(6), D(6)); }
            chips.ResumeLayout(false);
            // Rows share one style: a zoom step repaints them instead of building them again.
            UiZoom.ApplyFonts(this, zoom); preview.SetZoom(zoom); UpdateChips(); UpdateSort(); UpdateSubject(); ArrangeList();
            foreach (var row in rows.Values) row.Restyle();
        }
        public void SetLanguage(string value)
        {
            language = value;
            search.Input.AccessibleName = search.Cue = Strings.Get("downloadsSearch", language); emptyStage.SetLanguage(language);
            for (var i = 0; i < chips.Controls.Count; i++)
            {
                var chip = (ModernButton)chips.Controls[i]; chip.Text = Strings.Get(FilterNames[i], language);
            }
            UpdateSort(); UpdateSubject();
            folder.Text = folder.AccessibleName = Strings.Get("openFolder", language); tips.SetToolTip(folder, folder.Text);
            more.Text = more.AccessibleName = Strings.Get("showMore", language);
            preview.SetLanguage(language);
            foreach (var row in rows.Values) row.SetLanguage(language);
            UpdateChips(); ArrangeList(); Rebuild();
        }
        public void SetItems(IEnumerable<DownloadRecord> source) { items.Clear(); items.AddRange(source.Select(x => x.Clone())); Rebuild(); }
        public void Upsert(DownloadRecord item)
        {
            var index = items.FindIndex(x => x.Id == item.Id);
            var before = index >= 0 ? items[index] : null;
            if (index >= 0) items[index] = item.Clone(); else { items.Insert(0, item.Clone()); animateRows = true; }
            // Progress changes neither what is shown nor its order (sorting uses the total); a new name or state can change both,
            // and the counts on the chips with them.
            DownloadRow row;
            if (before == null || before.State != item.State || before.FileName != item.FileName || before.Total != item.Total) Rebuild();
            else if (rows.TryGetValue(item.Id, out row)) row.Update(item.Clone());
        }
        public void Remove(string id) { Remove(new[] { id }); }
        // Several at once (a folder deleted outside the app) rebuild the list once, and a removed row leaves as a picture of
        // itself only when a few go. The shown file closes when its record leaves.
        public void Remove(ICollection<string> ids)
        {
            var gone = new HashSet<string>(ids);
            if (items.RemoveAll(x => gone.Contains(x.Id)) == 0) return;
            if (Previewing && preview.Item != null && gone.Contains(preview.Item.Id)) ClosePreview();
            animateRows = gone.Count <= 8; Rebuild();
        }
        public bool ClearSearch() { if (Previewing || search.Input.TextLength == 0) return false; search.Input.Clear(); return true; }
        // Refresh re-checks files on disk (moved or deleted outside the app) or reopens the shown file.
        public void RefreshView() { if (Previewing && preview.Item != null) Preview(preview.Item); else { recheckFiles = true; Rebuild(); } }
        public bool PrintPreview() { return preview.Print(); }
        public void Preview(DownloadRecord item) { _ = PreviewAsync(item); }
        internal async Task PreviewAsync(DownloadRecord item)
        {
            if (item == null || item.State != DownloadState.Completed) return;
            listPage.Visible = false;
            await preview.ShowAsync(item);
        }
        public void ClosePreview()
        {
            if (!preview.Close()) return;
            listPage.Visible = true; listPage.BringToFront();
            MotionSurface.Reveal(listPage, -1);
            ViewChanged?.Invoke();
        }
        internal void ReleaseViewer() { preview.ReleaseViewer(); }
        // After a theme change: the menus, the rows and the shown file follow the new palette.
        internal void ApplyTheme() { Theme.Style(rowMenu); preview.ApplyTheme(); Invalidate(true); }
        private int[] Counts()
        {
            // What a chip promises to show: its kind within the current search and course.
            var counts = new int[Filters.Length]; var words = FileKinds.Words(search.Input.Text);
            foreach (var record in items)
            {
                if (!FileKinds.Matches(record, words, null, subject)) continue;
                counts[0]++; var kind = FileKinds.Of(record.FileName);
                for (var i = 1; i < Filters.Length; i++) if (FileKinds.Is(kind, Filters[i].Value)) counts[i]++;
            }
            return counts;
        }
        private void UpdateChips()
        {
            var counts = Counts(); chips.SuspendLayout();
            for (var i = 0; i < chips.Controls.Count; i++)
            {
                var chip = (ModernButton)chips.Controls[i]; var shown = i == 0 || counts[i] > 0 || Equals(chip.Tag, filter);
                chip.Selected = Equals(chip.Tag, filter); chip.Note = counts[i].ToString(); chip.AccessibleName = chip.Text + ": " + counts[i];
                chip.Width = TextRenderer.MeasureText(chip.Text, chip.Font).Width + TextRenderer.MeasureText(chip.Note, chip.Font).Width + D(30);
                chip.Visible = shown; chip.Invalidate();
            }
            chips.ResumeLayout();
        }
        private static string SortKey(DownloadSort value)
        { return value == DownloadSort.Name ? "sortName" : value == DownloadSort.Size ? "sortSize" : "sortNewest"; }
        private void SetOrder(DownloadSort value)
        {
            if (order == value) return;
            order = value; UpdateSort(); ArrangeList(); Rebuild(); MotionSurface.Reveal(list);
            OrderChanged?.Invoke();
        }
        // The order the student chose last time (preferences), given before the list is filled: no animation, no event.
        public DownloadSort Order { get { return order; } set { order = value; UpdateSort(); } }
        private void UpdateSort()
        {
            sort.Text = Strings.Get(SortKey(order), language);
            sort.AccessibleName = string.Format(Strings.Get("sortLabel", language), sort.Text);
            sort.Width = TextRenderer.MeasureText(sort.Text, sort.Font).Width + D(76); sort.Invalidate();
        }
        private List<KeyValuePair<string, int>> Subjects()
        {
            return items.Where(x => !string.IsNullOrWhiteSpace(x.Source)).GroupBy(x => x.Source.Trim(), StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToList();
        }
        private void OpenSort()
        {
            var choices = new[] { DownloadSort.Newest, DownloadSort.Name, DownloadSort.Size }.Select(value => new PickItem { Text = Strings.Get(SortKey(value), language), Value = value, Selected = value == order });
            sortMenu.Show(sort, choices, scale, zoom, Math.Max(sort.Width, D(200)));
        }
        // Courses first, then the site's own pages files were saved from: those are not subjects a student looks for.
        private void OpenSubjects()
        {
            var choices = new List<PickItem> { new PickItem { Text = Strings.Get("subjectAll", language), Note = items.Count.ToString(), Value = null, Selected = subject == null } };
            var known = Subjects();
            foreach (var pair in known.Where(x => !FileKinds.PageSource(x.Key)))
                choices.Add(new PickItem { Group = Strings.Get("subjectCourses", language), Text = pair.Key, Note = pair.Value.ToString(), Value = pair.Key, Selected = string.Equals(pair.Key, subject, StringComparison.CurrentCultureIgnoreCase) });
            foreach (var pair in known.Where(x => FileKinds.PageSource(x.Key)))
                choices.Add(new PickItem { Group = Strings.Get("subjectPages", language), Text = pair.Key, Note = pair.Value.ToString(), Value = pair.Key, Selected = string.Equals(pair.Key, subject, StringComparison.CurrentCultureIgnoreCase) });
            subjectMenu.Show(subjects, choices, scale, zoom, Math.Max(subjects.Width, D(320)));
        }
        private void SetSubject(string value)
        {
            if (string.Equals(subject, value, StringComparison.CurrentCultureIgnoreCase)) return;
            subject = value; limit = PageSize; UpdateSubject(); UpdateChips(); ArrangeList(); Rebuild(); MotionSurface.Reveal(list);
        }
        // The button names the chosen course; a long name is shortened on the button, never in its accessible name.
        // Returns true when the filter row needs arranging again.
        private bool UpdateSubject()
        {
            var known = Subjects();
            if (subject != null && !known.Any(x => string.Equals(x.Key, subject, StringComparison.CurrentCultureIgnoreCase))) subject = null;
            var name = subject ?? Strings.Get("subjectAll", language);
            subjects.AccessibleName = string.Format(Strings.Get("filterSubject", language), name);
            var shown = name; var room = Math.Max(D(90), Math.Min(D(240), listPage.ClientSize.Width - D(48) - D(260) - sort.Width - D(70)));
            while (shown.Length > 4 && TextRenderer.MeasureText(shown, subjects.Font).Width > room) shown = shown.Substring(0, shown.Length - 2).TrimEnd() + "…";
            var width = TextRenderer.MeasureText(shown, subjects.Font).Width + D(76);
            var changed = hasSubjects != known.Count > 0 || subjects.Width != width;
            hasSubjects = known.Count > 0;
            subjects.Text = shown; subjects.Width = width; subjects.Visible = hasSubjects; subjects.Selected = subject != null; subjects.Invalidate();
            return changed;
        }
        private void ArrangeList()
        {
            var pad = D(24); var width = Math.Max(D(200), listPage.ClientSize.Width - pad * 2); var height = D(40); var top = D(14);
            // One line: search takes what is left after the course, the order and the folder.
            folder.SetBounds(pad + width - height, top, height, height);
            sort.SetBounds(folder.Left - D(8) - sort.Width, top, sort.Width, height);
            var right = sort.Left;
            if (hasSubjects) { subjects.SetBounds(sort.Left - D(8) - subjects.Width, top, subjects.Width, height); right = subjects.Left; }
            // The field keeps room around its box for the focus ring, so its box lines up with the buttons.
            var ring = D(TextInput.Ring);
            search.SetBounds(pad - ring, top - ring, Math.Max(D(140), right - D(8) - pad) + ring * 2, height + ring * 2);
            chips.SetBounds(pad, top + height + D(12), width, D(84));
            chips.Height = chips.GetPreferredSize(new Size(width, 0)).Height;
            var below = chips.Bottom + D(2);
            arranging = true;
            try { list.SetBounds(0, below, listPage.ClientSize.Width, Math.Max(0, listPage.ClientSize.Height - below)); }
            finally { arranging = false; }
            LayoutRows();
        }
        private void Rebuild()
        {
            if (IsDisposed) return;
            if (UpdateSubject()) ArrangeList();
            UpdateChips(); if (chips.Height != chips.GetPreferredSize(new Size(chips.Width, 0)).Height) ArrangeList();
            var words = FileKinds.Words(search.Input.Text); var recheck = recheckFiles; recheckFiles = false;
            var visible = DownloadOrder.Apply(items.Where(x => FileKinds.Matches(x, words, filter, subject)), order);
            var shown = visible.Take(limit).ToList();
            // Newest first reads by day; any other order is one sheet.
            var groups = shown.Select(x => order == DownloadSort.Newest ? Period(x) : null).ToList();
            sheet.SuspendLayout();
            var kept = new HashSet<string>(shown.Select(x => x.Id));
            // A removed row leaves as a picture of itself while the rows below close the gap.
            foreach (var id in rows.Keys.Where(id => !kept.Contains(id)).ToList())
            {
                var gone = rows[id]; rows.Remove(id);
                if (animateRows && Kit.Moving(gone)) MotionPopup.Play(MotionPopup.Capture(gone), gone.RectangleToScreen(gone.ClientRectangle), 240);
                Park(gone);
            }
            for (var index = 0; index < shown.Count; index++)
            {
                var item = shown[index]; DownloadRow row;
                if (!rows.TryGetValue(item.Id, out row))
                {
                    row = NextRow(); rows[item.Id] = row; fresh.Add(row);
                }
                row.Update(item, recheck); row.Tag = index;
                var opens = index == 0 || groups[index] != groups[index - 1];
                row.Place(opens ? groups[index] : null, opens, index + 1 == shown.Count || groups[index] != groups[index + 1]);
            }
            empty.Text = Strings.Get(items.Count == 0 ? "downloadsEmpty" : "downloadsNoMatch", language);
            empty.Visible = shown.Count == 0;
            emptyStage.Visible = items.Count == 0;
            moreShown = visible.Count > shown.Count; more.Visible = moreShown;
            sheet.ResumeLayout(false); LayoutRows();
            ViewChanged?.Invoke();
        }
        private string Period(DownloadRecord item)
        {
            var day = (item.Finished ?? item.Started).Date; var today = DateTime.Today;
            if (day >= today) return Strings.Get("periodToday", language);
            if (day == today.AddDays(-1)) return Strings.Get("periodYesterday", language);
            if (day > today.AddDays(-7)) return Strings.Get("periodWeek", language);
            if (day > today.AddDays(-31)) return Strings.Get("periodMonth", language);
            return Strings.Get("periodEarlier", language);
        }
        private void StepRow(DownloadRow from, int delta)
        {
            var ordered = rows.Values.OrderBy(r => (int)r.Tag).ToList(); var at = ordered.IndexOf(from) + delta;
            if (at < 0) { search.Input.Focus(); return; }
            if (at >= ordered.Count) return;
            SettleAll(); ordered[at].Focus(); list.ScrollControlIntoView(ordered[at]);
        }
        private void ShowRowMenu(DownloadRow row, Point at)
        {
            rowMenu.Items.Clear();
            Action<string, Action, bool> add = (key, run, enabled) => { var entry = new ToolStripMenuItem(Strings.Get(key, language)) { Enabled = enabled }; entry.Click += (s, e) => run(); rowMenu.Items.Add(entry); };
            add("openInside", row.Open, row.CanOpen); add("openOutside", row.OpenElsewhere, row.CanOpenOutside); add("showInFolder", row.Reveal, row.CanOpen);
            rowMenu.Items.Add(new ToolStripSeparator());
            add(row.Item?.State == DownloadState.Running ? "cancelDownload" : "removeFromList", row.Remove, true);
            rowMenu.Show(row, at);
        }
        // Set when the list itself changed: a new or removed file, not a resize or a filter.
        private bool animateRows;
        // Refresh looks at the disk again; a rebuild for a typed letter does not.
        private bool recheckFiles;
        private Tween rowMove;
        private readonly HashSet<DownloadRow> fresh = new HashSet<DownloadRow>();
        // ArrangeList places the rows itself once the list has its size; a second pass from the list's own Resize is wasted.
        private bool arranging;
        // Moving one row window is slow on some machines (about a millisecond each), and 100 rows are 100 windows. So the list
        // scrolls one sheet, a window step moves only the rows in view, and the others follow in small batches once it pauses.
        private readonly System.Windows.Forms.Timer settle = new System.Windows.Forms.Timer { Interval = 40 };
        private readonly Dictionary<DownloadRow, Rectangle> waiting = new Dictionary<DownloadRow, Rectangle>();
        // Rows no filter shows wait hidden for the next one: a new window costs far more than a reused one.
        private readonly Stack<DownloadRow> spare = new Stack<DownloadRow>();
        private readonly HashSet<DownloadRow> parked = new HashSet<DownloadRow>();
        private bool placing, moreShown;
        private Rectangle View { get { return new Rectangle(0, -sheet.Top - D(120), Math.Max(1, sheet.Width), list.ClientSize.Height + D(240)); } }
        private DownloadRow NextRow()
        {
            if (spare.Count > 0) { var used = spare.Pop(); used.Recycle(); return used; }
            var row = new DownloadRow(rowStyle, language);
            row.OpenInside += x => Preview(x); row.OpenOutside += x => OpenOutside?.Invoke(x); row.ShowInFolder += x => ShowInFolder?.Invoke(x);
            row.CancelDownload += x => CancelDownload?.Invoke(x); row.RemoveRecord += x => RemoveRecord?.Invoke(x);
            row.Step += StepRow; row.MenuRequested += ShowRowMenu;
            sheet.Controls.Add(row);
            return row;
        }
        private void Park(DownloadRow row)
        {
            spare.Push(row); parked.Add(row); fresh.Remove(row);
            if (View.IntersectsWith(row.Bounds)) { waiting.Remove(row); row.Visible = false; } else { waiting[row] = Rectangle.Empty; settle.Start(); }
        }
        private void Apply(DownloadRow row, Rectangle target)
        {
            if (row.IsDisposed) return;
            if (target.IsEmpty) { row.Visible = false; return; }
            row.Bounds = target; row.Visible = true; parked.Remove(row);
        }
        private void Settle()
        {
            foreach (var pair in waiting.Take(8).ToList()) { waiting.Remove(pair.Key); Apply(pair.Key, pair.Value); }
            if (waiting.Count == 0) settle.Stop();
        }
        private void SettleAll() { foreach (var pair in waiting.ToList()) Apply(pair.Key, pair.Value); waiting.Clear(); settle.Stop(); }
        // Scrolling brings waiting rows into view before they would show at their old place.
        private void PlaceNear()
        {
            if (placing || waiting.Count == 0) return;
            var view = View;
            foreach (var pair in waiting.Where(p => view.IntersectsWith(p.Value.IsEmpty ? p.Key.Bounds : p.Value)).ToList()) { waiting.Remove(pair.Key); Apply(pair.Key, pair.Value); }
        }
        private void LayoutRows()
        {
            if (placing) return;
            placing = true; sheet.SuspendLayout();
            try { PlaceRows(); }
            finally { sheet.ResumeLayout(false); placing = false; }
        }
        private void PlaceRows()
        {
            // Rows stay two text lines tall; a denser list shows more files on a laptop screen.
            var ordered = rows.Values.OrderBy(r => (int)r.Tag).ToList(); var pad = D(24); var y = D(2);
            var bottom = y + ordered.Sum(r => r.WantedHeight);
            var height = ordered.Count == 0 ? D(270) : bottom + (moreShown ? D(60) : D(2));
            // The sheet leaves the scroll bar its room, so the bar coming or going never asks for a second pass.
            var bar = height > list.Height ? SystemInformation.VerticalScrollBarWidth : 0;
            sheet.Size = new Size(Math.Max(1, list.Width - bar), height);
            var width = Math.Max(D(200), sheet.Width - pad * 2); var view = View;
            var animate = animateRows && Kit.Moving(list); animateRows = false; rowMove?.Cancel();
            var moves = new List<Tuple<DownloadRow, int, int>>();
            foreach (var row in ordered)
            {
                var rowHeight = row.WantedHeight; var near = view.IntersectsWith(new Rectangle(pad, y, width, rowHeight));
                // Rows carry each other: what follows a new row travels by its height, and the row itself arrives in place.
                var from = animate && near && !fresh.Contains(row) ? row.Top : y;
                var target = new Rectangle(pad, from, width, rowHeight);
                if (row.Bounds == target && !parked.Contains(row)) waiting.Remove(row);
                else if (near) { waiting.Remove(row); Apply(row, target); }
                else waiting[row] = target;
                if (from != y) moves.Add(Tuple.Create(row, from, y));
                y += rowHeight;
            }
            if (moves.Count > 0) rowMove = Motion.Run(Motion.Slide, t => { var e = Motion.Travel(t); foreach (var move in moves) if (!move.Item1.IsDisposed) move.Item1.Top = Motion.Lerp(move.Item2, move.Item3, e); });
            if (animate) foreach (var row in fresh) if (!row.IsDisposed && !waiting.ContainsKey(row)) row.Arrive();
            fresh.Clear();
            emptyStage.SetBounds(pad, D(32), width, D(148));
            empty.SetBounds(pad, emptyStage.Visible ? D(200) : D(40), width, D(60));
            more.SetBounds(pad + (width - D(200)) / 2, y + D(12), D(200), D(36));
            // Restarted on every step, so the batches run once the window stops changing.
            settle.Stop(); if (waiting.Count > 0) settle.Start();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { settle.Dispose(); sortMenu.Dispose(); subjectMenu.Dispose(); rowMenu.Dispose(); tips.Dispose(); rowStyle.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
