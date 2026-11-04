using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Univer.Desktop.Core
{
    internal sealed class TextPreview
    {
        public string Text { get; set; }
        public bool Truncated { get; set; }
    }
    internal static class PreviewText
    {
        public const int ByteLimit = 8 * 1024 * 1024;
        public static TextPreview Read(string path, CancellationToken cancellation)
        {
            using (var stream = File.OpenRead(path)) return Read(stream, cancellation);
        }
        public static TextPreview Read(Stream stream, CancellationToken cancellation)
        {
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[65536];
                while (memory.Length < ByteLimit)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, ByteLimit - memory.Length));
                    if (count == 0) break;
                    memory.Write(buffer, 0, count);
                }
                cancellation.ThrowIfCancellationRequested();
                var truncated = memory.Length == ByteLimit && stream.ReadByte() >= 0;
                var bytes = memory.GetBuffer(); var length = (int)memory.Length;
                string text;
                try
                {
                    // Decoder with flush=false retains an incomplete UTF-8 character at the cut boundary.
                    var decoder = new UTF8Encoding(false, true).GetDecoder();
                    var chars = new char[decoder.GetCharCount(bytes, 0, length, !truncated)];
                    decoder = new UTF8Encoding(false, true).GetDecoder();
                    text = new string(chars, 0, decoder.GetChars(bytes, 0, length, chars, 0, !truncated));
                }
                catch (ArgumentException) { text = Encoding.GetEncoding(1251).GetString(bytes, 0, length); }
                return new TextPreview { Text = text.TrimStart('\uFEFF'), Truncated = truncated };
            }
        }
    }
}
