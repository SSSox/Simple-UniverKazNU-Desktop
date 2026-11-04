using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Univer.Desktop.Core
{
    // A local list of files downloaded through the app. It contains names and local paths only,
    // never URLs with session state, cookies or credentials.
    internal sealed class DownloadHistory
    {
        public const int Limit = 1000;
        private readonly string path;
        private readonly AsyncOperationGate writes = new AsyncOperationGate();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private readonly List<DownloadRecord> items = new List<DownloadRecord>();
        public DownloadHistory(string root) { path = System.IO.Path.Combine(root, "downloads.json"); }
        public IList<DownloadRecord> Items { get { return items; } }
        // A network path outside folder loses its path, so the list never touches a remote share; local paths stay.
        public void Load(string folder = null)
        {
            items.Clear();
            if (!File.Exists(path)) return;
            List<DownloadRecord> loaded;
            try { loaded = json.Deserialize<List<DownloadRecord>>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception) { loaded = null; } // A damaged list must not stop the app; it is rebuilt from new downloads.
            if (loaded == null) return;
            foreach (var item in loaded)
            {
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;
                item.Path = item.Path ?? ""; item.FileName = item.FileName ?? ""; item.Source = item.Source ?? ""; item.Error = item.Error ?? ""; item.Key = item.Key ?? "";
                if (item.Path.Length > 0 && (!System.IO.Path.IsPathRooted(item.Path) || item.Path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)) continue;
                // Only a completed file has a path.
                if (item.State != DownloadState.Completed || (folder != null && Remote(item.Path) && !Inside(item.Path, folder))) item.Path = "";
                // JavaScriptSerializer stores times as UTC and reads them back as UTC; the list shows local time.
                item.Started = Local(item.Started); if (item.Finished.HasValue) item.Finished = Local(item.Finished.Value);
                // A process that ended mid-download cannot resume it.
                if (item.State == DownloadState.Running) { item.State = DownloadState.Failed; item.Error = "downloadInterrupted"; }
                items.Add(item);
            }
            Trim();
        }
        // UNC, \\?\ and \\.\ paths; checking one starts an SMB connection.
        internal static bool Remote(string path) { return path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal); }
        internal static bool Inside(string path, string folder)
        {
            try
            {
                var parent = System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                var full = System.IO.Path.GetFullPath(path);
                return full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && full.IndexOf(System.IO.Path.DirectorySeparatorChar, parent.Length) < 0;
            }
            catch (Exception) { return false; }
        }
        private static DateTime Local(DateTime value) { return value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value; }
        public void Upsert(DownloadRecord item)
        {
            var index = items.FindIndex(x => x.Id == item.Id);
            if (index >= 0) items[index] = item; else items.Insert(0, item);
            Trim();
        }
        public bool Remove(string id) { return items.RemoveAll(x => x.Id == id) > 0; }
        // The newest completed download of the same address whose file is still on disk. Entries saved before addresses
        // were recognised fall back to the same caption from the same course.
        public DownloadRecord Downloaded(string key, string caption, string source)
        {
            var bare = string.IsNullOrWhiteSpace(caption) ? "" : DownloadKeys.Bare(FileNames.Safe(caption));
            return items.FirstOrDefault(x => x.State == DownloadState.Completed && x.Path.Length > 0 &&
                (key.Length > 0 && x.Key == key || x.Key.Length == 0 && bare.Length > 0 && DownloadKeys.Bare(x.FileName) == bare && string.Equals(x.Source, source ?? "", StringComparison.CurrentCultureIgnoreCase)) &&
                File.Exists(x.Path));
        }
        private void Trim() { if (items.Count > Limit) items.RemoveRange(Limit, items.Count - Limit); }
        public Task SaveAsync()
        {
            var snapshot = items.Select(x => x.Clone()).ToList();
            return writes.RunAsync(() => Task.Run(() =>
            {
                AtomicFile.Write(path, new UTF8Encoding(false).GetBytes(json.Serialize(snapshot)));
            }));
        }
    }
}
