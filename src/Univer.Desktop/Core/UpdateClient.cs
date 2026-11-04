using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    // Reads releases from public GitHub addresses. The API is not used: a campus shares one address and its request limit.
    internal sealed class UpdateClient : IDisposable
    {
        private const int MaxRedirects = 5, MaxChecksumBytes = 4096;
        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30), Stall = TimeSpan.FromSeconds(60);
        private readonly HttpClient client;
        public UpdateClient(HttpMessageHandler handler = null)
        {
            // Redirects are followed by hand, so each hop is checked before it is requested.
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Univer-Desktop");
        }
        public async Task<string> LatestVersionAsync(CancellationToken token)
        {
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = new HttpRequestMessage(HttpMethod.Head, Updates.Latest))
            {
                limit.CancelAfter(Deadline);
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token))
                {
                    if (!Redirected(response)) throw new IOException("Unexpected answer: " + (int)response.StatusCode);
                    return Updates.Version(Target(response, Updates.Latest));
                }
            }
        }
        public async Task<string> ChecksumAsync(string url, CancellationToken token)
        {
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(Deadline);
                using (var response = await OpenAsync(url, limit.Token))
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var buffer = new byte[MaxChecksumBytes];
                    var length = 0; int read;
                    while (length < buffer.Length && (read = await CancellableRead.ReadAsync(stream, buffer, length, buffer.Length - length, limit.Token)) > 0) length += read;
                    var hash = Updates.Checksum(Encoding.UTF8.GetString(buffer, 0, length));
                    if (hash == null) throw new IOException("No checksum in the release");
                    return hash;
                }
            }
        }
        // Returns the SHA-256 of what was written, in lower case.
        public async Task<string> DownloadAsync(string url, Stream output, IProgress<int> progress, CancellationToken token)
        {
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(Deadline);
                using (var response = await OpenAsync(url, limit.Token))
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var hash = SHA256.Create())
                {
                    var length = response.Content.Headers.ContentLength ?? -1;
                    if (length > Updates.MaxSetupBytes) throw new IOException("Update file is too large");
                    var buffer = new byte[81920];
                    long total = 0; var shown = -1;
                    while (true)
                    {
                        // Each read has its own limit: a stalled connection fails instead of holding the window open.
                        limit.CancelAfter(Stall);
                        var read = await CancellableRead.ReadAsync(stream, buffer, 0, buffer.Length, limit.Token);
                        if (read == 0) break;
                        total += read;
                        if (total > Updates.MaxSetupBytes) throw new IOException("Update file is too large");
                        hash.TransformBlock(buffer, 0, read, null, 0);
                        await output.WriteAsync(buffer, 0, read, limit.Token);
                        var percent = length > 0 ? (int)(total * 100 / length) : 0;
                        if (percent != shown) { shown = percent; progress?.Report(percent); }
                    }
                    if (length >= 0 && total != length) throw new IOException("Update file ended early");
                    hash.TransformFinalBlock(buffer, 0, 0);
                    return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        private async Task<HttpResponseMessage> OpenAsync(string url, CancellationToken token)
        {
            for (var hop = 0; hop <= MaxRedirects; hop++)
            {
                if (!Updates.AllowedHost(url)) throw new IOException("Refused address");
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                if (response.IsSuccessStatusCode) return response;
                using (response)
                {
                    if (!Redirected(response)) throw new IOException("Unexpected answer: " + (int)response.StatusCode);
                    url = Target(response, url);
                }
            }
            throw new IOException("Too many redirects");
        }
        private static bool Redirected(HttpResponseMessage response) { var status = (int)response.StatusCode; return status >= 300 && status < 400 && response.Headers.Location != null; }
        private static string Target(HttpResponseMessage response, string from)
        {
            var location = response.Headers.Location;
            return (location.IsAbsoluteUri ? location : new Uri(new Uri(from), location)).AbsoluteUri;
        }
        public void Dispose() { client.Dispose(); }
    }
}
