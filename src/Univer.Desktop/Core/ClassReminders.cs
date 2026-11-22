using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Univer.Desktop.UI;

namespace Univer.Desktop.Core
{
    // A tray reminder before a class, from the app's own copy of this week (ScheduleWeek) and nothing else: no page is
    // read for it. On by default, 15 minutes ahead; the student picks 5, 10, 15 or 30 minutes or turns it off.
    internal static class ClassReminders
    {
        internal static readonly int[] Leads = { 5, 10, 15, 30 };
        internal const int DefaultLead = 15;
        internal static int Lead(int minutes) { return Array.IndexOf(Leads, minutes) >= 0 ? minutes : DefaultLead; }
        // The classes to announce now: those that start next, within the lead time, and were not announced before. Classes
        // that start together (two groups, a stream) come in one reminder. A class already begun is never announced, so
        // a computer that wakes up mid-class stays quiet. A copy of another week gives nothing.
        internal static List<ScheduleClass> Due(ScheduleWeek week, DateTime now, int lead, ICollection<string> announced)
        {
            var due = new List<ScheduleClass>();
            if (week?.Classes == null || now < week.Start || now >= week.End || announced == null) return due;
            var window = TimeSpan.FromMinutes(Lead(lead));
            foreach (var group in week.Classes.Where(c => c.Start > now && c.Start - now <= window).GroupBy(c => c.Start).OrderBy(g => g.Key))
            {
                due.AddRange(group.Where(c => !announced.Contains(Key(c))));
                if (due.Count > 0) break;
            }
            return due;
        }
        internal static string Key(ScheduleClass lesson) { return lesson.Start.ToString("s", CultureInfo.InvariantCulture) + "|" + lesson.Name + "|" + lesson.Room; }
        // The balloon: when it starts, then the class and its room. Behind the app's PIN it does not say which class.
        internal static string Title(IList<ScheduleClass> due, DateTime now, string lang) { return string.Format(Strings.Get("reminderTitle", lang), MinutesLeft(due[0], now)); }
        internal static string Body(IList<ScheduleClass> due, string lang, bool hidden)
        {
            if (hidden) return Strings.Get("reminderHidden", lang);
            var text = string.Join(Environment.NewLine, due.Take(3).Select(c => c.Room.Length > 0 ? c.Name + " · " + string.Format(Strings.Get("reminderRoom", lang), c.Room) : c.Name));
            // Windows takes at most 255 characters for a balloon's text.
            return text.Length <= 255 ? text : text.Substring(0, 254) + "…";
        }
        // The tray icon's tooltip: the app's name, then the next class of the kept week that has not begun, as
        // "Сегодня, 10:00 · Математический анализ". Behind the PIN it says only when, as the balloon names no class there.
        // Windows Forms on .NET Framework refuses a NotifyIcon text longer than 63 characters, so the name is cut to fit.
        internal const int TrayTextLimit = 63;
        internal static string TrayText(ScheduleWeek week, DateTime now, string lang, bool hidden)
        {
            var title = Strings.Get("appTitle", lang);
            var next = Next(week, now);
            if (next == null) return Cut(title, TrayTextLimit);
            var day = next.Start.Date == now.Date ? Strings.Get("trayToday", lang) : next.Start.Date == now.Date.AddDays(1) ? Strings.Get("trayTomorrow", lang) : OfflineWeek.DayName(next.Start, lang);
            var line = day + ", " + next.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + " · ";
            var name = hidden ? Strings.Get("trayClassHidden", lang) : next.Name;
            var room = TrayTextLimit - title.Length - 1 - line.Length;
            return room < 4 ? Cut(title + "\n" + line.TrimEnd(' ', '·'), TrayTextLimit) : title + "\n" + line + Cut(name, room);
        }
        // The first class of the kept week that starts after now; a copy of another week gives none.
        internal static ScheduleClass Next(ScheduleWeek week, DateTime now)
        {
            if (week?.Classes == null || now < week.Start || now >= week.End) return null;
            return week.Classes.Where(c => c.Start > now).OrderBy(c => c.Start).ThenBy(c => c.Name, StringComparer.Ordinal).FirstOrDefault();
        }
        private static string Cut(string text, int limit)
        {
            text = text ?? "";
            if (text.Length <= limit) return text;
            var cut = limit - 1;
            if (char.IsHighSurrogate(text[cut - 1])) cut--;
            return text.Substring(0, cut).TrimEnd() + "…";
        }
        // Whole minutes left, rounded up, so "in 15 min" is said at 14:45:00 and also at 14:45:30.
        internal static int MinutesLeft(ScheduleClass lesson, DateTime now) { return Math.Max(1, (int)Math.Ceiling((lesson.Start - now).TotalMinutes)); }
    }
}
