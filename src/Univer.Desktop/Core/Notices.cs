using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // One entry of a counted list ("Удовлетворенность обучающихся качеством преподавания", until "30.11.2026").
    internal sealed class NoticeDetail
    {
        public string Title { get; set; } = "";
        public string Until { get; set; } = "";
    }
    // A site notification: a menu count (Counter) or a page message asking for a questionnaire first (Blocked).
    // One the site stops showing is kept as Gone for a while, so a read one that comes back stays read.
    internal sealed class SiteNotice
    {
        public const string Counter = "counter", Blocked = "blocked";
        public string Key { get; set; } = "";
        public string Kind { get; set; } = Counter;
        public string Title { get; set; } = "";
        // The trusted page that resolves it: the menu link, or the link in the message (else the page itself).
        public string Url { get; set; } = "";
        // The site's own number.
        public int Count { get; set; }
        // Rows of the counted list not answered yet, read at this Count; -1 while no read of the list has said.
        public int Open { get; set; } = -1;
        public string Text { get; set; } = "";
        // A message's page; for a counter, the section whose menu showed the count ("/student/").
        public string Page { get; set; } = "";
        public long FirstSeen { get; set; }
        public long LastSeen { get; set; }
        public bool Read { get; set; }
        // The site's number when it was marked read: only a larger one makes it unread again.
        public int ReadCount { get; set; }
        public bool Gone { get; set; }
        public List<NoticeDetail> Details { get; set; } = new List<NoticeDetail>();
        public SiteNotice Copy()
        {
            var copy = (SiteNotice)MemberwiseClone();
            copy.Details = (Details ?? new List<NoticeDetail>()).Select(d => new NoticeDetail { Title = d.Title, Until = d.Until }).ToList();
            return copy;
        }
    }
    // One link of a site menu or section strip, with the count shown next to it (0 for none); Menu: in the main menu.
    // Open is what a read of its list found (-1: not read), with the open entries in Details.
    internal sealed class NoticeLink
    {
        public string Url, Title;
        public int Count, Open = -1;
        public bool Menu;
        public List<NoticeDetail> Details = new List<NoticeDetail>();
    }
    // A page message that asks for a questionnaire or test; Url is the link inside it, or the page.
    internal sealed class NoticeMessage { public string Title, Text, Url; }
    // One page read. A counter is gone only when its own section shows the link without a count or its full menu drops it;
    // a message when its page is read without it.
    internal sealed class NoticeScan
    {
        public string Page;
        // Menu: the whole main menu was read. Fetching: the page began its background reads.
        public bool Menu, Fetching;
        public List<NoticeLink> Links = new List<NoticeLink>();
        public List<NoticeMessage> Messages = new List<NoticeMessage>();
    }
    internal static class Notices
    {
        public const string MessagePrefix = "univer-notices:";
        public const int Max = 30, Stored = 60, TitleLength = 100, TextLength = 280, MaxDetails = 3, MaxMessage = 65536;
        // How long an item the site no longer shows keeps its read mark.
        public const long KeepGone = 60L * 24 * 60 * 60 * 1000;
        private static readonly Regex DatePattern = new Regex(@"^\d{2}\.\d{2}\.\d{4}$", RegexOptions.CultureInvariant);
        private static readonly Regex Space = new Regex(@"[\p{Cc}\s]+", RegexOptions.CultureInvariant);
        public static long Now() { return (DateTime.UtcNow.Ticks - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks) / TimeSpan.TicksPerMillisecond; }
        // The number the panel shows: the site's count, or fewer when its list was read and has fewer open rows.
        public static int Shown(SiteNotice notice) { return notice.Open >= 0 ? Math.Min(notice.Count, notice.Open) : notice.Count; }
        public static bool Visible(SiteNotice notice) { return !notice.Gone && (notice.Kind == SiteNotice.Blocked || Shown(notice) > 0); }
        public static List<SiteNotice> Showing(IEnumerable<SiteNotice> notices) { return notices.Where(Visible).Take(Max).ToList(); }
        public static int Unread(IEnumerable<SiteNotice> notices) { return Showing(notices).Count(n => !n.Read); }
        // The section a page belongs to: its first path segment ("/student/rating/" → "/student/").
        public static string Section(string page)
        {
            if (string.IsNullOrEmpty(page) || page[0] != '/') return "/";
            var path = page.Split('?')[0]; var end = path.IndexOf('/', 1);
            return end < 0 ? path.TrimEnd('/') + "/" : path.Substring(0, end + 1);
        }
        private static string CounterKey(string url) { return "counter:" + url; }
        private static string MessageKey(string page, string text)
        {
            using (var sha = SHA256.Create())
                return "blocked:" + page + "#" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)), 0, 6).Replace("-", "").ToLowerInvariant();
        }
        // The stored list updated by scan; unchanged items keep their first-seen time and read mark.
        public static List<SiteNotice> Merge(IEnumerable<SiteNotice> current, NoticeScan scan, long now)
        {
            var byKey = new Dictionary<string, SiteNotice>(StringComparer.Ordinal);
            foreach (var notice in current) byKey[notice.Key] = notice.Copy();
            var place = Section(scan.Page);
            var shown = scan.Links.GroupBy(l => l.Url, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            if (scan.Menu)
                foreach (var notice in byKey.Values)
                    if (notice.Kind == SiteNotice.Counter && notice.Page == place && !shown.ContainsKey(notice.Url)) notice.Gone = true;
            foreach (var pair in shown)
            {
                var count = Math.Max(0, Math.Min(9999, pair.Value.Max(l => l.Count)));
                SiteNotice old; byKey.TryGetValue(CounterKey(pair.Key), out old);
                if (count == 0)
                {
                    // Only the section that showed the count can say it is gone: another section's menu may list the same
                    // link without ever counting it.
                    if (old != null && old.Page == place) old.Gone = true;
                    continue;
                }
                if (old == null) byKey[CounterKey(pair.Key)] = old = new SiteNotice { Key = CounterKey(pair.Key), Url = pair.Key, FirstSeen = now };
                // A different count may be a new questionnaire: what the list said about the old number no longer applies.
                if (old.Count != count) { old.Open = -1; old.Details = new List<NoticeDetail>(); }
                var title = Clip(pair.Value.First(l => l.Count == count).Title, TitleLength);
                old.Title = title.Length > 0 ? title : pair.Key; old.Count = count; old.Page = place; old.Gone = false; old.LastSeen = now;
                var described = pair.Value.FirstOrDefault(l => l.Open >= 0);
                if (described != null) { old.Open = described.Open; old.Details = described.Details.Take(MaxDetails).ToList(); }
                old.Read = old.Read && count <= old.ReadCount;
            }
            var messages = new Dictionary<string, NoticeMessage>(StringComparer.Ordinal);
            foreach (var message in scan.Messages.Where(m => !string.IsNullOrEmpty(m.Text)))
            {
                var key = MessageKey(scan.Page, Clip(message.Text, TextLength));
                if (!messages.ContainsKey(key)) messages[key] = message;
            }
            foreach (var notice in byKey.Values)
                if (notice.Kind == SiteNotice.Blocked && notice.Page == scan.Page && !messages.ContainsKey(notice.Key)) notice.Gone = true;
            foreach (var pair in messages)
            {
                SiteNotice old;
                if (!byKey.TryGetValue(pair.Key, out old))
                    byKey[pair.Key] = old = new SiteNotice { Key = pair.Key, Kind = SiteNotice.Blocked, Text = Clip(pair.Value.Text, TextLength), Page = scan.Page, FirstSeen = now };
                old.Title = Clip(pair.Value.Title, TitleLength); old.Url = pair.Value.Url; old.Gone = false; old.LastSeen = now;
            }
            return Ordered(byKey.Values, now);
        }
        // Shown unread first, then the newest; items gone for longer than KeepGone are forgotten.
        public static List<SiteNotice> Ordered(IEnumerable<SiteNotice> notices, long now)
        {
            return notices.Where(n => !n.Gone || now - n.LastSeen < KeepGone)
                .OrderBy(n => Visible(n) ? 0 : 1).ThenBy(n => n.Read).ThenByDescending(n => n.FirstSeen).ThenBy(n => n.Key, StringComparer.Ordinal)
                .Take(Stored).ToList();
        }
        public static List<SiteNotice> MarkRead(IEnumerable<SiteNotice> current, string key)
        { return current.Select(n => { var copy = n.Copy(); if (copy.Key == key) { copy.Read = true; copy.ReadCount = copy.Count; } return copy; }).ToList(); }
        public static List<SiteNotice> MarkAllRead(IEnumerable<SiteNotice> current)
        { return current.Select(n => { var copy = n.Copy(); if (Visible(copy)) { copy.Read = true; copy.ReadCount = copy.Count; } return copy; }).ToList(); }
        // Shown unread items that are new, or whose site count grew; an item that only came back keeps quiet.
        public static List<SiteNotice> NewlyUnread(IEnumerable<SiteNotice> previous, IEnumerable<SiteNotice> next)
        {
            var before = previous.ToDictionary(n => n.Key, StringComparer.Ordinal);
            return Showing(next).Where(n => { SiteNotice old; return !n.Read && (!before.TryGetValue(n.Key, out old) || n.Count > old.Count); }).ToList();
        }
        // The scan in a page result (section false) or a background message (section true) of this generation, or null for
        // anything malformed. Links outside the trusted origin, files and oversized values are dropped.
        public static NoticeScan Parse(string message, bool section, string generation)
        {
            if (message == null || message.Length > MaxMessage) return null;
            Dictionary<string, object> json;
            try { json = new JavaScriptSerializer().DeserializeObject(message) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (json == null || Int(json, "v") != 1 || Str(json, "kind") != (section ? "section" : "page") || Str(json, "g") != generation) return null;
            var page = Str(json, "page");
            if (!SitePath(page)) return null;
            var scan = new NoticeScan { Page = page };
            var links = Items(json, "links");
            foreach (var item in links.Take(120).OfType<Dictionary<string, object>>())
            {
                var url = Address(Str(item, "u")); if (url == null) continue;
                var link = new NoticeLink { Url = url, Title = Clip(Str(item, "t"), TitleLength), Count = Math.Max(0, Math.Min(9999, Int(item, "n"))), Menu = Int(item, "m") == 1 };
                if (item.ContainsKey("o")) link.Open = Math.Max(0, Math.Min(9999, Int(item, "o")));
                foreach (var entry in Items(item, "d").Take(MaxDetails).OfType<Dictionary<string, object>>())
                {
                    var name = Clip(Str(entry, "t"), TitleLength); if (name.Length == 0) continue;
                    var until = Str(entry, "e");
                    link.Details.Add(new NoticeDetail { Title = name, Until = DatePattern.IsMatch(until) ? until : "" });
                }
                scan.Links.Add(link);
            }
            scan.Menu = Bool(json, "menu") && links.Length < 120 && scan.Links.Any(l => l.Menu);
            scan.Fetching = Bool(json, "fetching");
            foreach (var item in Items(json, "blocks").Take(5).OfType<Dictionary<string, object>>())
            {
                var text = Clip(Str(item, "x"), TextLength); if (text.Length == 0) continue;
                var target = Address(Str(item, "l")) ?? Address(page); if (target == null) continue;
                scan.Messages.Add(new NoticeMessage { Title = Clip(Str(item, "t"), TitleLength), Text = text, Url = target });
            }
            return scan;
        }
        // The background read's last message of this generation: pages read and pages that failed, or false for anything else.
        public static bool ParseDone(string message, string generation, out int read, out int failed)
        {
            read = failed = 0;
            if (message == null || message.Length > 1024) return false;
            Dictionary<string, object> json;
            try { json = new JavaScriptSerializer().DeserializeObject(message) as Dictionary<string, object>; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            if (json == null || Int(json, "v") != 1 || Str(json, "kind") != "done" || Str(json, "g") != generation) return false;
            read = Math.Max(0, Math.Min(99, Int(json, "read"))); failed = Math.Max(0, Math.Min(99, Int(json, "failed")));
            return true;
        }
        // A list read back from disk: only well-formed items with trusted addresses, in display order.
        public static List<SiteNotice> Normalize(IEnumerable<SiteNotice> stored, long now)
        {
            var result = new List<SiteNotice>(); var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in stored ?? Enumerable.Empty<SiteNotice>())
            {
                if (n == null || string.IsNullOrEmpty(n.Key) || !keys.Add(n.Key) || (n.Kind != SiteNotice.Counter && n.Kind != SiteNotice.Blocked)) continue;
                if (!SitePolicy.Trusted(n.Url) || SitePolicy.DocumentDownload(n.Url) || SitePolicy.Logout(n.Url)) continue;
                var copy = n.Copy();
                copy.Title = Clip(copy.Title, TitleLength); copy.Text = Clip(copy.Text, TextLength); copy.Page = copy.Page ?? "";
                copy.Count = Math.Max(0, Math.Min(9999, copy.Count)); copy.Open = Math.Max(-1, Math.Min(9999, copy.Open));
                copy.Details = copy.Details.Where(d => d != null && !string.IsNullOrEmpty(d.Title)).Take(MaxDetails)
                    .Select(d => new NoticeDetail { Title = Clip(d.Title, TitleLength), Until = DatePattern.IsMatch(d.Until ?? "") ? d.Until : "" }).ToList();
                result.Add(copy);
            }
            return Ordered(result, now);
        }
        private static bool SitePath(string value) { return value.Length >= 1 && value.Length <= 512 && value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal); }
        // An absolute trusted page address for a site path, never a file, a download or the logout.
        private static string Address(string value)
        {
            if (!SitePath(value)) return null;
            var url = SitePolicy.Origin + value;
            return SitePolicy.Trusted(url) && !SitePolicy.DocumentDownload(url) && !SitePolicy.ProtectedDownload(url) && !SitePolicy.Logout(url) ? url : null;
        }
        private static string Clip(string value, int limit) { var text = Space.Replace(value ?? "", " ").Trim(); return text.Length > limit ? text.Substring(0, limit) : text; }
        private static bool Bool(Dictionary<string, object> json, string key) { object value; return json.TryGetValue(key, out value) && value is bool && (bool)value; }
        private static string Str(Dictionary<string, object> json, string key) { object value; return json.TryGetValue(key, out value) ? value as string ?? "" : ""; }
        private static int Int(Dictionary<string, object> json, string key)
        {
            object value;
            if (!json.TryGetValue(key, out value) || value == null || value is string || value is bool) return 0;
            try { return Convert.ToInt32(value); } catch (Exception) { return 0; }
        }
        private static object[] Items(Dictionary<string, object> json, string key)
        { object value; return json.TryGetValue(key, out value) ? (value as object[] ?? (value as System.Collections.ArrayList)?.ToArray() ?? new object[0]) : new object[0]; }
    }
}
