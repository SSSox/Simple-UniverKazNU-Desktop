using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // The diagnostics file (Core/DiagnosticFile.cs): Settings' button writes it to Downloads and shows it in its folder. The
    // app keeps no log on disk; the last events of this run live in memory only (statuses other than loading and ready,
    // failed loads with their code and the page's address shape).
    internal sealed partial class MainForm
    {
        private static readonly HashSet<string> RoutineStatus = new HashSet<string>(StringComparer.Ordinal) { "loading", "ready", "saving" };
        private DiagnosticLog diagnosticLog;
        private readonly DateTime startedAt = DateTime.Now;
        // Self-test keeps Explorer closed and reads where the file went.
        private Action<string> showDiagnosticsForQa;
        private string diagnosticsFolderForQa;
        private DiagnosticLog DiagnosticEvents { get { return diagnosticLog ?? (diagnosticLog = new DiagnosticLog(DiagnosticSecrets)); } }
        private void NoteEvent(string text) { DiagnosticEvents.Add(DateTime.Now, text); }
        private void NoteStatus(string key) { if (key != null && !RoutineStatus.Contains(key)) NoteEvent("status " + key); }
        // What could name the student or this computer, removed from every line wherever it appears.
        private IEnumerable<string> DiagnosticSecrets()
        {
            var secrets = new List<string> { credentials?.Login, Environment.UserName, Environment.MachineName, Environment.UserDomainName };
            try { secrets.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); } catch (Exception) { }
            return secrets.Where(s => !string.IsNullOrWhiteSpace(s));
        }
        // Settings and state as names and numbers: no addresses but the shown page's shape, no texts of the site.
        private List<KeyValuePair<string, string>> DiagnosticFacts()
        {
            Func<bool, string> on = value => value ? "on" : "off";
            var facts = new List<KeyValuePair<string, string>>();
            Action<string, object> add = (name, value) => facts.Add(new KeyValuePair<string, string>(name, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));
            add("App", AppInfo.Version + " " + AppInfo.Flavor + " " + AppInfo.Architecture + (updates == null ? "" : updates.Installed ? ", installed" : ", portable"));
            add("Chromium", AppInfo.Chromium);
            add("Windows", Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit"));
            add(".NET", Environment.Version);
            add("Culture", System.Globalization.CultureInfo.CurrentUICulture.Name + " / " + System.Globalization.CultureInfo.CurrentCulture.Name);
            add("Display", Screen.AllScreens.Length + " screen(s), primary " + Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height + ", scale " + Math.Round(Native.WindowScale(Handle) * 100) + "%");
            add("Window", WindowState + ", " + ClientSize.Width + "x" + ClientSize.Height + (Visible ? "" : ", hidden in the tray"));
            add("Running", Math.Round((DateTime.Now - startedAt).TotalMinutes) + " min");
            add("Language", prefs.Language);
            add("Theme", prefs.Theme + (Theme.Dark ? " (dark now)" : " (light now)"));
            add("Zoom", prefs.Zoom + "%");
            add("Reading view", on(prefs.Readable));
            add("Animations", on(prefs.Animations));
            add("Tray", on(prefs.Tray));
            add("Start with Windows", ReadAutostart().ToString().ToLowerInvariant());
            add("Class reminders", on(prefs.Reminders) + ", " + prefs.ReminderLead + " min");
            add("New grades", on(prefs.Grades) + ", every " + prefs.GradeInterval + " min");
            add("Links in the system browser", on(prefs.ExternalLinks));
            add("Saved sign-in", credentials == null ? (credentialsUnreadable ? "unreadable" : "none") : credentials.Sealed ? "sealed by the PIN" : "saved");
            add("PIN", on(PinSet));
            add("Automatic sign-in", prefs.AutoLoginPaused || guard.Stopped ? "paused" : "on");
            add("Settings storage", storageFailed ? "failed" : "ok");
            add("Shown page", main == null ? "none" : DiagnosticFile.Address(main.Address));
            add("Document viewer", document == null ? "closed" : "open");
            add("Page state", (error ? "failed " + failureCode : "loaded") + (OfflineShown ? ", offline week shown" : "") + (loading.Visible ? ", placeholder shown" : ""));
            add("Banner", messages.Current?.Key ?? "none");
            add("Kept week", scheduleWeek == null ? "none" : scheduleWeek.Classes.Count + " classes");
            add("Downloads listed", downloadsView?.Count ?? 0);
            add("Update check", updates == null ? "off" : updates.Phase + (updates.Available != null ? ", " + updates.Available + " offered" : ""));
            return facts;
        }
        // Settings' button: the file goes to Downloads under a name that never replaces another, then its folder opens with
        // it selected. Returns the file's name, or null when it could not be written.
        private async Task<string> SaveDiagnosticsAsync()
        {
            var now = DateTime.Now;
            var text = DiagnosticFile.Build(DiagnosticFacts(), DiagnosticEvents.Items, DiagnosticSecrets(), now);
            var folder = diagnosticsFolderForQa ?? DownloadService.DownloadsFolder();
            try
            {
                var path = await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    var temp = Path.Combine(folder, PartialDownloads.NewName());
                    try
                    {
                        File.WriteAllText(temp, text, new UTF8Encoding(true));
                        return SavedFile.Keep(temp, folder, DiagnosticFile.FileName(now), null);
                    }
                    finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { } }
                });
                if (showDiagnosticsForQa != null) showDiagnosticsForQa(path);
                else try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); } catch (Exception) { /* The file is saved; only its folder did not open. */ }
                return Path.GetFileName(path);
            }
            catch (Exception) { return null; }
        }
    }
}
