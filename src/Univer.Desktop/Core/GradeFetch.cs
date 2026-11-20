using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    // One reading of «Текущая аттестация» for the new-grade check (UI/MainForm.Grades.cs): a GET with the profile's cookies,
    // as the browser asks for the page, outside any browser, so no page script, automation tick or sign-in follows it. A
    // sign-in or read-notice page, a redirect off the site, an error or anything but a plain HTML page is no reading.
    internal static class GradeFetch
    {
        internal const string Path = "/student/attestation/";
        internal const int RedirectLimit = 4, SizeLimit = 2000000;
        internal static async Task<string> ReadAsync(Func<string, CancellationToken, Task<HttpResponseMessage>> get, string url, CancellationToken token)
        {
            var next = url;
            for (var redirects = 0; redirects <= RedirectLimit; redirects++)
            {
                if (!SitePolicy.Trusted(next) || Signs(next)) return null;
                using (var response = await get(next, token).ConfigureAwait(false))
                {
                    var code = (int)response.StatusCode;
                    if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
                    {
                        if (response.Headers.Location == null) return null;
                        next = new Uri(new Uri(next), response.Headers.Location).AbsoluteUri;
                        continue;
                    }
                    if ((int)response.StatusCode != 200) return null;
                    foreach (var encoding in response.Content.Headers.ContentEncoding)
                        if (!encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)) return null;
                    var type = response.Content.Headers.ContentType;
                    if (type == null || (type.MediaType != "text/html" && type.MediaType != "application/xhtml+xml")) return null;
                    var length = response.Content.Headers.ContentLength;
                    if (length.HasValue && length.Value > SizeLimit) return null;
                    byte[] body;
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) body = await ReadAllAsync(stream, token).ConfigureAwait(false);
                    if (body == null) return null;
                    var html = Decode(body, type.CharSet);
                    return SignInPage(html) ? null : html;
                }
            }
            return null;
        }
        // The site's own sign-in form, shown at the asked address when the session has ended.
        private static readonly Regex SignInForm = new Regex(@"\bid\s*=\s*[""']?login_frm\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        internal static bool SignInPage(string html) { return html != null && SignInForm.IsMatch(html); }
        private static bool Signs(string url)
        {
            var path = new Uri(url).AbsolutePath;
            return path.StartsWith("/user/login", StringComparison.OrdinalIgnoreCase) || path.IndexOf("/attentionStudents/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.StartsWith("/user/setagree", StringComparison.OrdinalIgnoreCase);
        }
        // At most SizeLimit bytes: a longer answer is no reading.
        private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken token)
        {
            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[65536]; int read;
                while ((read = await CancellableRead.ReadAsync(stream, chunk, 0, chunk.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > SizeLimit) return null;
                    buffer.Write(chunk, 0, read);
                }
                return buffer.ToArray();
            }
        }
        private static string Decode(byte[] body, string charset)
        {
            Encoding encoding = Encoding.UTF8;
            if (!string.IsNullOrWhiteSpace(charset)) { try { encoding = Encoding.GetEncoding(charset.Trim('"', ' ')); } catch (ArgumentException) { } }
            return encoding.GetString(body);
        }
    }
}
