using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        private DownloadHistory history;
        private DownloadsView downloadsView;
        private DownloadRecord activeDownload;
        private readonly Dictionary<string, DownloadState> savedStates = new Dictionary<string, DownloadState>();
        private bool DownloadsVisible { get { return downloadsView != null && downloadsView.Visible; } }
        private void BuildDownloads()
        {
            history = new DownloadHistory(storage.Root);
            try { history.Load(Web.DownloadService.DownloadsFolder()); } catch (Exception) { /* A damaged list starts empty; files stay in Downloads. */ }
            foreach (var item in history.Items) savedStates[item.Id] = item.State;
            if (!selfTest) System.Threading.Tasks.Task.Run(() =>
            {
                PartialDownloads.CleanUp(Web.DownloadService.DownloadsFolder(), TimeSpan.FromMinutes(1));
                Web.SingleSignOn.CleanUp(TimeSpan.Zero); // A Moodle hand-off page left by an earlier, abruptly ended run.
            });
            downloadsView = new DownloadsView(layoutScale, External);
            downloadsView.SetItems(history.Items);
            downloadsView.OpenOutside += OpenRecordOutside;
            downloadsView.ShowInFolder += ShowRecordInFolder;
            downloadsView.CancelDownload += record => downloads?.Cancel(record.Id);
            downloadsView.RemoveRecord += record => { history.Remove(record.Id); savedStates.Remove(record.Id); downloadsView.Remove(record.Id); _ = SaveHistoryAsync(); };
            downloadsView.OpenFolder += OpenDownloads;
            downloadsView.ViewChanged += () => { if (!closing) { UpdateLocation(); ArrangeToolbar(); } };
            downloadsView.FindHandler = new Web.FindResults((source, total, ordinal, final) => Dispatch(() => findBar.ShowResult(source, total, ordinal, final)));
            downloadsView.Shortcut += key => { if (!closing) { var message = new System.Windows.Forms.Message(); ProcessCmdKey(ref message, key); } };
            canvas.Controls.Add(downloadsView);
        }
        private void ShowDownloads(DownloadRecord preview = null)
        {
            if (closing || downloadsView == null) return;
            var opening = !downloadsView.Visible;
            // Over the website, newly shown child windows are black until each paints. A plain page-coloured cover
            // is painted first, the page is built and painted beneath it, and the cover then steps aside.
            var reveal = opening && !selfTest && !accountReview;
            if (reveal) { pageCover.Visible = true; pageCover.BringToFront(); pageCover.Refresh(); }
            downloadsView.Visible = true; downloadsView.BringToFront();
            if (reveal) pageCover.BringToFront();
            if (preview != null) downloadsView.Preview(preview); else downloadsView.ClosePreview();
            UpdateLocation(); UpdateHistoryButtons(); ArrangeToolbar();
            if (opening || preview != null || toast.ContainsFocus) downloadsView.FocusWorkspace();
            if (!reveal) { if (opening) downloadsView.AnimateEntrance(); return; }
            PaintNow(downloadsView);
            var generation = ++coverGeneration;
            QueueAsync(async () => { await System.Threading.Tasks.Task.Delay(60); if (generation == coverGeneration && downloadsView.Visible) { pageCover.Visible = false; PaintNow(downloadsView); downloadsView.AnimateEntrance(); } });
        }
        private int coverGeneration;
        // New child windows are black until their first paint; paint the whole page before Windows shows the frame.
        private static void PaintNow(System.Windows.Forms.Control control)
        {
            if (!control.Visible || !control.IsHandleCreated) return;
            control.Update();
            foreach (System.Windows.Forms.Control child in control.Controls) PaintNow(child);
        }
        private void HideDownloads()
        {
            if (!DownloadsVisible) return;
            ++coverGeneration; pageCover.Visible = false;
            downloadsView.ClosePreview(); downloadsView.Visible = false; downloadsView.ReleaseViewer();
            if (loading.Visible) loading.BringToFront();
            UpdateLocation(); UpdateHistoryButtons(); ArrangeToolbar();
        }
        private void OnDownloadChanged(DownloadRecord record)
        {
            if (history == null) return;
            history.Upsert(record.Clone()); downloadsView.Upsert(record);
            if (record.State == DownloadState.Completed) lastDownloadPath = record.Path;
            if (record.State == DownloadState.Running) activeDownload = record;
            else if (activeDownload?.Id == record.Id) activeDownload = history.Items.FirstOrDefault(x => x.State == DownloadState.Running);
            DownloadState previous;
            if (!savedStates.TryGetValue(record.Id, out previous) || previous != record.State) { savedStates[record.Id] = record.State; _ = SaveHistoryAsync(); }
            if (!closing) { RenderFeedback(); if (DownloadsVisible) UpdateHeading(); }
        }
        // Writes are serialized by the history itself; callers do not wait for the disk.
        private async System.Threading.Tasks.Task SaveHistoryAsync()
        {
            try { await history.SaveAsync(); }
            catch (Exception) { /* The list is a convenience; downloaded files themselves are already saved. */ }
        }
        private string DownloadProgressText()
        {
            var item = activeDownload; if (item == null) return null;
            var amount = item.Total > 0 ? (item.Received * 100 / Math.Max(1, item.Total)) + "%" : FileKinds.Size(item.Received, prefs.Language);
            return string.Format(Strings.Get("downloadProgress", prefs.Language), string.IsNullOrEmpty(item.FileName) ? "…" : item.FileName, amount);
        }
        private void OpenRecordOutside(DownloadRecord record)
        {
            if (record == null || !File.Exists(record.Path) || !FileNames.CanOpen(record.Path)) { SetStatus("downloadOpenFailed"); return; }
            try { Process.Start(new ProcessStartInfo(record.Path) { UseShellExecute = true }); }
            catch (Exception) { SetStatus("downloadOpenFailed"); }
        }
        private void ShowRecordInFolder(DownloadRecord record)
        {
            if (record == null || !File.Exists(record.Path)) { OpenDownloads(); return; }
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + record.Path + "\"") { UseShellExecute = false }); }
            catch (Exception) { OpenDownloads(); }
        }
        private DownloadRecord LastCompletedDownload()
        {
            return history?.Items.FirstOrDefault(x => x.State == DownloadState.Completed && string.Equals(x.Path, lastDownloadPath, StringComparison.OrdinalIgnoreCase));
        }
    }
}
