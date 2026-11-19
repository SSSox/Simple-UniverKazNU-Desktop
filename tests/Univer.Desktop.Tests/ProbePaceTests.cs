using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ProbePaceTests
    {
        [Theory]
        // A press within the last ten seconds: the shown page is asked on every tick.
        [InlineData(true, 0, 1000, true)]
        [InlineData(true, 9999, 1000, true)]
        // Long after the last press a shown page is asked every fifteen seconds.
        [InlineData(true, 10000, 1000, false)]
        [InlineData(true, 60000, 14999, false)]
        [InlineData(true, 60000, 15000, true)]
        // A window in the tray or minimized is asked every thirty seconds, whatever was pressed before it was hidden.
        [InlineData(false, 0, 1000, false)]
        [InlineData(false, 60000, 15000, false)]
        [InlineData(false, 60000, 29999, false)]
        [InlineData(false, 60000, 30000, true)]
        public void A_quiet_page_is_asked_often_only_after_a_press(bool shown, long sinceInput, long sinceProbe, bool due)
        {
            Assert.Equal(due, ProbePace.Due(shown, sinceInput, sinceProbe));
        }

        [Fact]
        public void A_clock_that_went_back_asks_now_and_does_not_count_as_a_press()
        {
            Assert.True(ProbePace.Due(true, 60000, -1));
            Assert.False(ProbePace.Due(true, -1, 1000));
        }
    }
}
