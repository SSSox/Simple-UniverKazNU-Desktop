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

        // The grade summary is made by the app itself, not received from the internet.
        [Fact]
        public void Keep_WithoutAnOriginLeavesTheFileUnmarked()
        {
            File.WriteAllText(Path.Combine(root, "Успеваемость 2026-11-18.png"), "older");
            var saved = SavedFile.Keep(Part("picture"), root, "Успеваемость 2026-11-18.png", null);
            Assert.Equal(Path.Combine(root, "Успеваемость 2026-11-18 (1).png"), saved);
            Assert.Null(SavedFile.Zone(saved));
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
        public void Keep_MarksTheStagedFileBeforeAttemptingTheFinalName()
        {
            var temp = Part("lecture");
            Directory.CreateDirectory(Path.Combine(root, "lecture.pdf"));
            Assert.Throws<IOException>(() => SavedFile.Keep(temp, root, "lecture.pdf", Site));
            Assert.True(File.Exists(temp));
            Assert.Contains("ZoneId=3", SavedFile.Zone(temp) ?? "");
        }

        [Fact]
        public void Keep_FailedMarkDoesNotPublishTheFinalName()
        {
            var temp = Part("lecture");
            File.SetAttributes(temp, FileAttributes.ReadOnly);
            try
            {
                Assert.Throws<IOException>(() => SavedFile.Keep(temp, root, "lecture.pdf", Site));
                Assert.True(File.Exists(temp));
                Assert.False(File.Exists(Path.Combine(root, "lecture.pdf")));
            }
            finally
            {
                foreach (var path in Directory.GetFiles(root)) File.SetAttributes(path, FileAttributes.Normal);
            }
        }

        [Fact]
        public void Keep_VolumeWithoutStreamsStillGetsTheFile()
        {
            var temp = Part("lecture");
            File.SetAttributes(temp, FileAttributes.ReadOnly);
            try
            {
                Assert.False(SavedFile.Mark(temp, Site));
                // Simulate the volume capability reported by FAT without requiring a second drive.
                var saved = SavedFile.Keep(temp, root, "lecture.pdf", Site, path =>
                {
                    Assert.Equal(temp, path);
                    Assert.False(File.Exists(Path.Combine(root, "lecture.pdf")));
                    return true;
                });
                Assert.Equal("lecture", File.ReadAllText(saved));
                Assert.Null(SavedFile.Zone(saved));
                Assert.False(File.Exists(temp));
            }
            finally
            {
                foreach (var path in Directory.GetFiles(root)) File.SetAttributes(path, FileAttributes.Normal);
            }
        }

        [Theory]
        [InlineData("header")]
        [InlineData("hint")]
        [InlineData("url")]
        public void Keep_TemporaryLookingDownloadNameSurvivesCleanup(string source)
        {
            var suggested = PartialDownloads.NewName();
            var name = FileNames.Resolve(source == "header" ? "attachment; filename=\"" + suggested + "\"" : null,
                source == "hint" ? suggested : null, "https://univer.kaznu.kz/" + suggested,
                "application/octet-stream", new byte[0], 0);
            var saved = SavedFile.Keep(Part("finished"), root, name, null);
            var abandoned = Path.Combine(root, PartialDownloads.NewName());
            File.WriteAllText(abandoned, "unfinished");
            File.SetLastWriteTimeUtc(saved, DateTime.UtcNow.AddHours(-1));
            File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-1));
            Assert.Equal(1, PartialDownloads.CleanUp(root, TimeSpan.FromMinutes(1)));
            Assert.Equal("finished", File.ReadAllText(saved));
            Assert.False(File.Exists(abandoned));
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
