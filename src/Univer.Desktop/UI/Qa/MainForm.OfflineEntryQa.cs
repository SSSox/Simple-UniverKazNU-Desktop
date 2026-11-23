using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Synthetic coming exams: an exam tomorrow morning, a midterm and an exam in three days; the exam page read yesterday
        // evening, the midterm page three days ago. Program uses it for --qa-offline too.
        internal static ExamPlan OfflineExamsForQa(DateTime now, string account)
        {
            var day = now.Date;
            var items = new[]
            {
                new ExamItem { Kind = ExamKind.Exam, Subject = "QA Микроэкономика", Start = day.AddDays(1).AddHours(9), Place = "215" },
                new ExamItem { Kind = ExamKind.Midterm, Subject = "QA Философия", Start = day.AddDays(3).AddHours(13), Place = "" },
                new ExamItem { Kind = ExamKind.Exam, Subject = "QA Статистика и анализ данных в социальных науках", Start = day.AddDays(3).AddHours(15).AddMinutes(30), Place = "Корпус 4, 101" },
            };
            var examAt = day.AddDays(-1).AddHours(18).AddMinutes(30); var midtermAt = day.AddDays(-3).AddHours(12).AddMinutes(10);
            return new ExamPlan(account, examAt, items, null, examAt, midtermAt);
        }
        // Offline entry: while the site waits (here its sign-in page, served by a fixture into a cut network) the offer comes
        // after OfflineEntry.WaitLimit (the clock is moved, not waited for), at once for a failed load, and only with a copy;
        // its button opens the copies without a single request; the screen has the week, the exams and the totals with their
        // saved times and its own buttons; it hides behind the PIN and comes back after it; signed-in content takes it away;
        // a closed offer stays closed until the trouble ends.
        private async Task OfflineEntryQaAsync(string qa, List<string> results)
        {
            var previousWeek = scheduleWeek; var previousGrades = lastGrades; var previousExams = examPlan; var previousFactory = main.ResourceRequestHandlerFactory;
            var previousLanguage = prefs.Language; var now = DateTime.Now;
            var before = await ProxyForQaAsync();
            Check(await SetProxyForQaAsync(ProxyAt(9)), "the offline entry step cuts this profile's network first", results);
            var fixtures = new OnlineTestQaFactory(null);
            main.ResourceRequestHandlerFactory = fixtures;
            var login = SitePolicy.Origin + "/user/login";
            fixtures.Pages[login] = "<!doctype html><html><head><meta charset='utf-8'><title>login</title></head><body><div id='l4'>QA sign-in page</div></body></html>";
            try
            {
                offlineSignsInForQa = true; offlineShiftForQa = 0; offlineEntry.Reset(); UpdateOfflineOffer();
                scheduleWeek = OfflineWeekForQa(now, ScheduleAccount); lastGrades = OfflineGradesForQa(now, ScheduleAccount); examPlan = OfflineExamsForQa(now, ScheduleAccount);
                main.Load(login);
                var opened = await UntilForQa(() => main.Address == login && !main.IsLoading && fixtures.Served(login) > 0, 10000);
                error = false;
                WatchOffline();
                Check(opened && !offlineOffered && !messages.Shows("offlineOffer"), "a sign-in page that has just opened offers nothing yet", results);
                offlineShiftForQa += OfflineEntry.WaitLimit + 1000; WatchOffline(); RenderFeedback();
                Check(offlineOffered && messages.Current?.Key == "offlineOffer" && feedback.Visible && feedback.Primary.Visible && feedback.Primary.Text == Strings.Get("offlineOpen", "ru") && feedback.Primary.AccessibleName == feedback.Primary.Text,
                    "a sign-in that waits longer than the limit offers «Открыть офлайн» in the banner", results);
                foreach (var lang in new[] { "ru", "kz", "en" })
                {
                    prefs.Language = lang; Localize(); await Task.Delay(60);
                    Check(feedback.Primary.Text == Strings.Get("offlineOpen", lang) && feedback.Primary.Right <= feedback.Width, "the offer's button reads and fits in " + lang, results);
                    SaveBitmap(feedback, Path.Combine(qa, "offline-offer-" + lang + ".png"));
                }
                prefs.Language = "ru"; Localize();
                // A problem banner above it (a failed load) carries the same button.
                messages.Update("httpError"); failureCode = "503"; RenderFeedback();
                Check(messages.Current?.Key == "httpError" && feedback.Offline.Visible && feedback.Offline.Text == Strings.Get("offlineOpen", "ru"), "a problem banner above the offer carries «Открыть офлайн» beside Retry", results);
                SaveBitmap(feedback, Path.Combine(qa, "offline-offer-error.png"));
                messages.Withdraw("httpError"); RenderFeedback();
                // The button opens the copies; nothing is asked of the site.
                var served = fixtures.Served(login); var asked = recoveryRequests; var reloads = recoveries; var address = main.Address;
                await FeedbackPrimaryAsync(); await Task.Delay(200);
                var words = offlineView.WordsForQa;
                Check(OfflineShown && OfflineOnTopForQa && offlineView.TitleForQa == Strings.Get("offlineSavedTitle", "ru") && offlineView.BackShownForQa && offlineView.RetryTextForQa == Strings.Get("offlineRetry", "ru") && offlineView.BarFitsForQa,
                    "the offer opens the saved data with «Повторить вход» and «Вернуться на сайт»", results);
                Check(fixtures.Served(login) == served && recoveryRequests == asked && recoveries == reloads && main.Address == address && !main.IsLoading, "opening the saved data sends no request and repeats nothing", results);
                Check(!offlineOffered && !messages.Shows("offlineOffer"), "the offer leaves once the saved data is open", results);
                Check(offlineView.FocusedForQa, "the keyboard goes to the saved data, which scrolls by keys", results);
                Check(words.Contains(OfflineWeek.Saved(scheduleWeek.SavedAt, now, "ru")) && words.Contains(OfflineWeek.Saved(lastGrades.SavedAt, now, "ru")) && words.Contains(Strings.Get("offlineExams", "ru")) &&
                    words.Contains(OfflineWeek.ExamSaved(examPlan, ExamKind.Exam, true, now, "ru")) && words.Contains(OfflineWeek.ExamSaved(examPlan, ExamKind.Midterm, true, now, "ru")) &&
                    words.Contains("09:00 QA Микроэкономика · Экзамен · ауд. 215") && words.Contains("13:00 QA Философия · Рубежный контроль") && offlineView.ExamDaysForQa == 2 && offlineView.ExamsBetweenForQa,
                    "the week, the coming exams and the last grades each say when they were saved; exams stand between the week and the grades", results);
                Check(offlineView.PaintedForQa && offlineView.TitlesDrawnForQa && offlineView.FitsForQa, "the saved data is painted, with every card's heading", results);
                foreach (var lang in new[] { "ru", "kz", "en" })
                {
                    prefs.Language = lang; Localize(); await Task.Delay(80);
                    Check(offlineView.TitleForQa == Strings.Get("offlineSavedTitle", lang) && offlineView.FitsForQa && offlineView.BarFitsForQa && offlineView.PaintedForQa, "the saved data reads and fits in " + lang, results);
                    SaveBitmap(offlineView, Path.Combine(qa, "offline-entry-" + lang + ".png"));
                }
                prefs.Language = "ru"; Localize();
                offlineView.PressForQa(System.Windows.Forms.Keys.End); await Task.Delay(60);
                SaveBitmap(offlineView, Path.Combine(qa, "offline-entry-end.png"));
                offlineView.PressForQa(System.Windows.Forms.Keys.Home);
                var themeBefore = prefs.Theme; prefs.Theme = ThemeChoice.Dark; ApplyTheme();
                try { await Task.Delay(150); SaveBitmap(offlineView, Path.Combine(qa, "offline-entry-dark.png")); Check(offlineView.TitlesDrawnForQa && offlineView.PaintedForQa, "the saved data is painted in the dark theme too", results); }
                finally { prefs.Theme = themeBefore; ApplyTheme(false); }
                SetZoom(150); await Task.Delay(150);
                Check(offlineView.FitsForQa && offlineView.BarFitsForQa && offlineView.PaintedForQa, "the saved data and its buttons fit at 150%", results);
                SaveBitmap(offlineView, Path.Combine(qa, "offline-entry-150.png"));
                SetZoom(100);
                // The site's own navigations under it (a sign-in's reload) leave it in place.
                main.Load(login); await UntilForQa(() => !main.IsLoading && fixtures.Served(login) > served, 8000);
                Check(OfflineShown && OfflineOnTopForQa, "sign-in's own navigations go on under the saved data", results);
                // Behind the PIN nothing of it shows; the unlock brings it back.
                locked = true; OfflineLockChanged(); WatchOffline();
                var hiddenLocked = !OfflineShown && !offlineOffered;
                offlineShiftForQa += OfflineEntry.WaitLimit + 1000; WatchOffline();
                hiddenLocked &= !OfflineShown && !offlineOffered;
                locked = false; OfflineLockChanged();
                Check(hiddenLocked && OfflineShown, "behind the PIN neither the saved data nor the offer shows; the unlock brings the saved data back", results);
                // «Вернуться на сайт» only hides it; the offer returns while the wait goes on.
                served = fixtures.Served(login);
                offlineView.PressBackForQa();
                Check(!OfflineShown && fixtures.Served(login) == served, "«Вернуться на сайт» shows the site again and sends nothing", results);
                WatchOffline(); var early = offlineOffered;
                offlineShiftForQa += OfflineEntry.WaitLimit + 1000; WatchOffline();
                Check(!early && offlineOffered, "after «Вернуться на сайт» the offer returns only once the wait goes on past the limit", results);
                // Signed-in content takes the opened screen away by itself, with no navigation.
                OpenOffline();
                Check(OfflineShown, "the offer opens the saved data again", results);
                address = main.Address; served = fixtures.Served(login);
                OfflineSiteWorks();
                Check(!OfflineShown && !offlineOffered && main.Address == address && fixtures.Served(login) == served, "signed-in content of the site takes the saved data away by itself", results);
                // A failed load offers at once; a closed offer stays closed until the trouble ends.
                error = true; failureCode = "503"; WatchOffline(); RenderFeedback();
                Check(offlineOffered && feedback.Visible, "a failed load the app retries offers the saved data at once", results);
                messages.Update("httpError"); RenderFeedback(); messages.Withdraw("httpError"); RenderFeedback();
                DismissFeedback(); WatchOffline();
                Check(!offlineOffered && !messages.Shows("offlineOffer"), "a closed offer stays closed while the same trouble goes on", results);
                // An automatic retry: RunRecovery clears the error and sends the reload before its navigation begins. That moment
                // is still the same trouble, so the closed offer does not come back when the reload fails again.
                error = false; recoveryLoading = true; WatchOffline();
                var duringRetry = !offlineOffered;
                recoveryLoading = false; error = true; WatchOffline();
                Check(duringRetry && !offlineOffered, "an automatic retry in between does not bring a closed offer back", results);
                error = false; offlineEntry.Observe(false, false, OfflineNow); error = true; WatchOffline();
                Check(offlineOffered, "the next trouble offers it again", results);
                // «Повторить вход» is the app's Retry: one safe reload of the page, and the screen goes.
                OpenOffline(); served = fixtures.Served(login); error = false;
                offlineView.PressRetryForQa();
                var reloaded = await UntilForQa(() => fixtures.Served(login) > served && !main.IsLoading, 8000);
                Check(reloaded && !OfflineShown, "«Повторить вход» reloads the page once and leaves the saved data", results);
                // Without a copy nothing is offered; without automatic sign-in neither.
                scheduleWeek = null; lastGrades = null; examPlan = null;
                error = true; WatchOffline();
                Check(!offlineOffered, "without a kept copy nothing is offered", results);
                scheduleWeek = OfflineWeekForQa(now, ScheduleAccount); offlineSignsInForQa = false; WatchOffline();
                Check(!offlineOffered, "without automatic sign-in nothing is offered", results);
                // Only exams in the future count as a copy.
                offlineSignsInForQa = true; scheduleWeek = null; examPlan = new ExamPlan(ScheduleAccount, now, new[] { new ExamItem { Kind = ExamKind.Exam, Subject = "QA прошлый", Start = now.AddHours(-2), Place = "" } }, null);
                WatchOffline();
                Check(!offlineOffered, "an exam that has begun is not a copy worth offering", results);
            }
            finally
            {
                offlineSignsInForQa = null; offlineShiftForQa = 0; locked = false; error = false;
                offlineEntry.Reset(); offlineOffered = false; messages.Withdraw("offlineOffer"); messages.Withdraw("httpError");
                prefs.Language = previousLanguage; Localize();
                main.ResourceRequestHandlerFactory = previousFactory;
                var previous = before as IDictionary<string, object>;
                var restored = await SetProxyForQaAsync(previous != null && previous.Count > 0 ? previous : new Dictionary<string, object> { { "mode", "system" } });
                scheduleWeek = previousWeek; lastGrades = previousGrades; examPlan = previousExams; siteUnreachable = false; UpdateOffline(); RenderFeedback();
                Check(restored, "the profile's network is given back after the offline entry step", results);
            }
        }
    }
}
