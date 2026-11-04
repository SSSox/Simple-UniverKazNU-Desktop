using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The viewer of one downloaded file: a slim bar over PDF.js, converted Office/web/text pages or a picture, all in one
    // private Chromium browser so find, zoom, print and theme work alike. Unreadable Office files fall back to preview handlers.
    internal sealed class FilePreview : Panel
    {
        public event Action Back, ContentChanged;
        public event Action<DownloadRecord> OpenOutside, ShowInFolder;
        public event Action<Keys> Shortcut;
        private readonly Panel bar = new Panel(), host = new Panel();
        private readonly Label title = new Label(), message = new Label();
        private readonly ModernButton back = new ModernButton(), outside = new ModernButton(), folder = new ModernButton();
        private readonly ToolTip tips = new ToolTip();
        private readonly LocalFiles files = new LocalFiles();
        private readonly Action<string> external;
        // Device pixels per CSS pixel at 100% zoom; the PDF reader tells the app's zoom from the screen's DPI with it.
        private readonly float screenScale;
        private float scale, zoom = 1;
        private RequestContext filesContext;
        private ChromiumWebBrowser viewer;
        private PreviewHost office;
        // A closed preview keeps its Chromium browser briefly for quick reopening, then releases its renderer.
        private readonly System.Windows.Forms.Timer viewerIdle = new System.Windows.Forms.Timer { Interval = 45000 };
        private DownloadRecord shown;
        private CancellationTokenSource preparation;
        private int generation;
        private bool reader;
        private string language = "ru";
        public FilePreview(float scale, Action<string> external)
        {
            this.scale = screenScale = scale; this.external = external;
            Dock = DockStyle.Fill; BackColor = Theme.Background; Visible = false; Theme.Tips(tips);
            bar.Dock = DockStyle.Top; bar.Height = D(56); bar.BackColor = Theme.Surface;
            bar.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1); };
            back.Look = ButtonLook.Outline; back.Icon = UiIcon.Back; back.Click += (s, e) => Back?.Invoke();
            outside.Look = ButtonLook.Primary; outside.Icon = UiIcon.External; outside.Click += (s, e) => { if (shown != null) OpenOutside?.Invoke(shown); };
            folder.Look = ButtonLook.Outline; folder.Icon = UiIcon.Folder; folder.Click += (s, e) => { if (shown != null) ShowInFolder?.Invoke(shown); };
            title.Font = Theme.Strong(Theme.Emphasis); title.ForeColor = Theme.Ink; title.AutoEllipsis = true; title.UseMnemonic = false; title.TextAlign = ContentAlignment.MiddleLeft;
            foreach (var control in new Control[] { back, title, outside, folder }) bar.Controls.Add(control);
            bar.Resize += (s, e) => ArrangeBar();
            host.Dock = DockStyle.Fill; host.BackColor = Theme.Background;
            message.Dock = DockStyle.Fill; message.TextAlign = ContentAlignment.MiddleCenter; message.ForeColor = Theme.Muted; message.Font = Theme.Font(Theme.Emphasis); message.Visible = false;
            host.Controls.Add(message);
            Controls.Add(host); Controls.Add(bar);
            viewerIdle.Tick += (s, e) => ReleaseViewer();
        }
        private int D(int value) { return (int)Math.Round(value * scale); }
        // Control.Visible reads false while the downloads page itself is hidden, so the preview keeps its own state.
        public bool Showing { get; private set; }
        public DownloadRecord Item { get { return shown; } }
        public bool PreferBuiltIn { get; set; }
        public IFindHandler FindHandler { get; set; }
        // Find works in every file shown by Chromium; Windows preview handlers for Office files have no find of their own.
        public ChromiumWebBrowser FindTarget { get { return Showing && viewer != null && viewer.Visible && !message.Visible ? viewer : null; } }
        internal ChromiumWebBrowser ViewerForQa { get { return viewer; } }
        internal bool OfficeForQa { get { return office != null && office.Visible; } }
        internal bool ReaderForQa { get { return reader; } }
        internal string MessageForQa { get { return message.Visible ? message.Text : ""; } }
        // The file's own bar; the find bar opens below it.
        public int BarHeight { get { return bar.Height; } }
        internal void Reveal() { MotionSurface.Reveal(bar); }
        internal void FocusBack() { back.Focus(); }
        public void SetLanguage(string value)
        {
            language = value;
            back.Text = back.AccessibleName = Strings.Get("backToDownloads", language);
            outside.Text = outside.AccessibleName = Strings.Get("openOutside", language);
            folder.Text = folder.AccessibleName = Strings.Get("showInFolder", language);
            ArrangeBar();
        }
        // value: the app zoom (1 = 100%). It sizes this bar and applies to the shown file, as on the website.
        public void SetZoom(float value)
        {
            scale = screenScale * value; zoom = value; bar.Height = D(56);
            ArrangeBar(); ApplyViewerZoom();
        }
        private void ApplyViewerZoom()
        {
            if (viewer != null && !viewer.IsDisposed && viewer.IsBrowserInitialized) viewer.SetZoomLevel(Math.Log(zoom, 1.2));
        }
        public async Task ShowAsync(DownloadRecord item)
        {
            CancelPreparation(); var current = generation;
            preparation = new CancellationTokenSource(); var cancellation = preparation.Token;
            shown = item.Clone(); reader = false; title.Text = item.FileName;
            Showing = true; Visible = true; BringToFront(); ArrangeBar();
            ShowMessage(Strings.Get("previewPreparing", language)); ContentChanged?.Invoke();
            Reveal();
            if (!File.Exists(item.Path)) { ShowMessage(Strings.Get("stateMissing", language)); ContentChanged?.Invoke(); return; }
            var kind = FileKinds.Of(item.Path);
            var extension = Path.GetExtension(item.Path).ToLowerInvariant();
            Func<string, PreviewOptions, CancellationToken, PreviewDocument> convert;
            try
            {
                if (kind == FileKind.Pdf) ShowReader(item);
                else if (kind == FileKind.Image && extension != ".bmp")
                {
                    var mime = Cef.GetMimeType(extension.TrimStart('.'));
                    Show(item, new PreviewContent(() => ResourceHandler.FromFilePath(item.Path, mime, true)));
                }
                else if (kind == FileKind.Text || extension == ".csv")
                {
                    var text = await Task.Run(() => PreviewText.Read(item.Path, cancellation), cancellation);
                    if (!Current(current, cancellation)) return;
                    var page = PreviewPage.Text(item.FileName, text.Text, text.Truncated ? Strings.Get("previewTextTruncated", language) : null);
                    Show(item, new PreviewContent(() => LocalFiles.Page(PreviewTheme.Apply(page, Theme.Dark), LocalFiles.DocumentPolicy)));
                }
                // DOCX always uses the bundled renderer, even when Office is installed.
                else if (extension == ".docx" || extension == ".docm" || extension == ".dotx")
                {
                    var options = Options(item);
                    var document = await Task.Run(() => DocxHtml.Convert(item.Path, options, cancellation), cancellation);
                    if (!Current(current, cancellation)) return;
                    ShowDocument(item, document);
                }
                // Older Office files and saved web pages use the bundled look; a preview handler is only the fallback for files the
                // renderers cannot read (Word 95, Excel 95, encrypted or damaged).
                else if ((convert = Converter(extension)) != null)
                {
                    var options = Options(item);
                    PreviewDocument document = null;
                    try { document = await Task.Run(() => convert(item.Path, options, cancellation), cancellation); }
                    catch (InvalidDataException)
                    {
                        if (!Current(current, cancellation)) return;
                        if (PreferBuiltIn || kind == FileKind.Html || !PreviewHost.HandlerFor(item.Path).HasValue || !ShowOffice(item.Path)) throw;
                    }
                    if (!Current(current, cancellation)) return;
                    if (document != null) ShowDocument(item, document); else ShowMessage(null);
                }
                else if (!PreferBuiltIn && (kind == FileKind.Word || kind == FileKind.Excel || kind == FileKind.PowerPoint) &&
                    PreviewHost.HandlerFor(item.Path).HasValue && ShowOffice(item.Path)) ShowMessage(null);
                else ShowMessage(Strings.Get("previewUnsupported", language));
            }
            catch (OperationCanceledException) { return; }
            catch (PreviewLimitException) { if (Current(current, cancellation)) ShowMessage(Strings.Get("previewLimit", language)); }
            catch (Exception) { if (Current(current, cancellation)) ShowMessage(Strings.Get("previewFailed", language)); }
            if (Current(current, cancellation)) ContentChanged?.Invoke();
        }
        // The bundled renderer for a file extension, or null. ZIP-based formats are read in place; the others whole.
        private static Func<string, PreviewOptions, CancellationToken, PreviewDocument> Converter(string extension)
        {
            switch (extension)
            {
                case ".doc": case ".dot": return DocHtml.Convert;
                case ".xlsx": case ".xlsm": case ".xltx": case ".xltm": return XlsxHtml.Convert;
                case ".xls": case ".xlt": return XlsHtml.Convert;
                case ".pptx": case ".pptm": case ".ppsx": case ".ppsm": case ".potx": case ".potm": return PptxHtml.Convert;
                case ".ppt": case ".pps": case ".pot": return PptHtml.Convert;
                case ".html": case ".htm": case ".xhtml": return HtmlPreview.Convert;
                default: return null;
            }
        }
        private PreviewOptions Options(DownloadRecord item)
        {
            var labels = new Dictionary<string, string>
            {
                { "sheets", Strings.Get("sheetList", language) }, { "sheetEmpty", Strings.Get("sheetEmpty", language) },
                { "sheetTruncated", Strings.Get("sheetTruncated", language) }, { "slide", Strings.Get("slideNumber", language) },
                { "slideEmpty", Strings.Get("slideEmpty", language) }, { "htmlNote", Strings.Get("htmlNote", language) }
            };
            return new PreviewOptions(item.FileName, Strings.Get("docxTableHint", language), Strings.Get("previewImageOmitted", language), language, labels);
        }
        // The page is themed each time it is served, so a reload after a theme change shows the new colours.
        private void ShowDocument(DownloadRecord item, PreviewDocument document)
        {
            var html = document.Html;
            Show(item, new PreviewContent(() => LocalFiles.Page(PreviewTheme.Apply(html, Theme.Dark), LocalFiles.DocumentPolicy)).With(document));
        }
        private void ShowReader(DownloadRecord item)
        {
            var strings = new Dictionary<string, string>
            {
                { "loading", Strings.Get("readerLoading", language) }, { "noText", Strings.Get("readerNoText", language) },
                { "page", Strings.Get("readerPage", language) }, { "printing", Strings.Get("readerPrinting", language) },
                { "link", Strings.Get("readerLink", language) }
            };
            var name = item.FileName; var shownLanguage = language; var path = item.Path;
            Show(item, new PreviewContent(() => LocalFiles.Page(PdfReaderPage.Html(name, shownLanguage, screenScale, strings, Theme.Dark), LocalFiles.ReaderPolicy))
                .With(PreviewRoutes.FileKey(item.FileName), () => ResourceHandler.FromFilePath(path, "application/pdf", true)));
            reader = true;
        }
        // After a theme change a converted page is served again in the new colours (a reload keeps its scroll position);
        // the PDF reader switches its own page without reloading the document.
        internal void ApplyTheme()
        {
            if (!Showing || viewer == null || viewer.IsDisposed || !viewer.IsBrowserInitialized || !viewer.Visible || message.Visible) return;
            if (!reader) { viewer.Reload(); return; }
            using (var frame = viewer.GetMainFrame())
                if (frame != null && frame.IsValid && LocalFiles.Owns(frame.Url)) frame.ExecuteJavaScriptAsync("document.documentElement.classList.toggle('dark'," + (Theme.Dark ? "true" : "false") + ")");
        }
        private bool Current(int expected, CancellationToken cancellation)
        { return !IsDisposed && !Disposing && !cancellation.IsCancellationRequested && expected == generation; }
        private void CancelPreparation()
        {
            generation++;
            if (preparation == null) return;
            preparation.Cancel(); preparation.Dispose(); preparation = null;
        }
        private void Post(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) action(); })); }
            catch (InvalidOperationException) { }
        }
        private void Show(DownloadRecord item, PreviewContent content)
        {
            ShowMessage(null); HideOffice(); viewerIdle.Stop();
            if (viewer == null || viewer.IsDisposed)
            {
                filesContext = new RequestContext(new RequestContextSettings());
                filesContext.RegisterSchemeHandlerFactory("https", LocalFiles.Host, files);
                viewer = new ChromiumWebBrowser("about:blank", filesContext) { Dock = DockStyle.Fill };
                var surface = Theme.Background; viewer.BrowserSettings.BackgroundColor = Cef.ColorSetARGB(255, surface.R, surface.G, surface.B);
                viewer.RequestHandler = new ViewerRequestHandler(link => Post(() => external(link)));
                viewer.LifeSpanHandler = new ViewerLifeSpan(link => Post(() => external(link)));
                viewer.KeyboardHandler = new ShortcutHandler(key => Post(() => Shortcut?.Invoke(key)), true);
                viewer.DownloadHandler = new NoDownloads();
                viewer.MenuHandler = new NoContextMenu();
                if (FindHandler != null) viewer.FindHandler = FindHandler;
                // Chromium keeps zoom per host; the shown file follows the app zoom once its page has committed.
                viewer.FrameLoadStart += (s, e) => { if (e.Frame.IsMain) Post(ApplyViewerZoom); };
                host.Controls.Add(viewer);
            }
            viewer.Visible = true; viewer.BringToFront();
            var url = files.Publish(item.FileName, content);
            var expected = generation; var browser = viewer;
            if (browser.IsBrowserInitialized) browser.Load(url);
            else
            {
                EventHandler ready = null;
                ready = (s, e) =>
                {
                    browser.IsBrowserInitializedChanged -= ready;
                    Post(() => { if (expected == generation && shown?.Id == item.Id && Showing && browser == viewer && !browser.IsDisposed) browser.Load(url); });
                };
                browser.IsBrowserInitializedChanged += ready;
            }
        }
        private bool ShowOffice(string path)
        {
            if (viewer != null) { viewer.Visible = false; files.Clear(); }
            if (office == null)
            {
                office = new PreviewHost { Dock = DockStyle.Fill, BackColor = Theme.Surface }; host.Controls.Add(office);
                office.Shortcut += key => Shortcut?.Invoke(key);
            }
            office.Visible = true; office.BringToFront();
            if (office.Open(path)) return true;
            HideOffice(); return false;
        }
        private void HideOffice() { if (office != null) { office.Unload(); office.Visible = false; } }
        private void ShowMessage(string text)
        {
            message.Text = text ?? ""; message.Visible = text != null;
            if (text != null) { HideOffice(); if (viewer != null) viewer.Visible = false; message.BringToFront(); }
        }
        // Returns false when no file was shown.
        public bool Close()
        {
            CancelPreparation();
            if (!Showing) return false;
            Showing = false; HideOffice(); files.Clear();
            if (viewer != null && viewer.IsBrowserInitialized) viewer.Load("about:blank");
            if (viewer != null) { viewerIdle.Stop(); viewerIdle.Start(); }
            shown = null; reader = false; Visible = false;
            MotionSurface.Stop(bar);
            return true;
        }
        // Frees the preview browser and its private request context (its renderer process) while no file is shown.
        internal void ReleaseViewer()
        {
            viewerIdle.Stop();
            if (viewer == null || Showing) return;
            // A browser still being created is released on the next idle tick: disposing it and its request context
            // mid-creation (a preview closed right after it opened) can end the whole process.
            if (!viewer.IsBrowserInitialized) { viewerIdle.Start(); return; }
            host.Controls.Remove(viewer); viewer.Dispose(); viewer = null;
            filesContext?.Dispose(); filesContext = null;
        }
        public bool Print()
        {
            if (!Showing || viewer == null || !viewer.Visible || !viewer.IsBrowserInitialized) return false;
            if (reader) _ = PrintReaderAsync(viewer); else viewer.Print();
            return true;
        }
        // The reader prints pictures of its pages, drawn at print resolution first. A file PDF.js could not open is in
        // Chromium's own viewer instead, which prints the PDF itself.
        private async Task PrintReaderAsync(ChromiumWebBrowser browser)
        {
            using (var frame = browser.GetMainFrame())
            {
                if (frame == null || !frame.IsValid || !LocalFiles.Owns(frame.Url)) return;
                bool reading;
                // A reader busy with a long file may miss the deadline; it then prints once it is free.
                try
                {
                    var response = await frame.EvaluateScriptAsync("typeof window.__univerReader==='object'", timeout: TimeSpan.FromSeconds(3));
                    reading = response.Success && response.Result is bool && (bool)response.Result;
                }
                catch (TaskCanceledException) { reading = true; }
                if (browser.IsDisposed || browser != viewer || !Showing || !frame.IsValid) return;
                if (reading) frame.ExecuteJavaScriptAsync("window.__univerReader?window.__univerReader.print():window.print()");
                else browser.Print();
            }
        }
        private void ArrangeBar()
        {
            var pad = D(16); var top = D(10); var height = D(36);
            back.Width = TextRenderer.MeasureText(back.Text, back.Font).Width + D(56); back.SetBounds(pad, top, back.Width, height);
            // The file name keeps at least a readable width; the folder action then shows only its icon.
            var folderText = TextRenderer.MeasureText(folder.Text, folder.Font).Width + D(56);
            var outsideText = TextRenderer.MeasureText(outside.Text, outside.Font).Width + D(56);
            folder.Compact = bar.Width - pad * 2 - back.Width - folderText - outsideText - D(32) < D(180);
            tips.SetToolTip(folder, folder.Compact ? folder.Text : "");
            folder.Width = folder.Compact ? D(40) : folderText;
            outside.Width = outsideText;
            folder.SetBounds(bar.Width - pad - folder.Width, top, folder.Width, height);
            outside.SetBounds(folder.Left - D(8) - outside.Width, top, outside.Width, height);
            title.SetBounds(back.Right + D(12), top, Math.Max(D(40), outside.Left - back.Right - D(24)), height);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { CancelPreparation(); viewerIdle.Dispose(); files.Clear(); office?.Dispose(); viewer?.Dispose(); filesContext?.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
        }
        private sealed class NoContextMenu : CefSharp.Handler.ContextMenuHandler
        {
            protected override void OnBeforeContextMenu(IWebBrowser chromium, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model) { model.Clear(); }
        }
    }
}
