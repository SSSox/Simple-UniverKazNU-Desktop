using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // The login mark of the visible --qa-offline run, under which Program saves its synthetic week and totals.
        internal static string OfflineRunAccount { get { return ScheduleWeek.AccountMark("qa-offline-run"); } }
        // A week of synthetic classes around today, saved yesterday afternoon. Program uses it for --qa-offline.
        internal static ScheduleWeek OfflineWeekForQa(DateTime now, string account)
        {
            var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
            var classes = new List<ScheduleClass>();
            for (var day = 0; day < 6; day++)
                foreach (var hour in new[] { 8, 10 })
                {
                    var start = monday.AddDays(day).AddHours(hour);
                    classes.Add(new ScheduleClass { Start = start, End = start.AddMinutes(50), Name = "QA Политология " + (day + 1) + (hour == 10 ? " и история политических учений Казахстана" : ""), Type = hour == 8 ? "Лекция" : "Семинар", Room = "30" + day, Teacher = "Синтетический П. П." });
                }
            return new ScheduleWeek { Start = monday, End = monday.AddDays(7), SavedAt = now.Date.AddDays(-1).AddHours(14).AddMinutes(5), Account = account, Classes = classes };
        }
        // The profile's Chromium is sent to a loopback proxy: a closed port fails every request in the real network stack
        // (ERR_PROXY_CONNECTION_FAILED), as without a connection; Windows' own network is not touched.
        private async Task<object> ProxyForQaAsync()
        {
            var context = main.GetBrowserHost().RequestContext;
            return await Cef.UIThreadTaskFactory.StartNew(() => context.GetPreference("proxy"));
        }
        private async Task<bool> SetProxyForQaAsync(object value)
        {
            var context = main.GetBrowserHost().RequestContext;
            return await Cef.UIThreadTaskFactory.StartNew(() =>
            {
                string failure;
                var set = context.CanSetPreference("proxy") && context.SetPreference("proxy", value, out failure) && string.IsNullOrEmpty(failure);
                return set;
            });
        }
        private static Dictionary<string, object> ProxyAt(int port) { return new Dictionary<string, object> { { "mode", "fixed_servers" }, { "server", "127.0.0.1:" + port } }; }
        private static async Task<bool> UntilForQa(Func<bool> reached, int milliseconds)
        {
            var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!reached()) { if (DateTime.UtcNow > until) return false; await Task.Delay(30); }
            return true;
        }
        private bool OfflineOnTopForQa { get { return OfflineShown && canvas.Controls.GetChildIndex(offlineView) < canvas.Controls.GetChildIndex(loading) && canvas.Controls.GetChildIndex(offlineView) < canvas.Controls.GetChildIndex(main); } }
        // A proxy that takes connections and never answers: a network that is there but leads nowhere, so a retry hangs.
        private sealed class StallProxyForQa : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly List<TcpClient> held = new List<TcpClient>();
            internal readonly int Port;
            internal int Held { get { lock (held) return held.Count; } }
            internal StallProxyForQa()
            {
                listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task.Run(async () =>
                {
                    while (true)
                    {
                        TcpClient client;
                        try { client = await listener.AcceptTcpClientAsync(); } catch (Exception) { return; }
                        lock (held) held.Add(client);
                    }
                });
            }
            public void Dispose() { try { listener.Stop(); } catch (Exception) { } lock (held) { foreach (var client in held) client.Close(); held.Clear(); } }
        }
        // Offline: a page that cannot be reached shows the kept week; a retry that hangs keeps it in front of the placeholder,
        // a retry that the site answers ends it, the student's own navigation takes it down until that fails too, Downloads
        // covers it, and without a kept week the banner is alone. The live site is never asked: every load starts only
        // once the cut is confirmed, and the pages that do load come from fixtures.
        private async Task OfflineQaAsync(string qa, List<string> results)
        {
            var previousWeek = scheduleWeek; var previousGrades = lastGrades; var previousExams = examPlan; var previousFactory = main.ResourceRequestHandlerFactory; var now = DateTime.Now;
            var before = await ProxyForQaAsync();
            var cut = await SetProxyForQaAsync(ProxyAt(9));
            Check(cut, "the self-test can cut this profile's network before it loads a site address", results);
            // Earlier steps leave fixture pages (the schedule, UMKD) in the factory: none may answer here, so every request
            // of this step goes to the cut network except the fixture it adds itself.
            var fixtures = new OnlineTestQaFactory(null);
            main.ResourceRequestHandlerFactory = fixtures;
            try
            {
                scheduleWeek = OfflineWeekForQa(now, ScheduleAccount); lastGrades = OfflineGradesForQa(now, ScheduleAccount); examPlan = null;
                var target = SitePolicy.Origin + "/student/myschedule/";
                main.Load(target);
                var shown = await UntilForQa(() => OfflineShown && error, 15000);
                QaTrace("offline-shown " + shown + " " + failureCode + " " + messages.Current?.Key);
                Check(shown && failureCode == "ProxyConnectionFailed" && messages.Current?.Key == "networkError" && OfflineOnTopForQa && offlineView.DaysForQa == 6 && offlineView.TodayMarkedForQa == (now.DayOfWeek != DayOfWeek.Sunday),
                    "a page that cannot be reached shows the kept week under the banner, today marked", results);
                var words = offlineView.WordsForQa;
                Check(words.Contains(OfflineWeek.Saved(scheduleWeek.SavedAt, now, "ru")) && words.Contains(OfflineWeek.Range(scheduleWeek, "ru")) && words.Contains("08:00–08:50 QA Политология 1 · Лекция · ауд. 300 · Синтетический П. П."),
                    "the screen says which week, when it was saved, and each class with its room and teacher", results);
                Check(offlineView.TitlesDrawnForQa, "each day's name and each course's name is painted in its card's heading", results);
                Check(words.Contains(Strings.Get("offlineGrades", "ru")) && words.Contains(OfflineWeek.Saved(lastGrades.SavedAt, now, "ru")) && words.Contains("QA Алгебра: РК1 71/100 РК2 88.5/100") && words.Contains("QA Физика: Итог 93") &&
                    offlineView.GradesBelowWeekForQa, "«Последние оценки» stands below the week with its saved time and each course's totals", results);
                QaTrace("offline-layout " + offlineView.LayoutForQa);
                Check(offlineView.TodayInViewForQa && offlineView.PaintedForQa, "today's card is scrolled into view when the screen appears (" + offlineView.LayoutForQa + ")", results);
                foreach (var lang in new[] { "ru", "kz", "en" })
                {
                    prefs.Language = lang; Localize(); await Task.Delay(80);
                    QaTrace("offline-layout-" + lang + " " + offlineView.LayoutForQa);
                    Check(offlineView.FitsForQa && offlineView.PaintedForQa && offlineView.TodayInViewForQa && offlineView.TitleForQa == Strings.Get("offlineTitle", lang), "the offline screen fits and shows the week in " + lang, results);
                    SaveBitmap(offlineView, Path.Combine(qa, "offline-" + lang + ".png"));
                }
                prefs.Language = "ru"; Localize();
                // The student's own scrolling is kept: a redraw (here the same language again) no longer jumps to today.
                Check(offlineView.FocusedForQa || !main.ContainsFocus, "the keys go to the schedule, not to the failed page", results);
                offlineView.PressForQa(Keys.End); var bottom = offlineView.ScrollForQa;
                Check(offlineView.GradesInViewForQa && offlineView.PaintedForQa && offlineView.TitlesDrawnForQa, "at the end of the screen the last grades are in view and painted", results);
                SaveBitmap(offlineView, Path.Combine(qa, "offline-grades-ru.png"));
                offlineView.PressForQa(Keys.Home); Localize(); offlineView.SetZoom(chromeZoom);
                Check(bottom > 0 && offlineView.ScrollForQa == 0 && offlineView.PaintedForQa, "End and Home scroll the week, and a redraw keeps the student's place", results);
                // A retry the app sends by itself into a network that leads nowhere: the screen stays in front of the
                // placeholder while it hangs, and after it fails.
                using (var stall = new StallProxyForQa())
                {
                    Check(await SetProxyForQaAsync(ProxyAt(stall.Port)), "the self-test can make a retry hang", results);
                    var loads = recoveries; recoverAt = DateTime.UtcNow; RunRecovery();
                    var covered = await UntilForQa(() => loading.Visible && stall.Held > 0, 8000);
                    var kept = OfflineOnTopForQa; await Task.Delay(600);
                    var still = loading.Visible && OfflineOnTopForQa;
                    Check(await SetProxyForQaAsync(ProxyAt(9)), "the cut is back before the hanging retry ends", results);
                    stall.Dispose();
                    var again = await UntilForQa(() => error && !loading.Visible, 15000);
                    QaTrace("offline-retry " + covered + " " + kept + " " + still + " " + again + " " + OfflineShown + " " + failureCode);
                    Check(recoveries == loads + 1 && covered && kept && still && again && OfflineOnTopForQa, "an automatic retry keeps the kept week in front of the loading placeholder while it hangs and after it fails", results);
                }
                // Downloads opens over it and gives it back.
                ShowDownloads(); await Task.Delay(100);
                var hidden = !OfflineShown;
                HideDownloads(); await Task.Delay(100);
                Check(hidden && OfflineOnTopForQa, "Downloads covers the offline screen and gives it back", results);
                // The student's own navigation takes it down, and its failure brings it back.
                NavigateSection("/student/umkd/");
                var down = await UntilForQa(() => !OfflineShown, 3000);
                var up = await UntilForQa(() => OfflineShown && error, 15000);
                Check(down && up, "the student's own navigation takes the screen down until that page fails too", results);
                // Zoom and the dark theme.
                SetZoom(150); await Task.Delay(150);
                Check(offlineView.FitsForQa && offlineView.PaintedForQa, "the offline screen fits and shows the week at 150%", results);
                SaveBitmap(offlineView, Path.Combine(qa, "offline-150.png"));
                SetZoom(100);
                var themeBefore = prefs.Theme; prefs.Theme = ThemeChoice.Dark; ApplyTheme();
                try { await Task.Delay(150); SaveBitmap(offlineView, Path.Combine(qa, "offline-dark.png")); Check(offlineView.TitlesDrawnForQa, "the day names are painted in the dark theme too", results); }
                finally { prefs.Theme = themeBefore; ApplyTheme(false); }
                // Totals without a week: the screen still stands, with the totals alone.
                scheduleWeek = null; UpdateOffline();
                offlineView.PressForQa(Keys.Home);
                var alone = offlineView.WordsForQa;
                Check(OfflineShown && offlineView.DaysForQa == 0 && alone.Contains(Strings.Get("offlineNoWeek", "ru")) && alone.Contains("QA Физика: Итог 93") && offlineView.PaintedForQa && offlineView.TitlesDrawnForQa && offlineView.FitsForQa,
                    "kept totals without a kept week still show the offline screen, with the totals alone", results);
                SaveBitmap(offlineView, Path.Combine(qa, "offline-grades-only.png"));
                // The schedule picture never has the totals: the same sheet turned into a picture column drops them.
                using (var column = new OfflineWeekView.WeekSheet())
                {
                    var export = OfflineWeekForQa(now, ScheduleAccount);
                    column.Set(export, lastGrades, OfflineExamsForQa(now, ScheduleAccount), now, "ru", false); column.SetExport(export, "QA", null, "ru"); column.Arrange(800, 1);
                    Check(column.Words.Contains("QA Политология 1") && !column.Words.Contains(Strings.Get("offlineGrades", "ru")) && !column.Words.Contains("QA Алгебра") && column.GradesTop < 0 && column.ExamsTop < 0 && !column.Words.Contains("QA Микроэкономика"), "the schedule picture carries neither the last grades nor the exams", results);
                }
                // Without a kept week and without kept totals only the banner speaks.
                var gradesNow = lastGrades; var examsNow = examPlan; lastGrades = null; examPlan = null; UpdateOffline();
                Check(!OfflineShown && error, "without a kept week, kept totals or coming exams a failed page has the banner alone", results);
                lastGrades = gradesNow; examPlan = examsNow;
                scheduleWeek = OfflineWeekForQa(now, ScheduleAccount); UpdateOffline();
                Check(OfflineShown && offlineView.DaysForQa == 6, "the kept week comes back while the page still fails", results);
                // The connection returns: the next automatic retry is answered (a fixture, ahead of the proxy) and the screen goes.
                var retried = RetryTarget();
                fixtures.Pages[retried] = "<!doctype html><html><head><meta charset='utf-8'><title>back</title></head><body><div id='l4'>back</div></body></html>";
                recoverAt = DateTime.UtcNow; RunRecovery();
                var gone = await UntilForQa(() => !OfflineShown && !error && fixtures.Served(retried) > 0, 8000);
                QaTrace("offline-back " + retried + " " + gone + " " + fixtures.Served(retried) + " " + OfflineShown + " " + error);
                Check(gone, "a retry the site answers takes the offline screen away", results);
            }
            finally
            {
                main.ResourceRequestHandlerFactory = previousFactory;
                var previous = before as IDictionary<string, object>;
                var restored = await SetProxyForQaAsync(previous != null && previous.Count > 0 ? previous : new Dictionary<string, object> { { "mode", "system" } });
                scheduleWeek = previousWeek; lastGrades = previousGrades; examPlan = previousExams; siteUnreachable = false; UpdateOffline();
                messages.Withdraw("networkError"); RenderFeedback();
                Check(restored, "the profile's network is given back after the offline step", results);
            }
        }
        // --qa-offline: an isolated, visible run whose Chromium has no way out from its first request (Program passes a
        // closed loopback proxy at start and writes the synthetic week to the new profile's schedule.json, so the start
        // reads it from disk as a real one does). qa-offline\trace.txt says every half second what the window showed and
        // ends with a verdict: from the first failure on the week stays on screen, through the app's own retries.
        // window.png is a PrintWindow capture (DrawToBitmap paints the browser over the panels).
        private async Task OfflineRunQaAsync()
        {
            var output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-offline"); Directory.CreateDirectory(output);
            var trace = new StringBuilder();
            trace.AppendLine("kept " + (scheduleWeek != null) + " classes=" + (scheduleWeek?.Classes.Count ?? 0) + " grades=" + (lastGrades?.Courses.Count ?? 0));
            var clock = Stopwatch.StartNew(); var shot = false; bool seen = false, today = false; var gaps = 0; var start = recoveries;
            while (clock.Elapsed < TimeSpan.FromSeconds(45) && !closing)
            {
                trace.AppendLine(clock.ElapsedMilliseconds + " shown=" + OfflineShown + " top=" + OfflineOnTopForQa + " loading=" + loading.Visible + " error=" + error + " retries=" + recoveries + " code=" + failureCode + " banner=" + messages.Current?.Key + " focus=" + offlineView.FocusedForQa);
                if (OfflineShown && OfflineOnTopForQa) { if (!seen) { today = offlineView.TodayInViewForQa && offlineView.PaintedForQa; trace.AppendLine("layout " + offlineView.LayoutForQa); } seen = true; } else if (seen) gaps++;
                if (!shot && OfflineShown && clock.Elapsed > TimeSpan.FromSeconds(3)) { shot = true; SaveNativeFrameForMotionQa(this, Path.Combine(output, "window.png")); }
                await Task.Delay(500);
            }
            var retries = recoveries - start;
            var auto = seen && today && gaps == 0 && retries >= 2;
            trace.AppendLine(auto ? "auto PASS the week stayed on screen through " + retries + " retries, today's card in view" : "auto FAIL seen=" + seen + " today=" + today + " gaps=" + gaps + " retries=" + retries);
            // The second road: the site stops failing outright but sign-in waits. The app's own retries are held (they would fail
            // again and bring the first road back), the offer's clock is moved past the limit, and the banner's button opens the
            // saved data. offer.png and entry.png are PrintWindow captures of the whole window.
            timer.Stop(); recoverAt = null; siteUnreachable = false; offlineSignsInForQa = true; UpdateOffline();
            messages.Withdraw("networkError"); RenderFeedback();
            offlineShiftForQa += OfflineEntry.WaitLimit + 1000; WatchOffline();
            await Task.Delay(600);
            var offer = offlineOffered && feedback.Visible && feedback.Primary.Visible && !OfflineShown;
            trace.AppendLine("offer offered=" + offlineOffered + " banner=" + messages.Current?.Key + " shown=" + OfflineShown + " button=" + feedback.Primary.Text);
            SaveNativeFrameForMotionQa(this, Path.Combine(output, "offer.png"));
            await FeedbackPrimaryAsync(); await Task.Delay(800);
            var entry = OfflineShown && OfflineOnTopForQa && offlineView.BackShownForQa && offlineView.ExamDaysForQa > 0 && offlineView.PaintedForQa && !offlineOffered;
            trace.AppendLine("entry shown=" + OfflineShown + " top=" + OfflineOnTopForQa + " exams=" + offlineView.ExamDaysForQa + " title=" + offlineView.TitleForQa);
            SaveNativeFrameForMotionQa(this, Path.Combine(output, "entry.png"));
            offlineView.PressForQa(Keys.End); await Task.Delay(300);
            SaveNativeFrameForMotionQa(this, Path.Combine(output, "entry-end.png"));
            trace.AppendLine(auto && offer && entry ? "PASS the week stayed on screen through " + retries + " retries, today's card in view; the offer opened the saved data" : "FAIL auto=" + auto + " offer=" + offer + " entry=" + entry);
            File.WriteAllText(Path.Combine(output, "trace.txt"), trace.ToString());
            ExitApplication();
        }
    }
}
