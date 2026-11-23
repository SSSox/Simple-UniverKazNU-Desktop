namespace Univer.Desktop.Core
{
    // What happens to one kept copy in memory (schedule.json, exams.json, last-grades.json) when the app's sign-in changes.
    internal enum KeptCopyStep { Keep, Load, Drop }

    // Whether the student's kept copies (schedule.json, exams.json, last-grades.json) may be read from disk and used: only while
    // the app signs in by itself (MainForm.SignsInByItself). After the site's own logout the login stays saved but automatic
    // sign-in is paused, so a copy a failed delete left behind must not reach the tray, the reminders or the offline screen.
    // The visible --qa-offline run gives its own synthetic account and reads the copies Program wrote for it.
    internal static class KeptCopies
    {
        public static bool Readable(bool signsInByItself, bool storageFailed, bool qaAccount)
        {
            return !storageFailed && (signsInByItself || qaAccount);
        }

        // A copy leaves memory as soon as the copies stop being readable (the pause takes effect before any disk write). Once
        // they are readable again (automatic sign-in resumed for the same login), a copy not held is read again, unless its
        // delete is still pending: that file belongs to a session that ended.
        public static KeptCopyStep Step(bool readable, bool held, bool clearPending)
        {
            if (!readable) return held ? KeptCopyStep.Drop : KeptCopyStep.Keep;
            return !held && !clearPending ? KeptCopyStep.Load : KeptCopyStep.Keep;
        }
    }
}
