using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Univer.Desktop.UI;

namespace Univer.Desktop.Core
{
    // The words of the offline screen (UI/OfflineWeekView.cs): the kept week by day, its dates and when it was saved.
    // Day and month names are the app's own, as in overview.js: Windows' Kazakh culture data is not the same on every
    // version, and Russian needs the genitive month after a day.
    internal static class OfflineWeek
    {
        private static readonly string[][] Months =
        {
            new[] { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" },
            new[] { "қаңтар", "ақпан", "наурыз", "сәуір", "мамыр", "маусым", "шілде", "тамыз", "қыркүйек", "қазан", "қараша", "желтоқсан" },
            new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }
        };
        // Monday first.
        private static readonly string[][] Days =
        {
            new[] { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье" },
            new[] { "Дүйсенбі", "Сейсенбі", "Сәрсенбі", "Бейсенбі", "Жұма", "Сенбі", "Жексенбі" },
            new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
        };
        private static int Index(string lang) { return lang == "kz" ? 1 : lang == "en" ? 2 : 0; }
        internal static bool Shows(ScheduleWeek week) { return week?.Classes != null && week.Classes.Count > 0; }
        internal static bool Current(ScheduleWeek week, DateTime now) { return week != null && now >= week.Start && now < week.End; }
        // The days that have classes, in order, each with its classes by time.
        internal static List<KeyValuePair<DateTime, List<ScheduleClass>>> ByDay(ScheduleWeek week)
        {
            if (!Shows(week)) return new List<KeyValuePair<DateTime, List<ScheduleClass>>>();
            return week.Classes.OrderBy(c => c.Start).ThenBy(c => c.End).GroupBy(c => c.Start.Date)
                .Select(g => new KeyValuePair<DateTime, List<ScheduleClass>>(g.Key, g.ToList())).ToList();
        }
        internal static string Date(DateTime day, string lang)
        {
            var month = Months[Index(lang)][day.Month - 1];
            return day.Day.ToString(CultureInfo.InvariantCulture) + " " + month;
        }
        internal static string DayTitle(DateTime day, string lang) { return Days[Index(lang)][((int)day.DayOfWeek + 6) % 7] + ", " + Date(day, lang); }
        // 16–22 November, or 30 November – 6 December across a month.
        internal static string Range(ScheduleWeek week, string lang)
        {
            var first = week.Start.Date; var last = week.End.AddTicks(-1).Date;
            if (first.Month == last.Month) return first.Day.ToString(CultureInfo.InvariantCulture) + "–" + Date(last, lang);
            return Date(first, lang) + " – " + Date(last, lang);
        }
        internal static string Saved(DateTime savedAt, DateTime now, string lang)
        {
            var time = savedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            return savedAt.Date == now.Date ? string.Format(Strings.Get("offlineSavedToday", lang), time) : string.Format(Strings.Get("offlineSavedOn", lang), Date(savedAt, lang), time);
        }
        internal static string Time(ScheduleClass lesson)
        {
            return lesson.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + "–" + lesson.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        // Under the class name: its kind, room and teacher, whichever the page gave.
        internal static string Details(ScheduleClass lesson, string lang)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(lesson.Type)) parts.Add(lesson.Type);
            if (!string.IsNullOrEmpty(lesson.Room)) parts.Add(string.Format(Strings.Get("reminderRoom", lang), lesson.Room));
            if (!string.IsNullOrEmpty(lesson.Teacher)) parts.Add(lesson.Teacher);
            return string.Join(" · ", parts);
        }
    }
}
