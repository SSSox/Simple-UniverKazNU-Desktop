using System;
using System.IO;
using System.Linq;

namespace Univer.Desktop.Core
{
    // What the app's own browser window may do: it shows other sites (Moodle, Google Drive, a teacher's page) with cookies of
    // its own profile, never Univer's session, never local files or other programs.
    internal enum WebNavigation { Allow, Univer, Shell, Block }
    internal static class WebLinks
    {
        public const string HandOffPath = "/univer-desktop-handoff";
        // A web page that opens inside the app: a plain http(s) page on a named host other than Univer.
        public static bool InApp(string url) { return SitePolicy.StudyLink(url); }
        // A navigation in the window: web pages go ahead; Univer goes back to the main window; mail and phone links go to
        // Windows; files, scripts, browser-internal pages and other programs' schemes are refused. Frames may also show the
        // page-made documents (about:, data:, blob:) that sites embed.
        public static WebNavigation Decide(string url, bool mainFrame)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri)) return WebNavigation.Block;
            if (SitePolicy.Trusted(url)) return mainFrame ? WebNavigation.Univer : WebNavigation.Block;
            if (uri.Scheme == "https" || uri.Scheme == "http") return SitePolicy.External(url) ? WebNavigation.Allow : WebNavigation.Block;
            if (uri.Scheme == "mailto" || uri.Scheme == "tel") return mainFrame && SitePolicy.External(url) ? WebNavigation.Shell : WebNavigation.Block;
            if (url == "about:blank" || url == "about:srcdoc") return WebNavigation.Allow;
            if (!mainFrame && (uri.Scheme == "data" || uri.Scheme == "blob")) return WebNavigation.Allow;
            return WebNavigation.Block;
        }
        // A Univer page the window may hand to the main window: any section page, never an address whose GET changes the
        // session or the site's settings (logout, language, account, read notice), so another site cannot steer them.
        public static bool HandBack(string url) { return SitePolicy.Trusted(url) && !SitePolicy.AccountPage(url); }
        // A page asked for in a new window or tab: a web page opens in the window, a Univer page goes back only as above,
        // and an empty page (about:blank, which a script fills afterwards) is refused rather than replacing the current one.
        public static WebNavigation Opened(string url)
        {
            var decision = Decide(url, true);
            if (decision == WebNavigation.Allow && !SitePolicy.External(url)) return WebNavigation.Block;
            if (decision == WebNavigation.Univer && !HandBack(url)) return WebNavigation.Block;
            return decision;
        }
        // The address the Moodle hand-off page is served at: Moodle's own origin, so its form posts there like the site's own.
        public static string HandOffAddress(string target)
        {
            Uri uri;
            if (!Uri.TryCreate(target, UriKind.Absolute, out uri) || uri.Scheme != "https" || !InApp(target)) return null;
            return uri.GetLeftPart(UriPartial.Authority) + HandOffPath;
        }
        public static bool HandOff(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.AbsolutePath.Equals(HandOffPath, StringComparison.OrdinalIgnoreCase);
        }
        // Programs and scripts are never saved from another site: one double click in Downloads would run them.
        private static readonly string[] Programs =
        {
            ".exe", ".com", ".scr", ".pif", ".bat", ".cmd", ".msi", ".msp", ".msix", ".appx", ".appxbundle", ".msixbundle", ".ps1", ".psm1",
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".cpl", ".dll", ".sys", ".lnk", ".url", ".reg", ".inf", ".jar", ".application",
            ".gadget", ".scf", ".chm", ".iso", ".img", ".vhd", ".vhdx", ".appref-ms", ".settingcontent-ms", ".library-ms",
            ".msc", ".diagcab", ".search-ms", ".searchconnector-ms", ".wsc", ".sct"
        };
        public static bool Blocked(string name)
        {
            var extension = Path.GetExtension((name ?? "").Trim().TrimEnd('.', ' ')).ToLowerInvariant();
            return Programs.Contains(extension);
        }
        // Where a file from another site is saved: its own safe name in Downloads, never over an existing file or one another
        // download is already writing (taken); null for a program.
        public static string Target(string folder, string suggested, Func<string, bool> taken = null)
        {
            var name = FileNames.Safe(string.IsNullOrWhiteSpace(suggested) ? "download" : suggested);
            if (string.IsNullOrWhiteSpace(name)) name = "download";
            if (Blocked(name)) return null;
            var stem = Path.GetFileNameWithoutExtension(name); var extension = Path.GetExtension(name);
            var path = Path.Combine(folder, name);
            for (var i = 2; File.Exists(path) || File.Exists(path + ".crdownload") || (taken != null && taken(path)); i++) path = Path.Combine(folder, stem + " (" + i + ")" + extension);
            return path;
        }
    }
}
