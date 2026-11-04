using System;

namespace Univer.Desktop.Core
{
    // Ctrl+wheel zoom: one step per notch (120), forward zooms in; touchpad fractions add up, a reversal or pause drops them.
    internal sealed class WheelSteps
    {
        public const int Notch = 120, Pause = 700;
        private int rest, last;
        // Before the first call rest is 0, so dropping it then changes nothing.
        public int Add(int delta, int now)
        {
            if (unchecked(now - last) > Pause || rest != 0 && Math.Sign(delta) != Math.Sign(rest)) rest = 0;
            last = now;
            rest += delta;
            var steps = rest / Notch;
            rest -= steps * Notch;
            return steps;
        }
    }
}
