using System;
using System.IO;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // A repeated download is recognised by a hash of its address (the list stores no addresses), and Downloads can show
    // the files of one course.
    public class RepeatDownloadTests : IDisposable
    {
        private const string Address = "https://univer.kaznu.kz/student/umkd/get/101/202/2026/1";
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        public RepeatDownloadTests() { Directory.CreateDirectory(root); }
        private DownloadRecord Saved(string name, string key, string source = "История Казахстана", DownloadState state = DownloadState.Completed)
        {
            var path = Path.Combine(root, name); File.WriteAllText(path, "x");
            return new DownloadRecord { FileName = name, Path = path, Key = key, Source = source, State = state, Finished = DateTime.Now };
        }
        [Fact]
        public void Key_IsAShortHashOfTheAddressWithoutItsFragment()
        {
            var key = DownloadKeys.Of(Address);
            Assert.Matches("^[0-9a-f]{32}$", key);
            Assert.Equal(key, DownloadKeys.Of(Address + "#page=2"));
            Assert.NotEqual(key, DownloadKeys.Of("https://univer.kaznu.kz/student/umkd/get/101/203/2026/1"));
            Assert.DoesNotContain("umkd", key);
            Assert.Equal("", DownloadKeys.Of(null));
        }
        [Fact]
        public void Downloaded_FindsTheSameAddressWhileTheFileIsOnDisk()
        {
            var history = new DownloadHistory(root);
            var earlier = Saved("Силлабус.pdf", DownloadKeys.Of(Address));
            history.Upsert(earlier);
            history.Upsert(Saved("Другой.pdf", DownloadKeys.Of(Address + "/other")));
            Assert.Same(earlier, history.Downloaded(DownloadKeys.Of(Address), "Силлабус", "История Казахстана"));
            // the address decides, not the caption
            Assert.Same(earlier, history.Downloaded(DownloadKeys.Of(Address), "Любая подпись", "Другой раздел"));
            File.Delete(earlier.Path);
            Assert.Null(history.Downloaded(DownloadKeys.Of(Address), "Силлабус", "История Казахстана"));
        }
        [Fact]
        public void Downloaded_IgnoresUnfinishedDownloadsAndOtherAddresses()
        {
            var history = new DownloadHistory(root);
            history.Upsert(Saved("Силлабус.pdf", DownloadKeys.Of(Address), state: DownloadState.Failed));
            history.Upsert(Saved("Лекция.pdf", DownloadKeys.Of(Address + "/2")));
            Assert.Null(history.Downloaded(DownloadKeys.Of(Address), "Силлабус", "История Казахстана"));
            // a keyed entry never matches by caption
            Assert.Null(history.Downloaded(DownloadKeys.Of(Address), "Лекция", "История Казахстана"));
        }
        [Fact]
        public void Downloaded_MatchesOlderEntriesByCaptionAndCourse()
        {
            var history = new DownloadHistory(root);
            var older = Saved("Силлабус (1).pdf", "");
            history.Upsert(older);
            Assert.Same(older, history.Downloaded(DownloadKeys.Of(Address), "Силлабус", "История Казахстана"));
            Assert.Same(older, history.Downloaded(DownloadKeys.Of(Address), "силлабус.PDF", "история казахстана"));
            // the same caption in another course is another file
            Assert.Null(history.Downloaded(DownloadKeys.Of(Address), "Силлабус", "Социология"));
            // no caption, no guess
            Assert.Null(history.Downloaded(DownloadKeys.Of(Address), "", "История Казахстана"));
            Assert.Null(history.Downloaded(DownloadKeys.Of(Address), "Силлабус 2", "История Казахстана"));
        }
        [Fact]
        public async System.Threading.Tasks.Task DownloadTimesSurviveTheListRoundTripAsTheSameInstant()
        {
            // JavaScriptSerializer writes a DateTime as an instant and reads it back as Kind=Utc; Load converts it to local
            // time for display. A record saved before midnight must not move to another day.
            var storage = root;
            var started = new DateTime(2026, 11, 1, 23, 58, 0, DateTimeKind.Local);
            var history = new DownloadHistory(storage);
            history.Upsert(new DownloadRecord { FileName = "Ночь.pdf", Path = Path.Combine(storage, "Ночь.pdf"), State = DownloadState.Completed, Started = started, Finished = started.AddMinutes(5) });
            await history.SaveAsync();
            var loaded = new DownloadHistory(storage); loaded.Load();
            var item = loaded.Items.Single();
            Assert.Equal(started.ToString("yyyy-MM-dd HH:mm"), item.Started.ToString("yyyy-MM-dd HH:mm"));
            Assert.Equal(started.AddMinutes(5).ToString("yyyy-MM-dd HH:mm"), item.Finished.Value.ToString("yyyy-MM-dd HH:mm"));
            Assert.True(item.Finished.Value >= item.Started);
        }
        [Fact]
        public async System.Threading.Tasks.Task Key_RoundTripsWithoutStoringTheAddress()
        {            var history = new DownloadHistory(root);
            history.Upsert(Saved("Силлабус.pdf", DownloadKeys.Of(Address)));
            await history.SaveAsync();
            var json = File.ReadAllText(Path.Combine(root, "downloads.json"));
            Assert.DoesNotContain("http", json); Assert.DoesNotContain("umkd", json);
            var loaded = new DownloadHistory(root); loaded.Load();
            Assert.Equal(DownloadKeys.Of(Address), loaded.Items.Single().Key);
            File.WriteAllText(Path.Combine(root, "downloads.json"), "[{\"Id\":\"a\",\"FileName\":\"old.pdf\",\"Key\":null,\"State\":1}]");
            loaded.Load(); Assert.Equal("", loaded.Items.Single().Key);
        }
        [Theory]
        [InlineData("Силлабус (2).pdf", "силлабус")]
        [InlineData("Лекция 1. Введение.docx", "лекция 1. введение")]
        [InlineData("Ёмкость", "емкость")]
        [InlineData("Отчёт.final.version", "отчет.final.version")]
        public void Bare_DropsTheRepeatSuffixAndExtension(string name, string expected) { Assert.Equal(expected, DownloadKeys.Bare(name)); }
        [Fact]
        public void Matches_FiltersByCourseIgnoringCase()
        {
            var item = new DownloadRecord { FileName = "Лекция.pdf", Source = " История Казахстана " };
            Assert.True(FileKinds.Matches(item, "", null, "история казахстана"));
            Assert.False(FileKinds.Matches(item, "", null, "Социология"));
            Assert.True(FileKinds.Matches(item, "лекция", FileKind.Pdf, null));
            Assert.False(FileKinds.Matches(new DownloadRecord { FileName = "x.pdf" }, "", null, "Социология"));
        }
        [Fact]
        public void RepeatedDownloadNotice_IsDismissableInTheDownloadChannel()
        {
            var message = UiStatus.Describe("downloadDuplicate");
            Assert.Equal(MessageAction.Downloads, message.Action); Assert.True(message.CanDismiss);
            var status = new UiStatus(); status.Update("downloadComplete"); status.Update("downloadDuplicate");
            Assert.Equal("downloadDuplicate", status.Current.Key);
            status.Update("downloading");
            Assert.False(status.Shows("downloadDuplicate"), "a new download replaces the question");
        }
        [Fact]
        public void RefusedStart_AnswersTheClickWithoutDisplacingRunningProgress()
        {
            // A repeat of a file already downloading starts nothing, so the click needs its own message.
            var busy = UiStatus.Describe("downloadBusy");
            Assert.True(busy.CanDismiss); Assert.Equal(MessageAction.None, busy.Action);
            var status = new UiStatus(); status.Update("downloading"); status.Update("downloadBusy");
            Assert.Equal("downloadBusy", status.Current.Key);
            Assert.True(status.Shows("downloading"), "the running download keeps its own channel and progress");
            status.Dismiss();
            Assert.Equal("downloading", status.Current.Key);
            // Polling the page must not erase the answer to the click.
            status.Update("downloadBusy"); status.Update("ready"); status.Update("loading");
            Assert.Equal("downloadBusy", status.Current.Key);
        }
        [Fact]
        public void OffOriginAndBusyStartsHaveNoDownloadRecord()
        {
            // Both keys mean nothing was published, so the list never gains a row stuck at 0%.
            foreach (var key in new[] { "offOrigin", "downloadBusy" })
            {
                var message = UiStatus.Describe(key);
                Assert.Equal(key, message.Key);
                Assert.True(message.CanDismiss);
            }
            var status = new UiStatus(); status.Update("downloadBusy"); Assert.Equal("downloadBusy", status.Current.Key);
        }
        [Fact]
        public void FailedExternalLinkSaysWindowsCouldNotOpenItRatherThanTheConnection()
        {
            var message = UiStatus.Describe("externalError");
            Assert.Equal(MessageTone.Warning, message.Tone);
            // Retry reloads the site, which does not help an outside link that Windows refused to open.
            Assert.Equal(MessageAction.None, message.Action);
            var status = new UiStatus(); status.Update("externalError"); status.Update("ready"); status.Update("loading");
            Assert.Equal("externalError", status.Current.Key);
            // A page failure is louder and keeps its own recovery action underneath.
            status.Update("networkError");
            Assert.Equal("networkError", status.Current.Key);
            // Navigating away clears the page failure but not the answer to the outside link: it stays until dismissed.
            status.BeginNavigation();
            Assert.Equal("externalError", status.Current.Key);
            status.Dismiss();
            Assert.Null(status.Current);
        }
        public void Dispose()
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }
}
