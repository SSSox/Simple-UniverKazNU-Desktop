using System;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // Which addresses belong to Univer and what the app does with them: the exact HTTPS origin, its printed documents,
    // file downloads, language switches, the news shortcut, the Moodle hand-off, logout and safe external links.
    internal static class SitePolicy
    {
        public const string Origin = "https://univer.kaznu.kz";
        public const string Home = Origin + "/";
        private static readonly Regex PrintPath = Path(@"^/student/(?:iup(?:/\d+)*/?|transcript(?:/\d+)?/?|recbook(?:/\d+)?/?|interactive/syllabus/print/\d+/\d+/?|myschedule/print/\d+/\d+/?)$");
        private static readonly Regex ProtectedPath = Path(@"^/student/umkd/get/\d+/\d+/\d+/\d+/?$");
        private static readonly Regex DocumentPath = Path(@"\.(?:pdf|docx?|xlsx?|pptx?|odt|ods|rtf|zip|rar)$");
        private static readonly Regex SingleSignOnPath = Path(@"^/user/loginMoodle/?$");
        private static readonly Regex LanguagePath = new Regex(@"^/lang/change/(ru|kz|en)/?$", RegexOptions.CultureInvariant);
        private static readonly Regex LogoutPath = new Regex(@"^/user/logout/?$", RegexOptions.CultureInvariant);
        private static readonly Regex LoginPath = Path(@"^/user/login/?$");
        private static readonly Regex AccountPath = Path(@"^/(?:user|lang|student/attention)");
        private static readonly Regex NewsPath = new Regex(@"^/(?:|my/news/?|my/news/(\d{1,4})/?)$", RegexOptions.CultureInvariant);
        private static readonly Regex ControlCharacter = new Regex(@"[\p{Cc}]", RegexOptions.CultureInvariant);
        private static Regex Path(string pattern) { return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        public static bool Trusted(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                uri.Host.Equals("univer.kaznu.kz", StringComparison.OrdinalIgnoreCase) &&
                uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo);
        }
        // A trusted address whose path matches.
        private static bool TrustedPath(string url, Regex path) { return Trusted(url) && path.IsMatch(new Uri(url).AbsolutePath); }
        // A child frame may load only Univer's own documents: a remote frame gets no automation and no native privileges.
        public static bool ChildFrame(string url) { return Trusted(url); }
        // An empty document has no origin of its own and is not a navigation to decide: it loads untouched, in any frame.
        public static bool EmptyDocument(string url) { return string.Equals(url, "about:blank", StringComparison.Ordinal); }
        public static bool PrintDocument(string url) { return TrustedPath(url, PrintPath); }
        public static bool ProtectedDownload(string url) { return TrustedPath(url, ProtectedPath); }
        public static bool DocumentDownload(string url) { return TrustedPath(url, DocumentPath); }
        public static string Language(string url)
        {
            if (!Trusted(url)) return null;
            var match = LanguagePath.Match(new Uri(url).AbsolutePath);
            return match.Success ? match.Groups[1].Value : null;
        }
        // "/" and the site's news pager (/my/news/{n}) always pass through /student/attention, which re-posts the
        // read notice and lands on page 1. /news/student/{n} is the observed direct news list.
        public static string NewsShortcut(string url)
        {
            if (!Trusted(url)) return null;
            var uri = new Uri(url);
            if (uri.Query.Length > 0 && uri.AbsolutePath == "/") return null;
            var match = NewsPath.Match(uri.AbsolutePath);
            if (!match.Success) return null;
            int page;
            return match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out page) && page > 1 ? Origin + "/news/student/" + page : Origin + "/news/student";
        }
        // Univer's own hand-off to Moodle; the signed redirect is opened in the user's browser.
        public static bool SingleSignOn(string url) { return TrustedPath(url, SingleSignOnPath); }
        // The same site page regardless of a trailing slash or the case of its path; the query must match.
        public static bool SamePage(string first, string second)
        {
            if (!Trusted(first) || !Trusted(second)) return false;
            Uri a = new Uri(first), b = new Uri(second);
            return string.Equals(a.AbsolutePath.TrimEnd('/'), b.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) && a.Query == b.Query;
        }
        // The site's full-page login form, where a request of an ended session is sent.
        public static bool LoginPage(string url) { return TrustedPath(url, LoginPath); }
        // A section page worth reopening after an automatic sign-in: not the start or news route (it passes the read notice
        // again), account, language or notice addresses, files or printed documents.
        public static bool Resumable(string url)
        {
            return Trusted(url) && NewsShortcut(url) == null && !DocumentDownload(url) && !ProtectedDownload(url) && !PrintDocument(url) &&
                new Uri(url).AbsolutePath.Length > 1 && !AccountPath.IsMatch(new Uri(url).AbsolutePath);
        }
        public static bool UserLogout(string url, bool userGesture, bool isRedirect) { return userGesture && !isRedirect && Logout(url); }
        public static bool Logout(string url) { return TrustedPath(url, LogoutPath); }
        public static bool External(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || ControlCharacter.IsMatch(url)) return false;
            return (uri.Scheme == "https" || uri.Scheme == "http") ? string.IsNullOrEmpty(uri.UserInfo) :
                uri.Scheme == "mailto" || uri.Scheme == "tel";
        }
    }
}
