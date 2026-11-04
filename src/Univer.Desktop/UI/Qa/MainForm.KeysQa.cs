using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // Manual keyboard check (--qa-keys) in an isolated profile with a synthetic PDF and RTF: every shortcut the previews hand
        // to the app goes to qa-keys.trace, while a person or desktop tool presses the keys through the real input path.
        private Task KeysQaAsync()
        {
            var trace = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "qa-keys.trace");
            File.WriteAllText(trace, "qa-keys-start\n");
            var folder = Path.Combine(storage.Root, "qa-files"); Directory.CreateDirectory(folder);
            var pdf = Path.Combine(folder, "Keys QA.pdf"); File.WriteAllBytes(pdf, SyntheticPdf("Keys QA"));
            var rtf = Path.Combine(folder, "Keys QA.rtf"); File.WriteAllText(rtf, "{\\rtf1\\ansi Keys QA: press Escape here\\par}");
            // --qa-keys-file=<path> adds one more local file (for example an Excel or PowerPoint document) and previews it.
            var extra = Array.Find(Environment.GetCommandLineArgs(), a => a.StartsWith("--qa-keys-file=", StringComparison.Ordinal));
            var supplied = extra == null ? null : extra.Substring("--qa-keys-file=".Length);
            if (supplied != null && !File.Exists(supplied)) supplied = null;
            foreach (var path in supplied != null ? new[] { rtf, pdf, supplied } : new[] { rtf, pdf })
            {
                var size = new FileInfo(path).Length;
                OnDownloadChanged(new DownloadRecord { FileName = Path.GetFileName(path), Path = path, State = DownloadState.Completed, Total = size, Received = size, Finished = DateTime.Now });
            }
            downloadsView.Shortcut += key => File.AppendAllText(trace, "shortcut " + key + " previewing=" + downloadsView.Previewing + " find=" + findBar.Searching + "\n");
            findBar.Trace = line => File.AppendAllText(trace, "find-result " + line + " text=" + findBar.InputForQa.TextLength + " shown=" + findBar.CountForQa + "\n");
            findBar.VisibleChanged += (s, e) => File.AppendAllText(trace, "find-bar " + (findBar.Visible ? "open" : "closed") + "\n");
            downloadsView.PreferBuiltIn = false;
            ShowDownloads();
            // --qa-keys-rtf opens the Windows (Word) preview, otherwise the PDF opens in Chromium's viewer.
            var office = Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-keys-rtf") >= 0;
            var target = supplied ?? (office ? rtf : pdf);
            var record = history.Items.FirstOrDefault(x => x.Path == target);
            if (record != null) ShowDownloads(record);
            File.AppendAllText(trace, "preview " + Path.GetExtension(target).TrimStart('.') + "\n");
            return Task.FromResult(true);
        }
    }
}
