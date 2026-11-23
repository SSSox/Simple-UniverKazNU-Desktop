using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // The app PIN, as on the phone: asked at start and after a minute away (in the tray, minimized or behind the Windows
    // lock screen); ten wrong guesses or "Forgot your PIN?" end the saved account through the usual logout. While locked,
    // the window's actions and the pages' keys do nothing, and nothing opens over the lock.
    internal sealed partial class MainForm
    {
        private static readonly TimeSpan AutoLockAfter = TimeSpan.FromMinutes(1);
        private LockView lockView;
        private PinGuard pinGuard;
        private bool locked, covered, sessionLocked, lockChecking, wiping, startPromptsPending, pinOfferShown, lockDialogOpen, lockQa;
        private DateTime hiddenAt;
        private Timer lockCloser;
        private bool LockAllowed { get { return LockScope.Allowed(lockQa, accountReview, storage.Root, Path.GetTempPath()); } }
        private bool PinSet { get { return storage.LockExists; } }
        private bool LockArmed { get { return LockAllowed && PinSet; } }
        private bool LockShown { get { return locked || covered; } }
        private void BuildLock()
        {
            pinGuard = new PinGuard(storage.LockStore);
            lockView = new LockView(SZ); Controls.Add(lockView); lockView.BringToFront(); PlaceLock();
            Resize += (s, e) => PlaceLock();
            lockView.Pad.Completed += pin => QueueAsync(() => UnlockAsync(pin));
            lockView.Pad.ExtraClicked += () => QueueAsync(ForgotPinAsync);
            lockView.SignInAgain += () => QueueAsync(() => WipeAsync("lockUnavailable"));
            // Up before the first paint, so the site never shows first.
            Load += (s, e) => { if (LockArmed) Lock(); };
            VisibleChanged += (s, e) => { if (!Visible) Cover(); else Resolve(); };
            Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Cover(); else Resolve(); };
            // At start the window is not visible yet when Lock() runs, and activation would focus the workspace: the pad
            // takes the keyboard once the window shows and whenever it comes back.
            Shown += (s, e) => { if (locked) lockView.FocusInput(); };
            Activated += (s, e) => { Resolve(); if (locked && !lockView.ContainsFocus && !lockDialogOpen) lockView.FocusInput(); };
            SystemEvents.SessionSwitch += SessionSwitched;
        }
        private void PlaceLock() { lockView?.SetBounds(0, 0, ClientSize.Width, ClientSize.Height); }
        private void ShowLockView() { PlaceLock(); lockView.Visible = true; lockView.BringToFront(); }
        private void SessionSwitched(object sender, SessionSwitchEventArgs e)
        {
            var away = e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect;
            var back = e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect;
            if (away || back) Dispatch(() => { sessionLocked = away; if (away) Cover(); else Resolve(); });
        }
        // The window went away: the lock covers it at once, so its thumbnail and its return never show the site.
        private void Cover()
        {
            if (closing || locked || covered || !LockArmed) return;
            covered = true; hiddenAt = DateTime.UtcNow;
            lockView.Render(prefs.Language); lockView.SetUnavailable(false); lockView.Pad.Clear();
            ShowLockView(); OfflineLockChanged();
        }
        // Minimizing from the title bar, the taskbar or the window menu: the cover is painted before Windows takes the
        // picture it animates and shows in the taskbar preview.
        private void CoverBeforeMinimize() { if (lockView == null || !LockArmed) return; Cover(); if (covered) lockView.Update(); }
        // Back within the minute: the cover goes quietly. Later (or with the clock moved back): the PIN is asked.
        private void Resolve()
        {
            if (!covered || closing || sessionLocked || !Visible || WindowState == FormWindowState.Minimized) return;
            covered = false;
            var away = DateTime.UtcNow - hiddenAt;
            if (away >= AutoLockAfter || away < TimeSpan.Zero) Lock();
            else { lockView.Visible = false; OfflineLockChanged(); }
        }
        private void Lock()
        {
            if (closing || !LockArmed) return;
            covered = false; locked = true; LockGate.Engaged = true;
            // Settings, the update offer and any question close: nothing stays open over the lock. A window busy saving
            // refuses for a moment, so it is asked again until it goes.
            if (!CloseOverLock()) StartLockCloser();
            if (findBar.Searching) findBar.Close();
            // A moment when another program holds the file is not a lost record: it is read again before giving up.
            var unreadable = true;
            for (var attempt = 0; attempt < 3 && unreadable; attempt++)
            {
                if (attempt > 0) System.Threading.Thread.Sleep(60);
                try { storage.LoadLock(); unreadable = false; } catch (Exception) { }
            }
            lockView.Render(prefs.Language); lockView.SetUnavailable(unreadable); lockView.Pad.Clear(); lockView.Pad.ShowMessage("", false);
            ShowLockView(); lockView.FocusInput(); OfflineLockChanged();
            var wait = pinGuard.RemainingWait(); if (wait > 0) lockView.Pad.Wait(wait);
            // The tenth wrong guess was counted, but the app closed before the account ended: finish it now.
            if (pinGuard.Exhausted()) Dispatch(() => QueueAsync(() => WipeAsync("lockWiped")));
        }
        private bool CloseOverLock()
        {
            var open = Application.OpenForms.Cast<Form>().Where(f => f != this && !f.IsDisposed && f.Visible).ToList();
            foreach (var form in open) { try { form.Close(); } catch (InvalidOperationException) { } }
            return open.All(f => f.IsDisposed || !f.Visible);
        }
        private void StartLockCloser()
        {
            if (lockCloser != null) return;
            lockCloser = new Timer { Interval = 250 };
            lockCloser.Tick += (s, e) => { if (!LockShown || CloseOverLock()) StopLockCloser(); };
            lockCloser.Start();
        }
        private void StopLockCloser() { if (lockCloser == null) return; lockCloser.Stop(); lockCloser.Dispose(); lockCloser = null; }
        // Tab and Shift+Tab go round the lock's own controls and never reach the hidden window behind it.
        private void LockTab(bool back)
        {
            var from = lockView.ContainsFocus ? ActiveControl : null;
            while (from is ContainerControl && ((ContainerControl)from).ActiveControl != null) from = ((ContainerControl)from).ActiveControl;
            if (from == null || !lockView.Contains(from) || !lockView.SelectNextControl(from, !back, true, true, true)) lockView.FocusInput();
        }
        private void Unlock()
        {
            locked = false; covered = false; LockGate.Engaged = false; StopLockCloser();
            lockView.Visible = false; lockView.Pad.Clear();
            // A success that arrived while locked shows now.
            RenderFeedback();
            if (WebShown) webPanel.Browser?.Focus(); else if (main != null && !main.IsDisposed) main.Focus();
            OfflineLockChanged();
            // What the start held back while locked (the first sign-in window, the Chromium 109 notice), then the update offer.
            if (startPromptsPending) { startPromptsPending = false; QueueAsync(StartPromptsAsync); }
            else EnterUpdates();
        }
        private async Task UnlockAsync(string pin)
        {
            if (!locked || lockChecking || wiping) return;
            lockChecking = true; lockView.Pad.SetBusy(true);
            PinResult result;
            try { result = await Task.Run(() => pinGuard.Guess(pin)); }
            catch (Exception) { result = new PinResult(PinOutcome.Failed); }
            finally { lockChecking = false; }
            if (closing) return;
            lockView.Pad.SetBusy(false);
            switch (result.Outcome)
            {
                case PinOutcome.Success:
                    // No second PIN is taken while the opened account is being put in place.
                    lockChecking = true;
                    try { await AcceptUnlockAsync(pin, result); } finally { lockChecking = false; }
                    if (!closing) Unlock();
                    return;
                case PinOutcome.Wiped: await WipeAsync("lockWiped"); return;
                case PinOutcome.Unavailable: lockView.SetUnavailable(true); lockView.FocusInput(); return;
                case PinOutcome.Wait: lockView.Pad.Wait(result.WaitMs); return;
                case PinOutcome.Failed: lockView.Pad.ShowKey("lockFailed", true); return;
                default:
                    lockView.Pad.ShowMessage(result.AttemptsLeft == 1 ? Strings.Get("lockLastAttempt", prefs.Language) : string.Format(Strings.Get("lockWrongPin", prefs.Language), result.AttemptsLeft), true);
                    if (result.WaitMs > 0) lockView.Pad.Wait(result.WaitMs);
                    return;
            }
        }
        private async Task ForgotPinAsync()
        {
            if (!locked || lockDialogOpen || wiping) return;
            var lang = prefs.Language; DialogResult answer;
            lockDialogOpen = true;
            try { using (var form = new ChoiceForm(Strings.Get("lockForgotTitle", lang), Strings.Get("lockForgotMessage", lang), Strings.Get("lockForgotAction", lang), Strings.Get("lockCancel", lang), true)) answer = form.ShowDialog(this); }
            finally { lockDialogOpen = false; }
            if (answer == DialogResult.Yes) await WipeAsync(null); else lockView.FocusInput();
        }
        // The saved account ends through the usual logout (the stop is saved first, then the profile is replaced); only then
        // does the PIN go and the window open, now signed out. A logout that failed keeps the lock, and the next try repeats it.
        private async Task WipeAsync(string messageKey)
        {
            if (wiping || closing) return;
            wiping = true;
            try
            {
                var until = DateTime.UtcNow.AddSeconds(15);
                while ((commandBusy || rotating) && !closing && DateTime.UtcNow < until) await Task.Delay(100);
                if (closing) return;
                if (commandBusy || rotating) { lockView.Pad.ShowKey("lockFailed", true); return; }
                var ended = false;
                commandBusy = true; SetCommandEnabled(false);
                try { await EndSessionAsync(true); ended = true; }
                catch (Exception e) { if (!closing) { guard.Stop(); ReportSessionFault(e, true); } }
                finally { commandBusy = false; if (!closing) SetCommandEnabled(true); }
                if (closing) return;
                if (!ended || credentials != null) { if (locked) lockView.Pad.ShowKey("lockFailed", true); return; }
                try { pinGuard.Forget(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (locked || covered) Unlock();
                if (messageKey != null) SetStatus(messageKey);
            }
            finally { wiping = false; }
        }
        // Offered once to anyone with a saved account who has not answered yet: new sign-ins and existing users alike.
        private void MaybeOfferPin()
        {
            if (!LockAllowed || pinOfferShown || prefs.PinOffered || credentials == null || storageFailed || PinSet) return;
            var epoch = sessionEpoch;
            var delay = new System.Windows.Forms.Timer { Interval = 900 };
            delay.Tick += (s, e) => { delay.Dispose(); if (epoch == sessionEpoch) QueueAsync(OfferPinAsync); };
            delay.Start();
        }
        private async Task OfferPinAsync()
        {
            if (closing || pinOfferShown || prefs.PinOffered || PinSet || credentials == null || prefs.AutoLoginPaused || LockShown || settingsOpen ||
                updateWindowOpen || lockDialogOpen || commandBusy || rotating || !Visible || WindowState == FormWindowState.Minimized) return;
            pinOfferShown = true;
            var lang = prefs.Language; DialogResult answer;
            lockDialogOpen = true;
            try { using (var form = new ChoiceForm(Strings.Get("offerPinTitle", lang), Strings.Get("offerPinMessage", lang), Strings.Get("offerPinSet", lang), Strings.Get("offerPinLater", lang), false)) answer = form.ShowDialog(this); }
            finally { lockDialogOpen = false; }
            if (closing) return;
            prefs.PinOffered = true; if (!storageFailed) await SavePreferencesAsync();
            if (answer == DialogResult.Yes) await SetPinAsync(this);
            else EnterUpdates();
        }
        private async Task<bool> SetPinAsync(IWin32Window owner)
        {
            if (PinSet || LockShown || lockDialogOpen) return false;
            string pin;
            lockDialogOpen = true;
            try
            {
                using (var dialog = new PinDialog(PinDialogMode.Create, prefs.Language, null))
                { if (dialog.ShowDialog(owner) != DialogResult.OK || closing) return false; pin = dialog.NewPin; }
            }
            finally { lockDialogOpen = false; }
            var account = Unsealed;
            try { await Task.Run(() => pinGuard.Enable(pin, account)); }
            catch (Exception) { SetStatus("lockFailed"); return false; }
            await StripPlainAsync();
            if (!prefs.PinOffered) { prefs.PinOffered = true; if (!storageFailed) QueueAsync(SavePreferencesAsync); }
            SetStatus("lockOn"); EnterUpdates(); UpdateTrayHint();
            return true;
        }
        private async Task<bool> ChangePinAsync(IWin32Window owner)
        {
            if (!PinSet || LockShown) return false;
            string pin = null; var ended = false;
            lockDialogOpen = true;
            try
            {
                using (var dialog = new PinDialog(PinDialogMode.Change, prefs.Language, guess => Task.Run(() => pinGuard.Guess(guess))))
                {
                    var answer = dialog.ShowDialog(owner);
                    ended = answer == DialogResult.Abort; if (answer == DialogResult.OK) pin = dialog.NewPin;
                }
            }
            finally { lockDialogOpen = false; }
            if (ended) { EndFromSettings(owner); return false; }
            if (pin == null || closing) return false;
            // The new PIN seals the account this unlocked window holds; a sealed one it could not open is refused, not lost.
            if (credentials != null && !credentials.Usable()) { SetStatus("lockFailed"); return false; }
            var account = Unsealed;
            try { await Task.Run(() => pinGuard.Enable(pin, account)); }
            catch (Exception) { SetStatus("lockFailed"); return false; }
            await StripPlainAsync();
            SetStatus("lockChanged");
            return true;
        }
        private Task<bool> DisablePinAsync(IWin32Window owner)
        {
            if (!PinSet || LockShown) return Task.FromResult(false);
            DialogResult answer;
            // The account goes back to the plain DPAPI file before the PIN record is removed; a failed write keeps the PIN.
            var held = Unsealed; var restored = false;
            Action<Credentials> restore = sealedAccount => { var plain = sealedAccount ?? held; if (plain != null) { storage.SaveCredentials(new Credentials { Login = plain.Login, Password = plain.Password }); restored = true; } };
            lockDialogOpen = true;
            try { using (var dialog = new PinDialog(PinDialogMode.Disable, prefs.Language, guess => Task.Run(() => pinGuard.Disable(guess, restore)))) answer = dialog.ShowDialog(owner); }
            finally { lockDialogOpen = false; }
            // The password is back in the plain file even when the PIN record could not then be removed: the next unlock
            // seals it again.
            if (restored) plainPassword = true;
            if (answer == DialogResult.Abort) { EndFromSettings(owner); return Task.FromResult(false); }
            if (answer != DialogResult.OK) return Task.FromResult(false);
            SetStatus("lockOff"); UpdateTrayHint();
            return Task.FromResult(true);
        }
        // Ten wrong current PINs in Settings (or a record that can no longer be read): Settings closes and the account ends.
        private void EndFromSettings(IWin32Window owner)
        {
            var settings = owner as Form;
            if (settings != null && settings != this && !settings.IsDisposed) settings.Close();
            QueueAsync(() => WipeAsync(pinGuard.Exhausted() ? "lockWiped" : "lockUnavailable"));
        }
        // The saved account when its password is known, the one a new or changed PIN seals.
        private Credentials Unsealed { get { return credentials != null && credentials.Usable() ? credentials : null; } }
        // Whether the plain DPAPI file still holds a password (it holds only the login once the account is sealed).
        private bool plainPassword;
        // What a right PIN opens. A newer record gives the sealed account; an older one, or an account that only the plain
        // file holds, is sealed now. A sealed account the record no longer has cannot sign in, and says so. Once the
        // password is known, the plain file keeps only the login and the waiting sign-in goes on.
        private async Task AcceptUnlockAsync(string pin, PinResult result)
        {
            var before = Unsealed != null;
            // A password the plain file holds is the newer one (saved without the PIN, or before a crash ended the
            // sealing): it replaces the sealed copy. Otherwise the sealed account is the saved one.
            var plain = plainPassword && Unsealed != null;
            if (result.Account != null && !plain) credentials = result.Account;
            else if (credentials != null && !credentials.Usable())
            {
                credentials = null; credentialsUnreadable = true; plainPassword = false; SetStatus("credentialsUnreadable");
                try { await storage.RemoveCredentialsAsync(); } catch (Exception) { }
                Localize(); return;
            }
            var account = Unsealed;
            if (account != null && (result.Account == null || plain))
            {
                try { await Task.Run(() => { if (result.Legacy) pinGuard.Enable(pin, account); else if (!pinGuard.StoreAccount(account)) throw new InvalidOperationException(); }); }
                catch (Exception) { return; }
            }
            if (account == null || closing) return;
            await StripPlainAsync();
            if (!before && !closing)
            {
                await RefreshDocumentScriptsAsync(); Localize();
                QueueAsync(() => automation.TickAsync(Active, credentials, prefs, document != null));
            }
        }
        // The account is sealed under the PIN: the plain file keeps only the login. A failed write leaves the password in
        // the plain file as before the PIN, and the next unlock tries again.
        private async Task StripPlainAsync()
        {
            var account = Unsealed;
            if (account == null || !plainPassword || !PinSet || !pinGuard.Opened) return;
            try { await storage.SaveCredentialsAsync(account.LoginOnly()); plainPassword = false; }
            catch (Exception) { }
        }
        // A new account from Settings: sealed under the PIN when one is set and opened, otherwise in the plain DPAPI file.
        private async Task SaveAccountAsync(Credentials next)
        {
            if (PinSet && await Task.Run(() => pinGuard.StoreAccount(next))) { await storage.SaveCredentialsAsync(next.LoginOnly()); plainPassword = false; }
            else { await storage.SaveCredentialsAsync(next); plainPassword = true; }
        }
        // Removing the saved account takes the sealed copy too; the PIN itself stays until the account is ended.
        private async Task RemoveAccountAsync()
        {
            await storage.RemoveCredentialsAsync(); plainPassword = false;
            // A sealed copy that cannot be removed fails the removal, as a plain file that cannot be deleted does. Ending the
            // account after the PIN (WipeAsync) removes the whole record next, so there a failure here does not stop it.
            if (PinSet)
            {
                try { await Task.Run(() => pinGuard.StoreAccount(null)); }
                catch (Exception) { if (!wiping) throw; }
            }
        }
        // What the window's start shows once it is open (and unlocked): the Chromium 109 notice, the first sign-in window
        // or the reason it cannot sign in, then the update offer.
        private async Task StartPromptsAsync()
        {
#if LEGACY_ENGINE
            ShowLegacyWarning();
            if (closing) return;
#endif
            if (storageFailed) SetStatus("storageError");
            else if (credentialsUnreadable) SetStatus("credentialsUnreadable");
            else if (credentials == null && !prefs.AutoLoginPaused) await OpenSettingsAsync(true);
            updatesReady = true; EnterUpdates();
        }
        // Keys the lock screen itself needs; everything else is swallowed while it shows. Alt+F4 still closes the window.
        private static bool LockKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode; var plain = (keyData & (Keys.Control | Keys.Alt)) == 0;
            return keyData == (Keys.Alt | Keys.F4) || plain && ((key >= Keys.D0 && key <= Keys.D9 && (keyData & Keys.Shift) == 0) || (key >= Keys.NumPad0 && key <= Keys.NumPad9) ||
                key == Keys.Back || key == Keys.Delete || key == Keys.Tab || key == Keys.Enter || key == Keys.Space);
        }
        private void DisposeLock() { SystemEvents.SessionSwitch -= SessionSwitched; LockGate.Engaged = false; StopLockCloser(); }
    }
}
