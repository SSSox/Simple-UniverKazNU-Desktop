using System.Collections.Generic;

namespace Univer.Desktop.Core
{
    // Bounds automatic sign-in without handing it to the user unless the password is proven wrong: stalls, refused AJAX
    // challenges and a full budget are waits; only an explicit stop or two full-page refusals in a row need the user.
    internal sealed class SessionGuard
    {
        // Three full-page logins per ten minutes; a block lifts at the earliest a minute after it began.
        public const long LoginWindow = 600000, RetrySpacing = 60000, AnswerLimit = 25000;
        public bool Blocked { get; private set; }
        public string LoginDocument { get; private set; }
        public string ReauthChallenge { get; private set; }
        // The page outlived its presentation deadline: it stays uncovered until the next navigation while sign-in goes on.
        public bool CoverSuspended { get; private set; }
        private long? loginAt, reauthAt, blockedAt;
        private readonly Queue<long> loginAttempts = new Queue<long>();
        private readonly Queue<long> reauthAttempts = new Queue<long>();
        private string completedReauth, reauthDocument, blockedDocument;
        private bool stopped;
        // Full-page logins the site answered with its login form again, since the last confirmed sign-in.
        private int rejections;
        // A hard stop that needs the user (Retry or Settings), unlike a block that a newer document lifts by itself.
        public bool Stopped { get { return Blocked && stopped; } }
        public readonly ConfirmationBudget Notices = new ConfirmationBudget();
        public readonly ConfirmationBudget Agreements = new ConfirmationBudget();
        private bool LoginBudget(long now)
        {
            while (loginAttempts.Count > 0 && now - loginAttempts.Peek() >= LoginWindow) loginAttempts.Dequeue();
            return loginAttempts.Count < 3;
        }
        private void Block(string document, long at) { Blocked = true; blockedDocument = document; blockedAt = at; }
        private void Lift() { Blocked = false; blockedDocument = null; blockedAt = null; }
        private bool Liftable(string document, long now)
        { return Blocked && !stopped && document != blockedDocument && now - blockedAt.GetValueOrDefault() >= RetrySpacing; }
        // Whether only a newer document can continue: the page that stalled or was refused is still shown, so a reload
        // brings the next one.
        public bool NeedsNewDocument(string document) { return Blocked && !stopped && document == blockedDocument; }
        public bool TryLogin(string document, long now)
        {
            if (Liftable(document, now) && LoginBudget(now)) Lift();
            // A full budget is a wait: the next login follows by itself once the oldest attempt leaves the window.
            if (Blocked || LoginDocument != null || ReauthChallenge != null || !LoginBudget(now)) return false;
            loginAttempts.Enqueue(now);
            // The new server session asks for its own notice and agreement: a reservation from the ended one (sent just
            // before the session ran out, with only the login form in between) must not hide them.
            Notices.Forget(); Agreements.Forget();
            LoginDocument = document; loginAt = now; return true;
        }
        // The login form is shown. A new document after a submit is a refusal (one more try a minute later, the second stops);
        // the same document past the answer limit is a stall a newer one continues. True while this document may not sign in.
        public bool LoginVisible(string document, long now)
        {
            if (LoginDocument != null)
            {
                var submitted = LoginDocument;
                if (submitted != document) { LoginDocument = null; if (++rejections >= 2) Stop(); else Block(submitted, now); }
                else if (now - loginAt.Value > AnswerLimit) { LoginDocument = null; Block(submitted, now); }
            }
            return Blocked && !Liftable(document, now);
        }
        public void Authenticated() { LoginDocument = null; rejections = 0; }
        // A page of the signed-in account newer than the block proves the session works again (a late answer, a manual
        // sign-in): every block but a hard stop ends.
        public bool AuthenticatedPage(string document)
        {
            if (!Blocked || stopped || document == blockedDocument) return false;
            Lift(); rejections = 0; return true;
        }
        // The page confirmed that nothing was submitted (for example the site disabled the button after the probe),
        // so the reservation and its attempt are returned and the next tick may try again.
        public void ReleaseLogin(string document)
        {
            if (LoginDocument != document) return;
            LoginDocument = null; loginAt = null; DropLast(loginAttempts);
        }
        public void ReleaseReauth(string challenge)
        {
            if (ReauthChallenge != challenge) return;
            ReauthChallenge = null; reauthAt = null; DropLast(reauthAttempts);
        }
        internal static void DropLast(Queue<long> queue)
        {
            if (queue.Count == 0) return;
            var kept = queue.ToArray(); queue.Clear();
            for (var i = 0; i < kept.Length - 1; i++) queue.Enqueue(kept[i]);
        }
        public bool TryReauth(string challenge, string document, long now)
        {
            if (Liftable(document, now)) Lift();
            if (Blocked || ReauthChallenge != null || challenge == completedReauth) return false;
            while (reauthAttempts.Count > 0 && now - reauthAttempts.Peek() >= 60000) reauthAttempts.Dequeue();
            // A burst of verified challenges waits for the budget instead of asking for the password again.
            if (reauthAttempts.Count >= 3) return false;
            reauthAttempts.Enqueue(now);
            ReauthChallenge = challenge; reauthDocument = document; reauthAt = now; return true;
        }
        // A refused challenge does not prove the password wrong (a dialog whose key went stale is refused too): the
        // full-page login of a newer document signs in again.
        public void ReauthResult(string challenge, bool success)
        {
            if (ReauthChallenge != challenge) return;
            ReauthChallenge = null;
            if (success) completedReauth = challenge; else if (!Blocked) Block(reauthDocument, reauthAt.GetValueOrDefault());
        }
        // The site's dialog counts a second try of its own.
        public void ReauthRejected(string document, long now) { ReauthChallenge = null; if (!stopped && blockedDocument != document) Block(document, now); }
        public bool ReauthTimedOut(long now)
        {
            if (ReauthChallenge != null && now - reauthAt.Value > AnswerLimit) { ReauthChallenge = null; if (!Blocked) Block(reauthDocument, now); }
            return Blocked;
        }
        public void Stop() { Blocked = true; stopped = true; LoginDocument = null; ReauthChallenge = null; }
        // The presentation deadline passed. True unless a hard stop already needs the user.
        public bool Suspend() { CoverSuspended = true; return !Stopped; }
        public void Navigated() { CoverSuspended = false; }
        public void Retry()
        {
            Blocked = false; stopped = false; CoverSuspended = false; LoginDocument = null; ReauthChallenge = null;
            loginAt = null; reauthAt = null; blockedAt = null; blockedDocument = null; reauthDocument = null; completedReauth = null;
            rejections = 0; reauthAttempts.Clear(); loginAttempts.Clear();
            Notices.Reset(); Agreements.Reset();
        }
    }
    internal sealed class ConfirmationBudget
    {
        private readonly HashSet<string> reserved = new HashSet<string>();
        private readonly Queue<long> attempts = new Queue<long>();
        private long? absent;
        public bool CanSubmit(string key, long now)
        {
            while (attempts.Count > 0 && now - attempts.Peek() >= 60000) attempts.Dequeue();
            return !reserved.Contains(key) && reserved.Count < 20 && attempts.Count < 3;
        }
        public void Submitted(string key, long now) { reserved.Add(key); attempts.Enqueue(now); absent = null; }
        // Nothing was submitted: give the reservation and its attempt back.
        public void Release(string key)
        {
            if (reserved.Remove(key)) SessionGuard.DropLast(attempts);
        }
        public void Observe(bool present, long now)
        {
            if (present) absent = null;
            else { if (!absent.HasValue) absent = now; if (now - absent.Value >= 2000) reserved.Clear(); }
        }
        // An automatic reload of an unconfirmed page, or a new server session: the form may be sent again, still within the
        // attempt budget.
        public void Forget() { reserved.Clear(); absent = null; }
        public void Reset() { reserved.Clear(); attempts.Clear(); absent = null; }
    }
    // The answer limit of one appearance of the notice or agreement. Each appearance (a new document, or the form shown
    // again) starts its own limit: an earlier one, for example before a login page in between, does not count against it.
    internal sealed class AppearanceTimer
    {
        private string episode;
        private long began;
        public bool Expired(string appearance, long now, long limit)
        {
            if (appearance != episode) { episode = appearance; began = now; }
            return now - began > limit;
        }
        public void Reset() { episode = null; }
    }
}
