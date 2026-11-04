using System;
using System.Collections.Generic;

namespace Univer.Desktop.Core
{
    // WinForms text is drawn by GDI, which applies HKLM FontSubstitutes. A machine can map
    // "Segoe UI" to an unrelated (for example monospace) face; Chromium is not affected.
    internal sealed class FontChoice
    {
        private static readonly string[] Preferred = { "Segoe UI Variable Text", "Segoe UI", "Tahoma", "Arial" };
        public string Regular { get; private set; }
        public string Strong { get; private set; }
        public static FontChoice Pick(ICollection<string> installed, Func<string, bool> substituted)
        {
            var choice = new FontChoice { Regular = "Segoe UI" };
            foreach (var name in Preferred)
                if (installed.Contains(name) && !substituted(name)) { choice.Regular = name; break; }
            // A separate semibold family avoids synthetic bold; null means Regular with FontStyle.Bold.
            var strong = choice.Regular + " Semibold";
            if (choice.Regular == "Segoe UI Variable Text" && installed.Contains(strong) && !substituted(strong)) choice.Strong = strong;
            return choice;
        }
        public static bool IsSubstituted(IEnumerable<string> substituteNames, string family)
        {
            foreach (var name in substituteNames)
                if (string.Equals(name, family, StringComparison.OrdinalIgnoreCase) ||
                    (name.StartsWith(family + ",", StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }
    }
}
