using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class UpdatesTests
    {
        private const string Repository = Updates.Repository;
        private static readonly string SetupAddress = Updates.SetupUrl("9.9.9", "modern", "x64");

        private sealed class Transport : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> answer;
            public readonly List<string> Asked = new List<string>();
            public Transport(Func<HttpRequestMessage, HttpResponseMessage> answer) { this.answer = answer; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            { Asked.Add(request.RequestUri.AbsoluteUri); return Task.FromResult(answer(request)); }
        }
        private static HttpResponseMessage Redirect(string location)
        { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute); return response; }
        private static HttpResponseMessage Body(byte[] bytes) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; }
        private static string Hex(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static UpdateService Service(Transport transport, string folder, string current = "0.14.0", string available = null, bool installed = true)
        {
            var application = Path.Combine(folder, "application.txt");
            Directory.CreateDirectory(folder); File.WriteAllText(application, "Unsigned.");
            return new UpdateService(new UpdateClient(transport), Path.Combine(folder, "updates"), application, current, "Modern", "x64", installed, available, null, 0);
        }
        private static Transport Release(byte[] setup, string checksum)
        {
            return new Transport(request =>
            {
                var url = request.RequestUri.AbsoluteUri;
                if (url == Updates.Latest) return Redirect(Repository + "/releases/tag/v9.9.9");
                if (url.EndsWith(".sha256", StringComparison.Ordinal)) return Body(Encoding.UTF8.GetBytes(checksum + "  file\n"));
                return url == SetupAddress ? Redirect("https://release-assets.githubusercontent.com/file") : Body(setup);
            });
        }
        private static string Folder() { return Path.Combine(Path.GetTempPath(), "Univer.Desktop.Tests", Guid.NewGuid().ToString("N")); }

        [Fact]
        public void OnlyAHigherVersionIsNewer()
        {
            Assert.True(Updates.Newer("0.14.1", "0.14"));
            Assert.True(Updates.Newer("0.10", "0.9"));
            Assert.True(Updates.Newer("1", "0.99.99"));
            Assert.False(Updates.Newer("0.14", "0.14.0"));
            Assert.False(Updates.Newer("0.13.9", "0.14.0"));
            Assert.False(Updates.Newer("0.14.0", "0.14.0"));
            Assert.False(Updates.Newer("beta", "0.14.0"));
            Assert.False(Updates.Newer("0.15", "unknown"));
            Assert.False(Updates.Newer(null, "0.14.0"));
        }
        [Fact]
        public void TheVersionComesOnlyFromThisRepositorysReleaseTag()
        {
            Assert.Equal("0.14.0", Updates.Version(Repository + "/releases/tag/v0.14.0"));
            Assert.Null(Updates.Version(Repository + "/releases"));
            Assert.Null(Updates.Version("https://github.com/someone/else/releases/tag/v9.9"));
            Assert.Null(Updates.Version(Repository + "/releases/tag/v0.14-beta"));
            Assert.Null(Updates.Version(null));
        }
        [Fact]
        public void ReleaseFilesAreOnlyAcceptedFromGitHubOverHttps()
        {
            Assert.True(Updates.AllowedHost(SetupAddress));
            Assert.True(Updates.AllowedHost("https://release-assets.githubusercontent.com/file?sig=1"));
            Assert.False(Updates.AllowedHost("http://github.com/x"));
            Assert.False(Updates.AllowedHost("https://github.com.evil.test/x"));
            Assert.False(Updates.AllowedHost("https://evilgithubusercontent.com/x"));
            Assert.False(Updates.AllowedHost("https://user@github.com/x"));
            Assert.False(Updates.AllowedHost("https://github.com:8443/x"));
            Assert.False(Updates.AllowedHost("not an address"));
        }
        [Fact]
        public void EveryVariantDownloadsItsOwnInstaller()
        {
            Assert.Equal("Univer-Desktop-0.15.0-modern-x64-setup.exe", Updates.SetupName("0.15.0", "Modern", "x64"));
            Assert.Equal(Repository + "/releases/download/v0.15.0/Univer-Desktop-0.15.0-legacy-x86-setup.exe", Updates.SetupUrl("0.15.0", "Legacy", "x86"));
            Assert.EndsWith("-setup.exe.sha256", Updates.ChecksumUrl("0.15.0", "Legacy", "x86"));
        }
        [Fact]
        public void OnlyAnInstalledCopyOfThisVariantInstallsUpdates()
        {
            Assert.True(Updates.Installed("Univer.Desktop.modern.x64\r\n", "Modern", "x64"));
            Assert.False(Updates.Installed("Univer.Desktop.legacy.x64", "Modern", "x64"));
            Assert.False(Updates.Installed("Univer.Desktop.modern.x86", "Modern", "x64"));
            Assert.False(Updates.Installed(null, "Modern", "x64"));
        }
        [Fact]
        public void AChecksumFileGivesItsHash()
        {
            var hash = new string('A', 64);
            Assert.Equal(hash.ToLowerInvariant(), Updates.Checksum(hash + "  Univer-Desktop.exe\n"));
            Assert.Equal(hash.ToLowerInvariant(), Updates.Checksum(hash));
            Assert.Null(Updates.Checksum("not a hash"));
            Assert.Null(Updates.Checksum(""));
        }
        [Fact]
        public void ChecksRunAtMostEverySixHoursAndSurviveAClockChange()
        {
            const long checkedAt = 1000000000000L;
            Assert.True(Updates.Due(checkedAt, 0));
            Assert.False(Updates.Due(checkedAt + Updates.CheckInterval - 1, checkedAt));
            Assert.True(Updates.Due(checkedAt + Updates.CheckInterval, checkedAt));
            Assert.True(Updates.Due(checkedAt - 1, checkedAt));
        }
        [Fact]
        public void TheWindowRespectsLaterAndDoNotShowAgainForThatVersionOnly()
        {
            Assert.True(Updates.Prompt("0.15", null, false));
            Assert.False(Updates.Prompt(null, null, false));
            Assert.False(Updates.Prompt("", null, false));
            Assert.False(Updates.Prompt("0.15", null, true));
            Assert.False(Updates.Prompt("0.15", "0.15", false));
            Assert.True(Updates.Prompt("0.16", "0.15", false));
        }
        [Fact]
        public void TheInstallerGetsTheRunningFolderAndLanguage()
        {
            Assert.Equal("/SILENT /NORESTART /UPDATE=1 /LANG=kazakh /DIR=\"C:\\Users\\Студент\\Univer\"", Updates.SetupArguments("C:\\Users\\Студент\\Univer\\", "kz"));
            Assert.Contains("/LANG=russian", Updates.SetupArguments("C:\\a", "ru"));
            Assert.Contains("/LANG=english", Updates.SetupArguments("C:\\a", "en"));
        }
        [Fact]
        public async Task TheLatestVersionIsReadFromTheRedirect()
        {
            using (var found = new UpdateClient(new Transport(r => Redirect(Repository + "/releases/tag/v0.15.0"))))
                Assert.Equal("0.15.0", await found.LatestVersionAsync(CancellationToken.None));
            using (var none = new UpdateClient(new Transport(r => Redirect(Repository + "/releases"))))
                Assert.Null(await none.LatestVersionAsync(CancellationToken.None));
            using (var broken = new UpdateClient(new Transport(r => new HttpResponseMessage(HttpStatusCode.InternalServerError))))
                await Assert.ThrowsAsync<IOException>(() => broken.LatestVersionAsync(CancellationToken.None));
        }
        [Fact]
        public async Task ADownloadFollowsGitHubAndReturnsTheHashOfTheBytes()
        {
            var body = Encoding.UTF8.GetBytes("setup bytes");
            var transport = Release(body, Hex(body));
            using (var client = new UpdateClient(transport))
            using (var output = new MemoryStream())
            {
                Assert.Equal(Hex(body), await client.DownloadAsync(SetupAddress, output, null, CancellationToken.None));
                Assert.Equal(body, output.ToArray());
                Assert.Equal(2, transport.Asked.Count);
            }
        }
        [Fact]
        public async Task ARedirectAwayFromGitHubIsRefusedBeforeItIsRequested()
        {
            var transport = new Transport(r => Redirect("https://evil.test/setup.exe"));
            using (var client = new UpdateClient(transport))
                await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync(SetupAddress, new MemoryStream(), null, CancellationToken.None));
            Assert.DoesNotContain(transport.Asked, url => url.StartsWith("https://evil.test", StringComparison.Ordinal));
        }
        [Fact]
        public async Task AShortOrOversizedFileIsRefused()
        {
            Func<long, Transport> sized = declared => new Transport(r =>
            {
                var response = Body(new byte[3]); response.Content.Headers.ContentLength = declared;
                return response;
            });
            using (var client = new UpdateClient(sized(Updates.MaxSetupBytes + 1)))
                await Assert.ThrowsAsync<IOException>(() => client.DownloadAsync(SetupAddress, new MemoryStream(), null, CancellationToken.None));
            using (var missing = new UpdateClient(new Transport(r => new HttpResponseMessage(HttpStatusCode.NotFound))))
                await Assert.ThrowsAsync<IOException>(() => missing.DownloadAsync(SetupAddress, new MemoryStream(), null, CancellationToken.None));
        }
        [Fact]
        public async Task ACheckFindsANewerReleaseAndRemembersWhenItRan()
        {
            var folder = Folder();
            using (var service = Service(Release(new byte[1], new string('a', 64)), folder))
            {
                await service.CheckAsync();
                Assert.Equal("9.9.9", service.Available);
                Assert.True(service.CheckedAt > 0);
                Assert.True(service.ShouldPrompt);
                service.Skip("9.9.9");
                Assert.False(service.ShouldPrompt);
                Assert.Equal("9.9.9", service.Available);
            }
        }
        [Fact]
        public async Task AFailedCheckKeepsWhatWasKnownAndWaitsBeforeTryingAgain()
        {
            var folder = Folder();
            var transport = new Transport(r => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using (var service = Service(transport, folder, available: "0.15.0"))
            {
                var now = DateTime.UtcNow.Ticks;
                await service.CheckIfDueAsync(now);
                Assert.Equal(UpdateFailure.Check, service.Failure);
                Assert.Equal("0.15.0", service.Available);
                await service.CheckIfDueAsync(now + TimeSpan.FromMinutes(1).Ticks);
                Assert.Single(transport.Asked);
            }
        }
        [Fact]
        public void AnOfferOlderThanTheRunningVersionIsForgotten()
        {
            using (var service = Service(Release(new byte[1], ""), Folder(), available: "0.13.0")) Assert.Null(service.Available);
        }
        [Fact]
        public async Task ADownloadWithTheRightHashLeavesOneVerifiedInstaller()
        {
            var body = Encoding.UTF8.GetBytes("setup bytes");
            var folder = Folder();
            using (var service = Service(Release(body, Hex(body)), folder, available: "9.9.9"))
            {
                var setup = await service.DownloadAsync();
                Assert.Equal(body, File.ReadAllBytes(setup));
                Assert.Equal(new[] { setup }, Directory.GetFiles(Path.Combine(folder, "updates")));
                Assert.Equal(UpdateFailure.None, service.Failure);
            }
        }
        [Fact]
        public async Task AWrongHashDeletesTheFile()
        {
            var folder = Folder();
            using (var service = Service(Release(new byte[8], new string('a', 64)), folder, available: "9.9.9"))
            {
                Assert.Null(await service.DownloadAsync());
                Assert.Equal(UpdateFailure.Verify, service.Failure);
                Assert.Empty(Directory.GetFiles(Path.Combine(folder, "updates")));
            }
        }
        [Fact]
        public async Task APortableCopyDownloadsNothing()
        {
            var transport = Release(new byte[1], "");
            using (var service = Service(transport, Folder(), available: "9.9.9", installed: false))
            {
                Assert.True(service.ShouldPrompt);
                Assert.Null(await service.DownloadAsync());
                Assert.Empty(transport.Asked);
            }
        }
        [Fact]
        public void AStartClearsWhatTheLastUpdateLeft()
        {
            var folder = Folder();
            using (var service = Service(Release(new byte[1], ""), folder))
            {
                var updates = Directory.CreateDirectory(Path.Combine(folder, "updates")).FullName;
                File.WriteAllText(Path.Combine(updates, "old-setup.exe"), "Old.");
                service.CleanUp();
                Assert.Empty(Directory.GetFiles(updates));
            }
        }
        [Fact]
        public void AnUnsignedAppAcceptsAChecksumButASignedOneNeedsItsOwnPublisher()
        {
            var folder = Folder(); Directory.CreateDirectory(folder);
            var unsigned = Path.Combine(folder, "unsigned.exe"); File.WriteAllText(unsigned, "Unsigned.");
            var signed = Process.GetCurrentProcess().MainModule.FileName;
            Assert.NotNull(Publisher.Subject(signed));
            Assert.True(Publisher.Trusted(signed));
            Assert.Null(Publisher.Subject(unsigned));
            Assert.False(Publisher.Trusted(unsigned));
            Assert.True(Publisher.SameApp(unsigned, unsigned));
            Assert.True(Publisher.SameApp(signed, signed));
            Assert.False(Publisher.SameApp(signed, unsigned));
        }
        [Fact]
        public void PreferencesKeepTheUpdateState()
        {
            var folder = Folder();
            var storage = new Storage(folder);
            var saved = new Preferences { UpdateVersion = "0.15.0", UpdateSkipped = "0.14.5", UpdateChecked = 42 };
            storage.SavePreferences(saved.Clone());
            var loaded = storage.LoadPreferences();
            Assert.Equal("0.15.0", loaded.UpdateVersion);
            Assert.Equal("0.14.5", loaded.UpdateSkipped);
            Assert.Equal(42, loaded.UpdateChecked);
            storage.SavePreferences(new Preferences { UpdateVersion = "../evil", UpdateSkipped = "x", UpdateChecked = -1 });
            loaded = storage.LoadPreferences();
            Assert.Equal("", loaded.UpdateVersion);
            Assert.Equal("", loaded.UpdateSkipped);
            Assert.Equal(0, loaded.UpdateChecked);
        }
    }
}
