using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Isolated QA: synthetic files in a temp folder and a recording recycler; the real Downloads folder and Recycle Bin
        // are never touched. The confirmation is answered by a hook instead of a window.
        private async Task DeleteQaAsync(List<string> results)
        {
            var folder = Path.Combine(Path.GetTempPath(), "Univer.Desktop.QA", "delete-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var saved = Recycler; var recorder = new MemoryRecycler();
            Check(saved is MemoryRecycler, "self-test never gets the real Recycle Bin", results);
            var ids = new List<string>();
            try
            {
                Recycler = recorder;
                Func<string, int, DownloadRecord> make = (name, size) =>
                {
                    var path = Path.Combine(folder, name); File.WriteAllBytes(path, new byte[size]);
                    var record = new DownloadRecord { FileName = name, Path = path, State = DownloadState.Completed, Received = size, Total = size, Finished = DateTime.Now, Source = "QA" };
                    ids.Add(record.Id); return record;
                };
                var good = make("Лекция QA.pdf", 20); var changed = make("Изменённый QA.pdf", 30); var shownText = make("Просмотр QA.txt", 40);
                good.Key = "qa-key"; changed.Total = changed.Received = 31; // the file on disk is a byte shorter than the record says
                var running = new DownloadRecord { FileName = "Идёт QA.pdf", State = DownloadState.Running, Received = 5, Total = 50 }; ids.Add(running.Id);
                foreach (var record in new[] { good, changed, shownText, running }) OnDownloadChanged(record);
                ShowDownloads(); await Task.Delay(150);
                await RestoreQaWindowAsync();

                Func<string, ToolStripItem> entry = id => (downloadsView.MenuForQa(id) ?? new List<ToolStripItem>()).FirstOrDefault(x => x.Text == Strings.Get("deleteFile", prefs.Language));
                var item = entry(good.Id);
                Check(item != null && item.Enabled, "a finished file's menu offers «Удалить файл»", results);
                Check(entry(running.Id) == null, "a running download's menu has no delete", results);
                // The menu text fits in every language: no longer than the longest entry the menu already had.
                foreach (var lang in new[] { "ru", "kz", "en" })
                {
                    var delete = TextRenderer.MeasureText(Strings.Get("deleteFile", lang), downloadsView.MenuFontForQa).Width;
                    var longest = TextRenderer.MeasureText(Strings.Get("removeFromList", lang), downloadsView.MenuFontForQa).Width;
                    Check(delete <= longest, "the delete entry fits the menu in " + lang, results);
                }

                // A declined question changes nothing.
                var asked = new List<string>(); var answer = false;
                deleteAnswerForQa = record => { asked.Add(record.Path); return answer; };
                entry(good.Id).PerformClick();
                await WaitDeleteQaAsync(() => asked.Count > 0);
                await Task.Delay(100);
                Check(asked.Count == 1 && recorder.Calls.Count == 0 && downloadsView.HasRowForQa(good.Id) && history.Items.Any(x => x.Id == good.Id), "declining the question keeps the file, the row and the record", results);

                // An accepted one hands exactly that path over, then the row and the record go.
                answer = true; var wasKnown = history.Downloaded("qa-key", good.FileName, "QA") != null;
                entry(good.Id).PerformClick();
                await WaitDeleteQaAsync(() => !downloadsView.HasRowForQa(good.Id));
                Check(recorder.Calls.Count == 1 && recorder.Calls[0] == good.Path && asked.Count == 2 && asked[1] == good.Path, "the recycler gets exactly the record's path after the question", results);
                Check(!downloadsView.HasRowForQa(good.Id) && history.Items.All(x => x.Id != good.Id) && messages.Current?.Key == "fileDeleted", "the row and record leave and the status says the file was moved", results);

                // A file that is not the one the app saved is refused before any question, and its row stays.
                var before = asked.Count;
                entry(changed.Id).PerformClick();
                await WaitDeleteQaAsync(() => messages.Current?.Key == "deleteChanged");
                Check(asked.Count == before && recorder.Calls.Count == 1 && downloadsView.HasRowForQa(changed.Id) && history.Items.Any(x => x.Id == changed.Id) && messages.Current?.Key == "deleteChanged",
                    "a file of another size is refused with its reason and the row stays", results);
                // No Recycle Bin: refused, never deleted for good.
                recorder.Available = false;
                var again = make("Флешка QA.pdf", 12); OnDownloadChanged(again);
                await DeleteDownloadAsync(again);
                Check(recorder.Calls.Count == 1 && File.Exists(again.Path) && downloadsView.HasRowForQa(again.Id) && messages.Current?.Key == "deleteNoBin", "a drive without a Recycle Bin refuses and keeps the file", results);
                recorder.Available = true;

                // The shown file is let go first: its preview closes before the move.
                downloadsView.PreferBuiltIn = true;
                downloadsView.Preview(shownText); await Task.Delay(300);
                var showing = downloadsView.Previewing && downloadsView.PreviewId == shownText.Id;
                var duringMove = false;
                deleteAnswerForQa = record => true;
                await DeleteDownloadAsync(shownText);
                duringMove = !downloadsView.Previewing;
                Check(showing && duringMove && recorder.Calls.Count == 2 && recorder.Calls[1] == shownText.Path && !downloadsView.HasRowForQa(shownText.Id), "deleting the file shown in the preview closes the preview first", results);

                Check(recorder.Owners.Count == 2 && recorder.Owners.All(x => x == Handle), "the recycler is handed the main window as the owner of the shell's question", results);

                // A file another program holds is "busy", not "no bin": it is kept and the status says so.
                recorder.Busy = true;
                var held = make("Занят QA.pdf", 14); OnDownloadChanged(held);
                await DeleteDownloadAsync(held);
                Check(recorder.Calls.Count == 2 && File.Exists(held.Path) && downloadsView.HasRowForQa(held.Id) && messages.Current?.Key == "deleteBusy", "a file open elsewhere is refused as busy and kept", results);
                recorder.Busy = false;

                // The preview's release is awaited with a bound: a holder that lets go after a moment is waited for.
                using (var holder = new FileStream(held.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var letGo = Task.Delay(350).ContinueWith(_ => holder.Dispose());
                    var released = await FileDeletion.WaitReleasedAsync(held.Path);
                    watch.Stop();
                    Check(released && watch.ElapsedMilliseconds >= 250 && watch.ElapsedMilliseconds < 1800, "the wait for a preview to let go ends when the file opens", results);
                    await letGo;
                }

                // The real shell path, short of moving anything into the user's bin: struct layout, drive decision, STA call.
                int structSize, flagsAt, abortedAt;
                RecycleBinRecycler.Layout(out structSize, out flagsAt, out abortedAt);
                var x64 = IntPtr.Size == 8;
                Check(structSize == (x64 ? 56 : 30) && flagsAt == (x64 ? 32 : 16) && abortedAt == (x64 ? 36 : 18), "SHFILEOPSTRUCT has the shell's layout (" + structSize + "/" + flagsAt + "/" + abortedAt + " on " + (x64 ? "x64" : "x86") + ")", results);
                var real = new RecycleBinRecycler();
                var probe = make("Реальный QA.pdf", 8);
                var tempFixed = false;
                try { tempFixed = new DriveInfo(Path.GetPathRoot(folder)).DriveType == DriveType.Fixed; } catch (Exception) { }
                Check(real.Support(probe.Path) == (tempFixed ? RecycleSupport.Yes : RecycleSupport.NoBin), "a file on a fixed drive is accepted by its resolved location", results);
                using (new FileStream(probe.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                    Check(real.Support(probe.Path) == RecycleSupport.Busy, "a file held without sharing is busy for the real recycler", results);
                Check(real.Support(Path.Combine(folder, "нет-такого.pdf")) == RecycleSupport.NoBin, "a missing file is not a bin question", results);
                var ranOnUi = true; var crashed = false;
                try { await real.RecycleAsync(Path.Combine(folder, "нет-такого.pdf"), Handle); } catch (IOException) { } catch (Exception) { crashed = true; }
                ranOnUi = RecycleBinRecycler.LastApartment != System.Threading.ApartmentState.STA;
                Check(!crashed && !ranOnUi, "the real shell call runs on its own STA thread with the window as owner and ends cleanly", results);

                // A repeat download of the same address is not called a repeat once the record is gone.
                Check(wasKnown && history.Downloaded("qa-key", good.FileName, "QA") == null, "after a deletion the same file is no longer 'already downloaded'", results);
            }
            finally
            {
                deleteAnswerForQa = null; downloadsView.PreferBuiltIn = false; Recycler = saved;
                downloadsView.ClosePreview();
                foreach (var id in ids) ForgetRecord(id);
                messages.Withdraw("fileDeleted"); messages.Withdraw("deleteChanged"); messages.Withdraw("deleteNoBin"); messages.Withdraw("deleteBusy"); RenderFeedback();
                HideDownloads();
                downloadsView.SetItems(history.Items);
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        private static async Task WaitDeleteQaAsync(Func<bool> done)
        {
            var until = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < until && !done()) await Task.Delay(50);
        }
    }
}
