using System;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class PageAccountTests
    {
        // The same vectors are pinned in tests/dom/automation.test.cjs against univer-automation.js's own mark.
        [Theory]
        [InlineData("Student.One", "ad6afa8d2")]
        [InlineData(" Student.One ", "ad6afa8d2")]
        [InlineData("student.one", "ad6afa8d2")]
        [InlineData("Студент01", "abd258a5c")]
        [InlineData("  СТУДЕНТ01 ", "abd258a5c")]
        [InlineData("qa-grades", "ae94b0d06")]
        [InlineData("a", "ae40c292c")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void TheMarkIsTheSameFnvHashThePageNotes(string login, string mark)
        {
            Assert.Equal(mark, PageAccount.Mark(login));
        }

        [Fact]
        public void NoLoginHasNoMark()
        {
            Assert.Equal("", PageAccount.Mark(null));
        }

        [Fact]
        public void AReadingIsAcceptedForTheSavedLoginsMarkOrNoMarkAtAll()
        {
            Assert.True(PageAccount.Accepts("Student.One", "ad6afa8d2"));
            Assert.True(PageAccount.Accepts("Student.One", ""));
            Assert.True(PageAccount.Accepts("Student.One", null));
            Assert.False(PageAccount.Accepts("Student.One", "abd258a5c"));
            Assert.False(PageAccount.Accepts("Student.One", "AD6AFA8D2"));
        }

        [Fact]
        public void WithoutASavedLoginNothingIsAccepted()
        {
            Assert.False(PageAccount.Accepts(null, ""));
            Assert.False(PageAccount.Accepts("  ", "ad6afa8d2"));
        }

        [Fact]
        public void TheAnswerSplitsIntoTheMarkAndTheReading()
        {
            string mark, raw;
            Assert.True(PageAccount.Split("ad6afa8d2\n{\"v\":1}", out mark, out raw));
            Assert.Equal("ad6afa8d2", mark); Assert.Equal("{\"v\":1}", raw);
            Assert.True(PageAccount.Split("\n", out mark, out raw));
            Assert.Equal("", mark); Assert.Equal("", raw);
            Assert.True(PageAccount.Split("a\nline one\nline two", out mark, out raw));
            Assert.Equal("a", mark); Assert.Equal("line one\nline two", raw);
            Assert.False(PageAccount.Split(null, out mark, out raw));
            Assert.False(PageAccount.Split("no break", out mark, out raw));
        }

        [Fact]
        public void TheScriptReadsTheMarkAndTheReaderInOneEvaluation()
        {
            var script = PageAccount.Script("window.x?window.x():''");
            Assert.Contains("univer.app.account.v1", script);
            Assert.Contains("var r=window.x?window.x():'';", script);
            Assert.Contains("replace(/[\\r\\n]/g,'?')", script);
        }

        [Fact]
        public void ARetryIsShortThreeTimesThenAMinute()
        {
            var retry = new ReadRetry();
            Assert.Equal(new[] { 10000L, 10000L, 10000L, 60000L, 60000L }, new[] { retry.NextDelayMs(), retry.NextDelayMs(), retry.NextDelayMs(), retry.NextDelayMs(), retry.NextDelayMs() });
            Assert.Equal(ReadRetry.ShortTries, retry.Used);
            retry.Reset();
            Assert.Equal(10000L, retry.NextDelayMs());
        }
    }
}
