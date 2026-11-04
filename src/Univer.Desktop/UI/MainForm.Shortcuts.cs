using System.Threading.Tasks;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // A pin or unpin clicked in "All sections" or on a home tile. The page shows the change at once;
        // the validated list is stored with the other preferences and sent back to the page.
        private async Task PinAsync(ChromiumWebBrowser browser, string payload)
        {
            if (closing || rotating) return;
            var order = Shortcuts.ApplyOrder(payload);
            var updated = order == null ? Shortcuts.Apply(prefs.Pins, payload) : null;
            if (updated == null && order == null) SetStatus("pinFailed");
            else { if (order != null) prefs.TileOrder = order; else prefs.Pins = updated; await SavePreferencesAsync(); await RefreshDocumentScriptsAsync(); }
            if (closing || browser.IsDisposed || browser != Active) return;
            await AutomationController.EvaluateAsync(browser, "window.__univerShortcutsV1&&window.__univerShortcutsV1.configure(" + Shortcuts.ToJson(prefs.Pins) + "," + Shortcuts.OrderJson(prefs.TileOrder) + ");'pins'");
        }
    }
}
