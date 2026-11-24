using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class StorageTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        [Fact]
        public void Credentials_RoundTrip_AreNotStoredAsPlainText()
        {
            var storage = new Storage(root); var password = Guid.NewGuid().ToString();
            storage.SaveCredentials(new Credentials { Login = "synthetic-local-only", Password = password });
            Assert.DoesNotContain(password, File.ReadAllText(Path.Combine(root, "credentials.dpapi")));
            Assert.Equal(password, storage.LoadCredentials().Password);
            storage.RemoveCredentials(); Assert.Null(storage.LoadCredentials());
        }
        [Fact]
        public void Credentials_TamperedRecord_FailsClosed()
        {
            var storage = new Storage(root); storage.SaveCredentials(new Credentials { Login = "test", Password = Guid.NewGuid().ToString() });
            var path = Path.Combine(root, "credentials.dpapi"); var bytes = File.ReadAllBytes(path); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(path, bytes);
            Assert.Throws<CryptographicException>(() => storage.LoadCredentials());
        }
        [Fact]
        public void Credentials_RemovalAlsoClearsThePinSealedAccount()
        {
            var storage = new Storage(root);
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            const string pin = "482613";
            new PinGuard(storage.LockStore, iterations: 5).Enable(pin, account);
            storage.SaveCredentials(account.LoginOnly());

            storage.RemoveCredentials();

            Assert.Null(new Storage(root).LoadCredentials());
            var unlocked = new PinGuard(new Storage(root).LockStore, iterations: 5).Guess(pin);
            Assert.Equal(PinOutcome.Success, unlocked.Outcome);
            Assert.Null(unlocked.Account);
        }
        [Theory]
        [InlineData("credentials.dpapi")]
        [InlineData("account.dpapi")]
        [InlineData("lock.dpapi")]
        public void Credentials_FailedRemovalStaysPendingAndBlocksPinRestoration(string lockedName)
        {
            var storage = new Storage(root);
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            const string pin = "482613";
            new PinGuard(storage.LockStore, iterations: 5).Enable(pin, account);
            // Simulate a previous interrupted sealing with both standalone records still on disk.
            storage.SaveCredentials(account.LoginOnly());
            File.WriteAllBytes(Path.Combine(root, "credentials.dpapi"), new byte[] { 1 });
            storage.SavePreferences(new Preferences { SessionClearPending = true, AutoLoginPaused = true });
            var marker = Path.Combine(root, "credentials.remove");
            using (File.Open(Path.Combine(root, lockedName), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<IOException>(() => storage.RemoveCredentials());
                Assert.True(File.Exists(marker));
                var next = new Storage(root);
                Assert.Null(next.Load().Credentials);
                var before = next.LoadPreferences(); var oldProfile = before.Profile; var oldWebProfile = before.WebProfile;
                Assert.ThrowsAny<IOException>(() => next.FinishSessionClear(before));
                Assert.NotEqual(oldProfile, next.LoadPreferences().Profile);
                Assert.NotEqual(oldWebProfile, next.LoadPreferences().WebProfile);
                Assert.False(next.LoadPreferences().SessionClearPending);
                var unlocked = new PinGuard(next.LockStore, iterations: 5).Guess(pin);
                Assert.NotEqual(PinOutcome.Success, unlocked.Outcome);
                Assert.Null(unlocked.Account);
                Assert.True(File.Exists(marker));
            }

            var restarted = new Storage(root);
            Assert.True(restarted.FinishSessionClear(restarted.LoadPreferences()));
            Assert.Null(restarted.LoadCredentials());
            Assert.False(File.Exists(marker));
            Assert.False(File.Exists(Path.Combine(root, "credentials.dpapi")));
            Assert.False(File.Exists(Path.Combine(root, "account.dpapi")));
            var result = new PinGuard(restarted.LockStore, iterations: 5).Guess(pin);
            Assert.Equal(PinOutcome.Success, result.Outcome);
            Assert.Null(result.Account);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Credentials_StartupFinishesRemovalBeforeOrAfterStandaloneFilesWent(bool standaloneRemoved)
        {
            var storage = new Storage(root);
            const string pin = "482613";
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            new PinGuard(storage.LockStore, iterations: 5).Enable(pin, account);
            storage.SaveCredentials(account.LoginOnly());
            storage.SavePreferences(new Preferences { SessionClearPending = true, AutoLoginPaused = true });
            // The durable marker survived an exit at either boundary of account removal.
            File.WriteAllBytes(Path.Combine(root, "credentials.remove"), new byte[0]);
            if (standaloneRemoved) File.Delete(Path.Combine(root, "account.dpapi"));

            var next = new Storage(root);
            Assert.Null(next.Load().Credentials);
            Assert.True(next.FinishSessionClear(next.LoadPreferences()));
            var result = new PinGuard(next.LockStore, iterations: 5).Guess(pin);
            Assert.Equal(PinOutcome.Success, result.Outcome);
            Assert.Null(result.Account);
            Assert.False(File.Exists(Path.Combine(root, "credentials.remove")));
        }
        [Fact]
        public void Credentials_RemovalMarkerMustBeDurableBeforeEitherRecordChanges()
        {
            var storage = new Storage(root);
            const string pin = "482613";
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            new PinGuard(storage.LockStore, iterations: 5).Enable(pin, account);
            storage.SaveCredentials(account.LoginOnly());
            var sealedAccount = storage.LoadLock().Account;
            using (File.Open(Path.Combine(root, "credentials.remove.new"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Assert.ThrowsAny<IOException>(() => storage.RemoveCredentials());

            Assert.False(File.Exists(Path.Combine(root, "credentials.remove")));
            Assert.Equal(account.Login, storage.LoadCredentials().Login);
            Assert.Equal(sealedAccount, storage.LoadLock().Account);
        }
        [Fact]
        public void Credentials_ForgottenPinRemovalRetriesAnUnreadableLockAfterRestart()
        {
            var storage = new Storage(root);
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            storage.SaveCredentials(account);
            var lockPath = Path.Combine(root, "lock.dpapi");
            File.WriteAllBytes(lockPath, new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(root, "lock.pin"), new byte[0]);
            storage.SavePreferences(new Preferences { SessionClearPending = true, AutoLoginPaused = true });
            using (File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<IOException>(() => storage.RemoveCredentials(true));
                Assert.True(storage.CredentialsRemovalPending);
                Assert.Null(new Storage(root).LoadCredentials());
            }

            var next = new Storage(root);
            Assert.True(next.FinishSessionClear(next.LoadPreferences()));
            Assert.False(next.CredentialsRemovalPending);
            Assert.False(next.LockExists);
            Assert.False(next.PinMarked);
        }
        [Theory]
        [InlineData("..")]
        [InlineData("../../outside")]
        [InlineData("C:\\Windows")]
        public void ProfilePath_Traversal_RejectsBeforeAccess(string profile)
        { var storage = new Storage(root); Assert.Throws<ArgumentException>(() => storage.ProfilePath(profile)); }
        [Fact]
        public void Preferences_LogoutPause_PersistsAcrossLaunches()
        {
            var storage = new Storage(root); var prefs = new Preferences { AutoLoginPaused = true, Language = "kz" };
            storage.SavePreferences(prefs); Assert.True(new Storage(root).LoadPreferences().AutoLoginPaused); Assert.Equal("kz", storage.LoadPreferences().Language);
        }
        [Fact]
        public void LegacyWarning_NewAndOldPreferences_DoNotSuppressNotice()
        {
            var storage = new Storage(root);
            Assert.False(storage.LoadPreferences().LegacyWarningDismissed);
            // An installed 0.7.0 preferences file has no warning choice yet.
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{\"Profile\":\"0123456789abcdef0123456789abcdef\",\"Language\":\"kz\",\"AutoLoginPaused\":true}");
            var old = storage.LoadPreferences();
            Assert.False(old.LegacyWarningDismissed); Assert.Equal("kz", old.Language); Assert.True(old.AutoLoginPaused);
        }
        [Fact]
        public async Task LegacyWarning_ChoiceSurvivesSettingsLogoutAndBuildSwitch()
        {
            var legacy = new Storage(root); var prefs = legacy.LoadPreferences(); prefs.LegacyWarningDismissed = true;
            await legacy.SavePreferencesAsync(prefs);
            var modern = new Storage(root, BrowserProfileFlavor.Modern);
            var updated = modern.LoadPreferences().Clone(); updated.Language = "en"; updated.AutoLoginPaused = true;
            updated.Profile = Guid.NewGuid().ToString("N"); updated.Readable = false;
            await modern.SavePreferencesAsync(updated);
            // Removing a saved account must not reset a Windows user's notice preference.
            await modern.SaveCredentialsAsync(new Credentials { Login = "synthetic", Password = "local fixture" });
            await modern.RemoveCredentialsAsync();
            var reopened = new Storage(root).LoadPreferences();
            Assert.True(reopened.LegacyWarningDismissed); Assert.Equal("en", reopened.Language);
            Assert.True(reopened.AutoLoginPaused); Assert.False(reopened.Readable); Assert.Null(legacy.LoadCredentials());
        }
        [Fact]
        public async Task LegacyWarning_QueuedWriteCapturesChoiceAndDoesNotLeakToNewProfile()
        {
            var storage = new Storage(root); var prefs = storage.LoadPreferences(); prefs.LegacyWarningDismissed = true;
            var saved = storage.SavePreferencesAsync(prefs); prefs.LegacyWarningDismissed = false; await saved;
            Assert.True(storage.LoadPreferences().LegacyWarningDismissed);
            Assert.False(new Storage(Path.Combine(root, "another-user")).LoadPreferences().LegacyWarningDismissed);
        }
        // A logout saves its mark with the pause before the browser profiles are replaced. A run that ends in between
        // (a crash, a kill, a power cut) leaves the old profile, with the session's cookies, as the current one.
        [Fact]
        public void InterruptedLogout_IsFinishedByTheNextStart()
        {
            var storage = new Storage(root);
            var stopped = new Preferences { AutoLoginPaused = true, SessionClearPending = true };
            storage.SavePreferences(stopped);
            Directory.CreateDirectory(storage.ProfilePath(stopped.Profile)); Directory.CreateDirectory(storage.WebProfilePath(stopped.WebProfile));
            File.WriteAllText(Path.Combine(root, "notices.json"), "[]"); File.WriteAllText(Path.Combine(root, "exams.json"), "{}"); File.WriteAllText(Path.Combine(root, "last-grades.json"), "{}");

            var next = new Storage(root); var prefs = next.Load().Preferences;
            Assert.True(prefs.SessionClearPending);
            Assert.True(next.FinishSessionClear(prefs));
            Assert.NotEqual(stopped.Profile, prefs.Profile); Assert.NotEqual(stopped.WebProfile, prefs.WebProfile);
            Assert.True(prefs.AutoLoginPaused); Assert.False(prefs.SessionClearPending);
            var saved = next.LoadPreferences();
            Assert.Equal(prefs.Profile, saved.Profile); Assert.Equal(prefs.WebProfile, saved.WebProfile);
            Assert.True(saved.AutoLoginPaused); Assert.False(saved.SessionClearPending);
            Assert.False(File.Exists(Path.Combine(root, "notices.json")));
            Assert.False(File.Exists(Path.Combine(root, "exams.json")));
            Assert.False(File.Exists(Path.Combine(root, "last-grades.json")));
            // The profiles of the session that was ending are obsolete now, and the usual start-up cleanup takes them.
            next.CleanOldProfiles(prefs.Profile);
            Assert.True(next.WebProfileSaved(prefs.WebProfile)); next.CleanOldWebProfiles(prefs.WebProfile);
            Assert.False(Directory.Exists(next.ProfilePath(stopped.Profile))); Assert.False(Directory.Exists(next.WebProfilePath(stopped.WebProfile)));
            Assert.False(next.FinishSessionClear(prefs));
        }
        [Fact]
        public void FinishedSessionAndOlderSettingsFiles_KeepTheirProfile()
        {
            var storage = new Storage(root); var profile = new string('a', 32);
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{\"Language\":\"kz\",\"AutoLoginPaused\":false,\"Profile\":\"" + profile + "\"}");
            var prefs = storage.Load().Preferences;
            Assert.False(prefs.SessionClearPending);
            Assert.False(prefs.KeptCopiesClearPending);
            Assert.False(storage.FinishSessionClear(prefs));
            Assert.Equal(profile, prefs.Profile); Assert.False(prefs.AutoLoginPaused);
        }
        [Fact]
        public void ReviewRun_StartsCleanButLeavesTheInterruptedLogoutOnDisk()
        {
            var storage = new Storage(root);
            var stopped = new Preferences { AutoLoginPaused = true, SessionClearPending = true };
            storage.SavePreferences(stopped);
            var prefs = storage.Load().Preferences;
            Assert.True(storage.FinishSessionClear(prefs, false));
            Assert.NotEqual(stopped.Profile, prefs.Profile);
            var saved = storage.LoadPreferences();
            Assert.True(saved.SessionClearPending); Assert.Equal(stopped.Profile, saved.Profile);
        }
        // Settings are copied before every queued save; a field the copy forgets would be dropped by the next one.
        [Fact]
        public void PreferencesCopy_KeepsThePendingSessionClear()
        { Assert.True(new Preferences { SessionClearPending = true }.Clone().SessionClearPending); }
        [Fact]
        public async Task PendingKeptCopiesClear_RoundTripsThroughAQueuedSave()
        {
            var storage = new Storage(root);
            Assert.False(storage.LoadPreferences().KeptCopiesClearPending);
            await storage.SavePreferencesAsync(new Preferences { KeptCopiesClearPending = true });
            Assert.True(new Storage(root).LoadPreferences().KeptCopiesClearPending);
        }

        [Fact]
        public void PendingKeptCopiesClear_StartupDeletesCopiesAndSavesClearedMarker()
        {
            var storage = new Storage(root);
            var saved = new Preferences { KeptCopiesClearPending = true };
            storage.SavePreferences(saved);
            WriteKeptCopies();
            var gradePath = Path.Combine(root, GradeFile.Name);
            File.WriteAllText(gradePath, "{}");

            var next = new Storage(root);
            var prefs = next.LoadPreferences();
            Assert.True(next.FinishSessionClear(prefs));

            Assert.False(prefs.KeptCopiesClearPending);
            Assert.False(next.LoadPreferences().KeptCopiesClearPending);
            Assert.Equal(saved.Profile, prefs.Profile);
            Assert.Equal(saved.WebProfile, prefs.WebProfile);
            Assert.False(prefs.AutoLoginPaused);
            Assert.All(KeptCopyNames, name => Assert.False(File.Exists(Path.Combine(root, name))));
            Assert.True(File.Exists(gradePath));
        }

        [Theory]
        [InlineData("schedule.json", false)]
        [InlineData("exams.json", false)]
        [InlineData("last-grades.json", false)]
        [InlineData("exams.json", true)]
        public void LockedKeptCopy_StartupKeepsMarkerUntilTheNextSuccessfulCleanup(string lockedName, bool interruptedLogout)
        {
            var storage = new Storage(root);
            storage.SavePreferences(new Preferences { SessionClearPending = interruptedLogout, KeptCopiesClearPending = !interruptedLogout });
            WriteKeptCopies();
            var lockedPath = Path.Combine(root, lockedName);

            using (File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var prefs = storage.LoadPreferences();
                Assert.True(storage.FinishSessionClear(prefs));
                Assert.True(prefs.KeptCopiesClearPending);
                var saved = storage.LoadPreferences();
                Assert.True(saved.KeptCopiesClearPending);
                Assert.False(saved.SessionClearPending);
                Assert.All(KeptCopyNames, name => Assert.Equal(name == lockedName, File.Exists(Path.Combine(root, name))));
            }

            var next = new Storage(root);
            Assert.True(next.FinishSessionClear(next.LoadPreferences()));
            Assert.False(File.Exists(lockedPath));
            Assert.False(next.LoadPreferences().KeptCopiesClearPending);
        }

        [Fact]
        public void PendingKeptCopiesClear_ReviewLeavesFilesAndMarkerOnDisk()
        {
            var storage = new Storage(root);
            storage.SavePreferences(new Preferences { KeptCopiesClearPending = true });
            WriteKeptCopies();

            var prefs = storage.LoadPreferences();
            Assert.True(storage.FinishSessionClear(prefs, false));
            Assert.True(prefs.KeptCopiesClearPending);
            Assert.True(storage.LoadPreferences().KeptCopiesClearPending);
            Assert.All(KeptCopyNames, name => Assert.True(File.Exists(Path.Combine(root, name))));
        }

        [Fact]
        public void Load_WithPreloadedPreferences_ReusesThemWithoutReparsingDisk()
        {
            var storage = new Storage(root);
            var saved = new Preferences { Language = "en", Theme = ThemeChoice.Dark, Zoom = 120 };
            storage.SavePreferences(saved);
            var preloaded = storage.LoadPreferences();
            // Overwrite preferences.json with invalid JSON: Load(preloaded) must use the passed copy rather than re-reading disk.
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{invalid");

            var state = storage.Load(preloaded);
            Assert.False(state.Failed);
            Assert.Equal("en", state.Preferences.Language);
            Assert.Equal(ThemeChoice.Dark, state.Preferences.Theme);
            Assert.Equal(120, state.Preferences.Zoom);
            Assert.NotSame(preloaded, state.Preferences);
        }

        [Fact]
        public void Load_WithPreloadedPreferencesAndMissingFile_StillPausesAutoLoginForSavedCredentials()
        {
            var storage = new Storage(root);
            storage.SaveCredentials(new Credentials { Login = "synthetic", Password = "local fixture" });
            var preloaded = storage.LoadPreferences();
            Assert.False(preloaded.AutoLoginPaused);

            var state = storage.Load(preloaded);
            Assert.False(state.Failed);
            Assert.NotNull(state.Credentials);
            Assert.True(state.Preferences.AutoLoginPaused);
            Assert.False(preloaded.AutoLoginPaused);
        }

        [Fact]
        public void PendingRemoval_IsRetriedByLoadLockOnceTheFileIsFree()
        {
            var storage = new Storage(root);
            const string pin = "482613";
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            new PinGuard(storage.LockStore, iterations: 5).Enable(pin, account);
            storage.SaveCredentials(account.LoginOnly());
            var lockPath = Path.Combine(root, "lock.dpapi");
            using (File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<IOException>(() => storage.RemoveCredentials());
                Assert.True(storage.CredentialsRemovalPending);
                // Still locked: reading the PIN must not pretend the removal finished.
                var pending = Assert.ThrowsAny<IOException>(() => storage.LoadLock());
                Assert.Equal("Credential removal is pending", pending.Message);
                Assert.True(storage.CredentialsRemovalPending);
            }

            var record = storage.LoadLock();
            Assert.False(storage.CredentialsRemovalPending);
            Assert.NotNull(record);
            Assert.Null(record.Account);
            var opened = new PinGuard(new Storage(root).LockStore, iterations: 5).Guess(pin);
            Assert.Equal(PinOutcome.Success, opened.Outcome);
            Assert.Null(opened.Account);
        }

        [Fact]
        public void PendingRemoval_DropsALockThatCanNeverBeDecrypted()
        {
            var storage = new Storage(root);
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            new PinGuard(storage.LockStore, iterations: 5).Enable("482613", account);
            storage.SaveCredentials(account.LoginOnly());
            var lockPath = Path.Combine(root, "lock.dpapi");
            var bytes = File.ReadAllBytes(lockPath);
            bytes[bytes.Length / 2] ^= 0x5A;
            File.WriteAllBytes(lockPath, bytes);
            var marker = Path.Combine(root, "credentials.remove");
            File.WriteAllBytes(marker, new byte[] { 0 });
            // The tamper rewrites the lock; the marker has to stay the later of the two or the lock would look new.
            File.SetLastWriteTimeUtc(marker, File.GetLastWriteTimeUtc(lockPath).AddMinutes(1));

            Assert.Null(storage.LoadLock());
            Assert.False(storage.CredentialsRemovalPending);
            Assert.False(storage.LockExists);
            Assert.False(storage.PinMarked);
            Assert.Null(storage.LoadLock());
        }

        [Fact]
        public void SaveLock_FinishesPendingRemovalBeforeTheNewPinIsWritten()
        {
            var storage = new Storage(root);
            var oldAccount = new Credentials { Login = "synthetic student", Password = "local fixture" };
            new PinGuard(storage.LockStore, iterations: 5).Enable("482613", oldAccount);
            storage.SaveCredentials(oldAccount.LoginOnly());
            var marker = Path.Combine(root, "credentials.remove");
            File.WriteAllBytes(marker, new byte[] { 0 });

            var fresh = new Credentials { Login = "synthetic student", Password = "replacement fixture" };
            var guard = new PinGuard(storage.LockStore, iterations: 5);
            guard.Enable("135792", fresh);
            storage.SaveCredentials(fresh.LoginOnly());

            Assert.False(storage.CredentialsRemovalPending);
            var opened = guard.Guess("135792");
            Assert.Equal(PinOutcome.Success, opened.Outcome);
            Assert.NotNull(opened.Account);
            Assert.Equal(fresh.Password, opened.Account.Password);
            Assert.Equal(fresh.Login, storage.LoadCredentials().Login);
        }

        [Fact]
        public void SaveCredentials_DoesNotWipeALockWrittenAfterTheRemovalMarker()
        {
            var storage = new Storage(root);
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            var guard = new PinGuard(storage.LockStore, iterations: 5);
            guard.Enable("482613", account);
            var marker = Path.Combine(root, "credentials.remove");
            File.WriteAllBytes(marker, new byte[] { 0 });
            File.SetLastWriteTimeUtc(marker, DateTime.UtcNow.AddMinutes(-5));

            storage.SaveCredentials(account.LoginOnly());

            Assert.False(File.Exists(marker));
            var opened = guard.Guess("482613");
            Assert.Equal(PinOutcome.Success, opened.Outcome);
            Assert.NotNull(opened.Account);
            Assert.Equal(account.Password, opened.Account.Password);
        }

        [Fact]
        public void SaveCredentials_DoesNotRemoveALockWrittenAfterAForgottenPinMarker()
        {
            var storage = new Storage(root);
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            var guard = new PinGuard(storage.LockStore, iterations: 5);
            guard.Enable("482613", account);
            var marker = Path.Combine(root, "credentials.remove");
            File.WriteAllBytes(marker, new byte[] { 1 });
            File.SetLastWriteTimeUtc(marker, DateTime.UtcNow.AddMinutes(-5));

            storage.SaveCredentials(account.LoginOnly());

            Assert.False(storage.CredentialsRemovalPending);
            Assert.True(storage.LockExists);
            var opened = guard.Guess("482613");
            Assert.Equal(PinOutcome.Success, opened.Outcome);
            Assert.NotNull(opened.Account);
            Assert.Equal(account.Password, opened.Account.Password);
        }

        [Fact]
        public void PendingRemoval_FinishesWhenPreferencesCannotBeRead()
        {
            var storage = new Storage(root);
            const string pin = "482613";
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            new PinGuard(storage.LockStore, iterations: 5).Enable(pin, account);
            storage.SaveCredentials(account.LoginOnly());
            File.WriteAllBytes(Path.Combine(root, "credentials.remove"), new byte[] { 0 });
            var preferences = Path.Combine(root, "preferences.json");
            File.WriteAllText(preferences, "{broken");
            var broken = File.ReadAllBytes(preferences);

            var opened = new Storage(root);
            Assert.True(opened.Load().Failed);
            opened.FinishPendingCredentialRemoval();

            Assert.False(opened.CredentialsRemovalPending);
            Assert.Null(opened.LoadLock().Account);
            Assert.Equal(broken, File.ReadAllBytes(preferences));
            var result = new PinGuard(opened.LockStore, iterations: 5).Guess(pin);
            Assert.Equal(PinOutcome.Success, result.Outcome);
            Assert.Null(result.Account);
        }

        [Fact]
        public void PendingRemoval_TransientFailureDoesNotTouchPreferences()
        {
            var storage = new Storage(root);
            var account = new Credentials { Login = "synthetic student", Password = "local fixture" };
            new PinGuard(storage.LockStore, iterations: 5).Enable("482613", account);
            storage.SaveCredentials(account.LoginOnly());
            File.WriteAllBytes(Path.Combine(root, "credentials.remove"), new byte[] { 0 });
            var preferences = Path.Combine(root, "preferences.json");
            File.WriteAllText(preferences, "{broken");
            var broken = File.ReadAllText(preferences);
            var lockPath = Path.Combine(root, "lock.dpapi");
            using (File.Open(lockPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.ThrowsAny<IOException>(() => storage.FinishPendingCredentialRemoval());
            }

            Assert.True(storage.CredentialsRemovalPending);
            Assert.True(storage.LockExists);
            Assert.Equal(broken, File.ReadAllText(preferences));
        }

        private static readonly string[] KeptCopyNames = { ScheduleFile.Name, ExamFile.Name, LastGradesFile.Name };

        private void WriteKeptCopies()
        {
            foreach (var name in KeptCopyNames) File.WriteAllText(Path.Combine(root, name), "{}");
        }

        public void Dispose()
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
}
