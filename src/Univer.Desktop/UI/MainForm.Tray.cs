using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // A second launch broadcasts this registered message; the running window, even when hidden in the tray, shows itself.
        internal static readonly int ShowWindowMessage = RegisterWindowMessage("Univer.Desktop.ShowWindow." + Environment.UserName);
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool exitRequested, trayHintShown, closeCommand;
        private Tween trayMotion;
        private int trayMotionGeneration;
        private FormWindowState trayRestoreState = FormWindowState.Normal;
        private void BuildTray()
        {
            if (selfTest || accountReview) return;
            trayMenu = new ContextMenuStrip { Font = Theme.Font(10), ShowImageMargin = false }; Theme.Style(trayMenu);
            Motion.FadeIn(trayMenu);
            trayIcon = new NotifyIcon { Icon = Icon, Text = "Univer · КазНУ", ContextMenuStrip = trayMenu, Visible = true };
            trayIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
            trayIcon.BalloonTipClicked += (s, e) => { var notice = noticeBalloon; noticeBalloon = false; RestoreFromTray(); if (notice) ShowNotices(); };
            trayIcon.BalloonTipClosed += (s, e) => noticeBalloon = false;
            LocalizeTray();
        }
        private void LocalizeTray()
        {
            if (trayMenu == null) return;
            var lang = prefs.Language;
            trayMenu.Items.Clear();
            var open = new ToolStripMenuItem(Strings.Get("trayOpen", lang)) { Font = Theme.Strong(10) };
            open.Click += (s, e) => RestoreFromTray();
            var unread = NoticesActive ? Notices.Unread(notices) : 0;
            var alerts = new ToolStripMenuItem(unread > 0 ? Strings.Get("notices", lang) + " (" + unread + ")" : Strings.Get("notices", lang));
            alerts.Click += (s, e) => { RestoreFromTray(); ShowNotices(); };
            var files = new ToolStripMenuItem(Strings.Get("downloads", lang)) { ShortcutKeyDisplayString = "Ctrl+J" };
            files.Click += (s, e) => { RestoreFromTray(); ShowDownloads(); };
            var settings = new ToolStripMenuItem(Strings.Get("settings", lang));
            settings.Click += (s, e) => { RestoreFromTray(); QueueAsync(() => OpenSettingsAsync()); };
            var exit = new ToolStripMenuItem(Strings.Get("trayExit", lang));
            exit.Click += (s, e) => ExitApplication();
            trayMenu.Items.AddRange(new ToolStripItem[] { open, alerts, files, settings, new ToolStripSeparator(), exit });
        }
        // Closing the window keeps the session, automatic sign-in and downloads alive in the tray.
        private bool HideToTray(FormClosingEventArgs e)
        {
            // Only the window's own close command (X, Alt+F4, taskbar "Close window") hides. WinForms keeps a cancelled
            // UserClosing reason, so a later plain WM_CLOSE from Task Manager or an installer must still exit.
            if (exitRequested || trayIcon == null || !prefs.Tray || !closeCommand || e.CloseReason != CloseReason.UserClosing) return false;
            e.Cancel = true;
            RememberWindow();
            trayMotion?.Cancel(); var generation = ++trayMotionGeneration;
            trayRestoreState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
            if (Motion.Enabled && !Motion.KeyboardInput)
            {
                if (!Motion.Reduced) WindowState = FormWindowState.Minimized;
                var fade = Motion.Reduced;
                trayMotion = Motion.Run(fade ? 140 : Motion.Normal, t => { if (fade && !IsDisposed) Opacity = 1 - .85 * Motion.EaseOut(t); }, () =>
                { if (!closing && generation == trayMotionGeneration) { Hide(); Opacity = 1; TrimHiddenMemory(); } });
            }
            else { Hide(); TrimHiddenMemory(); }
            if (!trayHintShown)
            {
                trayHintShown = true; noticeBalloon = false;
                trayIcon.ShowBalloonTip(5000, Strings.Get("trayBalloonTitle", prefs.Language), Strings.Get("trayBalloon", prefs.Language), ToolTipIcon.Info);
            }
            return true;
        }
        // With only the tray icon left, release a closed file preview's browser and trim the working set; the session, timers
        // and downloads keep running, and trimmed pages are read back on demand.
        private void TrimHiddenMemory()
        {
            if (closing || Visible) return;
            downloadsView?.ReleaseViewer();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            try { using (var self = System.Diagnostics.Process.GetCurrentProcess()) SetProcessWorkingSetSize(self.Handle, (IntPtr)(-1), (IntPtr)(-1)); }
            catch (Exception) { }
        }
        private void RestoreFromTray()
        {
            if (closing) return;
            ++trayMotionGeneration; trayMotion?.Cancel(); Opacity = 1;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = trayRestoreState;
            Activate(); BringToFront();
            SetForegroundWindow(Handle);
        }
        private void ExitApplication() { exitRequested = true; if (!Visible) Show(); Close(); }
        private void DisposeTray()
        {
            ++trayMotionGeneration; trayMotion?.Cancel();
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
            trayMenu?.Dispose(); trayMenu = null;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00A1) Motion.KeyboardInput = false;
            if (m.Msg == ShowWindowMessage && ShowWindowMessage != 0) { RestoreFromTray(); return; }
            // WM_SYSCOMMAND/SC_CLOSE raises FormClosing synchronously inside the default handling.
            if (m.Msg == 0x0112 && ((long)m.WParam & 0xFFF0) == 0xF060)
            {
                closeCommand = true;
                try { base.WndProc(ref m); } finally { closeCommand = false; }
                return;
            }
            base.WndProc(ref m);
        }
        internal static Icon LoadAppIcon()
        {
            try
            {
                using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("Univer.Desktop.app.ico"))
                    return stream == null ? null : new Icon(stream);
            }
            catch (Exception) { return null; }
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);
    }
}
