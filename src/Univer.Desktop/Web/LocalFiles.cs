using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using CefSharp;
using CefSharp.Handler;
using Univer.Desktop.Core;

namespace Univer.Desktop.Web
{
    // One shown file: its page and the resources the page names (see PreviewRoutes).
    internal sealed class PreviewContent
    {
        private readonly Dictionary<string, Func<IResourceHandler>> resources = new Dictionary<string, Func<IResourceHandler>>(StringComparer.Ordinal);
        public PreviewContent(Func<IResourceHandler> page) { Page = page; }
        public Func<IResourceHandler> Page { get; }
        // key: the resource path below the page's token, unescaped, for example "images/2.png".
        public PreviewContent With(string key, Func<IResourceHandler> resource) { resources[key] = resource; return this; }
        public PreviewContent With(PreviewDocument document)
        {
            foreach (var image in document.Images) { var picture = image; With(picture.Path, () => ResourceHandler.FromByteArray(picture.Data, picture.MimeType)); }
            return this;
        }
        public Func<IResourceHandler> Resource(string key) { Func<IResourceHandler> resource; return resources.TryGetValue(key, out resource) ? resource : null; }
    }
    // Serves one-off previews under a reserved HTTPS host in a separate in-memory profile: only viewer tokens and the
    // reader's asset folders resolve, and file:// stays disabled.
    internal sealed class LocalFiles : ISchemeHandlerFactory
    {
        public const string Host = PreviewRoutes.Host;
        // Converted Word documents never need scripts, frames or network access; their pictures come from the same host.
        public const string DocumentPolicy = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'";
        // The PDF reader runs only the app's own scripts and PDF.js (with its WebAssembly image decoders and worker) and
        // reads only this host: the PDF itself and PDF.js's data files.
        public const string ReaderPolicy = "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; worker-src 'self'; connect-src 'self'; " +
            "img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; base-uri 'none'; form-action 'none'";
        private readonly ConcurrentDictionary<string, PreviewContent> items = new ConcurrentDictionary<string, PreviewContent>();
        private readonly string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
        public string Publish(string name, PreviewContent content)
        {
            items.Clear(); // The viewer shows one file at a time; older tokens stop resolving.
            var token = Guid.NewGuid().ToString("N");
            items[token] = content;
            return PreviewRoutes.PageUrl(token, name);
        }
        public void Clear() { items.Clear(); }
        public static bool Owns(string url) { return PreviewRoutes.Owns(url); }
        public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
        {
            // An exception escaping a Chromium callback would end the process; answer with an error page instead.
            try
            {
                Uri uri; string[] segments; PreviewContent content;
                if (!Uri.TryCreate(request.Url, UriKind.Absolute, out uri) || uri.Host != Host || !PreviewRoutes.TrySplit(uri.AbsolutePath, out segments)) return NotFound();
                if (segments[0] == PreviewRoutes.AssetsSegment)
                {
                    var file = PreviewRoutes.AssetFile(assets, segments);
                    return file == null ? NotFound() : Asset(file);
                }
                if (!items.TryGetValue(segments[0], out content)) return NotFound();
                if (segments.Length == 2) return content.Page();
                var resource = content.Resource(string.Join("/", segments, 1, segments.Length - 1));
                return resource == null ? NotFound() : resource();
            }
            catch (Exception) { return ResourceHandler.ForErrorMessage("The file cannot be shown", HttpStatusCode.InternalServerError); }
        }
        public static IResourceHandler Page(string html, string policy)
        {
            var handler = new ResourceHandler("text/html", new MemoryStream(Encoding.UTF8.GetBytes(html)), true, "utf-8");
            handler.Headers.Add("Content-Security-Policy", policy);
            return handler;
        }
        private static IResourceHandler Asset(string file)
        {
            var handler = new ResourceHandler(PreviewRoutes.MimeType(file), File.OpenRead(file), true);
            // PDF.js's worker takes its policy from its own response.
            handler.Headers.Add("Content-Security-Policy", ReaderPolicy);
            handler.Headers.Add("Cache-Control", "max-age=86400");
            return handler;
        }
        private static IResourceHandler NotFound() { return ResourceHandler.ForErrorMessage("Not found", HttpStatusCode.NotFound); }
    }
    // The viewer only shows its own preview, Chromium's PDF viewer and nothing else.
    internal sealed class ViewerRequestHandler : RequestHandler
    {
        private readonly Action<string> external;
        public ViewerRequestHandler(Action<string> external) { this.external = external; }
        protected override bool OnBeforeBrowse(IWebBrowser chromium, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
        {
            var url = request.Url ?? "";
            if (LocalFiles.Owns(url) || url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) || url == "about:blank" || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return false;
            if (frame.IsMain && userGesture && SitePolicy.External(url)) external(url);
            return true;
        }
        protected override bool OnCertificateError(IWebBrowser chromium, IBrowser browser, CefErrorCode error, string url, ISslInfo info, IRequestCallback callback) { return false; }
    }
    internal sealed class ViewerLifeSpan : LifeSpanHandler
    {
        private readonly Action<string> external;
        public ViewerLifeSpan(Action<string> external) { this.external = external; }
        protected override bool DoClose(IWebBrowser chromium, IBrowser browser) { return true; }
        protected override bool OnBeforePopup(IWebBrowser chromium, IBrowser browser, IFrame frame, string url, string name, WindowOpenDisposition disposition, bool userGesture,
            IPopupFeatures features, IWindowInfo windowInfo, IBrowserSettings settings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
        { newBrowser = null; if (userGesture && SitePolicy.External(url)) external(url); return true; }
    }
    // The file is already local: Chromium's own PDF "download" never writes anywhere.
    internal sealed class NoDownloads : DownloadHandler
    {
#if LEGACY_ENGINE
        protected override void OnBeforeDownload(IWebBrowser chromium, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback) { }
#else
        protected override bool OnBeforeDownload(IWebBrowser chromium, IBrowser browser, DownloadItem item, IBeforeDownloadCallback callback) { return true; }
#endif
        protected override void OnDownloadUpdated(IWebBrowser chromium, IBrowser browser, DownloadItem item, IDownloadItemCallback callback)
        { if (item.IsInProgress && !callback.IsDisposed) callback.Cancel(); }
    }
}
