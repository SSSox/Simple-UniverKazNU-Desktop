using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class SessionGuardTests
    {
        [Fact]
        public void LoginVisible_NewLoginDocument_WaitsAMinuteThenSignsInOnThatForm()
        {
            var guard = new SessionGuard(); Assert.True(guard.TryLogin("a", 0));
            Assert.True(guard.LoginVisible("b", 1000)); Assert.False(guard.TryLogin("b", 30000));
            Assert.False(guard.Stopped); Assert.False(guard.NeedsNewDocument("b"));
            Assert.False(guard.LoginVisible("b", 61000)); Assert.True(guard.TryLogin("b", 61000)); Assert.False(guard.Blocked);
        }
        [Fact]
        public void LoginVisible_SecondRefusalInARow_StopsUntilTheUser()
        {
            var guard = new SessionGuard(); guard.TryLogin("a", 0); guard.LoginVisible("b", 1000);
            Assert.True(guard.TryLogin("b", 61000)); Assert.True(guard.LoginVisible("c", 62000));
            Assert.True(guard.Stopped); Assert.False(guard.TryLogin("d", 700000));
            guard.Retry(); Assert.True(guard.TryLogin("d", 700001));
        }
        [Fact]
        public void LoginVisible_RefusalAfterAConfirmedSignIn_StartsCountingAgain()
        {
            var guard = new SessionGuard(); guard.TryLogin("a", 0); guard.LoginVisible("b", 1000);
            Assert.True(guard.TryLogin("b", 61000)); guard.Authenticated();
            Assert.True(guard.TryLogin("c", 62000)); guard.LoginVisible("d", 63000);
            Assert.False(guard.Stopped); Assert.True(guard.Blocked);
        }
        [Fact]
        public void LoginVisible_Timeout_ANewerDocumentSignsInAgainWithoutRetry()
        {
            var guard = new SessionGuard(); guard.TryLogin("a", 0);
            Assert.True(guard.LoginVisible("a", 25001)); Assert.True(guard.NeedsNewDocument("a")); Assert.False(guard.Stopped);
            Assert.False(guard.TryLogin("a", 90000));
            Assert.False(guard.TryLogin("b", 50000)); // a minute after the stall at the earliest
            Assert.True(guard.TryLogin("b", 85002)); Assert.False(guard.Blocked);
        }
        [Fact]
        public void LoginBudget_Full_IsAWaitThatContinuesByItself()
        {
            var guard = new SessionGuard();
            for (var i = 0; i < 3; i++) { Assert.True(guard.TryLogin(i.ToString(), i * 1000)); guard.Authenticated(); }
            Assert.False(guard.TryLogin("fourth", 3000)); Assert.False(guard.Blocked);
            Assert.False(guard.TryLogin("fourth", 599999)); Assert.True(guard.TryLogin("fourth", 600000));
        }
        [Fact]
        public void AuthenticatedPage_NewerSignedInPage_LiftsABlockButNotAHardStop()
        {
            var guard = new SessionGuard(); guard.TryLogin("a", 0); guard.LoginVisible("a", 25001);
            Assert.False(guard.AuthenticatedPage("a")); Assert.True(guard.AuthenticatedPage("news")); Assert.False(guard.Blocked);
            guard.Stop(); Assert.False(guard.AuthenticatedPage("other")); Assert.True(guard.Stopped);
        }
        [Fact]
        public void Suspend_SlowPage_UncoversWithoutStoppingSignIn()
        {
            var guard = new SessionGuard(); Assert.True(guard.Suspend()); Assert.True(guard.CoverSuspended);
            Assert.False(guard.Blocked); Assert.True(guard.TryLogin("a", 0));
            guard.Navigated(); Assert.False(guard.CoverSuspended);
        }
        [Fact]
        public void Suspend_DuringPendingLogin_KeepsWaitingForItsAnswer()
        {
            var guard = new SessionGuard(); guard.TryLogin("a", 0); Assert.True(guard.Suspend()); guard.Navigated();
            Assert.False(guard.Blocked); Assert.Equal("a", guard.LoginDocument);
        }
        [Fact]
        public void Suspend_AfterExplicitStop_StaysStoppedAfterNavigation()
        {
            var guard = new SessionGuard(); guard.Stop(); Assert.False(guard.Suspend()); guard.Navigated();
            Assert.True(guard.Stopped); Assert.False(guard.TryLogin("a", 70000));
        }
        [Fact]
        public void Navigated_AfterStop_RequiresExplicitRetry()
        {
            var guard = new SessionGuard(); guard.Suspend(); guard.Stop(); guard.Navigated();
            Assert.True(guard.Blocked); guard.Retry(); Assert.False(guard.Blocked);
        }
        [Fact]
        public void TryLogin_PendingRequest_DoesNotSendAgain()
        {
            var guard = new SessionGuard(); Assert.True(guard.TryLogin("a", 0)); Assert.False(guard.TryLogin("a", 100));
        }
        [Fact]
        public void TryLogin_ConfirmedSuccess_AllowsNewServerLoginBeforeOneMinute()
        {
            var guard = new SessionGuard(); guard.TryLogin("a", 0); guard.Authenticated();
            Assert.True(guard.TryLogin("b", 1000)); Assert.False(guard.Blocked);
        }
        [Fact]
        public void ReauthResult_WrongChallenge_DoesNotConfirm()
        {
            var guard = new SessionGuard(); guard.TryReauth("a", "doc", 0); guard.ReauthResult("b", true);
            Assert.Equal("a", guard.ReauthChallenge); Assert.True(guard.ReauthTimedOut(25001));
        }
        [Fact]
        public void ReauthResult_Failure_ReloadsForTheFullPageLogin()
        {
            var guard = new SessionGuard(); guard.TryReauth("a", "doc", 0); guard.ReauthResult("a", false);
            Assert.True(guard.Blocked); Assert.False(guard.Stopped); Assert.Null(guard.ReauthChallenge);
            Assert.True(guard.NeedsNewDocument("doc"));
            Assert.True(guard.TryLogin("login", 60000)); Assert.False(guard.Blocked);
        }
        [Fact]
        public void ReauthTimedOut_ANewerDialogMayAnswerAgain()
        {
            var guard = new SessionGuard(); guard.TryReauth("a", "doc", 0);
            Assert.True(guard.ReauthTimedOut(25001)); Assert.False(guard.Stopped);
            Assert.False(guard.TryReauth("b", "doc", 90000)); Assert.True(guard.TryReauth("b", "reloaded", 90000));
        }
        [Fact]
        public void ReauthRejected_SecondTryOfTheDialog_BlocksThatDocumentOnly()
        {
            var guard = new SessionGuard(); guard.ReauthRejected("doc", 0);
            Assert.True(guard.NeedsNewDocument("doc")); Assert.False(guard.Stopped);
            guard.ReauthRejected("other", 10); Assert.True(guard.NeedsNewDocument("other"));
            guard.Stop(); guard.ReauthRejected("third", 20); Assert.True(guard.Stopped);
        }
        [Fact]
        public void SuccessfulReauthAllowsANewServerChallengeWithoutOneMinuteLockout()
        {
            var guard = new SessionGuard(); Assert.True(guard.TryReauth("first", "doc", 0)); guard.ReauthResult("first", true);
            Assert.True(guard.TryReauth("second", "doc", 1500)); Assert.False(guard.Blocked);
        }
        [Fact]
        public void SuccessfulReauthDoesNotResubmitAnAnimatingDialog()
        {
            var guard = new SessionGuard(); guard.TryReauth("first", "doc", 0); guard.ReauthResult("first", true);
            Assert.False(guard.TryReauth("first", "doc", 500)); Assert.False(guard.Blocked);
        }
        [Fact]
        public void RepeatedServerChallengesWaitForTheBudgetWithoutStopping()
        {
            var guard = new SessionGuard();
            for (var i = 0; i < 3; i++) { Assert.True(guard.TryReauth(i.ToString(), "doc", i * 1000)); guard.ReauthResult(i.ToString(), true); }
            Assert.False(guard.TryReauth("fourth", "doc", 3000)); Assert.False(guard.Blocked);
            Assert.True(guard.TryReauth("fourth", "doc", 60000));
        }
        [Fact]
        public void ConfirmationBudget_ReloadSameNotice_ReservesBeforeClick()
        {
            var budget = new ConfirmationBudget(); budget.Submitted("notice", 0);
            Assert.False(budget.CanSubmit("notice", 61000));
        }
        [Fact]
        public void ConfirmationBudget_Forget_ReloadedNoticeMaySendAgainWithinTheBudget()
        {
            var budget = new ConfirmationBudget(); budget.Submitted("a", 0); budget.Submitted("b", 10); budget.Submitted("c", 20);
            budget.Forget(); Assert.False(budget.CanSubmit("a", 500)); Assert.True(budget.CanSubmit("a", 60001));
        }
        [Fact]
        public void TryLogin_NewServerSession_FreesTheNoticeSentBeforeTheSessionEnded()
        {
            var guard = new SessionGuard(); guard.Notices.Submitted("notice", 0); guard.Agreements.Submitted("agreement", 0);
            Assert.False(guard.Notices.CanSubmit("notice", 1000)); // only the login form came in between
            Assert.True(guard.TryLogin("login", 1000));
            Assert.True(guard.Notices.CanSubmit("notice", 2000)); Assert.True(guard.Agreements.CanSubmit("agreement", 2000));
        }
        [Fact]
        public void TryLogin_RefusedWhilePending_KeepsTheLaterReservation()
        {
            var guard = new SessionGuard(); guard.Notices.Submitted("notice", 0); guard.TryLogin("a", 0);
            guard.Notices.Submitted("notice", 10); Assert.False(guard.TryLogin("b", 20)); // the pending login is not a new session
            Assert.False(guard.Notices.CanSubmit("notice", 30));
        }
        [Fact]
        public void AppearanceTimer_EachAppearanceGetsItsOwnLimit()
        {
            var timer = new AppearanceTimer();
            Assert.False(timer.Expired("first", 0, 25000)); Assert.True(timer.Expired("first", 25001, 25000));
            // The same notice again after a login page: a new appearance, not an old failure.
            Assert.False(timer.Expired("second", 40000, 25000)); Assert.False(timer.Expired("second", 65000, 25000));
            Assert.True(timer.Expired("second", 65001, 25000));
            timer.Reset(); Assert.False(timer.Expired("second", 90000, 25000));
        }
        [Fact]
        public void ConfirmationBudget_ConfirmedAbsence_AllowsLaterAppearance()
        {
            var budget = new ConfirmationBudget(); budget.Submitted("notice", 0);
            budget.Observe(false, 100); budget.Observe(false, 2101);
            Assert.True(budget.CanSubmit("notice", 2102));
        }
        [Fact]
        public void ConfirmationBudget_TransientAbsence_DoesNotReset()
        {
            var budget = new ConfirmationBudget(); budget.Submitted("notice", 0);
            budget.Observe(false, 100); budget.Observe(true, 1000);
            Assert.False(budget.CanSubmit("notice", 2102));
        }
        [Fact]
        public void ReleaseLogin_NothingSubmitted_ReturnsReservationAndAttempt()
        {
            var guard = new SessionGuard();
            for (var i = 0; i < 5; i++) { Assert.True(guard.TryLogin("a", i)); guard.ReleaseLogin("a"); }
            Assert.False(guard.Blocked); Assert.True(guard.TryLogin("a", 10));
            Assert.False(guard.TryLogin("a", 11)); // A real submission is still protected against a repeat.
        }
        [Fact]
        public void ReleaseLogin_OtherDocument_KeepsReservation()
        {
            var guard = new SessionGuard(); Assert.True(guard.TryLogin("a", 0)); guard.ReleaseLogin("b");
            Assert.False(guard.TryLogin("a", 1));
        }
        [Fact]
        public void ReleaseReauth_NothingSubmitted_AllowsSameChallengeAgain()
        {
            var guard = new SessionGuard();
            for (var i = 0; i < 5; i++) { Assert.True(guard.TryReauth("c", "doc", i)); guard.ReleaseReauth("c"); }
            Assert.False(guard.Blocked); Assert.True(guard.TryReauth("c", "doc", 10));
        }
        [Fact]
        public void ConfirmationBudget_Release_ReturnsKeyAndAttempt()
        {
            var budget = new ConfirmationBudget();
            for (var i = 0; i < 5; i++) { Assert.True(budget.CanSubmit("a", i)); budget.Submitted("a", i); budget.Release("a"); }
            Assert.True(budget.CanSubmit("a", 10)); budget.Submitted("a", 10); Assert.False(budget.CanSubmit("a", 11));
        }
        [Fact]
        public void ConfirmationBudget_ThreeDifferentPostsInOneMinute_StopsLoop()
        {
            var budget = new ConfirmationBudget(); budget.Submitted("a", 0); budget.Submitted("b", 10); budget.Submitted("c", 20);
            Assert.False(budget.CanSubmit("d", 500)); Assert.True(budget.CanSubmit("d", 60001));
        }
    }
}
