using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Univer.Desktop.Core
{
    internal enum UpdatePhase { Idle, Checking, Downloading, Verifying }
    internal enum UpdateFailure { None, Check, Download, Verify, Signature, Install }
    // What is known about updates and the work on them. The UI thread owns it; Changed is raised there.
    internal sealed class UpdateService : IDisposable
    {
        private readonly UpdateClient client;
        private readonly string folder, application, flavor, architecture;
        private CancellationTokenSource work;
        private long attemptedAt;
        private bool disposed;
        public string Current { get; private set; }
        public bool Installed { get; private set; }
        public string Available { get; private set; }
        public string Skipped { get; private set; }
        public long CheckedAt { get; private set; }
        public UpdatePhase Phase { get; private set; }
        public int Percent { get; private set; }
        public UpdateFailure Failure { get; private set; }
        // The window at entry has been answered in this run.
        public bool Answered { get; private set; }
        public event Action Changed;
        public UpdateService(UpdateClient client, string folder, string application, string current, string flavor, string architecture, bool installed, string available, string skipped, long checkedAt)
        {
            this.client = client; this.folder = folder; this.application = application; this.flavor = flavor; this.architecture = architecture;
            Current = current; Installed = installed; Skipped = skipped; CheckedAt = checkedAt;
            Available = Updates.Newer(available, current) ? available : null;
        }
        public bool ShouldPrompt { get { return Phase == UpdatePhase.Idle && Updates.Prompt(Available, Skipped, Answered); } }
        // The installer has finished with its file by the next start; one still running keeps it until the start after that.
        public void CleanUp()
        {
            try
            {
                if (!Directory.Exists(folder) || !ProfileCleanup.IsPlainPath(folder)) return;
                foreach (var file in Directory.GetFiles(folder)) try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // The automatic check. It does not depend on the student's choices: they only decide what is shown.
        public Task CheckIfDueAsync(long now)
        {
            return Updates.Due(now, CheckedAt) && Math.Abs(now - attemptedAt) >= Updates.RetryInterval ? CheckAsync() : Task.FromResult(true);
        }
        public async Task CheckAsync()
        {
            if (!Begin(UpdatePhase.Checking)) return;
            attemptedAt = DateTime.UtcNow.Ticks;
            var token = work.Token;
            try
            {
                var latest = await client.LatestVersionAsync(token);
                Available = Updates.Newer(latest, Current) ? latest : null;
                CheckedAt = DateTime.UtcNow.Ticks;
                Finish(UpdateFailure.None);
            }
            catch (Exception) when (token.IsCancellationRequested) { Finish(UpdateFailure.None); }
            catch (Exception) { Finish(UpdateFailure.Check); }
        }
        // "Later": the window returns at the next start.
        public void Later() { Answered = true; Raise(); }
        // "Do not show again": this version stays in Settings, and a newer one is offered again.
        public void Skip(string version) { Skipped = version; Answered = true; Raise(); }
        public void Report(UpdateFailure failure) { Finish(failure); }
        public void Cancel() { work?.Cancel(); }
        // The path of a verified installer, or null after a failure or Cancel.
        public async Task<string> DownloadAsync()
        {
            var version = Available;
            if (version == null || !Installed || !Begin(UpdatePhase.Downloading)) return null;
            Answered = true; Raise();
            var token = work.Token;
            var setup = Path.Combine(folder, Updates.SetupName(version, flavor, architecture));
            var part = setup + ".part";
            try
            {
                Directory.CreateDirectory(folder);
                var expected = await client.ChecksumAsync(Updates.ChecksumUrl(version, flavor, architecture), token);
                var progress = new Progress<int>(percent => { if (Phase == UpdatePhase.Downloading) { Percent = percent; Raise(); } });
                var url = Updates.SetupUrl(version, flavor, architecture);
                var actual = await Task.Run(async () => { using (var output = File.Create(part)) return await client.DownloadAsync(url, output, progress, token); });
                if (actual != expected) { Delete(part); Finish(UpdateFailure.Verify); return null; }
                Phase = UpdatePhase.Verifying; Raise();
                Delete(setup); File.Move(part, setup);
                if (!await Task.Run(() => Publisher.SameApp(application, setup))) { Delete(setup); Finish(UpdateFailure.Signature); return null; }
                if (token.IsCancellationRequested) { Delete(setup); Finish(UpdateFailure.None); return null; }
                Finish(UpdateFailure.None);
                return setup;
            }
            catch (Exception) when (token.IsCancellationRequested) { Delete(part); Finish(UpdateFailure.None); return null; }
            catch (Exception) { Delete(part); Finish(UpdateFailure.Download); return null; }
        }
        private bool Begin(UpdatePhase phase)
        {
            if (disposed || Phase != UpdatePhase.Idle) return false;
            work?.Dispose(); work = new CancellationTokenSource();
            Phase = phase; Percent = 0; Failure = UpdateFailure.None; Raise();
            return true;
        }
        private void Finish(UpdateFailure failure) { Phase = UpdatePhase.Idle; Percent = 0; Failure = failure; Raise(); }
        private void Raise() { if (!disposed) Changed?.Invoke(); }
        private static void Delete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        public void Dispose() { disposed = true; work?.Cancel(); client.Dispose(); }
    }
}
