using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    internal sealed class TextInput : Panel
    {
        public TextBox Input { get; } = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Font(12), BackColor = Theme.Surface, ForeColor = Theme.Ink };
        public ModernButton Reveal { get; } = new ModernButton { Icon = UiIcon.Eye, Compact = true, Size = new Size(40, 40), TabStop = true };
        private bool invalid;
        public bool Invalid { get { return invalid; } set { invalid = value; Invalidate(); } }
        private readonly CueBanner cue;
        // The grey hint shown while the field is empty.
        public string Cue { get { return cue.Text; } set { cue.Text = value; } }
        public TextInput(bool password)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface; Height = 48; TabStop = false;
            cue = new CueBanner(Input);
            Input.UseSystemPasswordChar = password; Input.MaxLength = password ? 1024 : 256;
            Controls.Add(Input);
            if (password)
            {
                Controls.Add(Reveal);
                Reveal.Click += (s, e) => { Input.UseSystemPasswordChar = !Input.UseSystemPasswordChar; Reveal.Icon = Input.UseSystemPasswordChar ? UiIcon.Eye : UiIcon.EyeOff; Reveal.Invalidate(); Input.Focus(); };
            }
            Input.GotFocus += (s, e) => Invalidate(); Input.LostFocus += (s, e) => Invalidate();
            Input.KeyDown += (s, e) => { Motion.KeyboardInput = true; var form = FindForm(); if (form != null) MotionSurface.StopTree(form); };
            Input.MouseDown += (s, e) => { Motion.KeyboardInput = false; var form = FindForm(); if (form != null) MotionSurface.StopTree(form); };
            Input.TextChanged += (s, e) => { if (Invalid) Invalid = false; };
            Resize += (s, e) => Arrange(password);
            Arrange(password);
        }
        private void Arrange(bool password)
        {
            float scale; using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = graphics.DpiX / 96f;
            var inset = (int)(12 * scale); var buttonWidth = password ? (int)(42 * scale) : 0;
            Input.Bounds = new Rectangle(inset, (Height - Input.PreferredHeight) / 2, Math.Max(20, Width - inset * 2 - buttonWidth), Input.PreferredHeight);
            if (password) Reveal.Bounds = new Rectangle(Width - buttonWidth - 4, 4, buttonWidth, Height - 8);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Outside the rounded border shows the page behind the field (Downloads puts it on the darker page colour).
            var g = e.Graphics; g.Clear(Theme.Behind(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 7))
            {
                using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
                using (var pen = new Pen(Invalid ? Theme.Danger : Input.Focused ? Theme.AccentInk : Theme.Border, Input.Focused || Invalid ? 1.8f : 1)) g.DrawPath(pen, path);
            }
        }
    }
    // A text box's cue banner (EM_SETCUEBANNER). The hint lives on the native edit control, so a recreated handle (for
    // example after a DPI change) gets the current text again.
    internal sealed class CueBanner
    {
        private readonly TextBox box;
        private string text = "";
        public CueBanner(TextBox box)
        {
            this.box = box;
            box.HandleCreated += (s, e) => Apply();
        }
        public string Text { get { return text; } set { text = value ?? ""; if (box.IsHandleCreated) Apply(); } }
        private void Apply() { SendMessage(box.Handle, 0x1501, (IntPtr)1, text); }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);
    }
}
