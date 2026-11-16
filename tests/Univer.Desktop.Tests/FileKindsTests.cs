using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class FileKindsTests
    {
        [Theory]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData(null, 0)]
        [InlineData("лекция", 1)]
        [InlineData("  лекция   пять ", 2)]
        public void A_search_splits_into_its_words(string query, int count) { Assert.Equal(count, FileKinds.Words(query).Length); }
        [Fact]
        public void Words_ignore_case_and_read_yo_as_ye() { Assert.Equal(new[] { "емкость", "qa" }, FileKinds.Words("Ёмкость QA")); }
        [Fact]
        public void Every_word_has_to_match_the_name_or_the_course()
        {
            var item = new DownloadRecord { FileName = "Лекция 5.pdf", Source = "История" };
            Assert.True(FileKinds.Matches(item, FileKinds.Words("история лекция"), null));
            Assert.False(FileKinds.Matches(item, FileKinds.Words("история химия"), null));
            Assert.True(FileKinds.Matches(item, new string[0], FileKind.Pdf));
        }
        [Theory]
        [InlineData(FileKind.Pdf, FileKind.Pdf, true)]
        [InlineData(FileKind.Word, FileKind.Pdf, false)]
        [InlineData(FileKind.Text, FileKind.Other, true)]
        [InlineData(FileKind.Archive, FileKind.Other, true)]
        [InlineData(FileKind.Html, FileKind.Other, true)]
        [InlineData(FileKind.Other, FileKind.Other, true)]
        [InlineData(FileKind.Excel, FileKind.Other, false)]
        public void A_filter_holds_its_kind_and_Other_the_kinds_without_a_chip(object actual, object wanted, bool expected) { Assert.Equal(expected, FileKinds.Is((FileKind)actual, (FileKind)wanted)); }
    }
}
