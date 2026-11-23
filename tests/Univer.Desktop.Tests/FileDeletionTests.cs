using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Univer.Desktop.UI;
using Xunit;

namespace Univer.Desktop.Tests
{
    public sealed class FileDeletionTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "Univer.Tests.Delete-" + Guid.NewGuid().ToString("N"));
        public FileDeletionTests() { Directory.CreateDirectory(folder); }
        public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        private DownloadRecord Saved(string name, int size = 10, string content = null)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, new byte[size]);
            return new DownloadRecord { FileName = name, Path = path, State = DownloadState.Completed, Received = size, Total = size };
        }

        [Fact]
        public void AFinishedFileOfTheSavedSizeGoesToTheRecyclerAndNothingElseIsTouched()
        {
            var record = Saved("a.pdf"); var other = Saved("b.pdf");
            var recycler = new MemoryRecycler();
            Assert.Equal(DeleteVerdict.Ok, FileDeletion.DeleteAsync(record, recycler, IntPtr.Zero).GetAwaiter().GetResult());
            Assert.Equal(new[] { record.Path }, recycler.Calls.ToArray());
            Assert.True(File.Exists(other.Path));
        }

        [Fact]
        public void ASizeThatDiffersFromTheRecordIsAFileChangedOutsideTheApp()
        {
            var record = Saved("a.pdf"); record.Total = record.Received = 99;
            var recycler = new MemoryRecycler();
            Assert.Equal(DeleteVerdict.Changed, FileDeletion.DeleteAsync(record, recycler, IntPtr.Zero).GetAwaiter().GetResult());
            Assert.Empty(recycler.Calls);
        }

        [Fact]
        public void ARecordWithoutAnyKnownSizeIsNotDeleted()
        {
            var record = Saved("a.pdf"); record.Total = -1; record.Received = -1;
            Assert.Equal(DeleteVerdict.Changed, FileDeletion.Check(record));
            // The received size stands in when the total was never learned.
            record.Received = 10;
            Assert.Equal(DeleteVerdict.Ok, FileDeletion.Check(record));
        }

        [Fact]
        public void AMissingFileAndADirectoryAreRefused()
        {
            var missing = Saved("a.pdf"); File.Delete(missing.Path);
            Assert.Equal(DeleteVerdict.Missing, FileDeletion.Check(missing));
            var directory = new DownloadRecord { FileName = "dir", Path = Path.Combine(folder, "dir"), State = DownloadState.Completed, Total = 0, Received = 0 };
            Directory.CreateDirectory(directory.Path);
            var recycler = new MemoryRecycler();
            Assert.Equal(DeleteVerdict.Directory, FileDeletion.DeleteAsync(directory, recycler, IntPtr.Zero).GetAwaiter().GetResult());
            Assert.Empty(recycler.Calls);
            Assert.True(Directory.Exists(directory.Path));
        }

        [Fact]
        public void APartFileARunningDownloadAndARecordWithoutAPathAreNotFiles()
        {
            var part = Saved("a.pdf.part");
            Assert.Equal(DeleteVerdict.Partial, FileDeletion.Check(part));
            var running = Saved("b.pdf"); running.State = DownloadState.Running;
            Assert.Equal(DeleteVerdict.NotAFile, FileDeletion.Check(running));
            Assert.Equal(DeleteVerdict.NotAFile, FileDeletion.Check(new DownloadRecord { State = DownloadState.Completed, Path = "" }));
            Assert.Equal(DeleteVerdict.NotAFile, FileDeletion.Check(new DownloadRecord { State = DownloadState.Completed, Path = "relative.pdf" }));
            Assert.Equal(DeleteVerdict.NotAFile, FileDeletion.Check(null));
        }

        [Fact]
        public void ALinkIsNeverFollowedOrDeleted()
        {
            var target = Saved("target.pdf");
            var link = Path.Combine(folder, "link.pdf");
            // A file symbolic link needs a privilege; where Windows refuses it there is no link to test with.
            if (!MakeLink(link, target.Path)) return;
            var record = new DownloadRecord { FileName = "link.pdf", Path = link, State = DownloadState.Completed, Total = 10, Received = 10 };
            var recycler = new MemoryRecycler();
            Assert.Equal(DeleteVerdict.Link, FileDeletion.DeleteAsync(record, recycler, IntPtr.Zero).GetAwaiter().GetResult());
            Assert.Empty(recycler.Calls);
            Assert.True(File.Exists(target.Path));
        }

        [Fact]
        public void ADriveWithoutARecycleBinRefusesInsteadOfDeletingForGood()
        {
            var record = Saved("a.pdf");
            var recycler = new MemoryRecycler { Available = false };
            Assert.Equal(DeleteVerdict.NoBin, FileDeletion.DeleteAsync(record, recycler, IntPtr.Zero).GetAwaiter().GetResult());
            Assert.Empty(recycler.Calls);
            Assert.True(File.Exists(record.Path));
        }

        [Fact]
        public void TheRealRecyclerTrustsOnlyAFixedLocalDrive()
        {
            var real = new RecycleBinRecycler();
            Assert.Equal(RecycleSupport.NoBin, real.Support(@"\\server\share\a.pdf"));
            Assert.Equal(RecycleSupport.NoBin, real.Support(@"\\?\C:\a.pdf"));
            Assert.Equal(RecycleSupport.NoBin, real.Support("a.pdf"));
            Assert.Equal(RecycleSupport.NoBin, real.Support(@"ZZ:\a.pdf"));
            Assert.Equal(RecycleSupport.NoBin, real.Support(""));
        }

        [Fact]
        public void TheDriveIsReadFromTheResolvedPathNotTheLetterTheFileWasSavedUnder()
        {
            Assert.Equal(@"C:\", FileDeletion.LocalDriveRoot(@"\\?\C:\x"));
            Assert.Equal(@"d:\", FileDeletion.LocalDriveRoot(@"\\?\d:\Users\a\b.pdf"));
            Assert.Null(FileDeletion.LocalDriveRoot(@"\\?\UNC\srv\share\x"));
            Assert.Null(FileDeletion.LocalDriveRoot(@"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\x"));
            Assert.Null(FileDeletion.LocalDriveRoot(@"C:\x"));
            Assert.Null(FileDeletion.LocalDriveRoot(@"\\?\C:"));
            Assert.Null(FileDeletion.LocalDriveRoot(""));
            Assert.Null(FileDeletion.LocalDriveRoot(null));
        }

        [Fact]
        public void TheVolumeAndTheShellPathAreReadFromTheResolvedNames()
        {
            const string guid = @"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\";
            Assert.Equal(guid, FileDeletion.VolumeRoot(guid + @"Users\a\b.pdf"));
            Assert.Null(FileDeletion.VolumeRoot(@"\\?\Volume{0123\x}\b.pdf"));
            Assert.Null(FileDeletion.VolumeRoot(@"\\?\C:\b.pdf"));
            Assert.Null(FileDeletion.VolumeRoot(@"\\?\UNC\srv\share\b.pdf"));
            Assert.Null(FileDeletion.VolumeRoot(@"\\?\Volume{}\b.pdf"));
            Assert.Null(FileDeletion.VolumeRoot(null));
            Assert.Equal(@"D:\Загрузки\b.pdf", FileDeletion.ShellPath(@"\\?\D:\Загрузки\b.pdf"));
            Assert.Null(FileDeletion.ShellPath(@"\\?\UNC\srv\share\b.pdf"));
            Assert.Null(FileDeletion.ShellPath(guid + "b.pdf"));
            Assert.Null(FileDeletion.ShellPath(@"\\?\C:\" + new string('a', 260)));
        }

        [Fact]
        public void TheRealRecyclerAcceptsAFileOnTheFixedTempDriveByItsVolume()
        {
            var record = Saved("a.pdf");
            var fixedDrive = new DriveInfo(Path.GetPathRoot(record.Path)).DriveType == DriveType.Fixed;
            Assert.Equal(fixedDrive ? RecycleSupport.Yes : RecycleSupport.NoBin, new RecycleBinRecycler().Support(record.Path));
        }

        [Fact]
        public void AFileThisUserMayNotReadIsStillJudgedByItsVolumeNotCalledBusy()
        {
            var record = Saved("a.pdf");
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            var deny = new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny);
            var security = File.GetAccessControl(record.Path); security.AddAccessRule(deny); File.SetAccessControl(record.Path, security);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() => File.OpenRead(record.Path).Dispose());
                var fixedDrive = new DriveInfo(Path.GetPathRoot(record.Path)).DriveType == DriveType.Fixed;
                Assert.Equal(fixedDrive ? RecycleSupport.Yes : RecycleSupport.NoBin, new RecycleBinRecycler().Support(record.Path));
            }
            finally { security = File.GetAccessControl(record.Path); security.RemoveAccessRule(deny); File.SetAccessControl(record.Path, security); }
            var denied = new MemoryRecycler { Denied = true };
            Assert.Equal(DeleteVerdict.Denied, FileDeletion.DeleteAsync(record, denied, IntPtr.Zero).GetAwaiter().GetResult());
            Assert.Empty(denied.Calls);
            Assert.Equal("deleteDenied", FileDeletion.StatusKey(DeleteVerdict.Denied));
        }

        [Fact]
        public void AReadOnlyFileNobodyHoldsIsReleasedAtOnce()
        {
            var record = Saved("a.pdf");
            File.SetAttributes(record.Path, FileAttributes.ReadOnly);
            try
            {
                var watch = Stopwatch.StartNew();
                Assert.True(FileDeletion.WaitReleasedAsync(record.Path, 1500, 50).GetAwaiter().GetResult());
                Assert.InRange(watch.ElapsedMilliseconds, 0, 1000);
            }
            finally { File.SetAttributes(record.Path, FileAttributes.Normal); }
        }

        [Fact]
        public void AFileAnotherProgramHoldsIsBusyNotWithoutABin()
        {
            var record = Saved("a.pdf");
            var recycler = new MemoryRecycler { Busy = true };
            Assert.Equal(DeleteVerdict.Busy, FileDeletion.DeleteAsync(record, recycler, IntPtr.Zero).GetAwaiter().GetResult());
            Assert.Empty(recycler.Calls);
            Assert.Equal("deleteBusy", FileDeletion.StatusKey(DeleteVerdict.Busy));
        }

        [Fact]
        public void TheOwnerWindowReachesTheRecycler()
        {
            var record = Saved("a.pdf"); var recycler = new MemoryRecycler();
            FileDeletion.DeleteAsync(record, recycler, new IntPtr(1234)).GetAwaiter().GetResult();
            Assert.Equal(new[] { new IntPtr(1234) }, recycler.Owners.ToArray());
        }

        [Fact]
        public void WaitingForARelease_GivesUpAfterTheLimitAndSucceedsOnceTheHolderLetsGo()
        {
            var record = Saved("a.pdf");
            using (var holder = new FileStream(record.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.False(FileDeletion.WaitReleasedAsync(record.Path, 250, 50).GetAwaiter().GetResult());
            Assert.True(FileDeletion.WaitReleasedAsync(record.Path, 250, 50).GetAwaiter().GetResult());
        }

        [Fact]
        public void TheRealStructureHasTheShellsLayout()
        {
            int size, flags, aborted;
            RecycleBinRecycler.Layout(out size, out flags, out aborted);
            Assert.Equal(IntPtr.Size == 8 ? 56 : 30, size);
            Assert.Equal(IntPtr.Size == 8 ? 32 : 16, flags);
            Assert.Equal(IntPtr.Size == 8 ? 36 : 18, aborted);
        }

        [Fact]
        public void ARecyclerThatCannotMoveTheFileIsAFailureNotASuccess()
        {
            var record = Saved("a.pdf");
            Assert.Equal(DeleteVerdict.Failed, FileDeletion.DeleteAsync(record, new Throwing(), IntPtr.Zero).GetAwaiter().GetResult());
        }

        [Fact]
        public void AFileThisAccountMayNotDeleteIsDeniedNotFailed()
        {
            var record = Saved("a.pdf");
            Assert.Equal(DeleteVerdict.Denied, FileDeletion.DeleteAsync(record, new Throwing(new UnauthorizedAccessException()), IntPtr.Zero).GetAwaiter().GetResult());
        }

        [Fact]
        public void EveryVerdictHasAStatusLineInAllThreeLanguages()
        {
            foreach (DeleteVerdict verdict in Enum.GetValues(typeof(DeleteVerdict)))
            {
                var key = FileDeletion.StatusKey(verdict);
                foreach (var lang in new[] { "ru", "kz", "en" }) Assert.False(string.IsNullOrWhiteSpace(Strings.Get(key, lang)), key + " " + lang);
                Assert.NotNull(UiStatus.Describe(key));
            }
            foreach (var key in new[] { "deleteFile", "deleteFileTitle", "deleteFileMessage", "deleteFileAction" })
                foreach (var lang in new[] { "ru", "kz", "en" }) Assert.False(string.IsNullOrWhiteSpace(Strings.Get(key, lang)), key + " " + lang);
        }

        private sealed class Throwing : IFileRecycler
        {
            private readonly Exception error;
            public Throwing(Exception error = null) { this.error = error ?? new IOException("open elsewhere"); }
            public RecycleSupport Support(string path) { return RecycleSupport.Yes; }
            public Task RecycleAsync(string path, IntPtr owner) { var failed = new TaskCompletionSource<bool>(); failed.SetException(error); return failed.Task; }
        }
        private static bool MakeLink(string link, string target)
        {
            try
            {
                var start = new ProcessStartInfo("cmd.exe", "/c mklink \"" + link + "\" \"" + target + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var process = Process.Start(start)) { process.WaitForExit(10000); }
                return File.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception) { return false; }
        }
    }
}
