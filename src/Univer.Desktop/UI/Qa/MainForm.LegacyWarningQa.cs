#if LEGACY_ENGINE
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private async Task LegacyWarningQaAsync(List<string> results, string qa)
        {
            Check(!prefs.LegacyWarningDismissed && !storage.LoadPreferences().LegacyWarningDismissed, "legacy first launch requires notice", results);
            ConfirmStartupWarningForQa(results, "first launch");
            ConfirmStartupWarningForQa(results, "next launch without checkbox");
            foreach (var language in new[] { "ru", "kz", "en" })
            {
                using (var form = new LegacyWarningForm(language))
                {
                    form.Opacity = 0; form.ConfirmAsync = ConfirmLegacyWarningAsync; form.Show(this); await Task.Delay(40);
                    Check(!form.NeverShowForQa.Checked && form.ConfirmForQa.AccessibleName == Strings.Get("legacyWarningConfirm", language),
                        "legacy notice defaults to repeat and localized confirmation " + language, results);
                    SaveBitmap(form, Path.Combine(qa, "legacy-warning-" + language + ".png"));
                    form.Height = 320;
                    var confirmBounds = new System.Drawing.Rectangle(form.PointToClient(form.ConfirmForQa.PointToScreen(System.Drawing.Point.Empty)), form.ConfirmForQa.Size);
                    Check(form.ClientRectangle.Contains(confirmBounds) && form.ConfirmForQa.Visible, "legacy confirmation stays reachable in short window " + language, results);
                    SaveBitmap(form, Path.Combine(qa, "legacy-warning-short-" + language + ".png"));
                    form.Close(); Check(!form.IsDisposed, "closing notice does not bypass confirmation " + language, results);
                    form.ConfirmForQa.PerformClick(); await Task.Delay(40);
                    Check(form.IsDisposed && !prefs.LegacyWarningDismissed && !storage.LoadPreferences().LegacyWarningDismissed,
                        "unchecked confirmation permits use and repeats on relaunch " + language, results);
                }
            }
            // A real atomic-write failure: preferences.json.new cannot be replaced by a file.
            var blockedWrite = Path.Combine(storage.Root, "preferences.json.new"); Directory.CreateDirectory(blockedWrite);
            try
            {
                using (var form = new LegacyWarningForm("ru"))
                {
                    form.Opacity = 0; form.ConfirmAsync = ConfirmLegacyWarningAsync; form.Show(this);
                    form.NeverShowForQa.Checked = true; form.ConfirmForQa.PerformClick();
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (!form.ConfirmForQa.Enabled && DateTime.UtcNow < until) await Task.Delay(30);
                    Check(!form.IsDisposed && form.SaveErrorVisible && !prefs.LegacyWarningDismissed && !storage.LoadPreferences().LegacyWarningDismissed,
                        "failed warning save remains open without suppressing future launches", results);
                    SaveBitmap(form, Path.Combine(qa, "legacy-warning-save-error.png"));
                    form.NeverShowForQa.Checked = false; form.ConfirmForQa.PerformClick(); await Task.Delay(40);
                    Check(form.IsDisposed, "warning save failure permits continuing without suppression", results);
                }
            }
            finally { Directory.Delete(blockedWrite); }
            using (var form = new LegacyWarningForm("ru"))
            {
                form.Opacity = 0; form.Show(this); var calls = 0; var pending = new TaskCompletionSource<bool>();
                form.ConfirmAsync = neverShow => { calls++; return pending.Task; };
                form.NeverShowForQa.Checked = true; form.ConfirmForQa.PerformClick(); form.ConfirmForQa.PerformClick();
                Check(calls == 1 && !form.NeverShowForQa.Enabled, "warning confirmation serializes double click and freezes choice", results);
                form.Close(); Check(!form.IsDisposed, "warning waits for durable confirmation", results);
                pending.SetResult(false); await Task.Delay(40);
                Check(form.ConfirmForQa.Enabled && form.NeverShowForQa.Enabled, "failed warning confirmation enables retry", results);
                form.ConfirmAsync = ConfirmLegacyWarningAsync; form.ConfirmForQa.PerformClick();
                var until = DateTime.UtcNow.AddSeconds(5);
                while (!form.IsDisposed && DateTime.UtcNow < until) await Task.Delay(30);
                Check(form.IsDisposed && prefs.LegacyWarningDismissed && storage.LoadPreferences().LegacyWarningDismissed,
                    "checked confirmation suppresses future launches only after durable save", results);
            }
            // Exercise the actual startup entry point: a suppressed notice returns without opening a modal.
            var windows = OwnedForms.Length; ShowLegacyWarning();
            Check(OwnedForms.Length == windows, "suppressed legacy startup opens no warning dialog", results);
        }
        private void ConfirmStartupWarningForQa(List<string> results, string scenario)
        {
            var dialogs = 0;
            using (var acknowledge = new System.Windows.Forms.Timer { Interval = 40 })
            {
                acknowledge.Tick += (s, e) =>
                {
                    foreach (var owned in OwnedForms)
                    {
                        var form = owned as LegacyWarningForm; if (form == null) continue;
                        acknowledge.Stop(); dialogs++; form.ConfirmForQa.PerformClick(); return;
                    }
                };
                acknowledge.Start(); ShowLegacyWarning();
            }
            Check(dialogs == 1 && !storage.LoadPreferences().LegacyWarningDismissed, "actual modal startup notice: " + scenario, results);
        }
    }
}
#endif
