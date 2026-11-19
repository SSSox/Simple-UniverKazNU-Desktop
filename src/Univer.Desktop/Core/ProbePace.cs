namespace Univer.Desktop.Core
{
    // How often a quiet page is asked for its state. A page that is shown, signed in (or left to the user) and waits for
    // nothing tells the app itself when that changes: the paint gate reports a known form, the document script reports the
    // user's presses. So it is asked every second only for a while after a press, and rarely otherwise; a window in the
    // tray or minimized is asked least. Times in milliseconds.
    internal static class ProbePace
    {
        public const int Active = 10000, Shown = 15000, Hidden = 30000;
        // sinceInput: since the user's last press on the page or the window's activation; sinceProbe: since the last tick.
        public static bool Due(bool shown, long sinceInput, long sinceProbe)
        {
            if (shown && sinceInput >= 0 && sinceInput < Active) return true;
            return sinceProbe < 0 || sinceProbe >= (shown ? Shown : Hidden);
        }
    }
}
