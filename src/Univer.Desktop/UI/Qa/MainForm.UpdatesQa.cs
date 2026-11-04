using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Univer.Desktop.Core;

namespace Univer.Desktop.UI
{
    internal sealed partial class MainForm
    {
        // A synthetic release: nothing reaches GitHub.
        private sealed class UpdatesQaTransport : HttpMessageHandler
        {
            private readonly byte[] setup; private readonly string checksum;
            public UpdatesQaTransport(byte[] setup, string checksum) { this.setup = setup; this.checksum = checksum; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var url = request.RequestUri.AbsoluteUri;
                if (url == Updates.Latest)
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri(Updates.ReleasePage("9.9.9"));
                    return Task.FromResult(redirect);
                }
                var body = url.EndsWith(".sha256", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(checksum + "  setup.exe") : setup;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            }
        }
        private UpdateService UpdatesQaService(string root, byte[] setup, string checksum, bool installed)
        {
            Directory.CreateDirectory(root);
            var application = Path.Combine(root, "application.txt"); File.WriteAllText(application, "Unsigned fixture.");
            return new UpdateService(new UpdateClient(new UpdatesQaTransport(setup, checksum)), Path.Combine(root, "updates"), application, AppInfo.Version, AppInfo.Flavor, AppInfo.Architecture, installed, null, null, 0);
        }
        private async Task WaitQaAsync(Func<bool> done)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!done() && DateTime.UtcNow < until) await Task.Delay(40);
        }
        // Native checks: the window at entry in three languages, Later and Do not show again, the download, a damaged file,
        // portable, and the Settings tab with its buttons.
        private async Task UpdatesQaAsync(string qa, List<string> results)
        {
            var setup = Encoding.UTF8.GetBytes("Synthetic installer.");
            string checksum;
            using (var hash = SHA256.Create()) checksum = BitConverter.ToString(hash.ComputeHash(setup)).Replace("-", "").ToLowerInvariant();
            var root = Path.Combine(storage.Root, "updates-qa");
            using (var service = UpdatesQaService(root, setup, checksum, true))
            {
                await service.CheckAsync();
                Check(service.Available == "9.9.9" && service.ShouldPrompt, "a check reads the newer release from the redirect and offers it", results);
                foreach (var language in new[] { "ru", "kz", "en" })
                    using (var form = new UpdateForm(service, language))
                    {
                        form.Opacity = 0; form.Show(this); await Task.Delay(40);
                        Check(form.InstallForQa.Visible && form.LaterForQa.Visible && form.NeverShowForQa.Visible && form.InstallForQa.Text == Strings.Get("updateInstall", language) && form.LaterForQa.Text == Strings.Get("updateLater", language),
                            "the window at entry has Later, Install and Do not show again in " + language, results);
                        QaSnapshot(Path.Combine(qa, "update-" + language + ".png"), form.Width, form.Height, bitmap => form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)));
                        form.Close();
                    }
                Check(service.Answered && service.Available == "9.9.9" && service.Skipped == null, "closing the window counts as Later and keeps the offer", results);
            }
            using (var service = UpdatesQaService(root, setup, checksum, true))
            {
                await service.CheckAsync();
                using (var form = new UpdateForm(service, "ru"))
                {
                    form.Opacity = 0; form.Show(this); await Task.Delay(40);
                    form.NeverShowForQa.Checked = true; form.LaterForQa.PerformClick();
                    await WaitQaAsync(() => form.IsDisposed || !form.Visible);
                }
                Check(service.Skipped == "9.9.9" && !service.ShouldPrompt && service.Available == "9.9.9", "Do not show again hides this version only and Settings still has it", results);
            }
            using (var service = UpdatesQaService(root, setup, checksum, true))
            {
                await service.CheckAsync();
                string path;
                using (var form = new UpdateForm(service, "ru"))
                {
                    form.Opacity = 0; form.Show(this); await Task.Delay(40);
                    form.InstallForQa.PerformClick();
                    await WaitQaAsync(() => form.IsDisposed || !form.Visible);
                    path = form.SetupPath;
                }
                Check(path != null && File.ReadAllBytes(path).SequenceEqual(setup) && Path.GetFileName(path) == Updates.SetupName("9.9.9", AppInfo.Flavor, AppInfo.Architecture),
                    "Install downloads the installer of this variant and hands it over verified", results);
                service.CleanUp();
                Check(!File.Exists(path), "the next start removes the used installer", results);
            }
            using (var service = UpdatesQaService(root, setup, new string('0', 64), true))
            {
                await service.CheckAsync();
                using (var form = new UpdateForm(service, "ru"))
                {
                    form.Opacity = 0; form.Show(this); await Task.Delay(40);
                    form.InstallForQa.PerformClick();
                    await WaitQaAsync(() => service.Failure != UpdateFailure.None);
                    await Task.Delay(40);
                    Check(!form.IsDisposed && form.Visible && form.SetupPath == null && form.ErrorForQa == Strings.Get("updateVerifyFailed", "ru") && form.InstallForQa.Visible,
                        "a damaged file is deleted, the window explains it and offers Install again", results);
                    form.Dispose();
                }
            }
            using (var service = UpdatesQaService(root, setup, checksum, false))
            {
                await service.CheckAsync();
                var opened = 0;
                using (var form = new UpdateForm(service, "ru"))
                {
                    form.OpenPage = () => opened++;
                    form.Opacity = 0; form.Show(this); await Task.Delay(40);
                    Check(form.InstallForQa.Text == Strings.Get("updateOpen", "ru"), "a portable copy offers the release page instead of Install", results);
                    QaSnapshot(Path.Combine(qa, "update-portable.png"), form.Width, form.Height, bitmap => form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)));
                    form.InstallForQa.PerformClick();
                    await WaitQaAsync(() => form.IsDisposed || !form.Visible);
                }
                Check(opened == 1 && !Directory.GetFiles(Path.Combine(root, "updates")).Any(f => f.Contains("9.9.9")), "the portable button opens the page and downloads nothing", results);
            }
            using (var service = UpdatesQaService(root, setup, checksum, true))
            using (var form = new SettingsForm(null, prefs, false, service))
            {
                form.Opacity = 0; form.Show(this); await Task.Delay(40);
                form.SelectUpdatesForQa(); MotionSurface.Stop(form);
                Check(form.UpdateStatusForQa.StartsWith(string.Format(Strings.Get("updateCurrent", prefs.Language), AppInfo.Version), StringComparison.Ordinal) && form.UpdateCheckForQa.Visible && !form.UpdateInstallForQa.Visible,
                    "Settings shows the version and Check for updates", results);
                form.UpdateCheckForQa.PerformClick();
                await WaitQaAsync(() => service.Phase == UpdatePhase.Idle && service.Available != null);
                await Task.Delay(40);
                Check(form.UpdateStatusForQa.Contains("9.9.9") && form.UpdateInstallForQa.Visible && form.UpdateFitsForQa, "the button checks at once and Install appears, inside the window", results);
                QaSnapshot(Path.Combine(qa, "settings-updates.png"), form.Width, form.Height, bitmap => form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)));
                form.UpdateInstallForQa.PerformClick();
                await WaitQaAsync(() => form.UpdateRequested);
                Check(form.UpdateRequested, "Install in Settings hands over to the update window", results);
            }
            Directory.Delete(root, true);
        }
    }
}
