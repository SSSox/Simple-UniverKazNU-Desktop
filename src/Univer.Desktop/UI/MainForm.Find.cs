using System;
using CefSharp.WinForms;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private string findPreview;
        // Ctrl+F, F3 and the toolbar button search the website or printed document in the main view, or the file shown in
        // Downloads. The Downloads list has its own search field, which takes the keyboard instead.
        private void OpenFind()
        {
            if (closing || findBar == null) return;
            if (DownloadsVisible)
            {
                var viewer = downloadsView.FindTarget;
                if (viewer == null) { if (!downloadsView.Previewing) downloadsView.FocusSearch(); return; }
                findPreview = downloadsView.PreviewName; findBar.Open(viewer, true); PlaceFind(); return;
            }
            var browser = Active; if (browser == null || !browser.IsBrowserInitialized) return;
            findPreview = null; findBar.Open(browser, document != null); PlaceFind();
        }
        private void PlaceFind()
        {
            if (findBar == null) return;
            var width = Math.Min(D(440), Math.Max(D(260), workspace.ClientSize.Width - D(32)));
            // Over a Downloads preview the bar sits below the file's own toolbar, which grows with the app zoom.
            var top = canvas.Top + D(10) + (DownloadsVisible && downloadsView.Previewing ? downloadsView.PreviewBarHeight : 0);
            findBar.SetBounds(workspace.ClientSize.Width - D(16) - width, top, width, D(48));
            if (findBar.Visible) findBar.BringToFront();
        }
        // The searched page or file went away (a navigation, a closed document, another file or view): its matches are gone.
        private void ForgetFind(ChromiumWebBrowser browser) { findBar?.Forget(browser); }
        private void CheckFindTarget()
        {
            if (findBar == null || !findBar.Searching) return;
            var expected = DownloadsVisible ? downloadsView.FindTarget : Active;
            if (findBar.Target != expected || (findPreview != null && (!DownloadsVisible || downloadsView.PreviewName != findPreview))) findBar.Close();
            else PlaceFind();
        }
    }
}