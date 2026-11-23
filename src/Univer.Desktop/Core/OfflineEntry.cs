using System;
using System.Globalization;

namespace Univer.Desktop.Core
{
    // When the app offers the offline screen (UI/MainForm.Offline.cs) and when a screen the student opened goes by itself.
    // The site is in trouble when its window fails or waits: a failed load the app retries (a lost connection, 5xx, 408, 429)
    // or a stopped sign-in offer the copies at once; the loading placeholder, the sign-in page and a sign-in that waits for its
    // budget offer them after WaitLimit, below the 45 s presentation deadline. Signed-in content of the site ends the trouble and
    // a screen the student opened; a calm page that is not signed-in content (a 404 of the site) ends only the trouble. Opening
    // the screen changes nothing on the site: sign-in goes on under it and no request is repeated.
    internal sealed class OfflineEntry
    {
        public const long WaitLimit = 20000;
        private long? troubleSince;
        private bool failing;
        // The student opened the screen by the offer; it stays until the site works or the student leaves it.
        public bool Chosen { get; private set; }
        // The student closed the offer: it comes back with the next trouble, not this one.
        public bool Declined { get; private set; }
        public bool Troubled(long now) { return troubleSince.HasValue && (failing || now - troubleSince.Value >= WaitLimit); }
        // One look at the site window while it is shown. Calm (neither waiting nor failing) ends the trouble.
        public void Observe(bool waiting, bool failed, long now)
        {
            if (!waiting && !failed) { troubleSince = null; failing = false; Declined = false; return; }
            if (!troubleSince.HasValue) troubleSince = now;
            failing |= failed;
        }
        // Nothing is known (the window is covered by the lock, or something else is shown): the wait starts over afterwards.
        public void Forget() { troubleSince = null; failing = false; }
        public bool Offers(long now, bool hasCopy, bool locked) { return hasCopy && !locked && !Chosen && !Declined && Troubled(now); }
        public bool Open(long now, bool hasCopy, bool locked)
        {
            if (!Offers(now, hasCopy, locked)) return false;
            Chosen = true; return true;
        }
        public void Decline() { Declined = true; }
        // The student left the screen (its button, a section, Retry): the wait starts over, so the offer returns once the trouble
        // has lasted the limit again, or at once with the next failure.
        public bool Leave() { if (!Chosen) return false; Chosen = false; troubleSince = null; failing = false; return true; }
        // The site shows signed-in content again: the trouble is over and the screen the student opened goes.
        public bool SiteWorks() { var was = Chosen; troubleSince = null; failing = false; Declined = false; Chosen = false; return was; }
        // A new session (logout, another account): nothing carries over.
        public void Reset() { troubleSince = null; failing = false; Declined = false; Chosen = false; }
        // A failed load the app reloads by itself: a network error of Chromium (not a certificate error, which stays with the
        // user, nor an aborted load) or an overloaded or unavailable server. A missing page is the site answering.
        public static bool Failure(string code)
        {
            if (string.IsNullOrEmpty(code) || code.StartsWith("Cert", StringComparison.Ordinal) || code == "Aborted") return false;
            int status;
            if (!int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out status)) return true;
            return status >= 500 || status == 408 || status == 429;
        }
    }
}
