using System;
using System.Globalization;
using System.Net;

namespace Univer.Desktop.Core
{
    // A response delta, including expiry tombstones that CookieContainer no longer enumerates.
    internal sealed class ResponseCookie
    {
        public string Name, Value, Domain = "", Path, SameSite = "";
        public bool Secure, HttpOnly;
        public DateTime? Expires;
        public static bool TryParse(Uri uri, string header, DateTime now, out ResponseCookie result)
        {
            result = null;
            if (uri == null || string.IsNullOrEmpty(header) || header.Length > 8192 || header.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) return false;
            var parts = header.Split(';'); var pair = parts[0].Trim(); var equals = pair.IndexOf('=');
            if (equals <= 0) return false;
            var value = new ResponseCookie { Name = pair.Substring(0, equals).Trim(), Value = pair.Substring(equals + 1).Trim(), Path = DefaultPath(uri) };
            long? maxAge = null;
            for (var i = 1; i < parts.Length; i++)
            {
                var attribute = parts[i].Trim(); var separator = attribute.IndexOf('=');
                var name = (separator < 0 ? attribute : attribute.Substring(0, separator)).Trim().ToLowerInvariant();
                var content = separator < 0 ? "" : attribute.Substring(separator + 1).Trim();
                switch (name)
                {
                    case "domain": value.Domain = content; break;
                    case "path": if (content.StartsWith("/", StringComparison.Ordinal)) value.Path = content; break;
                    case "secure": value.Secure = true; break;
                    case "httponly": value.HttpOnly = true; break;
                    case "samesite": value.SameSite = content.ToLowerInvariant(); break;
                    case "max-age": long age; if (long.TryParse(content, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out age)) maxAge = age; break;
                    case "expires":
                        DateTime expiry;
                        if (DateTime.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out expiry)) value.Expires = expiry;
                        break;
                }
            }
            if (maxAge.HasValue)
                value.Expires = maxAge <= 0 ? now.AddDays(-1) : maxAge >= (DateTime.MaxValue - now).TotalSeconds ? DateTime.MaxValue : now.AddSeconds(maxAge.Value);
            try
            {
                // Validate names, values, path and domain with the framework's cookie implementation.
                // Keep expiry out of this probe: a deletion must remain visible to the caller.
                var cookie = new Cookie(value.Name, value.Value, value.Path);
                if (value.Domain.Length > 0) cookie.Domain = value.Domain;
                var validation = new UriBuilder(uri) { Path = value.Path, Query = "", Fragment = "" }.Uri;
                new CookieContainer().Add(validation, cookie);
            }
            catch (CookieException) { return false; }
            catch (ArgumentException) { return false; }
            result = value; return true;
        }
        private static string DefaultPath(Uri uri)
        {
            var path = uri.AbsolutePath; var slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path.Substring(0, slash);
        }
    }
}
