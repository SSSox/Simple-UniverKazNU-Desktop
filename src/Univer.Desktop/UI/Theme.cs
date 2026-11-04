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
        public Color Accent, AccentHover, AccentInk, Ink, Muted, Background, Surface, Border, Hover, Selected, Danger, Success, Warning, Pending,
            Disabled, DangerLine, ErrorFill, WarningFill, SuccessFill, InfoFill, LegacyFill, Skeleton, SuccessLine, SuccessBadge, SuccessTrack;
        public Color[] Kinds;   // badge colours of PDF, Word, Excel, PowerPoint and image files
        public bool Dark;
        public Color[] Roles() { return new[] { Background, Surface, Ink, Muted, Danger, ErrorFill, WarningFill, SuccessFill, InfoFill, LegacyFill }; }
    }
    internal static class Theme
    {
        public static readonly Palette Light = new Palette
        {
            Accent = Color.FromArgb(27, 95, 166), AccentHover = Color.FromArgb(21, 77, 136), AccentInk = Color.FromArgb(27, 95, 166),
            Ink = Color.FromArgb(22, 40, 59), Muted = Color.FromArgb(91, 107, 124), Background = Color.FromArgb(241, 244, 247), Surface = Color.FromArgb(255, 255, 255),
            Border = Color.FromArgb(221, 228, 236), Hover = Color.FromArgb(236, 241, 247), Selected = Color.FromArgb(230, 239, 250),
            Danger = Color.FromArgb(180, 35, 24), Success = Color.FromArgb(30, 122, 76), Warning = Color.FromArgb(176, 112, 0), Pending = Color.FromArgb(196, 132, 0),
            Disabled = Color.FromArgb(150, 162, 175), DangerLine = Color.FromArgb(236, 196, 192),
            ErrorFill = Color.FromArgb(255, 243, 242), WarningFill = Color.FromArgb(255, 249, 232), SuccessFill = Color.FromArgb(235, 248, 241), InfoFill = Color.FromArgb(235, 243, 253),
            LegacyFill = Color.FromArgb(255, 245, 218), Skeleton = Color.FromArgb(226, 232, 240),
            SuccessLine = Color.FromArgb(190, 222, 205), SuccessBadge = Color.FromArgb(226, 244, 234), SuccessTrack = Color.FromArgb(235, 244, 239),
            Kinds = new[] { Color.FromArgb(200, 52, 44), Color.FromArgb(43, 87, 154), Color.FromArgb(33, 115, 70), Color.FromArgb(196, 82, 36), Color.FromArgb(124, 77, 175) }
        };
        // Dark surfaces with light text; the accent fill keeps white text readable, and accent text is lighter.
        public static readonly Palette DarkPalette = new Palette
        {
            Dark = true,
            Accent = Color.FromArgb(44, 111, 192), AccentHover = Color.FromArgb(58, 126, 208), AccentInk = Color.FromArgb(122, 176, 238),
            Ink = Color.FromArgb(230, 235, 241), Muted = Color.FromArgb(158, 171, 185), Background = Color.FromArgb(21, 25, 30), Surface = Color.FromArgb(29, 34, 41),
            Border = Color.FromArgb(52, 61, 72), Hover = Color.FromArgb(38, 45, 54), Selected = Color.FromArgb(32, 52, 78),
            Danger = Color.FromArgb(242, 118, 107), Success = Color.FromArgb(92, 196, 143), Warning = Color.FromArgb(230, 170, 60), Pending = Color.FromArgb(226, 168, 58),
            Disabled = Color.FromArgb(98, 110, 124), DangerLine = Color.FromArgb(110, 52, 48),
            ErrorFill = Color.FromArgb(58, 32, 34), WarningFill = Color.FromArgb(56, 46, 26), SuccessFill = Color.FromArgb(27, 50, 40), InfoFill = Color.FromArgb(29, 43, 61),
            LegacyFill = Color.FromArgb(60, 50, 28), Skeleton = Color.FromArgb(44, 51, 60),
            SuccessLine = Color.FromArgb(40, 90, 64), SuccessBadge = Color.FromArgb(30, 62, 46), SuccessTrack = Color.FromArgb(36, 58, 47),
            Kinds = new[] { Color.FromArgb(240, 110, 100), Color.FromArgb(110, 160, 235), Color.FromArgb(90, 190, 130), Color.FromArgb(240, 140, 90), Color.FromArgb(180, 140, 230) }
        };
        private static Palette current = Light;
        public static Palette Current { get { return current; } }
        public static bool Dark { get { return current.Dark; } }
        public static Color Accent { get { return current.Accent; } }
        public static Color AccentHover { get { return current.AccentHover; } }
        // Accent as text, icons, focus rings and markers on the theme's surfaces.
        public static Color AccentInk { get { return current.AccentInk; } }
        public static Color Ink { get { return current.Ink; } }
        public static Color Muted { get { return current.Muted; } }
        public static Color Background { get { return current.Background; } }
        public static Color Surface { get { return current.Surface; } }
        public static Color Border { get { return current.Border; } }
        public static Color Hover { get { return current.Hover; } }
        public static Color Selected { get { return current.Selected; } }
        public static Color Danger { get { return current.Danger; } }
        public static Color Success { get { return current.Success; } }
        // One type scale for native chrome, matching the website layer at 96 DPI: 12/13/14/15/16 px.
        // Caption is only for short labels; readable secondary text starts at Meta.
        public const float Caption = 9.75f, Meta = 9.75f, Body = 10.5f, Emphasis = 11.25f, Title = 12f;
        private static readonly FontChoice fonts = LoadFonts();
        public static Font Font(float size) { return new Font(fonts.Regular, size); }
        public static Font Font(float size, Font template) { return new Font(template.FontFamily, size, template.Style); }
        public static Font Strong(float size) { return fonts.Strong != null ? new Font(fonts.Strong, size) : new Font(fonts.Regular, size, FontStyle.Bold); }
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
        // Tooltips in the theme's colours; Windows draws them light in either mode.
        public static void Tips(ToolTip tips)
        {
            tips.OwnerDraw = true;
            tips.Popup += (s, e) =>
            {
                if (tipFont == null) tipFont = Font(Meta);
                var size = TextRenderer.MeasureText(tips.GetToolTip(e.AssociatedControl), tipFont, new Size(480, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                e.ToolTipSize = new Size(size.Width + 16, size.Height + 10);
            };
            tips.Draw += (s, e) =>
            {
                if (tipFont == null) tipFont = Font(Meta);
                using (var fill = new SolidBrush(Surface)) e.Graphics.FillRectangle(fill, e.Bounds);
                using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                var text = Rectangle.Inflate(e.Bounds, -8, -5);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, tipFont, text, Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
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
            if (!(menu.Renderer is MenuRenderer)) menu.Renderer = new MenuRenderer();
            menu.BackColor = Surface; menu.ForeColor = Ink;
            foreach (ToolStripItem item in menu.Items) { item.ForeColor = Ink; item.BackColor = Surface; }
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
                if (form.Visible) SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
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
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
    // Menus drawn with MenuColors; in the dark theme the check mark and submenu arrows use the theme's ink, since the
    // system draws them black.
    internal sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { }
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
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Selected;
        public override Color MenuItemPressedGradientEnd => Theme.Selected;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color CheckBackground => Theme.Selected;
        public override Color CheckSelectedBackground => Theme.Selected;
        public override Color CheckPressedBackground => Theme.Selected;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Surface;
    }
    internal enum UiIcon { None, Home, Downloads, Settings, External, Back, Forward, Reload, Menu, Plus, Minus, Eye, EyeOff, Lock, Person, Close, Check, Info, Warning, Calendar, Book, Journal, Folder, Sort, Cap, Search, Up, Down, Bell }
    internal static class Icons
    {
        public static void Draw(Graphics g, UiIcon icon, RectangleF area, Color color)
        {
            if (icon == UiIcon.None) return;
            var state = g.Save(); g.TranslateTransform(area.X, area.Y); g.ScaleTransform(area.Width / 24f, area.Height / 24f);
            using (var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (icon)
                {
                    case UiIcon.Calendar: g.DrawRectangle(pen, 4, 5, 16, 16); g.DrawLine(pen, 4, 10, 20, 10); g.DrawLine(pen, 8, 3, 8, 7); g.DrawLine(pen, 16, 3, 16, 7); g.DrawLine(pen, 8, 14, 10, 14); g.DrawLine(pen, 14, 14, 16, 14); g.DrawLine(pen, 8, 17, 10, 17); break;
                    case UiIcon.Book: g.DrawRectangle(pen, 5, 3, 15, 18); g.DrawLine(pen, 9, 3, 9, 21); g.DrawLine(pen, 12, 8, 17, 8); g.DrawLine(pen, 12, 12, 17, 12); break;
                    case UiIcon.Cap: g.DrawPolygon(pen, new[] { new PointF(2, 9), new PointF(12, 4), new PointF(22, 9), new PointF(12, 14) }); g.DrawLines(pen, new[] { new PointF(6, 11), new PointF(6, 16), new PointF(12, 19), new PointF(18, 16), new PointF(18, 11) }); g.DrawLine(pen, 22, 9, 22, 15); break;
                    case UiIcon.Journal: g.DrawRectangle(pen, 4, 3, 16, 18); g.DrawLines(pen, new[] {new PointF(7, 10),new PointF(10, 13),new PointF(17, 7)}); g.DrawLine(pen, 8, 17, 16, 17); break;
                    case UiIcon.Home: g.DrawLines(pen, new[] { new PointF(3, 11), new PointF(12, 3), new PointF(21, 11) }); g.DrawLines(pen, new[] { new PointF(5, 10), new PointF(5, 21), new PointF(10, 21), new PointF(10, 15), new PointF(14, 15), new PointF(14, 21), new PointF(19, 21), new PointF(19, 10) }); break;
                    case UiIcon.Downloads: g.DrawLine(pen, 12, 3, 12, 15); g.DrawLines(pen, new[] { new PointF(7, 11), new PointF(12, 16), new PointF(17, 11) }); g.DrawLines(pen, new[] { new PointF(4, 16), new PointF(4, 21), new PointF(20, 21), new PointF(20, 16) }); break;
                    case UiIcon.Folder: g.DrawLines(pen, new[] { new PointF(3, 19), new PointF(3, 5), new PointF(9, 5), new PointF(11, 8), new PointF(21, 8), new PointF(21, 19), new PointF(3, 19) }); g.DrawLine(pen, 3, 11, 21, 11); break;
                    case UiIcon.Sort: g.DrawLine(pen, 8, 4, 8, 20); g.DrawLines(pen, new[] { new PointF(4, 16), new PointF(8, 20), new PointF(12, 16) }); g.DrawLine(pen, 16, 20, 16, 4); g.DrawLines(pen, new[] { new PointF(12, 8), new PointF(16, 4), new PointF(20, 8) }); break;
                    case UiIcon.Back: g.DrawLines(pen, new[] { new PointF(11, 5), new PointF(4, 12), new PointF(11, 19) }); g.DrawLine(pen, 4, 12, 21, 12); break;
                    case UiIcon.Forward: g.DrawLines(pen, new[] { new PointF(13, 5), new PointF(20, 12), new PointF(13, 19) }); g.DrawLine(pen, 3, 12, 20, 12); break;
                    case UiIcon.Reload: g.DrawArc(pen, 4, 4, 16, 16, 40, 290); g.DrawLines(pen, new[] { new PointF(20, 3), new PointF(20, 9), new PointF(14, 9) }); break;
                    case UiIcon.Menu: g.DrawLine(pen, 4, 6, 20, 6); g.DrawLine(pen, 4, 12, 20, 12); g.DrawLine(pen, 4, 18, 20, 18); break;
                    case UiIcon.Plus: g.DrawLine(pen, 5, 12, 19, 12); g.DrawLine(pen, 12, 5, 12, 19); break;
                    case UiIcon.Minus: g.DrawLine(pen, 5, 12, 19, 12); break;
                    case UiIcon.Close: g.DrawLine(pen, 6, 6, 18, 18); g.DrawLine(pen, 18, 6, 6, 18); break;
                    case UiIcon.Search: g.DrawEllipse(pen, 4, 4, 12, 12); g.DrawLine(pen, 14.5f, 14.5f, 20, 20); break;
                    case UiIcon.Up: g.DrawLines(pen, new[] { new PointF(6, 15), new PointF(12, 9), new PointF(18, 15) }); break;
                    case UiIcon.Down: g.DrawLines(pen, new[] { new PointF(6, 9), new PointF(12, 15), new PointF(18, 9) }); break;
                    case UiIcon.External: g.DrawLines(pen, new[] { new PointF(12, 5), new PointF(4, 5), new PointF(4, 20), new PointF(19, 20), new PointF(19, 12) }); g.DrawLine(pen, 11, 13, 21, 3); g.DrawLines(pen, new[] { new PointF(15, 3), new PointF(21, 3), new PointF(21, 9) }); break;
                    case UiIcon.Person: g.DrawEllipse(pen, 8, 3, 8, 8); g.DrawArc(pen, 4, 13, 16, 16, 180, 180); g.DrawLine(pen, 4, 21, 20, 21); break;
                    case UiIcon.Settings: g.DrawEllipse(pen, 5, 5, 14, 14); g.DrawEllipse(pen, 9, 9, 6, 6); for (var i = 0; i < 8; i++) { var a = i * Math.PI / 4; g.DrawLine(pen, (float)(12 + 7 * Math.Cos(a)), (float)(12 + 7 * Math.Sin(a)), (float)(12 + 10 * Math.Cos(a)), (float)(12 + 10 * Math.Sin(a))); } break;
                    case UiIcon.Lock: g.DrawArc(pen, 7, 2, 10, 12, 180, 180); g.DrawRectangle(pen, 5, 9, 14, 12); g.DrawLine(pen, 12, 14, 12, 17); break;
                    case UiIcon.Check: g.DrawLines(pen, new[] { new PointF(4, 12), new PointF(10, 18), new PointF(20, 6) }); break;
                    case UiIcon.Eye:
                    case UiIcon.EyeOff: g.DrawBezier(pen, 2, 12, 7, 3, 17, 3, 22, 12); g.DrawBezier(pen, 2, 12, 7, 21, 17, 21, 22, 12); g.DrawEllipse(pen, 9, 9, 6, 6); if (icon == UiIcon.EyeOff) g.DrawLine(pen, 3, 3, 21, 21); break;
                    case UiIcon.Warning: g.DrawPolygon(pen, new[] { new PointF(12, 3), new PointF(22, 21), new PointF(2, 21) }); g.DrawLine(pen, 12, 9, 12, 14); g.DrawEllipse(pen, 11.6f, 17, .8f, .8f); break;
                    case UiIcon.Bell: g.DrawBezier(pen, 6, 17, 6.5f, 14, 6, 6, 12, 5); g.DrawBezier(pen, 18, 17, 17.5f, 14, 18, 6, 12, 5); g.DrawLine(pen, 4.5f, 17, 19.5f, 17); g.DrawLine(pen, 12, 3, 12, 5); g.DrawArc(pen, 9.5f, 17.5f, 5, 4, 0, 180); break;
                    case UiIcon.Info: g.DrawEllipse(pen, 3, 3, 18, 18); g.DrawLine(pen, 12, 11, 12, 17); g.DrawEllipse(pen, 11.6f, 6.5f, .8f, .8f); break;
                }
            }
            g.Restore(state);
        }
    }
}
