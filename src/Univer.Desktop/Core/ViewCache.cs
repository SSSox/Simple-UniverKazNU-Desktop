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
        private static readonly string[] EmptyStrings = new string[0];
        private string key, script, language;
        private bool hasSnapshot, animations, dark, readable, viewer;
        private string[] pinTitles = EmptyStrings, pinUrls = EmptyStrings, tileOrder = EmptyStrings, hiddenTiles = EmptyStrings;
        public int Builds { get; private set; }
        public string Get(string newKey, Func<string> build)
        {
            if (script != null && !hasSnapshot && string.Equals(key, newKey, StringComparison.Ordinal)) return script;
            var built = build();
            hasSnapshot = false; key = newKey; script = built; Builds++;
            return built;
        }
        // Allocation-free lookup against the current preferences, theme and viewer flags: repeated ticks do not build
        // temporary arrays, iterators, StringBuilders, key strings or closures on a cache hit.
        public bool TryGet(Preferences prefs, bool isDark, bool isViewer, out string cached)
        {
            if (hasSnapshot && script != null && Matches(prefs, isDark, isViewer))
            {
                cached = script;
                return true;
            }
            cached = null;
            return false;
        }
        public string Store(Preferences prefs, bool isDark, bool isViewer, string built)
        {
            animations = prefs != null && prefs.Animations;
            dark = isDark;
            readable = prefs != null && prefs.Readable;
            viewer = isViewer;
            language = prefs != null ? prefs.Language ?? "" : "";
            SnapshotPins(prefs != null ? prefs.Pins : null);
            tileOrder = SnapshotList(prefs != null ? prefs.TileOrder : null);
            hiddenTiles = SnapshotList(prefs != null ? prefs.HiddenTiles : null);
            hasSnapshot = true; key = null; script = built; Builds++;
            return built;
        }
        private bool Matches(Preferences prefs, bool isDark, bool isViewer)
        {
            var nextAnimations = prefs != null && prefs.Animations;
            var nextReadable = prefs != null && prefs.Readable;
            var nextLanguage = prefs != null ? prefs.Language ?? "" : "";
            if (animations != nextAnimations || dark != isDark || readable != nextReadable || viewer != isViewer) return false;
            if (!string.Equals(language, nextLanguage, StringComparison.Ordinal)) return false;
            if (!PinsMatch(prefs != null ? prefs.Pins : null)) return false;
            if (!ListMatches(prefs != null ? prefs.TileOrder : null, tileOrder)) return false;
            return ListMatches(prefs != null ? prefs.HiddenTiles : null, hiddenTiles);
        }
        private bool PinsMatch(List<PinnedLink> pins)
        {
            var count = pins == null ? 0 : pins.Count;
            if (count != pinTitles.Length) return false;
            for (var i = 0; i < count; i++)
            {
                var pin = pins[i];
                var title = pin != null ? pin.Title ?? "" : "";
                var url = pin != null ? pin.Url ?? "" : "";
                if (!string.Equals(pinTitles[i], title, StringComparison.Ordinal) ||
                    !string.Equals(pinUrls[i], url, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
        private static bool ListMatches(List<string> list, string[] snapshot)
        {
            var count = list == null ? 0 : list.Count;
            if (count != snapshot.Length) return false;
            for (var i = 0; i < count; i++)
                if (!string.Equals(snapshot[i], list[i] ?? "", StringComparison.Ordinal)) return false;
            return true;
        }
        private void SnapshotPins(List<PinnedLink> pins)
        {
            var count = pins == null ? 0 : pins.Count;
            if (count == 0) { pinTitles = EmptyStrings; pinUrls = EmptyStrings; return; }
            var titles = new string[count];
            var urls = new string[count];
            for (var i = 0; i < count; i++)
            {
                var pin = pins[i];
                titles[i] = pin != null ? pin.Title ?? "" : "";
                urls[i] = pin != null ? pin.Url ?? "" : "";
            }
            pinTitles = titles;
            pinUrls = urls;
        }
        private static string[] SnapshotList(List<string> list)
        {
            var count = list == null ? 0 : list.Count;
            if (count == 0) return EmptyStrings;
            var copy = new string[count];
            for (var i = 0; i < count; i++) copy[i] = list[i] ?? "";
            return copy;
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
