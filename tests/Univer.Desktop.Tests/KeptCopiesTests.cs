using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class KeptCopiesTests
    {
        [Fact]
        public void APausedOrMissingSignInLoadsNothing()
        {
            Assert.False(KeptCopies.Readable(false, false, false));
        }

        [Fact]
        public void AnAppThatSignsInByItselfLoadsTheCopies()
        {
            Assert.True(KeptCopies.Readable(true, false, false));
        }

        [Fact]
        public void AFailedStorageLoadsNothing()
        {
            Assert.False(KeptCopies.Readable(true, true, false));
            Assert.False(KeptCopies.Readable(false, true, true));
        }

        [Fact]
        public void TheOfflineQaRunReadsItsOwnCopies()
        {
            Assert.True(KeptCopies.Readable(false, false, true));
        }

        [Fact]
        public void APauseDropsAHeldCopyBeforeAnythingElse()
        {
            Assert.Equal(KeptCopyStep.Drop, KeptCopies.Step(false, true, false));
            Assert.Equal(KeptCopyStep.Drop, KeptCopies.Step(false, true, true));
            Assert.Equal(KeptCopyStep.Keep, KeptCopies.Step(false, false, false));
        }

        [Fact]
        public void APausedStartThenASameAccountResumeReadsTheRetainedCopy()
        {
            // Paused start: the copy on disk stays unread.
            var start = KeptCopies.Step(KeptCopies.Readable(false, false, false), false, false);
            Assert.Equal(KeptCopyStep.Keep, start);
            // Automatic sign-in resumes for the same login: the retained copy is read again.
            var resumed = KeptCopies.Step(KeptCopies.Readable(true, false, false), false, false);
            Assert.Equal(KeptCopyStep.Load, resumed);
        }

        [Fact]
        public void AResumeLeavesACopyWhoseDeleteIsPendingUnread()
        {
            Assert.Equal(KeptCopyStep.Keep, KeptCopies.Step(true, false, true));
        }

        [Fact]
        public void AHeldCopyIsNotReadAgain()
        {
            Assert.Equal(KeptCopyStep.Keep, KeptCopies.Step(true, true, false));
        }
    }
}
