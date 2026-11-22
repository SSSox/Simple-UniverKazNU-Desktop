using System;
using System.IO;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // Downloads order and start page kept in preferences; the tray tooltip's next class.
    public class ConveniencesTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "univer-conveniences-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(root, true); } catch (Exception) { } }

        [Fact]
        public void DownloadsOrderAndStartPage_SurviveSaveCloneAndRestart()
        {
            var storage = new Storage(root);
            var prefs = storage.LoadPreferences();
            Assert.Equal("newest", prefs.DownloadsOrder); Assert.Equal("home", prefs.StartPage);
            prefs.DownloadsOrder = "size"; prefs.StartPage = "schedule";
            var copy = prefs.Clone();
            Assert.Equal("size", copy.DownloadsOrder); Assert.Equal("schedule", copy.StartPage);
            storage.SavePreferences(copy);
            var loaded = new Storage(root).LoadPreferences();
            Assert.Equal("size", loaded.DownloadsOrder); Assert.Equal("schedule", loaded.StartPage);
        }

        [Fact]
        public void UnknownValuesInTheFile_FallBackToDefaults()
        {
            var storage = new Storage(root);
            var prefs = storage.LoadPreferences(); storage.SavePreferences(prefs);
            var path = Path.Combine(root, "preferences.json");
            var text = File.ReadAllText(path).Replace("\"DownloadsOrder\":\"newest\"", "\"DownloadsOrder\":\"random\"").Replace("\"StartPage\":\"home\"", "\"StartPage\":\"https://evil.example/\"");
            File.WriteAllText(path, text);
            var loaded = new Storage(root).LoadPreferences();
            Assert.Equal("newest", loaded.DownloadsOrder); Assert.Equal("home", loaded.StartPage);
        }

        [Theory]
        [InlineData("newest")] [InlineData("name")] [InlineData("size")]
        public void DownloadOrderWords_RoundTrip(string word) { Assert.Equal(word, DownloadOrder.Name(DownloadOrder.Parse(word))); Assert.NotEqual(DownloadOrder.Parse("newest") == DownloadOrder.Parse(word), word != "newest"); }

        [Fact]
        public void StartPages_AreSiteSectionsThatResumeAfterSignIn()
        {
            Assert.Equal(SitePolicy.Home, StartPages.Address("home"));
            Assert.Equal(SitePolicy.Origin + "/student/myschedule/", StartPages.Address("schedule"));
            Assert.Equal(SitePolicy.Home, StartPages.Address("../user/logout"));
            Assert.Equal(SitePolicy.Home, StartPages.Address(null));
            foreach (var key in StartPages.Keys.Where(k => k != "home"))
            {
                Assert.True(SitePolicy.Trusted(StartPages.Address(key)), key);
                Assert.True(SitePolicy.Resumable(StartPages.Address(key)), key);
                Assert.NotEqual(key, Univer.Desktop.UI.Strings.Get(key, "ru"));
            }
        }

        private static readonly DateTime Monday = new DateTime(2026, 11, 9);
        private static ScheduleWeek Week(params ScheduleClass[] classes) { return new ScheduleWeek { Start = Monday, End = Monday.AddDays(7), Classes = classes.ToList() }; }
        private static ScheduleClass At(int day, int hour, string name)
        { var start = Monday.AddDays(day).AddHours(hour); return new ScheduleClass { Start = start, End = start.AddMinutes(50), Name = name, Room = "305", Type = "", Teacher = "" }; }

        [Fact]
        public void TrayText_NamesTheNextClassThatHasNotBegun()
        {
            var week = Week(At(0, 9, "Политология"), At(0, 11, "Философия"), At(1, 9, "История"), At(3, 13, "Экономика"));
            Assert.Equal("Univer · КазНУ\nСегодня, 11:00 · Философия", ClassReminders.TrayText(week, Monday.AddHours(9).AddMinutes(5), "ru", false));
            Assert.Equal("Univer · КазНУ\nЗавтра, 09:00 · История", ClassReminders.TrayText(week, Monday.AddHours(12), "ru", false));
            Assert.Equal("Univer · KazNU\nThursday, 13:00 · Экономика", ClassReminders.TrayText(week, Monday.AddDays(1).AddHours(10), "en", false));
            Assert.Equal("Univer · ҚазҰУ\nБүгін, 11:00 · сабақ", ClassReminders.TrayText(week, Monday.AddHours(10), "kz", true));
            // Nothing left this week, another week's copy, no copy: the name alone.
            Assert.Equal("Univer · КазНУ", ClassReminders.TrayText(week, Monday.AddDays(4), "ru", false));
            Assert.Equal("Univer · КазНУ", ClassReminders.TrayText(week, Monday.AddDays(8), "ru", false));
            Assert.Equal("Univer · КазНУ", ClassReminders.TrayText(null, Monday, "ru", false));
        }

        [Fact]
        public void TrayText_HiddenNeverNamesTheClass_AndLongNamesFitWindows()
        {
            var name = "Очень длинное название дисциплины, которое не помещается в подсказку значка";
            var week = Week(At(2, 9, name));
            foreach (var lang in new[] { "ru", "kz", "en" })
            {
                var text = ClassReminders.TrayText(week, Monday, lang, false);
                Assert.True(text.Length <= ClassReminders.TrayTextLimit, text);
                Assert.EndsWith("…", text);
                Assert.DoesNotContain("Очень", ClassReminders.TrayText(week, Monday, lang, true));
            }
        }
    }
}
