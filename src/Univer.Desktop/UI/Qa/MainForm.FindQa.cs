using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // One find round through the real shortcuts: Ctrl+F opens the bar for the expected browser, typing counts the
        // matches, F3 moves to the next one and Escape closes the bar while the page or file stays open.
        private async Task FindRoundQaAsync(string what, string needle, int minimum, int exact, List<string> results)
        {
            var key = new Message(); ProcessCmdKey(ref key, Keys.Control | Keys.F);
            var expected = DownloadsVisible ? downloadsView.FindTarget : Active;
            Check(findBar.Searching && findBar.Target == expected && find.Selected && findBar.Bounds.Top >= canvas.Top, "Ctrl+F opens find for the " + what, results);
            findBar.InputForQa.Text = needle;
            var total = await FindCountQaAsync(0);
            Check(total >= minimum && (exact < 0 || total == exact), "find counts the matches in the " + what + " (" + total + ")", results);
            if (total >= 2)
            {
                var next = new Message(); ProcessCmdKey(ref next, Keys.F3);
                var moved = await FindCountQaAsync(2);
                QaTrace("find-next " + what + " " + findBar.CountForQa);
                Check(moved >= 2 && Regex.IsMatch(findBar.CountForQa, @"^2\D"), "F3 moves to the next match in the " + what, results);
            }
            findBar.InputForQa.Text = needle + "qqzz";
            var none = await FindCountQaAsync(-1);
            Check(none == 0 && findBar.CountForQa == Strings.Get("findNone", prefs.Language), "find reports no matches in the " + what, results);
            findBar.InputForQa.Text = needle; await FindCountQaAsync(0);
            var escape = new Message(); ProcessCmdKey(ref escape, Keys.Escape);
            Check(!findBar.Searching && !find.Selected && (DownloadsVisible ? downloadsView.Previewing : Active == expected), "Escape closes find first and keeps the " + what + " open", results);
        }
        // Waits for Chromium's final count ("n of m" or "no matches"); ordinal > 0 waits for that active match.
        private async Task<int> FindCountQaAsync(int ordinal)
        {
            var until = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(100);
                var text = findBar.CountForQa;
                if (ordinal < 0 && text == Strings.Get("findNone", prefs.Language)) return 0;
                var match = Regex.Match(text, @"^(\d+)\D+(\d+)$");
                if (match.Success && ordinal >= 0 && (ordinal == 0 || int.Parse(match.Groups[1].Value) == ordinal)) return int.Parse(match.Groups[2].Value);
            }
            return -1;
        }
        // Ctrl+F pressed inside Chromium's PDF viewer (its own frame and, in Chromium 109, a guest that can bypass
        // OnPreKeyEvent) must still reach the app and open find for the shown file.
        private async Task PdfFindKeyQaAsync(CefSharp.WinForms.ChromiumWebBrowser browser, List<string> results)
        {
            var host = browser.GetBrowser().GetHost(); host.SetFocus(true); await Task.Delay(300);
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.RawKeyDown, WindowsKeyCode = (int)Keys.F, Modifiers = CefEventFlags.ControlDown });
            host.SendKeyEvent(new KeyEvent { Type = KeyEventType.KeyUp, WindowsKeyCode = (int)Keys.F, Modifiers = CefEventFlags.ControlDown });
            for (var until = DateTime.UtcNow.AddSeconds(3); !findBar.Searching && DateTime.UtcNow < until;) await Task.Delay(50);
            Check(findBar.Searching && findBar.Target == browser, "Ctrl+F pressed inside the PDF viewer opens find for the file", results);
            var escape = new Message(); ProcessCmdKey(ref escape, Keys.Escape);
        }
        private async Task SiteFindQaAsync(string qa, List<string> results)
        {
            findBar.Trace = line => QaTrace("find-result " + line);
            main.LoadHtml("<h1>Часто задаваемые вопросы</h1><p>Как получить справку QA?</p><p>Справка QA выдаётся в ЦОС.</p><p>Срок справки QA — три дня.</p>", SitePolicy.Origin + "/my/faq/");
            var until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until && (main.IsLoading || await Web.AutomationController.EvaluateAsync(main, "document.body&&document.body.textContent.includes('три дня')?'ok':''") != "ok")) await Task.Delay(100);
            // The reading layout rebuilds the FAQ after load; a DOM change drops Chromium's active match, so search the settled page.
            await WaitQaPresentationAsync(); await Task.Delay(300);
            Check(find.Visible && !find.Bounds.IntersectsWith(mode.Bounds) && (!pageTitle.Visible || !pageTitle.Bounds.IntersectsWith(find.Bounds)) && find.AccessibleName == Strings.Get("findOnPage", prefs.Language),
                "toolbar find button sits beside the view menu with its name", results);
            var open = new Message(); ProcessCmdKey(ref open, Keys.Control | Keys.F);
            findBar.InputForQa.Text = "справк"; await FindCountQaAsync(0);
            SaveBitmap(this, Path.Combine(qa, "find-page.png"));
            var close = new Message(); ProcessCmdKey(ref close, Keys.Escape);
            await FindRoundQaAsync("website page", "QA", 3, 3, results);
            ProcessCmdKey(ref open, Keys.Control | Keys.F); findBar.InputForQa.Text = "QA";
            main.LoadHtml("<p>Документы</p>", SitePolicy.Origin + "/my/docs/"); until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until && findBar.Searching) await Task.Delay(50);
            Check(!findBar.Searching, "navigation closes find for the page that went away", results);
        }
    }
}