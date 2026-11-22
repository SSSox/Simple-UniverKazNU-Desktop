using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Where a deleted file goes. A normal run gets the Recycle Bin from Program; self-test, the review and the isolated
        // checks keep this recording copy, so no check ever touches the user's Downloads folder or Recycle Bin.
        internal IFileRecycler Recycler = new MemoryRecycler();
        // Self-test answers the confirmation without a window; null asks the user.
        internal Func<DownloadRecord, bool> deleteAnswerForQa;
        private bool deleteOpen;
        // «Удалить файл»: ask first, let the preview release the file, move it to the Recycle Bin, and only then take the
        // row and its record off the list. A refusal or a failure says why and leaves the row where it is.
        private async Task DeleteDownloadAsync(DownloadRecord shown)
        {
            if (deleteOpen || history == null || closing || LockShown || shown == null) return;
            var id = shown.Id;
            var record = history.Items.FirstOrDefault(x => x.Id == id);
            if (record == null) return;
            deleteOpen = true;
            try
            {
                var snapshot = record.Clone();
                var verdict = await Task.Run(() => FileDeletion.Check(snapshot));
                if (closing || history == null) return;
                if (verdict != DeleteVerdict.Ok) { Refuse(id, snapshot, verdict); return; }
                if (!ConfirmDelete(snapshot) || closing || history == null || LockShown) return;
                // The record may have changed, or gone, while the question was open.
                record = history.Items.FirstOrDefault(x => x.Id == id);
                if (record == null || !string.Equals(record.Path, snapshot.Path, StringComparison.OrdinalIgnoreCase)) return;
                snapshot = record.Clone();
                if (downloadsView.PreviewId == id) { downloadsView.ClosePreview(); downloadsView.ReleaseViewer(); }
                var recycler = Recycler;
                verdict = await Task.Run(() => FileDeletion.Delete(snapshot, recycler));
                if (closing || history == null) return;
                if (verdict == DeleteVerdict.Ok) { ForgetRecord(id); SetStatus("fileDeleted"); }
                else Refuse(id, snapshot, verdict);
            }
            finally { deleteOpen = false; }
        }
        private void Refuse(string id, DownloadRecord snapshot, DeleteVerdict verdict)
        {
            // A file that is not there any more has nothing to delete: the row goes, as when it is opened.
            if (verdict == DeleteVerdict.Missing) { ForgetRecord(id); SetStatus("downloadGone"); return; }
            SetStatus(FileDeletion.StatusKey(verdict));
        }
        private bool ConfirmDelete(DownloadRecord record)
        {
            if (deleteAnswerForQa != null) return deleteAnswerForQa(record);
            var lang = prefs.Language;
            var name = string.IsNullOrEmpty(record.FileName) ? System.IO.Path.GetFileName(record.Path) : record.FileName;
            using (var form = new ChoiceForm(Strings.Get("deleteFileTitle", lang), string.Format(Strings.Get("deleteFileMessage", lang), name),
                Strings.Get("deleteFileAction", lang), Strings.Get("lockCancel", lang), true))
                return form.ShowDialog(this) == DialogResult.Yes;
        }
    }
}
