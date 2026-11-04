using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Univer.Desktop.Core
{
    // "СДО Moodle" opens Univer's /user/loginMoodle sign-in form. After the site's own form signs in,
    // Univer leaves for Moodle; that hand-off is passed to the user's browser exactly once.
    internal static class SignOnPage
    {
        // Only an absolute HTTPS address without user info can leave the application.
        public static bool CanOpen(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && !Regex.IsMatch(url, @"[\p{Cc}]");
        }
        // The site's own exit: a server redirect, or a POST whose fields can be replayed. A script navigation to some other HTTPS address is not that exit.
        public static bool HandOffExit(string url, string method, bool isRedirect, bool hasFormBody)
        {
            if (!CanOpen(url)) return false;
            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)) return hasFormBody;
            return string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && isRedirect;
        }
        public static List<KeyValuePair<string, string>> ParseForm(string body)
        {
            var fields = new List<KeyValuePair<string, string>>();
            foreach (var pair in (body ?? "").Split('&'))
            {
                if (pair.Length == 0) continue;
                var at = pair.IndexOf('=');
                Func<string, string> decode = s => { try { return Uri.UnescapeDataString(s.Replace('+', ' ')); } catch (UriFormatException) { return s; } };
                fields.Add(new KeyValuePair<string, string>(decode(at < 0 ? pair : pair.Substring(0, at)), at < 0 ? "" : decode(pair.Substring(at + 1))));
            }
            return fields;
        }
        public static string AutoSubmitPage(string url, string method, IEnumerable<KeyValuePair<string, string>> fields, string title)
        {
            var page = new StringBuilder();
            page.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"referrer\" content=\"no-referrer\"><title>")
                .Append(WebUtility.HtmlEncode(title)).Append("</title></head><body onload=\"document.forms[0].submit()\" style=\"font-family:Segoe UI,Arial,sans-serif;color:#16283b\">")
                .Append("<form method=\"").Append(method == "POST" ? "post" : "get").Append("\" action=\"").Append(WebUtility.HtmlEncode(url)).Append("\">");
            foreach (var field in fields ?? new List<KeyValuePair<string, string>>())
                page.Append("<input type=\"hidden\" name=\"").Append(WebUtility.HtmlEncode(field.Key)).Append("\" value=\"").Append(WebUtility.HtmlEncode(field.Value)).Append("\">");
            page.Append("<p>").Append(WebUtility.HtmlEncode(title)).Append("…</p><noscript><button type=\"submit\">OK</button></noscript></form></body></html>");
            return page.ToString();
        }
    }
}
