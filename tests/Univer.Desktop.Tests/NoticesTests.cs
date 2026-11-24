using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // The site's notification marks as notices.js reports them. Menu markup follows the live account read on
    // 2 November 2026 (Android): "Анкетирование студентов" with a red "(2)" next to the link to /student/rating/.
    public class NoticesTests : IDisposable
    {
        private const string Rating = SitePolicy.Origin + "/student/rating/";
        private readonly string root = Path.Combine(Path.GetTempPath(), "Univer.Desktop.UnitTests", Guid.NewGuid().ToString("N"));
        private static NoticeScan Scan(string page, int count, int open = -1, bool menu = true, params NoticeDetail[] details)
        {
            var scan = new NoticeScan { Page = page, Menu = menu };
            scan.Links.Add(new NoticeLink { Url = Rating, Title = "Анкетирование студентов", Count = count, Open = open, Menu = true, Details = details.ToList() });
            scan.Links.Add(new NoticeLink { Url = SitePolicy.Origin + "/student/umkd/", Title = "УМКД", Menu = true });
            return scan;
        }
        private static NoticeDetail Detail(string title, string until) { return new NoticeDetail { Title = title, Until = until }; }
        [Fact]
        public void AnsweredQuestionnaires_AreNotShown_ThoughTheSiteKeepsItsCount()
        {
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/bachelor/", 2), 1);
            Assert.Equal(1, Notices.Unread(list));
            // The list page is read: both rows carry "+", so nothing is open.
            list = Notices.Merge(list, Scan("/student/rating/", 2, open: 0), 2);
            Assert.Empty(Notices.Showing(list));
            Assert.Equal(0, Notices.Unread(list));
            // Every later page still shows "(2)" in the menu, without the list: the answer stays known.
            list = Notices.Merge(list, Scan("/student/attendance/full/", 2), 3);
            Assert.Empty(Notices.Showing(list));
            Assert.Empty(Notices.NewlyUnread(list, Notices.Merge(list, Scan("/student/umkd/", 2), 4)));
        }
        [Fact]
        public void PartlyAnsweredList_ShowsOnlyTheOpenQuestionnaires()
        {
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/rating/", 2, 1, true, Detail("Восприятие коррупции - 2026, осенний сем.", "11.11.2026")), 1);
            var notice = Notices.Showing(list).Single();
            Assert.Equal(1, Notices.Shown(notice));
            Assert.Equal("Восприятие коррупции - 2026, осенний сем.", notice.Details.Single().Title);
            // A list without answer marks never raises the number above the site's own count.
            list = Notices.Merge(list, Scan("/student/rating/", 2, 7), 2);
            Assert.Equal(2, Notices.Shown(Notices.Showing(list).Single()));
        }
        [Fact]
        public void NewQuestionnaire_AfterAnsweredOnes_IsShownAndAnnounced()
        {
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/rating/", 2, 0), 1);
            var next = Notices.Merge(list, Scan("/student/bachelor/", 3), 2);
            // The list said nothing about the third one yet: the site's number counts until it is read again.
            Assert.Equal(3, Notices.Shown(Notices.Showing(next).Single()));
            Assert.Single(Notices.NewlyUnread(list, next));
        }
        [Fact]
        public void ReadNotice_StaysRead_WhenItDisappearsAndComesBack()
        {
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/bachelor/", 2), 1);
            list = Notices.MarkRead(list, list.Single(n => n.Kind == SiteNotice.Counter).Key);
            Assert.Equal(0, Notices.Unread(list));
            // The same section shows the link without a count, then with it again.
            var gone = Notices.Merge(list, Scan("/student/attendance/full/", 0), 2);
            Assert.Empty(Notices.Showing(gone));
            var back = Notices.Merge(gone, Scan("/student/bachelor/", 2), 3);
            Assert.True(Notices.Showing(back).Single().Read);
            Assert.Empty(Notices.NewlyUnread(gone, back));
            // Only a larger number makes it unread again.
            var grown = Notices.Merge(back, Scan("/student/bachelor/", 3), 4);
            Assert.False(Notices.Showing(grown).Single().Read);
            Assert.Single(Notices.NewlyUnread(back, grown));
        }
        [Fact]
        public void OtherSectionsMenu_WithoutTheCount_DoesNotHideIt()
        {
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/bachelor/", 2), 1);
            var services = Notices.Merge(list, Scan("/ssc/", 0), 2);
            Assert.Single(Notices.Showing(services));
            // A complete menu of another section that does not list the link at all says nothing either.
            var other = new NoticeScan { Page = "/ssc/", Menu = true };
            other.Links.Add(new NoticeLink { Url = SitePolicy.Origin + "/ssc/request/", Title = "Заявки", Menu = true });
            Assert.Single(Notices.Showing(Notices.Merge(services, other, 3)));
        }
        [Fact]
        public void BlockedMessage_GoneAndBack_KeepsItsReadMark()
        {
            var scan = new NoticeScan { Page = "/student/attestation/" };
            scan.Messages.Add(new NoticeMessage { Title = "Аттестация", Text = "Для просмотра успеваемости необходимо пройти анкетирование.", Url = Rating });
            var list = Notices.Merge(new List<SiteNotice>(), scan, 1);
            Assert.Equal(1, Notices.Unread(list));
            list = Notices.MarkAllRead(list);
            var without = Notices.Merge(list, new NoticeScan { Page = "/student/attestation/" }, 2);
            Assert.Empty(Notices.Showing(without));
            var again = Notices.Merge(without, scan, 3);
            Assert.True(Notices.Showing(again).Single().Read);
            Assert.Empty(Notices.NewlyUnread(without, again));
        }
        [Fact]
        public void GoneItems_AreForgottenAfterTheKeepPeriod()
        {
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/bachelor/", 2), 1);
            list = Notices.Merge(list, Scan("/student/bachelor/", 0), 2);
            Assert.Single(list);
            Assert.Empty(Notices.Ordered(list, 2 + Notices.KeepGone));
        }
        [Fact]
        public void Parse_AcceptsOnlyBoundedTrustedData_OfTheCurrentGeneration()
        {
            const string json = "{\"v\":1,\"g\":\"n3\",\"kind\":\"page\",\"page\":\"/student/rating/\",\"menu\":true,\"fetching\":true,\"links\":[" +
                "{\"u\":\"/student/rating/\",\"t\":\"Анкетирование студентов\",\"n\":2,\"m\":1,\"o\":0,\"d\":[{\"t\":\"Анкета\",\"e\":\"30.11.2026\"},{\"t\":\"Вторая\",\"e\":\"скоро\"}]}," +
                "{\"u\":\"//evil.example/\",\"t\":\"x\",\"n\":5},{\"u\":\"/user/logout\",\"t\":\"Выход\",\"n\":1},{\"u\":\"/files/a.pdf\",\"t\":\"PDF\",\"n\":1}]," +
                "\"blocks\":[{\"t\":\"Аттестация\",\"x\":\"Для просмотра успеваемости необходимо пройти анкетирование.\",\"l\":\"https://evil.example/\"}]}";
            Assert.Null(Notices.Parse(json, false, "n4"));
            Assert.Null(Notices.Parse(json, true, "n3"));
            Assert.Null(Notices.Parse("{not json", false, "n3"));
            var scan = Notices.Parse(json, false, "n3");
            var link = scan.Links.Single();
            Assert.Equal(Rating, link.Url); Assert.Equal(2, link.Count); Assert.Equal(0, link.Open); Assert.True(link.Menu);
            Assert.Equal(new[] { "30.11.2026", "" }, link.Details.Select(d => d.Until));
            Assert.True(scan.Menu); Assert.True(scan.Fetching);
            // A message link off the site falls back to the page itself.
            Assert.Equal(Rating, scan.Messages.Single().Url);
        }
        [Fact]
        public void ParseDone_ReadsOnlyTheLastMessageOfThisGeneration()
        {
            int read, failed;
            Assert.True(Notices.ParseDone("{\"v\":1,\"g\":\"n2\",\"kind\":\"done\",\"page\":\"/\",\"read\":2,\"failed\":1}", "n2", out read, out failed));
            Assert.Equal(new[] { 2, 1 }, new[] { read, failed });
            Assert.False(Notices.ParseDone("{\"v\":1,\"g\":\"n1\",\"kind\":\"done\",\"read\":2,\"failed\":0}", "n2", out read, out failed));
            Assert.False(Notices.ParseDone("{\"v\":1,\"g\":\"n2\",\"kind\":\"section\",\"page\":\"/\"}", "n2", out read, out failed));
            Assert.False(Notices.ParseDone("not json", "n2", out read, out failed));
        }
        [Fact]
        public void Section_IsTheFirstPathSegment()
        {
            Assert.Equal("/student/", Notices.Section("/student/rating/"));
            Assert.Equal("/ssc/", Notices.Section("/ssc/"));
            Assert.Equal("/ssc/", Notices.Section("/ssc?x=1"));
            Assert.Equal("/", Notices.Section("/"));
        }
        [Fact]
        public async Task Storage_RoundTripsTheList_AndDeletesTheFileForAnEmptyOne()
        {
            var storage = new Storage(root);
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/rating/", 2, 1, true, Detail("Анкета", "30.11.2026")), Notices.Now());
            var account = ScheduleWeek.AccountMark("first student");
            await storage.SaveNoticesAsync(account, list);
            var loaded = new Storage(root).LoadNotices(account);
            Assert.Equal(1, Notices.Shown(Notices.Showing(loaded).Single()));
            Assert.Equal("30.11.2026", loaded.Single().Details.Single().Until);
            File.WriteAllText(Path.Combine(root, "notices.json"), "{\"Account\":\"" + account + "\",\"Notices\":[{\"Key\":\"counter:x\",\"Kind\":\"counter\",\"Url\":\"https://evil.example/\",\"Count\":3}]}");
            Assert.Empty(storage.LoadNotices(account));
            File.WriteAllText(Path.Combine(root, "notices.json"), "{broken");
            Assert.Empty(storage.LoadNotices(account));
            await storage.SaveNoticesAsync(account, new List<SiteNotice>());
            Assert.False(File.Exists(Path.Combine(root, "notices.json")));
        }
        [Fact]
        public async Task Storage_NoticesBelongOnlyToTheAccountThatSavedThem()
        {
            var storage = new Storage(root);
            var account = ScheduleWeek.AccountMark("first student");
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/rating/", 2), Notices.Now());
            await storage.SaveNoticesAsync(account, list);

            var next = new Storage(root);
            Assert.Single(next.LoadNotices(account));
            Assert.Empty(next.LoadNotices(ScheduleWeek.AccountMark("second student")));
            Assert.Empty(next.LoadNotices(""));
            Assert.Empty(next.LoadNotices(account, true));
        }
        [Fact]
        public void Storage_UnboundLegacyNoticesAreNotAssignedToTheCurrentAccount()
        {
            var storage = new Storage(root);
            var list = Notices.Merge(new List<SiteNotice>(), Scan("/student/rating/", 2), Notices.Now());
            File.WriteAllText(Path.Combine(root, "notices.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(list));
            Assert.Empty(storage.LoadNotices(ScheduleWeek.AccountMark("first student")));
        }
        [Fact]
        public async Task Storage_LockedNoticesStayPendingAcrossAccountSwitchAndRestarts()
        {
            var storage = new Storage(root);
            var account = ScheduleWeek.AccountMark("first student");
            await storage.SaveNoticesAsync(account, Notices.Merge(new List<SiteNotice>(), Scan("/student/rating/", 2), Notices.Now()));
            storage.SavePreferences(new Preferences { SessionClearPending = true, AutoLoginPaused = true });
            var path = Path.Combine(root, "notices.json");
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var prefs = storage.LoadPreferences();
                Assert.True(storage.FinishSessionClear(prefs));
                Assert.True(prefs.NoticesClearPending);
                Assert.True(new Storage(root).LoadPreferences().NoticesClearPending);

                // A new sign-in may resume before another program releases the old cache.
                prefs.AutoLoginPaused = false; await storage.SavePreferencesAsync(prefs);
                var next = new Storage(root); prefs = next.LoadPreferences();
                Assert.True(next.FinishSessionClear(prefs));
                Assert.True(prefs.NoticesClearPending);
                Assert.False(prefs.AutoLoginPaused);
                Assert.Empty(next.LoadNotices(ScheduleWeek.AccountMark("second student"), prefs.NoticesClearPending));
                Assert.Empty(next.LoadNotices(account, prefs.NoticesClearPending));
            }

            var restarted = new Storage(root); var clean = restarted.LoadPreferences();
            Assert.True(restarted.FinishSessionClear(clean));
            Assert.False(File.Exists(path));
            Assert.False(restarted.LoadPreferences().NoticesClearPending);
            Assert.False(restarted.FinishSessionClear(clean));
        }
        [Fact]
        public async Task StalePreferencesSave_KeepsAPendingNoticeClearUntilTheFileIsGone()
        {
            var storage = new Storage(root);
            var account = ScheduleWeek.AccountMark("first student");
            await storage.SaveNoticesAsync(account, Notices.Merge(new List<SiteNotice>(), Scan("/student/rating/", 2), Notices.Now()));
            var pending = storage.LoadPreferences();
            pending.NoticesClearPending = true;
            storage.SavePreferences(pending);

            var stale = storage.LoadPreferences().Clone();
            stale.NoticesClearPending = false;
            stale.Language = "en";
            await storage.SavePreferencesAsync(stale);

            var saved = new Storage(root).LoadPreferences();
            Assert.True(saved.NoticesClearPending);
            Assert.Equal("en", saved.Language);
            Assert.Single(storage.LoadNotices(account, false));
            Assert.Empty(storage.LoadNotices(account, saved.NoticesClearPending));

            File.Delete(Path.Combine(root, "notices.json"));
            stale.Language = "kz";
            stale.NoticesClearPending = false;
            await storage.SavePreferencesAsync(stale);
            var cleared = new Storage(root).LoadPreferences();
            Assert.False(cleared.NoticesClearPending);
            Assert.Equal("kz", cleared.Language);
        }
        public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
