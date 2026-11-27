using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // New grades: the settings row in each language, Save keeps the choice and turning it off removes the kept reading;
        // the first reading and a removed course are silent, a changed score comes as one balloon that opens the
        // attestation page, a failed reading or a reading from an ended session changes nothing. Fixture pages instead of
        // the network, an isolated hidden tray icon and a synthetic login mark.
        private async Task GradesQaAsync(string qa, List<string> results)
        {
            Check(prefs.Grades && prefs.GradeInterval == GradeNotice.DefaultInterval, "new-grade notifications are on, every 30 minutes, by default", results);
            foreach (var lang in new[] { "ru", "kz", "en" })
            {
                var shown = prefs.Clone(); shown.Language = lang;
                using (var form = new SettingsForm(null, shown))
                {
                    form.Opacity = 0; form.ShowInTaskbar = false; form.Show(this); form.SelectUpdatesForQa(); await Task.Delay(150);
                    var interval = form.IntervalForQa;
                    QaTrace("grades-" + lang + " " + form.IntervalFitsForQa + " " + form.LeadFitsForQa + " " + string.Join("|", form.OverflowForQa()));
                    Check(form.IntervalFitsForQa && form.LeadFitsForQa && interval.Visible && interval.Enabled && form.OverflowForQa().Length == 0 &&
                        interval.Text == string.Format(Strings.Get("gradeInterval", lang), 30) && interval.AccessibleName.StartsWith(Strings.Get("gradeIntervalTitle", lang) + ": ") && !form.SaveButton.Enabled,
                        "the new-grade row fits with its interval choice and changes nothing until touched (" + lang + ")", results);
                    SaveBitmap(form, Path.Combine(qa, "grades-" + lang + ".png"));
                    form.ChooseIntervalForQa(60); await Task.Delay(50);
                    var chosen = form.GradeInterval == 60 && interval.Text == string.Format(Strings.Get("gradeInterval", lang), 60) && form.SaveButton.Enabled && form.IntervalFitsForQa;
                    form.GradesForQa.Checked = false; await Task.Delay(50);
                    Check(chosen && !form.Grades && !interval.Enabled && form.SaveButton.Enabled, "choosing an interval and turning new grades off are offered to Save (" + lang + ")", results);
                    form.Close();
                }
            }
            var file = Path.Combine(storage.Root, GradeFile.Name);
            var previousTray = trayIcon; var previousOpens = balloonOpens; var previousWatch = gradeWatch;
            var page = AttestationFixture(false);
            string answer = page; var reads = 0;
            var markBefore = await KeepMarkForQaAsync();
            gradeAccountForQa = ScheduleWeek.AccountMark("qa-grades"); gateLoginForQa = "qa-grades";
            // The check reads the shown page's account mark before it asks the site (ReadingAllowed): a site page that carries it.
            Func<string, Task> showMark = async mark =>
            {
                main.LoadHtml("<!doctype html><html><head><title>grades-mark-qa</title></head><body><script>localStorage.setItem('" + PageAccount.Key + "','" + mark + "');</script><div id=\"l4\">QA</div></body></html>", SitePolicy.Origin + "/student/news/");
                // The title is there before the body's script has set the mark: wait for the mark itself.
                for (var until = DateTime.UtcNow.AddSeconds(8); DateTime.UtcNow < until && await AutomationController.EvaluateAsync(main, "document.title+'|'+localStorage.getItem('" + PageAccount.Key + "')") != "grades-mark-qa|" + mark;) await Task.Delay(100);
            };
            await showMark(PageAccount.Mark("qa-grades"));
            gradeReadForQa = (url, token) => { reads++; return Task.FromResult(url == SitePolicy.Origin + "/student/attestation/" ? answer : null); };
            using (var isolatedTray = new NotifyIcon { Visible = false })
            {
                trayIcon = isolatedTray;
                try
                {
                    await ClearGradesAsync(); gradeTitleShown = gradeBodyShown = null; balloonOpens = null;
                    await ReadGradesAsync();
                    var kept = storage.LoadGrades(gradeAccountForQa);
                    Check(reads == 1 && gradeTitleShown == null && balloonOpens == null && kept != null && kept.Rows.Count == 5 && kept.Rows[0].Course == "Алгоритмы и структуры данных",
                        "the first reading of the attestation page is kept quietly as the baseline", results);
                    Check(File.Exists(file) && !File.ReadAllText(file).Contains("<"), "the kept reading holds the table's text, not the page", results);
                    await ReadGradesAsync();
                    Check(gradeTitleShown == null, "the same scores again say nothing", results);
                    // Психология: РК2 0 → 71, the total 18 → 38.
                    var updated = page.Replace("<td>60</td><td>0</td><td>0</td><td>18</td>", "<td>60</td><td>71</td><td>0</td><td>38</td>");
                    answer = updated;
                    await ReadGradesAsync();
                    QaTrace("grades-balloon " + gradeTitleShown + " | " + gradeBodyShown);
                    Check(gradeTitleShown == Strings.Get("gradesNew", prefs.Language) && gradeBodyShown == "Психология: РК2 71 · Сумма 38" && balloonOpens == "grades",
                        "a changed score comes as one balloon naming the course and its new scores, and its click opens the attestation page", results);
                    // A failed reading (a sign-in page, an error): nothing changes.
                    var before = File.GetLastWriteTimeUtc(file); var sheetBefore = gradeWatch.Baseline; gradeTitleShown = null;
                    answer = null; await ReadGradesAsync();
                    Check(gradeWatch.Baseline == sheetBefore && File.GetLastWriteTimeUtc(file) == before && gradeTitleShown == null, "a failed reading leaves the baseline as it was and says nothing", results);
                    // The session ends while the page answers: the reading is dropped. The epoch goes back at once, since the
                    // browser's own callbacks are bound to it and later steps need them.
                    answer = page; var epochWas = sessionEpoch;
                    gradeReadForQa = (url, token) => { reads++; sessionEpoch++; return Task.FromResult(answer); };
                    try { await ReadGradesAsync(); } finally { sessionEpoch = epochWas; }
                    Check(gradeWatch.Baseline == sheetBefore && File.GetLastWriteTimeUtc(file) == before && gradeTitleShown == null, "a reading from a session that ended meanwhile is dropped", results);
                    gradeReadForQa = (url, token) => { reads++; return Task.FromResult(answer); };
                    // Removing a course with all remaining scores intact is quiet.
                    answer = updated.Replace("<tr><td>Физическая культура</td><td>2</td><td>70</td><td>64</td><td>65</td><td>66</td><td>1.67</td><td>C-</td><td>удовл.</td></tr>", "");
                    await ReadGradesAsync();
                    Check(gradeTitleShown == null && gradeWatch.Baseline?.Rows.Count == 4, "removing a course alone updates the baseline without a balloon", results);
                    // The page shown belongs to another account (the student who signed in by hand after the site's own logout): no
                    // request is made and nothing is kept or announced; the saved login's own page brings the check back.
                    await showMark(PageAccount.Mark("qa-other"));
                    var sheetKept = gradeWatch.Baseline; var countOther = reads; gradeTitleShown = null; answer = updated;
                    gradesDueAt = scheduleClock.ElapsedMilliseconds + 120 * 60000L;
                    await ReadGradesAsync();
                    Check(reads == countOther && gradeWatch.Baseline == sheetKept && gradeTitleShown == null, "a page of another account than the saved login gets no grade check and nothing is announced", results);
                    Check(gradesDueAt - scheduleClock.ElapsedMilliseconds <= GradeRetryMs, "a page not allowed yet is asked again in a minute, not after the whole interval", results);
                    await showMark(PageAccount.Mark("qa-grades")); answer = page;
                    // Behind a PIN the balloon names nothing.
                    var body = GradeNotice.Body(new List<GradeChange> { new GradeChange { Course = "Психология", Cells = new List<string> { "РК2 71" } } }, prefs.Language, true);
                    Check(body == Strings.Get("gradesLocked", prefs.Language) && !body.Contains("Психология"), "behind a PIN the balloon names no course or score", results);
                    // Without a login mark nothing is read or kept.
                    gradeAccountForQa = ""; var count = reads; await ReadGradesAsync();
                    Check(reads == count && storage.LoadGrades("") == null, "without a saved login nothing is read or kept", results);
                    gradeAccountForQa = ScheduleWeek.AccountMark("qa-grades");
                    // Turned off in Settings: the kept reading goes, and nothing more is read.
                    Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, grades: false, gradeInterval: 60) && !prefs.Grades && prefs.GradeInterval == 60 &&
                        !storage.LoadPreferences().Grades && storage.LoadPreferences().GradeInterval == 60, "Save keeps new grades off and the interval chosen", results);
                    var removed = await UntilForQa(() => !File.Exists(file) && gradeWatch.Baseline == null, 3000);
                    count = reads; GradeContent(); await Task.Delay(50);
                    Check(removed && reads == count, "turned off, the kept reading is removed and nothing is read", results);
                    Check(await ApplySettingsAsync(SettingsAction.Save, null, prefs.Language, prefs.Readable, grades: true, gradeInterval: GradeNotice.DefaultInterval) && prefs.Grades && prefs.GradeInterval == 30,
                        "Save turns them back on", results);
                    messages.Withdraw("settingsSaved"); messages.Withdraw("networkError"); RenderFeedback();
                    // The session's end takes the reading with it.
                    await ReadGradesAsync(); var present = File.Exists(file);
                    await ClearGradesAsync();
                    Check(present && !File.Exists(file) && gradeWatch.Baseline == null, "clearing the session's reading removes the file", results);
                }
                finally
                {
                    trayIcon = previousTray; balloonOpens = previousOpens; gradeWatch = previousWatch;
                    gradeAccountForQa = null; gateLoginForQa = null; gradeReadForQa = null;
                    await RestoreMarkForQaAsync(markBefore); gradeTitleShown = gradeBodyShown = null; gradesDueAt = long.MinValue;
                    prefs.Grades = true; prefs.GradeInterval = GradeNotice.DefaultInterval;
                }
            }
        }
    }
}
