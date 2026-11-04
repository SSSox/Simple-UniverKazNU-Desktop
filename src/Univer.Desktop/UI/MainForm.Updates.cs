using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private UpdateService updates;
        private bool updatesReady, updateWindowOpen;
        private void BuildUpdates()
        {
            var application = AppDomain.CurrentDomain.BaseDirectory;
            string marker = null;
            try { marker = File.ReadAllText(Path.Combine(application, "installation-id.txt")); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            updates = new UpdateService(new UpdateClient(), Path.Combine(storage.Root, "updates"), Path.Combine(application, "Univer.Desktop.exe"), AppInfo.Version,
                AppInfo.Flavor, AppInfo.Architecture, Updates.Installed(marker, AppInfo.Flavor, AppInfo.Architecture), prefs.UpdateVersion, prefs.UpdateSkipped, prefs.UpdateChecked);
            updates.Changed += UpdatesChanged;
            updates.CleanUp();
        }
        // Every return to the window: the check when it is due, and the offer when one is waiting.
        private void EnterUpdates()
        {
            if (!updatesReady || closing) return;
            QueueAsync(() => updates.CheckIfDueAsync(DateTime.UtcNow.Ticks));
            OfferUpdate();
        }
        private void UpdatesChanged()
        {
            var available = updates.Available ?? ""; var skipped = updates.Skipped ?? "";
            if (prefs.UpdateVersion != available || prefs.UpdateSkipped != skipped || prefs.UpdateChecked != updates.CheckedAt)
            {
                prefs.UpdateVersion = available; prefs.UpdateSkipped = skipped; prefs.UpdateChecked = updates.CheckedAt;
                // Never replace unreadable preferences with defaults just to remember a check.
                if (!storageFailed) QueueAsync(SavePreferencesAsync);
            }
            OfferUpdate();
        }
        // Waits for the settings window and any running command; the next return to the window tries again.
        private void OfferUpdate()
        {
            if (!updatesReady || closing || settingsOpen || commandBusy || updateWindowOpen || !Visible || WindowState == FormWindowState.Minimized || !updates.ShouldPrompt) return;
            updateWindowOpen = true;
            Dispatch(() => ShowUpdate(false));
        }
        private void ShowUpdate(bool installNow)
        {
            updateWindowOpen = true;
            string setup = null;
            try
            {
                if (closing || updates.Available == null) return;
                using (var form = new UpdateForm(updates, prefs.Language, installNow))
                {
                    form.OpenPage = () => External(Updates.ReleasePage(updates.Available));
                    form.ShowDialog(this);
                    setup = form.SetupPath;
                }
            }
            finally { updateWindowOpen = false; }
            if (setup != null) LaunchUpdate(setup);
        }
        private void LaunchUpdate(string setup)
        {
            try { Process.Start(new ProcessStartInfo(setup, Updates.SetupArguments(AppDomain.CurrentDomain.BaseDirectory, prefs.Language)) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(setup) }); }
            catch (Exception) { updates.Report(UpdateFailure.Install); return; }
            // The installer waits for this process to end and starts the new version itself.
            ExitApplication();
        }
    }
}
