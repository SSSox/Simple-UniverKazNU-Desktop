using System;
using System.IO;
using System.Text;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // Settings → Language & view → Theme: light, dark or as Windows (the default), kept in the preferences file.
    public class ThemePreferenceTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        [Fact]
        public void OlderPreferenceFilesWithoutAThemeFollowWindows()
        {
            var storage = new Storage(root);
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{\"Language\":\"kz\",\"Readable\":true,\"Profile\":\"" + Guid.NewGuid().ToString("N") + "\",\"Zoom\":110}", Encoding.UTF8);
            var prefs = storage.LoadPreferences();
            Assert.Equal(ThemeChoice.System, prefs.Theme);
            Assert.Equal("kz", prefs.Language);
            Assert.Equal(110, prefs.Zoom);
        }
        [Theory]
        [InlineData("dark", "dark")]
        [InlineData("light", "light")]
        [InlineData("system", "system")]
        [InlineData("Dark", "system")]
        [InlineData("purple", "system")]
        [InlineData(null, "system")]
        public void ThemeRoundTripsAndUnknownValuesFollowWindows(string saved, string loaded)
        {
            var storage = new Storage(root);
            storage.SavePreferences(new Preferences { Theme = saved });
            Assert.Equal(loaded, storage.LoadPreferences().Theme);
        }
        [Fact]
        public void QueuedWritesKeepTheThemeChosenAtSaveTime()
        {
            var prefs = new Preferences { Theme = ThemeChoice.Dark };
            var copy = prefs.Clone(); prefs.Theme = ThemeChoice.Light;
            Assert.Equal(ThemeChoice.Dark, copy.Theme);
            Assert.Equal(ThemeChoice.System, new Preferences().Theme);
        }
        [Theory]
        [InlineData("system", false, false)]
        [InlineData("system", true, true)]
        [InlineData("light", true, false)]
        [InlineData("dark", false, true)]
        [InlineData("unknown", true, true)]
        public void TheChoiceResolvesAgainstWindowsAppMode(string choice, bool windowsDark, bool dark)
        {
            Assert.Equal(dark, ThemeChoice.IsDark(choice, windowsDark));
        }
        public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
