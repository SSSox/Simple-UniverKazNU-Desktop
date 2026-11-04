using System;
using System.Windows.Forms;
using Microsoft.Win32;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // Light, dark or Windows' app mode. The palette is set before the window is built (Program); a later change recolours
    // every open window, the menus, downloads, file preview and the site's reading layout (Original keeps the site's look).
    internal sealed partial class MainForm
    {
        private void HookTheme()
        {
            Theme.Window(this);
            SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
            FormClosed += (s, e) => SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
        }
        // Windows raises this on its own thread when the app mode (or any personalization) changes.
        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle || e.Category == UserPreferenceCategory.Color)
                Dispatch(() => { if (ThemeChoice.Normalize(prefs.Theme) == ThemeChoice.System && !selfTest) ApplyTheme(); });
        }
        // Self-test runs light whatever Windows uses, except where its theme step asks for Windows' mode.
        private bool qaFollowsWindows;
        private bool DarkWanted { get { return ThemeChoice.IsDark(prefs.Theme, (!selfTest || qaFollowsWindows) && Theme.SystemDark()); } }
        private void ApplyTheme(bool? dark = null)
        {
            if (closing || !Theme.Set(dark ?? DarkWanted)) return;
            foreach (Form form in Application.OpenForms) Theme.Recolor(form);
            Theme.Style(viewMenu); if (trayMenu != null) Theme.Style(trayMenu);
            downloadsView?.ApplyTheme(); findBar?.Invalidate(true); toast?.Invalidate(true);
            RenderFeedback(); sidebar.Invalidate(true); toolbar.Invalidate(true); loading.Invalidate();
            if (automation != null) automation.Dark = Theme.Dark;
            // The next page starts in the new theme; the shown one switches now.
            QueueAsync(RefreshDocumentScriptsAsync);
            var script = "window.__univerTheme=" + (Theme.Dark ? "'dark'" : "'light'") + ";window.__univerThemeV1&&window.__univerThemeV1.refresh();'theme'";
            foreach (var browser in new[] { main, document })
                if (browser != null && !browser.IsDisposed && browser.IsBrowserInitialized) QueueAsync(() => AutomationController.EvaluateAsync(browser, script));
        }
    }
}
