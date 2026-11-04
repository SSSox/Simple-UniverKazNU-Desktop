using System;
using System.IO;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class PolishFixesTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        public PolishFixesTests() { Directory.CreateDirectory(root); }
        public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

        [Fact]
        public void Guard_SlowPageHold_IsNotAStopThatNeedsTheUser()
        {
            var guard = new SessionGuard(); Assert.True(guard.Suspend());
            Assert.True(guard.CoverSuspended); Assert.False(guard.Blocked); Assert.False(guard.Stopped);
            guard.Navigated(); Assert.False(guard.CoverSuspended); Assert.False(guard.Stopped);
        }
        [Fact]
        public void Guard_SlowPageDuringSignIn_KeepsWaitingAndANewerFormSignsIn()
        {
            var guard = new SessionGuard(); Assert.True(guard.TryLogin("a", 0));
            Assert.True(guard.Suspend()); Assert.False(guard.Stopped); Assert.Equal("a", guard.LoginDocument);
            guard.LoginVisible("a", 25001); Assert.False(guard.Stopped); Assert.True(guard.NeedsNewDocument("a"));
            Assert.True(guard.TryLogin("b", 85002));
        }
        [Fact]
        public void Settings_UnchangedCredentialsAfterSlowPage_DoNotRotateTheSession()
        {
            var saved = new Credentials { Login = "synthetic", Password = "local" };
            var guard = new SessionGuard(); guard.Suspend();
            Assert.Equal(SettingsChange.AppearanceOnly, SettingsDecision.Classify(saved, new Credentials { Login = "synthetic", Password = "local" }, guard.Stopped));
        }
        [Fact]
        public void Storage_UnreadableCredentials_KeepSettingsAndSessionProfile()
        {
            var storage = new Storage(root);
            var prefs = new Preferences { Language = "kz", Zoom = 130, Readable = false };
            prefs.Pins.Add(new PinnedLink { Url = SitePolicy.Origin + "/student/iup/", Title = "IUP" });
            storage.SavePreferences(prefs);
            File.WriteAllBytes(Path.Combine(root, "credentials.dpapi"), new byte[] { 1, 2, 3, 4 });
            var state = storage.Load();
            Assert.False(state.Failed); Assert.True(state.CredentialsUnreadable); Assert.Null(state.Credentials);
            Assert.Equal(prefs.Profile, state.Preferences.Profile); Assert.Equal("kz", state.Preferences.Language);
            Assert.Equal(130, state.Preferences.Zoom); Assert.False(state.Preferences.Readable); Assert.False(state.Preferences.AutoLoginPaused);
            Assert.Single(state.Preferences.Pins);
        }
        [Fact]
        public void Storage_DamagedSettings_ReadableSavedSignInKeepsSigningIn()
        {
            var storage = new Storage(root);
            storage.SaveCredentials(new Credentials { Login = "synthetic", Password = Guid.NewGuid().ToString() });
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{ not json");
            var state = storage.Load();
            Assert.True(state.Failed); Assert.False(state.CredentialsUnreadable); Assert.False(state.Preferences.AutoLoginPaused);
            Assert.Equal("synthetic", state.Credentials.Login);
        }
        [Fact]
        public void Storage_DamagedSettingsWithoutSavedSignIn_StaysPaused()
        {
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{ not json");
            var state = new Storage(root).Load();
            Assert.True(state.Failed); Assert.Null(state.Credentials); Assert.True(state.Preferences.AutoLoginPaused);
        }
        [Fact]
        public void Storage_CleanState_LoadsWithoutFailure()
        {
            var state = new Storage(root).Load();
            Assert.False(state.Failed); Assert.False(state.CredentialsUnreadable); Assert.Null(state.Credentials); Assert.False(state.Preferences.AutoLoginPaused);
        }
        [Fact]
        public void KeyReleases_ReleaseOfAHandledPress_IsNotDispatchedAgain()
        {
            var keys = new KeyReleases(); keys.Pressed(74, 1000);
            Assert.True(keys.Released(74, 1100));
            // The next release without a press (Chromium's PDF guest) is a shortcut of its own.
            Assert.False(keys.Released(74, 1200));
        }
        [Fact]
        public void KeyReleases_ReleaseWithOtherModifiersStillEndsThePress()
        {
            // Ctrl let go before J: the release is tracked by key code, so a later PDF-only release is not swallowed.
            var keys = new KeyReleases(); keys.Pressed(74, 0);
            Assert.True(keys.Released(74, 50));
            Assert.False(keys.Released(74, 5000));
        }
        [Fact]
        public void KeyReleases_LostReleaseExpires()
        {
            var keys = new KeyReleases(); keys.Pressed(80, 0);
            Assert.False(keys.Released(80, KeyReleases.Lifetime + 1));
            keys.Pressed(80, int.MaxValue - 10);
            Assert.True(keys.Released(80, unchecked(int.MaxValue + 100)));
        }
        [Theory]
        [InlineData("/student/transcript", "/student/transcript/", true)]
        [InlineData("/Student/Transcript/", "/student/transcript", true)]
        [InlineData("/student/transcript?x=1", "/student/transcript", false)]
        [InlineData("/student/attendance/full/", "/student/transcript/", false)]
        public void SamePage_IgnoresTrailingSlashAndPathCase(string first, string second, bool expected)
        { Assert.Equal(expected, SitePolicy.SamePage(SitePolicy.Origin + first, SitePolicy.Origin + second)); }
        [Fact]
        public void SamePage_OffOriginIsNeverTheSourcePage()
        { Assert.False(SitePolicy.SamePage("https://example.com/student/transcript", SitePolicy.Origin + "/student/transcript")); }
        [Fact]
        public void History_NetworkPathsOutsideDownloadsAndUnfinishedRecordsLoseTheirPath()
        {
            var downloads = Path.Combine(root, "Downloads");
            var inside = Path.Combine(downloads, "ok.pdf");
            var moved = Path.Combine(root, "Old downloads", "kept.pdf");
            var json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new[]
            {
                new DownloadRecord { Id = "inside", FileName = "ok.pdf", Path = inside, State = DownloadState.Completed },
                new DownloadRecord { Id = "moved", FileName = "kept.pdf", Path = moved, State = DownloadState.Completed },
                new DownloadRecord { Id = "share", FileName = "x.pdf", Path = @"\\host\share\x.pdf", State = DownloadState.Completed },
                new DownloadRecord { Id = "device", FileName = "w.pdf", Path = @"\\?\UNC\host\share\w.pdf", State = DownloadState.Completed },
                new DownloadRecord { Id = "failed", FileName = "z.pdf", Path = Path.Combine(downloads, "z.pdf"), State = DownloadState.Failed },
            });
            File.WriteAllText(Path.Combine(root, "downloads.json"), json);
            var history = new DownloadHistory(root); history.Load(downloads);
            Assert.Equal(5, history.Items.Count);
            Assert.Equal(inside, history.Items.Single(x => x.Id == "inside").Path);
            // A Downloads folder moved without its files keeps the old local records openable.
            Assert.Equal(moved, history.Items.Single(x => x.Id == "moved").Path);
            Assert.All(history.Items.Where(x => x.Id == "share" || x.Id == "device" || x.Id == "failed"), x => Assert.Equal("", x.Path));
        }
        [Fact]
        public void History_RedirectedNetworkDownloadsFolderKeepsItsFiles()
        {
            var share = @"\\fileserver\users\student\Downloads";
            File.WriteAllText(Path.Combine(root, "downloads.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new[]
            { new DownloadRecord { Id = "a", FileName = "a.pdf", Path = share + @"\a.pdf", State = DownloadState.Completed } }));
            var history = new DownloadHistory(root); history.Load(share);
            Assert.Equal(share + @"\a.pdf", history.Items.Single().Path);
        }
        [Fact]
        public void Status_UnsupportedPageFile_ExplainsWithoutHidingADownloadResult()
        {
            var status = new UiStatus(); status.Update("downloadComplete"); status.Update("downloadUnsupported");
            Assert.Equal("downloadUnsupported", status.Current.Key);
            Assert.Equal(MessageTone.Warning, status.Current.Tone); Assert.Equal(MessageAction.None, status.Current.Action); Assert.True(status.Current.CanDismiss);
            status.Dismiss(); Assert.Equal("downloadComplete", status.Current.Key);
        }
        [Fact]
        public void Status_UnexpectedError_OffersRetryAndKeepsTheSignInStateApart()
        {
            var status = new UiStatus(); status.Update("unexpectedError");
            Assert.Equal(MessageTone.Error, status.Current.Tone); Assert.Equal(MessageAction.Retry, status.Current.Action);
            status.Update("automationFailed"); Assert.Equal("automationFailed", status.Current.Key);
            status.Update("unexpectedError"); Assert.Equal("automationFailed", status.Current.Key);
            // A new page clears the unexpected error like a failed load; the sign-in failure stays until Retry.
            status.BeginNavigation(); Assert.Equal("automationFailed", status.Current.Key);
            status.Update("unexpectedError"); status.Retry(); Assert.Null(status.Current);
        }
        [Fact]
        public void Status_UnreadableCredentials_PointToSettingsUntilANewSession()
        {
            var status = new UiStatus(); status.Update("credentialsUnreadable");
            Assert.Equal(MessageAction.Settings, status.Current.Action); Assert.False(status.Current.CanDismiss);
            status.BeginNavigation(); status.Update("ready"); Assert.Equal("credentialsUnreadable", status.Current.Key);
            status.Update("manualSignIn"); Assert.Equal("manualSignIn", status.Current.Key);
            status.Update("credentialsUnreadable"); status.NewSession(); Assert.Null(status.Current);
        }
        [Fact]
        public void Status_SlowPageMessage_LeavesWithTheNextPage()
        {
            var status = new UiStatus(); status.Update("presentationSlow");
            Assert.Equal("presentationSlow", status.Current.Key); Assert.Equal(MessageAction.Retry, status.Current.Action);
            status.BeginNavigation(); status.Update("ready"); Assert.Null(status.Current);
        }
        [Fact]
        public void FormSubmissions_AFileAnsweringARecentPostIsNotFetchedAgain()
        {
            var forms = new FormSubmissions(); var export = SitePolicy.Origin + "/student/export";
            Assert.False(forms.Answers(export, 0));
            forms.Posted(export, 1000);
            Assert.True(forms.Answers(export, 1000 + FormSubmissions.Window));
            Assert.False(forms.Answers(export, 1001 + FormSubmissions.Window));
            Assert.False(forms.Answers(SitePolicy.Origin + "/student/umkd/get/1/2/2026/1", 1500));
            Assert.False(forms.Answers(null, 1500));
        }
        [Fact]
        public void FormSubmissions_KeepOnlyTheNewestPosts()
        {
            var forms = new FormSubmissions();
            for (var i = 0; i <= FormSubmissions.Limit; i++) forms.Posted(SitePolicy.Origin + "/form/" + i, i * 10);
            Assert.False(forms.Answers(SitePolicy.Origin + "/form/0", 500));
            Assert.True(forms.Answers(SitePolicy.Origin + "/form/" + FormSubmissions.Limit, 500));
            Assert.True(forms.Answers(SitePolicy.Origin + "/form/1", 500));
        }
        [Fact]
        public void Status_FileFromAForm_IsExplainedLikeAPageMadeFile()
        {
            var status = new UiStatus(); status.Update("downloading"); status.Update("downloadFromForm");
            Assert.Equal("downloadFromForm", status.Current.Key); Assert.Equal(MessageAction.None, status.Current.Action);
            status.Dismiss(); Assert.Equal("downloading", status.Current.Key);
        }
        [Fact]
        public void KeyReleases_AnotherKeyDownForgetsAHandledPressWhoseReleaseChromiumDropped()
        {
            // Ctrl+0 handled, its release dropped by Chromium; the next key press ends that, so a PDF-guest Ctrl+0 release fires.
            var keys = new KeyReleases(); keys.Pressed(48, 0);
            keys.KeyDown(16);
            Assert.False(keys.Released(48, 100));
        }
        [Fact]
        public void KeyReleases_AKeyDownThatReachedThePageEndsEveryHandledPress()
        {
            // Chromium 109's PDF guest sends key-downs past the pre-renderer hook; such a key-down still ends the earlier press.
            var keys = new KeyReleases(); keys.Pressed(48, 0);
            keys.PageKeyDown();
            Assert.False(keys.Released(48, 100));
        }
        [Fact]
        public void KeyReleases_AutoRepeatOfTheSameKeyKeepsThePress()
        {
            var keys = new KeyReleases(); keys.KeyDown(18); keys.Pressed(37, 0);
            keys.KeyDown(37); keys.Pressed(37, 500);
            Assert.True(keys.Released(37, 600));
        }
        [Fact]
        public void KeyReleases_OneTrackerServesPressAndReleaseInDifferentBrowsers()
        {
            // Alt+Left pressed in the viewer closes it; the release arrives over the main page and must not go Back again.
            var shared = new KeyReleases(); shared.Pressed(37, 0);
            Assert.True(shared.Released(37, 400));
        }
    }
}
