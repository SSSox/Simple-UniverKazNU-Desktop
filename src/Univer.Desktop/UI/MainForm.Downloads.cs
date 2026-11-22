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
            downloadsView.Order = DownloadOrder.Parse(prefs.DownloadsOrder);
            downloadsView.SetItems(history.Items);
            // A chosen order is kept for the next start; self-test and the review leave the settings file alone.
            downloadsView.OrderChanged += () =>
            {
                prefs.DownloadsOrder = DownloadOrder.Name(downloadsView.Order);
                if (!storageFailed && !selfTest && !accountReview) QueueAsync(SavePreferencesAsync);
            };
            downloadsView.OpenOutside += OpenRecordOutside;
            downloadsView.ShowInFolder += ShowRecordInFolder;
            downloadsView.CancelDownload += record => { downloads?.Cancel(record.Id); CancelWebDownload(record.Id); };
            downloadsView.RemoveRecord += record => ForgetRecord(record.Id);
            downloadsView.DeleteFile += record => QueueAsync(() => DeleteDownloadAsync(record));
            downloadsView.OpenFolder += OpenDownloads;
            downloadsView.ViewChanged += () => { if (!closing) { UpdateLocation(); ArrangeToolbar(); } };
            downloadsView.FindHandler = new Web.FindResults((source, total, ordinal, final) => Dispatch(() => findBar.ShowResult(source, total, ordinal, final)));
            downloadsView.Shortcut += key => { if (!closing) { var message = new System.Windows.Forms.Message(); ProcessCmdKey(ref message, key); } };
            canvas.Controls.Add(downloadsView);
        }
        private void ShowDownloads(DownloadRecord preview = null)
        {
            if (LockShown) return;
            if (closing || downloadsView == null) return;
            CloseWeb();
            var opening = !downloadsView.Visible;
            if (opening) QueueAsync(PruneDownloadsAsync);
            // Over the website, newly shown child windows are black until each paints. A plain page-coloured cover
            // is painted first, the page is built and painted beneath it, and the cover then steps aside.
            var reveal = opening && !selfTest && !accountReview;
            if (reveal) { pageCover.Visible = true; pageCover.BringToFront(); pageCover.Refresh(); }
            downloadsView.Visible = true; downloadsView.BringToFront(); UpdateOffline();
            if (reveal) pageCover.BringToFront();
            if (preview != null) downloadsView.Preview(preview); else downloadsView.ClosePreview();
            UpdateLocation(); UpdateHistoryButtons(); ArrangeToolbar();
            if (opening || preview != null || toast.ContainsFocus) downloadsView.FocusWorkspace();
            if (!reveal) { if (opening) downloadsView.AnimateEntrance(); return; }
            Kit.PaintNow(downloadsView);
            var generation = ++coverGeneration;
            QueueAsync(async () => { await System.Threading.Tasks.Task.Delay(60); if (generation == coverGeneration && downloadsView.Visible) { pageCover.Visible = false; Kit.PaintNow(downloadsView); downloadsView.AnimateEntrance(); } });
        }
        private int coverGeneration;
        private void HideDownloads()
        {
            if (!DownloadsVisible) return;
            ++coverGeneration; pageCover.Visible = false;
            downloadsView.ClosePreview(); downloadsView.Visible = false; downloadsView.ReleaseViewer();
            if (loading.Visible) loading.BringToFront();
            UpdateOffline();
            UpdateLocation(); UpdateHistoryButtons(); ArrangeToolbar();
        }
        private void ForgetRecord(string id)
        {
            if (history == null) return;
            history.Remove(id); savedStates.Remove(id); downloadsView.Remove(id);
            if (activeDownload?.Id == id) activeDownload = history.Items.FirstOrDefault(x => x.State == DownloadState.Running);
            // The row was a teacher's link, not a file: with nothing else arriving, «Скачивание…» goes with it.
            if (activeDownload == null) messages.Withdraw("downloading");
            _ = SaveHistoryAsync(); if (!closing) RenderFeedback();
        }
        // Files deleted, moved or renamed since they were saved leave the list. The disk is read off the UI thread, twice a
        // moment apart (a file being replaced is not mistaken for a gone one), and only a record that still points at the
        // same place is removed; one save covers them all. Unknown (an offline drive or share) keeps the record.
        private bool pruning, pruneAgain;
        private async System.Threading.Tasks.Task PruneDownloadsAsync()
        {
            if (history == null || closing) return;
            if (pruning) { pruneAgain = true; return; }
            var candidates = history.Items.Where(x => x.State == DownloadState.Completed && x.Path.Length > 0).Select(x => new KeyValuePair<string, string>(x.Id, x.Path)).ToList();
            if (candidates.Count == 0) return;
            pruning = true;
            List<KeyValuePair<string, string>> gone;
            try
            {
                gone = await System.Threading.Tasks.Task.Run(async () =>
                {
                    var first = candidates.Where(x => DownloadHistory.Locate(x.Value) == FilePresence.Gone).ToList();
                    if (first.Count == 0) return first;
                    await System.Threading.Tasks.Task.Delay(PruneRecheck);
                    return first.Where(x => DownloadHistory.Locate(x.Value) == FilePresence.Gone).ToList();
                });
            }
            finally { pruning = false; }
            if (closing || history == null) return;
            var removed = new List<string>();
            foreach (var pair in gone)
            {
                var record = history.Items.FirstOrDefault(x => x.Id == pair.Key);
                if (record == null || record.State != DownloadState.Completed || !string.Equals(record.Path, pair.Value, StringComparison.OrdinalIgnoreCase)) continue;
                history.Remove(record.Id); savedStates.Remove(record.Id); removed.Add(record.Id);
            }
            if (removed.Count > 0) { downloadsView.Remove(removed); _ = SaveHistoryAsync(); if (!closing) { RenderFeedback(); if (DownloadsVisible) UpdateHeading(); } }
            if (pruneAgain) { pruneAgain = false; await PruneDownloadsAsync(); }
        }
        private readonly Dictionary<string, Action<DownloadRecord>> rechecking = new Dictionary<string, Action<DownloadRecord>>();
        private static readonly TimeSpan PruneRecheck = TimeSpan.FromMilliseconds(1500);
        // Open and Show in folder on a file that looks gone check again a moment later, like the background tidy-up: gone
        // twice, the record leaves the list and the status says why; back again (a file being replaced), the action runs.
        private void WhenPresent(DownloadRecord record, Action<DownloadRecord> action)
        {
            if (record == null || DownloadHistory.Locate(record.Path) != FilePresence.Gone) { action(record); return; }
            var id = record.Id; var path = record.Path;
            // Clicks while the same record is being checked again share that recheck; the latest click's action runs.
            var pending = rechecking.ContainsKey(id); rechecking[id] = action;
            if (pending) return;
            QueueAsync(async () =>
            {
                bool gone; Action<DownloadRecord> latest;
                try { gone = await DownloadHistory.StillGoneAsync(path, PruneRecheck); }
                finally { if (!rechecking.TryGetValue(id, out latest)) latest = action; rechecking.Remove(id); }
                if (closing || history == null) return;
                var current = history.Items.FirstOrDefault(x => x.Id == id);
                if (current == null || !string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase)) return;
                if (gone) { ForgetRecord(id); SetStatus("downloadGone"); } else latest(current);
            });
        }
        private void OnDownloadChanged(DownloadRecord record)
        {
            if (history == null) return;
            // A file the page just handed over travels to where it now lives.
            if (record.State == DownloadState.Running && !savedStates.ContainsKey(record.Id) && !string.IsNullOrEmpty(record.FileName)) FlyToDownloads(record);
            history.Upsert(record.Clone()); downloadsView.Upsert(record);
            if (record.State == DownloadState.Completed) lastDownloadPath = record.Path;
            if (record.State == DownloadState.Running) activeDownload = record;
            else if (activeDownload?.Id == record.Id) activeDownload = history.Items.FirstOrDefault(x => x.State == DownloadState.Running);
            DownloadState previous;
            if (!savedStates.TryGetValue(record.Id, out previous) || previous != record.State) { savedStates[record.Id] = record.State; _ = SaveHistoryAsync(); }
            if (!closing) { RenderFeedback(); if (DownloadsVisible) UpdateHeading(); }
        }
        private void FlyToDownloads(DownloadRecord record)
        {
            if (DownloadsVisible || !Kit.Moving(downloadsButton) || WindowState == System.Windows.Forms.FormWindowState.Minimized) return;
            var chip = Kit.FileChip(record.FileName, FileKinds.Badge(FileKinds.Of(record.FileName)), DownloadRow.Tone(record.FileName), D(100) / 100f);
            var target = downloadsButton.RectangleToScreen(downloadsButton.ClientRectangle);
            var at = System.Windows.Forms.Cursor.Position;
            if (!RectangleToScreen(ClientRectangle).Contains(at)) at = workspace.PointToScreen(new System.Drawing.Point(workspace.Width / 2, workspace.Height / 2));
            MotionPopup.Fly(chip, new System.Drawing.Point(at.X - chip.Width / 2, at.Y - chip.Height / 2), new System.Drawing.Point(target.Left + target.Height / 2, target.Top + target.Height / 2));
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
        private void OpenRecordOutside(DownloadRecord record) { WhenPresent(record, OpenRecordNow); }
        private void OpenRecordNow(DownloadRecord record)
        {
            if (record == null || !File.Exists(record.Path) || !FileNames.CanOpen(record.Path)) { SetStatus("downloadOpenFailed"); return; }
            try { Process.Start(new ProcessStartInfo(record.Path) { UseShellExecute = true }); }
            catch (Exception) { SetStatus("downloadOpenFailed"); }
        }
        private void ShowRecordInFolder(DownloadRecord record) { WhenPresent(record, ShowRecordNow); }
        private void ShowRecordNow(DownloadRecord record)
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
