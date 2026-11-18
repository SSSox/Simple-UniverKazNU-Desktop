using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public sealed class UiStatusTests
    {
        [Fact]
        public void DownloadProgressMayExpireWithoutRestoringOnPolling_AndCompletionIsANewNotice()
        {
            var status = new UiStatus(); status.Update("downloading"); Assert.True(status.Current.CanDismiss);
            status.Dismiss(); status.Update("ready"); Assert.Null(status.Current);
            status.Update("downloadComplete"); Assert.Equal("downloadComplete", status.Current.Key);
            status.Update("storageError"); status.Dismiss(); Assert.Equal("storageError", status.Current.Key);
        }
        [Theory]
        [InlineData("downloadComplete")]
        [InlineData("downloadFailed")]
        [InlineData("automationFailed")]
        [InlineData("storageError")]
        public void PagePollingDoesNotEraseActionableMessage(string key)
        { var status = new UiStatus(); status.Update(key); status.Update("ready"); status.Update("loading"); Assert.Equal(key, status.Current.Key); }
        [Fact]
        public void AutomaticSignInRestore_AsksNothingAndEndsWithASignedInPage()
        {
            var status = new UiStatus(); status.Update("sessionRestoring");
            Assert.Equal(MessageTone.Working, status.Current.Tone); Assert.Equal(MessageAction.None, status.Current.Action);
            status.Update("signingIn"); Assert.Equal("sessionRestoring", status.Current.Key);
            status.Update("ready"); Assert.Null(status.Current);
            var reset = UiStatus.Describe("sessionReset");
            Assert.Equal(MessageAction.None, reset.Action); Assert.True(reset.CanDismiss);
        }
        [Fact]
        public void DownloadSignInNoticeSurvivesPollingAndIsReplacedByOutcome()
        {
            var status = new UiStatus(); status.Update("downloadLogin"); status.Update("downloadSigningIn");
            status.Update("ready"); status.Update("loading"); Assert.Equal("downloadSigningIn", status.Current.Key);
            status.Update("downloading"); status.Update("downloadComplete"); Assert.Equal("downloadComplete", status.Current.Key);
        }
        // The grade summary's answers are not part of the page's automation: a sign-in tick or a ready page leaves them.
        [Theory]
        [InlineData("reportCopied")]
        [InlineData("reportFailed")]
        [InlineData("reportCopyFailed")]
        public void GradeSummaryAnswerSurvivesAutomationAndPolling(string key)
        {
            var status = new UiStatus(); status.Update(key); status.Update("signingIn"); status.Update("ready");
            Assert.True(status.Shows(key)); Assert.True(UiStatus.Describe(key).CanDismiss); Assert.Equal(MessageAction.None, UiStatus.Describe(key).Action);
        }
        [Fact]
        public void SuccessfulPageClearsWorkingAutomationOnly()
        { var status = new UiStatus(); status.Update("signingIn"); status.Update("ready"); Assert.Null(status.Current); Assert.Equal("ready", status.PageKey); }
        [Fact]
        public void DismissRevealsOtherIndependentMessage()
        { var status = new UiStatus(); status.Update("manualSignIn"); status.Update("downloadComplete"); status.Dismiss(); Assert.Equal("manualSignIn", status.Current.Key); }
        [Fact]
        public void ErrorPriorityPreventsFalseSuccess()
        { var status = new UiStatus(); status.Update("storageError"); status.Update("downloadComplete"); Assert.Equal(MessageTone.Error, status.Current.Tone); status.Dismiss(); Assert.Equal("storageError", status.Current.Key); }
        [Fact]
        public void FailedSettingsOperationCanRemoveWorkingMessage()
        { var status = new UiStatus(); status.Update("saving"); status.FinishSettings(); status.Update("manualSignIn"); Assert.Equal("manualSignIn", status.Current.Key); }
        [Fact]
        public void SilentSignInUsesLoadingStateWithoutAnAuthenticationNotification()
        { var status = new UiStatus(); status.Update("signingIn"); Assert.Null(status.Current); Assert.Equal("loading", status.PageKey); status.Update("ready"); Assert.Equal("ready", status.PageKey); }
        [Fact]
        public void KnownNoticeUsesLoadingStateWithoutAnAgreementNotification()
        { var status = new UiStatus(); status.Update("confirming"); Assert.Null(status.Current); Assert.Equal("loading", status.PageKey); }
        [Fact]
        public void OversizedDownloadStaysVisibleAndOffersDownloads()
        {
            var status = new UiStatus(); status.Update("downloadTooLarge"); status.Update("ready");
            Assert.Equal("downloadTooLarge", status.Current.Key);
            Assert.Equal(MessageAction.Downloads, status.Current.Action);
        }
        [Fact]
        public void CertificateFailureOffersRetryAndSurvivesReadyPolling()
        { var status = new UiStatus(); status.Update("certificateError"); status.Update("ready"); Assert.Equal(MessageAction.Retry, status.Current.Action); Assert.Equal(MessageTone.Error, status.Current.Tone); status.BeginNavigation(); Assert.Null(status.Current); }
        [Fact]
        public void PresentationDelayOffersRetryAndClearsOnNextNavigation()
        {
            var status = new UiStatus(); status.Update("presentationSlow"); status.Update("ready"); status.Update("loading");
            Assert.Equal("presentationSlow", status.Current.Key);
            Assert.Equal(MessageAction.Retry, status.Current.Action);
            Assert.True(status.Current.CanDismiss);
            status.BeginNavigation(); Assert.Null(status.Current);
            status.Update("presentationSlow"); status.Retry(); Assert.Null(status.Current);
        }
        // A summary that could not be copied is a warning on the grades page only; a saved download stays told.
        [Fact]
        public void GradeSummaryAnswerLeavesWithItsPage()
        {
            var status = new UiStatus(); status.Update("reportCopyFailed"); status.Update("downloadComplete");
            Assert.Equal("reportCopyFailed", status.Current.Key);
            status.BeginNavigation(); Assert.Equal("downloadComplete", status.Current.Key);
        }
    }
}
