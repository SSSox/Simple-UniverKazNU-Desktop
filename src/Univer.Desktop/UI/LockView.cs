using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // The whole window while the app is locked: the site, the sidebar and the toasts stay under it, unseen and unreachable.
    // A card in the middle holds the PIN pad, or, when the PIN record cannot be read, the way back through signing in again.
    internal sealed class LockView : Panel
    {
        private readonly Func<int, int> sz;
        private readonly Panel card = new SmoothPanel();
        private readonly Label problem = new Label();
        private readonly ModernButton again = new ModernButton();
        public PinPad Pad { get; private set; }
        public bool Unavailable { get; private set; }
        public event Action SignInAgain;
        internal ModernButton SignInAgainForQa { get { return again; } }
        public LockView(Func<int, int> sz)
        {
            this.sz = sz; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            // Placed by hand over the whole window: docked Fill in front, it would leave the docked sidebar uncovered.
            Dock = DockStyle.None; Visible = false; BackColor = Theme.Background; TabStop = false;
            card.BackColor = Theme.Background; card.Paint += (s, e) => PaintCard(e.Graphics);
            Pad = new PinPad(sz);
            problem.Font = Theme.Font(Theme.Body); problem.ForeColor = Theme.Danger; problem.TextAlign = ContentAlignment.MiddleCenter; problem.UseMnemonic = false; problem.Visible = false;
            again.Look = ButtonLook.Primary; again.Visible = false; again.Click += (s, e) => SignInAgain?.Invoke();
            card.Controls.Add(Pad); card.Controls.Add(problem); card.Controls.Add(again); Controls.Add(card);
            Resize += (s, e) => Arrange();
        }
        public void Render(string language)
        {
            Pad.SetTexts("lockUnlockTitle", "lockUnlockHint", "lockForgot", language);
            problem.Text = Strings.Get("lockUnavailable", language);
            again.Text = again.AccessibleName = Strings.Get("lockSignInAgain", language);
            Arrange();
        }
        // The PIN record exists but cannot be read (another Windows profile, a damaged file): only signing in again helps.
        public void SetUnavailable(bool value) { Unavailable = value; Pad.Visible = !value; problem.Visible = again.Visible = value; Arrange(); }
        public void FocusInput() { if (Unavailable) again.Focus(); else Pad.Focus(); }
        private void Arrange()
        {
            var padding = sz(32);
            var width = Math.Min(ClientSize.Width - sz(32), Pad.WantedWidth + padding * 2);
            var height = Unavailable ? sz(48) + sz(64) + sz(40) + padding * 2 : Pad.Wanted + sz(56) + padding * 2;
            height = Math.Min(height, Math.Max(sz(200), ClientSize.Height - sz(32)));
            card.SetBounds((ClientSize.Width - width) / 2, Math.Max(sz(16), (ClientSize.Height - height) / 2), width, height);
            var top = padding + sz(56);
            Pad.SetBounds(padding, top, width - padding * 2, Math.Max(0, height - top - padding));
            problem.SetBounds(padding, top, width - padding * 2, sz(64));
            again.SetBounds((width - sz(220)) / 2, problem.Bottom + sz(8), sz(220), sz(40));
            card.Invalidate();
        }
        private void PaintCard(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, card.Width - 1, card.Height - 1), sz(Kit.RadiusCard)))
            using (var fill = new SolidBrush(Theme.Surface)) using (var pen = new Pen(Theme.Current.Line)) { g.FillPath(fill, path); g.DrawPath(pen, path); }
            // The lock sign in a soft accent circle over the pad.
            var circle = new RectangleF((card.Width - sz(48)) / 2f, sz(28), sz(48), sz(48));
            using (var brush = new SolidBrush(Color.FromArgb(36, Theme.Accent))) g.FillEllipse(brush, circle);
            Icons.Draw(g, UiIcon.Lock, RectangleF.Inflate(circle, -sz(13), -sz(13)), Theme.Accent);
        }
    }
}
