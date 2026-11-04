using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // Ctrl+wheel zoom steps: a notch is one step; touchpad and smooth-wheel fractions add up until a notch.
    public class WheelStepsTests
    {
        [Fact]
        public void EachNotchIsOneStepInItsDirection()
        {
            var wheel = new WheelSteps();
            Assert.Equal(1, wheel.Add(120, 0));
            Assert.Equal(2, wheel.Add(240, 10));
            Assert.Equal(-1, wheel.Add(-120, 20));
        }
        [Fact]
        public void FractionsOfANotchAddUp()
        {
            var wheel = new WheelSteps();
            Assert.Equal(0, wheel.Add(40, 0));
            Assert.Equal(0, wheel.Add(40, 16));
            Assert.Equal(1, wheel.Add(40, 32));
            Assert.Equal(0, wheel.Add(100, 48));
            Assert.Equal(1, wheel.Add(30, 64));
        }
        [Fact]
        public void TurningBackOrPausingDropsTheRemainder()
        {
            var wheel = new WheelSteps();
            Assert.Equal(0, wheel.Add(100, 0));
            Assert.Equal(0, wheel.Add(-100, 10));
            Assert.Equal(-1, wheel.Add(-20, 20));
            Assert.Equal(0, wheel.Add(100, 30));
            Assert.Equal(0, wheel.Add(30, 30 + WheelSteps.Pause + 1));
            Assert.Equal(1, wheel.Add(90, WheelSteps.Pause + 50));
        }
        [Fact]
        public void APauseOfExactlyThePauseKeepsTheRemainder()
        {
            var wheel = new WheelSteps();
            Assert.Equal(0, wheel.Add(60, 1000));
            Assert.Equal(1, wheel.Add(60, 1000 + WheelSteps.Pause));
            Assert.Equal(0, wheel.Add(60, 2000));
            Assert.Equal(0, wheel.Add(60, 2000 + WheelSteps.Pause + 1));
        }
        [Fact]
        public void TheTickCounterMayWrap()
        {
            var wheel = new WheelSteps();
            Assert.Equal(0, wheel.Add(60, int.MaxValue - 5));
            Assert.Equal(1, wheel.Add(60, unchecked(int.MaxValue + 5)));
        }
    }
}
