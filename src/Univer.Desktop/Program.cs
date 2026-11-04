using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.UI;

namespace Univer.Desktop
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
#if !LEGACY_ENGINE
            // Check before JIT-compiling the method that uses native CEF types.
            if (Environment.OSVersion.Version.Major < 10)
            {
                MessageBox.Show(Strings.Get("modernWindowsRequired", "ru"), "Univer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
#endif
            return Run(args);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            var selfTest = Array.IndexOf(args, "--self-test") >= 0;
#if QA_REVIEW
            var reviewAll = Array.IndexOf(args, "--review-all") >= 0;
            var accountReview = reviewAll || Array.IndexOf(args, "--review-account") >= 0 || Array.IndexOf(args, "--review-credentials") >= 0;
#else
            // The live-cabinet reviewer walks real pages and writes captures. It is built only into a QA build, so a
            // released executable cannot be pointed at a signed-in session by a command-line argument.
            var reviewAll = false;
            var accountReview = false;
#endif
            // Chromium adds this switch when it restarts an elevated launch without administrator rights.
            var deElevatedRelaunch = Array.IndexOf(args, "--do-not-de-elevate") >= 0;
            if (selfTest)
            {
                var trace = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.trace");
                File.WriteAllText(trace, "program-start\n");
                Application.ThreadException += (sender, error) =>
                { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.failed"), error.Exception.GetType().Name + "\n" + error.Exception.StackTrace); Application.ExitThread(); };
            }
            // The manual keyboard and study-page checks run visibly but, like self-test, in a fresh profile with no saved sign-in.
            var isolated = selfTest || Array.IndexOf(args, "--qa-keys") >= 0 || Array.IndexOf(args, "--qa-study") >= 0;
            var data = isolated ? Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", Guid.NewGuid().ToString("N")) :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Univer.Desktop");
            using (var mutex = new Mutex(false, "Local\\Univer.Desktop." + (isolated ? Guid.NewGuid().ToString("N") : Environment.UserName)))
            {
                bool owns;
                // The elevated original still holds the mutex for a moment while it exits after relaunching this copy.
                try { owns = mutex.WaitOne(deElevatedRelaunch ? 5000 : 0); } catch (AbandonedMutexException) { owns = true; }
                if (!owns) { if (!ActivateRunningInstance()) MessageBox.Show("Univer уже запущен.", "Univer"); return 0; }
                var errorLanguage = "ru";
                try
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#if LEGACY_ENGINE
                    Cef.EnableHighDPISupport();
#endif
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
#if LEGACY_ENGINE
                    var storage = new Storage(data);
#else
                    var storage = new Storage(data, BrowserProfileFlavor.Modern);
#endif
                    var settings = new CefSettings { RootCachePath = storage.BrowserRoot, LogSeverity = LogSeverity.Disable, PersistSessionCookies = true };
                    // Date fields and the PDF viewer follow Chromium's per-process locale; Kazakh has none, so Russian (same dd.mm.yyyy)
                    // stands in. The header sent to the site does not change.
                    string startLanguage, startTheme;
                    try { var start = storage.LoadPreferences(); startLanguage = start.Language; startTheme = start.Theme; } catch (Exception) { startLanguage = "ru"; startTheme = ThemeChoice.System; }
                    // The window is built in the chosen theme from the start, so nothing is recoloured on the first paint.
                    // Self-test starts light whatever Windows uses; its theme step switches to dark itself.
                    UI.Theme.Set(!selfTest && ThemeChoice.IsDark(startTheme, UI.Theme.SystemDark()));
                    errorLanguage = startLanguage;
                    settings.Locale = startLanguage == "en" ? "en-GB" : "ru";
                    settings.AcceptLanguageList = "en-US,en";
                    settings.CefCommandLineArgs.Add("disable-component-update", "1");
                    // Before a page paints, and while a resized view catches up, Chromium shows this colour.
                    // Its default is black, which flashed as dark blocks between pages; this matches the shell.
                    settings.BackgroundColor = UI.Theme.Dark ? Cef.ColorSetARGB(255, UI.Theme.Background.R, UI.Theme.Background.G, UI.Theme.Background.B) : Cef.ColorSetARGB(255, 243, 246, 249);
                    // The loading placeholder covers the page while it is prepared. Chromium's occlusion tracking would
                    // then drop the covered page's frames and show a black surface for a moment when it is uncovered.
                    settings.CefCommandLineArgs.Add("disable-backgrounding-occluded-windows", "1");
                    // Chromium keeps an idle pre-started renderer (~45 MB) for the next new site. This app stays on one
                    // site whose renderer is reused, so the spare only costs memory; site isolation itself is unchanged.
#if LEGACY_ENGINE
                    // Chromium 109's bundled roots predate the site's current Sectigo chain.
                    // Use Windows' certificate verifier and trust store, retaining full TLS validation.
                    settings.CefCommandLineArgs.Add("disable-features", "ChromeRootStoreUsed,CalculateNativeWinOcclusion,SpareRendererForSitePerProcess");
#else
                    settings.CefCommandLineArgs.Add("disable-features", "CalculateNativeWinOcclusion,SpareRendererForSitePerProcess");
#endif
                    if (!Cef.Initialize(settings))
                    {
#if !LEGACY_ENGINE
                        // Started as administrator, Chromium relaunches the app with normal rights and that copy
                        // opens the window; this elevated process ends quietly instead of reporting a failed start.
                        if (Cef.GetExitCode() == CefSharp.Enums.ResultCode.NormalExitAutoDeElevated) return 0;
#endif
                        throw new InvalidOperationException("Browser initialization failed");
                    }
                    try
                    {
                        using (var form = new MainForm(storage, selfTest, Array.IndexOf(args, "--live-smoke") >= 0, accountReview, reviewAll))
                        {
                            // Self-test records the failure itself; users get a recoverable message, never a stack trace.
                            if (!selfTest) Application.ThreadException += (sender, error) => form.ReportUnexpectedError();
                            Application.Run(form);
                        }
                    }
                    finally { Cef.Shutdown(); }
                    if (selfTest) return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.ok")) ? 0 : 1;
                    return 0;
                }
                catch (Exception exception)
                {
                    if (selfTest) File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.failed"), exception.GetType().Name + "\n" + exception.StackTrace);
                    if (!selfTest)
#if LEGACY_ENGINE
                        MessageBox.Show(Strings.Get("legacyStartupFailed", errorLanguage), "Univer", MessageBoxButtons.OK, MessageBoxIcon.Error);
#else
                        MessageBox.Show(Strings.Get("modernStartupFailed", errorLanguage), "Univer", MessageBoxButtons.OK, MessageBoxIcon.Error);
#endif
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        // A second launch asks the running window (also when hidden in the tray) to show itself.
        private static bool ActivateRunningInstance()
        {
            try { AllowSetForegroundWindow(-1); return MainForm.ShowWindowMessage != 0 && PostMessage((IntPtr)0xffff, MainForm.ShowWindowMessage, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception) { return false; }
        }
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int process);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
