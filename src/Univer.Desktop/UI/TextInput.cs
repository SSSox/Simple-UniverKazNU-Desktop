using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    internal sealed class TextInput : Panel
    {
        public TextBox Input { get; } = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Font(Theme.Body), BackColor = Theme.Surface, ForeColor = Theme.Ink };
        public ModernButton Reveal { get; } = new ModernButton { Icon = UiIcon.Eye, Compact = true, Size = new Size(28, 28), TabStop = true };
        // The field draws a 34 px box with room around it for its 3 px ring, so focus never moves the text.
        public const int Ring = 3;
        private float focusLevel, errorLevel;
        private Tween focusChange, errorChange;
        private bool invalid;
        public bool Invalid
        {
            get { return invalid; }
            set
            {
                if (invalid == value) return;
                invalid = value; errorChange?.Cancel(); var from = errorLevel; var to = value ? 1f : 0f;
                errorChange = Motion.Run(Kit.Moving(this) ? 200 : 0, t => { errorLevel = Motion.Lerp(from, to, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
            }
        }
        private void Focusing()
        {
            focusChange?.Cancel(); var from = focusLevel; var to = Input.Focused ? 1f : 0f;
            focusChange = Motion.Run(Kit.Moving(this) ? 200 : 0, t => { focusLevel = Motion.Lerp(from, to, Motion.EaseOut(t)); if (!IsDisposed) Invalidate(); });
        }
        private readonly CueBanner cue;
        private bool secret;
        private float zoom = 1;
        public float Zoom { get { return zoom; } set { zoom = value; Arrange(secret); Invalidate(); } }
        // The grey hint shown while the field is empty.
        public string Cue { get { return cue.Text; } set { cue.Text = value; } }
        public TextInput(bool password)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface; Height = 40; TabStop = false;
            cue = new CueBanner(Input); secret = password;
            Input.UseSystemPasswordChar = password; Input.MaxLength = password ? 1024 : 256;
            Controls.Add(Input);
            if (password)
            {
                Controls.Add(Reveal);
                Reveal.Click += (s, e) => { Input.UseSystemPasswordChar = !Input.UseSystemPasswordChar; Reveal.Icon = Input.UseSystemPasswordChar ? UiIcon.Eye : UiIcon.EyeOff; Reveal.Invalidate(); Input.Focus(); };
            }
            Input.GotFocus += (s, e) => Focusing(); Input.LostFocus += (s, e) => Focusing();
            Input.KeyDown += (s, e) => { Motion.KeyboardInput = true; var form = FindForm(); if (form != null) MotionSurface.StopTree(form); };
            Input.MouseDown += (s, e) => { Motion.KeyboardInput = false; var form = FindForm(); if (form != null) MotionSurface.StopTree(form); };
            Input.TextChanged += (s, e) => { if (Invalid) Invalid = false; };
            Resize += (s, e) => Arrange(password);
            Arrange(password);
        }
        private void Arrange(bool password)
        {
            var scale = Theme.DpiScale * zoom;
            var inset = (int)((11 + Ring) * scale); var buttonWidth = password ? (int)(30 * scale) : 0;
            Input.Bounds = new Rectangle(inset, (Height - Input.PreferredHeight) / 2, Math.Max(20, Width - inset * 2 - buttonWidth), Input.PreferredHeight);
            if (password) Reveal.Bounds = new Rectangle(Width - (int)((Ring + 3) * scale) - (int)(28 * scale), (Height - (int)(28 * scale)) / 2, (int)(28 * scale), (int)(28 * scale));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            // Outside the rounded border shows the page behind the field (Downloads puts it on the darker page colour).
            var g = e.Graphics; g.Clear(Theme.Behind(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = g.DpiX / 96f * zoom; var ring = Ring * scale; var palette = Theme.Current;
            var box = new RectangleF(ring + .5f, ring + .5f, Width - ring * 2 - 1, Height - ring * 2 - 1);
            var level = Math.Max(focusLevel, errorLevel);
            var soft = Motion.Mix(palette.AccentSoft, palette.DangerSoft, errorLevel); var edge = Motion.Mix(palette.Control, Motion.Mix(palette.AccentInk, palette.Danger, errorLevel), level);
            if (level > .01f)
                using (var halo = Theme.Rounded(RectangleF.Inflate(box, ring - .5f, ring - .5f), (Kit.RadiusMedium + Ring) * scale)) using (var fill = new SolidBrush(Color.FromArgb((int)(255 * level), soft))) g.FillPath(fill, halo);
            using (var path = Theme.Rounded(box, Kit.RadiusMedium * scale))
            {
                using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
                using (var pen = new Pen(edge)) g.DrawPath(pen, path);
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
