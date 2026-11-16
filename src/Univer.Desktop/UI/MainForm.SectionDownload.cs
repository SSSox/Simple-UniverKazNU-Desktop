using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using CefSharp.WinForms;
using Univer.Desktop.Core;
using Univer.Desktop.Web;

namespace Univer.Desktop.UI
{
    // «Скачать все» in a UMKD section: the files go one after another, so the server and the Downloads list see one
    // transfer at a time. Names, course and section are read for every file before the first one, so the student may
    // leave the page meanwhile. A file already on disk is skipped, a teacher's link is counted and left for its own click,
    // and the run speaks for its files: one summary at the end instead of a message per file. A sign-in the server asks
    // for reloads the page once with saved sign-in and goes on from the refused file; otherwise the run ends there.
    internal sealed partial class MainForm
    {
        private sealed class SectionRun { public string Page, Section; public bool Stop, Login, Resumed; public int Done, Total, Saved, Present, Links, Failed; }
        private SectionRun sectionRun, sectionResult;
        // QA: answers for a file of the run in place of the service, with the run's own status sink.
        private Func<string, Action<string>, Task<string>> sectionFetch;
        private async Task DownloadSectionAsync(ChromiumWebBrowser browser, string payload)
        {
            var batch = DownloadBatch.Parse(payload);
            if (batch == null || closing || rotating || commandBusy || browser == null || browser != Active || !SitePolicy.StudyMaterials(browser.Address)) return;
            var running = sectionRun;
            var here = running != null && running.Section == batch.Section && SitePolicy.SamePage(running.Page, browser.Address);
            switch (DownloadBatch.Choose(batch.Stop, here, running != null && !here))
            {
                case SectionClick.Stop: running.Stop = true; SetStatus("downloadAllStopping"); return;
                case SectionClick.Show: ShowSectionRun(running); return;
                case SectionClick.Relabel: ShowSectionRun(new SectionRun { Page = browser.Address, Section = batch.Section }, true); return;
                case SectionClick.Busy: SetStatus("downloadAllBusy"); return;
            }
            await RunSectionAsync(browser, new SectionRun { Page = browser.Address, Section = batch.Section, Total = batch.Urls.Count }, batch.Urls);
        }
        private async Task RunSectionAsync(ChromiumWebBrowser browser, SectionRun run, IList<string> urls)
        {
            var service = downloads; IList<string> refused = null;
            sectionRun = run;
            try
            {
                var agent = await AutomationController.EvaluateAsync(browser, "navigator.userAgent");
                var files = StudyMaterials.Files(await AutomationController.EvaluateAsync(browser, StudyMaterials.FilesScript(urls)));
                var source = siteTitle;
                Action<string> quiet = key => { };
                ShowSectionRun(run);
                for (var index = 0; index < urls.Count; index++)
                {
                    var url = urls[index];
                    // A settings save pauses the run, and a file the student is fetching by its own click is waited for and
                    // then found on disk; a logout, a new account or closing ends the run.
                    while ((commandBusy || service.Busy(url)) && !Ended(run, service)) await Task.Delay(250);
                    if (Ended(run, service)) break;
                    StudyMaterials.StudyFile file; files.TryGetValue(url, out file);
                    var name = file == null ? "" : file.Name; var key = DownloadKeys.Of(url);
                    if (history != null && history.Downloaded(key, name, source) != null) run.Present++;
                    else if (downloadProbe != null && downloadProbe(url)) run.Saved++;
                    else
                    {
                        var result = sectionFetch != null ? await sectionFetch(url, quiet) :
                            await service.DownloadAsync(url, run.Page, agent, name, source, key, StudyFolders.Relative(file?.Course, file?.Section), found => { }, quiet);
                        if (result == "downloadComplete") run.Saved++;
                        else if (result == "studyLink") run.Links++;
                        else if (result == "downloadLogin") { refused = urls.Skip(index).ToList(); break; }
                        else run.Failed++;
                    }
                    run.Done++;
                    ShowSectionRun(run);
                }
            }
            finally
            {
                sectionRun = null;
                // After a logout or a new account the run just ends: the new session starts without the old messages.
                if (service == downloads && !closing && !rotating && !(refused != null && ResumeSection(browser, run, refused)))
                {
                    ShowSectionRun(run, true);
                    if (refused != null) { run.Login = true; run.Failed += refused.Count; }
                    sectionResult = run;
                    SetStatus(run.Login ? "downloadLogin" : run.Stop ? "downloadAllStopped" : "downloadAllDone");
                }
                else ShowSectionRun(run, true);
            }
        }
        private bool Ended(SectionRun run, DownloadService service) { return run.Stop || service != downloads || closing || rotating; }
        // The server ended the session behind the page. With saved sign-in the page reloads, the usual login signs in again
        // and the run goes on from the refused file, once; an explicit logout or a stopped guard leaves it to the student,
        // whose next click skips the files already saved.
        private bool ResumeSection(ChromiumWebBrowser browser, SectionRun run, IList<string> refused)
        {
            if (run.Resumed || run.Stop || credentials == null || prefs.AutoLoginPaused || guard.Blocked || resumeDownload != null || browser == null || browser.IsDisposed ||
                browser != Active || !SitePolicy.SamePage(run.Page, browser.Address)) return false;
            run.Resumed = true;
            resumeDownload = new PendingDownload { Url = refused[0], Until = DateTime.UtcNow.AddSeconds(90), Run = run, Rest = refused };
            SetStatus("downloadSigningIn");
            browser.Load(run.Page);
            return true;
        }
        private void ResumeSectionRun(PendingDownload pending)
        {
            var browser = Active;
            // Signed in, but a run the student started meanwhile or another page leaves the section where it stopped: its
            // counts say so, and a click on its button goes on (saved files are skipped).
            if (sectionRun != null) return;
            if (browser == null || browser.IsDisposed || !SitePolicy.SamePage(pending.Run.Page, browser.Address)) { pending.Run.Stop = true; sectionResult = pending.Run; SetStatus("downloadAllStopped"); return; }
            QueueAsync(() => RunSectionAsync(browser, pending.Run, pending.Rest));
        }
        // The section's button on the page shows the run (Stop and how far it got) while that page is still open.
        private void ShowSectionRun(SectionRun run, bool finished = false)
        {
            if (!finished && !closing) SetStatus("downloadAllProgress");
            var browser = Active;
            if (closing || browser == null || browser.IsDisposed || !SitePolicy.SamePage(run.Page, browser.Address)) return;
            var script = "window.__univerPagesV1&&window.__univerPagesV1.downloading&&window.__univerPagesV1.downloading(" + new JavaScriptSerializer().Serialize(run.Section) + "," +
                (finished ? "0,0" : run.Done + "," + run.Total) + ");'ok'";
            _ = AutomationController.EvaluateAsync(browser, script);
        }
        // "Downloading section «…»: file 3 of 9" while it runs; the counts once it has ended.
        private string SectionText(string key, string lang)
        {
            var run = key == "downloadAllProgress" ? sectionRun : sectionResult;
            if (run == null) return null;
            var section = run.Section.Length > 60 ? run.Section.Substring(0, 59).TrimEnd() + "…" : run.Section;
            if (key == "downloadAllProgress") return string.Format(Strings.Get(key, lang), section, Math.Min(run.Done + 1, run.Total), run.Total);
            var parts = new List<string> { string.Format(Strings.Get("downloadAllSaved", lang), run.Saved) };
            if (run.Present > 0) parts.Add(string.Format(Strings.Get("downloadAllPresent", lang), run.Present));
            if (run.Links > 0) parts.Add(string.Format(Strings.Get("downloadAllLinks", lang), run.Links));
            if (run.Failed > 0) parts.Add(string.Format(Strings.Get("downloadAllFailed", lang), run.Failed));
            return string.Format(Strings.Get(key, lang), section) + " " + string.Join(", ", parts) + ".";
        }
    }
}
