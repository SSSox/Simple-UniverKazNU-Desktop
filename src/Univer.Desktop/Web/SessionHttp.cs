using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.Web
{
    // HttpClient that shares the browser's Univer session. Cookies are attached only to exact Univer
    // origin requests and response cookies are written back to the same Chromium profile.
    internal sealed class SessionHttp : IDisposable
    {
        private readonly ICookieManager cookieManager;
        private readonly CookieContainer cookies = new CookieContainer();
        private readonly HttpClientHandler handler;
        public HttpClient Client { get; private set; }
        public SessionHttp(IRequestContext context, string agent, string referer, TimeSpan timeout)
        {
            cookieManager = context.GetCookieManager(null);
            handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = cookies, AutomaticDecompression = DecompressionMethods.None };
            Client = new HttpClient(handler) { Timeout = timeout };
            Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", string.IsNullOrWhiteSpace(agent) ? "UniverDesktop" : agent);
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "identity");
            if (SitePolicy.Trusted(referer)) Client.DefaultRequestHeaders.Referrer = new Uri(referer);
        }
        public async Task<HttpResponseMessage> GetAsync(string url, CancellationToken token)
        {
            if (!SitePolicy.Trusted(url)) throw new IOException("offOrigin");
            var uri = new Uri(url);
            foreach (var c in await cookieManager.VisitUrlCookiesAsync(url, true))
            {
                try { cookies.Add(uri, new System.Net.Cookie(c.Name, c.Value, c.Path, c.Domain) { Secure = c.Secure, HttpOnly = c.HttpOnly, Expires = c.Expires ?? DateTime.MinValue }); }
                catch (CookieException) { /* A cookie .NET cannot represent is simply not forwarded. */ }
            }
            token.ThrowIfCancellationRequested();
            var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            try
            {
                IEnumerable<string> setCookies;
                if (response.Headers.TryGetValues("Set-Cookie", out setCookies))
                {
                    foreach (var header in setCookies)
                    {
                        token.ThrowIfCancellationRequested();
                        try { cookies.SetCookies(uri, header); } catch (CookieException) { }
                        ResponseCookie delta;
                        if (!ResponseCookie.TryParse(uri, header, DateTime.UtcNow, out delta)) continue;
                        var cookie = new CefSharp.Cookie { Name = delta.Name, Value = delta.Value, Domain = delta.Domain, Path = delta.Path,
                            Secure = delta.Secure, HttpOnly = delta.HttpOnly, Expires = delta.Expires };
                        if (delta.SameSite == "lax") cookie.SameSite = CefSharp.Enums.CookieSameSite.LaxMode;
                        else if (delta.SameSite == "strict") cookie.SameSite = CefSharp.Enums.CookieSameSite.StrictMode;
                        else if (delta.SameSite == "none") cookie.SameSite = CefSharp.Enums.CookieSameSite.NoRestriction;
                        await cookieManager.SetCookieAsync(url, cookie);
                    }
                }
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        public static string Redirect(HttpResponseMessage response, string current)
        {
            var code = (int)response.StatusCode;
            if (code != 301 && code != 302 && code != 303 && code != 307 && code != 308) return null;
            if (response.Headers.Location == null) throw new IOException("downloadFailed");
            return new Uri(new Uri(current), response.Headers.Location).AbsoluteUri;
        }
        public void Dispose() { Client.Dispose(); handler.Dispose(); }
    }
}
