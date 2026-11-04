using System;
using System.Collections.Generic;
using System.Linq;

namespace Univer.Desktop.Core
{
    internal enum DownloadSort { Newest, Name, Size }
    // The order of the "Загрузки" list. Ties keep the newest file first, so a re-sort never shuffles equal rows.
    internal static class DownloadOrder
    {
        public static List<DownloadRecord> Apply(IEnumerable<DownloadRecord> items, DownloadSort sort)
        {
            switch (sort)
            {
                case DownloadSort.Name: return items.OrderBy(x => x.FileName ?? "", NaturalComparer.Instance).ThenByDescending(x => x.Started).ToList();
                case DownloadSort.Size: return items.OrderByDescending(Size).ThenByDescending(x => x.Started).ToList();
                default: return items.OrderByDescending(x => x.Started).ToList();
            }
        }
        // A finished file reports its total; a download without Content-Length only knows what was received.
        public static long Size(DownloadRecord item) { return item.Total >= 0 ? item.Total : item.Received; }
        // Explorer-like names: "Лекция 2" comes before "Лекция 10"; letters compare in the user's culture, ignoring case.
        internal sealed class NaturalComparer : IComparer<string>
        {
            public static readonly NaturalComparer Instance = new NaturalComparer();
            public int Compare(string a, string b)
            {
                a = a ?? ""; b = b ?? "";
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    int si = i, sj = j;
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        var x = a.Substring(si, i - si).TrimStart('0'); var y = b.Substring(sj, j - sj).TrimStart('0');
                        if (x.Length != y.Length) return x.Length.CompareTo(y.Length);
                        var digits = string.CompareOrdinal(x, y); if (digits != 0) return digits;
                    }
                    else
                    {
                        // At least one side starts with a non-digit, so each pass consumes text.
                        while (i < a.Length && !char.IsDigit(a[i])) i++;
                        while (j < b.Length && !char.IsDigit(b[j])) j++;
                        var text = string.Compare(a.Substring(si, i - si), b.Substring(sj, j - sj), StringComparison.CurrentCultureIgnoreCase);
                        if (text != 0) return text;
                    }
                }
                return (a.Length - i).CompareTo(b.Length - j);
            }
        }
    }
}
