using System;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    // One run at a time on the UI thread. Calls that come during a run fold into one more run right after it, with the
    // latest call's arguments: the browser, credentials and preferences may have been replaced meanwhile.
    internal sealed class Coalescer<T> where T : class
    {
        private bool busy;
        private T latest;
        public async Task RunAsync(T value, Func<T, Task> run, Func<T, bool> valid)
        {
            if (busy) { latest = value; return; }
            busy = true;
            try
            {
                for (var current = value; current != null; )
                {
                    latest = null;
                    await run(current);
                    current = latest != null && valid(latest) ? latest : null;
                }
            }
            finally { busy = false; latest = null; }
        }
        // A navigation began (a viewer opened over the page, the page is being replaced): a call stored before it was made
        // for what was shown then; calls after it fold as usual.
        public void Drop() { latest = null; }
    }
}
