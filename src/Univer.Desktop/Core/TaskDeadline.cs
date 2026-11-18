using System;
using System.Threading;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    internal static class TaskDeadline
    {
        public static async Task AwaitAsync(Task operation, TimeSpan timeout, CancellationToken cancellation)
        {
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var delay = Task.Delay(timeout, wait.Token);
                if (await Task.WhenAny(operation, delay).ConfigureAwait(false) != operation)
                {
                    // CEF may finish after the caller stops waiting. Observe a later fault.
                    ObserveLateFailure(operation);
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException("Operation deadline exceeded");
                }
                wait.Cancel();
                await operation.ConfigureAwait(false);
            }
        }
        // The operation's answer, or the fallback as soon as stop completes first; a later fault is observed.
        public static async Task<T> UntilAsync<T>(Task<T> operation, Task stop, T fallback)
        {
            if (await Task.WhenAny(operation, stop).ConfigureAwait(false) == operation) return await operation.ConfigureAwait(false);
            ObserveLateFailure(operation);
            return fallback;
        }
        private static void ObserveLateFailure(Task operation)
        {
            operation.ContinueWith(finished => { var observed = finished.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
