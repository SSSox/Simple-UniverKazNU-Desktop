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

                // A repeat download of the same address is not called a repeat once the record is gone.
                Check(wasKnown && history.Downloaded("qa-key", good.FileName, "QA") == null, "after a deletion the same file is no longer 'already downloaded'", results);
            }
            finally
            {
                deleteAnswerForQa = null; downloadsView.PreferBuiltIn = false; Recycler = saved;
                downloadsView.ClosePreview();
                foreach (var id in ids) ForgetRecord(id);
                messages.Withdraw("fileDeleted"); messages.Withdraw("deleteChanged"); messages.Withdraw("deleteNoBin"); RenderFeedback();
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
