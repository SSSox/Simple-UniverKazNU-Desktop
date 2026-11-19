using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Univer.Desktop.Core
{
    // Where the "start with Windows" line lives. The app writes it only through this, so self-test and xUnit use the
    // in-memory copy and never touch the user's registry.
    internal interface IRunKey
    {
        string Read(string name);
        void Write(string name, string value);
        void Delete(string name);
    }
    // HKCU\…\Run: per user, no administrator rights, the same place Windows' Startup apps page lists and switches.
    internal sealed class RegistryRunKey : IRunKey
    {
        private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public string Read(string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunPath, false)) return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        public void Write(string name, string value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunPath)) key.SetValue(name, value, RegistryValueKind.String);
        }
        public void Delete(string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunPath, true)) key?.DeleteValue(name, false);
        }
    }
    internal sealed class MemoryRunKey : IRunKey
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Read(string name) { string value; return values.TryGetValue(name, out value) ? value : null; }
        public void Write(string name, string value) { values[name] = value; }
        public void Delete(string name) { values.Remove(name); }
    }
    internal enum AutostartState { Off, On, Elsewhere }
    // Start with Windows, straight into the tray. Off until the user turns it on; the line names this copy's own
    // Univer.Desktop.exe, so a moved or second copy is shown as "elsewhere" and Save writes this copy's path.
    internal static class Autostart
    {
        internal const string ValueName = "Univer.Desktop";
        internal const string TrayArgument = "--tray";
        internal static bool StartsInTray(string[] args) { return args != null && Array.IndexOf(args, TrayArgument) >= 0; }
        internal static string Command(string exe) { return "\"" + exe + "\" " + TrayArgument; }
        internal static AutostartState State(IRunKey key, string exe)
        {
            var value = key.Read(ValueName);
            if (string.IsNullOrWhiteSpace(value)) return AutostartState.Off;
            var target = Target(value);
            return target != null && SamePath(target, exe) && value.Trim().EndsWith(" " + TrayArgument, StringComparison.Ordinal) ? AutostartState.On : AutostartState.Elsewhere;
        }
        // The program the line starts: the quoted first part, or the text up to the first space.
        internal static string Target(string value)
        {
            if (value == null) return null;
            value = value.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = value.IndexOf('"', 1);
                return end > 1 ? value.Substring(1, end - 1) : null;
            }
            var space = value.IndexOf(' ');
            return space < 0 ? value : value.Substring(0, space);
        }
        // On writes this copy's line (also over one that names another copy); off removes the line whoever wrote it.
        // Returns whether anything was changed.
        internal static bool Apply(IRunKey key, bool on, string exe)
        {
            var state = State(key, exe);
            if (on && state != AutostartState.On) { key.Write(ValueName, Command(exe)); return true; }
            if (!on && state != AutostartState.Off) { key.Delete(ValueName); return true; }
            return false;
        }
        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }
    }
}
