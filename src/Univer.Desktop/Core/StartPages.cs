using System;

namespace Univer.Desktop.Core
{
    // The page the app opens with (Settings → View). Kept as a key of this fixed list, never as an address, so a
    // preferences file cannot send the start anywhere else. Each is a sidebar section that the site sends to its sign-in
    // form when the session has ended and that the app reopens after its own sign-in (SitePolicy.Resumable).
    internal static class StartPages
    {
        internal const string Home = "home";
        // The words are the sidebar's (Strings: the same keys).
        internal static readonly string[] Keys = { Home, "bachelor", "schedule", "journal", "attestation", "materials", "distance" };
        private static readonly string[] Paths = { "/", "/student/bachelor/", "/student/myschedule/", "/student/attendance/full/", "/student/attestation/", "/student/umkd/", "/student/discussion/index/" };
        internal static string Normalize(string key) { return Array.IndexOf(Keys, key) >= 0 ? key : Home; }
        internal static string Address(string key) { return SitePolicy.Origin + Paths[Array.IndexOf(Keys, Normalize(key))]; }
    }
}
