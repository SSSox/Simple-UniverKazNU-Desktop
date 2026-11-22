using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // The app PIN in the isolated profile: setting it, the lock over the whole window, keys and actions that do nothing
        // while locked (typing into the page included), wrong guesses and their wait, the quiet return within the minute, an
        // unreadable record, and the tenth wrong guess ending the saved account through logout.
        private async Task LockQaAsync(string qa, List<string> results)
        {
            lockQa = true;
            try
            {
                storage.RemoveLock();
                using (var form = new SettingsForm(null, prefs) { PinSet = () => PinSet })
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); form.SelectUpdatesForQa(); await Task.Delay(60);
                    Check(form.SecurityHintForQa == Strings.Get("securityOff", prefs.Language), "Settings says no PIN is set", results);
                    SaveBitmap(form, Path.Combine(qa, "pin-settings-off.png")); form.Close();
                }
                using (var dialog = new PinDialog(PinDialogMode.Create, "ru", null))
                {
                    dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(this); await Task.Delay(60);
                    SaveBitmap(dialog, Path.Combine(qa, "pin-create.png"));
                    var pad = dialog.PadForQa;
                    pad.TypeForQa("111111");
                    Check(pad.MessageForQa == Strings.Get("lockWeakRepeated", "ru") && pad.CountForQa == 0, "an easy PIN is refused with its reason", results);
                    pad.TypeForQa("135792"); pad.TypeForQa("135790");
                    Check(pad.MessageForQa == Strings.Get("lockMismatch", "ru") && dialog.DialogResult != DialogResult.OK, "a second typing that differs starts over", results);
                    pad.TypeForQa("135792"); pad.TypeForQa("135792");
                    Check(dialog.DialogResult == DialogResult.OK && dialog.NewPin == "135792", "the same PIN typed twice is accepted", results);
                    dialog.Close();
                }
                // In Settings the window is already open: a record that cannot be read for a moment ends nothing.
                using (var dialog = new PinDialog(PinDialogMode.Change, "ru", guess => Task.FromResult(new PinResult(PinOutcome.Unavailable))))
                {
                    dialog.Opacity = 0; dialog.ShowInTaskbar = false; dialog.Show(this); await Task.Delay(60);
                    dialog.PadForQa.TypeForQa("135792"); await Task.Delay(150);
                    Check(dialog.DialogResult != DialogResult.Abort && !dialog.Wiped && dialog.PadForQa.MessageForQa == Strings.Get("lockFailed", "ru"),
                        "in Settings an unreadable record says the change failed and ends nothing", results);
                    dialog.Close();
                }
                foreach (var lang in new[] { "ru", "kz", "en" })
                    using (var offer = new ChoiceForm(Strings.Get("offerPinTitle", lang), Strings.Get("offerPinMessage", lang), Strings.Get("offerPinSet", lang), Strings.Get("offerPinLater", lang), false))
                    { offer.Opacity = 0; offer.ShowInTaskbar = false; offer.Show(this); await Task.Delay(40); SaveBitmap(offer, Path.Combine(qa, "pin-offer-" + lang + ".png")); offer.Close(); }
                await Task.Run(() => pinGuard.Enable("135792"));
                Check(storage.LockExists, "the PIN is stored", results);
                // A login sealed under the PIN still opens the chosen start page: the app signs in once the PIN is entered.
                {
                    var credentialsWere = credentials; var pageWas = prefs.StartPage; var pausedWas = prefs.AutoLoginPaused;
                    try
                    {
                        prefs.StartPage = "schedule"; prefs.AutoLoginPaused = false;
                        credentials = new Credentials { Login = "qa-sealed", Sealed = true };
                        var sealedStart = StartAddress() == SitePolicy.Origin + "/student/myschedule/";
                        prefs.AutoLoginPaused = true; var pausedStart = StartAddress() == SitePolicy.Home;
                        Check(sealedStart && pausedStart, "a login sealed under the PIN opens the chosen start page, a paused one the home page", results);
                    }
                    finally { credentials = credentialsWere; prefs.StartPage = pageWas; prefs.AutoLoginPaused = pausedWas; }
                }
                foreach (var lang in new[] { "ru", "kz", "en" })
                {
                    var shown = prefs.Clone(); shown.Language = lang;
                    using (var form = new SettingsForm(null, shown) { PinSet = () => PinSet })
                    {
                        form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); form.SelectUpdatesForQa(); await Task.Delay(60);
                        Check(form.SecurityHintForQa == Strings.Get("securityOn", lang) && form.OverflowForQa().Length == 0, "Settings says the PIN is on, nothing clipped " + lang, results);
                        SaveBitmap(form, Path.Combine(qa, "pin-settings-on-" + lang + ".png")); form.Close();
                    }
                }

                // Typing reaches the page while the app is open (so the check below can fail), and nothing does while locked.
                main.LoadHtml("<input id='q' autofocus><script>window.qaMark='1'</script>", SitePolicy.Origin + "/my/lock-qa/");
                var until = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < until && (main.IsLoading || await Web.AutomationController.EvaluateAsync(main, "document.getElementById('q')?'ok':''") != "ok")) await Task.Delay(100);
                await Web.AutomationController.EvaluateAsync(main, "document.getElementById('q').focus();'ok'");
                var host = main.GetBrowser().GetHost(); host.SetFocus(true); await Task.Delay(200);
                Check(await TypeSettledAsync(host, 'a', "") == "a", "typing reaches the page while the app is open", results);
                Lock();
                Check(locked && LockGate.Engaged && lockView.Visible && lockView.Bounds == ClientRectangle && Controls.GetChildIndex(lockView) == 0 && ContainsFocus && !main.ContainsFocus,
                    "the lock covers the whole window, sidebar included, and takes the focus", results);
                SaveBitmap(lockView, Path.Combine(qa, "lock-ru.png"));
                // The input keeps the page's focus and Chromium gets the window's: only the lock can stop the typing.
                await Web.AutomationController.EvaluateAsync(main, "document.getElementById('q').focus();'ok'"); host.SetFocus(true); await Task.Delay(200);
                // As many presses, as far apart, as TypeSettledAsync gives an open page: Chromium 109 drops the first one by
                // itself, and that must not pass for the lock's work.
                for (var press = 0; press < 4; press++) { TypeQa(host, 'b'); await Task.Delay(800); }
                host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = (int)Keys.F5 }); host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = (int)Keys.F5 });
                foreach (var key in new[] { Keys.Control | Keys.Oemcomma, Keys.Control | Keys.J, Keys.Control | Keys.F, Keys.F5, Keys.Control | Keys.P })
                { var message = new Message(); ProcessCmdKey(ref message, key); }
                ShowNotices(); ShowDownloads(); await Task.Delay(800);
                Check(await Web.AutomationController.EvaluateAsync(main, "document.getElementById('q').value+'|'+window.qaMark") == "a|1" && !settingsOpen && !DownloadsVisible && !findBar.Searching && !NoticesShown,
                    "while locked, typing into the page, F5, Ctrl+comma, Ctrl+J, Ctrl+F, Notices and Downloads do nothing", results);
                lockView.FocusInput();
                var inside = true;
                foreach (var key in new[] { Keys.Tab, Keys.Tab, Keys.Tab, Keys.Shift | Keys.Tab, Keys.Shift | Keys.Tab, Keys.Shift | Keys.Tab, Keys.Shift | Keys.Tab })
                { var message = new Message(); ProcessCmdKey(ref message, key); inside &= lockView.ContainsFocus; }
                Check(inside, "Tab and Shift+Tab go round the lock and never reach the window behind it", results);
                lockView.FocusInput();

                for (var i = 1; i <= 4; i++) { lockView.Pad.TypeForQa("246801"); await LockIdleQaAsync(); }
                Check(lockView.Pad.MessageForQa == string.Format(Strings.Get("lockWrongPin", "ru"), 6) && lockView.Pad.AcceptsForQa, "four wrong PINs cost nothing but an attempt each", results);
                lockView.Pad.TypeForQa("246801"); await LockIdleQaAsync();
                var record = storage.LoadLock();
                Check(!lockView.Pad.AcceptsForQa && record.Failures == 5 && record.WaitMs == 30000 && lockView.Pad.MessageForQa.StartsWith(Strings.Get("lockWait", "ru").Split('{')[0]),
                    "the fifth wrong PIN shuts the pad for 30 seconds", results);
                SaveBitmap(lockView, Path.Combine(qa, "lock-wait.png"));
                // The wait as if it had run out.
                record.WaitStart = DateTime.UtcNow.AddMinutes(-1).Ticks; storage.SaveLock(record); lockView.Pad.Wait(0);
                lockView.Pad.TypeForQa("135792"); await LockIdleQaAsync();
                Check(!locked && !LockGate.Engaged && !lockView.Visible && storage.LoadLock().Failures == 0, "the right PIN opens the window and clears the counter", results);
                await Web.AutomationController.EvaluateAsync(main, "document.getElementById('q').focus();'ok'"); host.SetFocus(true); await Task.Delay(200); await TypeSettledAsync(host, 'c', "a");
                var typed = await Web.AutomationController.EvaluateAsync(main, "document.getElementById('q').value+'|'+(document.activeElement&&document.activeElement.id)"); QaTrace("lock-typed " + typed);
                Check(typed == "ac|q", "typing reaches the page again once unlocked", results);

                Cover(); var coveredNow = covered && lockView.Visible; Resolve();
                Check(coveredNow && !covered && !locked && !lockView.Visible, "back within the minute, the cover goes without asking", results);
                Cover(); hiddenAt = DateTime.UtcNow.AddMinutes(-2); Resolve();
                Check(locked && lockView.Visible, "after a minute away the PIN is asked", results);
                lockView.Pad.TypeForQa("135792"); await LockIdleQaAsync();

                var saved = File.ReadAllBytes(Path.Combine(storage.Root, "lock.dpapi"));
                File.WriteAllBytes(Path.Combine(storage.Root, "lock.dpapi"), new byte[] { 1, 2, 3 });
                Lock();
                Check(lockView.Unavailable && lockView.SignInAgainForQa.Visible, "an unreadable PIN record offers only signing in again", results);
                SaveBitmap(lockView, Path.Combine(qa, "lock-unavailable.png"));
                File.WriteAllBytes(Path.Combine(storage.Root, "lock.dpapi"), saved); Lock();
                Check(!lockView.Unavailable, "a readable record shows the pad again", results);

                // The PIN seals the saved password (review L14): the plain file keeps only the login, a new start knows no
                // password until the PIN is entered, and the right PIN brings it back.
                var synthetic = new Credentials { Login = "QA student", Password = "synthetic fixture" };
                storage.SaveCredentials(synthetic); credentials = synthetic; plainPassword = true;
                await Task.Run(() => pinGuard.Enable("135792", synthetic)); await StripPlainAsync();
                var plainFile = storage.LoadCredentials();
                Check(plainFile != null && plainFile.Sealed && plainFile.Password == null && plainFile.Login == "QA student", "with a PIN the plain file keeps only the login", results);
                pinGuard = new PinGuard(storage.LockStore); credentials = storage.Load().Credentials;
                {
                    var pageWas = prefs.StartPage; var pausedWas = prefs.AutoLoginPaused;
                    prefs.StartPage = "schedule"; prefs.AutoLoginPaused = false;
                    Check(!credentials.Usable() && StartAddress() == SitePolicy.Origin + "/student/myschedule/", "a new start with the login as saved under the PIN opens the chosen start page", results);
                    prefs.StartPage = pageWas; prefs.AutoLoginPaused = pausedWas;
                }
                Lock(); lockView.Pad.TypeForQa("135792"); await LockIdleQaAsync();
                Check(!locked && credentials != null && credentials.Usable() && credentials.Password == "synthetic fixture", "after a new start the right PIN opens the sealed password", results);
                Lock();

                // The tenth wrong PIN: the saved account ends through the usual logout and the PIN goes with it.
                await storage.SaveCredentialsAsync(synthetic); credentials = synthetic;
                record = storage.LoadLock(); record.Failures = 9; record.WaitMs = 0; storage.SaveLock(record);
                lockView.Pad.TypeForQa("246801"); await LockIdleQaAsync();
                until = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < until && (wiping || locked)) await Task.Delay(100);
                Check(!storage.LockExists && storage.LoadCredentials() == null && credentials == null && storage.LoadPreferences().AutoLoginPaused && !locked && !LockGate.Engaged && messages.Shows("lockWiped"),
                    "the tenth wrong PIN ends the saved account through logout and removes the PIN", results);
            }
            finally
            {
                if (locked || covered) Unlock();
                lockQa = false; storage.RemoveLock(); pinGuard.Forget();
            }
        }
        // A whole key press, as a keyboard sends it: Chromium drops a lone character after a swallowed key until the next key-down.
        // SendKeyEvent, not DevTools input: DevTools input skips the app's keyboard handler, so the lock could not stop it.
        private static void TypeQa(IBrowserHost host, char value)
        {
            var code = (int)char.ToUpperInvariant(value);
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = code });
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.Char, WindowsKeyCode = value });
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = code });
        }
        // Chromium 109 loses the first injected press after a load or a focus change (traced 17 Nov 2026: same document,
        // the next press lands), so an open page gets the press again until the field changes, at most four times.
        private async Task<string> TypeSettledAsync(IBrowserHost host, char value, string before)
        {
            const string read = "document.getElementById('q').value";
            var now = before;
            for (var press = 0; press < 4 && now == before; press++)
            {
                TypeQa(host, value);
                for (var look = 0; look < 8 && now == before; look++) { await Task.Delay(100); now = await Web.AutomationController.EvaluateAsync(main, read) ?? before; }
            }
            return now;
        }
        private async Task LockIdleQaAsync()
        {
            await Task.Delay(50);
            for (var until = DateTime.UtcNow.AddSeconds(10); (lockChecking || wiping) && DateTime.UtcNow < until;) await Task.Delay(50);
            await Task.Delay(50);
        }
    }
}
