using System;
using System.Collections.Generic;
using System.Text;

namespace Univer.Desktop.Core
{
    // The last built page script and the plain values it was built from. The app asks every tick, and the settings
    // object is changed in place, so the key is the values themselves (joined with separators that cannot occur in
    // them), never the object. One entry; UI-thread use.
    internal sealed class ViewCache
    {
        private string key, script;
        public int Builds { get; private set; }
        public string Get(string newKey, Func<string> build)
        {
            if (script != null && string.Equals(key, newKey, StringComparison.Ordinal)) return script;
            var built = build();
            key = newKey; script = built; Builds++;
            return built;
        }
        // Flags and texts, then each list's items in order; an item ends with a unit separator, a group with a record one.
        public static string Key(IEnumerable<string> singles, params IEnumerable<string>[] lists)
        {
            var sb = new StringBuilder();
            foreach (var single in singles) sb.Append(single ?? "").Append('\u001e');
            foreach (var list in lists)
            {
                if (list != null) foreach (var item in list) sb.Append(item ?? "").Append('\u001f');
                sb.Append('\u001e');
            }
            return sb.ToString();
        }
    }
}
