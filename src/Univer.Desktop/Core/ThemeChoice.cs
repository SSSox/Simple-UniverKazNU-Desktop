namespace Univer.Desktop.Core
{
    // The interface theme the student chose in Settings: light, dark, or as Windows' app mode (the default).
    internal static class ThemeChoice
    {
        public const string System = "system", Light = "light", Dark = "dark";
        public static string Normalize(string value) { return value == Light || value == Dark ? value : System; }
        // systemDark: Windows' "Choose your app mode" is Dark (false where Windows has no such setting).
        public static bool IsDark(string value, bool systemDark)
        {
            value = Normalize(value);
            return value == Dark || value == System && systemDark;
        }
    }
}
