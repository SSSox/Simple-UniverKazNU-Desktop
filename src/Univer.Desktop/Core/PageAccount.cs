using System;
using System.Globalization;

namespace Univer.Desktop.Core
{
    // The account the site's pages belong to, as the page itself notes it: when the sign-in form is sent, univer-automation.js
    // stores 'a' + the FNV-1a hash (32 bits, hex) of the lower-cased, trimmed login under localStorage['univer.app.account.v1']
    // (the app's own sign-in and a manual one alike). The same hash here lets the app check that what a page shows is the
    // saved login's, not another student's who signed in by hand after the site's own logout. The JS and C# marks are pinned by
    // the same vectors (PageAccountTests, tests/dom/automation.test.cjs). Only the characters .NET and JS case-fold alike
    // (Latin, Cyrillic) are promised equal.
    internal static class PageAccount
    {
        internal const string Key = "univer.app.account.v1";
        internal static string Mark(string login)
        {
            var text = (login ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0) return "";
            var hash = 0x811c9dc5u;
            unchecked { foreach (var ch in text) { hash ^= ch; hash *= 0x01000193u; } }
            return "a" + hash.ToString("x", CultureInfo.InvariantCulture);
        }
        // The page's reading may be kept or announced for `login` when the page has no mark (a session from before the mark
        // existed; a new profile always gets one at its first sign-in) or its mark is this login's. No login: never.
        internal static bool Accepts(string login, string pageMark)
        {
            var own = Mark(login);
            return own.Length > 0 && (string.IsNullOrEmpty(pageMark) || string.Equals(pageMark, own, StringComparison.Ordinal));
        }
        // A script that answers the page's mark, a line break and what the reader `call` (an expression giving a string) answers,
        // in one evaluation, so the mark belongs to the same moment as the reading.
        internal static string Script(string call)
        {
            return "(function(){var m='';try{m=String(localStorage.getItem('" + Key + "')||'').replace(/[\\r\\n]/g,'?')}catch(_){}var r=" + call + ";return m+'\\n'+(typeof r==='string'?r:'')})()";
        }
        // The page's mark alone, for a reader that asks no page (the background grade check).
        internal static readonly string MarkScript = "(function(){try{return String(localStorage.getItem('" + Key + "')||'').replace(/[\\r\\n]/g,'?')}catch(_){return ''}})()";
        internal static bool Split(string answer, out string mark, out string raw)
        {
            mark = raw = null;
            if (answer == null) return false;
            var cut = answer.IndexOf('\n');
            if (cut < 0) return false;
            mark = answer.Substring(0, cut); raw = answer.Substring(cut + 1);
            return true;
        }
    }
    // How soon a read that gave nothing yet is asked again: a few short waits (the page reads its data a moment after it
    // opens), then the ordinary minute until a reading comes or the page changes.
    internal sealed class ReadRetry
    {
        internal const int ShortTries = 3;
        internal const long ShortMs = 10000, FullMs = 60000;
        private int used;
        internal int Used { get { return used; } }
        internal void Reset() { used = 0; }
        // The wait before the next ask, after a read that gave nothing.
        internal long NextDelayMs() { if (used < ShortTries) { used++; return ShortMs; } return FullMs; }
    }
}
