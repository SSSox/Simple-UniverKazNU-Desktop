#if LEGACY_ENGINE
using System.Drawing;
using System.Windows.Forms;

namespace Univer.Desktop.UI
{
    // Shared copy and appearance for the permanent settings notice and startup dialog.
    internal sealed class LegacyWarningPanel : Panel
    {
        private readonly bool detailed;
        private readonly Label heading = new Label(), body = new Label();
        public LegacyWarningPanel(bool detailed = false)
        {
            this.detailed = detailed;
            BackColor = Theme.Current.LegacyFill; ForeColor = Theme.Ink;
            heading.Font = Theme.Strong(detailed ? 17 : 10);
            heading.Height = detailed ? 40 : 26; heading.Dock = DockStyle.Top;
            heading.UseMnemonic = body.UseMnemonic = false;
            body.Font = Theme.Font(detailed ? 10.5f : 9.5f); body.Dock = DockStyle.Fill;
            Controls.Add(body); Controls.Add(heading);
            SetLanguage("ru");
        }
        public void SetLanguage(string language)
        {
            heading.Text = Strings.Get("legacyWarningTitle", language);
            body.Text = Strings.Get(detailed ? "legacyWarningDetail" : "legacyBrowser", language);
            AccessibleName = heading.Text; AccessibleDescription = body.Text;
        }
    }
}
#endif
