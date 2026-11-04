using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.Web
{
    // Saves Univer files with the browser's session: HTTPS GET to the exact origin, every redirect rechecked, sign-in pages
    // and HTML refused, a per-chunk stall deadline, a 1 GB cap and a Downloads name that never replaces a file.
    internal sealed class DownloadService : IDisposable
    {
        private const long SizeLimit = 1024L * 1024 * 1024;
        private const int RedirectLimit = 6;
        private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(2);
        private readonly IRequestContext context;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly HashSet<string> running = new HashSet<string>();
        private readonly Dictionary<string, CancellationTokenSource> active = new Dictionary<string, CancellationTokenSource>();
        private readonly Action<string> report;
        private readonly Action<DownloadRecord> changed;
        public DownloadService(IRequestContext context, Action<string> report, Action<DownloadRecord> changed = null) { this.context = context; this.report = report; this.changed = changed; }
        public void Cancel(string id) { CancellationTokenSource source; if (active.TryGetValue(id, out source)) source.Cancel(); }
        // Returns the status key of the outcome. "offOrigin" and "downloadBusy" mean nothing started, so no record was
        // published; every other key describes the attempt itself.
        public async Task<string> DownloadAsync(string url, string referer, string agent, string suggestedName = null, string source = null, string addressKey = null)
        {
            if (!SitePolicy.Trusted(url)) return "offOrigin";
            if (!running.Add(url)) return "downloadBusy";
            var item = new DownloadRecord { FileName = string.IsNullOrWhiteSpace(suggestedName) ? "" : FileNames.Safe(suggestedName), Source = source ?? "", Key = addressKey ?? "", State = DownloadState.Running };
            var user = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            // A stall deadline, renewed by every received chunk: large files on slow connections still finish.
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(user.Token);
            deadline.CancelAfter(StallTimeout);
            active[item.Id] = user;
            Action publish = () => changed?.Invoke(item.Clone());
            var transfer = new Transfer { Item = item, Token = deadline.Token, Deadline = deadline, Publish = publish };
            try
            {
                report("downloading"); publish();
                using (var http = new SessionHttp(context, agent, referer, TimeSpan.FromMinutes(5)))
                using (var response = await OpenAsync(http, url, transfer))
                {
                    Check(response);
                    // Some real responses omit or use a non-standard Content-Disposition.
                    // Read the raw header: the typed parser can discard otherwise useful names.
                    IEnumerable<string> dispositions;
                    var disposition = response.Content.Headers.TryGetValues("Content-Disposition", out dispositions) ? string.Join(";", dispositions) : null;
                    var headerName = FileNames.HeaderName(disposition);
                    if (!string.IsNullOrWhiteSpace(headerName)) item.FileName = FileNames.Safe(headerName);
                    var length = response.Content.Headers.ContentLength;
                    if (length > SizeLimit) throw new IOException("downloadTooLarge");
                    item.Total = length ?? -1; publish();
                    var folder = DownloadsFolder(); Directory.CreateDirectory(folder);
                    using (var stream = await response.Content.ReadAsStreamAsync()) await WriteAsync(stream, folder, transfer);
                    if (transfer.Total == 0 || (length.HasValue && transfer.Total != length.Value)) throw new IOException("downloadFailed");
                    transfer.Token.ThrowIfCancellationRequested();
                    var type = response.Content.Headers.ContentType;
                    var name = FileNames.Resolve(disposition, suggestedName, transfer.Url, type == null ? null : type.MediaType, transfer.Prefix, transfer.PrefixLength, transfer.Temp);
                    item.Path = Keep(transfer.Temp, folder, name); transfer.Temp = null;
                    item.FileName = Path.GetFileName(item.Path);
                    item.Received = item.Total = transfer.Total; item.State = DownloadState.Completed; item.Finished = DateTime.Now; publish();
                    report("downloadComplete"); return "downloadComplete";
                }
            }
            catch (OperationCanceledException)
            {
                var byUser = user.IsCancellationRequested && !cancellation.IsCancellationRequested;
                Finish(item, cancellation.IsCancellationRequested || byUser ? DownloadState.Canceled : DownloadState.Failed, byUser ? "downloadCanceled" : "downloadFailed");
                if (!cancellation.IsCancellationRequested) report(byUser ? "downloadCanceled" : "downloadFailed");
                publish(); return item.Error;
            }
            catch (IOException e)
            {
                var key = DownloadOutcome.Key(e.Message);
                Finish(item, DownloadState.Failed, key); report(key); publish(); return key;
            }
            catch (Exception) { Finish(item, DownloadState.Failed, "downloadFailed"); report("downloadFailed"); publish(); return "downloadFailed"; }
            finally
            {
                active.Remove(item.Id); deadline.Dispose(); user.Dispose(); running.Remove(url);
                if (transfer.Temp != null) { try { File.Delete(transfer.Temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
        // One download in progress: where it landed after redirects, its temporary file and what has arrived so far.
        private sealed class Transfer
        {
            public DownloadRecord Item; public CancellationToken Token; public CancellationTokenSource Deadline; public Action Publish;
            public string Url, Temp; public long Total;
            // The first bytes, kept for recognising sign-in pages and the file type.
            public byte[] Prefix = new byte[4096]; public int PrefixLength;
        }
        // Follows the server's redirects within Univer and returns the final answer. Sign-in and read-notice pages,
        // whether reached directly or by a redirect, mean the session has to be renewed first.
        private static async Task<HttpResponseMessage> OpenAsync(SessionHttp http, string url, Transfer transfer)
        {
            var next = url;
            for (var redirects = 0; redirects < RedirectLimit; redirects++)
            {
                if (!SitePolicy.Trusted(next)) throw new IOException("offOrigin");
                var path = new Uri(next).AbsolutePath;
                if (path.StartsWith("/user/login", StringComparison.OrdinalIgnoreCase) || path.IndexOf("/attentionStudents/", StringComparison.OrdinalIgnoreCase) >= 0) throw new IOException("downloadLogin");
                var response = await http.GetAsync(next, transfer.Token);
                string redirect;
                try { redirect = SessionHttp.Redirect(response, next); }
                catch { response.Dispose(); throw; }
                if (redirect == null) { transfer.Url = next; return response; }
                response.Dispose(); next = redirect;
            }
            throw new IOException("downloadFailed");
        }
        // Only a plain, unencoded 200 answer that is not a web page is a file.
        private static void Check(HttpResponseMessage response)
        {
            var code = (int)response.StatusCode;
            if (code == 213 || code == 401) throw new IOException("downloadLogin");
            if (code != 200) throw new IOException("downloadFailed");
            foreach (var encoding in response.Content.Headers.ContentEncoding)
                if (!encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)) throw new IOException("downloadFailed");
            var type = response.Content.Headers.ContentType;
            if (type != null && (type.MediaType == "text/html" || type.MediaType == "application/xhtml+xml")) throw new IOException("downloadLogin");
        }
        // Reads the answer into a temporary file in folder, publishing progress a few times a second.
        private static async Task WriteAsync(Stream stream, string folder, Transfer transfer)
        {
            var prefix = transfer.Prefix; var count = 0;
            while (count < prefix.Length)
            {
                var read = await CancellableRead.ReadAsync(stream, prefix, count, prefix.Length - count, transfer.Token);
                if (read == 0) break; count += read;
            }
            transfer.PrefixLength = count;
            if (FileNames.LoginHtml(prefix, count)) throw new IOException("downloadLogin");
            transfer.Temp = Path.Combine(folder, PartialDownloads.NewName());
            long total = count; transfer.Item.Received = total;
            var clock = Stopwatch.StartNew();
            using (var file = new FileStream(transfer.Temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await file.WriteAsync(prefix, 0, count, transfer.Token);
                var buffer = new byte[65536]; int chunk;
                while ((chunk = await CancellableRead.ReadAsync(stream, buffer, 0, buffer.Length, transfer.Token)) > 0)
                {
                    total += chunk;
                    if (total > SizeLimit) throw new IOException("downloadTooLarge");
                    await file.WriteAsync(buffer, 0, chunk, transfer.Token);
                    transfer.Item.Received = total; transfer.Deadline.CancelAfter(StallTimeout);
                    if (clock.ElapsedMilliseconds >= 150) { clock.Restart(); transfer.Publish(); }
                }
            }
            transfer.Total = total;
        }
        // Moves the finished file to its name in folder, adding " (1)", " (2)"… when that name is taken: File.Move never
        // overwrites a user's existing file.
        private static string Keep(string temp, string folder, string name)
        {
            for (var i = 0; ; i++)
            {
                var destination = Path.Combine(folder, i == 0 ? name : Path.GetFileNameWithoutExtension(name) + " (" + i + ")" + Path.GetExtension(name));
                try { File.Move(temp, destination); return destination; }
                catch (IOException) { if (i >= 999 || !File.Exists(destination)) throw; }
            }
        }
        private static void Finish(DownloadRecord item, DownloadState state, string error)
        {
            item.State = state; item.Error = error; item.Finished = DateTime.Now;
            if (string.IsNullOrEmpty(item.FileName)) item.FileName = "Univer-document";
        }
        public void Dispose() { cancellation.Cancel(); }
        public static string DownloadsFolder()
        {
            IntPtr pointer;
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pointer) == 0)
            { try { return Marshal.PtrToStringUni(pointer); } finally { Marshal.FreeCoTaskMem(pointer); } }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    }
}
