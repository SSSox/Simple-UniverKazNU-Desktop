using System;

namespace Univer.Desktop.Core
{
    internal enum DownloadState { Running, Completed, Failed, Canceled }
    // One file in the downloads list: its name, local path, progress and the course it came from.
    internal sealed class DownloadRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = "";
        public string Path { get; set; } = "";
        public DownloadState State { get; set; }
        public long Received { get; set; }
        public long Total { get; set; } = -1;
        public DateTime Started { get; set; } = DateTime.Now;
        public DateTime? Finished { get; set; }
        public string Error { get; set; } = "";
        public string Source { get; set; } = "";
        // A hash of the address the file came from, so a repeated download can be recognised without storing the address.
        public string Key { get; set; } = "";
        public DownloadRecord Clone() { return (DownloadRecord)MemberwiseClone(); }
    }
    // The status key shown for a failed download: known causes keep their own message, anything else is a plain failure.
    internal static class DownloadOutcome
    {
        public static string Key(string message)
        {
            if (message == "downloadLogin" || message == "offOrigin" || message == "downloadTooLarge") return message;
            return "downloadFailed";
        }
    }
}
