using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    internal static class CancellableRead
    {
        // .NET Framework's response stream checks the token only before a read, so a stalled connection ignores Cancel until
        // the socket's five-minute timeout; closing the stream ends the read, reported as the cancellation it was.
        public static async Task<int> ReadAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (token.Register(state => { try { ((Stream)state).Dispose(); } catch (Exception) { } }, stream))
            {
                try { return await stream.ReadAsync(buffer, offset, count, token); }
                catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            }
        }
    }
}
