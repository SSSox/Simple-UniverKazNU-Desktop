using System;
using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // A file saved from the site carries the mark of a download, as one saved by a browser does: Windows SmartScreen
    // and Office's Protected View rely on it.
    public sealed class SavedFileTests : IDisposable
    {
        private const string Site = "https://univer.kaznu.kz/";
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        public SavedFileTests() { Directory.CreateDirectory(root); }
        private string Part(string text) { var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".part"); File.WriteAllText(path, text); return path; }

        [Fact]
        public void Keep_MarksTheFinishedFileAsADownloadFromTheSite()
        {
            var saved = SavedFile.Keep(Part("lecture"), root, "Лекция 1.pdf", Site);
            Assert.Equal(Path.Combine(root, "Лекция 1.pdf"), saved);
            Assert.Equal("lecture", File.ReadAllText(saved));
            var zone = SavedFile.Zone(saved);
            Assert.NotNull(zone);
            Assert.Equal(new[] { "[ZoneTransfer]", "ZoneId=3", "HostUrl=" + Site }, zone.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public void Keep_NeverOverwritesOrMarksTheUsersOwnFile()
        {
            var own = Path.Combine(root, "Лекция 1.pdf"); File.WriteAllText(own, "mine");
            var saved = SavedFile.Keep(Part("lecture"), root, "Лекция 1.pdf", Site);
            Assert.Equal(Path.Combine(root, "Лекция 1 (1).pdf"), saved);
            Assert.Equal("mine", File.ReadAllText(own));
            Assert.Null(SavedFile.Zone(own));
            Assert.Contains("ZoneId=3", SavedFile.Zone(saved));
        }

        [Fact]
        public void Keep_MarksAFileWhoseNameIsNearThePathLimit()
        {
            // The path fits in 260 characters; with ":Zone.Identifier" after it, it does not.
            var name = new string('д', 255 - root.Length - 1 - 4) + ".pdf";
            var saved = SavedFile.Keep(Part("lecture"), root, name, Site);
            Assert.Equal(255, saved.Length);
            Assert.Contains("ZoneId=3", SavedFile.Zone(saved) ?? "");
        }

        [Fact]
        public void Mark_ThatCannotBeWrittenIsNotAnError()
        {
            // No such folder: the same answer a drive without alternate streams gives.
            var missing = Path.Combine(root, "no such folder", "file.pdf");
            Assert.False(SavedFile.Mark(missing, Site));
            Assert.False(File.Exists(missing));
            Assert.Null(SavedFile.Zone(missing));
        }

        public void Dispose()
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
}
