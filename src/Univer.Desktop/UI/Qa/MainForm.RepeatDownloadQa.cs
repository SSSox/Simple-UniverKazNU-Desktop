using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // A file downloaded before from the same address, still on disk: the app asks before saving a second copy.
        // The download request itself is replaced by a probe, so no network is used.
        private async Task RepeatDownloadQaAsync(string qa, DownloadRecord file, List<string> results)
        {
            var address = SitePolicy.Origin + "/student/umkd/get/101/202/2026/1";
            var saved = file.Clone(); saved.Id = Guid.NewGuid().ToString("N"); saved.Key = DownloadKeys.Of(address); saved.Finished = DateTime.Now.AddMinutes(-5);
            history.Upsert(saved);
            string started = null; downloadProbe = url => { started = url; return true; };
            Func<Task> wait = async () => { var until = DateTime.UtcNow.AddSeconds(3); while (started == null && DateTime.UtcNow < until) await Task.Delay(50); };
            try
            {
                HideDownloads();
                await DownloadAsync(address, "Силлабус QA");
                Check(messages.Current?.Key == "downloadDuplicate" && toast.Key == "downloadDuplicate" && toast.Primary.Text == Strings.Get("openExisting", prefs.Language) &&
                    toast.Secondary.Text == Strings.Get("downloadAgain", prefs.Language) && toast.AccessibleName.Contains(saved.FileName) && started == null &&
                    toast.Primary.Bottom < toast.Height && toast.Secondary.Bottom < toast.Height && toast.Secondary.Right <= toast.Width,
                    "a repeated download asks first, naming the file, with Open and Download again inside the notice", results);
                await Task.Delay(900);
                QaSnapshot(Path.Combine(qa, "download-repeat-toast.png"), toast.Width, toast.Height, bitmap => toast.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, toast.Size)));
                await FeedbackPrimaryAsync();
                Check(DownloadsVisible && downloadsView.Previewing && downloadsView.PreviewName == saved.FileName && messages.Current?.Key != "downloadDuplicate" && started == null,
                    "Open shows the file downloaded before instead of saving it again", results);
                // Closing right away releases a preview browser that may still be starting.
                HideDownloads();
                await DownloadAsync(address, "Силлабус QA");
                QaTrace("repeat-download current=" + messages.Current?.Key + " pending=" + (duplicate != null));
                FeedbackSecondary(); await wait();
                QaTrace("repeat-again started=" + (started != null) + " current=" + messages.Current?.Key + " busy=" + closing + rotating + commandBusy + " active=" + (Active == main));
                Check(started == address && messages.Current?.Key != "downloadDuplicate", "Download again saves a new copy of the same file", results);
                // A file deleted from disk is simply downloaded again; another address is never mistaken for it.
                started = null; saved.Path = Path.Combine(Path.GetDirectoryName(saved.Path), "removed-" + Guid.NewGuid().ToString("N") + ".pdf");
                await DownloadAsync(address, "Силлабус QA"); await wait();
                var removed = started == address && messages.Current?.Key != "downloadDuplicate";
                saved.Path = file.Path; started = null;
                await DownloadAsync(address + "/other", "Другой файл"); await wait();
                Check(removed && started == address + "/other" && messages.Current?.Key != "downloadDuplicate", "a missing file or another address downloads without asking", results);
            }
            finally { downloadProbe = null; duplicate = null; history.Remove(saved.Id); messages.Dismiss(); RenderFeedback(); }
        }
    }
}
