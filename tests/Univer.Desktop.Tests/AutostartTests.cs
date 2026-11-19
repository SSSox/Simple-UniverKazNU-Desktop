using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class AutostartTests
    {
        private const string Exe = @"C:\Users\Student\AppData\Local\Programs\Univer\Univer.Desktop.exe";

        [Fact]
        public void IsOffUntilTurnedOnAndThenStartsThisCopyInTheTray()
        {
            var key = new MemoryRunKey();
            Assert.Equal(AutostartState.Off, Autostart.State(key, Exe));
            Assert.True(Autostart.Apply(key, true, Exe));
            Assert.Equal("\"" + Exe + "\" --tray", key.Read(Autostart.ValueName));
            Assert.Equal(AutostartState.On, Autostart.State(key, Exe));
            // Saving again with nothing changed writes nothing.
            Assert.False(Autostart.Apply(key, true, Exe));
            Assert.True(Autostart.Apply(key, false, Exe));
            Assert.Null(key.Read(Autostart.ValueName));
            Assert.False(Autostart.Apply(key, false, Exe));
        }

        [Theory]
        // A copy moved or unpacked elsewhere, or a line without the tray argument: shown as on, and Save repairs it.
        [InlineData("\"D:\\Portable\\Univer\\Univer.Desktop.exe\" --tray")]
        [InlineData("\"C:\\Users\\Student\\AppData\\Local\\Programs\\Univer\\Univer.Desktop.exe\"")]
        [InlineData("C:\\Old\\Univer.Desktop.exe --tray")]
        [InlineData("\"")]
        public void ALineForAnotherCopyIsRepairedBySave(string line)
        {
            var key = new MemoryRunKey();
            key.Write(Autostart.ValueName, line);
            Assert.Equal(AutostartState.Elsewhere, Autostart.State(key, Exe));
            Assert.True(Autostart.Apply(key, true, Exe));
            Assert.Equal(AutostartState.On, Autostart.State(key, Exe));
        }

        [Fact]
        public void TurningOffRemovesALineThatNamesAnotherCopy()
        {
            var key = new MemoryRunKey();
            key.Write(Autostart.ValueName, "\"D:\\Portable\\Univer.Desktop.exe\" --tray");
            Assert.True(Autostart.Apply(key, false, Exe));
            Assert.Equal(AutostartState.Off, Autostart.State(key, Exe));
        }

        [Fact]
        public void PathsCompareWithoutCase()
        {
            var key = new MemoryRunKey();
            key.Write(Autostart.ValueName, "\"" + Exe.ToUpperInvariant() + "\" --tray");
            Assert.Equal(AutostartState.On, Autostart.State(key, Exe));
        }

        [Theory]
        [InlineData(new[] { "--tray" }, true)]
        [InlineData(new[] { "--self-test", "--tray" }, true)]
        [InlineData(new[] { "--TRAY" }, false)]
        [InlineData(new string[0], false)]
        public void OnlyTheExactArgumentStartsInTheTray(string[] args, bool tray)
        {
            Assert.Equal(tray, Autostart.StartsInTray(args));
        }
    }
}
