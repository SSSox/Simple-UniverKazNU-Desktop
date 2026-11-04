namespace Univer.Desktop.Core
{
    // Page scripts name the origin in their own source (univer-layout.js carries itself into frames through toString()),
    // so native code rewrites those literals from SitePolicy.Origin; a stale origin would silently never match.
    internal static class ScriptBundle
    {
        internal const string OriginLiteral = "'https://univer.kaznu.kz'";
        public static string WithOrigin(string script) { return WithOrigin(script, SitePolicy.Origin); }
        // origin is a parameter so the substitution stays testable while both addresses are still the same.
        internal static string WithOrigin(string script, string origin)
        {
            if (string.IsNullOrEmpty(script) || string.IsNullOrEmpty(origin)) return script;
            return script.Replace(OriginLiteral, "'" + origin + "'");
        }
    }
}
