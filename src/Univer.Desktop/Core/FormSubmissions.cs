using System;
using System.Collections.Generic;

namespace Univer.Desktop.Core
{
    // Recent main-frame form submissions of one browser. A file that answers one is never fetched again as a GET,
    // which would silently drop the form's fields. Chromium calls in from its own threads.
    internal sealed class FormSubmissions
    {
        public const int Window = 60000, Limit = 16;
        private readonly object sync = new object();
        private readonly Dictionary<string, int> posted = new Dictionary<string, int>(StringComparer.Ordinal);
        public void Posted(string url, int now)
        {
            if (string.IsNullOrEmpty(url)) return;
            lock (sync)
            {
                posted[url] = now;
                if (posted.Count <= Limit) return;
                var oldest = ""; var age = -1;
                foreach (var pair in posted) if (unchecked(now - pair.Value) > age) { oldest = pair.Key; age = unchecked(now - pair.Value); }
                posted.Remove(oldest);
            }
        }
        public bool Answers(string url, int now)
        {
            int at;
            lock (sync) return url != null && posted.TryGetValue(url, out at) && unchecked(now - at) <= Window;
        }
    }
}
