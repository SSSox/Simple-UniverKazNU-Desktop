using System;
using System.Diagnostics;
using System.IO;
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
            Assert.Equal(DeleteVerdict.Ok, FileDeletion.Delete(record, recycler));
            Assert.Equal(new[] { record.Path }, recycler.Calls.ToArray());
            Assert.True(File.Exists(other.Path));
        }

        [Fact]
        public void ASizeThatDiffersFromTheRecordIsAFileChangedOutsideTheApp()
        {
            var record = Saved("a.pdf"); record.Total = record.Received = 99;
            var recycler = new MemoryRecycler();
            Assert.Equal(DeleteVerdict.Changed, FileDeletion.Delete(record, recycler));
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
            Assert.Equal(DeleteVerdict.Directory, FileDeletion.Delete(directory, recycler));
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
            Assert.Equal(DeleteVerdict.Link, FileDeletion.Delete(record, recycler));
            Assert.Empty(recycler.Calls);
            Assert.True(File.Exists(target.Path));
        }

        [Fact]
        public void ADriveWithoutARecycleBinRefusesInsteadOfDeletingForGood()
        {
            var record = Saved("a.pdf");
            var recycler = new MemoryRecycler { Available = false };
            Assert.Equal(DeleteVerdict.NoBin, FileDeletion.Delete(record, recycler));
            Assert.Empty(recycler.Calls);
            Assert.True(File.Exists(record.Path));
        }

        [Fact]
        public void TheRealRecyclerTrustsOnlyAFixedLocalDrive()
        {
            var real = new RecycleBinRecycler();
            Assert.False(real.CanRecycle(@"\\server\share\a.pdf"));
            Assert.False(real.CanRecycle(@"\\?\C:\a.pdf"));
            Assert.False(real.CanRecycle("a.pdf"));
            Assert.False(real.CanRecycle(@"ZZ:\a.pdf"));
        }

        [Fact]
        public void ARecyclerThatCannotMoveTheFileIsAFailureNotASuccess()
        {
            var record = Saved("a.pdf");
            Assert.Equal(DeleteVerdict.Failed, FileDeletion.Delete(record, new Throwing()));
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
            public bool CanRecycle(string path) { return true; }
            public void Recycle(string path) { throw new IOException("open elsewhere"); }
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
