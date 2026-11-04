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
    internal sealed class DownloadsView : Panel
    {
        public event Action<DownloadRecord> OpenOutside, ShowInFolder, CancelDownload, RemoveRecord;
        public event Action OpenFolder, ViewChanged;
        public event Action<Keys> Shortcut;
        private const int PageSize = 100;
        private static readonly FileKind?[] Filters = { null, FileKind.Pdf, FileKind.Word, FileKind.Excel, FileKind.PowerPoint, FileKind.Image, FileKind.Other };
        private static readonly string[] FilterNames = { "filterAll", "filterPdf", "filterWord", "filterExcel", "filterPowerPoint", "filterImages", "filterOther" };
        private float scale, zoom = 1;
        private readonly float baseScale;
        private readonly Panel listPage = new Panel(), list = new Panel();
        private readonly FilePreview preview;
        private readonly TextInput search = new TextInput(false);
        private readonly FlowLayoutPanel chips = new FlowLayoutPanel();
        private readonly ModernButton folder = new ModernButton(), more = new ModernButton(), sort = new ModernButton(), subjects = new ModernButton();
        private readonly ContextMenuStrip sortMenu = new ContextMenuStrip(), subjectMenu = new ContextMenuStrip();
        private readonly Label empty = new Label();
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
                var chip = new ModernButton { Look = ButtonLook.Outline, Height = D(34), Tag = kind, BackColor = Theme.Background, Font = Theme.Font(Theme.Meta) };
                chip.Margin = new Padding(0, 0, D(6), D(6));
                chip.Click += (s, e) => { filter = (FileKind?)((ModernButton)s).Tag; limit = PageSize; UpdateChips(); Rebuild(); MotionSurface.Reveal(list); };
                chips.Controls.Add(chip);
            }
            folder.Look = ButtonLook.Outline; folder.Icon = UiIcon.Folder; folder.BackColor = Theme.Background; folder.Click += (s, e) => OpenFolder?.Invoke();
            // Sorting shares the filter row: the chosen order stays visible on the button itself.
            sort.Look = ButtonLook.Outline; sort.Icon = UiIcon.Sort; sort.BackColor = Theme.Background; sort.Font = Theme.Font(Theme.Meta); sort.Height = D(34);
            sort.Click += (s, e) => sortMenu.Show(sort, new Point(0, sort.Height));
            StyleMenu(sortMenu);
            foreach (var value in new[] { DownloadSort.Newest, DownloadSort.Name, DownloadSort.Size })
            { var item = new ToolStripMenuItem { Tag = value }; item.Click += (s, e) => SetOrder((DownloadSort)((ToolStripMenuItem)s).Tag); sortMenu.Items.Add(item); }
            // Courses come from the list itself: each one the files were downloaded from, with its file count.
            subjects.Look = ButtonLook.Outline; subjects.Icon = UiIcon.Book; subjects.BackColor = Theme.Background; subjects.Font = Theme.Font(Theme.Meta); subjects.Height = D(34); subjects.Visible = false;
            subjects.Click += (s, e) => { FillSubjects(); subjectMenu.Show(subjects, new Point(0, subjects.Height)); };
            StyleMenu(subjectMenu);
            list.AutoScroll = true; list.BackColor = Theme.Background; list.Resize += (s, e) => LayoutRows();
            empty.ForeColor = Theme.Muted; empty.Font = Theme.Font(Theme.Body); empty.TextAlign = ContentAlignment.TopCenter; empty.AutoSize = false; empty.BackColor = Theme.Background;
            more.Look = ButtonLook.Outline; more.BackColor = Theme.Background; more.Click += (s, e) => { limit += PageSize; Rebuild(); MotionSurface.Reveal(list); };
            list.Controls.Add(empty); list.Controls.Add(more);
            foreach (var control in new Control[] { search, chips, subjects, sort, folder, list }) listPage.Controls.Add(control);
            listPage.Resize += (s, e) => ArrangeList();
            SetLanguage(language);
        }
        private static void StyleMenu(ContextMenuStrip menu)
        {
            menu.Font = Theme.Font(Theme.Body); menu.ShowImageMargin = false; menu.ShowCheckMargin = true; Theme.Style(menu);
            Motion.FadeIn(menu);
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
        internal ChromiumWebBrowser ViewerForQa { get { return preview.ViewerForQa; } }
        internal bool OfficeForQa { get { return preview.OfficeForQa; } }
        internal bool ReaderForQa { get { return preview.ReaderForQa; } }
        internal string MessageForQa { get { return preview.MessageForQa; } }
        internal TextBox SearchForQa { get { return search.Input; } }
        internal string CueForQa { get { return search.Cue; } }
        internal DownloadSort SortForQa { get { return order; } set { SetOrder(value); } }
        internal string SubjectForQa { get { return subject; } set { SetSubject(value); } }
        internal string[] SubjectsForQa { get { return Subjects().Select(x => x.Key + "=" + x.Value).ToArray(); } }
        internal ModernButton SubjectButtonForQa { get { return subjects; } }
        internal bool FilterRowFitsForQa { get { return !hasSubjects || (subjects.Right <= sort.Left && chips.Right <= subjects.Left && subjects.Top == sort.Top); } }
        internal string[] RowOrderForQa { get { return rows.Values.OrderBy(r => (int)r.Tag).Select(r => r.AccessibleName).ToArray(); } }
        internal int RowsForQa { get { return rows.Count; } }
        internal bool ActionsPlacedForQa(string id) { DownloadRow row; return rows.TryGetValue(id, out row) && row.ActionsPlacedForQa; }
        // Follows the app zoom: sizes come from the zoomed scale, fonts are scaled from their original sizes.
        public void SetZoom(float value)
        {
            value = Math.Max(0.5f, Math.Min(2f, value));
            if (Math.Abs(zoom - value) < 0.001f) return;
            zoom = value; scale = baseScale * zoom;
            foreach (Control chip in chips.Controls) { chip.Height = D(34); chip.Margin = new Padding(0, 0, D(6), D(6)); }
            sort.Height = subjects.Height = D(34);
            foreach (var row in rows.Values) row.Dispose();
            rows.Clear();
            UiZoom.ApplyFonts(this, zoom); preview.SetZoom(zoom); UpdateChips(); Rebuild(); ArrangeList();
        }
        public void SetLanguage(string value)
        {
            language = value;
            search.Input.AccessibleName = search.Cue = Strings.Get("downloadsSearch", language);
            for (var i = 0; i < chips.Controls.Count; i++)
            {
                var chip = (ModernButton)chips.Controls[i]; chip.Text = chip.AccessibleName = Strings.Get(FilterNames[i], language);
                chip.Width = TextRenderer.MeasureText(chip.Text, chip.Font).Width + D(30);
            }
            foreach (ToolStripMenuItem item in sortMenu.Items) item.Text = Strings.Get(SortKey((DownloadSort)item.Tag), language);
            UpdateSort(); UpdateSubject();
            folder.Text = folder.AccessibleName = Strings.Get("openFolder", language);
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
            if (index >= 0) items[index] = item.Clone(); else items.Insert(0, item.Clone());
            DownloadRow row;
            if (index >= 0 && rows.TryGetValue(item.Id, out row) && FileKinds.Matches(item, search.Input.Text, filter, subject)) row.Update(item.Clone());
            // Progress of a file the filter or page hides changes neither what is shown nor its order (sorting uses the total).
            else if (before != null && !rows.ContainsKey(item.Id) && before.State == item.State && before.FileName == item.FileName && before.Total == item.Total) return;
            else Rebuild();
        }
        public void Remove(string id) { items.RemoveAll(x => x.Id == id); Rebuild(); MotionSurface.Reveal(list, -1); }
        public bool ClearSearch() { if (Previewing || search.Input.TextLength == 0) return false; search.Input.Clear(); return true; }
        // Refresh re-checks files on disk (moved or deleted outside the app) or reopens the shown file.
        public void RefreshView() { if (Previewing && preview.Item != null) Preview(preview.Item); else Rebuild(); }
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
        internal void ApplyTheme() { Theme.Style(sortMenu); Theme.Style(subjectMenu); preview.ApplyTheme(); Invalidate(true); }
        private void UpdateChips()
        {
            foreach (ModernButton chip in chips.Controls) { chip.Selected = Equals(chip.Tag, filter); chip.Invalidate(); }
        }
        private static string SortKey(DownloadSort value)
        { return value == DownloadSort.Name ? "sortName" : value == DownloadSort.Size ? "sortSize" : "sortNewest"; }
        private void SetOrder(DownloadSort value)
        {
            if (order == value) return;
            order = value; UpdateSort(); ArrangeList(); Rebuild(); MotionSurface.Reveal(list);
        }
        private void UpdateSort()
        {
            sort.Text = Strings.Get(SortKey(order), language);
            sort.AccessibleName = string.Format(Strings.Get("sortLabel", language), sort.Text);
            foreach (ToolStripMenuItem item in sortMenu.Items) item.Checked = Equals(item.Tag, order);
            sort.Width = TextRenderer.MeasureText(sort.Text, sort.Font).Width + D(54); sort.Invalidate();
        }
        private List<KeyValuePair<string, int>> Subjects()
        {
            return items.Where(x => !string.IsNullOrWhiteSpace(x.Source)).GroupBy(x => x.Source.Trim(), StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToList();
        }
        private void FillSubjects()
        {
            subjectMenu.Items.Clear();
            var all = new ToolStripMenuItem(Strings.Get("subjectAll", language)) { Checked = subject == null, ShortcutKeyDisplayString = items.Count.ToString() };
            all.Click += (s, e) => SetSubject(null); subjectMenu.Items.Add(all);
            var known = Subjects(); if (known.Count > 0) subjectMenu.Items.Add(new ToolStripSeparator());
            foreach (var pair in known)
            {
                var name = pair.Key;
                var item = new ToolStripMenuItem(name) { Checked = string.Equals(name, subject, StringComparison.CurrentCultureIgnoreCase), ShortcutKeyDisplayString = pair.Value.ToString() };
                item.Click += (s, e) => SetSubject(name); subjectMenu.Items.Add(item);
            }
        }
        private void SetSubject(string value)
        {
            if (string.Equals(subject, value, StringComparison.CurrentCultureIgnoreCase)) return;
            subject = value; limit = PageSize; UpdateSubject(); ArrangeList(); Rebuild(); MotionSurface.Reveal(list);
        }
        // The button names the chosen course; a long name is shortened on the button, never in its accessible name.
        // Returns true when the filter row needs arranging again.
        private bool UpdateSubject()
        {
            var known = Subjects();
            if (subject != null && !known.Any(x => string.Equals(x.Key, subject, StringComparison.CurrentCultureIgnoreCase))) subject = null;
            var name = subject ?? Strings.Get("subjectAll", language);
            subjects.AccessibleName = string.Format(Strings.Get("filterSubject", language), name);
            var shown = name; var room = D(240);
            while (shown.Length > 4 && TextRenderer.MeasureText(shown, subjects.Font).Width > room) shown = shown.Substring(0, shown.Length - 2).TrimEnd() + "…";
            var width = TextRenderer.MeasureText(shown, subjects.Font).Width + D(54);
            var changed = hasSubjects != known.Count > 0 || subjects.Width != width;
            hasSubjects = known.Count > 0;
            subjects.Text = shown; subjects.Width = width; subjects.Visible = hasSubjects; subjects.Selected = subject != null; subjects.Invalidate();
            return changed;
        }
        private void ArrangeList()
        {
            var pad = D(24); var width = Math.Max(D(200), listPage.ClientSize.Width - pad * 2);
            // The window header already names the page: search and the folder button share the first row.
            folder.Width = Math.Max(D(150), TextRenderer.MeasureText(folder.Text, folder.Font).Width + D(54));
            folder.SetBounds(pad + width - folder.Width, D(23), folder.Width, D(36));
            search.SetBounds(pad, D(20), Math.Max(D(160), Math.Min(width - folder.Width - D(16), D(460))), D(42));
            sort.SetBounds(pad + width - sort.Width, D(74), sort.Width, D(34));
            var right = sort.Left;
            if (hasSubjects) { subjects.SetBounds(sort.Left - D(8) - subjects.Width, D(74), subjects.Width, D(34)); right = subjects.Left; }
            var chipsWidth = Math.Max(D(120), right - pad - D(12));
            chips.SetBounds(pad, D(74), chipsWidth, D(84));
            chips.Height = chips.GetPreferredSize(new Size(chipsWidth, 0)).Height;
            var top = chips.Bottom + D(2);
            list.SetBounds(0, top, listPage.ClientSize.Width, Math.Max(0, listPage.ClientSize.Height - top));
            LayoutRows();
        }
        private void Rebuild()
        {
            if (IsDisposed) return;
            if (UpdateSubject()) ArrangeList();
            var visible = DownloadOrder.Apply(items.Where(x => FileKinds.Matches(x, search.Input.Text, filter, subject)), order);
            var shown = visible.Take(limit).ToList();
            list.SuspendLayout();
            var kept = new HashSet<string>(shown.Select(x => x.Id));
            foreach (var id in rows.Keys.Where(id => !kept.Contains(id)).ToList()) { rows[id].Dispose(); rows.Remove(id); }
            for (var index = 0; index < shown.Count; index++)
            {
                var item = shown[index]; DownloadRow row;
                if (!rows.TryGetValue(item.Id, out row))
                {
                    row = new DownloadRow(scale, language); UiZoom.ApplyFonts(row, zoom);
                    row.OpenInside += x => Preview(x); row.OpenOutside += x => OpenOutside?.Invoke(x); row.ShowInFolder += x => ShowInFolder?.Invoke(x);
                    row.CancelDownload += x => CancelDownload?.Invoke(x); row.RemoveRecord += x => RemoveRecord?.Invoke(x);
                    rows[item.Id] = row; list.Controls.Add(row);
                }
                row.Update(item); row.Tag = index;
            }
            empty.Text = Strings.Get(items.Count == 0 ? "downloadsEmpty" : "downloadsNoMatch", language);
            empty.Visible = shown.Count == 0;
            more.Visible = visible.Count > shown.Count;
            list.ResumeLayout(); LayoutRows();
            ViewChanged?.Invoke();
        }
        private void LayoutRows()
        {
            // Rows stay two text lines tall; a denser list shows more files on a laptop screen.
            var pad = D(24); var width = Math.Max(D(200), list.ClientSize.Width - pad * 2); var y = D(8) + list.AutoScrollPosition.Y; var height = D(66);
            foreach (var row in rows.Values.OrderBy(r => (int)r.Tag)) { row.SetBounds(pad, y, width, height); y += height + D(6); }
            empty.SetBounds(pad, D(40) + list.AutoScrollPosition.Y, width, D(60));
            more.SetBounds(pad + (width - D(200)) / 2, y + D(4), D(200), D(36));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { sortMenu.Dispose(); subjectMenu.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
