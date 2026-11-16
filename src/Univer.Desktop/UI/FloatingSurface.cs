using System;
using System.Drawing;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // A panel that opens over the website. A child window is empty until it paints, and Windows may show that frame, so the
    // panel first comes up behind an empty region. A picture of it fades in above the page, the real panel takes over under
    // the finished picture, and closing fades a picture of it out. The picture owns no input; the panel has the focus at once.
    internal sealed class FloatingSurface : IDisposable
    {
        private const int Duration = 200;
        private MotionPopup arrival, departure;
        public bool Arriving { get { return arrival != null; } }
        public void Show(Control target, int radius, Action shape)
        {
            Settle(); departure?.Dispose(); departure = null;
            target.Region = new Region(Rectangle.Empty);
            target.Visible = true; target.BringToFront();
            var taken = false;
            Action takeOver = () => { taken = true; arrival = null; if (target.IsDisposed) return; shape(); Kit.PaintNow(target); };
            var picture = Kit.Moving(target) ? MotionPopup.Round(MotionPopup.Capture(target), radius) : null;
            if (picture == null) { takeOver(); return; }
            var made = MotionPopup.Arrive(picture, target.RectangleToScreen(target.ClientRectangle), Duration, takeOver);
            // With animations off the arrival ends while it is being made.
            if (!taken) arrival = made;
        }
        public void Hide(Control target, int radius)
        {
            Settle();
            if (Kit.Moving(target)) { departure?.Dispose(); departure = MotionPopup.Play(MotionPopup.Round(MotionPopup.Capture(target), radius), target.RectangleToScreen(target.ClientRectangle), 160); }
            target.Visible = false;
        }
        public void Settle() { var open = arrival; arrival = null; open?.Drop(); }
        public void Dispose() { arrival?.Dispose(); arrival = null; departure?.Dispose(); departure = null; }
    }
}
