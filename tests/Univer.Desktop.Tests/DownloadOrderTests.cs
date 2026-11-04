using System;
using System.Linq;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public sealed class DownloadOrderTests
    {
        private static DownloadRecord File(string name, long total, int day, long received = 0)
        { return new DownloadRecord { FileName = name, Total = total, Received = received, Started = new DateTime(2026, 10, day, 12, 0, 0) }; }
        private static string[] Names(DownloadSort sort, params DownloadRecord[] items)
        { return DownloadOrder.Apply(items, sort).Select(x => x.FileName).ToArray(); }
        [Fact]
        public void NewestFirstIsTheDefaultOrder()
        { Assert.Equal(new[] { "c.pdf", "b.pdf", "a.pdf" }, Names(DownloadSort.Newest, File("a.pdf", 1, 1), File("c.pdf", 1, 3), File("b.pdf", 1, 2))); }
        [Fact]
        public void NameOrderComparesNumbersByValueAndIgnoresCase()
        {
            Assert.Equal(new[] { "Альбом.docx", "Лекция 1.pdf", "Лекция 2.pdf", "лекция 10.pdf" },
                Names(DownloadSort.Name, File("лекция 10.pdf", 1, 1), File("Лекция 2.pdf", 1, 2), File("Альбом.docx", 1, 4), File("Лекция 1.pdf", 1, 5)));
            Assert.Equal(0, DownloadOrder.NaturalComparer.Instance.Compare("Лекция 02", "лекция 2"));
        }
        [Fact]
        public void SizeOrderUsesReceivedBytesWithoutTotalAndKeepsNewestFirstOnTies()
        {
            // A server without Content-Length leaves Total at -1; the bytes already received still count.
            Assert.Equal(new[] { "big.zip", "streamed.bin", "new.pdf", "old.pdf" },
                Names(DownloadSort.Size, File("old.pdf", 10, 1), File("big.zip", 500, 2), File("streamed.bin", -1, 3, 300), File("new.pdf", 10, 4)));
        }
        [Fact]
        public void NaturalComparerHandlesEmptyLeadingDigitsAndNull()
        {
            var compare = DownloadOrder.NaturalComparer.Instance;
            Assert.True(compare.Compare("1 файл", "файл") < 0);
            Assert.True(compare.Compare("файл", "файл 2") < 0);
            Assert.Equal(0, compare.Compare(null, ""));
            Assert.True(compare.Compare("9", "10") < 0);
        }
    }
}
