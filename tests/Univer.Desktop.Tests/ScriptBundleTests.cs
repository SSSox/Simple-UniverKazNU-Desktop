using System;
using System.IO;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    // The page scripts compare location.origin against a literal of their own. ScriptBundle rewrites it from
    // SitePolicy.Origin before the bundle is registered, so the two can never drift apart unnoticed.
    public class ScriptBundleTests
    {
        private static readonly string[] Bundle =
        {
            "paint-gate.js", "page-common.js", "univer-automation.js", "news.js", "study-links.js", "study-pages.js",
            "course-tasks.js", "attendance.js", "overview.js", "shortcuts.js", "notices.js", "univer-layout.js", "theme.js", "desktop.js"
        };
        private static string Assets()
        {
            // Tests run from tests/Univer.Desktop.Tests/bin/<config>/net48; walk up to the repository root.
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "src", "Univer.Desktop", "Assets");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("src/Univer.Desktop/Assets");
        }
        [Fact]
        public void OriginLiteralMatchesSitePolicy()
        {
            // A change to the site address must update SitePolicy and the scripts together; this fails first.
            Assert.Equal("'" + SitePolicy.Origin + "'", ScriptBundle.OriginLiteral);
        }
        [Fact]
        public void EveryBundleScriptNamesTheOriginAsALiteralSoItCanBeRewritten()
        {
            foreach (var name in Bundle)
            {
                var script = File.ReadAllText(Path.Combine(Assets(), name));
                Assert.Contains(ScriptBundle.OriginLiteral, script);
                // Rewriting to another address must fully remove the literal; the shipped address is the same one today,
                // so the substitution is checked against a distinct origin.
                var moved = ScriptBundle.WithOrigin(script, "https://moved.example");
                Assert.DoesNotContain(ScriptBundle.OriginLiteral, moved);
                Assert.Contains("'https://moved.example'", moved);
            }
        }
        [Fact]
        public void RewriteKeepsTheScriptWorkingForTheCurrentOrigin()
        {
            var script = "if (location.origin !== " + ScriptBundle.OriginLiteral + ") return;";
            Assert.Equal("if (location.origin !== '" + SitePolicy.Origin + "') return;", ScriptBundle.WithOrigin(script));
            Assert.Equal("if (location.origin !== 'https://moved.example') return;", ScriptBundle.WithOrigin(script, "https://moved.example"));
            // Nothing else is touched, and a script without a literal comes back exactly as it was.
            Assert.Equal("var x = 1;", ScriptBundle.WithOrigin("var x = 1;"));
            Assert.Null(ScriptBundle.WithOrigin(null));
            Assert.Null(ScriptBundle.WithOrigin(null, "https://moved.example"));
            // Repeating the substitution is idempotent: a second pass cannot corrupt an already rewritten script.
            Assert.Equal(ScriptBundle.WithOrigin(script, "https://moved.example"), ScriptBundle.WithOrigin(ScriptBundle.WithOrigin(script, "https://moved.example"), "https://moved.example"));
        }
    }
}
