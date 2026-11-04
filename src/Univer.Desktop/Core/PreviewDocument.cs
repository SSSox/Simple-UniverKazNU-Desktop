using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace Univer.Desktop.Core
{
    // What the bundled renderers need besides the file: the page title, the interface language and the localized hints
    // they may show.
    internal sealed class PreviewOptions
    {
        private readonly IDictionary<string, string> labels;
        public PreviewOptions(string title, string tableHint = null, string omittedImage = null, string language = null, IDictionary<string, string> labels = null)
        {
            Title = title ?? ""; TableHint = tableHint; OmittedImage = omittedImage;
            Language = language == "kz" || language == "en" ? language : "ru";
            this.labels = labels ?? new Dictionary<string, string>();
        }
        public string Title { get; }
        // Shown above the first table: how to scroll a wide table.
        public string TableHint { get; }
        // Shown in place of a picture that is too large to show.
        public string OmittedImage { get; }
        public string Language { get; }
        // Numbers and dates in workbooks read as Excel shows them for this language: "1 234,5" and 31.12.2025 for Russian
        // and Kazakh, "1,234.5" and 12/31/2025 for English.
        public CultureInfo Culture { get { return CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : "ru-RU"); } }
        // Short interface texts of workbooks, slides and web pages (sheet list, slide numbers, notes); empty when not given.
        public string Label(string key) { string value; return labels.TryGetValue(key, out value) ? value ?? "" : ""; }
    }
    internal sealed class PreviewImage
    {
        public PreviewImage(string path, string mimeType, byte[] data) { Path = path; MimeType = mimeType; Data = data; }
        // Relative to the page, for example "images/3.png".
        public string Path { get; }
        public string MimeType { get; }
        public byte[] Data { get; }
    }
    // A converted document: its page and the pictures it shows. The viewer serves each picture beside the page, so
    // pictures neither inflate the markup nor count against its size limit, and the text stays searchable.
    internal sealed class PreviewDocument
    {
        public PreviewDocument(string html, IList<PreviewImage> images) { Html = html; Images = images; }
        public string Html { get; }
        public IList<PreviewImage> Images { get; }
    }
    // One document's pictures within a size budget: one past it becomes a short note, a repeated one is kept once.
    internal sealed class PreviewImages
    {
        public const long ImageLimit = 16L * 1024 * 1024, TotalLimit = 128L * 1024 * 1024;
        private readonly List<PreviewImage> items = new List<PreviewImage>();
        private readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
        private long total;
        public IList<PreviewImage> Items { get { return items; } }
        public bool Fits(long length) { return length > 0 && length <= ImageLimit && total + length <= TotalLimit; }
        // The address of a picture already added under this key (its place in the file), or null.
        public string Find(string key) { string path; return paths.TryGetValue(key, out path) ? path : null; }
        // Returns the picture's address relative to the page, or null when it does not fit.
        public string Add(string key, byte[] data, string mimeType)
        {
            if (data == null || !Fits(data.Length)) return null;
            total += data.Length;
            var path = "images/" + (items.Count + 1) + Extension(mimeType);
            items.Add(new PreviewImage(path, mimeType, data));
            paths[key] = path;
            return path;
        }
        // Writes the picture at path, or the note that stands in for one that did not fit (path null).
        public static void Write(PreviewMarkup html, string path, long widthPixels, PreviewOptions options)
        {
            if (path == null)
            {
                if (!string.IsNullOrEmpty(options.OmittedImage)) html.Append("<span class=\"omitted\">").Append(WebUtility.HtmlEncode(options.OmittedImage)).Append("</span>");
                return;
            }
            html.Append("<img alt=\"\"");
            if (widthPixels > 0 && widthPixels <= 4000) html.Append(" style=\"width:").Append((int)widthPixels).Append("px\"");
            html.Append(" src=\"").Append(path).Append("\">");
        }
        private static string Extension(string mimeType)
        {
            switch (mimeType)
            {
                case "image/png": return ".png";
                case "image/jpeg": return ".jpg";
                case "image/gif": return ".gif";
                case "image/bmp": return ".bmp";
                default: throw new ArgumentException("Unsupported picture type", "mimeType");
            }
        }
    }
}
