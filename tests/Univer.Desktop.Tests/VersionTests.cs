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
        [Fact]
        public void ManifestAndPackageCarryTheProjectVersion()
        {
            var root = Root();
            var project = XDocument.Load(Path.Combine(root, "src", "Univer.Desktop", "Univer.Desktop.csproj"));
            var version = project.Descendants("Version").Select(element => element.Value.Trim()).First(value => value.Length != 0);
            Assert.Matches(@"^\d+\.\d+\.\d+$", version);
            XNamespace asm = "urn:schemas-microsoft-com:asm.v1";
            var identity = XDocument.Load(Path.Combine(root, "src", "Univer.Desktop", "app.manifest")).Root.Element(asm + "assemblyIdentity");
            Assert.Equal(version + ".0", (string)identity.Attribute("version"));
            var package = File.ReadAllText(Path.Combine(root, "package.json"));
            Assert.Equal(version, Regex.Match(package, "\"version\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value);
        }
    }
}
