using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // The update window at the window's start, through the window's own path (EnterUpdates, the check, OfferUpdate) with
        // a local release feed: a due check, a release found earlier, a return from the tray, and a start behind the PIN.
        private async Task UpdateEntryQaAsync(List<string> results)
        {
            var setup = Encoding.UTF8.GetBytes("Synthetic installer.");
            string checksum;
            using (var hash = SHA256.Create()) checksum = BitConverter.ToString(hash.ComputeHash(setup)).Replace("-", "").ToLowerInvariant();
            var root = Path.Combine(storage.Root, "updates-entry-qa"); Directory.CreateDirectory(root);
            var application = Path.Combine(root, "application.txt"); File.WriteAllText(application, "Unsigned fixture.");
            Func<string, long, UpdateService> service = (available, checkedAt) =>
                new UpdateService(new UpdateClient(new UpdatesQaTransport(setup, checksum)), Path.Combine(root, "updates"), application, AppInfo.Version, AppInfo.Flavor, AppInfo.Architecture, true, available, null, checkedAt);
            var original = updates; var wasReady = updatesReady;
            try
            {
                AttachUpdatesQa(service(null, 0)); updatesReady = true; EnterUpdates();
                Check(await UpdateWindowQaAsync(), "at start a due check finds the new release and the update window opens", results);

                AttachUpdatesQa(service("9.9.9", DateTime.UtcNow.Ticks)); EnterUpdates();
                Check(await UpdateWindowQaAsync(), "at start a release found by an earlier check is offered, and the start checks again even right after the last check", results);

                AttachUpdatesQa(service("9.9.9", DateTime.UtcNow.Ticks));
                Hide(); EnterUpdates(); await Task.Delay(600);
                var none = !updateWindowOpen && !UpdateWindowShownQa();
                Show(); EnterUpdates();
                Check(none && await UpdateWindowQaAsync(), "in the tray nothing opens; the window's return shows the offer", results);

                lockQa = true;
                await Task.Run(() => pinGuard.Enable("135792"));
                Lock();
                AttachUpdatesQa(service("9.9.9", DateTime.UtcNow.Ticks)); EnterUpdates(); await Task.Delay(600);
                none = !updateWindowOpen && !UpdateWindowShownQa();
                lockView.Pad.TypeForQa("135792"); await LockIdleQaAsync();
                Check(none && !locked && await UpdateWindowQaAsync(), "with a PIN the offer waits behind the lock and opens after the unlock", results);
            }
            finally
            {
                if (locked || covered) Unlock();
                lockQa = false; storage.RemoveLock();
                AttachUpdatesQa(original); updatesReady = wasReady;
            }
        }
        private void AttachUpdatesQa(UpdateService next)
        {
            if (updates != null) updates.Changed -= UpdatesChanged;
            updates = next; updates.Changed += UpdatesChanged;
        }
        private static bool UpdateWindowShownQa() { return Application.OpenForms.OfType<UpdateForm>().Any(form => form.Visible); }
        // Waits for the update window, answers Later, and waits for it to close.
        private async Task<bool> UpdateWindowQaAsync()
        {
            UpdateForm form = null;
            for (var until = DateTime.UtcNow.AddSeconds(8); form == null && DateTime.UtcNow < until; await Task.Delay(50))
                form = Application.OpenForms.OfType<UpdateForm>().FirstOrDefault(f => f.Visible);
            if (form == null) return false;
            form.LaterForQa.PerformClick();
            await WaitQaAsync(() => !updateWindowOpen);
            return !updateWindowOpen;
        }
    }
}
