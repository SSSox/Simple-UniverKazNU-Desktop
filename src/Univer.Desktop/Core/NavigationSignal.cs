using System.Threading;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    // Completes each time the page may have changed under a pending read (a navigation began, a new document parsed).
    // Code waiting on it resumes after Raise() has returned, never inside the handler that raised it (net452 has no
    // RunContinuationsAsynchronously).
    internal sealed class NavigationSignal
    {
        private TaskCompletionSource<bool> next = new TaskCompletionSource<bool>();
        public Task Next { get { return next.Task; } }
        public void Raise()
        {
            var raised = next; next = new TaskCompletionSource<bool>();
            ThreadPool.QueueUserWorkItem(_ => raised.TrySetResult(true));
        }
    }
}
