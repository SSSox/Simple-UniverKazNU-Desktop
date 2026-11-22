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
        public void Dispose()
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
}
