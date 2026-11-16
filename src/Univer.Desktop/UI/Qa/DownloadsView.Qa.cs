using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CefSharp.WinForms;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    // What the self-test reads and presses on the Downloads page.
    internal sealed partial class DownloadsView
    {
        internal ChromiumWebBrowser ViewerForQa { get { return preview.ViewerForQa; } }
        internal bool OfficeForQa { get { return preview.OfficeForQa; } }
        internal bool ReaderForQa { get { return preview.ReaderForQa; } }
        internal string MessageForQa { get { return preview.MessageForQa; } }
        internal TextBox SearchForQa { get { return search.Input; } }
        internal DownloadSort SortForQa { get { return order; } set { SetOrder(value); } }
        internal string SubjectForQa { get { return subject; } set { SetSubject(value); } }
        internal string[] SubjectsForQa { get { return Subjects().Select(x => x.Key + "=" + x.Value).ToArray(); } }
        internal ModernButton SubjectButtonForQa { get { return subjects; } }
        internal IEnumerable<ModernButton> ChipsForQa { get { return chips.Controls.Cast<ModernButton>(); } }
        internal PickList OpenSubjectsForQa() { OpenSubjects(); return subjectMenu.ListForQa; }
        internal void CloseMenusForQa() { subjectMenu.Close(); sortMenu.Close(); }
        internal string[] SubjectGroupsForQa { get { var known = Subjects(); return new[] { known.Any(x => !FileKinds.PageSource(x.Key)) ? Strings.Get("subjectCourses", language) : "", known.Any(x => FileKinds.PageSource(x.Key)) ? Strings.Get("subjectPages", language) : "" }; } }
        internal bool FilterRowFitsForQa { get { return search.Right <= (hasSubjects ? subjects.Left : sort.Left) && (!hasSubjects || subjects.Right <= sort.Left && subjects.Top == sort.Top) && sort.Right <= folder.Left; } }
        internal string[] RowOrderForQa { get { return rows.Values.OrderBy(r => (int)r.Tag).Select(r => r.AccessibleName).ToArray(); } }
        internal int RowsForQa { get { return rows.Count; } }
        internal bool HasRowForQa(string id) { return rows.ContainsKey(id); }
        // Rows out of view catch up in batches; this says they all did, each at the width the list gives it.
        internal bool RowsSettledForQa { get { SettleAll(); return rows.Values.All(r => r.Width == Math.Max(D(200), sheet.Width - D(48)) && !parked.Contains(r)) && !spare.Any(r => r.Visible); } }
        internal bool ActionsPlacedForQa(string id) { DownloadRow row; return rows.TryGetValue(id, out row) && row.ActionsPlacedForQa; }
        internal bool CanOpenForQa(string id) { DownloadRow row; return rows.TryGetValue(id, out row) && row.CanOpen; }
        internal string HeadingForQa(string id) { DownloadRow row; return rows.TryGetValue(id, out row) ? row.Heading : null; }
    }
}
