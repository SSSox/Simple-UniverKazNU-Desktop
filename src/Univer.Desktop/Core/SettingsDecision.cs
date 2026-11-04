using System;

namespace Univer.Desktop.Core
{
    internal enum SettingsChange { AppearanceOnly, ResumeSignIn, NewCredentials }
    internal static class SettingsDecision
    {
        public static SettingsChange Classify(Credentials current, Credentials next, bool paused)
        {
            if (next == null) return SettingsChange.AppearanceOnly;
            if (current == null || !string.Equals(current.Login, next.Login, StringComparison.Ordinal) ||
                !string.Equals(current.Password, next.Password, StringComparison.Ordinal)) return SettingsChange.NewCredentials;
            return paused ? SettingsChange.ResumeSignIn : SettingsChange.AppearanceOnly;
        }
    }
}
