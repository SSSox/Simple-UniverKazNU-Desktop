using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Start with Windows: Settings shows Windows' line as it is, Save writes this copy's path (and repairs another
        // copy's), and a start into the tray keeps the session behind a window that never shows. Only the in-memory key.
        private async Task AutostartQaAsync(string qa, List<string> results)
        {
            Check(RunKey is MemoryRunKey, "self-test never reads or writes the user's start-with-Windows line", results);
            var exe = AutostartExe; const string moved = @"""D:\Moved\Univer.Desktop.exe"" --tray";
            RunKey.Delete(Autostart.ValueName);
            foreach (var lang in new[] { "ru", "kz", "en" })
            {
                var shown = prefs.Clone(); shown.Language = lang;
                using (var form = new SettingsForm(null, shown))
                {
                    form.StartupState = ReadAutostart();
                    var off = !form.Startup && !form.StartupChanged && !form.SaveButton.Enabled;
                    RunKey.Write(Autostart.ValueName, moved);
                    form.StartupState = ReadAutostart();
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); form.SelectUpdatesForQa(); await Task.Delay(150);
                    var startup = form.StartupForQa;
                    Check(off && form.Startup && form.StartupChanged && form.SaveButton.Enabled && startup.Hint == Strings.Get("startupElsewhere", lang) && startup.FitsForQa && startup.Visible,
                        "start with Windows is off at first, and a line for another copy shows as on with Save ready to repair it (" + lang + ")", results);
                    SaveBitmap(form, Path.Combine(qa, "autostart-" + lang + ".png"));
                    form.Close();
                    RunKey.Delete(Autostart.ValueName);
                }
            }
            RunKey.Write(Autostart.ValueName, moved);
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, startup: true) && RunKey.Read(Autostart.ValueName) == "\"" + exe + "\" --tray" && ReadAutostart() == AutostartState.On,
                "Save writes this copy's path with the tray argument over another copy's", results);
            Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, startup: false) && RunKey.Read(Autostart.ValueName) == null && ReadAutostart() == AutostartState.Off,
                "turning it off removes the line", results);
            messages.Withdraw("settingsSaved"); RenderFeedback();
            // The start into the tray, on this window: hidden before it is painted, the page still answers, and opening the
            // window releases what the start held back (here counted, not opened).
            var previousTray = trayIcon; var previousOpacity = Opacity; var prompts = 0;
            using (var isolatedTray = new NotifyIcon { Visible = false })
            {
                trayIcon = isolatedTray; startPromptsForQa = () => { prompts++; return Task.FromResult(0); };
                try
                {
                    main.LoadHtml("<!doctype html><title>tray</title><p>tray</p>", SitePolicy.Origin + "/qa/tray");
                    for (var until = DateTime.UtcNow.AddSeconds(5); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title") != "tray";) await Task.Delay(100);
                    Opacity = 1; StartHidden = true;
                    var transparent = Opacity == 0;
                    EnterTrayAtStart(); await Task.Delay(200);
                    var answer = await AutomationController.EvaluateAsync(main, "'alive-'+(1+1)"); var alive = answer == "alive-2";
                    QaTrace("autostart-hidden " + transparent + " " + Visible + " " + startPromptsPending + " " + StartHidden + " " + answer);
                    Check(transparent && !Visible && startPromptsPending && !StartHidden && alive, "a start into the tray hides the unpainted window and keeps its page running", results);
                    RestoreFromTray(); await Task.Delay(200);
                    Check(Visible && Opacity == 1 && !startPromptsPending && prompts == 1, "opening it from the tray shows the window and releases the held-back start once", results);
                    RestoreFromTray(); await Task.Delay(100);
                    Check(prompts == 1, "opening it again releases nothing more", results);
                }
                finally { trayIcon = previousTray; startPromptsForQa = null; startPromptsPending = false; Opacity = previousOpacity; }
            }
            // Without a tray icon (self-test, the review) the argument is ignored: the window would have no way back.
            StartHidden = true;
            Check(!StartHidden && Opacity == previousOpacity, "without a tray icon the window never starts hidden", results);
        }
    }
}
