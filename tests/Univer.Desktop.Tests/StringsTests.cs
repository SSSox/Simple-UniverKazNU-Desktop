using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Univer.Desktop.UI;
using Xunit;

namespace Univer.Desktop.Tests
{
    // The app's own RU/KZ/EN texts: every key has three non-empty texts with the same placeholders,
    // English carries no Cyrillic and Kazakh carries no Russian-only words.
    public class StringsTests
    {
        private static readonly Regex Cyrillic = new Regex("[А-Яа-яЁёӘәҒғҚқҢңӨөҰұҮүҺһІі]");
        // The site's own Kazakh names are ОӘК, Moodle ҚОЖ and құпиясөз; «Ауд.» and «через» are Russian.
        private static readonly Regex RussianInKazakh = new Regex(@"УМКД|СДО|[Пп]арол|Ауд\.|[Чч]ерез |құпия сөз");
        // Words Kazakh writes the same way as Russian.
        private static readonly HashSet<string> SameInKazakh = new HashSet<string> { "Логин", "Кабинет", "Аккаунт", "Силлабус.pdf", "Файл", "Семинар", "Транскрипт", "6 цифр" };

        private static Dictionary<string, string[]> All()
        { return (Dictionary<string, string[]>)typeof(Strings).GetField("values", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }

        [Fact]
        public void EveryKeyHasThreeTextsWithTheSamePlaceholders()
        {
            var broken = new List<string>();
            foreach (var pair in All())
            {
                var texts = pair.Value;
                if (texts.Length != 3 || texts.Any(string.IsNullOrWhiteSpace)) { broken.Add(pair.Key + ": not three texts"); continue; }
                var holes = texts.Select(t => string.Join(",", Regex.Matches(t, @"\{\d+\}").Cast<Match>().Select(m => m.Value).OrderBy(v => v))).ToArray();
                if (holes.Distinct().Count() != 1) broken.Add(pair.Key + ": " + string.Join(" | ", holes));
            }
            Assert.Empty(broken);
        }

        [Fact]
        public void EnglishHasNoCyrillic()
        { Assert.Empty(All().Where(p => Cyrillic.IsMatch(p.Value[2])).Select(p => p.Key + ": " + p.Value[2])); }

        [Fact]
        public void KazakhIsNotRussian()
        {
            var found = All().Where(p => RussianInKazakh.IsMatch(p.Value[1]) || p.Value[1] == p.Value[0] && Cyrillic.IsMatch(p.Value[0]) && !SameInKazakh.Contains(p.Value[0]))
                .Select(p => p.Key + ": " + p.Value[1]);
            Assert.Empty(found);
        }

        [Fact]
        public void TheWindowTitleNamesTheUniversityInEachLanguage()
        {
            Assert.Equal("Univer · КазНУ", Strings.Get("appTitle", "ru"));
            Assert.Equal("Univer · ҚазҰУ", Strings.Get("appTitle", "kz"));
            Assert.Equal("Univer · KazNU", Strings.Get("appTitle", "en"));
        }
    }
}
