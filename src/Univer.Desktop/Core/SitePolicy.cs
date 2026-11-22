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
        public const string LoginAddress = Origin + "/user/login";
        private static readonly Regex PrintPath = Path(@"^/student/(?:iup(?:/\d+)*/?|transcript(?:/\d+)?/?|recbook(?:/\d+)?/?|interactive/syllabus/print/\d+/\d+/?|myschedule/print/\d+/\d+/?)$");
        private static readonly Regex ProtectedPath = Path(@"^/student/umkd/get/\d+/\d+/\d+/\d+/?$");
        private static readonly Regex MaterialsPath = Path(@"^/student/umkd/\d+/\d+/\d+/?$");
        private static readonly Regex GradesPath = Path(@"^/student/attendance/(?:full|show)(?:/\d+)*/?$");
        // The schedule page, a semester tab of it or one week's own address (the export buttons' pages, overview.js schedulePage).
        private static readonly Regex SchedulePath = Path(@"^/student/myschedule(?:/\d{4}/[12](?:/\d{1,2}\.\d{1,2}\.\d{4}/\d{1,2}\.\d{1,2}\.\d{4})?)?/?$");
        private static readonly Regex ExamsPath = Path(@"^/student/(?:myexam|mymidterm)/schedule/?$");
        private static readonly Regex DocumentPath =Path(@"\.(?:pdf|docx?|xlsx?|pptx?|odt|ods|rtf|zip|rar)$");
        private static readonly Regex SingleSignOnPath = Path(@"^/user/loginMoodle/?$");
        private static readonly Regex OnlineTestPath = Path(@"^/student/AuthorizationOnOnlineTestForm/?$");
        public const string OnlineTestHost = "online-test.kaznu.kz";
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
        // The site's own flag link for a language; anything unknown falls back to Russian, the app's default.
        public static string LanguageSwitch(string language)
        {
            return Origin + "/lang/change/" + (language == "kz" || language == "en" ? language : "ru") + "/";
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
        // «Онлайн тест»: the site's route that signs the student in to the test portal with a server redirect.
        public static bool OnlineTest(string url) { return TrustedPath(url, OnlineTestPath); }
        // That portal: HTTPS on port 443 at exactly online-test.kaznu.kz, without a user name.
        public static bool OnlineTestPortal(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && !ControlCharacter.IsMatch(url) && uri.Scheme == "https" &&
                uri.Host.Equals(OnlineTestHost, StringComparison.OrdinalIgnoreCase) && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo);
        }
        // Only the server's redirect that answers the student's own «Онлайн тест» click leaves for the portal; a script
        // navigation, a form, a late or a repeated redirect, and the printed-document viewer do not.
        public static bool OnlineTestExit(string url, string method, bool isRedirect, bool armed, bool viewer)
        { return armed && !viewer && isRedirect && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && OnlineTestPortal(url); }
        // The click that arms that redirect: a GET of the route with a user gesture, not a redirect, in the site window.
        public static bool OnlineTestClick(string url, string method, bool userGesture, bool isRedirect, bool viewer)
        { return userGesture && !isRedirect && !viewer && string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && OnlineTest(url); }
        // How long the student's «Онлайн тест» click waits for the server's redirect to the portal.
        public static readonly TimeSpan OnlineTestWindow = TimeSpan.FromSeconds(30);
        // The page the site window returns to once the portal opens: the student's own page, not the route itself or an
        // account, login or notice address it passed through.
        public static string OnlineTestReturnPage(string lastGet) { return Trusted(lastGet) && !OnlineTest(lastGet) && !AccountPage(lastGet) ? lastGet : null; }
        // Opened from the student's own page, the route returns there. A second click from the route itself (or from an
        // account page) finds no such page and keeps the one found first, within the window. After an automatic sign-in the
        // route reopens from the page the sign-in landed on, so that page is the one the site window returns to.
        public static string OnlineTestReturnPage(string lastGet, string stored, DateTime storedAt, DateTime now)
        { return OnlineTestReturnPage(lastGet) ?? (stored != null && now - storedAt <= OnlineTestWindow ? stored : null); }
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
        // Account, language, logout and read-notice addresses: GET requests there change the session or the site's settings.
        public static bool AccountPage(string url) { return TrustedPath(url, AccountPath); }
        public static bool UserLogout(string url, bool userGesture, bool isRedirect) { return userGesture && !isRedirect && Logout(url); }
        public static bool Logout(string url) { return TrustedPath(url, LogoutPath); }
        public static bool External(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || ControlCharacter.IsMatch(url)) return false;
            return (uri.Scheme == "https" || uri.Scheme == "http") ? string.IsNullOrEmpty(uri.UserInfo) :
                uri.Scheme == "mailto" || uri.Scheme == "tel";
        }
        // A course's materials in UMKD (/student/umkd/{course}/{year}/{term}), where a teacher may leave links to other sites.
        public static bool StudyMaterials(string url) { return TrustedPath(url, MaterialsPath); }
        // The attendance and grades journal («Успеваемость»), the only page the grade summary is made from.
        public static bool Grades(string url) { return TrustedPath(url, GradesPath); }
        public static bool SchedulePage(string url) { return TrustedPath(url, SchedulePath); }
        public static bool ExamPage(string url) { return TrustedPath(url, ExamsPath); }
        // A teacher's link from UMKD: a plain web page on a named host. Mail and phone links, addresses with a user name,
        // bare IP addresses and local names are not study sources.
        public static bool StudyLink(string url)
        {
            Uri uri;
            if (!External(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return false;
            return uri.HostNameType == UriHostNameType.Dns && uri.Host.IndexOf('.') > 0 && !uri.Host.EndsWith(".", StringComparison.Ordinal) &&
                !uri.Host.Equals("univer.kaznu.kz", StringComparison.OrdinalIgnoreCase);
        }
        // A UMKD material the server sends on to a web page on another site: the teacher left a link there, not a file.
        public static bool MaterialLink(string material, string target) { return ProtectedDownload(material) && !Trusted(target) && StudyLink(target); }
        // The host of an outside address as the user should read it, or "".
        public static string Host(string url) { Uri uri; return Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host : ""; }
    }
}
