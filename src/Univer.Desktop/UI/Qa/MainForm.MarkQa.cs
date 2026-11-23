using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The QA steps that read site pages plant their own account mark (PageAccount); each gives back the mark the site's storage
    // held before it, so a later step (or the session the review runs in) sees the same account as before.
    internal sealed partial class MainForm
    {
        private static readonly Regex MarkShape = new Regex("^a[0-9a-f]{1,8}$", RegexOptions.CultureInvariant);
        // The mark before the step, read only while the shown page is the site's (localStorage belongs to its origin); null
        // when there is none or it could not be read.
        private async Task<string> KeepMarkForQaAsync()
        {
            if (main == null || main.IsDisposed || !SitePolicy.Trusted(main.Address)) return null;
            try { var mark = await PageMarkAsync(); return mark != null && MarkShape.IsMatch(mark) ? mark : null; }
            catch (Exception) { return null; }
        }
        private Task<string> RestoreMarkForQaAsync(string previous)
        {
            var script = previous != null && MarkShape.IsMatch(previous)
                ? "localStorage.setItem('" + PageAccount.Key + "','" + previous + "');'restored'"
                : "localStorage.removeItem('" + PageAccount.Key + "');'cleared'";
            return AutomationController.EvaluateAsync(main, script);
        }
    }
}
