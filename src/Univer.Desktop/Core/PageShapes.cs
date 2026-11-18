using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // The geometry of a site page as it was last shown, so the next visit's loading placeholder has the page's real layout.
    // Rectangles in CSS pixels only: kind (0 box, 1 text or picture), x, y, width, height, radius, plate, edge.
    internal sealed class PageShape
    {
        // 2: bars stuck to the window's bottom are left out (page-shape.js).
        public const int Version = 2;
        public const int MaxItems = 700;
        // The page's own width (without the scroll bar), the window's inner width and its height, all in CSS pixels.
        public int Width { get; set; }
        public int Outer { get; set; }
        public int Height { get; set; }
        public List<int[]> Items { get; set; } = new List<int[]>();
        // Whether the shape was taken in a window of this inner width (CSS pixels): a narrower or wider window lays the page out anew.
        public bool Fits(float outerWidth) { return Math.Abs(outerWidth - Outer) <= 3; }
        // The page script's answer, checked: anything malformed is no shape at all, never a partial one.
        public static PageShape Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 200000) return null;
            try
            {
                var data = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (data == null || Number(data, "v") != Version) return null;
                var shape = new PageShape { Width = Number(data, "w"), Outer = Number(data, "o"), Height = Number(data, "h") };
                if (shape.Width < 200 || shape.Width > 8000 || shape.Outer < shape.Width || shape.Outer > shape.Width + 40 || shape.Height < 100 || shape.Height > 8000) return null;
                object raw; if (!data.TryGetValue("items", out raw)) return null;
                var list = raw as object[]; if (list == null || list.Length > MaxItems) return null;
                foreach (var entry in list)
                {
                    var values = entry as object[]; if (values == null || values.Length != 8) return null;
                    var item = new int[8];
                    for (var i = 0; i < 8; i++) { if (!(values[i] is int)) return null; item[i] = (int)values[i]; }
                    if (!Valid(item, shape)) return null;
                    shape.Items.Add(item);
                }
                return shape.Items.Count > 0 ? shape : null;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
        public static bool Valid(int[] item, PageShape shape)
        {
            return item != null && item.Length == 8 && (item[0] == 0 || item[0] == 1) && item[1] > -200 && item[1] < shape.Width + 200 &&
                item[2] > -200 && item[2] < shape.Height + 200 && item[3] >= 0 && item[3] <= 8000 && item[4] >= 0 && item[4] <= 8000 &&
                item[5] >= 0 && item[5] <= 1000 && item[6] >= 0 && item[6] <= 3 && item[7] >= 0 && item[7] <= 2;
        }
        private static int Number(Dictionary<string, object> data, string name)
        { object value; return data.TryGetValue(name, out value) && value is int ? (int)value : -1; }
    }
    // The last shape of each page kind: the same address with other numbers in it (another course, another week) shares one.
    internal sealed class PageShapes
    {
        private const int Keep = 40;
        private readonly Dictionary<string, PageShape> shapes = new Dictionary<string, PageShape>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        public static string Key(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !SitePolicy.Trusted(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri)) return null;
            var path = Regex.Replace(uri.AbsolutePath.ToLowerInvariant().TrimEnd('/'), @"\d+", "#");
            return path.Length == 0 ? "/" : path;
        }
        public int Count { get { return shapes.Count; } }
        public PageShape Find(string url)
        {
            var key = Key(url); PageShape shape;
            return key != null && shapes.TryGetValue(key, out shape) ? shape : null;
        }
        public void Remember(string url, PageShape shape)
        {
            var key = Key(url); if (key == null || shape == null) return;
            shapes[key] = shape; order.Remove(key); order.Add(key);
            while (order.Count > Keep) { shapes.Remove(order[0]); order.RemoveAt(0); }
        }
        public void Clear() { shapes.Clear(); order.Clear(); }
        public string Serialize()
        {
            var list = order.Select(key => new Dictionary<string, object> { { "k", key }, { "w", shapes[key].Width }, { "o", shapes[key].Outer }, { "h", shapes[key].Height }, { "items", shapes[key].Items } }).ToList();
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(new Dictionary<string, object> { { "v", PageShape.Version }, { "pages", list } });
        }
        public static PageShapes Load(string json)
        {
            var result = new PageShapes();
            if (string.IsNullOrEmpty(json) || json.Length > 16 * 1024 * 1024) return result;
            try
            {
                var data = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
                // Shapes of an earlier version may hold what that page script took by mistake (version 1: bars stuck to the
                // window's bottom); they are dropped and taken again on the next visit.
                object pages, version; if (data == null || !data.TryGetValue("v", out version) || !(version is int) || (int)version != PageShape.Version) return result;
                if (!data.TryGetValue("pages", out pages) || !(pages is object[])) return result;
                foreach (var page in (object[])pages)
                {
                    var entry = page as Dictionary<string, object>; object key;
                    if (entry == null || !entry.TryGetValue("k", out key) || !(key is string) || ((string)key).Length > 300 || !((string)key).StartsWith("/", StringComparison.Ordinal)) continue;
                    var copy = new Dictionary<string, object>(entry) { { "v", PageShape.Version } };
                    var shape = PageShape.Parse(new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(copy));
                    if (shape == null) continue;
                    result.shapes[(string)key] = shape; result.order.Remove((string)key); result.order.Add((string)key);
                }
                while (result.order.Count > Keep) { result.shapes.Remove(result.order[0]); result.order.RemoveAt(0); }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return result;
        }
        public static PageShapes Read(string path)
        {
            try { return File.Exists(path) ? Load(File.ReadAllText(path, Encoding.UTF8)) : new PageShapes(); }
            catch (IOException) { return new PageShapes(); }
            catch (UnauthorizedAccessException) { return new PageShapes(); }
        }
        public void Write(string path)
        {
            try { AtomicFile.Write(path, new UTF8Encoding(false).GetBytes(Serialize())); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
