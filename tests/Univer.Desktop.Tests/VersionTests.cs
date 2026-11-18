using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Univer.Desktop.Tests
{
    // A release bumps the version in three files; the 0.13.0 manifest kept 0.12.1.0 until this test.
    public class VersionTests
    {
        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "src", "Univer.Desktop", "Univer.Desktop.csproj"))) return directory.FullName;
            throw new DirectoryNotFoundException("src/Univer.Desktop/Univer.Desktop.csproj");
        }
        private static string ProjectVersion(string root)
        {
            var project = XDocument.Load(Path.Combine(root, "src", "Univer.Desktop", "Univer.Desktop.csproj"));
            return project.Descendants("Version").Select(element => element.Value.Trim()).First(value => value.Length != 0);
        }
        [Fact]
        public void ManifestAndPackageCarryTheProjectVersion()
        {
            var root = Root();
            var version = ProjectVersion(root);
            Assert.Matches(@"^\d+\.\d+\.\d+$", version);
            XNamespace asm = "urn:schemas-microsoft-com:asm.v1";
            var identity = XDocument.Load(Path.Combine(root, "src", "Univer.Desktop", "app.manifest")).Root.Element(asm + "assemblyIdentity");
            Assert.Equal(version + ".0", (string)identity.Attribute("version"));
            var package = File.ReadAllText(Path.Combine(root, "package.json"));
            Assert.Equal(version, Regex.Match(package, "\"version\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value);
        }

        // The browser extension ships with each release; a fourth part is a fix to the extension alone.
        [Fact]
        public void ExtensionCarriesTheProjectVersion()
        {
            var root = Root();
            var version = ProjectVersion(root);
            var manifest = File.ReadAllText(Path.Combine(root, "src", "Univer.Extension", "manifest.json"));
            var extension = Regex.Match(manifest, "\"version\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            Assert.Matches(@"^\d+\.\d+\.\d+(\.\d+)?$", extension);
            Assert.Equal(version, string.Join(".", extension.Split('.').Take(3)));
        }
    }
}
