using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // What the self-test reads and presses in the settings window.
    internal sealed partial class SettingsForm
    {
        internal ModernButton SaveButton { get { return save; } }
        internal ModernButton CancelForQa { get { return cancel; } }
        internal Control TabsForQa { get { return accountTab.Parent; } }
        internal Control ViewSheetForQa { get { return viewSheet; } }
        internal int UnselectedTabCentreForQa { get { var tab = accountTab.Selected ? viewTab : accountTab; return tab.Left + tab.Width / 2; } }
        internal StageView StageForQa { get { return stage; } }
        internal TextInput LoginInput { get { return login; } }
        internal TextInput PasswordInput { get { return password; } }
        internal void SelectAppearanceForQa() { ShowTab(true); }
        internal void SelectUpdatesForQa() { ShowPanel(appPanel); }
        internal void ChangeForQa() { animations.Checked = !animations.Checked; }
        internal ModernButton WebDataClearForQa { get { return webDataClear; } }
        internal string WebDataHintForQa { get { return webDataHint.Text; } }
        internal string SecurityHintForQa { get { return securityHint.Text; } }
        internal ModernButton LanguageForQa(int index) { return languages[index]; }
        internal ModernButton DarkThemeForQa { get { return themes[2]; } }
        // A key by the road a real one takes: the focused option, its panels, then the form.
        internal void PressForQa(ModernButton focused, Keys key)
        {
            ActiveControl = focused;
            var message = Message.Create(focused.Handle, 0x0100, (System.IntPtr)(int)key, System.IntPtr.Zero);
            focused.PreProcessMessage(ref message);
        }
        // On a scaled display AutoScale multiplies the widths measured before load; at 96 DPI the self-test does it by hand.
        internal void ScaleWidthsForQa(float factor) { foreach (var control in new Control[] { accountTab, viewTab, appTab, save, cancel, updateCheck }) control.Width = (int)(control.Width * factor); }
        internal bool HugsFitForQa { get { return new[] { save, cancel, updateCheck }.All(button => button.Width == button.HugWidth); } }
        internal bool TabsFitForQa { get { return new[] { accountTab, viewTab, appTab }.All(tab => tab.Width == TextRenderer.MeasureText(tab.Text, tab.Font).Width + (int)Px(4)); } }
        internal bool UpdateButtonsWholeForQa { get { return updateCheck.Left >= 0 && updateCheck.Right <= updateSlot.Width && (updates.Available == null || updateInstall.Right <= updateSlot.Width); } }
        internal string UpdateStatusForQa { get { return updateStatus.Text; } }
        internal ModernButton UpdateCheckForQa { get { return updateCheck; } }
        internal ModernButton UpdateInstallForQa { get { return updateInstall; } }
        internal bool UpdateFitsForQa
        {
            get
            {
                var shown = content.RectangleToClient(updateInstall.RectangleToScreen(updateInstall.ClientRectangle));
                var status = TextRenderer.MeasureText(updateStatus.Text, updateStatus.Font, new Size(updateStatus.Width, int.MaxValue), TextFormatFlags.WordBreak);
                return shown.Bottom <= content.ClientSize.Height && status.Height <= updateStatus.Height;
            }
        }
        internal bool AboutFitsForQa()
        {
            var shown = content.RectangleToClient(about.RectangleToScreen(about.ClientRectangle));
            var text = TextRenderer.MeasureText(about.Text, about.Font, new Size(about.Width, int.MaxValue), TextFormatFlags.WordBreak);
            return about.Visible && shown.Top >= 0 && shown.Bottom <= content.ClientSize.Height && text.Height <= about.Height && about.Text.Contains(AppInfo.Version + " (" + AppInfo.Flavor + ", Chromium ");
        }
        // Every name and short line keeps to one line of its row, in each language.
        internal string[] OverflowForQa()
        {
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var labels = new[] { logoutTitle, logoutHint, forgetTitle, forgetHint, languageLabel, languageHint, themeLabel, privacy };
            var words = labels.Where(l => TextRenderer.MeasureText(l.Text, l.Font, Size.Empty, flags).Width > l.Width).Select(l => l.Text);
            var buttons = languages.Concat(themes).Where(b => TextRenderer.MeasureText(b.Text, b.Font, Size.Empty, flags).Width > b.Width - Px(8)).Select(b => b.Text);
            var toggles = new[] { readable, tray, animations }.Where(t => !t.FitsForQa).Select(t => t.Text);
            return words.Concat(buttons).Concat(toggles).ToArray();
        }
    }
}
