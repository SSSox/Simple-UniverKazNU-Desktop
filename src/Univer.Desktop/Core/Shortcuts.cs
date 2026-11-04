using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // A section or useful link the student pinned to the home screen.
    internal sealed class PinnedLink
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public PinnedLink Clone() { return new PinnedLink { Title = Title, Url = Url }; }
    }
    // Pins arrive from a page click and are kept in preferences, so every value is checked again here.
    internal static class Shortcuts
    {
        public const int Limit = 24;
        public const string MessagePrefix = "univer-pin:";
        private const int TitleLimit = 120;
        public static bool FixedHomeLink(string url)
        {
            if (!SitePolicy.Trusted(url)) return false;
            var uri = new Uri(url);
            if (uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
            return Regex.IsMatch(uri.AbsolutePath.TrimEnd('/'), @"^/(student/(myschedule|attendance/full|transcript|umkd)|user/loginMoodle|ssc(/UserServiceList|/StatementList)?)$", RegexOptions.IgnoreCase);
        }
        public static bool Allowed(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out uri) || Regex.IsMatch(url, @"[\p{Cc}]")) return false;
            if (SitePolicy.Trusted(url)) return !Regex.IsMatch(uri.AbsolutePath, @"^/user/(logout|login)/?$", RegexOptions.IgnoreCase);
            // Off-site useful links open in the user's browser; only plain web addresses qualify.
            return (uri.Scheme == "https" || uri.Scheme == "http") && SitePolicy.External(url) && !uri.Host.Equals("univer.kaznu.kz", StringComparison.OrdinalIgnoreCase);
        }
        public static string CleanTitle(string title)
        {
            var value = Regex.Replace(title ?? "", @"[\p{Cc}\s]+", " ").Trim();
            if (value.Length <= TitleLimit) return value;
            var cut = char.IsHighSurrogate(value[TitleLimit - 1]) ? TitleLimit - 1 : TitleLimit;
            return value.Substring(0, cut).TrimEnd();
        }
        public static List<PinnedLink> Normalize(IEnumerable<PinnedLink> pins)
        {
            var result = new List<PinnedLink>();
            foreach (var pin in pins ?? Enumerable.Empty<PinnedLink>())
            {
                if (pin == null || !Allowed(pin.Url) || FixedHomeLink(pin.Url)) continue;
                var title = CleanTitle(pin.Title);
                if (title.Length == 0 || result.Any(p => Same(p.Url, pin.Url))) continue;
                result.Add(new PinnedLink { Title = title, Url = pin.Url });
                if (result.Count == Limit) break;
            }
            return result;
        }
        // Payload: {"pin":true|false,"title":"…","url":"https://…"}. Returns null for anything malformed.
        public static List<PinnedLink> Apply(IEnumerable<PinnedLink> current, string payload)
        {
            Dictionary<string, object> message;
            try { message = new JavaScriptSerializer { MaxJsonLength = 8192 }.Deserialize<Dictionary<string, object>>(payload ?? ""); }
            catch (Exception) { return null; }
            object pin, url, title;
            if (message == null || !message.TryGetValue("pin", out pin) || !(pin is bool) || !message.TryGetValue("url", out url) || !(url is string) || !Allowed((string)url)) return null;
            var list = Normalize(current);
            if (!(bool)pin) { list.RemoveAll(p => Same(p.Url, (string)url)); return list; }
            if (FixedHomeLink((string)url)) return list;
            message.TryGetValue("title", out title);
            var clean = CleanTitle(title as string);
            if (clean.Length == 0 || list.Any(p => Same(p.Url, (string)url))) return list;
            if (list.Count >= Limit) return null;
            list.Add(new PinnedLink { Title = clean, Url = (string)url });
            return list;
        }
        public static string ToJson(IEnumerable<PinnedLink> pins)
        {
            return new JavaScriptSerializer().Serialize(Normalize(pins).Select(p => new Dictionary<string, string> { { "title", p.Title }, { "url", p.Url } }).ToList());
        }
        public static List<string> NormalizeOrder(IEnumerable<string> order)
        { return (order ?? Enumerable.Empty<string>()).Where(Allowed).Distinct(StringComparer.Ordinal).Take(Limit + 6).ToList(); }
        public static List<string> ApplyOrder(string payload)
        {
            try
            {
                var message = new JavaScriptSerializer { MaxJsonLength = 65536 }.Deserialize<Dictionary<string, object>>(payload ?? "");
                object value;
                if (message == null || !message.TryGetValue("order", out value) || !(value is System.Collections.IEnumerable) || value is string) return null;
                var list = ((System.Collections.IEnumerable)value).Cast<object>().ToList();
                if (list.Count > Limit + 6 || list.Any(item => !(item is string) || !Allowed((string)item))) return null;
                var result = NormalizeOrder(list.Cast<string>());
                return result.Count == list.Count ? result : null;
            }
            catch (Exception) { return null; }
        }
        public static string OrderJson(IEnumerable<string> order)
        { return new JavaScriptSerializer().Serialize(NormalizeOrder(order)); }
        private static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.Ordinal); }
    }
}
