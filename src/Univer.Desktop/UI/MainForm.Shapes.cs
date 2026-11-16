using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The loading placeholder learns each page's real layout: a while after a page is shown its boxes and text lines are
    // kept (geometry only, never text), and the next visit's placeholder is drawn from them, so the two look alike.
    internal sealed partial class MainForm
    {
        private PageShapes pageShapes;
        private string shapeRoute, shapeScript;
        private Task shapeWrite = Task.FromResult(0);
        private string ShapesPath { get { return Path.Combine(storage.Root, "page-shapes.json"); } }
        // The account review keeps what it measures in memory only.
        private void BuildShapes() { pageShapes = selfTest || accountReview ? new PageShapes() : PageShapes.Read(ShapesPath); }
        // The placeholder for a navigation: the destination's drawn shape and, in the reading layout, its layout from the last visit.
        private void ShowRoute(string url)
        {
            loading.SetRoute(url);
            shapeRoute = url == "document" ? null : url;
            loading.Captured = shapeRoute != null && prefs.Readable && pageShapes != null ? pageShapes.Find(url) : null;
        }
        private void ScheduleShape(ChromiumWebBrowser browser)
        {
            if (selfTest || pageShapes == null || !prefs.Readable || browser != main || browser.IsDisposed) return;
            var requested = shapeRoute; var epoch = sessionEpoch; var address = browser.Address;
            Dispatch(async () =>
            {
                try
                {
                    await Task.Delay(1500);
                    if (!Current(browser, epoch, address)) return;
                    if (shapeScript == null) shapeScript = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "page-shape.js"));
                    var shape = PageShape.Parse(await AutomationController.EvaluateAsync(browser, shapeScript));
                    if (shape == null || !Current(browser, epoch, address)) return;
                    pageShapes.Remember(address, shape);
                    if (requested != null && PageShapes.Key(requested) != PageShapes.Key(address)) pageShapes.Remember(requested, shape);
                    SaveShapes();
                }
                catch (Exception) { /* A page that could not be measured keeps the drawn placeholder; nothing to report. */ }
            });
        }
        // Still the same shown page of the same session, settled and in the reading layout.
        private bool Current(ChromiumWebBrowser browser, int epoch, string address)
        {
            return !closing && epoch == sessionEpoch && browser == main && Active == main && !browser.IsDisposed && browser.IsBrowserInitialized &&
                !error && !loading.Visible && prefs.Readable && browser.Address == address && SitePolicy.Trusted(address) && Ordinary(address);
        }
        // Sign-in and read-notice pages stand in for whatever page was asked for; their layout is nobody's.
        private static bool Ordinary(string address) { return !SitePolicy.LoginPage(address) && address.IndexOf("/attentionStudents", StringComparison.OrdinalIgnoreCase) < 0; }
        private void SaveShapes()
        {
            if (selfTest || accountReview) return;
            var json = pageShapes.Serialize(); var path = ShapesPath;
            shapeWrite = shapeWrite.ContinueWith(_ =>
            {
                try { AtomicFile.Write(path, new UTF8Encoding(false).GetBytes(json)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }, TaskScheduler.Default);
        }
        // A new account's pages are not the previous one's.
        private void ForgetShapes() { if (pageShapes == null) return; pageShapes.Clear(); loading.Captured = null; SaveShapes(); }
    }
}
