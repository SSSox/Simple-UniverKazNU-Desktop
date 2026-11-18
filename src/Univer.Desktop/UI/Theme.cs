using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // The colours of one theme. Roles a control keeps in its BackColor or ForeColor are distinct within a palette, so
    // switching themes can recolour an open window by matching each control's colour to its role.
    internal sealed class Palette
    {
        public Color Accent, AccentHover, AccentInk, OnAccent, Ink, Muted, Background, Surface, Border, Hover, Selected, Danger, Success, Warning, Pending,
            Disabled, DangerLine, ErrorFill, WarningFill, SuccessFill, InfoFill, LegacyFill, Skeleton, SuccessLine, SuccessBadge, SuccessTrack,
            Raised, Line, Track, Control, AccentSoft, DangerSoft, ToastFill, ToastHover, ToastInk, ToastMuted;
        public Color[] Kinds;   // badge colours of PDF, Word, Excel, PowerPoint and image files
        public bool Dark;
        public Color[] Roles() { return new[] { Background, Surface, Ink, Muted, Danger, ErrorFill, WarningFill, SuccessFill, InfoFill, LegacyFill }; }
    }
    internal static class Theme
    {
        public static readonly Palette Light = new Palette
        {
            Accent = Color.FromArgb(27, 95, 166), AccentHover = Color.FromArgb(21, 77, 136), AccentInk = Color.FromArgb(27, 95, 166), OnAccent = Color.White,
            Ink = Color.FromArgb(22, 40, 59), Muted = Color.FromArgb(91, 107, 124), Background = Color.FromArgb(241, 244, 247), Surface = Color.FromArgb(255, 255, 255),
            Border = Color.FromArgb(221, 228, 236), Hover = Color.FromArgb(236, 241, 247), Selected = Color.FromArgb(230, 239, 250),
            Danger = Color.FromArgb(180, 35, 24), Success = Color.FromArgb(30, 122, 76), Warning = Color.FromArgb(176, 112, 0), Pending = Color.FromArgb(196, 132, 0),
            Disabled = Color.FromArgb(150, 162, 175), DangerLine = Color.FromArgb(236, 196, 192),
            ErrorFill = Color.FromArgb(255, 243, 242), WarningFill = Color.FromArgb(255, 249, 232), SuccessFill = Color.FromArgb(235, 248, 241), InfoFill = Color.FromArgb(235, 243, 253),
            LegacyFill = Color.FromArgb(255, 245, 218), Skeleton = Color.FromArgb(226, 232, 240),
            SuccessLine = Color.FromArgb(190, 222, 205), SuccessBadge = Color.FromArgb(226, 244, 234), SuccessTrack = Color.FromArgb(235, 244, 239),
            Raised = Color.White, Line = Color.FromArgb(230, 235, 241), Track = Color.FromArgb(233, 238, 244), Control = Color.FromArgb(205, 214, 224),
            AccentSoft = Color.FromArgb(211, 226, 245), DangerSoft = Color.FromArgb(250, 224, 221),
            ToastFill = Color.FromArgb(24, 33, 46), ToastHover = Color.FromArgb(44, 56, 72), ToastInk = Color.White, ToastMuted = Color.FromArgb(190, 200, 212),
            Kinds = new[] { Color.FromArgb(200, 52, 44), Color.FromArgb(43, 87, 154), Color.FromArgb(33, 115, 70), Color.FromArgb(196, 82, 36), Color.FromArgb(124, 77, 175) }
        };
        // Dark surfaces with light text; the accent fill keeps white text readable, and accent text is lighter.
        public static readonly Palette DarkPalette = new Palette
        {
            Dark = true,
            Accent = Color.FromArgb(44, 111, 192), AccentHover = Color.FromArgb(58, 126, 208), AccentInk = Color.FromArgb(122, 176, 238), OnAccent = Color.White,
            Ink = Color.FromArgb(230, 235, 241), Muted = Color.FromArgb(158, 171, 185), Background = Color.FromArgb(21, 25, 30), Surface = Color.FromArgb(29, 34, 41),
            Border = Color.FromArgb(52, 61, 72), Hover = Color.FromArgb(38, 45, 54), Selected = Color.FromArgb(32, 52, 78),
            Danger = Color.FromArgb(242, 118, 107), Success = Color.FromArgb(92, 196, 143), Warning = Color.FromArgb(230, 170, 60), Pending = Color.FromArgb(226, 168, 58),
            Disabled = Color.FromArgb(98, 110, 124), DangerLine = Color.FromArgb(110, 52, 48),
            ErrorFill = Color.FromArgb(58, 32, 34), WarningFill = Color.FromArgb(56, 46, 26), SuccessFill = Color.FromArgb(27, 50, 40), InfoFill = Color.FromArgb(29, 43, 61),
            LegacyFill = Color.FromArgb(60, 50, 28), Skeleton = Color.FromArgb(44, 51, 60),
            SuccessLine = Color.FromArgb(40, 90, 64), SuccessBadge = Color.FromArgb(30, 62, 46), SuccessTrack = Color.FromArgb(36, 58, 47),
            Raised = Color.FromArgb(44, 52, 62), Line = Color.FromArgb(44, 52, 62), Track = Color.FromArgb(38, 45, 54), Control = Color.FromArgb(66, 76, 88),
            AccentSoft = Color.FromArgb(36, 60, 92), DangerSoft = Color.FromArgb(82, 42, 42),
            ToastFill = Color.FromArgb(50, 59, 70), ToastHover = Color.FromArgb(66, 76, 88), ToastInk = Color.FromArgb(236, 240, 245), ToastMuted = Color.FromArgb(176, 187, 200),
            Kinds = new[] { Color.FromArgb(240, 110, 100), Color.FromArgb(110, 160, 235), Color.FromArgb(90, 190, 130), Color.FromArgb(240, 140, 90), Color.FromArgb(180, 140, 230) }
        };
        private static Palette current = Light;
        public static Palette Current { get { return current; } }
        public static bool Dark { get { return current.Dark; } }
        public static Color Accent { get { return current.Accent; } }
        public static Color AccentHover { get { return current.AccentHover; } }
        // Accent as text, icons, focus rings and markers on the theme's surfaces.
        public static Color AccentInk { get { return current.AccentInk; } }
        // Text and icons on an accent fill.
        public static Color OnAccent { get { return current.OnAccent; } }
        public static Color Ink { get { return current.Ink; } }
        public static Color Muted { get { return current.Muted; } }
        public static Color Background { get { return current.Background; } }
        public static Color Surface { get { return current.Surface; } }
        public static Color Border { get { return current.Border; } }
        public static Color Hover { get { return current.Hover; } }
        public static Color Selected { get { return current.Selected; } }
        public static Color Danger { get { return current.Danger; } }
        public static Color Success { get { return current.Success; } }
        private static float dpiScale;
        // Device pixels per design pixel. GDI reports one DPI for the whole process, so it is read once, after Chromium started.
        public static float DpiScale { get { if (dpiScale <= 0) using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = graphics.DpiX / 96f; return dpiScale; } }
        // The kit's type scale at 96 DPI: 11, 12, 13, 14 and 15 px, one family, hierarchy from one weight step.
        // Caption is only for short labels; readable secondary text starts at Meta.
        public const float Caption = 8.25f, Meta = 9f, Body = 9.75f, Emphasis = 10.5f, Title = 11.25f;
        // Enumerating the installed families takes ~25 ms; Program starts it on a worker while Chromium starts.
        private static readonly Lazy<FontChoice> fonts = new Lazy<FontChoice>(LoadFonts);
        public static void LoadFontsEarly() { var unused = fonts.Value; }
        public static Font Font(float size) { return new Font(fonts.Value.Regular, size); }
        public static Font Font(float size, Font template) { return new Font(template.FontFamily, size, template.Style); }
        public static Font Strong(float size) { var choice = fonts.Value; return choice.Strong != null ? new Font(choice.Strong, size) : new Font(choice.Regular, size, FontStyle.Bold); }
        // Titles: the same family one weight up.
        public static Font Display(float size) { return Strong(size); }
        private static FontChoice LoadFonts()
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var substitutes = new List<string>();
            try { using (var collection = new InstalledFontCollection()) foreach (var family in collection.Families) installed.Add(family.Name); }
            catch (Exception) { installed.Add("Segoe UI"); }
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes"))
                    if (key != null) substitutes.AddRange(key.GetValueNames());
            }
            catch (Exception) { /* Without registry access GDI still has the installed families. */ }
            return FontChoice.Pick(installed, name => FontChoice.IsSubstituted(substitutes, name));
        }
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        // The colour showing behind a rounded control: its corners take it, not the control's own fill.
        public static Color Behind(Control control)
        {
            for (var parent = control.Parent; parent != null; parent = parent.Parent) if (parent.BackColor.A == 255) return parent.BackColor;
            return control.BackColor.A == 255 ? control.BackColor : Background;
        }
        private static Font tipFont;
        private const string ShortcutMark = " · ";
        // "Saved files · Ctrl+J": the words and the shortcut that follows them.
        private static void SplitTip(string text, out string words, out string keys)
        {
            var at = text.LastIndexOf(ShortcutMark, StringComparison.Ordinal);
            keys = at < 0 ? "" : text.Substring(at + ShortcutMark.Length);
            if (keys.Length > 12 || keys.IndexOf(' ') >= 0 || (keys.IndexOf('+') < 0 && !(keys.Length <= 3 && keys.StartsWith("F", StringComparison.Ordinal)))) keys = "";
            words = keys.Length == 0 ? text : text.Substring(0, at);
        }
        // The kit's tooltip: a small dark label. A shortcut goes on keycaps at its end.
        public static void Tips(ToolTip tips)
        {
            tips.OwnerDraw = true;
            tips.Popup += (s, e) =>
            {
                if (tipFont == null) tipFont = Font(Caption);
                string words, keys; SplitTip(tips.GetToolTip(e.AssociatedControl), out words, out keys);
                var size = TextRenderer.MeasureText(words, tipFont, new Size(420, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                var width = size.Width + (keys.Length > 0 ? 8 + Kit.Keys(null, keys, tipFont, Point.Empty, Color.Empty, Color.Empty, Color.Empty, 1) : 0);
                e.ToolTipSize = new Size(width + 16, Math.Max(22, size.Height + 8));
            };
            tips.Draw += (s, e) =>
            {
                if (tipFont == null) tipFont = Font(Caption);
                string words, keys; SplitTip(e.ToolTipText, out words, out keys);
                var palette = Current;
                using (var fill = new SolidBrush(palette.ToastFill)) e.Graphics.FillRectangle(fill, e.Bounds);
                var text = Rectangle.Inflate(e.Bounds, -8, -4);
                TextRenderer.DrawText(e.Graphics, words, tipFont, text, palette.ToastInk, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                if (keys.Length > 0)
                {
                    var width = Kit.Keys(null, keys, tipFont, Point.Empty, Color.Empty, Color.Empty, Color.Empty, 1);
                    Kit.Keys(e.Graphics, keys, tipFont, new Point(e.Bounds.Right - 8 - width, e.Bounds.Y + (e.Bounds.Height - 18) / 2), palette.ToastInk, palette.ToastHover, palette.ToastHover, 1);
                }
            };
        }
        // Switches the palette; the caller recolours open windows with Recolor. Returns false when nothing changed.
        public static bool Set(bool dark)
        {
            if (dark == current.Dark) return false;
            current = dark ? DarkPalette : Light;
            return true;
        }
        // Windows' "Choose your default app mode": dark when AppsUseLightTheme is 0. Windows 7 and 8 have no such
        // setting and stay light.
        public static bool SystemDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception) { return false; }
        }
        // Recolours a window built in the other theme: every control colour that names a role of the previous palette
        // takes that role's colour in the current one; painted controls read the palette when they draw.
        public static void Recolor(Control root)
        {
            var from = current.Dark ? Light.Roles() : DarkPalette.Roles(); var to = current.Roles();
            var map = new Dictionary<int, Color>();
            for (var i = 0; i < from.Length; i++) if (!map.ContainsKey(from[i].ToArgb())) map[from[i].ToArgb()] = to[i];
            Recolor(root, map);
            var form = root as Form;
            if (form != null) TitleBar(form);
            root.Invalidate(true);
        }
        private static void Recolor(Control control, Dictionary<int, Color> map)
        {
            Color color;
            if (control.BackColor.A == 255 && map.TryGetValue(control.BackColor.ToArgb(), out color)) control.BackColor = color;
            if (map.TryGetValue(control.ForeColor.ToArgb(), out color)) control.ForeColor = color;
            var strip = control.ContextMenuStrip; if (strip != null) Style(strip);
            Scrollbars(control);
            foreach (Control child in control.Controls) Recolor(child, map);
        }
        // Menus are drawn by MenuColors; their items take the menu's text colour.
        public static void Style(ToolStrip menu)
        {
            if (!(menu.Renderer is MenuRenderer)) { menu.Renderer = new MenuRenderer(); menu.HandleCreated += (s, e) => RoundPopup(menu.Handle); if (menu.IsHandleCreated) RoundPopup(menu.Handle); }
            menu.Padding = new Padding(4, 4, 4, 4); menu.BackColor = Current.Raised; menu.ForeColor = Ink;
            foreach (ToolStripItem item in menu.Items) { item.ForeColor = Ink; item.BackColor = Current.Raised; item.Padding = new Padding(0, 2, 0, 2); }
        }
        // Popups take the system's small rounded corners and shadow where Windows 11 offers them.
        private static void RoundPopup(IntPtr window)
        {
            try { var small = 3; DwmSetWindowAttribute(window, 33, ref small, 4); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        // A window built in the current theme: its frame and the scroll bars of its panels follow the palette too.
        public static void Window(Form form)
        {
            form.HandleCreated += (s, e) => TitleBar(form);
            if (form.IsHandleCreated) TitleBar(form);
            form.Shown += (s, e) => ScrollbarsIn(form);
        }
        private static void ScrollbarsIn(Control control) { Scrollbars(control); foreach (Control child in control.Controls) ScrollbarsIn(child); }
        // The window frame follows the theme (Windows 10 1809 and later; older Windows keeps its frame).
        public static void TitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                var value = current.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref value, 4);
                // A shown frame repaints with the new colour only after a frame change; a new one paints it when shown.
                if (form.Visible) Native.SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        // Windows-drawn parts in the theme's colours: scroll bars of scrolling panels, check boxes and the closed drop-down
        // list (Windows 10 1809 and later draw them dark; elsewhere they keep the light look, with the theme's text colour).
        public static void Scrollbars(Control control)
        {
            var panel = control as ScrollableControl;
            var native = panel != null && panel.AutoScroll ? "DarkMode_Explorer" : control is CheckBox ? "DarkMode_Explorer" : control is ComboBox ? "DarkMode_CFD" : null;
            if (native == null) return;
            if (control is ComboBox) { control.BackColor = Surface; control.ForeColor = Ink; }
            if (!control.IsHandleCreated) return;
            try { SetWindowTheme(control.Handle, current.Dark ? native : null, null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string application, string list);
    }
    // Menus drawn with MenuColors; in the dark theme the check mark and submenu arrows use the theme's ink, since the
    // system draws them black.
    internal sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        // Hover fills are inset and rounded, as the kit's menu rows are.
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = e.Graphics.DpiX / 96f; var area = new RectangleF(2 * scale, 0, e.Item.Width - 4 * scale, e.Item.Height);
            using (var path = Theme.Rounded(area, Kit.RadiusSmall * scale)) using (var fill = new SolidBrush(Theme.Hover)) e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Current.Control)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (!Theme.Dark) { base.OnRenderItemCheck(e); return; }
            var area = e.ImageRectangle;
            if (area.Width <= 0 || area.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(area.X - 1, area.Y - 1, area.Width + 2, area.Height + 2), 4)) using (var fill = new SolidBrush(Theme.Selected)) e.Graphics.FillPath(fill, path);
            Icons.Draw(e.Graphics, UiIcon.Check, area, Theme.AccentInk);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { if (Theme.Dark) e.ArrowColor = Theme.Ink; base.OnRenderArrow(e); }
    }
    internal sealed class MenuColors : System.Windows.Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Current.Raised;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Selected;
        public override Color MenuItemPressedGradientEnd => Theme.Selected;
        public override Color ImageMarginGradientBegin => Theme.Current.Raised;
        public override Color ImageMarginGradientMiddle => Theme.Current.Raised;
        public override Color ImageMarginGradientEnd => Theme.Current.Raised;
        public override Color CheckBackground => Theme.Selected;
        public override Color CheckSelectedBackground => Theme.Selected;
        public override Color CheckPressedBackground => Theme.Selected;
        public override Color SeparatorDark => Theme.Current.Line;
        public override Color SeparatorLight => Theme.Current.Raised;
    }
    internal enum UiIcon { None, Award, Laptop, Home, Downloads, Settings, External, Back, Forward, Reload, Menu, Plus, Minus, Eye, EyeOff, Lock, Person, Close, Check, Info, Warning, Calendar, Book, Journal, Folder, Sort, Cap, Search, Up, Down, Bell, File, Left, Right, Present }
    internal static class Icons
    {
        // One stroke for the whole shell: 1.9 grid units is about 1.4 px at the usual 18 px box.
        public const float Stroke = 1.9f;
        public static void Draw(Graphics g, UiIcon icon, RectangleF area, Color color)
        {
            if (icon == UiIcon.None) return;
            var path = LucideIcons.Get(icon); if (path == null) return;
            var state = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias; g.TranslateTransform(area.X, area.Y); g.ScaleTransform(area.Width / 24f, area.Height / 24f);
            using (var pen = new Pen(color, Stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
            g.Restore(state);
        }
    }
}
