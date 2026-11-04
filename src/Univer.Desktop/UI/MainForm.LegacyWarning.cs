#if LEGACY_ENGINE
using System;
using System.Threading.Tasks;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private void ShowLegacyWarning()
        {
            if (prefs.LegacyWarningDismissed) return;
            using (var form = new LegacyWarningForm(prefs.Language))
            {
                form.ConfirmAsync = ConfirmLegacyWarningAsync;
                form.ShowDialog(this);
            }
        }
        private async Task<bool> ConfirmLegacyWarningAsync(bool neverShow)
        {
            if (closing) return false;
            if (!neverShow) return true;
            // Never replace unreadable account preferences with fallback state just to hide a notice.
            if (storageFailed) return false;
            var updated = prefs.Clone(); updated.LegacyWarningDismissed = true;
            try
            {
                await storage.SavePreferencesAsync(updated);
                if (closing) return false;
                prefs.LegacyWarningDismissed = true;
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
#endif
