using System;
using System.Threading;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    internal sealed class AsyncOperationGate
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public async Task RunAsync(Func<Task> operation)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { await operation().ConfigureAwait(false); }
            finally { gate.Release(); }
        }
    }
}
