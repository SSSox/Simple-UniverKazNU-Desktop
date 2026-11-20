using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // The diagnostics file: the settings row in each language, its button writes the file (to a QA folder, Explorer kept
        // closed) and says so, and the file names the app and its state but nothing personal planted in the events.
        private async Task DiagnosticsQaAsync(string qa, List<string> results)
        {
            var folder = Path.Combine(qa, "diagnostics"); Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder)) File.Delete(old);
            string shown = null;
            diagnosticsFolderForQa = folder; showDiagnosticsForQa = path => shown = path;
            const string mail = "qa.student@example.kz", iin = "870112345678", phone = "+7 (701) 123-45-67";
            NoteEvent("status qa " + mail + " " + iin + " " + phone + " https://univer.kaznu.kz/student/attentionStudents/12345/?token=qa-secret-token#x " +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") + " " + Environment.MachineName);
            try
            {
                foreach (var lang in new[] { "ru", "kz", "en" })
                {
                    var settings = prefs.Clone(); settings.Language = lang;
                    using (var form = new SettingsForm(null, settings))
                    {
                        form.SaveDiagnostics = SaveDiagnosticsAsync;
                        form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); form.SelectUpdatesForQa(); await Task.Delay(150);
                        var button = form.DiagnosticsForQa;
                        QaTrace("diagnostics-" + lang + " " + form.DiagnosticsFitsForQa + " " + string.Join("|", form.OverflowForQa()));
                        Check(form.DiagnosticsFitsForQa && button.Visible && button.Enabled && form.DiagnosticsHintForQa == Strings.Get("diagnosticsHint", lang) && form.AboutFitsForQa() &&
                            button.AccessibleName == Strings.Get("diagnosticsTitle", lang) + ": " + Strings.Get("diagnosticsSave", lang),
                            "the diagnostics row fits above the version line (" + lang + ")", results);
                        SaveBitmap(form, Path.Combine(qa, "diagnostics-" + lang + ".png"));
                        if (lang == "ru")
                        {
                            shown = null; button.PerformClick();
                            var saved = await UntilForQa(() => shown != null && form.DiagnosticsHintForQa != Strings.Get("diagnosticsHint", lang), 5000);
                            Check(saved && File.Exists(shown) && Path.GetDirectoryName(shown) == folder && form.DiagnosticsHintForQa == string.Format(Strings.Get("diagnosticsSaved", lang), Path.GetFileName(shown)) && !form.SaveButton.Enabled,
                                "Save writes the file, shows it in its folder and says where, and the window has nothing else to save", results);
                        }
                        form.Close();
                    }
                }
                var file = shown != null && File.Exists(shown) ? File.ReadAllText(shown) : "";
                QaTrace("diagnostics-file " + file.Length);
                Check(file.Contains("App: " + AppInfo.Version + " " + AppInfo.Flavor) && file.Contains("Chromium: ") && file.Contains("New grades: on, every 30 min") && file.Contains("status qa") &&
                    file.Contains("/student/attentionStudents/{n}/"), "the file names the version, the engine, the settings and the recent events", results);
                var leaks = new List<string>();
                foreach (var planted in new[] { mail, iin, "123-45-67", "12345", "token=", "qa-secret", Environment.MachineName })
                    if (file.IndexOf(planted, StringComparison.OrdinalIgnoreCase) >= 0) leaks.Add(planted);
                if (Environment.UserName.Length >= DiagnosticFile.MinSecret && Regex.IsMatch(file, @"(?<![\p{L}\p{N}])" + Regex.Escape(Environment.UserName) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase)) leaks.Add("user name");
                QaTrace("diagnostics-leaks " + string.Join("|", leaks));
                Check(file.Length > 0 && leaks.Count == 0, "no e-mail, IIN, phone, record id, token, Windows user or computer name reaches the file", results);
            }
            finally { diagnosticsFolderForQa = null; showDiagnosticsForQa = null; diagnosticLog = null; }
        }
    }
}
