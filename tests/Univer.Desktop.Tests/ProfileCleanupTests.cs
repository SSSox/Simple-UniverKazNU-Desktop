using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ProfileCleanupTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        private string junction;
        private const string Old = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Active = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        [Fact]
        public void BrowserRootJunctionDoesNotDeleteItsTarget()
        {
            var app = Path.Combine(root, "app"); var target = Path.Combine(root, "target");
            var sentinel = MakeSentinel(Path.Combine(target, Old));
            var storage = new Storage(app); junction = storage.BrowserRoot; CreateJunction(junction, target);
            storage.CleanOldProfiles(Active); Assert.True(File.Exists(sentinel));
        }
        [Fact]
        public void JunctionAncestorDoesNotDeleteItsTarget()
        {
            var target = Path.Combine(root, "target"); var sentinel = MakeSentinel(Path.Combine(target, "app", "Browser", Old));
            junction = Path.Combine(root, "ancestor"); CreateJunction(junction, target);
            new Storage(Path.Combine(junction, "app")).CleanOldProfiles(Active); Assert.True(File.Exists(sentinel));
        }
        [Fact]
        public void JunctionInsideProfilePreservesBothProfileAndTarget()
        {
            var storage = new Storage(Path.Combine(root, "app")); var profile = storage.ProfilePath(Old);
            var own = MakeSentinel(profile); var target = Path.Combine(root, "target"); var foreign = MakeSentinel(target);
            junction = Path.Combine(profile, "nested"); CreateJunction(junction, target);
            storage.CleanOldProfiles(Active); Assert.True(File.Exists(own)); Assert.True(File.Exists(foreign));
        }
        [Fact]
        public void PlainObsoleteProfilesAreRemovedAndActiveProfileSurvives()
        {
            var storage = new Storage(root); var old = MakeSentinel(storage.ProfilePath(Old)); var active = MakeSentinel(storage.ProfilePath(Active));
            storage.CleanOldProfiles(Active); Assert.False(File.Exists(old)); Assert.True(File.Exists(active));
        }
        [Fact]
        public void BrowserVersionsShareSettingsButNeverBrowserDatabases()
        {
            var legacy = new Storage(root); var modern = new Storage(root, BrowserProfileFlavor.Modern);
            Assert.NotEqual(legacy.ProfilePath(Active), modern.ProfilePath(Active));
            legacy.SavePreferences(new Preferences { Language = "kz", Profile = Active });
            Assert.Equal("kz", modern.LoadPreferences().Language); Assert.Equal(Active, modern.LoadPreferences().Profile);
        }
        private static string MakeSentinel(string folder)
        { Directory.CreateDirectory(folder); var path = Path.Combine(folder, "sentinel.txt"); File.WriteAllText(path, "Isolated regression fixture"); return path; }
        private static void CreateJunction(string path, string target)
        {
            Directory.CreateDirectory(path);
            var substitute = Encoding.Unicode.GetBytes(@"\??\" + Path.GetFullPath(target));
            var print = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
            var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
            Array.Copy(BitConverter.GetBytes(0xA0000003u), 0, buffer, 0, 4);
            Array.Copy(BitConverter.GetBytes((ushort)(buffer.Length - 8)), 0, buffer, 4, 2);
            Array.Copy(BitConverter.GetBytes((ushort)substitute.Length), 0, buffer, 10, 2);
            Array.Copy(BitConverter.GetBytes((ushort)(substitute.Length + 2)), 0, buffer, 12, 2);
            Array.Copy(BitConverter.GetBytes((ushort)print.Length), 0, buffer, 14, 2);
            Array.Copy(substitute, 0, buffer, 16, substitute.Length); Array.Copy(print, 0, buffer, 18 + substitute.Length, print.Length);
            using (var handle = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                int returned;
                if (handle.IsInvalid || !DeviceIoControl(handle, 0x900A4, buffer, buffer.Length, IntPtr.Zero, 0, out returned, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        public void Dispose()
        {
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected fixture path");
            // Remove only our verified junction, without recursive traversal, before deleting the fixture tree.
            if (junction != null && Directory.Exists(junction)) Directory.Delete(junction, false);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputLength, IntPtr output, int outputLength, out int returned, IntPtr overlapped);
    }
}
