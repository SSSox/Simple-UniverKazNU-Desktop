using System;
using CefSharp;

namespace Univer.Desktop.UI
{
    // Version details for the About line in Settings, so a support request can name the exact build and engine.
    internal static class AppInfo
    {
        public static string Version { get { var v = typeof(AppInfo).Assembly.GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; } }
#if LEGACY_ENGINE
        public const string Flavor = "Legacy";
#else
        public const string Flavor = "Modern";
#endif
        public static string Architecture { get { return Environment.Is64BitProcess ? "x64" : "x86"; } }
        // Compiled into CefSharp.Core from the CEF headers; reading it needs no initialized browser.
        public static string Chromium { get { try { return Cef.ChromiumVersion; } catch (Exception) { return "?"; } } }
    }
}