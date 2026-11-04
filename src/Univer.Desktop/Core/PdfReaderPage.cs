using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // The page of the in-app PDF reader. PDF.js and the reader script (Assets/pdf-reader) draw the pages; this page
    // carries only the file's address, the screen scale and the reader's localized texts.
    internal static class PdfReaderPage
    {
        // screenScale: device pixels per CSS pixel at 100% zoom, so the reader can tell the app's zoom from the screen's DPI.
        // dark: the reader's dark theme (its pages stay paper-white, the PDF's own colours).
        public static string Html(string title, string language, double screenScale, IDictionary<string, string> strings, bool dark = false)
        {
            var config = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "file", PreviewRoutes.FilePath(title) },
                { "scale", screenScale > 0 ? screenScale : 1 },
                { "strings", strings ?? new Dictionary<string, string>() }
            }).Replace("</", "<\\/");
            string loading;
            if (strings == null || !strings.TryGetValue("loading", out loading)) loading = "";
            var name = WebUtility.HtmlEncode(title ?? "");
            return "<!doctype html><html" + (dark ? " class=\"dark\"" : "") + " lang=\"" + (language == "kz" ? "kk" : language == "en" ? "en" : "ru") + "\"><head><meta charset=\"utf-8\">" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>" + name + "</title>" +
                "<link rel=\"stylesheet\" href=\"/assets/pdf-reader/reader.css\">" +
                "<script type=\"application/json\" id=\"reader-config\">" + config + "</script>" +
                "<script type=\"module\" src=\"/assets/pdf-reader/start.mjs\"></script></head>" +
                "<body><main id=\"pages\" aria-label=\"" + name + "\"><p class=\"status\" role=\"status\">" + WebUtility.HtmlEncode(loading) + "</p></main></body></html>";
        }
    }
}
