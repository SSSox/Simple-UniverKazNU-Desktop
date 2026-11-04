using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ShortcutsTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        private const string Messages = "https://univer.kaznu.kz/my/messages/index/";
        [Fact]
        public void TileOrder_PersistsWithPins_ClonesIndependently_AndRejectsMalformedRequests()
        {
            var order = Shortcuts.ApplyOrder("{\"order\":[\"" + Messages + "\",\"https://univer.kaznu.kz/student/myschedule/\"]}");
            Assert.Equal(Messages, order[0]);
            var storage = new Storage(root); var prefs = storage.LoadPreferences(); prefs.TileOrder = order;
            storage.SavePreferences(prefs); var loaded = storage.LoadPreferences(); Assert.Equal(order, loaded.TileOrder);
            var clone = loaded.Clone(); clone.TileOrder.Clear(); Assert.Equal(2, loaded.TileOrder.Count);
            Assert.Null(Shortcuts.ApplyOrder("{\"order\":[\"javascript:alert(1)\"]}"));
            Assert.Null(Shortcuts.ApplyOrder("{\"order\":[\"https://univer.kaznu.kz/user/logout\"]}"));
            Assert.Null(Shortcuts.ApplyOrder("{\"order\":[\"" + Messages + "\",\"" + Messages + "\"]}"));
            Assert.Null(Shortcuts.ApplyOrder("{\"order\":\"bad\"}"));
        }
        [Theory]
        [InlineData("https://univer.kaznu.kz/Content/instructions/Files/guide.docx", true)]
        [InlineData("https://univer.kaznu.kz/Content/instructions/Files/guide.PDF?download=1", true)]
        [InlineData("http://univer.kaznu.kz/guide.docx", false)]
        [InlineData("https://univer.kaznu.kz.evil/guide.docx", false)]
        [InlineData("https://univer.kaznu.kz/guide.html", false)]
        [InlineData("https://univer.kaznu.kz/guide.exe", false)]
        [InlineData("https://user@univer.kaznu.kz/guide.docx", false)]
        public void DocumentLinks_AreDownloadsOnlyOnTheExactHttpsOrigin(string url, bool expected)
        { Assert.Equal(expected, SitePolicy.DocumentDownload(url)); }
        [Fact]
        public void Pin_AddsSectionsAndUsefulLinks_AndUnpinRemovesThem()
        {
            var pins = Shortcuts.Apply(new List<PinnedLink>(), "{\"pin\":true,\"title\":\"  Сообщения \\n\",\"url\":\"" + Messages + "\"}");
            pins = Shortcuts.Apply(pins, "{\"pin\":true,\"title\":\"Библиотека\",\"url\":\"https://lib.kaznu.kz/\"}");
            Assert.Equal(new[] { "Сообщения", "Библиотека" }, pins.Select(p => p.Title));
            Assert.Equal(2, Shortcuts.Apply(pins, "{\"pin\":true,\"title\":\"Again\",\"url\":\"" + Messages + "\"}").Count);
            pins = Shortcuts.Apply(pins, "{\"pin\":false,\"url\":\"" + Messages + "\"}");
            Assert.Equal("https://lib.kaznu.kz/", pins.Single().Url);
        }
        [Theory]
        [InlineData("/student/myschedule/")]
        [InlineData("/student/attendance/full")]
        [InlineData("/student/transcript/")]
        [InlineData("/student/umkd/")]
        [InlineData("/user/loginMoodle")]
        [InlineData("/ssc/")]
        [InlineData("/ssc/UserServiceList")]
        [InlineData("/ssc/StatementList/")]
        public void FixedTiles_CannotBePinnedAgain_AndOldDuplicatesAreRemoved(string path)
        {
            var url = SitePolicy.Origin + path;
            var old = new[] { new PinnedLink { Title = "Duplicate", Url = url }, new PinnedLink { Title = "Messages", Url = Messages } };
            Assert.Equal(Messages, Shortcuts.Normalize(old).Single().Url);
            var payload = new JavaScriptSerializer().Serialize(new { pin = true, title = "Duplicate", url });
            Assert.Equal(Messages, Shortcuts.Apply(old, payload).Single().Url);
            Assert.Contains(url, Shortcuts.NormalizeOrder(new[] { url }));
            Assert.False(Shortcuts.FixedHomeLink(url + "?view=other"));
            Assert.False(Shortcuts.FixedHomeLink("https://lib.kaznu.kz" + path));
        }
        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("file:///C:/Windows/notepad.exe")]
        [InlineData("http://univer.kaznu.kz/my/messages/")]
        [InlineData("https://univer.kaznu.kz:8443/my/")]
        [InlineData("https://user:pass@lib.kaznu.kz/")]
        [InlineData("https://univer.kaznu.kz/user/logout")]
        [InlineData("mailto:help@kaznu.kz")]
        [InlineData("/my/messages/")]
        public void Pin_RejectsUnsafeOrNonWebAddresses(string url)
        {
            Assert.Null(Shortcuts.Apply(new List<PinnedLink>(), "{\"pin\":true,\"title\":\"X\",\"url\":\"" + url + "\"}"));
        }
        [Theory]
        [InlineData("not json")]
        [InlineData("{\"pin\":\"yes\",\"title\":\"X\",\"url\":\"https://lib.kaznu.kz/\"}")]
        [InlineData("{\"pin\":true,\"title\":\"   \",\"url\":\"https://lib.kaznu.kz/\"}")]
        [InlineData("{\"pin\":true,\"title\":\"X\"}")]
        public void Pin_IgnoresMalformedMessages(string payload)
        {
            var result = Shortcuts.Apply(new List<PinnedLink>(), payload);
            Assert.True(result == null || result.Count == 0);
        }
        [Fact]
        public void Pin_StopsAtLimit_AndLongTitlesAreShortened()
        {
            var pins = Enumerable.Range(0, Shortcuts.Limit).Select(i => new PinnedLink { Title = "S" + i, Url = "https://univer.kaznu.kz/s/" + i }).ToList();
            Assert.Null(Shortcuts.Apply(pins, "{\"pin\":true,\"title\":\"X\",\"url\":\"https://lib.kaznu.kz/\"}"));
            Assert.Equal(120, Shortcuts.CleanTitle(new string('a', 300)).Length);
        }
        [Fact]
        public void Pins_PersistBetweenLaunches_AndInvalidStoredEntriesAreDropped()
        {
            var storage = new Storage(root); var prefs = storage.LoadPreferences();
            prefs.Pins = new List<PinnedLink> { new PinnedLink { Title = "Сообщения", Url = Messages }, new PinnedLink { Title = "Bad", Url = "javascript:x" }, null };
            storage.SavePreferences(prefs);
            var loaded = new Storage(root).LoadPreferences();
            Assert.Equal(Messages, loaded.Pins.Single().Url);
            var copy = loaded.Clone(); copy.Pins.Clear(); Assert.Single(loaded.Pins);
        }
        [Fact]
        public void PreferencesWithoutPins_LoadWithEmptyList()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "preferences.json"), "{\"Language\":\"ru\",\"Profile\":\"" + Guid.NewGuid().ToString("N") + "\"}");
            Assert.Empty(new Storage(root).LoadPreferences().Pins);
        }
        [Fact]
        public void PageData_IsEscapedJson()
        {
            var json = Shortcuts.ToJson(new[] { new PinnedLink { Title = "</script><b>", Url = Messages } });
            Assert.DoesNotContain("</script>", json); Assert.StartsWith("[{", json);
        }
        public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
