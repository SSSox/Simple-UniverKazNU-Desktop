using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // Six digits typed or clicked: a heading, six dots, one line for what went wrong and a number pad, as on the phone.
    // The sixth digit hands the code over at once; the host checks it and answers with Clear, ShowMessage or Wait.
    internal sealed class PinPad : Panel
    {
        private readonly Func<int, int> sz;
        private readonly Label title = new Label(), hint = new Label(), message = new Label();
        private readonly Panel dots = new SmoothPanel();
        private readonly ModernButton[] keys = new ModernButton[12];
        private readonly ModernButton extra = new ModernButton();
        private readonly StringBuilder digits = new StringBuilder(PinPolicy.Length);
        private readonly Timer countdown = new Timer { Interval = 250 };
        private DateTime waitUntil;
        private string language = "ru", messageKey;
        private bool busy;
        public event Action<string> Completed;
        public event Action ExtraClicked;
        internal int CountForQa { get { return digits.Length; } }
        internal string MessageForQa { get { return message.Text; } }
        internal bool AcceptsForQa { get { return !busy && !Waiting; } }
        internal ModernButton ExtraForQa { get { return extra; } }
        public bool Waiting { get { return waitUntil > DateTime.UtcNow; } }
        public PinPad(Func<int, int> sz)
        {
            this.sz = sz;
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); TabStop = true; BackColor = Theme.Surface;
            title.Font = Theme.Display(15f); title.TextAlign = ContentAlignment.MiddleCenter; title.UseMnemonic = false; title.ForeColor = Theme.Ink;
            hint.Font = Theme.Font(Theme.Meta); hint.TextAlign = ContentAlignment.MiddleCenter; hint.UseMnemonic = false; hint.ForeColor = Theme.Muted;
            message.Font = Theme.Font(Theme.Meta); message.TextAlign = ContentAlignment.TopCenter; message.UseMnemonic = false;
            dots.Paint += (s, e) => PaintDots(e.Graphics);
            Controls.Add(title); Controls.Add(hint); Controls.Add(dots); Controls.Add(message);
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i] = new ModernButton { Look = ButtonLook.Outline, TabStop = false, Font = Theme.Font(Theme.Title) };
                var digit = i == 10 ? 0 : i + 1;
                if (i == 9) { key.Visible = false; continue; }
                if (i == 11) { key.Icon = UiIcon.Left; key.Look = ButtonLook.Quiet; key.Click += (s, e) => { Remove(); Focus(); }; }
                else { key.Text = digit.ToString(); key.AccessibleName = key.Text; key.Click += (s, e) => { Add((char)('0' + digit)); Focus(); }; }
                Controls.Add(key);
            }
            extra.Look = ButtonLook.Quiet; extra.Visible = false; extra.Click += (s, e) => ExtraClicked?.Invoke(); Controls.Add(extra);
            countdown.Tick += (s, e) => RenderWait();
            Resize += (s, e) => Arrange();
            GotFocus += (s, e) => dots.Invalidate(); LostFocus += (s, e) => dots.Invalidate();
        }
        // The heading and the line under it; the extra action (Forgot your PIN?) only where one is given.
        public void SetTexts(string titleKey, string hintKey, string extraKey, string language)
        {
            this.language = language;
            title.Text = Strings.Get(titleKey, language); hint.Text = hintKey == null ? "" : Strings.Get(hintKey, language);
            extra.Visible = extraKey != null; if (extraKey != null) extra.Text = extra.AccessibleName = Strings.Get(extraKey, language);
            keys[11].AccessibleName = Strings.Get("lockBackspace", language);
            AccessibleName = title.Text; Describe();
            if (messageKey != null && !Waiting) message.Text = Strings.Get(messageKey, language);
            Arrange();
        }
        public void Clear() { digits.Clear(); Describe(); dots.Invalidate(); }
        // A line under the dots: red for a refused code, quiet for a hint; null clears it.
        public void ShowMessage(string text, bool error)
        {
            messageKey = null; message.Text = text ?? ""; message.ForeColor = error ? Theme.Danger : Theme.Muted;
            message.AccessibleName = message.Text;
        }
        public void ShowKey(string key, bool error) { ShowMessage(Strings.Get(key, language), error); messageKey = key; }
        public void SetBusy(bool value)
        {
            busy = value;
            if (busy) ShowKey("lockChecking", false);
            foreach (var key in keys) key.Enabled = !busy && !Waiting;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }
        // Too many wrong codes: the pad stays shut and counts down to the next try.
        public void Wait(long milliseconds)
        {
            waitUntil = DateTime.UtcNow.AddMilliseconds(milliseconds); Clear();
            countdown.Start(); RenderWait();
        }
        private void RenderWait()
        {
            var left = waitUntil - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                countdown.Stop(); waitUntil = DateTime.MinValue; ShowMessage("", false);
                foreach (var key in keys) key.Enabled = !busy;
                return;
            }
            foreach (var key in keys) key.Enabled = false;
            var seconds = (int)Math.Ceiling(left.TotalSeconds);
            ShowMessage(string.Format(Strings.Get("lockWait", language), (seconds / 60) + ":" + (seconds % 60).ToString("00")), true);
        }
        private void Add(char digit)
        {
            if (busy || Waiting || digits.Length >= PinPolicy.Length) return;
            if (digits.Length == 0 && message.ForeColor == Theme.Danger && messageKey != "lockLastAttempt") ShowMessage("", false);
            digits.Append(digit); Describe(); dots.Invalidate();
            if (digits.Length == PinPolicy.Length) { var pin = digits.ToString(); digits.Clear(); Completed?.Invoke(pin); dots.Invalidate(); }
        }
        private void Remove() { if (busy || Waiting || digits.Length == 0) return; digits.Length--; Describe(); dots.Invalidate(); }
        private void Describe() { AccessibleDescription = string.Format(Strings.Get("lockProgress", language), digits.Length, PinPolicy.Length); }
        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return (keyData & (Keys.Control | Keys.Alt)) == 0 && (Digit(key) >= 0 || key == Keys.Back || key == Keys.Delete) || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!e.Control && !e.Alt && !e.Shift)
            {
                var digit = Digit(e.KeyCode);
                if (digit >= 0) { Add((char)('0' + digit)); e.Handled = e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Back) { Remove(); e.Handled = e.SuppressKeyPress = true; return; }
                if (e.KeyCode == Keys.Delete) { Clear(); e.Handled = e.SuppressKeyPress = true; return; }
            }
            base.OnKeyDown(e);
        }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
        private static int Digit(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return key - Keys.D0;
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return key - Keys.NumPad0;
            return -1;
        }
        internal void TypeForQa(string text) { foreach (var c in text) Add(c); }
        private void Arrange()
        {
            var width = ClientSize.Width; var y = 0;
            title.SetBounds(0, y, width, sz(30)); y = title.Bottom + sz(2);
            hint.SetBounds(0, y, width, sz(20)); y = hint.Bottom + sz(16);
            dots.SetBounds(0, y, width, sz(24)); y = dots.Bottom + sz(10);
            message.SetBounds(0, y, width, sz(36)); y = message.Bottom + sz(6);
            var keyWidth = sz(72); var keyHeight = sz(40); var gap = sz(8);
            var left = (width - keyWidth * 3 - gap * 2) / 2;
            for (var i = 0; i < keys.Length; i++) keys[i].SetBounds(left + (i % 3) * (keyWidth + gap), y + (i / 3) * (keyHeight + gap), keyWidth, keyHeight);
            y += keyHeight * 4 + gap * 3 + sz(12);
            extra.SetBounds((width - sz(220)) / 2, y, sz(220), sz(32));
        }
        // The height the pad needs at its width: heading, dots, message line, pad and the extra action.
        public int Wanted { get { return sz(30 + 2 + 20 + 16 + 24 + 10 + 36 + 6) + sz(40) * 4 + sz(8) * 3 + sz(12) + sz(32); } }
        public int WantedWidth { get { return sz(72) * 3 + sz(8) * 2 + sz(48); } }
        private void PaintDots(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var size = sz(14); var gap = sz(14); var total = size * PinPolicy.Length + gap * (PinPolicy.Length - 1);
            var x = (dots.Width - total) / 2f; var y = (dots.Height - size) / 2f;
            var error = message.ForeColor == Theme.Danger && message.Text.Length > 0 && digits.Length == 0;
            for (var i = 0; i < PinPolicy.Length; i++, x += size + gap)
            {
                var r = new RectangleF(x, y, size, size);
                if (i < digits.Length) using (var brush = new SolidBrush(Theme.Accent)) g.FillEllipse(brush, r);
                else using (var pen = new Pen(error ? Theme.Danger : Focused && i == digits.Length ? Theme.Accent : Theme.Current.Line, sz(2) / 1.5f)) g.DrawEllipse(pen, r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) countdown.Dispose(); base.Dispose(disposing); }
    }
}
