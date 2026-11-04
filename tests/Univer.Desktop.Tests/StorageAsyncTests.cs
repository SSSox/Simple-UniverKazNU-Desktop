using System;
using System.IO;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public sealed class StorageAsyncTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        [Fact]
        public async Task QueuedPreferenceWriteCapturesOriginalValues()
        {
            var storage = new Storage(root); var prefs = new Preferences { Language = "en", Readable = false };
            var pending = storage.SavePreferencesAsync(prefs); prefs.Language = "ru"; prefs.Readable = true;
            await pending; Assert.Equal("en", storage.LoadPreferences().Language); Assert.False(storage.LoadPreferences().Readable);
        }
        [Fact]
        public async Task MultipleWritesPreserveLastPauseAndLeaveNoTemporaryFile()
        {
            var storage = new Storage(root); var prefs = new Preferences { Language = "en" };
            var first = storage.SavePreferencesAsync(prefs); prefs.AutoLoginPaused = true;
            var second = storage.SavePreferencesAsync(prefs); await Task.WhenAll(first, second);
            Assert.True(storage.LoadPreferences().AutoLoginPaused); Assert.False(File.Exists(Path.Combine(root, "preferences.json.new")));
        }
        public void Dispose()
        {
            var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests")) + Path.DirectorySeparatorChar;
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
}
