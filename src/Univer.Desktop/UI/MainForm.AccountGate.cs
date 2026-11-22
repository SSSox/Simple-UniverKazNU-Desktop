using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // One gate for everything the app keeps or announces from the site (the week copy, the exam list, the last totals and the
    // background grade check). After the site's own logout the saved login stays but the session is gone and automatic sign-in
    // is paused, so another student can sign in by hand on the same profile; what that student's pages show must not be kept
    // or announced under the saved login. So a reading counts only when (a) the app signs in by itself (SignsInByItself) and
    // (b) the page's account mark (PageAccount) is the saved login's, an empty mark being accepted: a session from before the
    // mark existed has none, and a new profile always gets one at its first sign-in. The mark is read in the same evaluation
    // as the reading, or just before a reading that asks no page.
    internal sealed partial class MainForm
    {
        // QA: this login stands in for the saved one (and for a sign-in by itself); null in a real run.
        private string gateLoginForQa;
        private bool ReadingAllowed(string pageMark)
        {
            if (gateLoginForQa != null) return PageAccount.Accepts(gateLoginForQa, pageMark);
            return SignsInByItself() && PageAccount.Accepts(credentials?.Login, pageMark);
        }
        // The shown page's mark ("" when it has none); null when it cannot be read at all.
        private System.Threading.Tasks.Task<string> PageMarkAsync()
        {
            return Web.AutomationController.EvaluateAsync(main, PageAccount.MarkScript);
        }
    }
}
