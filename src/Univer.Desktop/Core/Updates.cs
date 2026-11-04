using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // Where the app's releases live and how an offered version is judged.
    internal static class Updates
    {
        public const string Repository = "https://github.com/SSSox/Simple-UniverKazNU-Desktop";
        public const string Latest = Repository + "/releases/latest";
        // Automatic checks run at most this often; the Settings button checks at once.
        public static readonly long CheckInterval = TimeSpan.FromHours(6).Ticks;
        // A failed automatic check waits this long, so an offline laptop does not retry on every return to the window.
        public static readonly long RetryInterval = TimeSpan.FromMinutes(15).Ticks;
        public const long MaxSetupBytes = 512L * 1024 * 1024;
        private static readonly Regex Tag = new Regex("^" + Regex.Escape(Repository) + "/releases/tag/v([0-9]+(?:\\.[0-9]+){0,3})/?$", RegexOptions.CultureInvariant);
        private static readonly Regex Number = new Regex("^[0-9]+(?:\\.[0-9]+){0,3}$", RegexOptions.CultureInvariant);
        private static readonly Regex Hash = new Regex("^([0-9a-fA-F]{64})(?:\\s.*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        public static bool IsVersion(string value) { return value != null && Number.IsMatch(value); }
        // The version in the address that "latest release" redirects to; null without a released version tag.
        public static string Version(string location)
        {
            var match = location == null ? Match.Empty : Tag.Match(location.Trim());
            return match.Success ? match.Groups[1].Value : null;
        }
        public static bool Newer(string candidate, string current)
        {
            if (!IsVersion(candidate) || !IsVersion(current)) return false;
            var next = candidate.Split('.'); var installed = current.Split('.');
            for (var index = 0; index < Math.Max(next.Length, installed.Length); index++)
            {
                var difference = Part(next, index).CompareTo(Part(installed, index));
                if (difference != 0) return difference > 0;
            }
            return false;
        }
        private static long Part(string[] parts, int index)
        {
            long value;
            return index < parts.Length && long.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
        public static string SetupName(string version, string flavor, string architecture)
        { return "Univer-Desktop-" + version + "-" + flavor.ToLowerInvariant() + "-" + architecture + "-setup.exe"; }
        public static string SetupUrl(string version, string flavor, string architecture)
        { return Repository + "/releases/download/v" + version + "/" + SetupName(version, flavor, architecture); }
        public static string ChecksumUrl(string version, string flavor, string architecture) { return SetupUrl(version, flavor, architecture) + ".sha256"; }
        public static string ReleasePage(string version) { return Repository + "/releases/tag/v" + version; }
        // The installer writes this marker; a portable folder has none and is updated by hand.
        public static bool Installed(string marker, string flavor, string architecture)
        { return marker != null && marker.Trim() == "Univer.Desktop." + flavor.ToLowerInvariant() + "." + architecture; }
        // The hash from a .sha256 file: <hash>  <name>.
        public static string Checksum(string text)
        {
            var match = text == null ? Match.Empty : Hash.Match(text.Trim());
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }
        // GitHub and the host it redirects release files to, over plain HTTPS.
        public static bool AllowedHost(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || uri.Port != 443) return false;
            var host = uri.Host.ToLowerInvariant();
            return host == "github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
        }
        // A clock moved backwards counts as due, or the next check could be years away.
        public static bool Due(long now, long checkedAt) { return checkedAt <= 0 || now < checkedAt || now - checkedAt >= CheckInterval; }
        // The window at entry: once per run, and never for a version the student chose not to see again.
        public static bool Prompt(string available, string skipped, bool answered) { return !string.IsNullOrEmpty(available) && available != skipped && !answered; }
        // /DIR keeps the update in the running copy's folder; /UPDATE=1 makes the installer wait for it to close and start it again.
        public static string SetupArguments(string directory, string language)
        {
            var name = language == "kz" ? "kazakh" : language == "en" ? "english" : "russian";
            return "/SILENT /NORESTART /UPDATE=1 /LANG=" + name + " /DIR=\"" + directory.TrimEnd('\\') + "\"";
        }
    }
}
