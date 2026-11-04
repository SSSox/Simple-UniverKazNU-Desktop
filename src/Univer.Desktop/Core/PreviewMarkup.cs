using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Univer.Desktop.Core
{
    internal sealed class PreviewLimitException : IOException
    { public PreviewLimitException() : base("Preview exceeds size or complexity limits") { } }
    internal sealed class PreviewMarkup
    {
        public const int CharacterLimit = 8 * 1024 * 1024;
        private readonly StringBuilder builder = new StringBuilder();
        private readonly CancellationToken cancellation;
        public PreviewMarkup(CancellationToken cancellation) { this.cancellation = cancellation; }
        public CancellationToken Cancellation { get { return cancellation; } }
        public int Length { get { return builder.Length; } }
        public PreviewMarkup Append(string value)
        {
            cancellation.ThrowIfCancellationRequested();
            if (value != null && value.Length > CharacterLimit - builder.Length) throw new PreviewLimitException();
            builder.Append(value); return this;
        }
        public PreviewMarkup Append(char value) { return Append(value.ToString()); }
        public PreviewMarkup Append(int value) { return Append(value.ToString(CultureInfo.InvariantCulture)); }
        public PreviewMarkup Append(PreviewMarkup value) { return Append(value.ToString()); }
        public override string ToString() { return builder.ToString(); }
    }
}
